using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 推恩令(华夏制度树「推恩令」节点)。本文化施行后，帝国内实行世袭的封国国王去世时：
//   · 有多个儿子、封国不止一城：嫡长子承袭封国(都城与原法理头衔不变)，其余城市按重要程度
//     逐一分给诸子，各自立为只用城市名、没有法理头衔的小封国；城多于子时多出的城归嫡长子；
//   · 绝嗣(没有可以继承的儿子)：封国全部城市收归帝国，设为行政区(郡县)，原法理由行政区接管；
//   · 其余情况(只有一个儿子、只有一座城)照常继承，不在这里处理。
// 法理的归属逻辑没有变：原法理头衔仍在嫡长子一支手里，分出去的只是城市。
public static class GraceEdictService
{
    // 推恩令 / 郡国并行(封国绝嗣则除国为郡，分封制下也能设郡) 分别由制度特性
    // grace_edict / commandery_kingdom 提供，任何线的节点都可以声明。

    public static bool IsCommanderyKingdomActive(Empire empire) =>
        empire != null && !empire.isRekt() && !empire.IsArchived() &&
        InstitutionSystem.HasFeature(empire, InstitutionFeatures.CommanderyKingdom);

    // 由 EmpireCraftKingdomBehCheckKing 在封国没有国王时调用；返回 true 表示已按推恩令处理完毕。
    public static bool TryHandleVacancy(Kingdom fief)
    {
        if (fief == null || fief.isRekt() || fief.hasKing()) return false;
        KingdomExtension.KingdomExtraData data = fief.GetOrCreate();
        long lastKingIdentityId = data.last_king_identity_id;
        if (lastKingIdentityId < 0) return false;
        data.last_king_identity_id = -1L;

        Empire empire = fief.GetEmpire();
        if (fief.IsEmpire() || empire == null || empire.isRekt() || empire.IsArchived() ||
            empire.CoreKingdom == null || empire.CoreKingdom == fief) return false;
        bool graceEdict = InstitutionSystem.HasFeature(empire, InstitutionFeatures.GraceEdict);
        bool commanderyKingdom = IsCommanderyKingdomActive(empire);
        if (!graceEdict && !commanderyKingdom) return false;
        Regime regime = fief.GetRegime();
        if (regime == null || regime.GetLeaderSelectMethod() != LeaderSelectMethod.Succession) return false;

        PersonalClanIdentity lastKing = SpecificClanManager.getPerson(lastKingIdentityId);
        if (lastKing == null) return false;
        List<Actor> sons = SpecificClanManager.getChildren(lastKing)
            .Select(item => item.Item2)
            .Where(identity => identity != null && identity.CanHeir())
            .OrderBy(identity => identity.rank)
            .Select(identity => identity._actor)
            .Where(actor => actor != null && !actor.isRekt() && actor.isAlive() && !actor.isKing() &&
                            actor.kingdom?.GetEmpire() == empire)
            .ToList();

        if (sons.Count == 0)
        {
            string fiefName = fief.GetKingdomName();
            Reclaim(empire, fief, lastKing.name, announce: graceEdict);
            if (!graceEdict)
                Record(empire, null, "commandery_kingdom_reclaim_history", lastKing.name, fiefName);
            return true;
        }
        // 只有郡国并行、没有推恩令时，有子嗣就照常继承，不拆分
        if (!graceEdict) return false;

        List<City> otherCities = (fief.cities ?? new List<City>())
            .Where(city => city != null && !city.isRekt() && city != fief.capital)
            .OrderByDescending(city => city.GetCityStrategicValue().Total).ThenBy(city => city.id)
            .ToList();
        if (sons.Count < 2 || otherCities.Count == 0) return false;

        Actor eldest = sons[0];
        var granted = new List<string>();
        foreach ((Actor son, City city) in sons.Skip(1).Zip(otherCities, (son, city) => (son, city)))
        {
            if (CreateCityFief(empire, regime, city, son))
                granted.Add($"{son.getName()}({city.GetCityName()})");
        }
        Crown(fief, eldest);

        string content = string.Format(LM.Get("grace_edict_partition_history"), lastKing.name, eldest.getName(),
            fief.GetKingdomName(), string.Join("、", granted));
        empire.RecordHistory(directContent: content, actorId: eldest.id, kingdomId: fief.id);
        ActionLibrary.showWhisperTip(content);
        return true;
    }

    // 次子们各得一城：立为只用城市名的小封国，不带法理头衔，同样世袭、归属帝国
    private static bool CreateCityFief(Empire empire, Regime parentRegime, City city, Actor son)
    {
        Kingdom cityFief = city.makeOwnKingdom(son);
        if (cityFief == null) return false;
        cityFief.SetRegimeType(parentRegime.type);
        cityFief.LoadRegime();
        Regime regime = cityFief.GetRegime();
        if (regime != null)
        {
            regime.SetLeaderSelectMethod(LeaderSelectMethod.Succession);
            regime.SetAllowSupportCenterArmy(false);
            regime.SetTaxLevel(TaxLevel.None);
        }
        if (cityFief.HasMainTitle()) cityFief.RemoveMainTitle();
        string cityName = city.GetCityName();
        cityFief.data.name = cityName;
        cityFief.SetKingdomCoreName(cityName, cityName);
        cityFief.SetFiedTimestamp(World.world.getCurWorldTime());
        empire.join(cityFief, true, true);
        return true;
    }

    internal static void Crown(Kingdom fief, Actor heir)
    {
        heir.removeFromArmy();
        if (heir.isCityLeader()) heir.city.removeLeader();
        OfficeObject office = fief.GetOffice();
        if (office == null)
        {
            fief.InitialRegime();
            office = fief.GetOffice();
        }
        if (office != null)
        {
            office.meta_object = fief;
            office.SetActor(heir);
        }
        else
        {
            fief.setKing(heir);
        }
    }

    public static bool IsActive(Empire empire) =>
        empire != null && !empire.isRekt() && !empire.IsArchived() && InstitutionSystem.HasFeature(empire, InstitutionFeatures.GraceEdict);

    // 推恩令下一人不得兼领两国：某人刚成为 kingdom 的国王时，如果他还是同一帝国里别的封国的国王，
    // 那个封国从他的宗室里另立一君(子女→兄弟姐妹→同宗族其他成年人)；宗室无人可立则收归郡县。
    // 由 KingdomPatch 的 setKing 补丁调用。帝国核心王国不会被这样处理。
    public static void OnKingCrowned(Kingdom kingdom, Actor king)
    {
        if (kingdom == null || kingdom.isRekt() || king == null || king.isRekt()) return;
        Empire empire = kingdom.GetEmpire();
        if (!IsActive(empire)) return;
        List<Kingdom> otherRealms = World.world.kingdoms.Where(other =>
                other != null && other != kingdom && !other.isRekt() && other.king == king &&
                other.GetEmpire() == empire && other != empire.CoreKingdom)
            .ToList();
        foreach (Kingdom other in otherRealms)
        {
            Actor successor = FindClanSuccessor(empire, king);
            if (successor != null)
            {
                Crown(other, successor);
                Record(empire, other, "grace_edict_double_rule_history", king.getName(), kingdom.GetKingdomName(),
                    other.GetKingdomName(), successor.getName());
            }
            else
            {
                string otherName = other.GetKingdomName();
                Reclaim(empire, other, king.getName(), announce: false);
                Record(empire, null, "grace_edict_double_rule_reclaim_history", king.getName(),
                    kingdom.GetKingdomName(), otherName);
            }
        }
    }

    private static Actor FindClanSuccessor(Empire empire, Actor king)
    {
        bool IsAvailable(Actor actor) => actor != null && actor != king && !actor.isRekt() && actor.isAlive() &&
                                         actor.isAdult() && !actor.isKing() && actor.kingdom?.GetEmpire() == empire;
        PersonalClanIdentity identity = king.GetPersonalIdentity();
        IEnumerable<Actor> Relatives(IEnumerable<(ClanRelation, PersonalClanIdentity)> list) => list
            .Select(item => item.Item2).Where(person => person != null && person.CanHeir())
            .OrderBy(person => person.rank).Select(person => person._actor);
        if (identity != null)
        {
            Actor child = Relatives(SpecificClanManager.getChildren(identity)).FirstOrDefault(IsAvailable);
            if (child != null) return child;
            Actor sibling = Relatives(SpecificClanManager.GetSiblingsWithRelation(identity)).FirstOrDefault(IsAvailable);
            if (sibling != null) return sibling;
        }
        return king.GetSpecificClan()?.AllAliveMembers?.Where(IsAvailable)
            .OrderByDescending(actor => actor.getAge()).FirstOrDefault();
    }

    private static void Record(Empire empire, Kingdom kingdom, string key, params object[] args)
    {
        string content = string.Format(LM.Get(key), args);
        empire.RecordHistory(directContent: content, kingdomId: kingdom?.id ?? empire.CoreKingdom?.id ?? -1L);
        ActionLibrary.showWhisperTip(content);
    }

    // 绝嗣：城市收归核心王国，再一并设为行政区；有法理的由行政区接管原法理
    private static void Reclaim(Empire empire, Kingdom fief, string lastKingName, bool announce = true)
    {
        KingdomTitle title = fief.GetMainTitle();
        string fiefName = fief.GetKingdomName();
        List<City> cities = (fief.cities ?? new List<City>()).Where(city => city != null && !city.isRekt()).ToList();
        foreach (City city in cities) city.joinAnotherKingdom(empire.CoreKingdom);
        List<City> reclaimed = cities.Where(city => city.kingdom == empire.CoreKingdom).ToList();
        if (reclaimed.Count > 0) empire.EstablishAdministrativeDivision(reclaimed, title);
        if (!announce) return;

        string content = string.Format(LM.Get("grace_edict_reclaim_history"), lastKingName, fiefName);
        empire.RecordHistory(directContent: content, kingdomId: empire.CoreKingdom.id);
        ActionLibrary.showWhisperTip(content);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 分封制帝国的征服规则(不限文化)：帝国直辖只守着自己的法理，不吞并别人的国家。
//   · 帝国核心王国攻下某国都城即视为征服：该国称臣加入帝国，此前被帝国占去的、属于该国
//     法理的城市全部归还；在攻下都城之前可以暂时占着对方的城；
//   · 归附的国家基本自由：保留外交权和军队，不交税、不向中央输兵，原王室照旧世袭；
//   · 本文化已施行分封类制度(开启"分封"诉求的制度)后，可以直接把该国君位封给皇子，
//     原国君迁往王畿安置。
//   · 西方封建制同样只能占一个法理：任何封建王国攻下别国都城，占城归还；攻城方在帝国里就并入帝国，
//     否则对方加入攻城方的联盟，原王室保留；
//   · 叛乱者也一样：分封制下攻下叛军都城，叛乱即告平定、对方重新归附("叛过就永不得回归"
//     是律令制的规矩，不适用于分封制)，但原叛君被废，由帝国强制扶立一位本地贵族为君；
//     分封之后优先改封皇子，没有合适的皇子才立本地贵族。
public static class FeudalConquestService
{
    public static bool AppliesTo(Empire empire) =>
        empire?.CoreKingdom?.GetRegime()?.type == RegimeType.ZhouFeudalism;

    // 本文化是否施行过开启"迫使朝贡"诉求的制度(华夏朝贡体系、阿拉伯吉兹亚税、游牧驿站网络)；没有就不能收朝贡国
    public static bool HasTributeInstitution(Empire empire)
    {
        CultureInstitutionState state =
            InstitutionSystem.GetOrCreateCultureState(InstitutionSystem.GetPrimaryCulture(empire));
        if (state == null) return false;
        return state.enacted_node_ids.Select(InstitutionDefinitionRegistry.Get).Any(node =>
            node != null && node.effects_on_complete.Any(effect =>
                effect.type == "enable_claim" && effect.value == "迫使朝贡"));
    }

    // 本文化是否施行过开启"分封"诉求的制度(华夏分封建国、游牧兀鲁思分封等)
    public static bool HasEnfeoffmentInstitution(Empire empire)
    {
        CultureInstitutionState state =
            InstitutionSystem.GetOrCreateCultureState(InstitutionSystem.GetPrimaryCulture(empire));
        if (state == null) return false;
        return state.enacted_node_ids.Select(InstitutionDefinitionRegistry.Get).Any(node =>
            node != null && node.effects_on_complete.Any(effect =>
                effect.type == "enable_claim" && effect.value == "分封"));
    }

    // 由 CityPatch.FinishedCapture 调用。返回 true 表示已按征服规则处理，城市不再易主。
    public static bool TryResolveCapitalConquest(City city, Kingdom captor, Kingdom conquered)
    {
        if (city == null || captor == null || conquered == null || conquered.isRekt()) return false;
        Empire empire = captor.GetEmpire();
        if (empire != null && (empire.isRekt() || empire.IsArchived())) empire = null;
        // 分封制：只管帝国核心王国攻城；西方封建制：任何封建王国攻城都只能占一个法理
        bool zhou = empire != null && empire.CoreKingdom == captor && AppliesTo(empire);
        bool western = captor.GetRegime()?.type == RegimeType.Feudalism;
        if (!zhou && !western) return false;
        if (conquered.IsEmpire() || (empire != null && conquered.GetEmpire() == empire) || conquered.capital != city)
            return false;
        bool rebel = empire != null &&
                     (conquered.HasRebelledAgainst(empire) ||
                      conquered.getWars().Any(war => war != null && !war.hasEnded() && IsRebellionWar(war) &&
                                                     (war._list_attackers.Contains(captor) ||
                                                      war._list_defenders.Contains(captor))));
        // 叛乱平定后可以重新归附：清掉"曾反叛本帝国"的记录，否则 join 会按律令制的规矩拒绝
        if (rebel) conquered.GetOrCreate().rebellion_origin_empire_ids?.Remove(empire.getID());

        // 该国名下的法理：已经被帝国占去的这些城市全部归还
        HashSet<long> realmTitles = conquered.GetRealmTitleIds().ToHashSet();
        KingdomTitle mainTitle = conquered.GetMainTitle();
        if (mainTitle != null) realmTitles.Add(mainTitle.id);
        foreach (City held in captor.cities.ToList())
        {
            KingdomTitle title = held?.GetTitle();
            if (held == null || held.isRekt() || held == captor.capital || title == null ||
                !realmTitles.Contains(title.id)) continue;
            held.joinAnotherKingdom(conquered);
        }

        foreach (War war in conquered.getWars().ToList())
        {
            bool opposing = (war._list_attackers.Contains(captor) && war._list_defenders.Contains(conquered)) ||
                            (war._list_defenders.Contains(captor) && war._list_attackers.Contains(conquered));
            if (opposing && war.isAlive() && !war.hasEnded()) war.lostWar(conquered);
        }

        // 攻城方不在帝国里(西方封建王国)：对方加入攻城方的联盟，原王室保留(帝国和联盟不兼容，先退出原帝国)
        if (empire == null)
        {
            Empire formerEmpire = conquered.GetEmpire();
            if (formerEmpire != null && !formerEmpire.isRekt()) formerEmpire.leave(conquered);
            PersonalUnionService.JoinAlliance(captor, conquered);
            Record(null, conquered, "feudal_conquest_alliance_history", captor.GetKingdomName(),
                conquered.GetKingdomName());
            return true;
        }

        empire.join(conquered, pForce: true);
        if (conquered.GetEmpire() != empire) return true;
        Regime regime = conquered.GetRegime();
        if (regime != null)
        {
            regime.SetAllowDiplomacy(true);
            regime.SetAllowArmy(true);
            regime.SetAllowSupportCenterArmy(false);
            regime.SetTaxLevel(TaxLevel.None);
            regime.SetLeaderSelectMethod(LeaderSelectMethod.Succession);
        }

        string conqueredName = conquered.GetKingdomName();
        Record(empire, conquered, rebel ? "feudal_conquest_rebel_submit_history" : "feudal_conquest_submit_history",
            empire.GetEmpireName(), conqueredName);
        bool princeInstalled = zhou && HasEnfeoffmentInstitution(empire) && TryInstallPrince(empire, conquered);
        if (rebel && !princeInstalled) InstallLocalNoble(empire, conquered);
        return true;
    }

    // 叛君被废，扶立一位本地贵族为君：优先贵族阶层，其次本国其他成年人，按威望取最高者
    private static void InstallLocalNoble(Empire empire, Kingdom conquered)
    {
        Actor formerKing = conquered.king;
        List<Actor> locals = (conquered.units ?? new List<Actor>()).ToList()
            .Where(actor => actor != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() &&
                            !actor.isKing() && actor != formerKing && actor.isUnitFitToRule())
            .ToList();
        Actor noble = locals.Where(actor => actor.GetOrCreate().socialClass == SocialClass.Noble)
                          .OrderByDescending(actor => actor.data?.renown ?? 0).FirstOrDefault()
                      ?? locals.OrderByDescending(actor => actor.data?.renown ?? 0).FirstOrDefault();
        if (noble == null) return;
        GraceEdictService.Crown(conquered, noble);
        if (conquered.king != noble) return;
        Record(empire, conquered, "feudal_conquest_noble_history", empire.GetEmpireName(), conquered.GetKingdomName(),
            formerKing?.getName() ?? "", noble.getName());
    }

    // 分封之后：把归附国的君位封给一位皇子(太子除外)，原国君迁往王畿
    private static bool TryInstallPrince(Empire empire, Kingdom conquered)
    {
        PersonalClanIdentity emperorIdentity = empire.Emperor?.GetPersonalIdentity();
        if (emperorIdentity == null) return false;
        long crownPrinceId = empire.CoreKingdom.GetHeir()?.id ?? -1L;
        Actor prince = SpecificClanManager.getChildren(emperorIdentity)
            .Select(item => item.Item2)
            .Where(identity => identity != null && identity.CanHeir())
            .OrderBy(identity => identity.rank)
            .Select(identity => identity._actor)
            .FirstOrDefault(actor => actor != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() &&
                                     !actor.isKing() && actor.id != crownPrinceId &&
                                     actor.kingdom?.GetEmpire() == empire);
        if (prince == null) return false;

        Actor formerKing = conquered.king;
        GraceEdictService.Crown(conquered, prince);
        if (conquered.king != prince) return false;
        City royalDomain = empire.CoreKingdom.capital;
        if (formerKing != null && !formerKing.isRekt() && formerKing.isAlive() && royalDomain != null)
        {
            formerKing.removeFromArmy();
            if (formerKing.isCityLeader()) formerKing.city.removeLeader();
            formerKing.joinCity(royalDomain);
            if (royalDomain._city_tile != null) formerKing.goTo(royalDomain._city_tile);
        }
        Record(empire, conquered, "feudal_conquest_prince_history", empire.GetEmpireName(), prince.getName(),
            conquered.GetKingdomName(), formerKing?.getName() ?? "");
        return true;
    }

    private static bool IsRebellionWar(War war) => war.GetEmpireWarType() is EmpireWarType.派系叛乱
        or EmpireWarType.地方叛乱 or EmpireWarType.地方独立 or EmpireWarType.民族叛乱 or EmpireWarType.宗教叛乱
        or EmpireWarType.不奉诏;

    private static void Record(Empire empire, Kingdom kingdom, string key, params object[] args)
    {
        string content = string.Format(LM.Get(key), args);
        empire?.RecordHistory(directContent: content, kingdomId: kingdom?.id ?? -1L);
        ActionLibrary.showWhisperTip(content);
    }
}

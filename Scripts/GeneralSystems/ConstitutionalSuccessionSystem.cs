using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 立宪后的皇位属于原皇室：继承人优先，空缺由议会/内阁择贤；只有整个宗室绝嗣才改共和。
// 万世一系(Unbroken)同样把皇位锁在原皇室一系里，守皇位的地方统一看 ThroneBound
public static class ConstitutionalSuccessionSystem
{
    // 万世一系(用户 2026-10-29 "天皇不应该是万世一系的吗""加万世一系")：日本一系的君主国，皇位只在皇室一系里传——
    // 不篡位、不改朝换代、外姓与外家不入继；权臣(摄关)、将军照旧掌实权，只是坐不上皇位(见 MilitaryRegency、Empire 的权臣)；
    // 皇位空缺由公卿在皇族里拥立(TryHandleVacancy)。皇室整个绝了才照常继承，免得皇位一直空着
    public static bool Unbroken(Empire empire)
    {
        if (empire?.data == null || empire.data.empire_specific_clan <= 0 || empire.data.constitutional_economy?.is_republic == true) return false;
        Kingdom core = empire.CoreKingdom;
        if (core?.data == null || core.isRekt() || !RegimeManager.IsMonarchy(core.GetRegime()?.type)) return false;
        return InstitutionSystem.GetCultureLine(CultureService.GetRealmCulture(core)) == "Japan" && DynastyAlive(empire);
    }

    // 皇位只在原皇室一系里传：立宪保护或万世一系
    public static bool ThroneBound(Empire empire) => IsProtected(empire) || Unbroken(empire);

    // 万世一系：同一天下里已有万世一系的天皇在位，别的日本诸侯不另立天皇称帝(皇族一系另立的不挡，如南北朝)；
    // 打下天下的大名在天皇之下当将军(见 MilitaryRegency)
    public static bool BlocksRivalThrone(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.king?.data == null || ModClass.EMPIRE_MANAGER == null) return false;
        if (InstitutionSystem.GetCultureLine(CultureService.GetRealmCulture(kingdom)) != "Japan") return false;
        EmpireCore mine = TianxiaSystem.MandateOf(kingdom) ?? TianxiaSystem.LeadOf(kingdom.capital);
        foreach (Empire empire in ModClass.EMPIRE_MANAGER.ToList())
        {
            if (empire?.data == null || empire.isRekt() || empire.IsArchived() || empire.CoreKingdom == kingdom || !Unbroken(empire)) continue;
            if (SpecificClanManager.SameLineage(kingdom.king.GetSpecificClan(), empire.EmpireSpecificClan)) continue;
            EmpireCore theirs = TianxiaSystem.Lead(EmpireCoreManager.Get(empire)) ?? TianxiaSystem.LeadOf(empire.CoreKingdom?.capital);
            if (mine == null || theirs == null || theirs == mine) return true;
        }
        return false;
    }

    // 皇室在世与否几秒才查一次(要扫全部宗族，继承、即位的判断调得很勤)
    private const float AliveCacheSeconds = 5f;
    private static readonly Dictionary<long, (float at, long clan, bool alive)> AliveCache = new();

    private static bool DynastyAlive(Empire empire)
    {
        float now = UnityEngine.Time.realtimeSinceStartup;
        if (AliveCache.TryGetValue(empire.id, out var cached) && cached.clan == empire.data.empire_specific_clan &&
            now >= cached.at && now - cached.at < AliveCacheSeconds)
            return cached.alive;
        bool alive = HasLivingDynasty(empire);
        AliveCache[empire.id] = (now, empire.data.empire_specific_clan, alive);
        return alive;
    }

    public static bool IsProtected(Empire empire)
    {
        ConstitutionalEconomyState state = empire?.data?.constitutional_economy;
        return state != null && !state.is_republic && empire.data.empire_specific_clan > 0 &&
               RegimeManager.IsMonarchy(empire.CoreKingdom?.GetRegime()?.type) &&
               (state.constitutional_monarchy ||
                state.constitution?.clauses?.form_of_state == ConstitutionFormOfState.ConstitutionalMonarchy ||
                ParliamentSystem.HasParliament(empire));
    }

    public static bool CanInherit(Empire empire, Actor actor) =>
        actor != null && !actor.isRekt() && actor.isAlive() && actor.isUnitFitToRule() &&
        actor.GetPersonalIdentity() is { is_concubine: false } &&
        empire?.EmpireSpecificClan != null &&
        SpecificClanManager.SameLineage(actor.GetSpecificClan(), empire.EmpireSpecificClan);

    private static List<Actor> LivingDynasty(Empire empire)
    {
        SpecificClan royal = empire?.EmpireSpecificClan;
        if (royal == null) return new List<Actor>();
        // 旁支也是皇室；未成年、暂不能执政的宗室仍然算在世，不能因此宣布绝嗣。
        return SpecificClanManager._specificClans
            .Where(clan => clan != null && SpecificClanManager.SameLineage(clan, royal))
            .SelectMany(clan => clan.AllAliveMembers)
            .Where(actor => actor != null && !actor.isRekt() && actor.isAlive())
            .GroupBy(actor => actor.id).Select(group => group.First()).ToList();
    }

    // 皇室在世的虚拟族人(无小人模式下并入人口数据、需要时再落成实体的宗室)
    private static List<PersonalClanIdentity> VirtualDynasty(Empire empire)
    {
        SpecificClan royal = empire?.EmpireSpecificClan;
        if (royal == null) return new List<PersonalClanIdentity>();
        return SpecificClanManager._specificClans
            .Where(clan => clan != null && SpecificClanManager.SameLineage(clan, royal))
            .SelectMany(clan => clan.SnapshotPeople())
            .Where(person => person != null && person.is_alive && person.is_virtual && !person.is_concubine)
            .GroupBy(person => person.id).Select(group => group.First()).ToList();
    }

    public static bool HasLivingDynasty(Empire empire) => LivingDynasty(empire).Count > 0 || VirtualDynasty(empire).Count > 0;

    // 有实体的宗室优先(按政绩、治理、声望)；都没有就从虚拟宗室里挑最年长的成年人落成实体
    public static Actor SelectRoyalHeir(Empire empire)
    {
        Actor real = LivingDynasty(empire)
            .Where(actor => actor != empire.Emperor && CanInherit(empire, actor))
            .OrderByDescending(actor => actor.GetIdentity()?.TotalPerformance ?? 0d)
            .ThenByDescending(actor => actor.stewardship)
            .ThenByDescending(actor => actor.renown)
            .ThenBy(actor => actor.id).FirstOrDefault();
        if (real != null) return real;
        PersonalClanIdentity virtualHeir = VirtualDynasty(empire)
            .Where(person => person.age >= 16)
            .OrderBy(person => person.rank).ThenByDescending(person => person.age).FirstOrDefault();
        Actor realized = virtualHeir?.Realize();
        return CanInherit(empire, realized) ? realized : null;
    }

    public static string SelectionRelation(Empire empire) => LM.Get(!IsProtected(empire) && Unbroken(empire)
        ? "unbroken_succession_court"
        : ParliamentSystem.HasParliament(empire) ? "constitutional_succession_parliament" : "constitutional_succession_cabinet");

    // 返回 true 表示该空位由宪制处理，禁止再回退到官员、选帝侯或普通王国选举。
    public static bool TryHandleVacancy(Kingdom kingdom)
    {
        if (kingdom == null || !kingdom.IsEmpire()) return false;
        Empire empire = kingdom.GetEmpire();
        if (!ThroneBound(empire)) return false;
        Actor emperor = empire.Emperor;
        if (emperor != null && !emperor.isRekt() && emperor.isAlive()) return true;

        Actor hereditary = EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckHeir.ResolveHeir(kingdom, false).actor;
        Actor heir = CanInherit(empire, hereditary) ? hereditary : kingdom.GetHeir();
        bool elected = !CanInherit(empire, heir);
        if (elected)
        {
            kingdom.RemoveHeir();
            heir = SelectRoyalHeir(empire);
        }
        if (heir != null)
        {
            // 宗室本是藩王时迎入中央，不能走普通继承的合并国土/迁移帝国核心分支。
            string body = SelectionRelation(empire);
            if (empire.InstallHeadOfState(heir) && elected)
                EventRecorder.Record(empire, string.Format(LM.Get("constitutional_succession_elected_history"),
                    body, heir.getName(), empire.GetEmpireFullName()), heir);
        }
        else if (!HasLivingDynasty(empire))
            RepublicSystem.ForceRepublicAfterDynastyExtinction(empire);
        else
        {
            // 宗室在世却没有能即位的成年人(都还年幼)：立年纪最长的宗室幼主，国政由议会/内阁代行(相当于摄政)，
            // 不让皇位一直空着
            Actor minor = LivingDynasty(empire)
                .Where(actor => actor.GetPersonalIdentity() is { is_concubine: false })
                .OrderByDescending(actor => actor.getAge()).ThenBy(actor => actor.id).FirstOrDefault();
            if (minor != null && empire.InstallHeadOfState(minor))
                EventRecorder.Record(empire, string.Format(LM.Get(IsProtected(empire)
                        ? "constitutional_minor_succession_history" : "unbroken_minor_succession_history"),
                    SelectionRelation(empire), minor.getName(), empire.GetEmpireFullName()), minor);
        }
        return true;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.AI.CityAI;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 共和改制。制度"废除君主制"(特性 abolish_monarchy)只是让本文化的帝国可以改制，各帝国各自走：
//   · 军方支持的改制：单一执政党完成本理念的文化支线、取得过半军队支持和足够议席，
//     推动"建立共和"诉求 → 皇帝退位，改为现代共和政体；
//   · 革命：政党在正统崩溃(<30)、其所依靠的阶层怨气高涨时推动"建立共和"；
//     革命战争胜利后废除帝制，共产/法西斯建立一党制；战败后该党被取缔。
//   · 复辟：共和之后保守主义政党完成路线、执政过半并掌握军队，推动"复辟帝制"。
// 改制后国号后缀按执政党理念从词库里取；本文化第一次有帝国以某理念共和，这个理念就成为本文化的
// 主导理念，开始向接壤的异文化传播(见 IdeologySpreadSystem)。
public static class RepublicSystem
{
    public const int CommonEraCalendar = 1;
    public const int RepublicEraCalendar = 2;
    public const string FeatureAbolishMonarchy = "abolish_monarchy";
    private const float SupermajorityShare = 2f / 3f;
    private const int RevolutionMandate = 30;
    private const float RevolutionGrievance = 50f;
    private const float CoreClassAffinity = 40f;
    private const int DraftingStartsAfterYears = 1;
    private const int FirstElectionAfterYears = 3;

    // 以这些理念立国时，宪法即规定一党专政(社会主义、共产主义为统一战线：中间派作为友党保留)
    private static readonly HashSet<PartyIdeology> OnePartyIdeologies =
        new() { PartyIdeology.Socialism, PartyIdeology.Communism, PartyIdeology.Fascism };

    private static ConstitutionalEconomyState State(Empire empire) => empire?.data?.constitutional_economy;

    public static bool IsRepublic(Empire empire) => State(empire)?.is_republic == true;

    // 改制共和的帝国不受文化政体同步(复合帝国区域制度、文化变更、制度同步)的影响，否则刚改制就被拉回君主制
    public static bool IsRegimeLocked(Kingdom kingdom) => IsRepublic(kingdom?.GetEmpire());

    public static bool IsOneParty(Empire empire) => !string.IsNullOrWhiteSpace(State(empire)?.one_party_id);

    // 多党制共和国的权力中心(宪法"权力中心"条款；没有宪法时按总统制)：
    //   · 总统制：大选后执政党领袖出任元首兼政府首脑(占总理之位)，任期固定，议会不能倒阁；任期限制管这个人；
    //   · 议会制：元首是议会选出的虚位元首(执政联盟中总理以外威望最高的人)，每届大选后改选；
    //     总理对议会负责，可被倒阁；任期限制管元首，不管总理；
    //   · 代表大会制(民主集中制)：元首为执政党领袖(见一党制逻辑)
    public static bool IsParliamentaryRepublic(Empire empire) =>
        IsRepublic(empire) && !IsOneParty(empire) && !PartyBanSystem.UsesDemocraticCentralism(empire) &&
        ConstitutionSystem.GetClauses(empire)?.power_center == ConstitutionPowerCenter.Parliamentary;

    public static bool IsPresidentialRepublic(Empire empire) =>
        IsRepublic(empire) && !IsOneParty(empire) && !PartyBanSystem.UsesDemocraticCentralism(empire) &&
        (ConstitutionSystem.GetClauses(empire)?.power_center ?? ConstitutionPowerCenter.Presidential) ==
        ConstitutionPowerCenter.Presidential;

    // 独裁度达到这个值，元首不再经过任何选举，由前任指定或直接接任(如朝鲜)
    public const float DesignatedSuccessionAutocracy = 80f;

    // 元首更替的史书措辞按产生方式：前任指定/接任(独裁度高) / 人民代表大会(民主集中制) / 全民普选 /
    // 议会选举 / 执政党推举(没有议会)。{0} 新任 {1} 前任 {2} 称号 {3} 执政党
    private static string HeadOfStateHistory(Empire empire, Actor head, string previous)
    {
        string key = ConstitutionSystem.TryGetLockedHeadSelection(empire, out ConstitutionHeadSelection selection)
            ? selection switch
            {
                ConstitutionHeadSelection.Designated => "republic_head_of_state_history_designated",
                ConstitutionHeadSelection.PartyNomination => "republic_head_of_state_history_party",
                ConstitutionHeadSelection.CongressVote => "republic_head_of_state_history_congress",
                ConstitutionHeadSelection.PopularVote => "republic_head_of_state_history_suffrage",
                _ => "republic_head_of_state_history_parliament"
            }
            : PartyBanSystem.GetAutocracy(empire) >= DesignatedSuccessionAutocracy
            ? "republic_head_of_state_history_designated"
            : !ParliamentSystem.HasParliament(empire) ? "republic_head_of_state_history_party"
            : PartyBanSystem.UsesDemocraticCentralism(empire) ? "republic_head_of_state_history_congress"
            : PartySystem.HasUniversalSuffrage(empire) ? "republic_head_of_state_history_suffrage"
            : "republic_head_of_state_history_parliament";
        string party = PartySystem.GetGovernmentParty(empire)?.Name;
        if (key == "republic_head_of_state_history_party" && string.IsNullOrWhiteSpace(party))
            key = "republic_head_of_state_history";
        return string.Format(LM.Get(key), head?.getName() ?? LM.Get("label_none"), previous,
            GetHeadOfStateTitle(empire), party ?? "");
    }

    // 共和国元首的称号按执政理念(执政党理念，没有则按建国理念)：主席、总统、执政、元首……
    // 语言文件里可按文化覆盖：head_of_state_<文化>_<理念> → head_of_state_<理念> → head_of_state
    public static string GetHeadOfStateTitle(Empire empire)
    {
        // 复辟帝制期间元首改称皇帝(见 RestorationSystem)
        if (RestorationSystem.IsProclaimed(empire)) return LM.Get("emperor");
        PartyIdeology ideology = IdeologyFamilies.StateIdeology(empire);
        string culture = InstitutionSystem.GetPrimaryCulture(empire);
        foreach (string key in new[] { $"head_of_state_{culture}_{ideology}", $"head_of_state_{ideology}", "head_of_state" })
        {
            string text = LM.Get(key);
            if (!string.IsNullOrWhiteSpace(text) && text != key) return text;
        }
        return "";
    }

    public static bool IsTransitioning(Empire empire) => IsRepublic(empire) &&
        State(empire)?.republic_transition_stage > 0;

    public static bool CanAbolish(Empire empire) =>
        !TechnologySystem.PremodernLocked &&
        empire?.CoreKingdom != null && !IsRepublic(empire) &&
        RegimeManager.IsMonarchy(empire.CoreKingdom.GetRegime()?.type) &&
        InstitutionSystem.GetFeature(empire, FeatureAbolishMonarchy) > 0f;

    #region 废君压力

    private const float AbolitionMajority = 0.5f;

    // 本文化已能废除君主制，君主国却迟迟不废、议会里非保守派占优时，压力逐年加剧(拖得越久涨得越快)：
    //   · 每年加重执政一方所依靠阶层的怨气，压力过半后天命逐年流失——革命战争的条件(怨气 ≥50、天命 <30)越来越容易满足；
    //   · 每年按压力掷一次危机：保守派执政则内阁倒台、提前大选重组内阁；非保守派执政则推动废除君主制
    //     (够三分之二和平退位，否则满足条件时爆发革命战争)。
    // 条件不再成立(已废除、保守派重新占优)时压力逐年消退。
    public static void UpdateAbolitionPressure(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || World.world == null) return;
        FixedFaction leading = LeadingReformParty(empire, out float share);
        bool active = CanAbolish(empire) && PartySystem.IsActive(empire) && leading != null &&
                      share > AbolitionMajority && !HasActiveRevolutionWar(empire);
        if (!active)
        {
            state.abolition_pressure = Mathf.Max(0f, state.abolition_pressure - 10f);
            if (state.abolition_pressure <= 0f) state.abolition_pressure_since = -1d;
            return;
        }
        if (state.abolition_pressure_since < 0d) state.abolition_pressure_since = World.world.getCurWorldTime();
        int years = Date.getYearsSince(state.abolition_pressure_since);
        float before = state.abolition_pressure;
        state.abolition_pressure = Mathf.Min(100f, state.abolition_pressure + 5f + years);
        if (before < 50f && state.abolition_pressure >= 50f)
            Record(empire, string.Format(LM.Get("republic_abolition_pressure_history"), empire.GetEmpireFullName(),
                leading.Name), leading.GetLeader());

        InstitutionEmpireState institutions = empire.data.institution_state;
        if (institutions != null)
        {
            institutions.class_grievances ??= new Dictionary<SocialClass, float>();
            foreach (SocialClass socialClass in Enum.GetValues(typeof(SocialClass)).Cast<SocialClass>()
                         .Where(socialClass => PartySystem.GetAffinity(leading.Ideology, socialClass) >= CoreClassAffinity))
            {
                institutions.class_grievances.TryGetValue(socialClass, out float current);
                institutions.class_grievances[socialClass] = Mathf.Min(100f, current + state.abolition_pressure * 0.05f);
            }
        }
        if (state.abolition_pressure >= 50f) empire.AddMandate(-1);

        if (UnityEngine.Random.value >= state.abolition_pressure / 200f) return;
        FixedFaction governing = PartySystem.GetGovernmentParty(empire);
        if (ParliamentSystem.HasParliament(empire) && governing?.Ideology == PartyIdeology.Conservatism)
        {
            // 刚选过(冷却中)：内阁勉强撑住，今年不倒阁
            if (!ParliamentSystem.CanCallSnapElection(state)) return;
            // 保守派内阁顶不住压力倒台：提前大选，重组内阁
            state.last_parliament_election = -1d;
            state.abolition_pressure = Mathf.Max(0f, state.abolition_pressure - 15f);
            Record(empire, string.Format(LM.Get("republic_abolition_cabinet_fall_history"), empire.GetEmpireFullName(),
                governing.Name), governing.GetLeader());
            return;
        }
        // 先判断能否真正推动(和平改制/退位/革命)，只有真的动起来才记"废君危机"，否则记为僵持并实打实地损失天命
        bool revolutionBefore = HasActiveRevolutionWar(empire);
        if (PushRepublic(empire, leading, () => Record(empire, string.Format(LM.Get("republic_abolition_crisis_history"),
                empire.GetEmpireFullName(), leading.Name), leading.GetLeader())))
        {
            if (IsRepublic(empire) || (!revolutionBefore && HasActiveRevolutionWar(empire))) state.abolition_pressure = 0f;
            return;
        }
        empire.AddMandate(-5);
        Record(empire, string.Format(LM.Get("republic_abolition_crisis_stalled_history"), empire.GetEmpireFullName(),
            leading.Name), leading.GetLeader());
    }

    // 议会(没有议会看中央占比)里的非保守派政党合计占比，以及其中最大的党
    private static FixedFaction LeadingReformParty(Empire empire, out float share)
    {
        share = 0f;
        List<FixedFaction> reformers = PartySystem.GetParties(empire)
            .Where(party => party.Ideology != PartyIdeology.Conservatism).ToList();
        if (reformers.Count == 0) return null;
        ConstitutionalEconomyState state = State(empire);
        int total = state?.parliament_seats?.Count ?? 0;
        Func<FixedFaction, float> weight = ParliamentSystem.HasParliament(empire) && total > 0
            ? party => state.parliament_seats.Count(seat => seat.faction_id == party.GetID()) / (float)total
            : party => party.CentralRatio / 100f;
        share = reformers.Sum(weight);
        return reformers.OrderByDescending(weight).First();
    }

    public static float GetAbolitionPressure(Empire empire) => State(empire)?.abolition_pressure ?? 0f;

    #endregion

    #region 建立共和

    public static bool CanAbdicate(Empire empire, FixedFaction party)
    {
        if (!CanAbolish(empire) || party == null || !party.IsParty || party.Ban ||
            party.Ideology == PartyIdeology.Conservatism || !CanCoup(empire, party)) return false;
        ConstitutionalEconomyState state = State(empire);
        int total = state?.parliament_seats?.Count ?? 0;
        if (ParliamentSystem.HasParliament(empire) && total > 0)
        {
            int abolitionSeats = state.parliament_seats.Count(seat => seat.faction_id == party.GetID());
            return ParliamentSystem.IsGoverningFaction(empire, party.GetID()) &&
                   abolitionSeats >= Mathf.CeilToInt(total * SupermajorityShare);
        }
        // 没有议会：主导朝局、中央占比达到三分之二的政党
        return empire.CoreKingdom.GetRegime()?.GetDominateFaction() == party &&
               party.CentralRatio >= Mathf.CeilToInt(100f * SupermajorityShare);
    }

    // A party cannot substitute a coalition's seats for its own completed program
    // or for actual support among the empire's soldiers.
    public static bool CanCoup(Empire empire, FixedFaction party)
    {
        if (empire?.CoreKingdom == null || party?.IsParty != true || party.Ban) return false;
        string culture = InstitutionSystem.GetPrimaryCulture(empire);
        if (InstitutionSystem.GetFeature(culture,
                IdeologyInstitutionPaths.StageFeature(party.Ideology, 3)) <= 0f) return false;
        return IdeologyPopulationSystem.GetMilitaryShare(empire, party.Ideology) > 0.5f;
    }

    public static bool CanRevolt(Empire empire, FixedFaction party)
    {
        if (!CanAbolish(empire) || party == null || !party.IsParty || party.Ban ||
            party.Ideology == PartyIdeology.Conservatism || party.GetLeader() == null) return false;
        if (HasActiveRevolutionWar(empire)) return false;
        return empire.Legitimacy < RevolutionMandate && GetCoreGrievance(empire, party) >= RevolutionGrievance &&
               (FindRebelKingdom(empire, party) != null || FindSplitLeader(empire, party) != null);
    }

    // Once workers and peasants are a durable majority, universal suffrage gives a
    // non-conservative governing party a constitutional route that does not require an army coup.
    public static bool CanMassPoliticsTransition(Empire empire, FixedFaction party)
    {
        if (!CanAbolish(empire) || party == null || !party.IsParty || party.Ban ||
            party.Ideology == PartyIdeology.Conservatism || !PartySystem.HasUniversalSuffrage(empire)) return false;
        Dictionary<SocialClass, float> shares = InstitutionSystem.BuildClassShares(empire);
        float workingMajority = (shares.TryGetValue(SocialClass.Labour, out float labour) ? labour : 0f) +
                                (shares.TryGetValue(SocialClass.Peasant, out float peasant) ? peasant : 0f);
        if (workingMajority < 0.55f) return false;
        FixedFaction governing = PartySystem.GetGovernmentParty(empire) ??
                                 empire.CoreKingdom.GetRegime()?.GetDominateFaction();
        return governing == party && party.CentralRatio >= 50;
    }

    // 本文化废除君主制之后才成立的君主国不被承认：强制改建现代国家(走与退位相同的建立共和流程)。
    // 废君之前就存在的君主国不在此列，仍按废君压力、退位、革命改制
    // 修宪改变国体(立国理念)：国号后缀、临时政府称呼、中央机构随之按新理念重取
    public static void SetFoundingIdeology(Empire empire, PartyIdeology ideology)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || !state.is_republic || state.republic_ideology == ideology) return;
        state.republic_ideology = ideology;
        empire.CoreKingdom.GetOrCreate().ideology_country_suffix = PartySystem.PickCountrySuffix(empire, ideology);
        EnsureIdeologyBureau(empire);
    }

    public static bool TryModernizeLateMonarchy(Empire empire)
    {
        if (empire?.data == null || IsRepublic(empire) || !CanAbolish(empire)) return false;
        double abolishedAt = InstitutionSystem.GetFeatureEnactedTime(InstitutionSystem.GetPrimaryCulture(empire),
            FeatureAbolishMonarchy);
        if (abolishedAt < 0d || empire.data.timestamp_established_time <= abolishedAt) return false;
        FixedFaction party = PartySystem.GetGovernmentParty(empire) ??
                             empire.CoreKingdom.GetRegime()?.GetDominateFaction();
        if (party != null && (!party.IsParty || party.Ban)) party = null;
        PartyIdeology ideology = party?.Ideology ?? IdeologyFamilies.StateIdeology(empire);
        Establish(empire, party, ideology, party?.GetLeader(), false, "republic_late_monarchy_history");
        return true;
    }

    public static bool TryMassPoliticsTransition(Empire empire)
    {
        if (empire == null || IsRepublic(empire) || IsTransitioning(empire)) return false;
        FixedFaction party = PartySystem.GetGovernmentParty(empire) ??
                             empire.CoreKingdom?.GetRegime()?.GetDominateFaction();
        if (!CanMassPoliticsTransition(empire, party)) return false;
        Establish(empire, party, false, "republic_mass_transition_history");
        return true;
    }

    // 本文化已能废除君主制，帝国正统却跌到革命线以下：各地不再等政党政治成熟，
    // 高概率组成互保同盟另立革命政府，打赢了就逼皇帝退位(见 ResolveRegionalRepublicCoalition)。
    // 不要求开放党禁：没有政党时以帝国里势力最大的非保守理念为纲领。每年由 ConstitutionalEconomySystem 调用。
    public static bool TryMandateCollapseCoalition(Empire empire) =>
        empire != null && empire.Legitimacy < RevolutionMandate && TryRegionalRepublicCoalition(empire, true);

    public static bool TryRegionalRepublicCoalition(Empire empire, bool mandateCollapse = false)
    {
        if (!CanAbolish(empire) || IsRepublic(empire) || HasActiveRevolutionWar(empire) ||
            EmpireCraftWorldLawLibrary.empirecraft_law_ban_empire.isEnabled()) return false;
        // 正统崩溃时正在打仗的省份也会倒戈，只排除已经在造反的
        List<Kingdom> members = empire.kingdoms_list.Where(kingdom => kingdom != null && !kingdom.isRekt() &&
            kingdom != empire.CoreKingdom && kingdom.hasKing() && kingdom.HasMainTitle() &&
            (mandateCollapse || !kingdom.getWars().Any()) &&
            !kingdom.IsFactionRebelling() && !kingdom.IsLocalRebelling()).ToList();
        if (members.Count < 2) return false;
        FixedFaction party = PartySystem.GetParties(empire)
            .Where(candidate => candidate.GetLeader() != null && candidate.Ideology != PartyIdeology.Conservatism &&
                IdeologyPopulationSystem.GetEmpireShare(empire, candidate.Ideology) >=
                IdeologyPopulationSystem.PartyFoundingShare)
            .OrderByDescending(candidate => members.Sum(member =>
                IdeologyPopulationSystem.GetKingdomShare(member, candidate.Ideology)))
            .ThenByDescending(candidate => candidate.CentralRatio).FirstOrDefault();
        if (party == null && !mandateCollapse) return false;
        PartyIdeology ideology = party?.Ideology ?? StrongestReformIdeology(empire);
        float chance = mandateCollapse
            ? 0.3f + 0.5f * Mathf.Clamp01((RevolutionMandate - empire.Legitimacy) / (float)RevolutionMandate)
            : 0.03f + (empire.CoreKingdom.hasEnemies() ? 0.06f : 0f) +
              Mathf.Clamp01((60f - empire.Legitimacy) / 60f) * 0.09f;
        if (UnityEngine.Random.value >= chance) return false;

        List<Kingdom> coalition = members
            .Where(member => IdeologyPopulationSystem.GetKingdomShare(member, ideology) >= 0.12f ||
                             party != null && party.AllMembers.Any(actor => actor?.kingdom == member))
            .OrderByDescending(member => IdeologyPopulationSystem.GetKingdomShare(member, ideology))
            .ThenByDescending(member => member.GetNationalPower()).Take(4).ToList();
        // 正统崩溃时理念不够的省份也会跟着离心：按理念、忠诚补足两家
        if (mandateCollapse && coalition.Count < 2)
            coalition.AddRange(members.Except(coalition)
                .OrderByDescending(member => IdeologyPopulationSystem.GetKingdomShare(member, ideology))
                .ThenBy(member => member.capital?.getLoyalty() ?? 0)
                .Take(2 - coalition.Count).ToList());
        if (coalition.Count < 2) return false;
        Kingdom leaderRealm = coalition[0];
        foreach (Kingdom member in coalition) empire.leave(member, pRecalc: false, isLeave: true);
        empire.recalculate();

        Alliance alliance = World.world.alliances.newAlliance(coalition[0], coalition[1]);
        if (alliance == null)
        {
            foreach (Kingdom member in coalition) empire.join(member, pForce: true, pLegitimacyTransfer: true);
            return false;
        }
        ModAllianceService.Mark(alliance);
        alliance.setName(string.Format(LM.Get("regional_republic_alliance_name"),
            leaderRealm.GetKingdomName()));
        foreach (Kingdom member in coalition.Skip(2)) alliance.join(member, true, true);
        foreach (Kingdom member in coalition) member.StartFactionRebelling(party);

        War war = World.world.diplomacy.startWar(leaderRealm, empire.CoreKingdom, WarTypeLibrary.rebellion);
        if (war == null)
        {
            World.world.alliances.dissolveAlliance(alliance);
            foreach (Kingdom member in coalition)
            {
                member.EndFactionRebelling();
                empire.join(member, pForce: true, pLegitimacyTransfer: true);
            }
            return false;
        }
        foreach (Kingdom member in coalition.Skip(1)) war.joinAttackers(member);
        war.SetEmpireWarType(EmpireWarType.派系叛乱);
        war.data.name = string.Format(LM.Get("regional_republic_war_name"), alliance.name);
        WarExtension.WarExtraData snapshot = war.GetOrCreate();
        snapshot.republic_revolution_empire_id = empire.id;
        snapshot.republic_revolution_party_id = party?.GetID() ?? "";
        snapshot.republic_revolution_party_name = party?.Name ?? alliance.name;
        snapshot.republic_revolution_ideology = ideology;
        snapshot.republic_revolution_leader_id = leaderRealm.king?.id ?? party?.GetLeader()?.id ?? -1L;
        snapshot.republic_regional_coalition = true;
        snapshot.republic_regional_forced_abdication = mandateCollapse;
        snapshot.republic_regional_leader_kingdom_id = leaderRealm.id;
        snapshot.republic_regional_alliance_id = alliance.id;
        snapshot.republic_revolution_allied_kingdom_ids = coalition.Skip(1).Select(member => member.id).ToList();
        string text = string.Format(LM.Get(mandateCollapse ? "regional_republic_collapse_started_history"
                : "regional_republic_started_history"), alliance.name,
            empire.GetEmpireFullName(), PartySystem.GetIdeologyName(ideology));
        Record(empire, text, party?.GetLeader() ?? leaderRealm.king);
        return true;
    }

    // 没有政党时的纲领：帝国里人口占比最大的非保守理念
    private static PartyIdeology StrongestReformIdeology(Empire empire) =>
        Enum.GetValues(typeof(PartyIdeology)).Cast<PartyIdeology>()
            .Where(ideology => ideology != PartyIdeology.Conservatism)
            .OrderByDescending(ideology => IdeologyPopulationSystem.GetEmpireShare(empire, ideology))
            .ThenBy(ideology => ideology == PartyIdeology.SocialLiberalism ? 0 : 1)
            .First();

    // 该党所依靠的阶层(理念对其吸引力高的)平均怨气
    private static float GetCoreGrievance(Empire empire, FixedFaction party)
    {
        IReadOnlyDictionary<SocialClass, float> grievances = InstitutionSystem.GetClassGrievances(empire);
        List<float> values = Enum.GetValues(typeof(SocialClass)).Cast<SocialClass>()
            .Where(socialClass => PartySystem.GetAffinity(party.Ideology, socialClass) >= CoreClassAffinity)
            .Select(socialClass => grievances.TryGetValue(socialClass, out float value) ? value : 0f).ToList();
        return values.Count == 0 ? 0f : values.Average();
    }

    // "建立共和"诉求执行：能和平退位就退位，否则走革命
    // 返回是否真的推动了改制(和平改制、退位或革命战争)；beforeAct 在真正动手前调用，用于先记下起因
    public static bool PushRepublic(Empire empire, FixedFaction party, Action beforeAct = null)
    {
        if (CanMassPoliticsTransition(empire, party))
        {
            beforeAct?.Invoke();
            Establish(empire, party, false, "republic_mass_transition_history");
            return true;
        }
        if (CanAbdicate(empire, party))
        {
            beforeAct?.Invoke();
            Establish(empire, party, false, "republic_coup_history");
            return true;
        }
        if (!CanRevolt(empire, party)) return false;
        beforeAct?.Invoke();
        return StartRevolutionWar(empire, party);
    }

    private static Kingdom FindRebelKingdom(Empire empire, FixedFaction party) => empire.kingdoms_list
        .Where(kingdom => kingdom != null && !kingdom.isRekt() && kingdom != empire.CoreKingdom &&
                          kingdom.hasKing() && !kingdom.IsFactionRebelling() &&
                          !kingdom.IsLocalRebelling() && !kingdom.getWars().Any() &&
                          (party.AllMembers.Any(actor => actor?.kingdom == kingdom) ||
                           // 无小人模式：党员实体很少，本藩国该理念支持率达到两成也算有群众基础
                           CityPopulationSystem.AbstractPopulationEnabled &&
                           IdeologyPopulationSystem.GetKingdomShare(kingdom, party.Ideology) >= 0.2f))
        .OrderByDescending(kingdom => IdeologyPopulationSystem.GetKingdomShare(kingdom, party.Ideology))
        .ThenByDescending(kingdom => party.AllMembers.Count(actor => actor?.kingdom == kingdom))
        .ThenByDescending(kingdom => kingdom.countTotalWarriors()).FirstOrDefault();

    private static Actor FindSplitLeader(Empire empire, FixedFaction party) => party.AllMembers
        .Concat(EmpirePopulation.Enumerate(empire.kingdoms_hashset))
        .Where(actor => actor != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() &&
                        actor.hasCity() && actor.city.kingdom != null &&
                        empire.kingdoms_hashset.Contains(actor.city.kingdom) &&
                        actor.city != actor.city.kingdom.capital && !actor.isKing() &&
                        PartySystem.GetAffinity(party.Ideology, actor.GetOrCreate().socialClass) >= CoreClassAffinity)
        .Distinct().OrderByDescending(actor => IdeologyPopulationSystem.GetCityShare(actor.city, party.Ideology))
        .ThenByDescending(actor => actor.GetFaction() == party)
        .ThenByDescending(actor => actor.renown).FirstOrDefault();

    // 现代革命：共和国里民意所向的理念迟迟不能经选举上台，其政党起兵推翻现政府。
    // 条件：已是共和国(不在过渡期)、没有进行中的革命战争、该党有领袖且找得到起兵的地方
    public static bool StartModernRevolution(Empire empire, FixedFaction party)
    {
        if (!IsRepublic(empire) || IsTransitioning(empire) || HasActiveRevolutionWar(empire) || party == null ||
            !party.IsParty || party.Ban || party.GetLeader() == null) return false;
        if (!ModernStability.PassRebellionGate(empire.CoreKingdom)) return false;
        if (FindRebelKingdom(empire, party) == null && FindSplitLeader(empire, party) == null) return false;
        return StartRevolutionWar(empire, party, modern: true);
    }

    private static bool StartRevolutionWar(Empire empire, FixedFaction party, bool modern = false)
    {
        Kingdom rebel = FindRebelKingdom(empire, party);
        City splitSeat = null;
        Kingdom splitOrigin = null;
        if (rebel == null)
        {
            Actor leader = FindSplitLeader(empire, party);
            splitSeat = leader?.city;
            splitOrigin = splitSeat?.kingdom;
            if (splitSeat == null) return false;
            rebel = splitSeat.makeOwnKingdom(leader, pRebellion: true);
            if (rebel == null) return false;
        }
        if (splitSeat == null) rebel.StartFactionRebelling(party);
        else if (!rebel.StartLocalRebelling(EmpireWarType.地方叛乱))
        {
            RebellionStartupService.RollbackCitySplit(splitSeat, splitOrigin, rebel);
            return false;
        }
        War war = World.world.diplomacy.startWar(rebel, empire.CoreKingdom, WarTypeLibrary.rebellion);
        if (war == null)
        {
            if (splitSeat == null) rebel.EndFactionRebelling();
            else
            {
                rebel.EndLocalRebelling();
                RebellionStartupService.RollbackCitySplit(splitSeat, splitOrigin, rebel);
            }
            return false;
        }
        war.SetEmpireWarType(splitSeat == null ? EmpireWarType.派系叛乱 : EmpireWarType.地方叛乱);
        war.data.name = string.Format(LM.Get(modern ? "republic_modern_revolution_war_name"
            : "republic_revolution_war_name"), party.Name);
        WarExtension.WarExtraData snapshot = war.GetOrCreate();
        snapshot.republic_revolution_empire_id = empire.id;
        snapshot.republic_revolution_party_id = party.GetID();
        snapshot.republic_revolution_party_name = party.Name;
        snapshot.republic_revolution_ideology = party.Ideology;
        snapshot.republic_revolution_leader_id = party.GetLeader()?.id ?? -1L;
        snapshot.republic_revolution_split_realm = splitSeat != null;
        snapshot.republic_modern_revolution = modern;
        int alliedRealms = RecruitRevolutionaryRealms(empire, party, war, rebel, snapshot);
        int volunteers = RaiseRevolutionaryMilitia(empire, rebel, party.Ideology, splitSeat != null);
        int defectors = DefectImperialSoldiers(empire, party, war, rebel);
        snapshot.republic_revolution_defected_soldiers = defectors;
        OrganizeRevolutionaryGarrison(rebel.capital, rebel);
        Record(empire, string.Format(LM.Get(modern ? "republic_modern_revolution_started_history"
                : "republic_revolution_started_history"), party.Name,
            rebel.GetKingdomName(), empire.GetEmpireFullName()), party.GetLeader());
        if (alliedRealms > 0)
            Record(empire, string.Format(LM.Get("republic_revolution_allies_history"), alliedRealms),
                party.GetLeader());
        if (defectors > 0 || volunteers > 0)
            Record(empire, string.Format(LM.Get("republic_revolution_forces_history"), defectors, volunteers,
                rebel.GetKingdomName()), party.GetLeader());
        return true;
    }

    private static int RecruitRevolutionaryRealms(Empire empire, FixedFaction party, War war, Kingdom rebel,
        WarExtension.WarExtraData snapshot)
    {
        float grievance = GetCoreGrievance(empire, party) / 100f;
        int joined = 0;
        foreach (Kingdom member in empire.kingdoms_list.ToList())
        {
            if (member == null || member.isRekt() || member == empire.CoreKingdom || member == rebel ||
                member.IsFactionRebelling() || member.IsLocalRebelling() ||
                member.getWars().Any(other => other != war && !other.hasEnded())) continue;
            if (war._list_attackers.Contains(member) || war._list_defenders.Contains(member)) continue;

            int supporters = party.AllMembers.Count(actor => actor != null && actor.kingdom == member);
            float localSupport = IdeologyPopulationSystem.GetKingdomShare(member, party.Ideology);
            float chance = Mathf.Clamp(0.12f + 0.10f * Mathf.Min(4, supporters) +
                0.22f * localSupport + 0.32f * grievance +
                0.18f * (RevolutionMandate - empire.Legitimacy) / RevolutionMandate,
                0.12f, 0.85f);
            if (UnityEngine.Random.value < chance)
            {
                war.joinAttackers(member);
                if (war._list_attackers.Contains(member))
                {
                    member.StartFactionRebelling(party);
                    snapshot.republic_revolution_allied_kingdom_ids.Add(member.id);
                    joined++;
                }
            }
            else if (UnityEngine.Random.value < 0.35f)
            {
                war.joinDefenders(member);
            }
        }
        return joined;
    }

    private static int DefectImperialSoldiers(Empire empire, FixedFaction party, War war, Kingdom rebel)
    {
        City capital = rebel.capital;
        if (capital == null || capital.isRekt()) return 0;
        IReadOnlyDictionary<SocialClass, float> grievances = InstitutionSystem.GetClassGrievances(empire);
        string culture = InstitutionSystem.GetPrimaryCulture(empire);
        float programBonus = InstitutionSystem.GetFeature(culture,
            IdeologyInstitutionPaths.StageFeature(party.Ideology, 3)) > 0f
            ? IdeologyInstitutionPaths.GetProfile(party.Ideology).DefectionBonus : 0f;
        int total = 0;
        foreach (Kingdom source in empire.kingdoms_list.ToList())
        {
            if (source?.units == null || source == rebel || source.isRekt() ||
                !war._list_defenders.Contains(source)) continue;
            List<Actor> soldiers = source.units.ToList().Where(actor => actor != null && !actor.isRekt() &&
                actor.isAlive() && actor.isWarrior() && !actor.isKing()).ToList();
            int limit = Mathf.CeilToInt(soldiers.Count * 0.45f);
            List<Actor> defectors = new();
            foreach (Actor soldier in soldiers.OrderByDescending(actor => actor.city == capital))
            {
                if (defectors.Count >= limit) break;
                SocialClass socialClass = soldier.GetOrCreate().socialClass;
                float grievance = grievances.TryGetValue(socialClass, out float value) ? value / 100f : 0f;
                float affinity = Mathf.Max(0f, PartySystem.GetAffinity(party.Ideology, socialClass)) / 100f;
                float chance = Mathf.Clamp(0.02f + 0.22f * affinity + 0.23f * grievance +
                    (IdeologyPopulationSystem.Get(soldier) == party.Ideology ? 0.24f : -0.08f) +
                    (soldier.GetFaction() == party ? 0.18f : 0f) +
                    (soldier.city == capital ? 0.15f : 0f) +
                    programBonus +
                    0.10f * (RevolutionMandate - empire.Legitimacy) / RevolutionMandate, 0.02f, 0.7f);
                if (UnityEngine.Random.value < chance) defectors.Add(soldier);
            }
            total += InstitutionSystem.TransferDefectingSoldiers(source, rebel, defectors);
        }
        return total;
    }

    private static int RaiseRevolutionaryMilitia(Empire empire, Kingdom rebel, PartyIdeology ideology, bool splitRealm)
    {
        City capital = rebel.capital;
        if (capital == null || capital.isRekt() || capital.units == null) return 0;
        float organization = InstitutionSystem.GetFeature(InstitutionSystem.GetPrimaryCulture(empire),
            IdeologyInstitutionPaths.StageFeature(ideology, 3)) > 0f
            ? IdeologyInstitutionPaths.GetProfile(ideology).MilitiaMultiplier : 1f;
        int target = Mathf.Clamp(Mathf.CeilToInt(empire.CoreKingdom.countTotalWarriors() * 0.3f * organization), 8, 52);
        int needed = Mathf.Max(0, target - rebel.countTotalWarriors());
        if (splitRealm) needed = Mathf.Max(8, needed);
        int raised = 0;
        // 无小人模式：从都城背景人口里武装革命军
        if (CityPopulationSystem.AbstractPopulationEnabled)
            raised += CityPopulationSystem.RaiseMilitia(capital, needed, 0.1f);
        foreach (Actor resident in capital.units.ToList())
        {
            if (raised >= needed) break;
            if (resident == null || resident.isRekt() || !resident.isAlive() || resident.isWarrior() ||
                !capital.checkCanMakeWarrior(resident)) continue;
            capital.makeWarrior(resident);
            if (resident.isWarrior()) raised++;
        }
        return raised;
    }

    private static void OrganizeRevolutionaryGarrison(City capital, Kingdom rebel)
    {
        if (capital == null || capital.isRekt() || capital.units == null) return;
        capital.checkArmyExistence();
        if (!capital.hasArmy()) EmpireCraftCityBehCheckArmy.CreateNewArmy(capital);
        Army army = capital.army;
        if (army != null && army._captain?.kingdom == rebel)
        {
            foreach (Actor warrior in capital.units.ToList().Where(actor => actor != null && actor.isAlive() &&
                         actor.isWarrior() && actor.kingdom == rebel && !actor.hasArmy()))
            {
                if (army.units.Count >= army._captain.warfare) break;
                warrior.setArmy(army);
            }
        }
    }

    public static bool IsRevolutionWar(War war) =>
        war != null && war.GetOrCreate().republic_revolution_empire_id > 0;

    public static bool HasActiveRevolutionWar(Empire empire) => empire?.CoreKingdom?.getWars()
        ?.Any(war => war != null && !war.hasEnded() && IsRevolutionWar(war)) == true;

    public static void ResolveRevolutionWar(War war, WarWinner winner)
    {
        if (!IsRevolutionWar(war)) return;
        WarExtension.WarExtraData snapshot = war.GetOrCreate();
        if (snapshot.republic_regional_coalition)
        {
            ResolveRegionalRepublicCoalition(war, winner, snapshot);
            return;
        }
        foreach (long kingdomId in snapshot.republic_revolution_allied_kingdom_ids ?? new List<long>())
        {
            Kingdom ally = World.world.kingdoms?.get(kingdomId);
            if (ally != null && !ally.isRekt()) ally.EndFactionRebelling();
        }
        Empire empire = ModClass.EMPIRE_MANAGER?.get(snapshot.republic_revolution_empire_id);
        if (empire?.CoreKingdom == null || empire.IsArchived()) return;
        FixedFaction party = PartySystem.GetParties(empire)
            .FirstOrDefault(candidate => candidate.GetID() == snapshot.republic_revolution_party_id);
        Actor leader = World.world.units?.get(snapshot.republic_revolution_leader_id);
        if (leader == null || leader.isRekt() || !leader.isAlive()) leader = war.getMainAttacker()?.king;
        Kingdom rebelKingdom = war.getMainAttacker();
        if (snapshot.republic_modern_revolution && IsRepublic(empire))
        {
            if (winner == WarWinner.Attackers && rebelKingdom != null && !rebelKingdom.isRekt())
            {
                if (snapshot.republic_revolution_split_realm)
                    empire.join(rebelKingdom, pForce: true, pLegitimacyTransfer: true);
                ApplyModernRevolution(empire, party, snapshot.republic_revolution_ideology, leader);
                return;
            }
            if (winner == WarWinner.Defenders)
            {
                party?.BanFaction();
                Record(empire, string.Format(LM.Get("republic_revolution_failed_history"),
                    snapshot.republic_revolution_party_name, empire.GetEmpireName()), leader);
            }
            return;
        }
        if (winner == WarWinner.Attackers && rebelKingdom != null && !rebelKingdom.isRekt())
        {
            Actor formerMonarch = empire.Emperor;
            List<Actor> royalFamily = SnapshotImmediateRoyalFamily(formerMonarch);
            float revolutionaryGrievance = party == null ? RevolutionGrievance : GetCoreGrievance(empire, party);
            if (snapshot.republic_revolution_split_realm)
                empire.join(rebelKingdom, pForce: true, pLegitimacyTransfer: true);
            Establish(empire, party, snapshot.republic_revolution_ideology, leader,
                OnePartyIdeologies.Contains(snapshot.republic_revolution_ideology),
                "republic_revolution_history");
            ResolveRevolutionaryRoyalFate(empire, formerMonarch, royalFamily,
                snapshot.republic_revolution_ideology, revolutionaryGrievance, leader);
            return;
        }
        if (winner == WarWinner.Defenders)
        {
            party?.BanFaction();
            Record(empire, string.Format(LM.Get("republic_revolution_failed_history"),
                snapshot.republic_revolution_party_name, empire.GetEmpireName()), leader);
        }
    }

    // 现代革命胜利：革命党组建新政府——改以其理念立国、领袖出任元首；一党专政理念关闭党禁，
    // 其余理念重开选举；旧宪法作废，下次更新时颁布新宪法(第 N 部)
    private static void ApplyModernRevolution(Empire empire, FixedFaction party, PartyIdeology ideology, Actor leader)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null) return;
        string oldName = empire.GetEmpireFullName();
        if (IsOneParty(empire)) PartyBanSystem.Open(empire, "party_ban_reopened_revolution_history");
        state.republic_ideology = ideology;
        empire.CoreKingdom.GetOrCreate().ideology_country_suffix = PartySystem.PickCountrySuffix(empire, ideology);
        if (party != null && !party.Ban && OnePartyIdeologies.Contains(ideology))
            PartyBanSystem.Close(empire, party, null, "party_ban_one_party_history");
        else
            state.last_parliament_election = -1d;
        if (leader != null && !leader.isRekt() && leader.isAlive()) empire.InstallHeadOfState(leader);
        state.constitution = null;
        EnsureIdeologyBureau(empire);
        Record(empire, string.Format(LM.Get("republic_modern_revolution_history"), party?.Name ?? "",
            PartySystem.GetIdeologyName(ideology), oldName, empire.GetEmpireFullName()), leader);
    }

    private static void ResolveRegionalRepublicCoalition(War war, WarWinner winner,
        WarExtension.WarExtraData snapshot)
    {
        Empire origin = ModClass.EMPIRE_MANAGER?.get(snapshot.republic_revolution_empire_id);
        Kingdom leaderRealm = World.world.kingdoms?.get(snapshot.republic_regional_leader_kingdom_id) ??
                              war.getMainAttacker();
        List<Kingdom> coalition = new[] { leaderRealm }.Concat(
                (snapshot.republic_revolution_allied_kingdom_ids ?? new List<long>())
                .Select(id => World.world.kingdoms?.get(id)))
            .Where(kingdom => kingdom != null && !kingdom.isRekt()).Distinct().ToList();
        Alliance alliance = World.world.alliances?.get(snapshot.republic_regional_alliance_id);
        // 正统崩溃时的互保同盟打赢了：不另立国家，逼皇帝退位，各省回归，原帝国改行共和
        if (winner == WarWinner.Attackers && snapshot.republic_regional_forced_abdication && origin != null &&
            !origin.isRekt() && !origin.IsArchived() && origin.CoreKingdom != null && !origin.CoreKingdom.isRekt() &&
            !IsRepublic(origin))
        {
            foreach (Kingdom member in coalition)
            {
                member.EndFactionRebelling();
                origin.join(member, pForce: true, pLegitimacyTransfer: true);
            }
            FixedFaction party = PartySystem.GetParties(origin)
                .FirstOrDefault(candidate => candidate.GetID() == snapshot.republic_revolution_party_id);
            Actor leader = party?.GetLeader() ?? World.world.units?.get(snapshot.republic_revolution_leader_id) ??
                           leaderRealm?.king;
            Establish(origin, party, snapshot.republic_revolution_ideology, leader, false,
                "regional_republic_abdication_history");
        }
        else if (winner == WarWinner.Attackers && leaderRealm != null && leaderRealm.HasMainTitle())
        {
            foreach (Kingdom member in coalition)
            {
                member.EndFactionRebelling();
                member.SetRegimeType(RegimeType.Modern);
                member.LoadRegime();
                member.SystemChange();
            }
            Empire republic = ModClass.EMPIRE_MANAGER?.NewEmpire(leaderRealm, allowCultureRival: true,
                suppressFoundingLog: true);
            if (republic != null)
            {
                foreach (Kingdom member in coalition.Where(member => member != leaderRealm))
                    republic.join(member, pForce: true, pLegitimacyTransfer: true);
                RepublicSystem.SyncWithRegime(republic, foundingIdeology: snapshot.republic_revolution_ideology);
                if (republic.data?.constitutional_economy != null)
                    republic.data.constitutional_economy.revolutionary_government = true;
                EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(leaderRealm);
                string text = string.Format(LM.Get("regional_republic_victory_history"),
                    republic.GetEmpireFullName(), origin?.GetEmpireFullName() ?? LM.Get("label_none"));
                republic.RecordHistory(directContent: text, actorId: leaderRealm.king?.id ?? -1L,
                    kingdomId: leaderRealm.id);
                TranslateHelper.LogEventMessage(text, leaderRealm);
            }
        }
        else if (origin != null && !origin.IsArchived())
        {
            foreach (Kingdom member in coalition)
            {
                member.EndFactionRebelling();
                origin.join(member, pForce: true, pLegitimacyTransfer: true);
            }
            FixedFaction party = PartySystem.GetParties(origin)
                .FirstOrDefault(candidate => candidate.GetID() == snapshot.republic_revolution_party_id);
            party?.BanFaction();
            string text = string.Format(LM.Get("regional_republic_failed_history"),
                snapshot.republic_revolution_party_name, origin.GetEmpireFullName());
            Record(origin, text, World.world.units?.get(snapshot.republic_revolution_leader_id));
        }
        if (alliance != null && alliance.isAlive()) World.world.alliances.dissolveAlliance(alliance);
    }

    private static List<Actor> SnapshotImmediateRoyalFamily(Actor monarch)
    {
        if (monarch == null || monarch.isRekt()) return new List<Actor>();
        IEnumerable<Actor> family = new[] { monarch }
            .Concat(monarch.lover == null ? Enumerable.Empty<Actor>() : new[] { monarch.lover })
            .Concat(monarch.getChildren() ?? Enumerable.Empty<Actor>());
        return family.Where(actor => actor != null && !actor.isRekt() && actor.isAlive())
            .GroupBy(actor => actor.id).Select(group => group.First()).ToList();
    }

    private static void ResolveRevolutionaryRoyalFate(Empire republic, Actor formerMonarch,
        List<Actor> royalFamily, PartyIdeology ideology, float grievance, Actor revolutionaryLeader)
    {
        if (republic?.CoreKingdom == null || formerMonarch == null || formerMonarch.isRekt() ||
            !formerMonarch.isAlive()) return;
        float anger = Mathf.Clamp01((grievance - RevolutionGrievance) / 50f);
        float executeChance = ideology switch
        {
            PartyIdeology.Anarchism => 0.55f,
            PartyIdeology.Communism => 0.52f,
            PartyIdeology.Fascism => 0.45f,
            PartyIdeology.Authoritarianism => 0.35f,
            PartyIdeology.Socialism => 0.28f,
            PartyIdeology.SocialDemocracy => 0.16f,
            PartyIdeology.SocialLiberalism => 0.18f,
            PartyIdeology.ConservativeLiberalism => 0.15f,
            PartyIdeology.Libertarianism => 0.14f,
            _ => 0.10f
        } + anger * 0.20f;
        float familyChance = ideology switch
        {
            PartyIdeology.Communism => 0.24f,
            PartyIdeology.Anarchism => 0.20f,
            PartyIdeology.Fascism => 0.16f,
            PartyIdeology.Authoritarianism => 0.10f,
            PartyIdeology.Socialism => 0.06f,
            _ => 0f
        } + anger * 0.10f;

        if (royalFamily.Count > 1 && UnityEngine.Random.value < familyChance)
        {
            List<Actor> victims = royalFamily.Where(actor => actor != revolutionaryLeader && actor.isAlive())
                .ToList();
            if (victims.Count == 0) return;
            string text = string.Format(LM.Get("republic_royal_family_executed_history"),
                formerMonarch.getName(), victims.Count, republic.GetEmpireFullName(),
                PartySystem.GetIdeologyName(ideology));
            RecordRevolutionaryFate(republic, text, formerMonarch);
            foreach (Actor victim in victims) ExecuteRevolutionaryVictim(victim, text, revolutionaryLeader);
            return;
        }
        if (UnityEngine.Random.value >= executeChance) return;
        string execution = string.Format(LM.Get("republic_monarch_executed_history"),
            formerMonarch.getName(), republic.GetEmpireFullName(), PartySystem.GetIdeologyName(ideology));
        RecordRevolutionaryFate(republic, execution, formerMonarch);
        ExecuteRevolutionaryVictim(formerMonarch, execution, revolutionaryLeader);
    }

    private static void RecordRevolutionaryFate(Empire republic, string history, Actor subject)
    {
        republic.RecordHistory(directContent: history, actorId: subject?.id ?? -1L,
            kingdomId: republic.CoreKingdom?.id ?? -1L);
        TranslateHelper.LogEventMessage(history, republic.CoreKingdom);
    }

    private static void ExecuteRevolutionaryVictim(Actor victim, string history, Actor revolutionaryLeader)
    {
        if (victim == null || victim.isRekt() || !victim.isAlive()) return;
        victim.RecordPersonalHistory(history, "revolution_execution", revolutionaryLeader?.id ?? -1L);
        victim.addTrait("death_mark");
        victim.ChangeDeathRate(1f);
        victim.setHealth(0);
    }

    // 皇室绝嗣是宪制下的必然改制，不受废除君主制科技、党派议席或军方支持门槛限制。
    public static bool ForceRepublicAfterDynastyExtinction(Empire empire)
    {
        if (!ConstitutionalSuccessionSystem.IsProtected(empire) ||
            ConstitutionalSuccessionSystem.HasLivingDynasty(empire) || World.world == null) return false;
        Actor incumbent = empire.Emperor;
        if (incumbent != null && !incumbent.isRekt() && incumbent.isAlive()) return false;
        Actor leader = ParliamentSystem.GetHeadOfGovernment(empire);
        FixedFaction party = leader?.GetFaction();
        if (party?.IsParty != true)
            party = PartySystem.GetParties(empire).OrderByDescending(candidate => candidate.CentralRatio).FirstOrDefault();
        leader ??= party?.GetLeader();
        leader ??= empire.CoreKingdom.units?.Where(actor => actor != null && !actor.isRekt() &&
            actor.isAlive() && actor.isAdult() && actor.isUnitFitToRule())
            .OrderByDescending(actor => actor.GetIdentity()?.TotalPerformance ?? 0d)
            .ThenByDescending(actor => actor.stewardship).ThenByDescending(actor => actor.renown).FirstOrDefault();
        PartyIdeology ideology = party?.Ideology ?? IdeologyFamilies.StateIdeology(empire);
        Establish(empire, party, ideology, leader, false, "constitutional_dynasty_extinct_history");
        ConstitutionalEconomyState state = State(empire);
        state.constitutional_monarchy = false;
        state.constitutional_reform_active = false;
        state.constitutional_reform_stage = 0;
        state.constitutional_reform_progress = 0f;
        ConstitutionSystem.Update(empire, state);
        EnsureHeadOfState(empire);
        return IsRepublic(empire);
    }

    private static void Establish(Empire empire, FixedFaction party, bool oneParty, string historyKey) =>
        Establish(empire, party, party.Ideology, party.GetLeader(), oneParty, historyKey);

    private static void Establish(Empire empire, FixedFaction party, PartyIdeology ideology, Actor leader,
        bool oneParty, string historyKey)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null) return;
        string formerEmperor = empire.Emperor?.getName() ?? LM.Get("label_none");
        state.previous_regime = (int)empire.CoreKingdom.GetRegime().type;
        state.deposed_royal_clan_id = empire.data.empire_specific_clan;
        state.is_republic = true;
        state.republic_ideology = ideology;
        state.republic_since = World.world.getCurWorldTime();
        ChooseCalendar(state);
        state.one_party_id = "";
        BeginTransition(state);

        TransitionRegime(empire, RegimeType.Modern);
        // 现代政体不设地主阶层：土地收归国有，私有土地市场关闭，地主不复存在
        Record(empire, string.Format(LM.Get("republic_land_nationalized_history"), empire.GetEmpireFullName()), leader);
        empire.CoreKingdom.RemoveHeir();
        empire.CoreKingdom.GetOrCreate().is_need_to_choose_heir = false;
        empire.CoreKingdom.GetOrCreate().ideology_country_suffix = PartySystem.PickCountrySuffix(empire, ideology);
        if (oneParty && party != null) ApplyFoundingPartyRule(empire);
        if (leader != null) empire.InstallHeadOfState(leader);
        empire.data.year_name = "";

        EnsureIdeologyBureau(empire);
        IdeologySpreadSystem.SetStateIdeology(InstitutionSystem.GetPrimaryCulture(empire), ideology, empire);
        EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(empire.CoreKingdom);
        Record(empire, string.Format(LM.Get(historyKey), formerEmperor, party?.Name ?? "",
            PartySystem.GetIdeologyName(ideology), empire.GetEmpireFullName()), leader);
        Record(empire, LM.Get("republic_provisional_government_history"), leader);
        RecordCalendarChoice(empire);
        TributaryAbolitionService.OnMonarchyAbolished(empire);
    }

    private static void BeginTransition(ConstitutionalEconomyState state)
    {
        state.republic_transition_stage = 1;
        state.republic_transition_started = World.world.getCurWorldTime();
        state.republic_first_election_pending = true;
        ParliamentSystem.Dissolve(state);
    }

    public static void UpdateTransition(Empire empire)
    {
        if (!IsTransitioning(empire)) return;
        ConstitutionalEconomyState state = State(empire);
        if (state.republic_transition_started < 0)
            state.republic_transition_started = World.world.getCurWorldTime();
        int years = Date.getYearsSince(state.republic_transition_started);
        if (years >= FirstElectionAfterYears)
        {
            state.republic_transition_stage = 0;
            Record(empire, LM.Get("republic_constitution_adopted_history"), empire.Emperor);
        }
        else if (years >= DraftingStartsAfterYears && state.republic_transition_stage == 1)
        {
            state.republic_transition_stage = 2;
            Record(empire, LM.Get("republic_constitution_drafting_history"), empire.Emperor);
        }
    }

    public static void OnFirstRepublicElection(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state?.republic_first_election_pending != true || IsTransitioning(empire) ||
            state.parliament_seats?.Count == 0) return;
        state.republic_first_election_pending = false;
        Record(empire, LM.Get("republic_first_election_history"), ParliamentSystem.GetPrimeMinister(empire));
    }

    #endregion

    #region 复辟帝制

    public static bool CanRestore(Empire empire, FixedFaction party)
    {
        if (!IsRepublic(empire) || party == null || !party.IsParty || party.Ban ||
            party.Ideology != PartyIdeology.Conservatism) return false;
        // 本文化已废除君主制(文化制度里的"废除君主制")：帝制不可能再复辟
        if (InstitutionSystem.GetFeature(InstitutionSystem.GetPrimaryCulture(empire), FeatureAbolishMonarchy) > 0f)
            return false;
        ConstitutionalEconomyState state = State(empire);
        int total = state?.parliament_seats?.Count ?? 0;
        return total > 0 && CanCoup(empire, party) &&
               ParliamentSystem.IsGoverningFaction(empire, party.GetID()) &&
               state.parliament_seats.Count(seat => seat.faction_id == party.GetID()) * 2 > total;
    }

    public static void Restore(Empire empire, FixedFaction party)
    {
        if (!CanRestore(empire, party)) return;
        ConstitutionalEconomyState state = State(empire);
        RegimeType previous = state.previous_regime >= 0 ? (RegimeType)state.previous_regime : RegimeType.Feudalism;
        state.is_republic = false;
        state.republic_calendar_mode = 0;
        state.one_party_id = "";
        state.republic_transition_stage = 0;
        state.republic_first_election_pending = false;
        empire.CoreKingdom.GetOrCreate().ideology_country_suffix = "";
        TransitionRegime(empire, previous);
        empire.data.has_year_name = empire.CoreKingdom.GetRegime()?.HasEraName() == true;
        // 原皇族有能继承的在世者就迎回，否则由执政的保守党领袖登基
        Actor monarch = SpecificClanManager.Get(state.deposed_royal_clan_id)?.all_valid_members
                            .Select(identity => identity.Realize())
                            .FirstOrDefault(actor => actor != null && !actor.isRekt() && actor.isAdult())
                        ?? party.GetLeader();
        if (monarch != null)
        {
            if (empire.Emperor?.id == monarch.id)
            {
                empire.EmperorLeft();
                empire.NewEmperor(monarch, isNew: true);
            }
            else empire.InstallHeadOfState(monarch);
        }
        EnsureIdeologyBureau(empire);
        EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(empire.CoreKingdom);
        Record(empire, string.Format(LM.Get("republic_restoration_history"), party.Name,
            monarch?.getName() ?? LM.Get("label_none"), empire.GetEmpireFullName()), monarch);
    }

    // 强人复辟成功(见 RestorationSystem)：称帝的元首本人登基，共和国改回君主制。
    // 调用前文化里的"废除君主制"已撤销(InstitutionSystem.RevokeAbolishMonarchy)
    public static void RestoreByStrongman(Empire empire, Actor monarch)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || !IsRepublic(empire)) return;
        RegimeType previous = state.previous_regime >= 0 ? (RegimeType)state.previous_regime : RegimeType.Feudalism;
        state.is_republic = false;
        state.republic_calendar_mode = 0;
        state.one_party_id = "";
        state.republic_transition_stage = 0;
        state.republic_first_election_pending = false;
        empire.CoreKingdom.GetOrCreate().ideology_country_suffix = "";
        TransitionRegime(empire, previous);
        empire.data.has_year_name = empire.CoreKingdom.GetRegime()?.HasEraName() == true;
        if (monarch != null && !monarch.isRekt())
        {
            if (empire.Emperor?.id == monarch.id)
            {
                empire.EmperorLeft();
                empire.NewEmperor(monarch, isNew: true);
            }
            else empire.InstallHeadOfState(monarch);
        }
        EnsureIdeologyBureau(empire);
        EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(empire.CoreKingdom);
    }

    #endregion

    #region 元首更替

    // 共和国元首：总统制下每届大选后由执政党领袖(总理)出任；议会制下为议会选出的虚位元首(见 IsParliamentaryRepublic)；
    // 一党制下为执政党领袖，领袖换人即换。afterElection：刚举行完大选(议会制据此改选元首)
    public static void UpdateHeadOfState(Empire empire, bool afterElection = false)
    {
        if (!IsRepublic(empire) || IsTransitioning(empire) ||
            State(empire)?.republic_first_election_pending == true) return;
        if (IsParliamentaryRepublic(empire) && UpdateCeremonialHead(empire, afterElection)) return;
        Actor head = IsOneParty(empire)
            ? PartySystem.GetParties(empire).FirstOrDefault(party => party.GetID() == State(empire).one_party_id)?.GetLeader()
            : ParliamentSystem.GetPrimeMinister(empire);
        head ??= empire.CoreKingdom.GetRegime()?.GetDominateFaction()?.GetLeader();
        if (head != null && (head.isRekt() || !head.isAlive())) head = null;
        // 总理、党魁、主导派系都没有合适人选时元首不能一直空着(没有元首的国家不会宣战、结盟，正统也上不去)：
        // 先找任一政党领袖，再从核心国里挑威望最高、适合执政的成年人代理
        Actor current = empire.Emperor;
        bool vacant = current == null || current.isRekt() || !current.isAlive();
        if (head == null && vacant)
        {
            head = PartySystem.GetParties(empire).Select(party => party.GetLeader())
                       .FirstOrDefault(leader => leader != null && !leader.isRekt() && leader.isAlive())
                   ?? empire.CoreKingdom.units?.Where(actor => actor != null && !actor.isRekt() && actor.isAlive() &&
                                                                actor.isAdult() && actor.isUnitFitToRule())
                       .OrderByDescending(actor => actor.renown).FirstOrDefault();
        }
        if (head == null || head.isRekt() || head.id == empire.Emperor?.id) return;
        string previous = empire.Emperor?.getName() ?? LM.Get("label_none");
        if (!empire.InstallHeadOfState(head)) return;
        Record(empire, HeadOfStateHistory(empire, head, previous), head);
    }

    #endregion

    // 议会制的虚位元首：在任者仍然合格(在世、身在本国、不是总理)且不是刚大选完就留任；
    // 否则由议会从执政联盟里选总理以外威望最高、未做满任期的人。返回是否已处理(没选出人时交回总统制逻辑兜底)
    private static bool UpdateCeremonialHead(Empire empire, bool afterElection)
    {
        ConstitutionalEconomyState state = State(empire);
        Actor primeMinister = ParliamentSystem.GetPrimeMinister(empire);
        Actor current = empire.Emperor;
        bool currentValid = current != null && !current.isRekt() && current.isAlive() && current.isAdult() &&
                            current.kingdom?.GetEmpire() == empire && current.id != primeMinister?.id;
        int limit = ConstitutionSystem.GetClauses(empire)?.max_terms ?? 0;
        state.head_terms ??= new Dictionary<long, int>();
        bool TermLimited(Actor actor) => limit > 0 && state.head_terms.TryGetValue(actor.id, out int served) &&
                                         served >= limit;
        if (currentValid && !afterElection) return true;
        if (currentValid && afterElection && !TermLimited(current))
        {
            // 议会连选连任
            state.head_terms[current.id] = (state.head_terms.TryGetValue(current.id, out int kept) ? kept : 0) + 1;
            return true;
        }
        var coalition = new HashSet<string>(ParliamentSystem.GetCoalitionIds(empire));
        IEnumerable<FixedFaction> parties = PartySystem.GetParties(empire);
        Actor head = parties.Where(party => coalition.Contains(party.GetID()))
                         .Concat(parties.Where(party => !coalition.Contains(party.GetID())))
                         .SelectMany(party => party.AllMembers)
                         .Where(actor => actor != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() &&
                                         actor.kingdom?.GetEmpire() == empire && actor.id != primeMinister?.id &&
                                         !actor.IsWarMachine() && !TermLimited(actor))
                         .OrderByDescending(actor => coalition.Contains(actor.GetFaction()?.GetID() ?? "") ? 1 : 0)
                         .ThenByDescending(actor => actor.renown)
                         .FirstOrDefault();
        if (head == null) return currentValid;
        string previous = current?.getName() ?? LM.Get("label_none");
        if (!empire.InstallHeadOfState(head)) return currentValid;
        state.head_terms[head.id] = (state.head_terms.TryGetValue(head.id, out int served) ? served : 0) + 1;
        Record(empire, string.Format(LM.Get("republic_ceremonial_head_history"), head.getName(), previous,
            GetHeadOfStateTitle(empire), head.GetFaction()?.Name ?? ""), head);
        return true;
    }

    #region 理念政体机构(只作用于本帝国，不随文化同步)

    private sealed class IdeologyBureau
    {
        public List<BureauSetting> cores = new();
        public List<BureauSetting> division = new();
    }

    private static Dictionary<string, IdeologyBureau> _bureaus;

    private static Dictionary<string, IdeologyBureau> Bureaus
    {
        get
        {
            if (_bureaus != null) return _bureaus;
            _bureaus = new Dictionary<string, IdeologyBureau>();
            try
            {
                string path = global::System.IO.Path.Combine(ModClass._declare.FolderPath, "IdeologyBureaus.json");
                if (global::System.IO.File.Exists(path))
                    _bureaus = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, IdeologyBureau>>(
                        global::System.IO.File.ReadAllText(path)) ?? _bureaus;
            }
            catch (Exception exception)
            {
                NeoModLoader.services.LogService.LogError($"读取 IdeologyBureaus.json 失败: {exception}");
            }
            return _bureaus;
        }
    }

    // 共和国核心国的中央机构按执政理念成套；其他情况返回 null(用政体默认机构)
    public static BureauConfig GetBureauOverride(Kingdom kingdom, BureauConfig fallback)
    {
        Empire empire = kingdom?.GetEmpire();
        if (empire == null || empire.CoreKingdom != kingdom || !IsRepublic(empire)) return null;
        if (!Bureaus.TryGetValue(State(empire).republic_ideology.ToString(), out IdeologyBureau bureau) ||
            bureau?.cores == null || bureau.division == null) return null;
        return new BureauConfig
        {
            cores = bureau.cores,
            division = bureau.division,
            harems = new List<BureauSetting>(),
            kingdoms = fallback?.kingdoms,
            cities = fallback?.cities,
            armies = fallback?.armies
        };
    }

    // 执政理念变了(改制共和、切换路线、复辟……)就把中央机构重建一次
    public static void EnsureIdeologyBureau(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || empire.CoreKingdom == null || empire.CoreKingdom.GetRegime()?.bureau_config == null) return;
        string wanted = IsRepublic(empire) ? state.republic_ideology.ToString() : "";
        if ((state.bureau_ideology ?? "") == wanted) return;
        state.bureau_ideology = wanted;
        empire.data.centerOffice ??= new CenterOffice();
        empire.data.centerOffice.Init(empire.CoreKingdom);
    }

    #endregion

    #region 与实际政体同步(玩家可随时在政体窗口切换国家制度)

    // 共和标记跟着实际政体走：切到现代共和政体就算改行共和(召开议会，理念取第一大党的，没有政党取本文化
    // 主导理念，再没有就是中间派)；切回任何君主政体就恢复君主制。君主制期间记下最近的君主政体，复辟时回到它。
    // manual = 玩家在政体窗口手动切换。只有手动切换(或复辟帝制)才算真的恢复君主制；
    // 其他代码路径把共和国的政体改回君主制，一律视为误改，直接改回现代共和政体。
    public static void SyncWithRegime(Empire empire, bool manual = false,
        PartyIdeology? foundingIdeology = null)
    {
        ConstitutionalEconomyState state = State(empire);
        RegimeType? current = empire?.CoreKingdom?.GetRegime()?.type;
        if (state == null || current == null || World.world == null) return;
        if (RegimeManager.IsMonarchy(current))
        {
            if (!state.is_republic)
            {
                state.previous_regime = (int)current.Value;
                return;
            }
            if (!manual)
            {
                // 改回失败(配置问题等)就放弃这次改制，别每次更新都重试刷一遍错误
                try
                {
                    TransitionRegime(empire, RegimeType.Modern);
                    return;
                }
                catch (Exception exception)
                {
                    NeoModLoader.services.LogService.LogError($"共和国政体恢复失败，改为君主制: {exception}");
                }
            }
            state.previous_regime = (int)current.Value;
            state.is_republic = false;
            state.republic_calendar_mode = 0;
            state.one_party_id = "";
            state.republic_transition_stage = 0;
            state.republic_first_election_pending = false;
            empire.CoreKingdom.GetOrCreate().ideology_country_suffix = "";
            empire.data.empire_type_key = "";
            empire.data.has_year_name = empire.CoreKingdom.GetRegime()?.HasEraName() == true;
            Actor monarch = empire.Emperor;
            if (monarch != null && !monarch.isRekt())
            {
                empire.EmperorLeft();
                empire.NewEmperor(monarch, isNew: true);
            }
            Record(empire, string.Format(LM.Get("republic_manual_monarchy_history"), empire.GetEmpireFullName()), null);
            return;
        }
        if (state.is_republic || current != RegimeType.Modern) return;
        FixedFaction largest = PartySystem.GetParties(empire).OrderByDescending(party => party.CentralRatio).FirstOrDefault();
        PartyIdeology ideology = foundingIdeology ?? largest?.Ideology ??
            (IdeologySpreadSystem.TryGetStateIdeology(InstitutionSystem.GetPrimaryCulture(empire), out PartyIdeology stateIdeology)
                ? stateIdeology : PartyIdeology.Centrism);
        state.is_republic = true;
        state.republic_ideology = ideology;
        state.republic_since = World.world.getCurWorldTime();
        ChooseCalendar(state);
        state.one_party_id = "";
        state.deposed_royal_clan_id = empire.data.empire_specific_clan;
        BeginTransition(state);
        if (largest != null || foundingIdeology.HasValue)
            empire.CoreKingdom.GetOrCreate().ideology_country_suffix = PartySystem.PickCountrySuffix(empire, ideology);
        empire.data.empire_type_key = "";
        empire.data.year_name = "";
        IdeologySpreadSystem.SetStateIdeology(InstitutionSystem.GetPrimaryCulture(empire), ideology, empire);
        Record(empire, string.Format(LM.Get("republic_manual_republic_history"), empire.GetEmpireFullName()), null);
        RecordCalendarChoice(empire);
        TributaryAbolitionService.OnMonarchyAbolished(empire);
    }

    public static void ApplyFoundingPartyRule(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state?.is_republic != true || !OnePartyIdeologies.Contains(state.republic_ideology) ||
            !string.IsNullOrWhiteSpace(state.one_party_id)) return;
        FixedFaction ruling = PartySystem.GetParties(empire)
            .FirstOrDefault(party => party.Ideology == state.republic_ideology);
        if (ruling == null) return;
        List<FixedFaction> allies = IdeologyFamilies.IsSocialist(ruling.Ideology)
            ? PartySystem.GetParties(empire).Where(party => party != ruling &&
                party.Ideology is PartyIdeology.Centrism or PartyIdeology.Socialism or PartyIdeology.Communism).ToList()
            : new List<FixedFaction>();
        PartyBanSystem.Close(empire, ruling, allies, "party_ban_founding_history");
    }

    // 换政体会从模板重建政体(派系表被重置)：有政党时把政党和中央占比原样搬到新政体上
    public static void CarryPartiesAcrossRegimeChange(Kingdom core, Action change)
    {
        List<FixedFaction> factions = core?.GetRegime()?.GetPlayerFactions()?.ToList() ?? new List<FixedFaction>();
        bool hasParties = factions.Any(faction => faction != null && faction.IsParty);
        Dictionary<FixedFaction, int> ratios = new(core?.GetOrCreate().FactionRatio ?? new Dictionary<FixedFaction, int>());
        change();
        if (!hasParties || core == null) return;
        Regime regime = core.GetRegime();
        Empire empire = core.GetEmpire();
        if (regime == null || empire == null) return;
        regime.PlayerFactions = factions;
        foreach (FixedFaction faction in factions)
        {
            faction.EmpireId = empire.getID();
            faction.FixMissedTemporaryFactions();
        }
        core.GetOrCreate().FactionRatio = ratios;
        core.ReconcileFactionRatios(factions);
        regime.FactionSpace = null;
    }

    #endregion

    #region 元首与反对党

    // 元首去世/离任：立即由总理(没有就是主导派系领袖)接任，不等下一届大选
    public static void EnsureHeadOfState(Empire empire)
    {
        if (!IsRepublic(empire)) return;
        Actor head = empire.Emperor;
        if (head != null && !head.isRekt() && head.isAlive()) return;
        if (IsTransitioning(empire) || State(empire)?.republic_first_election_pending == true)
        {
            Actor successor = PartySystem.GetParties(empire)
                                  .FirstOrDefault(party => party.Ideology == State(empire).republic_ideology)
                                  ?.GetLeader()
                              ?? empire.CoreKingdom.units?.Where(actor => actor != null && !actor.isRekt() &&
                                  actor.isAlive() && actor.isAdult()).OrderByDescending(actor => actor.renown)
                                  .FirstOrDefault();
            if (successor != null && empire.InstallHeadOfState(successor))
                Record(empire, HeadOfStateHistory(empire, successor, head?.getName() ?? LM.Get("label_none")), successor);
            return;
        }
        UpdateHeadOfState(empire);
    }

    public static Actor GetOppositionLeader(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state?.parliament_seats == null || state.parliament_seats.Count == 0) return null;
        FixedFaction opposition = state.parliament_seats
            .Where(seat => !ParliamentSystem.IsGoverningFaction(empire, seat.faction_id))
            .GroupBy(seat => seat.faction_id).OrderByDescending(group => group.Count())
            .Select(group => PartySystem.GetParties(empire).FirstOrDefault(party => party.GetID() == group.Key))
            .FirstOrDefault(party => party != null);
        return opposition?.GetLeader();
    }

    public static int GetRepublicYears(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        return state == null || state.republic_since < 0 ? 0 : Date.getYearsSince(state.republic_since) + 1;
    }

    public static int GetCalendarMode(Empire empire)
    {
        int mode = State(empire)?.republic_calendar_mode ?? 0;
        return mode == CommonEraCalendar ? CommonEraCalendar : RepublicEraCalendar;
    }

    public static string GetCalendarText(Empire empire)
    {
        if (!IsRepublic(empire)) return "";
        int mode = GetCalendarMode(empire);
        int year = mode == CommonEraCalendar
            ? Date.getYearsSince(0d) + 1
            : GetRepublicYears(empire);
        return string.Format(LM.Get(mode == CommonEraCalendar
            ? "republic_common_era_format" : "republic_calendar_format"), year);
    }

    public static void SetCalendarMode(Empire empire, int mode)
    {
        if (!IsRepublic(empire) || (mode != CommonEraCalendar && mode != RepublicEraCalendar)) return;
        ConstitutionalEconomyState state = State(empire);
        if (state.republic_calendar_mode == mode) return;
        state.republic_calendar_mode = mode;
        RecordCalendarChoice(empire);
    }

    private static void ChooseCalendar(ConstitutionalEconomyState state)
    {
        state.republic_calendar_mode = UnityEngine.Random.value < 0.5f
            ? CommonEraCalendar : RepublicEraCalendar;
    }

    private static void RecordCalendarChoice(Empire empire)
    {
        string content = string.Format(LM.Get("republic_calendar_chosen_history"),
            empire.GetEmpireFullName(), GetCalendarMode(empire) == CommonEraCalendar
                ? LM.Get("republic_common_era_label") : LM.Get("republic_era_label"));
        Record(empire, content, null);
    }

    #endregion

    #region 工具

    private static void TransitionRegime(Empire empire, RegimeType regimeType)
    {
        CarryPartiesAcrossRegimeChange(empire.CoreKingdom,
            () => InstitutionSystem.ChangeRegimeForTransition(empire, regimeType));
        empire.data.empire_type_key = "";
    }

    private static void Record(Empire empire, string content, Actor actor)
    {
        empire.RecordHistory(directContent: content, actorId: actor?.id ?? -1L, kingdomId: empire.CoreKingdom.id);
        actor?.RecordPersonalHistory(content, "republic");
        TranslateHelper.LogEventMessage(content, empire.CoreKingdom);
    }

    #endregion
}

using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 党禁与选举制度：按帝国(不是按文化)动态开关。
//
// 文化层面的"开放党禁"制度只决定这个文化有没有政党政治；具体到某个国家，党禁可以重新关上、再打开：
//   · 关闭党禁(执政党之外的党一律取缔，one_party_id = 执政党)：
//       - 社会主义阵营(社会主义、共产主义)与中间派合占 ≥ 90% 议席 → 统一战线：社会主义党领导，中间派作为友党保留；
//       - 威权/法西斯政党占满全部议席 → 一党专政；
//       - 执政党独裁度拉满(见 GetAutocracy)；
//       - 社会主义政党掌握军队后发动政变、修改宪法；
//       - 社会主义、共产主义、法西斯立国时宪法即规定一党专政(RepublicSystem.ApplyFoundingPartyRule)。
//   · 重开党禁：党禁关闭期间每年累积压力。压力主要看帝国核心的民心——
//     核心法理内城市对执政理念(统一战线含友党理念)的支持率低、核心城市失守都会让压力快速上升；民意差也会加压。
//     用户 2026-11-10：压力满了不再无声翻多党(旧逻辑)，只导进三条路——核心繁荣不过门槛的穷/贫瘠国家守一党、
//     压力搁置，等外力(战败和约)来逼；过门槛的君主国由肯让步的君主妥协(和平开放、转君主立宪、保王朝)，
//     君主强硬或已是一党制共和则交由人民革命(TempFac_开放党禁)推动、取胜才开。见 UpdateReopenPressure。
//   · 选举制度：普选(按行政区直选) / 民主集中制(代表大会逐级推选：议席按党组织实力分配，领导党保有三分之二)。
//     关闭党禁时自动改行民主集中制，重开时恢复普选；玩家也可以在制度窗口手动切换。
public static class PartyBanSystem
{
    public const string ModeOpen = "open";
    public const string ModeOneParty = "one_party";
    public const string ModeUnitedFront = "united_front";

    private const float UnitedFrontSeatShare = 0.9f;
    private const float AutocracyThreshold = 95f;
    private const float LeadingPartyCongressShare = 2f / 3f;
    private const float CoupChance = 0.15f;
    private const float CoreSupportComfort = 0.5f;
    private const float CoreSupportCrisis = 0.35f;
    private const float CoreLossCrisis = 0.3f;

    private static ConstitutionalEconomyState State(Empire empire)
    {
        ConstitutionalEconomyState state = empire?.data?.constitutional_economy;
        if (state == null) return null;
        state.allied_party_ids ??= new List<string>();
        state.ruling_streak_party_id ??= "";
        return state;
    }

    #region 查询



    public static bool IsClosed(Empire empire) => RepublicSystem.IsOneParty(empire);

    public static string GetMode(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || !IsClosed(empire)) return ModeOpen;
        return state.allied_party_ids.Count > 0 ? ModeUnitedFront : ModeOneParty;
    }

    // 政治协商：一党领导下仍有友党参加(统一战线)。作用：宪政合法性 +ConsultationLegitimacy、
    // 民意异见 -ConsultationDissent、友党参与施政议程协商(见 ModernLegitimacy / PublicOpinionSystem / ParliamentSystem)
    public const int ConsultationLegitimacy = 5;
    public const float ConsultationDissent = 0.05f;

    public static List<FixedFaction> GetConsultativeParties(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || !IsClosed(empire) || state.allied_party_ids == null) return new List<FixedFaction>();
        return PartySystem.GetParties(empire).Where(party => party.GetID() != state.one_party_id &&
                                                             state.allied_party_ids.Contains(party.GetID())).ToList();
    }

    // 政协友党在中央的占比上限：友党只参与协商，不能压过领导党、更不能成为主导派系
    public const int ConsultativeRatioCap = 5;

    // 把各政协友党的中央占比压到 ConsultativeRatioCap 以内，超出部分归领导党
    public static void CapConsultativeRatios(Kingdom kingdom, Dictionary<FixedFaction, int> ratios)
    {
        if (kingdom == null || ratios == null || ratios.Count == 0 || !kingdom.IsEmpire()) return;
        Empire empire = kingdom.GetEmpire();
        ConstitutionalEconomyState state = State(empire);
        if (empire?.CoreKingdom != kingdom || state == null || !IsClosed(empire) ||
            state.allied_party_ids == null || state.allied_party_ids.Count == 0) return;
        FixedFaction leader = ratios.Keys.FirstOrDefault(faction => faction != null && faction.GetID() == state.one_party_id);
        int excess = 0;
        foreach (FixedFaction faction in ratios.Keys.ToList())
        {
            if (faction == null || faction == leader || !state.allied_party_ids.Contains(faction.GetID())) continue;
            if (ratios[faction] <= ConsultativeRatioCap) continue;
            excess += ratios[faction] - ConsultativeRatioCap;
            ratios[faction] = ConsultativeRatioCap;
        }
        if (excess > 0 && leader != null) ratios[leader] = Mathf.Min(100, ratios[leader] + excess);
    }

    public static bool IsConsultative(Empire empire, FixedFaction party)
    {
        ConstitutionalEconomyState state = State(empire);
        return party != null && state != null && IsClosed(empire) && party.GetID() != state.one_party_id &&
               state.allied_party_ids.Contains(party.GetID());
    }

    public static bool IsLeadingParty(Empire empire, FixedFaction party)
    {
        ConstitutionalEconomyState state = State(empire);
        return party != null && state != null && IsClosed(empire) && party.GetID() == state.one_party_id;
    }

    // 政党被并入、解散后：从友党名单中移除；若它是一党制的领导党，由并入的目标党接任，
    // 没有目标党时由实力最强的友党(没有友党则最强的政党)接任；一个政党都不剩就重开党禁
    public static void OnPartyRemoved(Empire empire, FixedFaction removed, FixedFaction successor)
    {
        if (removed != null) HandOver(empire, removed.GetID(), successor);
    }

    private static void HandOver(Empire empire, string id, FixedFaction successor)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null) return;
        state.allied_party_ids.Remove(id);
        if (state.one_party_id != id) return;
        List<FixedFaction> parties = PartySystem.GetParties(empire);
        FixedFaction heir = successor != null && parties.Contains(successor) ? successor : null;
        heir ??= parties.Where(party => state.allied_party_ids.Contains(party.GetID()))
            .OrderByDescending(party => party.CentralRatio).FirstOrDefault();
        heir ??= parties.OrderByDescending(party => party.CentralRatio).FirstOrDefault();
        if (heir == null)
        {
            state.one_party_id = "";
            state.allied_party_ids.Clear();
            return;
        }
        state.one_party_id = heir.GetID();
        state.allied_party_ids.Remove(heir.GetID());
        heir.Ban = false;
        state.last_parliament_election = -1d;
    }

    // 旧存档修复：领导党早已不在(并入、解散时没有交接)，或领导党同时挂在友党名单里
    private static void RepairLeadingParty(Empire empire, ConstitutionalEconomyState state)
    {
        if (!IsClosed(empire)) return;
        if (PartySystem.GetParties(empire).All(party => party.GetID() != state.one_party_id))
        {
            HandOver(empire, state.one_party_id, null);
            return;
        }
        state.allied_party_ids.Remove(state.one_party_id);
    }

    public static bool HasConsultation(Empire empire) => GetConsultativeParties(empire).Count > 0;

    public static bool IsAllowedParty(Empire empire, FixedFaction party)
    {
        ConstitutionalEconomyState state = State(empire);
        if (party == null || state == null) return false;
        return !IsClosed(empire) || party.GetID() == state.one_party_id ||
               state.allied_party_ids.Contains(party.GetID());
    }

    // 民主集中制只在本文化推行过普选(有全民选举的制度基础)后才能选；没有普选制度时照旧是限制选举
    public static bool CanChooseElectoralSystem(Empire empire) =>
        PartySystem.IsActive(empire) && PartySystem.HasUniversalSuffragePolicy(empire);

    // 宪法手定了权力中心时按宪法(代表大会制即民主集中制)，否则按党禁时定下的选举制度
    public static bool UsesDemocraticCentralism(Empire empire) =>
        CanChooseElectoralSystem(empire) &&
        (ConstitutionSystem.TryGetLockedPowerCenter(empire, out Data.ConstitutionPowerCenter powerCenter) &&
         RepublicSystem.IsRepublic(empire)
            ? powerCenter == Data.ConstitutionPowerCenter.Congress
            : State(empire)?.democratic_centralism == true);

    public static float GetReopenPressure(Empire empire) => State(empire)?.party_ban_reopen_pressure ?? 0f;

    // 独裁度(0~100)：
    //   执政党议席(无议会看中央占比) 40 + 执政理念的威权程度 25 + 元首兼执政党领袖 15 + 连续执政年数(每年 1，最多 20)
    public static float GetAutocracy(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        FixedFaction party = PartySystem.GetGovernmentParty(empire);
        if (state == null || party == null) return 0f;
        int seats = state.parliament_seats?.Count ?? 0;
        float share = ParliamentSystem.HasParliament(empire) && seats > 0
            ? state.parliament_seats.Count(seat => seat.faction_id == party.GetID()) / (float)seats
            : party.CentralRatio / 100f;
        float authority = Mathf.Clamp01(-PartySystem.GetPosition(party.Ideology).y / 75f);
        Actor leader = party.GetLeader();
        bool personalRule = leader != null && empire.Emperor != null && leader.id == empire.Emperor.id;
        float years = state.ruling_streak_party_id == party.GetID() && state.ruling_streak_since >= 0d
            ? Mathf.Min(20f, Date.getYearsSince(state.ruling_streak_since))
            : 0f;
        return Mathf.Clamp(40f * share + 25f * authority + (personalRule ? 15f : 0f) + years, 0f, 100f);
    }

    // 帝国核心的民心：核心法理内、仍归本帝国的城市里，信奉执政理念(统一战线含友党理念)的人口比例；
    // 以及核心城市中已不归本帝国的比例。没有帝国核心时退回全帝国统计。
    public static (float support, float lost) GetCoreSupport(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        FixedFaction ruling = PartySystem.GetGovernmentParty(empire);
        if (state == null || ruling == null) return (1f, 0f);
        var ideologies = new HashSet<PartyIdeology> { ruling.Ideology };
        foreach (FixedFaction ally in PartySystem.GetParties(empire)
                     .Where(party => state.allied_party_ids.Contains(party.GetID())))
            ideologies.Add(ally.Ideology);

        EmpireCore core = EmpireCoreManager.Get(empire);
        List<City> coreCities = core == null ? new List<City>() : EmpireCoreManager.GetCities(core);
        List<City> held = EmpireCoreControl.HeldCities(empire, core);
        float lost = coreCities.Count == 0 ? 0f : 1f - (float)held.Count / coreCities.Count;
        IEnumerable<City> sample = held.Count > 0
            ? held
            : empire.kingdoms_list.Where(kingdom => kingdom != null && !kingdom.isRekt())
                .SelectMany(kingdom => kingdom.cities ?? Enumerable.Empty<City>());
        int population = 0;
        int supporters = 0;
        foreach (City city in sample)
        {
            foreach (KeyValuePair<PartyIdeology, int> pair in IdeologyPopulationSystem.GetCityCounts(city))
            {
                population += pair.Value;
                if (ideologies.Contains(pair.Key)) supporters += pair.Value;
            }
        }
        return (population == 0 ? 1f : (float)supporters / population, lost);
    }

    // 自主现代化(开放党禁)的硬发展门槛(用户 2026-11-10："贫瘠穷地方不该自己革命现代化")：
    // 核心繁荣低于此值的穷/贫瘠国家守封建、一党，自主的两条路(人民革命、君主妥协)都开不了党禁，
    // 只有外力(战败和约，见 ConstitutionalEconomySystem.ImposeModernization)能逼其现代化。
    // 繁荣度 = 核心法理内各城 IdeologyPopulationSystem.CityProsperity 均值(0~1)。
    // 阈值校准：城市繁荣度 ≈ 0.7×工商市民比(占成年人三成即满) + 0.3×有高级民居。典型发达核心(工商市民近三成、
    // 有高级民居)在 0.5 以上，轻度城市化(工商两成、无高级民居)约 0.14，纯农/游牧核心约 0.05~0.1；
    // 取 0.35：把"有像样工商阶层的发达核心"放行、"贫瘠草原/纯农穷核心"挡在门外。
    public const float ModernizationProsperityFloor = 0.35f;

    // 帝国核心的繁荣度(0~1)：核心法理内仍归本帝国的城市繁荣度均值；没有核心城市时退回核心王国自己的城。
    public static float CoreProsperity(Empire empire)
    {
        if (empire?.CoreKingdom == null) return 0f;
        EmpireCore core = EmpireCoreManager.Get(empire);
        List<City> cities = core == null ? new List<City>() : EmpireCoreControl.HeldCities(empire, core);
        if (cities.Count == 0)
            cities = empire.CoreKingdom.cities?.Where(city => city != null && !city.isRekt()).ToList() ?? new List<City>();
        return cities.Count == 0 ? 0f : cities.Average(IdeologyPopulationSystem.CityProsperity);
    }

    // 能否走自主路(人民革命 / 君主妥协)开放党禁：核心繁荣须过发展门槛。
    // 只拦"新的自主开放"，已经开放/现代化的国家不经过这里，不会被打回封建。
    public static bool PassesModernizationGate(Empire empire) =>
        CoreProsperity(empire) >= ModernizationProsperityFloor;

    #endregion

    #region 开关党禁

    // 进入一党制后其余政党的去留：有政治协商制度(统一战线，有友党)的，其余政党都参加政治协商、接受领导，
    // 只有与执政党理念截然对立的(光谱距离 ≥ HostileDistance，如共产党领导时的法西斯、资本主义政党)被解散；
    // 没有政治协商的(纯一党制)其余政党一律解散。返回参加政治协商的友党
    private const float HostileDistance = 120f;

    private static List<FixedFaction> SettleOtherParties(Empire empire, FixedFaction leader, List<FixedFaction> allies)
    {
        bool consultative = allies.Count > 0;
        var allowed = new List<FixedFaction>(allies);
        var dissolved = new List<string>();
        foreach (FixedFaction party in empire.CoreKingdom.GetRegime().GetPlayerFactions()
                     .Where(party => party != null && party.IsParty && party != leader && !allowed.Contains(party))
                     .ToList())
        {
            if (consultative && !party.Ban &&
                PartySystem.IdeologyDistance(party.Ideology, leader.Ideology) < HostileDistance)
            {
                allowed.Add(party);
                continue;
            }
            if (!party.Ban) dissolved.Add(party.Name);
            PartySystem.DisbandParty(empire, party);
        }
        foreach (FixedFaction ally in allowed) ally.Ban = false;
        if (dissolved.Count > 0)
            EventRecorder.Record(empire, string.Format(LM.Get("party_ban_dissolved_history"),
                empire.GetEmpireFullName(), string.Join("、", dissolved)));
        return allowed;
    }

    public static void Close(Empire empire, FixedFaction leader, IEnumerable<FixedFaction> allies, string historyKey)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || leader == null || !leader.IsParty || leader.Ban) return;
        List<FixedFaction> allowed = (allies ?? Enumerable.Empty<FixedFaction>())
            .Where(ally => ally != null && ally != leader && ally.IsParty && !ally.Ban).Distinct().ToList();
        state.one_party_id = leader.GetID();
        allowed = SettleOtherParties(empire, leader, allowed);
        state.allied_party_ids = allowed.Select(ally => ally.GetID()).ToList();
        empire.CoreKingdom?.ClampFactionRatio();
        if (CanChooseElectoralSystem(empire)) state.democratic_centralism = true;
        state.party_ban_reopen_pressure = 0f;
        state.party_ban_since = World.world.getCurWorldTime();
        state.last_parliament_election = -1d;
        // 党禁由政变、独裁等途径改变：宪法里手定的政党制度随之失效(宪法页修改时会重新手定)
        ConstitutionSystem.ReleaseLock(empire, ConstitutionSystem.ClausePartySystem);
        string allyNames = allowed.Count == 0 ? LM.Get("label_none") : string.Join("、", allowed.Select(ally => ally.Name));
        EventRecorder.Record(empire, string.Format(LM.Get(historyKey), empire.GetEmpireFullName(), leader.Name, allyNames),
            leader.GetLeader());
    }

    public static void Open(Empire empire, string historyKey)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || !IsClosed(empire)) return;
        string rulingName = PartySystem.GetGovernmentParty(empire)?.Name ?? "";
        state.one_party_id = "";
        state.allied_party_ids.Clear();
        // 被取缔的政党恢复合法(党员当年已被遣散，重新从民间吸收)
        foreach (FixedFaction party in empire.CoreKingdom?.GetRegime()?.GetPlayerFactions()?
                     .Where(faction => faction != null && faction.IsParty && faction.Ban).ToList() ??
                 new List<FixedFaction>())
            party.Ban = false;
        state.democratic_centralism = false;
        state.party_ban_reopen_pressure = 0f;
        state.party_ban_since = -1d;
        state.last_parliament_election = -1d;
        ConstitutionSystem.ReleaseLock(empire, ConstitutionSystem.ClausePartySystem);
        EventRecorder.Record(empire, string.Format(LM.Get(historyKey), empire.GetEmpireFullName(), rulingName), null);
    }

    // 宪法页修改政党制度：多党 → 开放党禁；一党/统一战线 → 由执政党关闭党禁(统一战线保留友党)。
    // 已经关闭时在一党与统一战线之间切换，只改友党名单，不走重开再关闭
    public static bool SetModeByConstitution(Empire empire, Data.ConstitutionPartySystem mode)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || !PartySystem.IsActive(empire) || RepublicSystem.IsTransitioning(empire)) return false;
        if (mode == Data.ConstitutionPartySystem.MultiParty)
        {
            if (IsClosed(empire)) Open(empire, "party_ban_reopened_constitution_history");
            ParliamentSystem.Update(empire);
            return true;
        }
        if (mode != Data.ConstitutionPartySystem.OneParty && mode != Data.ConstitutionPartySystem.UnitedFront) return false;
        FixedFaction leader = IsClosed(empire)
            ? PartySystem.GetParties(empire).FirstOrDefault(party => party.GetID() == state.one_party_id)
            : PartySystem.GetGovernmentParty(empire) ?? empire.CoreKingdom?.GetRegime()?.GetDominateFaction();
        if (leader == null || !leader.IsParty || leader.Ban) return false;
        List<FixedFaction> allies = mode == Data.ConstitutionPartySystem.UnitedFront
            ? UnitedFrontAllies(empire, leader)
            : new List<FixedFaction>();
        if (mode == Data.ConstitutionPartySystem.UnitedFront && allies.Count == 0) return false;
        if (!IsClosed(empire))
        {
            Close(empire, leader, allies, allies.Count > 0 ? "party_ban_united_front_history" : "party_ban_one_party_history");
        }
        else
        {
            allies = SettleOtherParties(empire, leader, allies);
            state.allied_party_ids = allies.Select(ally => ally.GetID()).ToList();
            empire.CoreKingdom?.ClampFactionRatio();
            state.last_parliament_election = -1d;
        }
        ParliamentSystem.Update(empire);
        return true;
    }

    public static bool CanUnitedFront(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        FixedFaction leader = IsClosed(empire)
            ? PartySystem.GetParties(empire).FirstOrDefault(party => party.GetID() == state?.one_party_id)
            : PartySystem.GetGovernmentParty(empire) ?? empire?.CoreKingdom?.GetRegime()?.GetDominateFaction();
        return leader != null && leader.IsParty && UnitedFrontAllies(empire, leader).Count > 0;
    }

    // 统一战线的友党：社会主义党领导时照旧(中间派与社会主义各党)，否则取理念相近(光谱距离 < 50)的合法政党
    private static List<FixedFaction> UnitedFrontAllies(Empire empire, FixedFaction leader)
    {
        List<FixedFaction> allies = DefaultAllies(empire, leader);
        if (allies.Count > 0) return allies;
        return empire.CoreKingdom?.GetRegime()?.GetPlayerFactions()?
            .Where(party => party != null && party != leader && party.IsParty &&
                            PartySystem.IdeologyDistance(party.Ideology, leader.Ideology) < 50f).ToList()
               ?? new List<FixedFaction>();
    }

    // 玩家手动关闭：执政党掌握三分之二以上(修宪门槛)或满足任一自动关闭条件
    public static bool CanCloseManually(Empire empire, out FixedFaction leader, out List<FixedFaction> allies)
    {
        allies = new List<FixedFaction>();
        leader = null;
        if (!PartySystem.IsActive(empire) || IsClosed(empire) || RepublicSystem.IsTransitioning(empire)) return false;
        if (TryFindAutomaticClose(empire, out leader, out allies, out _)) return true;
        FixedFaction governing = PartySystem.GetGovernmentParty(empire);
        if (governing == null) return false;
        if (SeatShare(empire, party => party == governing) < LeadingPartyCongressShare) return false;
        leader = governing;
        allies = DefaultAllies(empire, governing);
        return true;
    }

    public static bool CloseManually(Empire empire)
    {
        if (!CanCloseManually(empire, out FixedFaction leader, out List<FixedFaction> allies)) return false;
        Close(empire, leader, allies, allies.Count > 0 ? "party_ban_united_front_history" : "party_ban_one_party_history");
        ParliamentSystem.Update(empire);
        return true;
    }

    public static bool OpenManually(Empire empire)
    {
        if (!IsClosed(empire)) return false;
        Open(empire, "party_ban_reopened_manual_history");
        ParliamentSystem.Update(empire);
        return true;
    }

    public static bool ToggleElectoralSystem(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || !CanChooseElectoralSystem(empire)) return false;
        state.democratic_centralism = !state.democratic_centralism;
        state.last_parliament_election = -1d;
        // 在制度窗口直接切换选举制度：宪法里手定的权力中心随之失效，改回按实际制度同步
        ConstitutionSystem.ReleaseLock(empire, ConstitutionSystem.ClausePowerCenter);
        EventRecorder.Record(empire, string.Format(LM.Get(state.democratic_centralism
            ? "electoral_democratic_centralism_history" : "electoral_universal_suffrage_history"),
            empire.GetEmpireFullName()), null);
        ParliamentSystem.Update(empire);
        return true;
    }

    // 社会主义党领导时，中间派作为友党保留
    private static List<FixedFaction> DefaultAllies(Empire empire, FixedFaction leader) =>
        IdeologyFamilies.IsSocialist(leader.Ideology)
            ? PartySystem.GetParties(empire).Where(party => party != leader &&
                (party.Ideology == PartyIdeology.Centrism || IdeologyFamilies.IsSocialist(party.Ideology))).ToList()
            : new List<FixedFaction>();

    #endregion

    #region 年度更新(由 ConstitutionalEconomySystem 调用)

    public static void Update(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || World.world == null || !PartySystem.IsActive(empire)) return;
        RepairLeadingParty(empire, state);
        TrackRulingStreak(empire, state);
        if (RepublicSystem.IsTransitioning(empire) || RepublicSystem.HasActiveRevolutionWar(empire)) return;
        if (state.last_party_ban_check >= 0d && Date.getYearsSince(state.last_party_ban_check) < 1) return;
        state.last_party_ban_check = World.world.getCurWorldTime();

        // 宪法手定了政党制度：独裁、民意等合法途径不再开关党禁，只有政变能推翻
        bool fixedByConstitution = ConstitutionSystem.IsPlayerLocked(empire, ConstitutionSystem.ClausePartySystem);
        if (IsClosed(empire))
        {
            // 旧存档：一党制下还挂着"已取缔"的政党，按现行规则解散
            FixedFaction ruling = PartySystem.GetParties(empire).FirstOrDefault(party => party.GetID() == state.one_party_id);
            if (ruling != null && empire.CoreKingdom.GetRegime().GetPlayerFactions().Any(party => party != null && party.IsParty && party.Ban))
                foreach (FixedFaction banned in empire.CoreKingdom.GetRegime().GetPlayerFactions()
                             .Where(party => party != null && party.IsParty && party.Ban).ToList())
                    PartySystem.DisbandParty(empire, banned);
            if (!fixedByConstitution) UpdateReopenPressure(empire, state);
            return;
        }
        if (!fixedByConstitution &&
            TryFindAutomaticClose(empire, out FixedFaction leader, out List<FixedFaction> allies, out string key))
        {
            Close(empire, leader, allies, key);
            return;
        }
        TrySocialistCoup(empire);
    }

    private static void TrackRulingStreak(Empire empire, ConstitutionalEconomyState state)
    {
        string id = PartySystem.GetGovernmentParty(empire)?.GetID() ?? "";
        if (id == state.ruling_streak_party_id) return;
        state.ruling_streak_party_id = id;
        state.ruling_streak_since = string.IsNullOrEmpty(id) ? -1d : World.world.getCurWorldTime();
    }

    private static bool TryFindAutomaticClose(Empire empire, out FixedFaction leader, out List<FixedFaction> allies,
        out string historyKey)
    {
        leader = null;
        allies = new List<FixedFaction>();
        historyKey = "";
        ConstitutionalEconomyState state = State(empire);
        if (state == null || IsClosed(empire)) return false;
        List<FixedFaction> parties = PartySystem.GetParties(empire);
        bool elected = ParliamentSystem.HasParliament(empire) && (state.parliament_seats?.Count ?? 0) > 0;

        if (elected)
        {
            // 社会主义 + 中间派 ≥ 90%：统一战线，议席最多的社会主义党领导
            float bloc = SeatShare(empire, party => IdeologyFamilies.IsSocialist(party.Ideology) || party.Ideology == PartyIdeology.Centrism);
            FixedFaction socialist = parties.Where(party => IdeologyFamilies.IsSocialist(party.Ideology))
                .OrderByDescending(party => Seats(state, party)).FirstOrDefault();
            if (bloc >= UnitedFrontSeatShare && socialist != null && Seats(state, socialist) > 0)
            {
                leader = socialist;
                allies = DefaultAllies(empire, socialist).Where(party => Seats(state, party) > 0).ToList();
                historyKey = "party_ban_united_front_history";
                return true;
            }
            // 威权政党占满全部议席：一党专政
            FixedFaction authoritarian = parties.Where(party => IdeologyFamilies.IsAuthoritarian(party.Ideology))
                .OrderByDescending(party => Seats(state, party)).FirstOrDefault();
            if (authoritarian != null && SeatShare(empire, party => party == authoritarian) >= 0.999f)
            {
                leader = authoritarian;
                historyKey = "party_ban_one_party_history";
                return true;
            }
        }
        // 独裁度拉满：执政党取缔其他政党
        FixedFaction governing = PartySystem.GetGovernmentParty(empire);
        if (governing != null && GetAutocracy(empire) >= AutocracyThreshold)
        {
            leader = governing;
            allies = DefaultAllies(empire, governing).Where(party => !elected || Seats(state, party) > 0).ToList();
            historyKey = "party_ban_autocracy_history";
            return true;
        }
        return false;
    }

    // 社会主义政党掌握军队(过半士兵信奉其理念、理念已完成第三阶段)时，在动荡中发动政变并修改宪法
    private static void TrySocialistCoup(Empire empire)
    {
        FixedFaction party = PartySystem.GetParties(empire)
            .Where(candidate => IdeologyFamilies.IsSocialist(candidate.Ideology) && RepublicSystem.CanCoup(empire, candidate))
            .OrderByDescending(candidate => IdeologyPopulationSystem.GetMilitaryShare(empire, candidate.Ideology))
            .FirstOrDefault();
        if (party == null) return;
        int opinion = PublicOpinionSystem.GetLevel(empire);
        float chance = CoupChance * (opinion + (empire.Legitimacy < 40 ? 1 : 0));
        if (chance <= 0f || UnityEngine.Random.value >= chance) return;
        if (!RepublicSystem.IsRepublic(empire))
        {
            // 君主国：先废君主、建立共和，宪法随之规定社会主义党领导
            RepublicSystem.PushRepublic(empire, party);
            if (!RepublicSystem.IsRepublic(empire)) return;
        }
        Close(empire, party, DefaultAllies(empire, party), "party_ban_socialist_coup_history");
    }

    // 君主在革命压力下会不会让步、和平开放党禁(类明治立宪)：改革派(明君)、弱势之君(庸弱、冲龄、昏聩)让步；
    // 暴君强压到底；中主、荒淫之君守成恋权，只有正统低(自身难保)时才被迫让步。
    private const float ConcedeLegitimacyFloor = 35f;

    private static bool MonarchWouldConcede(Empire empire)
    {
        Actor ruler = empire?.Emperor;
        if (ruler == null) return true; // 无主/摄政：朝廷无力弹压，被迫让步
        switch (RulerQualitySystem.Of(ruler))
        {
            case RulerQualitySystem.Grade.Wise:    // 明君顺势立宪
            case RulerQualitySystem.Grade.Weak:    // 庸弱之君无力弹压
            case RulerQualitySystem.Grade.Minor:   // 冲龄之主/摄政
            case RulerQualitySystem.Grade.Fatuous: // 昏聩不理，朝局失控
                return true;
            case RulerQualitySystem.Grade.Tyrant:  // 暴君强压到底，绝不妥协
                return false;
            default:                               // 中主、荒淫之君：守成恋权，正统低时才被迫让步
                return empire.Legitimacy < ConcedeLegitimacyFloor;
        }
    }

    // 用户 2026-11-10：压力满了不再无声 Open()，只导进三条路之一(见本文件头注释)。
    private static void UpdateReopenPressure(Empire empire, ConstitutionalEconomyState state)
    {
        (float support, float lost) = GetCoreSupport(empire);
        float delta = 0f;
        if (support < CoreSupportCrisis) delta += (CoreSupportCrisis - support) * 100f;
        else if (support >= CoreSupportComfort) delta -= 10f;
        if (lost >= CoreLossCrisis) delta += 15f + lost * 20f;
        int opinion = PublicOpinionSystem.GetLevel(empire);
        delta += opinion == 0 ? -5f : 5f * opinion;
        PartyOpeningPressure challenge = PartyOpeningRules.Measure(
            IdeologyPopulationSystem.GetEmpireCounts(empire), IdeologyFamilies.StateIdeology(empire));
        if (challenge.HasChallenge) delta = Mathf.Max(delta, 12f + challenge.LeadShare * 40f);
        state.party_ban_reopen_pressure = Mathf.Clamp(state.party_ban_reopen_pressure + delta, 0f, 100f);
        if (state.party_ban_reopen_pressure < 100f) return;
        // 没过发展门槛：穷/贫瘠核心守一党，压力搁置、维持现状，等外力(战败和约)来逼。
        if (!PassesModernizationGate(empire)) return;
        // 君主制 + 君主肯让步：君主妥协，和平开放党禁、随之确立君主立宪(保王朝)。
        if (!RepublicSystem.IsRepublic(empire) && MonarchWouldConcede(empire))
        {
            Open(empire, "party_ban_reopened_monarch_concession_history");
            ConstitutionalEconomySystem.ConcedeConstitutionalMonarchy(empire);
            ParliamentSystem.Update(empire);
            return;
        }
        // 君主强硬、或已是一党制共和：不自上而下开放，交由人民革命(TempFac_开放党禁)推动、取胜才 Open。
        // 压力保持满格，让革命诉求持续获得最高优先级；革命成功按现有流程记史(君主国可能亡朝)。
    }

    #endregion

    #region 民主集中制选举(由 ParliamentSystem 调用)

    // 代表大会逐级推选：各行政区按人口分代表名额，区内按党组织实力(党员数)分；党禁关闭时领导党保有三分之二
    public static List<(FixedFaction party, long district)> AllocateCongressSeats(Empire empire,
        List<FixedFaction> parties, int seats)
    {
        var result = new List<(FixedFaction, long)>();
        if (parties == null || parties.Count == 0 || seats <= 0) return result;
        ConstitutionalEconomyState state = State(empire);
        FixedFaction leader = IsClosed(empire)
            ? parties.FirstOrDefault(party => party.GetID() == state?.one_party_id)
            : null;
        Dictionary<FixedFaction, float> strength = parties.ToDictionary(party => party,
            party => PartySystem.OrganizationSize(empire, party) + 1f);

        Dictionary<FixedFaction, int> national;
        if (leader != null)
        {
            int leading = Mathf.CeilToInt(seats * LeadingPartyCongressShare);
            List<FixedFaction> others = parties.Where(party => party != leader).ToList();
            national = others.Count == 0
                ? new Dictionary<FixedFaction, int>()
                : ParliamentSystem.AllocateSeats(others, seats - leading, party => strength[party]);
            national[leader] = seats - national.Values.Sum();
        }
        else
        {
            national = ParliamentSystem.AllocateSeats(parties, seats, party => strength[party]);
        }

        // 代表按人口派驻到各行政区，只影响议员从哪个区推选
        List<Kingdom> districts = empire.kingdoms_list.Where(kingdom => kingdom != null && !kingdom.isRekt() &&
            kingdom.units?.Count > 0).OrderByDescending(kingdom => kingdom.units.Count).ToList();
        int index = 0;
        foreach (KeyValuePair<FixedFaction, int> pair in national.OrderByDescending(pair => pair.Value))
            for (int i = 0; i < pair.Value; i++)
            {
                long district = districts.Count == 0 ? -1L : districts[index++ % districts.Count].id;
                result.Add((pair.Key, district));
            }
        return result;
    }

    #endregion

    private static int Seats(ConstitutionalEconomyState state, FixedFaction party) =>
        state?.parliament_seats?.Count(seat => seat.faction_id == party.GetID()) ?? 0;

    private static float SeatShare(Empire empire, Func<FixedFaction, bool> filter)
    {
        ConstitutionalEconomyState state = State(empire);
        int total = state?.parliament_seats?.Count ?? 0;
        List<FixedFaction> parties = PartySystem.GetParties(empire);
        if (ParliamentSystem.HasParliament(empire) && total > 0)
        {
            HashSet<string> ids = new(parties.Where(filter).Select(party => party.GetID()));
            return state.parliament_seats.Count(seat => ids.Contains(seat.faction_id)) / (float)total;
        }
        return parties.Where(filter).Sum(party => party.CentralRatio) / 100f;
    }

}

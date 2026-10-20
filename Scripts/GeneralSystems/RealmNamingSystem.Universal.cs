using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 西方：普世帝国之名(方案第 6 步，用户 2026-10-20 拍板)。
//   · 名字取自天下：本帝国法理所在天下的基名(西方天下默认"{0}普世帝国"里的 {0})；未经教宗认可只能用本帝国法理名；
//   · 取得途径(教宗加冕时判，自行加冕不算)：
//       ① 教廷对本帝国核心王国好感为正，正常请冕；
//       ② 施压：加冕远征到达、或与教廷接壤(水师可达)，本帝国(连成员)国力至少是教廷的三倍——不论好感；教权 −5，教廷记恨；
//          开战：诉求「逼冕」开战，打下教廷都城或圣城、或战胜，由和约加冕(解除这场战争招来的绝罚)，教权 −10；打输正统 −5、十年不再逼冕；
//       ③ 控制教宗国：教廷是本帝国的成员、附庸，或教廷之囚落在本帝国——加冕即授，不看好感(神罗照旧要远征)；
//          本国就是教宗国的是圣座帝国，照旧称"某某圣座"，不另取；
//   · 同一天下只一位普世皇帝：已有未失格的，教宗不给别家授名(加冕照旧)；
//   · 收回：绝罚期间暂停(名字退回本名，悔罪、解除后恢复)；脱离罗马、离开那个天下即收回；换皇帝不收回，新君三年仍未由教宗加冕才收回；
//   · "神圣"前缀与普世之名各自取得、可以叠加(神圣 + 基名 + 帝国)；普世之名不排"第几帝国"。
public static partial class RealmNamingSystem
{
    public const int ViaNone = 0, ViaFavor = 1, ViaPressure = 2, ViaWar = 3, ViaControl = 4, ViaDecree = 5;

    // 挟冕之辱：教廷对逼冕的帝国好感 −30，二十年回复
    private const int CoercionGrudge = 30;
    private const float CoercionGrudgeYears = 20f, CoerceCooldownYears = 10f;
    private const float PressureAuthority = -5f, WarAuthority = -10f;
    private const int CoerceFailedLegitimacy = -5;

    private static PapalRelationState Papal(Empire empire) => empire?.data == null ? null : PapacySystem.State(empire);

    // 普世之名生效中(取得且未因绝罚暂停)
    public static bool HoldsUniversal(Empire empire)
    {
        PapalRelationState state = Papal(empire);
        return state != null && state.universal_lead_id >= 0 && !state.universal_suspended;
    }

    // 本帝国法理所在的天下(主核心)
    public static EmpireCore UniversalLead(Empire empire)
    {
        EmpireCore own = EmpireCoreManager.Get(empire);
        return own == null ? null : TianxiaSystem.Lead(own);
    }

    // 同一天下里另一位未失格的普世皇帝
    public static Empire OtherUniversal(EmpireCore lead, Empire except)
    {
        if (lead == null || ModClass.EMPIRE_MANAGER == null) return null;
        return ModClass.EMPIRE_MANAGER.FirstOrDefault(other => other?.data != null && other != except && !other.isRekt() &&
                                                               !other.IsArchived() && HoldsUniversal(other) &&
                                                               Papal(other).universal_lead_id == lead.id);
    }

    private static int Opinion(Kingdom from, Kingdom to) => from == null || to == null ? 0
        : World.world?.diplomacy?.getOpinion(from, to)?.total ?? 0;

    // 控制教宗国：教廷是本帝国的成员(含教廷之囚落在本帝国)或附庸(圣座帝国不算)
    public static bool ControlsPapacy(Empire empire, Kingdom papacy)
    {
        if (empire?.CoreKingdom == null || papacy == null || PapacySystem.IsHolySee(empire)) return false;
        return papacy == empire.CoreKingdom || papacy.GetEmpire() == empire ||
               EmpireCoreControl.Sovereign(papacy) == empire.CoreKingdom;
    }

    // 施压：兵临城下(加冕远征到达，或与教廷接壤、水师可达)，国力至少是教廷的三倍
    public static bool CanPressure(Empire empire, Kingdom papacy, bool romzugArrived)
    {
        if (empire?.CoreKingdom == null || papacy == null) return false;
        bool near = romzugArrived || empire.IsNeighbourWith(papacy) || StraitReach.Across(empire.CoreKingdom, papacy);
        return near && RealmNameRules.PressureMet(WarSituation.Power(empire), WarSituation.Power(papacy));
    }

    // 教宗加冕时取得普世之名的途径(PapacySystem.Coronation 调用；不够格返回 ViaNone)
    public static int CoronationVia(Empire empire, Kingdom papacy, bool romzugArrived)
    {
        if (!Enabled || !IsWestern(empire) || papacy == null) return ViaNone;
        if (ControlsPapacy(empire, papacy)) return ViaControl;
        if (Opinion(papacy, empire.CoreKingdom) > 0) return ViaFavor;
        if (CanPressure(empire, papacy, romzugArrived)) return ViaPressure;
        return ViaNone;
    }

    // 教宗为这位皇帝加冕之后(不含自行加冕)：已有普世之名的记下新君；没有的按途径授
    public static void OnPapalCoronation(Empire empire, int via)
    {
        if (!Enabled || empire?.data == null || empire.Emperor == null) return;
        PapalRelationState state = Papal(empire);
        if (state == null) return;
        if (state.universal_lead_id >= 0)
        {
            state.universal_emperor_id = empire.Emperor.id;
            return;
        }
        TryGrantUniversal(empire, via);
    }

    // 授普世之名；quiet = 读档追授，只记史册不上公告
    public static bool TryGrantUniversal(Empire empire, int via, bool quiet = false)
    {
        if (via == ViaNone || !Enabled || empire?.data == null || empire.CoreKingdom == null || !IsWestern(empire)) return false;
        PapalRelationState state = Papal(empire);
        EmpireCore lead = UniversalLead(empire);
        if (state == null || lead == null || PapacySystem.IsHolySee(empire)) return false;
        if (state.universal_lead_id >= 0 && !state.universal_suspended) return false;
        if (OtherUniversal(lead, empire) != null) return false;
        string baseName = TianxiaBaseName(lead);
        if (string.IsNullOrWhiteSpace(baseName)) return false;
        Religion religion = PapacySystem.ReligionOf(empire);
        string before = empire.GetEmpireFullName();
        if (empire.data.name_source != SourceTianxia) empire.data.pre_tianxia_name = empire.data.core_name ?? "";
        state.universal_lead_id = lead.id;
        state.universal_via = via;
        state.universal_at = World.world.getCurWorldTime();
        state.universal_emperor_id = empire.Emperor?.id ?? -1L;
        state.universal_suspended = false;
        empire.data.name_source = SourceTianxia;
        empire.SetEmpireName(baseName, preserveEmpireType: true);
        state.universal_name = empire.data.core_name ?? "";
        if (via == ViaPressure || via == ViaWar)
        {
            state.coerced_at = state.universal_at;
            PapacySystem.AddAuthority(religion, via == ViaWar ? WarAuthority : PressureAuthority);
        }
        // 不是联合天下时基名就是本帝国法理名，名字不变，只记身份
        bool renamed = !string.Equals(before, empire.GetEmpireFullName(), StringComparison.Ordinal);
        string text = string.Format(LM.Get(renamed ? "realm_universal_granted_history" : "realm_universal_recognized_history"),
            PapacySystem.Pope(religion)?.getName() ?? LM.Get("label_none"), empire.Emperor?.getName() ?? "",
            before, empire.GetEmpireFullName(), TianxiaSystem.Name(lead), LM.Get("realm_universal_via_" + via));
        if (quiet) empire.RecordHistory(directContent: text, actorId: empire.Emperor?.id ?? -1L, kingdomId: empire.CoreKingdom.id);
        else EventRecorder.Record(empire, text);
        LogService.LogInfo($"[EmpireCraft][普世之名] {before} 经{via}得 {baseName}，称 {empire.GetEmpireFullName()}");
        return true;
    }

    // 收回(suspend = 绝罚暂停，悔罪后恢复)
    public static void RevokeUniversal(Empire empire, string reasonKey, bool suspend = false)
    {
        PapalRelationState state = Papal(empire);
        if (state == null || state.universal_lead_id < 0) return;
        if (suspend && state.universal_suspended) return;
        string before = empire.GetEmpireFullName();
        string restore = !string.IsNullOrWhiteSpace(empire.data.pre_tianxia_name)
            ? empire.data.pre_tianxia_name
            : CoreName(EmpireCoreManager.Get(empire));
        if (suspend) state.universal_suspended = true;
        else
        {
            state.universal_lead_id = -1L;
            state.universal_via = ViaNone;
            state.universal_emperor_id = -1L;
            state.universal_suspended = false;
        }
        empire.data.name_source = empire.data.named_core_id >= 0 ? SourceEmpireTitle : SourceLegacy;
        if (!string.IsNullOrWhiteSpace(restore)) empire.SetEmpireName(restore, preserveEmpireType: true);
        EventRecorder.Record(empire, string.Format(LM.Get(suspend ? "realm_universal_suspended_history" : "realm_universal_revoked_history"),
            before, empire.GetEmpireFullName(), LM.Get(reasonKey)));
        LogService.LogInfo($"[EmpireCraft][普世之名] {before} 失普世之名({reasonKey}{(suspend ? "，暂停" : "")})，称 {empire.GetEmpireFullName()}");
    }

    // 绝罚解除后恢复
    private static void ResumeUniversal(Empire empire)
    {
        PapalRelationState state = Papal(empire);
        EmpireCore lead = UniversalLead(empire);
        if (state == null || lead == null || lead.id != state.universal_lead_id || OtherUniversal(lead, empire) != null)
        {
            RevokeUniversal(empire, "realm_universal_reason_taken");
            return;
        }
        string baseName = TianxiaBaseName(lead);
        if (string.IsNullOrWhiteSpace(baseName)) return;
        string before = empire.GetEmpireFullName();
        empire.data.pre_tianxia_name = empire.data.core_name ?? "";
        state.universal_suspended = false;
        empire.data.name_source = SourceTianxia;
        empire.SetEmpireName(baseName, preserveEmpireType: true);
        state.universal_name = empire.data.core_name ?? "";
        EventRecorder.Record(empire, string.Format(LM.Get("realm_universal_resumed_history"), before, empire.GetEmpireFullName()));
    }

    // 读档后头一次年度结算之前加的冕(旧档)：按新规追授一次
    private static object _session;
    private static double _sessionStart = -1d;

    // 每年(RealmNamingSystem.Yearly)
    private static void YearlyUniversal(Empire empire)
    {
        PapalRelationState state = Papal(empire);
        if (state == null || !IsWestern(empire) && state.universal_lead_id < 0) return;
        if (!ReferenceEquals(_session, World.world))
        {
            _session = World.world;
            _sessionStart = World.world.getCurWorldTime();
        }
        if (state.universal_lead_id < 0)
        {
            // 读档追授：本局之前就由教宗加冕、仍在位、没有被绝罚的
            if (state.crowned_at >= 0d && state.crowned_at < _sessionStart && !state.self_crowned &&
                PapacySystem.IsCrowned(empire) && !PapacySystem.IsExcommunicated(empire) && !PapacySystem.BrokeWithRome(empire))
            {
                Kingdom papacy = PapacySystem.Papacy(PapacySystem.ReligionOf(empire));
                int via = papacy == null ? ViaNone
                    : ControlsPapacy(empire, papacy) ? ViaControl
                    : Opinion(papacy, empire.CoreKingdom) > 0 ? ViaFavor : ViaNone;
                TryGrantUniversal(empire, via, quiet: true);
            }
            return;
        }
        EmpireCore lead = UniversalLead(empire);
        if (!IsWestern(empire) || lead == null || lead.id != state.universal_lead_id)
        {
            RevokeUniversal(empire, "realm_universal_reason_left");
            return;
        }
        if (PapacySystem.BrokeWithRome(empire))
        {
            RevokeUniversal(empire, "realm_universal_reason_rome");
            return;
        }
        if (PapacySystem.IsExcommunicated(empire))
        {
            RevokeUniversal(empire, "realm_universal_reason_ban", suspend: true);
            return;
        }
        if (state.universal_suspended)
        {
            ResumeUniversal(empire);
            return;
        }
        Actor emperor = empire.Emperor;
        if (emperor == null) return;
        if (state.universal_emperor_id != emperor.id)
        {
            if (PapacySystem.IsCrowned(empire) && !state.self_crowned)
            {
                state.universal_emperor_id = emperor.id;
                return;
            }
            double since = empire.data.newEmperor_timestamp;
            if (since >= 0d && World.world.getCurWorldTime() >= since && RealmNameRules.UncrownedTooLong(Date.getYearsSince(since)))
                RevokeUniversal(empire, "realm_universal_reason_uncrowned");
            return;
        }
        // 同一位皇帝在位时国号被别处改过(分立、篡位改号之类)：普世之名作罢
        if (!string.IsNullOrWhiteSpace(state.universal_name) &&
            !string.Equals(empire.data.core_name, state.universal_name, StringComparison.Ordinal))
            RevokeUniversal(empire, "realm_universal_reason_renamed");
    }

    // 好感修正 opinion_coerced_coronation：教廷对逼冕(施压、开战)的帝国核心王国好感下降，二十年回复
    public static int CoercionOpinion(Kingdom from, Kingdom to)
    {
        Empire empire = to?.GetEmpire();
        if (empire?.data == null || empire.CoreKingdom != to || from == null) return 0;
        PapalRelationState state = Papal(empire);
        if (state == null || state.coerced_at < 0d) return 0;
        if (PapacySystem.Papacy(PapacySystem.ReligionOf(empire)) != from) return 0;
        float years = Date.getYearsSince(state.coerced_at);
        return years >= CoercionGrudgeYears ? 0 : -(int)(CoercionGrudge * (1f - years / CoercionGrudgeYears));
    }

    // 诉求「逼冕」的条件(TempFac_逼冕)：西方、联合天下里未取普世之名、天下没有别的普世皇帝、教廷好感不正、
    // 教廷不在本帝国手里、没被绝罚、没脱离罗马、国力胜过教廷、接壤或水师可达、十年内没逼冕失败过
    public static bool WantsCoercion(Empire empire, out Kingdom papacy)
    {
        papacy = null;
        if (!Enabled || empire?.data == null || empire.CoreKingdom == null || empire.Emperor == null) return false;
        if (!IsWestern(empire) || !PapacySystem.Applies(empire) || PapacySystem.IsHolySee(empire)) return false;
        PapalRelationState state = Papal(empire);
        if (state == null || state.universal_lead_id >= 0) return false;
        if (state.coerce_failed_at >= 0d && Date.getYearsSince(state.coerce_failed_at) < CoerceCooldownYears) return false;
        EmpireCore lead = UniversalLead(empire);
        if (lead == null || !TianxiaSystem.IsJoint(lead) || OtherUniversal(lead, empire) != null) return false;
        if (PapacySystem.IsExcommunicated(empire) || PapacySystem.BrokeWithRome(empire)) return false;
        papacy = PapacySystem.Papacy(PapacySystem.ReligionOf(empire));
        if (papacy == null || papacy.isRekt() || ControlsPapacy(empire, papacy)) return false;
        if (Opinion(papacy, empire.CoreKingdom) > 0 || empire.CoreKingdom.isInWarWith(papacy)) return false;
        if (WarSituation.Power(empire) <= WarSituation.Power(papacy)) return false;
        if (!empire.IsNeighbourWith(papacy) && !StraitReach.Across(empire.CoreKingdom, papacy)) return false;
        return WarGate.CanDeclare(empire.CoreKingdom, papacy);
    }

    // 逼冕得手(打下教廷都城或圣城，或战胜)：解绝罚、教宗加冕、授名
    public static void CoerceVictory(Empire empire)
    {
        if (!Enabled || empire?.data == null || HoldsUniversal(empire)) return;
        if (PapacySystem.CrownByForce(empire)) TryGrantUniversal(empire, ViaWar);
    }

    // 逼冕之战结束(WarPatch 战争结束时调用)
    public static void ResolveCoerceWar(War war, WarWinner winner)
    {
        Empire empire = war?.getMainAttacker()?.GetEmpire();
        if (empire?.data == null || empire.isRekt() || empire.IsArchived()) return;
        if (winner == WarWinner.Attackers)
        {
            CoerceVictory(empire);
            return;
        }
        if (HoldsUniversal(empire)) return;
        PapalRelationState state = Papal(empire);
        if (state == null) return;
        state.coerce_failed_at = World.world.getCurWorldTime();
        empire.AddMandate(CoerceFailedLegitimacy);
        EventRecorder.Record(empire, string.Format(LM.Get("realm_coerce_failed_history"), empire.GetEmpireFullName(),
            war.getMainDefender()?.GetKingdomName() ?? ""));
    }

    // 逼冕之战攻下城时(CityPatch)：教廷都城或圣城到手即算得手
    public static bool IsCoercionPrize(City city, Empire attacker)
    {
        if (city?.kingdom == null || attacker?.data == null) return false;
        Religion religion = PapacySystem.ReligionOf(attacker);
        Kingdom papacy = PapacySystem.Papacy(religion);
        return papacy != null && city.kingdom == papacy && (papacy.capital == city || PapacySystem.HolyCity(religion) == city);
    }

    // 逼冕诉求的紧迫度(ClaimAgendaSystem)
    public static float CoercionUrgency(Empire empire) => WantsCoercion(empire, out _) ? 12f : 0f;

    // 逼冕诉求挂在中央、攘夷派上(没有就挂当权派)，只给联合天下里还没有普世之名的西方帝国
    private static readonly FactionType[] CoercionHosts = { FactionType.中央, FactionType.攘夷 };

    private static void SyncCoercionClaim(Empire empire)
    {
        List<FixedFaction> factions = empire?.CoreKingdom?.GetRegime()?.GetPlayerFactions();
        if (factions == null) return;
        EmpireCore lead = UniversalLead(empire);
        bool wanted = Enabled && IsWestern(empire) && PapacySystem.Applies(empire) && !PapacySystem.IsHolySee(empire) &&
                      lead != null && TianxiaSystem.IsJoint(lead) && Papal(empire)?.universal_lead_id < 0;
        List<FixedFaction> hosts = factions.Where(f => f != null && CoercionHosts.Contains(f.Type)).ToList();
        if (hosts.Count == 0 && factions.FirstOrDefault(f => f?.Force == true) is FixedFaction dominate) hosts.Add(dominate);
        const TemporaryFactionType type = TemporaryFactionType.逼冕;
        foreach (FixedFaction faction in factions.Where(f => f != null))
        {
            faction.TemporaryFactionTypesRecord ??= new List<TemporaryFactionType>();
            faction.TemporaryFactions ??= new List<TemporaryFaction>();
            TemporaryFaction existing = faction.TemporaryFactions.FirstOrDefault(tf => tf != null && tf.type == type);
            if (wanted && hosts.Contains(faction))
            {
                if (!faction.TemporaryFactionTypesRecord.Contains(type)) faction.TemporaryFactionTypesRecord.Add(type);
                if (existing == null && FactionManager.Config?.StoredTemporaryFaction != null &&
                    FactionManager.Config.StoredTemporaryFaction.TryGetValue(type, out TemporaryFaction prototype))
                {
                    TemporaryFaction claim = prototype.Clone(faction);
                    claim.Init(faction);
                    claim.SetEmpire(empire);
                    faction.TemporaryFactions.Add(claim);
                }
            }
            else if (existing?.IsStarted() != true)
            {
                faction.TemporaryFactionTypesRecord.Remove(type);
                faction.TemporaryFactions.RemoveAll(tf => tf != null && tf.type == type);
            }
        }
    }

    // 上帝视角：直接授、收回(教廷页按钮)
    public static bool DecreeUniversal(Empire empire) => TryGrantUniversal(empire, ViaDecree);

    public static void DecreeRevoke(Empire empire) => RevokeUniversal(empire, "realm_universal_reason_decree");

    // 界面一行：普世之名的状况
    public static string UniversalStatus(Empire empire)
    {
        PapalRelationState state = Papal(empire);
        if (state == null) return "";
        EmpireCore lead = UniversalLead(empire);
        string realm = lead == null ? LM.Get("label_none") : TianxiaSystem.Name(lead);
        if (state.universal_lead_id >= 0)
            return string.Format(LM.Get(state.universal_suspended ? "realm_universal_status_suspended" : "realm_universal_status_held"),
                TianxiaBaseName(lead), LM.Get("realm_universal_via_" + state.universal_via));
        Empire other = OtherUniversal(lead, empire);
        if (other != null) return string.Format(LM.Get("realm_universal_status_other"), other.GetEmpireFullName());
        return string.Format(LM.Get("realm_universal_status_none"), realm);
    }
}

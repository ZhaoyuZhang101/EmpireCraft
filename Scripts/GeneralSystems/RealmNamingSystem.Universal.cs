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
//   · 名字取自天下：教廷所在天下(圣地所属)的基名(西方天下默认"{0}普世帝国"里的 {0})；未经教宗认可只能用本帝国法理名。
//     用户 2026-10-27 改定(截图"神圣日耳曼帝国")：几个天下同奉一教时只有那一个教廷能加冕，它也只能授自己所在天下之名——
//     教廷在罗马天下，受冕的日耳曼皇帝得的是罗马之名(神圣罗马)，不是日耳曼；除了教宗谁也授不了天下之名，
//     日耳曼天下没有教廷，就没人能称日耳曼之名，只能用本帝国法理名(德意志)。华夏只有现代唯一合法政府用天下名(见 Modern)。
//     以前取的是本帝国法理所在天下的基名，旧档里这样授的名每年核对时改授教廷所在天下之名(见 Rehome)；
//   · 按史实重做(用户 2026-11-08："冠以神圣加天下之名的机制要改改，不能哪个国家都要"，查史后按方案改)：
//     罗马之名不是谁强谁拿——800 年查理曼、962 年奥托都是天下没有罗马皇帝时，统一了天下大半、又护着教廷的那一家受教宗加冕
//     ("帝位转移")；此后帝号绑在那个位子上经选举代代承袭，法兰西再强也不争(国王在其国内即皇帝)。所以：
//       ① 承统：天下没有普世皇帝时才授，要同时——堪当(受教廷所在天下的天命，或据城最多且有 CapableShare 以上)、
//          护教(教廷是本帝国核心、成员或附庸，即圣地在本帝国手里)、由教宗加冕(ViaControl)；
//          以前的好感请冕、兵临施压、兴兵逼冕三条路都取消(逼冕诉求不再挂，旧档里还没发动的清掉)；
//       ② 承袭：取得后随帝国走，新君不论世袭、选帝都当即承袭，不待教宗加冕；不再每年收回转授给更强的一家；
//       ③ 空缺：帝国亡了、脱离罗马、不再是西方，或连续 LapseYears 年连 KeepShare 都据不到、又不护教廷(不复为天下之主)，帝位才空出来；
//          绝罚期间照旧暂停(名字退回本名，悔罪、解除后恢复)；天授(剧本开局、上帝视角)的不受③里的年限；
//       ④ 教宗自授：皇帝就是教宗(圣座帝国，或教宗国成了帝国核心，见 PapacySystem.PopeIsEmperor)，天下空缺时教宗自授天下之名
//          (ViaSelf，不看堪当)，国号改称"罗马帝国"之类而不是一直叫"教宗国"；但不称神圣；
//     收回后改回取得前的本名，那本名若也是天下之名(旧规则下得来的)，改用本帝国法理名；
//     教廷页"授普世之名"在天下已有普世皇帝时改为"转授"：从那家收回、授给本帝国(上帝视角)；
//   · "神圣"不是教宗授的：1157 年巴巴罗萨在与教廷争权时才自称神圣帝国(神圣直接来自上帝，不靠教宗)，距 800 年已三百多年。
//     现在持普世之名满 PapacySystem.HolyYears 年、又与教廷正面相争过的，帝国自称神圣(神授君权诉求)，见 PapacySystem.HolyBlocker；
//     教宗加冕不再顺带授神圣。普世之名不排"第几帝国"。
public static partial class RealmNamingSystem
{
    public const int ViaNone = 0, ViaFavor = 1, ViaPressure = 2, ViaWar = 3, ViaControl = 4, ViaDecree = 5, ViaSelf = 6;

    // 挟冕之辱：教廷对逼冕的帝国好感 −30，二十年回复
    private const int CoercionGrudge = 30;
    private const float CoercionGrudgeYears = 20f;
    private const int CoerceFailedLegitimacy = -5;
    // 承统要据天下城的份额(以前 1/4，加上"另择护教之君"年年转授，名号在强国之间换手)；持名之后不复为天下之主的份额与年限
    public const float CapableShare = 1f / 3f, KeepShare = 1f / 6f;
    public const int LapseYears = 30;

    // 堪当普世皇帝：受着这个天下的天命，或是天下里据城最多的一家、且据有 CapableShare 以上(像那个天下的共主)
    public static bool Capable(Empire empire, EmpireCore lead)
    {
        Kingdom core = empire?.CoreKingdom;
        if (core == null || lead == null) return false;
        if (TianxiaSystem.Holder(lead) == core) return true;
        (Dictionary<Kingdom, int> held, int total) = TianxiaSystem.Holdings(lead);
        if (total <= 0 || !held.TryGetValue(core, out int mine)) return false;
        return mine >= total * CapableShare && held.Values.All(count => count <= mine);
    }

    // 持名的仍算天下之主：受着天命、护着教廷，或据有那个天下 KeepShare 以上的城(大空位时代的神罗也还是大国)
    public static bool StillPrimate(Empire empire, EmpireCore lead)
    {
        Kingdom core = empire?.CoreKingdom;
        if (core == null || lead == null) return false;
        if (TianxiaSystem.Holder(lead) == core) return true;
        if (PapacySystem.PopeIsEmperor(empire) || ControlsPapacy(empire, PapacySystem.Papacy(PapacySystem.ReligionOf(empire)))) return true;
        (Dictionary<Kingdom, int> held, int total) = TianxiaSystem.Holdings(lead);
        return total > 0 && held.TryGetValue(core, out int mine) && mine >= total * KeepShare;
    }

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

    // 教廷所在的天下：这个宗教的圣地所属的天下，天下之名只有它能授、也只授它这一个。
    // 按圣地所在算，不看圣地眼下归谁——圣地一时失陷，已授的名不因此作废(失陷期间没有教廷，也就没人加冕、授名)
    // 教派没有自己的教廷，取本宗的(号召力所在，见 ReligionSphere"教派与圣地")
    public static EmpireCore SeeLead(Religion religion)
    {
        City see = PapacySystem.HolyCity(ReligionSphere.Root(religion));
        return see == null ? null : TianxiaSystem.LeadOf(see);
    }

    public static EmpireCore SeeLead(Empire empire) => SeeLead(PapacySystem.ReligionOf(empire));

    // 国号取自的天下：普世之名记的那个，现代唯一合法政府记的那个
    public static EmpireCore NamedTianxia(Empire empire)
    {
        PapalRelationState state = Papal(empire);
        long id = state != null && state.universal_lead_id >= 0 ? state.universal_lead_id
            : empire?.data?.constitutional_economy?.named_after_tianxia_lead_id ?? -1L;
        return id >= 0 ? EmpireCoreManager.Get(id) : UniversalLead(empire);
    }

    // "某某人的皇帝"里的某某(神圣之名的史书用，见 PapacySystem.GrantHoly)：教廷所在天下的基名，取不到用本帝国国号
    public static string PeopleName(Empire empire)
    {
        EmpireCore lead = SeeLead(empire);
        string baseName = lead == null ? null : TianxiaBaseName(lead);
        return string.IsNullOrWhiteSpace(baseName) ? empire.GetEmpireName() : baseName;
    }

    // 收回后改回的名：取得前的本名；那本名若也是某个天下之名(旧规则下授过别的天下之名、又换过的)，改用本帝国法理名
    private static string NameBeforeUniversal(Empire empire)
    {
        string own = CoreName(EmpireCoreManager.Get(empire));
        string before = empire.data.pre_tianxia_name;
        if (string.IsNullOrWhiteSpace(before)) return own;
        if (string.IsNullOrWhiteSpace(own) || string.Equals(before, own, StringComparison.Ordinal)) return before;
        return TianxiaSystem.Leads().Any(lead => string.Equals(TianxiaBaseName(lead), before, StringComparison.Ordinal)) ? own : before;
    }

    // 同一天下里另一位未失格的普世皇帝
    public static Empire OtherUniversal(EmpireCore lead, Empire except)
    {
        if (lead == null || ModClass.EMPIRE_MANAGER == null) return null;
        return ModClass.EMPIRE_MANAGER.FirstOrDefault(other => other?.data != null && other != except && !other.isRekt() &&
                                                               !other.IsArchived() && HoldsUniversal(other) &&
                                                               Papal(other).universal_lead_id == lead.id);
    }

    // 控制教宗国：教廷是本帝国的成员(含教廷之囚落在本帝国)或附庸(圣座帝国不算)
    public static bool ControlsPapacy(Empire empire, Kingdom papacy)
    {
        if (empire?.CoreKingdom == null || papacy == null || PapacySystem.IsHolySee(empire)) return false;
        return papacy == empire.CoreKingdom || papacy.GetEmpire() == empire ||
               EmpireCoreControl.Sovereign(papacy) == empire.CoreKingdom;
    }

    // 教宗加冕时取得普世之名的途径(PapacySystem.Coronation 调用；不够格返回 ViaNone)：只有护教的(教廷在本帝国手里)才承统，
    // 好感请冕、兵临施压不再算(见文件头①)；皇帝就是教宗的自授(④)
    public static int CoronationVia(Empire empire, Kingdom papacy, bool romzugArrived)
    {
        if (!Enabled || !IsWestern(empire) || papacy == null) return ViaNone;
        if (PapacySystem.PopeIsEmperor(empire)) return ViaSelf;
        return ControlsPapacy(empire, papacy) ? ViaControl : ViaNone;
    }

    // 教宗为这位皇帝加冕之后(不含自行加冕)：已有普世之名的记下新君；没有的按途径承统。神圣不再随加冕授(见文件头)
    public static void OnPapalCoronation(Empire empire, int via)
    {
        if (!Enabled || empire?.data == null || empire.Emperor == null) return;
        PapalRelationState state = Papal(empire);
        if (state == null) return;
        if (state.universal_lead_id >= 0) state.universal_emperor_id = empire.Emperor.id;
        else TryGrantUniversal(empire, via);
    }

    // 授普世之名；quiet = 读档追授，只记史册不上公告。只有护教承统(ViaControl)、教宗自授(ViaSelf)与天授(ViaDecree)三途
    public static bool TryGrantUniversal(Empire empire, int via, bool quiet = false)
    {
        if (via != ViaControl && via != ViaDecree && via != ViaSelf || !Enabled || empire?.data == null || empire.CoreKingdom == null ||
            !IsWestern(empire)) return false;
        // 只有那个唯一的教廷授得了天下之名，别的教宗国授不了(见 PapacySystem.TheChurch)
        if (PapacySystem.ReligionOf(empire) != PapacySystem.TheChurch()) return false;
        PapalRelationState state = Papal(empire);
        EmpireCore lead = SeeLead(empire);
        if (state == null || lead == null) return false;
        if (state.universal_lead_id >= 0 && !state.universal_suspended) return false;
        if (OtherUniversal(lead, empire) != null) return false;
        // 教宗只授堪当的(天授、教宗自授不论)
        if (via == ViaControl && !Capable(empire, lead)) return false;
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
        state.universal_unfit_since = -1d;
        // 教廷所在天下的基名与本帝国法理名一样时名字不变，只记身份
        bool renamed = !string.Equals(before, empire.GetEmpireFullName(), StringComparison.Ordinal);
        string text = string.Format(LM.Get(via == ViaSelf ? "realm_universal_self_history"
                : renamed ? "realm_universal_granted_history" : "realm_universal_recognized_history"),
            PapacySystem.Pope(religion)?.getName() ?? LM.Get("label_none"), empire.Emperor?.getName() ?? "",
            before, empire.GetEmpireFullName(), TianxiaSystem.Name(lead), LM.Get("realm_universal_via_" + via));
        if (quiet) empire.RecordHistory(directContent: text, actorId: empire.Emperor?.id ?? -1L, kingdomId: empire.CoreKingdom.id);
        else RecordRename(empire, text);
        LogService.LogInfo($"[EmpireCraft][普世之名] {before} 经{via}得 {baseName}，称 {empire.GetEmpireFullName()}");
        return true;
    }

    // 剧本开局即持某个天下的普世之名(世界地图：十字军、临高两档的神圣罗马)：不经教宗加冕，按天授记，不上公告
    public static void GrantUniversalAtStart(Empire empire, EmpireCore lead)
    {
        PapalRelationState state = Papal(empire);
        string baseName = lead == null ? null : TianxiaBaseName(lead);
        if (state == null || empire.CoreKingdom == null || string.IsNullOrWhiteSpace(baseName)) return;
        if (empire.data.name_source != SourceTianxia) empire.data.pre_tianxia_name = empire.data.core_name ?? "";
        state.universal_lead_id = lead.id;
        state.universal_via = ViaDecree;
        state.universal_at = World.world.getCurWorldTime();
        state.universal_emperor_id = empire.Emperor?.id ?? -1L;
        state.universal_suspended = false;
        empire.data.name_source = SourceTianxia;
        empire.SetEmpireName(baseName, preserveEmpireType: true);
        state.universal_name = empire.data.core_name ?? "";
    }

    // 收回(suspend = 绝罚暂停，悔罪后恢复)
    public static void RevokeUniversal(Empire empire, string reasonKey, bool suspend = false)
    {
        PapalRelationState state = Papal(empire);
        if (state == null || state.universal_lead_id < 0) return;
        if (suspend && state.universal_suspended) return;
        string before = empire.GetEmpireFullName();
        string restore = NameBeforeUniversal(empire);
        if (suspend) state.universal_suspended = true;
        else
        {
            state.universal_lead_id = -1L;
            state.universal_via = ViaNone;
            state.universal_emperor_id = -1L;
            state.universal_suspended = false;
            state.universal_unfit_since = -1d;
        }
        empire.data.name_source = empire.data.named_core_id >= 0 ? SourceEmpireTitle : SourceLegacy;
        if (!string.IsNullOrWhiteSpace(restore)) empire.SetEmpireName(restore, preserveEmpireType: true);
        string text = string.Format(LM.Get(suspend ? "realm_universal_suspended_history" : "realm_universal_revoked_history"),
            before, empire.GetEmpireFullName(), LM.Get(reasonKey));
        // 绝罚暂废、悔罪恢复常常来回好几次，照旧只按帝国大小上屏；真失了普世之名是改国号的大事
        if (suspend) EventRecorder.Record(empire, text);
        else RecordRename(empire, text);
        // 神圣依附于普世之名，一并去掉(见 PapacySystem.HolyBlocker)
        if (PapacySystem.HasHolyName(empire)) PapacySystem.RevokeHoly(empire);
        LogService.LogInfo($"[EmpireCraft][普世之名] {before} 失普世之名({reasonKey}{(suspend ? "，暂停" : "")})，称 {empire.GetEmpireFullName()}");
    }

    // 绝罚解除后恢复
    private static void ResumeUniversal(Empire empire)
    {
        PapalRelationState state = Papal(empire);
        EmpireCore lead = SeeLead(empire);
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
    // 本局已核过追授的帝国(每个只核一次：之后只在教宗加冕时授)
    private static readonly HashSet<long> RetroChecked = new();

    // 每年(RealmNamingSystem.Yearly)
    private static void YearlyUniversal(Empire empire)
    {
        // 没有普世之名还挂着神圣的(旧档、别处授的)、不是那个唯一教会的、多出来的：神圣去掉(见 PapacySystem.HolyBlocker、HolyStays)
        if (Enabled && PapacySystem.HasHolyName(empire) && (!HoldsUniversal(empire) || !PapacySystem.HolyStays(empire)))
            PapacySystem.RevokeHoly(empire);
        PapalRelationState state = Papal(empire);
        if (state == null || !IsWestern(empire) && state.universal_lead_id < 0) return;
        if (!ReferenceEquals(_session, World.world))
        {
            _session = World.world;
            _sessionStart = World.world.getCurWorldTime();
            RetroChecked.Clear();
        }
        if (state.universal_lead_id < 0)
        {
            // 皇帝就是教宗：天下空缺就自授(不必等下一任教宗加冕)
            if (PapacySystem.PopeIsEmperor(empire) && !PapacySystem.IsExcommunicated(empire) && TryGrantUniversal(empire, ViaSelf)) return;
            // 读档追授：本局之前就由教宗加冕、仍在位、没有被绝罚、护着教廷的(每个帝国只核一次)
            if (RetroChecked.Add(empire.data.id) && state.crowned_at >= 0d && state.crowned_at < _sessionStart && !state.self_crowned &&
                PapacySystem.IsCrowned(empire) && !PapacySystem.IsExcommunicated(empire) && !PapacySystem.BrokeWithRome(empire))
                TryGrantUniversal(empire, CoronationVia(empire, PapacySystem.Papacy(PapacySystem.ReligionOf(empire)), false), quiet: true);
            return;
        }
        if (!IsWestern(empire))
        {
            RevokeUniversal(empire, "realm_universal_reason_left");
            return;
        }
        if (PapacySystem.BrokeWithRome(empire))
        {
            RevokeUniversal(empire, "realm_universal_reason_rome");
            return;
        }
        EmpireCore see = SeeLead(empire);
        // 天授的名：教廷暂时不在(圣地毁弃、所奉之教没有圣地)不收回；教廷换了天下照旧改授
        if (see == null ? state.universal_via != ViaDecree : see.id != state.universal_lead_id)
        {
            if (!Rehome(empire, state, see)) RevokeUniversal(empire, "realm_universal_reason_see");
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
        // 新君当即承袭，不论世袭、选帝，不待教宗加冕(帝号绑在这个位子上，见文件头②)
        if (state.universal_emperor_id != emperor.id)
        {
            state.universal_emperor_id = emperor.id;
            return;
        }
        // 同一位皇帝在位时国号被别处改过(分立、篡位改号之类)：普世之名作罢；同名相争让出的不算(见 RealmNamingSystem.Contest)
        if (!empire.data.title_claim_yielded && !string.IsNullOrWhiteSpace(state.universal_name) &&
            !string.Equals(empire.data.core_name, state.universal_name, StringComparison.Ordinal))
        {
            RevokeUniversal(empire, "realm_universal_reason_renamed");
            return;
        }
        if (state.universal_via != ViaDecree) CheckLapse(empire, state);
    }

    // 帝位空缺(见文件头③)：连续 LapseYears 年不复为天下之主(StillPrimate)才收回；不再由教宗年年收回、转授更强的一家
    private static void CheckLapse(Empire empire, PapalRelationState state)
    {
        if (StillPrimate(empire, SeeLead(empire)))
        {
            state.universal_unfit_since = -1d;
            return;
        }
        double now = World.world.getCurWorldTime();
        if (state.universal_unfit_since < 0d || state.universal_unfit_since > now)
        {
            state.universal_unfit_since = now;
            return;
        }
        if (Date.getYearsSince(state.universal_unfit_since) >= LapseYears) RevokeUniversal(empire, "realm_universal_reason_lapsed");
    }

    // 普世之名记的天下不是教廷所在的天下：旧档里按本帝国所在天下授的(以前的规则)，或天下分合、圣地改换了天下。
    // 没被绝罚、教廷所在天下之名没归别家的，改授教廷所在天下之名(取得途径照旧)；做不到的由调用方收回
    private static bool Rehome(Empire empire, PapalRelationState state, EmpireCore see)
    {
        if (see == null || PapacySystem.IsExcommunicated(empire) || OtherUniversal(see, empire) != null) return false;
        string baseName = TianxiaBaseName(see);
        if (string.IsNullOrWhiteSpace(baseName)) return false;
        string before = empire.GetEmpireFullName();
        state.universal_lead_id = see.id;
        state.universal_suspended = false;
        empire.data.name_source = SourceTianxia;
        empire.SetEmpireName(baseName, preserveEmpireType: true);
        state.universal_name = empire.data.core_name ?? "";
        if (string.Equals(before, empire.GetEmpireFullName(), StringComparison.Ordinal)) return true;
        RecordRename(empire, string.Format(LM.Get("realm_universal_rehomed_history"), before, TianxiaSystem.Name(see),
            empire.GetEmpireFullName()));
        LogService.LogInfo($"[EmpireCraft][普世之名] {before} 改授教廷所在的 {TianxiaSystem.Name(see)} 之名，称 {empire.GetEmpireFullName()}");
        return true;
    }

    // 得、失、改授普世之名：国号变了，记进当朝史册，不论帝国大小都上屏(用户 2026-10-27 "怎么突然就变成罗马帝国了"：
    // 以前按帝国大小上屏，同一种提示两秒只上一条、屏上至多五条，加速时被别的提示顶掉，改了国号却没看到)
    private static void RecordRename(Empire empire, string text)
    {
        if (empire?.data == null || empire.isRekt()) return;
        empire.RecordHistory(directContent: text, actorId: empire.Emperor?.id ?? -1L, kingdomId: empire.CoreKingdom?.id ?? -1L);
        if (empire.CoreKingdom != null) EmpireCraft.Scripts.HelperFunc.TranslateHelper.LogMajor(text, empire.CoreKingdom);
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

    // 诉求「逼冕」(TempFac_逼冕)已取消(用户 2026-11-08 按史实重做：天下之名不是谁兵强谁逼来的，见文件头①)，不再提；
    // 旧档里还没发动的由 SyncCoercionClaim 清掉，已开打的照旧打完
    public static bool WantsCoercion(Empire empire, out Kingdom papacy)
    {
        papacy = null;
        return false;
    }

    // 旧档里的逼冕之战得手：解绝罚、教宗加冕(只加冕，不再因此授名)
    public static void CoerceVictory(Empire empire)
    {
        if (!Enabled || empire?.data == null || HoldsUniversal(empire)) return;
        PapacySystem.CrownByForce(empire);
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

    // 逼冕诉求挂在中央、攘夷派上(没有就挂当权派)，只给还没有普世之名、得了会改名的西方帝国
    private static readonly FactionType[] CoercionHosts = { FactionType.中央, FactionType.攘夷 };

    private static void SyncCoercionClaim(Empire empire)
    {
        List<FixedFaction> factions = empire?.CoreKingdom?.GetRegime()?.GetPlayerFactions();
        if (factions == null) return;
        // 逼冕已取消：只清掉旧档挂着、还没发动的
        bool wanted = false;
        // 不该有、也没挂过的(非西方帝国大多如此)：不动派系
        if (!wanted && !factions.Any(f => f?.TemporaryFactionTypesRecord?.Contains(TemporaryFactionType.逼冕) == true ||
                                          f?.TemporaryFactions?.Any(tf => tf?.type == TemporaryFactionType.逼冕) == true)) return;
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
    // 教廷页"授普世之名"：天下已有别的普世皇帝时转授(从那家收回、授给本帝国)
    public static bool DecreeUniversal(Empire empire)
    {
        Empire holder = Holder(empire);
        if (holder != null && (!Enabled || !IsWestern(empire) || PapacySystem.BrokeWithRome(empire)))
            return false;
        if (holder != null) RevokeUniversal(holder, "realm_universal_reason_transferred");
        return TryGrantUniversal(empire, ViaDecree);
    }

    // 教廷所在天下眼下的另一位普世皇帝(没有为空)
    public static Empire Holder(Empire empire) => empire?.data == null ? null : OtherUniversal(SeeLead(empire), empire);

    public static void DecreeRevoke(Empire empire) => RevokeUniversal(empire, "realm_universal_reason_decree");

    // 界面一行：普世之名的状况
    public static string UniversalStatus(Empire empire)
    {
        PapalRelationState state = Papal(empire);
        if (state == null) return "";
        EmpireCore lead = state.universal_lead_id >= 0 ? EmpireCoreManager.Get(state.universal_lead_id) : SeeLead(empire);
        string realm = lead == null ? LM.Get("label_none") : TianxiaSystem.Name(lead);
        if (state.universal_lead_id >= 0)
            return string.Format(LM.Get(state.universal_suspended ? "realm_universal_status_suspended" : "realm_universal_status_held"),
                TianxiaBaseName(lead), LM.Get("realm_universal_via_" + state.universal_via));
        Empire other = OtherUniversal(lead, empire);
        if (other != null) return string.Format(LM.Get("realm_universal_status_other"), other.GetEmpireFullName());
        return string.Format(LM.Get("realm_universal_status_none"), realm);
    }
}

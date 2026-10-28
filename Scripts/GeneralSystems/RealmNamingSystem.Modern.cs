using System;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 现代：华夏的唯一合法政府以天下之名为国号(方案第 7 步，用户 2026-10-20 拍板；10-29 "现代制度下华夏要用天下名称作国号，
// 不再依赖帝国法理""唯一中央政府也只看天下")。
//   · 唯一合法政府：现代政体或改制共和的华夏政权，是本天下的中央(军阀时期以天下为单位，见 WarlordEraSystem)，
//     或天下里没有别的已组建的现代政府；不再要受天命；
//   · 国号：天下基名 + 执政理念后缀(后缀照原规则)，不再用帝国法理名；军阀时期的中央直接按天下名显示(见 ModernRealmName)；
//   · 失去唯一地位：只要仍是本天下的中央就留着天下名；丢了天命、中央之位又被别家取代，才改回原名(帝国法理名)；
//     与之对立的临时政府据法统过半时以天下名自居并括注都城(见 WarlordEraSystem.ProvisionalSeat)；
//   · 现代国号本就不带方位前缀；复辟帝制自然称"{天下基名}帝国"。
public static partial class RealmNamingSystem
{
    private static bool IsModernGovernment(Empire empire)
    {
        if (empire?.data == null || empire.isRekt() || empire.IsArchived() || empire.CoreKingdom == null) return false;
        return empire.CoreKingdom.GetRegime()?.type == RegimeType.Modern || RepublicSystem.IsRepublic(empire);
    }

    public static bool NamedAfterTianxia(Empire empire) =>
        empire?.data?.constitutional_economy?.named_after_tianxia_lead_id >= 0;

    // 唯一合法政府：是本天下的中央(不是临时政府)，或天下里没有别的已组建的现代政府
    public static bool SoleGovernment(Empire empire, EmpireCore lead)
    {
        if (!Enabled || lead == null || !IsHuaxia(empire) || !IsModernGovernment(empire)) return false;
        if (WarlordEraSystem.CentralOf(lead) == empire) return !WarlordEraSystem.IsProvisionalGovernment(empire);
        return ModClass.EMPIRE_MANAGER?.Any(other => other != empire && IsModernGovernment(other) &&
                                                     other.CoreKingdom.capital != null &&
                                                     TianxiaSystem.LeadOf(other.CoreKingdom.capital) == lead) != true;
    }

    // 华夏现代政府以天下为号时用的天下基名(中央、以法统自居的临时政府)；不是华夏现代政府返回 null
    public static string ModernRealmName(Empire empire, EmpireCore anyCore)
    {
        if (!Enabled || anyCore == null || !IsHuaxia(empire) || !IsModernGovernment(empire)) return null;
        string name = TianxiaBaseName(anyCore);
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    // 现代中央以帝国法理名作国号时(WarlordEraSystem.CentralStyleName)：已用天下名的用天下基名
    public static string ModernBaseName(Empire empire)
    {
        if (!Enabled || !NamedAfterTianxia(empire)) return null;
        string name = empire.data.core_name;
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    // 临时政府以法统自居时的名号(WarlordEraSystem.ProvisionalSeat)：这个天下眼下有政府以天下名为号的，也以天下名自居
    public static string NationalName(EmpireCore lawful)
    {
        if (!Enabled || lawful == null || ModClass.EMPIRE_MANAGER == null) return null;
        EmpireCore lead = TianxiaSystem.Lead(lawful);
        if (lead == null) return null;
        bool used = ModClass.EMPIRE_MANAGER.Any(other => other?.data?.constitutional_economy != null && !other.isRekt() &&
                                                          !other.IsArchived() &&
                                                          other.data.constitutional_economy.named_after_tianxia_lead_id == lead.id);
        return used ? TianxiaBaseName(lead) : null;
    }

    // 读档后(换世界后)头一年：改名只记史册，不上世界公告(仿天命首年，见 TianxiaSystem)
    private static object _modernSession;
    private static double _modernSessionStart = -1d;

    private static bool QuietYear()
    {
        if (!ReferenceEquals(_modernSession, World.world))
        {
            _modernSession = World.world;
            _modernSessionStart = World.world.getCurWorldTime();
        }
        return Date.getYearsSince(_modernSessionStart) < 1f;
    }

    private static void RecordName(Empire empire, string text, bool quiet)
    {
        if (quiet) empire.RecordHistory(directContent: text, actorId: empire.Emperor?.id ?? -1L, kingdomId: empire.CoreKingdom?.id ?? -1L);
        else EventRecorder.Record(empire, text);
    }

    // 唯一合法政府改称天下基名(每年；组建政府、统一天下时当即，见 ModernStateFormationSystem.FormGovernment、
    // WarlordEraSystem.CompleteUnification)：改了返回 true
    public static bool ClaimModernRealmName(Empire empire, bool? quiet = null)
    {
        var state = empire?.data?.constitutional_economy;
        if (!Enabled || state == null || !IsHuaxia(empire) || empire.data.title_claim_yielded ||
            state.named_after_tianxia_lead_id >= 0) return false;
        EmpireCore own = EmpireCoreManager.Get(empire);
        EmpireCore lead = own == null ? null : TianxiaSystem.Lead(own);
        if (!SoleGovernment(empire, lead) || empire.CoreKingdom.HasCustomCountryNaming()) return false;
        string baseName = TianxiaBaseName(lead);
        if (string.IsNullOrWhiteSpace(baseName)) return false;
        string before = empire.GetEmpireFullName();
        empire.data.pre_tianxia_name = empire.data.core_name ?? "";
        state.named_after_tianxia_lead_id = lead.id;
        empire.data.name_source = SourceTianxia;
        empire.SetEmpireName(baseName, preserveEmpireType: true);
        if (string.Equals(before, empire.GetEmpireFullName(), StringComparison.Ordinal)) return true;
        RecordName(empire, string.Format(LM.Get("realm_tianxia_government_history"), before,
            TianxiaSystem.Name(lead), empire.GetEmpireFullName()), quiet ?? QuietYear());
        LogService.LogInfo($"[EmpireCraft][天下国号] {before} 为 {TianxiaSystem.Name(lead)} 唯一合法政府，改称 {empire.GetEmpireFullName()}");
        return true;
    }

    // 每年(RealmNamingSystem.Yearly)
    private static void YearlyModern(Empire empire)
    {
        var state = empire.data.constitutional_economy;
        if (state == null || !IsHuaxia(empire)) return;
        // 同名相争里让出了名号的不再改称天下基名(以前改回、再撞名、再让出，每年一轮，同普世之名，见 Contest)
        if (empire.data.title_claim_yielded) return;
        EmpireCore own = EmpireCoreManager.Get(empire);
        EmpireCore lead = own == null ? null : TianxiaSystem.Lead(own);
        bool quiet = QuietYear();
        if (state.named_after_tianxia_lead_id < 0)
        {
            ClaimModernRealmName(empire, quiet);
            return;
        }
        // 已用天下名：仍是本天下的中央、或仍受着天命就留着；改了政体、离了那个天下、丢了天命又失了中央之位才改回
        bool sameRealm = lead != null && lead.id == state.named_after_tianxia_lead_id;
        bool mandate = sameRealm && TianxiaSystem.MandateOf(empire.CoreKingdom) == lead;
        bool central = lead != null && (lead.central_empire_id <= 0 || lead.central_empire_id == empire.id);
        if (Enabled && sameRealm && IsModernGovernment(empire) && (mandate || central))
        {
            // 天下基名改了(天下窗口里改基名)：跟着改
            string baseName = TianxiaBaseName(lead);
            if (!string.IsNullOrWhiteSpace(baseName) && SoleGovernment(empire, lead) &&
                !string.Equals(empire.data.core_name, baseName, StringComparison.Ordinal))
                empire.SetEmpireName(baseName, preserveEmpireType: true);
            return;
        }
        if (!Enabled) return;
        string previous = empire.GetEmpireFullName();
        string restore = !string.IsNullOrWhiteSpace(empire.data.pre_tianxia_name) ? empire.data.pre_tianxia_name : CoreName(own);
        state.named_after_tianxia_lead_id = -1L;
        empire.data.name_source = empire.data.named_core_id >= 0 ? SourceEmpireTitle : SourceLegacy;
        if (!string.IsNullOrWhiteSpace(restore)) empire.SetEmpireName(restore, preserveEmpireType: true);
        RecordName(empire, string.Format(LM.Get("realm_tianxia_government_lost_history"), previous, empire.GetEmpireFullName()), quiet);
        LogService.LogInfo($"[EmpireCraft][天下国号] {previous} 不再是唯一合法政府，改回 {empire.GetEmpireFullName()}");
    }
}

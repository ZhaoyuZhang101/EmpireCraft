using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 天下之名的界面文字：帝国窗口档案栏的国号来源、各京与第二直隶；天下窗口改基名
public static partial class RealmNamingSystem
{
    // 国号来源一行(旧规则定的不显示)
    public static string NameSourceLine(Empire empire)
    {
        if (empire?.data == null) return "";
        if (empire.data.title_claim_yielded)
            return string.Format(LM.Get("realm_name_source_yielded"), empire.data.title_claim_name);
        switch (empire.data.name_source)
        {
            case SourceEmpireTitle:
            {
                string core = CoreName(EmpireCoreManager.Get(empire.data.named_core_id));
                return string.IsNullOrWhiteSpace(core) ? "" : string.Format(LM.Get("realm_name_source_title"), core);
            }
            case SourceRestoration:
                return string.Format(LM.Get("realm_name_source_restored"), empire.GetEmpireName(),
                    LM.Get(string.IsNullOrWhiteSpace(empire.data.founding_prefix) ? RealmNameRules.Later : empire.data.founding_prefix));
            case SourceGrandName:
                return LM.Get("realm_name_source_grand");
            case SourceRebelBanner:
                return string.Format(LM.Get("realm_name_source_banner"), empire.GetEmpireName());
            case SourceUsurper:
                return LM.Get("realm_name_source_usurper");
            case SourceTianxia:
            {
                EmpireCore lead = NamedTianxia(empire);
                return lead == null ? "" : string.Format(LM.Get("realm_name_source_tianxia"), TianxiaSystem.Name(lead));
            }
            default:
                return "";
        }
    }

    // 各京一行：京师 某城 · 东京 某城……
    public static string JingLine(Empire empire)
    {
        List<Data.JingRecord> records = empire?.data?.jing;
        if (records == null || records.Count == 0) return "";
        IEnumerable<string> parts = records.Where(record => record != null).Select(record =>
            $"{MultiCapitalSystem.JingName(record)} {World.world?.cities?.get(record.city_id)?.GetCityName() ?? ""}");
        return string.Format(LM.Get("realm_jing_line"), string.Join(" · ", parts));
    }

    // 第二直隶一行：国名(储君名 / 空缺)
    public static string ZhiliLine(Empire empire)
    {
        long id = empire?.data?.zhili_kingdom_id ?? -1L;
        Kingdom zhili = id < 0 ? null : World.world?.kingdoms?.get(id);
        if (zhili == null || !MultiCapitalSystem.IsZhili(zhili)) return "";
        string ruler = zhili.hasKing() ? zhili.king.getName() : LM.Get("realm_zhili_vacant");
        return string.Format(LM.Get("realm_zhili_line"), zhili.GetKingdomFullName(), ruler);
    }

    // 天下窗口改天下基名：已以天下之名为号的(普世皇帝、唯一合法政府)跟着改
    public static void SetTianxiaBase(EmpireCore anyCore, string value)
    {
        EmpireCore lead = TianxiaSystem.Lead(anyCore);
        if (lead == null) return;
        lead.tianxia_base_name = value?.Trim() ?? "";
        string baseName = TianxiaBaseName(lead);
        if (string.IsNullOrWhiteSpace(baseName) || ModClass.EMPIRE_MANAGER == null) return;
        foreach (Empire empire in ModClass.EMPIRE_MANAGER.ToList())
        {
            if (empire?.data == null || empire.isRekt() || empire.IsArchived()) continue;
            var papal = PapacySystem.State(empire);
            bool universal = papal != null && papal.universal_lead_id == lead.id && !papal.universal_suspended;
            bool modern = empire.data.constitutional_economy?.named_after_tianxia_lead_id == lead.id;
            if (!universal && !modern) continue;
            empire.SetEmpireName(baseName, preserveEmpireType: true);
            if (universal) papal.universal_name = empire.data.core_name ?? "";
        }
    }
}

using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 华夏朝代演进下的行政区划与地方官称谓(阶段见 CourtSystem.Stage)，依历代实制：
//   周制：京畿就是本国，写本国国名；城称"邑"，长官邑宰；
//   郡县(秦汉魏晋)：京畿为"司州"；地方行政区为"州"，长官刺史；城称"郡"，长官太守；
//           军府长官都督(都督诸州军事)，都护府长官都护；羁縻地为"属国"，长官属国都尉；
//           禁军与镇戍：羽林中郎将、都尉、校尉、障尉；
//   三省六部(隋唐)：京畿为"京畿道"；地方为"道"，长官观察使；城称"县"，长官县令，府城府尹；
//           军府节度使、都护府都护、羁縻州刺史；禁军与镇戍沿用羽林大将军、都督、镇将、戍主；
//   内阁(明)：京畿为"直隶省"；地方为"省"(承宣布政使司)，长官布政使；城称"县"，长官知县，府城知府；
//           军府长官总兵(镇守总兵官)，都护府改为"都司"、长官都指挥使；羁縻地为"宣慰司"，长官宣慰使；
//           禁军与镇戍：京营提督、都指挥使、参将、守备。
// 只改称谓(国名后缀、城名后缀、官名、地图标签)，不改国家类别与官职本身。
public static class DynastyNaming
{
    // 名称每帧都可能要用，阶段按帝国缓存两秒
    private static readonly Dictionary<long, (float until, HuaxiaCourtStage stage)> Cache = new();

    public static HuaxiaCourtStage StageOf(Empire empire)
    {
        if (empire == null || empire.isRekt()) return HuaxiaCourtStage.None;
        float now = Time.unscaledTime;
        if (Cache.TryGetValue(empire.id, out var hit) && hit.until > now) return hit.stage;
        HuaxiaCourtStage stage = CourtSystem.Stage(empire);
        Cache[empire.id] = (now + 2f, stage);
        return stage;
    }

    public static HuaxiaCourtStage StageOf(Kingdom kingdom) =>
        kingdom?.data == null || !kingdom.IsInEmpire() ? HuaxiaCourtStage.None : StageOf(kingdom.GetEmpire());

    // 国家类别后缀的文本键：律令制的直隶(京畿)与行政区按阶段改称
    // 华夏原始阶段(母系氏族至家天下，用原始政体)：称谓换成本土的部落联盟、邦、方国、共主、方伯、巫祝、族长，城称邑
    public static bool HuaxiaOrigin(Kingdom kingdom) =>
        kingdom?.data != null && !kingdom.isRekt() && (kingdom.GetRegime()?.type == RegimeType.HuaxiaOrigin ||
        kingdom.GetRegime()?.type == RegimeType.Origin && InstitutionSystem.GetCultureLine(CultureService.GetRealmCulture(kingdom)) == "Huaxia");

    // 华夏原始政体的部落联盟(见 RegimeType.HuaxiaOrigin)：称呼换成共主——皇帝→共主、皇室→共主氏族、帝国首府→共主都邑、
    // 索取皇位→争为共主(用户 2026-10-16 选"称呼改成共主")；有 "_huaxia_origin" 译文的键才换
    public static bool HuaxiaOriginEmpire(Empire empire) =>
        empire?.CoreKingdom?.GetRegime()?.type == RegimeType.HuaxiaOrigin;

    public static string Term(Empire empire, string key) =>
        HuaxiaOriginEmpire(empire) && LM.Has(key + "_huaxia_origin") ? key + "_huaxia_origin" : key;

    // 制度线专属称谓：日本线的国家类别、官名在文本键后加 "_japan"(天皇、国司、大名、国……见各政体 OfficialType.csv)
    public static string LineSuffix(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return null;
        // 游牧、阿拉伯只在原始阶段另有称谓(部落、首领、谢赫……见 Origin/OfficialType.csv)，没有译文的键照旧用政体原名
        return InstitutionSystem.GetCultureLine(CultureService.GetRealmCulture(kingdom)) switch
        {
            "Japan" => "_japan",
            "Youmu" => "_youmu",
            "Arabic" => "_arabic",
            _ => null
        };
    }

    // 文化专属称谓(用户 2026-10-19 选"加文化特性与节点过滤")：同一条制度线里个别文化另有叫法——印度走阿拉伯一线，不称哈里发、
    // 苏丹；阿兹特克、林地部族走游牧一线，不称大可汗、斡里朵。有 "<键>_<文化小写>" 译文的先用(见各政体 OfficialType.csv、
    // 制度节点名)；与制度线同名的文化(华夏、罗马、游牧、日本)用的就是线本身的称谓，不另找
    private static readonly Dictionary<string, string> CultureSuffixes = new();

    public static string CultureVariant(string key, string culture)
    {
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(culture)) return null;
        if (!CultureSuffixes.TryGetValue(culture, out string suffix))
        {
            suffix = string.Equals(InstitutionSystem.GetCultureLine(culture), culture, global::System.StringComparison.OrdinalIgnoreCase)
                ? ""
                : "_" + culture.ToLowerInvariant();
            CultureSuffixes[culture] = suffix;
        }
        if (suffix.Length == 0) return null;
        string variant = key + suffix;
        return LM.Has(variant) ? variant : null;
    }

    public static string CultureVariant(Kingdom kingdom, string key) =>
        kingdom?.data == null || kingdom.isRekt() ? null : CultureVariant(key, CultureService.GetRealmCulture(kingdom));

    // 帝国名按部落、部落联盟称的原始政体：华夏，连同从部落起步的游牧、阿拉伯
    public static bool TribalNaming(Kingdom kingdom)
    {
        if (HuaxiaOrigin(kingdom)) return true;
        if (kingdom?.data == null || kingdom.isRekt() || kingdom.GetRegime()?.type != RegimeType.Origin) return false;
        return InstitutionSystem.GetCultureLine(CultureService.GetRealmCulture(kingdom)) is "Youmu" or "Arabic";
    }

    // 帝国名的类别：有共事的部落称部落联盟(见 TribalFissionService.Incorporate)，共事的部落都散了只称部落；进入家天下后称"朝"(夏、商)。
    // 10-26 起单个部落不再是帝国(只是国，国名后缀"部落"见 Origin/OfficialType.csv)，帝国只剩部落联盟；
    // 共事的部落取按月缓存的反查表(见 TribalFissionService.VassalsOf)，帝号每年重取一回也不再扫全部王国
    public static string HuaxiaOriginEmpireKey(Kingdom core) =>
        TribalFissionService.VassalsOf(core?.GetEmpire()).Count == 0 ? "Origin_empire_huaxia_tribe"
        : InstitutionSystem.IsEnacted(CultureService.GetRealmCulture(core), "huaxia_hereditary_kingship")
            ? "Origin_empire_huaxia_dynasty"
            : "Origin_empire_huaxia";

    public static string KingdomTypeKey(Kingdom kingdom, KingdomType type)
    {
        // 元首制：核心改称帝国(见 Empire.IsPrincipate)
        if (type == KingdomType.ClassicalRepublic_centre && kingdom?.GetEmpire() is Empire principate &&
            principate.CoreKingdom == kingdom && principate.IsPrincipate() && LM.Has(type + "_principate"))
            return CultureVariant(kingdom, type + "_principate") ?? type + "_principate";
        if (CultureVariant(kingdom, type.ToString()) is string cultureKey) return cultureKey;
        if (LineSuffix(kingdom) is string lineSuffix && LM.Has(type + lineSuffix)) return type + lineSuffix;
        if (type is KingdomType.Origin_centre or KingdomType.Origin_kingdom && HuaxiaOrigin(kingdom) &&
            LM.Has(type + "_huaxia"))
            return type + "_huaxia";
        string key = type switch
        {
            KingdomType.LvLing_centre => "dynasty_capital",
            KingdomType.LvLing_province => "dynasty_province",
            KingdomType.LvLing_jimizhou => "dynasty_jimi",
            KingdomType.LvLing_duhufu => "dynasty_duhufu",
            KingdomType.LvLing_zhili => "dynasty_zhili",
            _ => null
        };
        if (key == null) return type.ToString();
        string stage = StageSuffix(StageOf(kingdom));
        return stage != null && LM.Has($"{key}_{stage}") ? $"{key}_{stage}" : type.ToString();
    }

    private static string StageSuffix(HuaxiaCourtStage stage) => stage switch
    {
        HuaxiaCourtStage.Zhou => "zhou",
        HuaxiaCourtStage.Han => "han",
        HuaxiaCourtStage.Tang => "tang",
        HuaxiaCourtStage.Ming => "ming",
        _ => null
    };

    // 官名(品级)的文本键：地方官、军府、禁军镇戍按阶段改称；没有对应的历代称谓时用政体原名
    public static string RankKey(RegimeType regime, int officeType, NanoObject meta)
    {
        string key = $"{regime}_officiallevel_{officeType}";
        Kingdom owner = meta switch { Kingdom k => k, City town => town.kingdom, Army army => army.getKingdom(), _ => null };
        // 元首制：帝国核心的首领称元首(见 Empire.IsPrincipate)
        if (officeType == 0 && regime == RegimeType.ClassicalRepublic && owner?.GetEmpire() is Empire ownerEmpire &&
            ownerEmpire.CoreKingdom == owner && ownerEmpire.IsPrincipate() && LM.Has(key + "_principate")) key += "_principate";
        if (CultureVariant(owner, key) is string cultureKey) return cultureKey;
        if (LineSuffix(owner) is string lineSuffix && LM.Has(key + lineSuffix)) return key + lineSuffix;
        if (regime == RegimeType.Origin)
            return owner != null && HuaxiaOrigin(owner) && LM.Has(key + "_huaxia") ? key + "_huaxia" : key;
        if (regime == RegimeType.ZhouFeudalism)
        {
            // 春秋僭越、战国相王：诸侯国君改称王
            if (officeType is >= 5 and <= 8 && meta is Kingdom realm && ZhouEraSystem.IsKing(realm) &&
                realm.GetKingdomType() != KingdomType.ZhouFeudalism_jun)
                return "ZhouFeudalism_officiallevel_wang";
            return officeType == 9 && meta is City && LM.Has(key + "_zhou") ? key + "_zhou" : key;
        }
        if (regime != RegimeType.LvLing || officeType < 6 || officeType > 12) return key;
        City city = meta as City;
        Kingdom kingdom = meta switch
        {
            Kingdom realm => realm,
            City town => town.kingdom,
            Army army => army.getKingdom(),
            _ => null
        };
        string stage = StageSuffix(StageOf(kingdom));
        if (stage == null) return key;
        KingdomType realmType = kingdom?.GetKingdomType() ?? KingdomType.default_country_post;
        string variant = officeType switch
        {
            6 when realmType == KingdomType.LvLing_duhufu => $"{key}_duhufu_{stage}",
            8 when city == null && realmType == KingdomType.LvLing_jimizhou => $"{key}_jimi_{stage}",
            8 when city != null && realmType == KingdomType.LvLing_jimizhou => null,
            8 when city != null && city.kingdom?.capital == city && city.GetCitySuffixKey() == "city_suffix_prefecture" =>
                $"{key}_fu_{stage}",
            _ => $"{key}_{stage}"
        };
        return variant != null && LM.Has(variant) ? variant : key;
    }

    // 城名后缀的文本键；不按朝代改称时返回 null
    public static string CitySuffixKey(City city)
    {
        Kingdom kingdom = city?.kingdom;
        Regime regime = kingdom?.GetRegime();
        if (regime == null) return null;
        if (regime.type == RegimeType.ZhouFeudalism || HuaxiaOrigin(kingdom)) return "city_suffix_yi";
        if (regime.type == RegimeType.LvLing && kingdom.GetKingdomType() != KingdomType.LvLing_jimizhou &&
            StageOf(kingdom) == HuaxiaCourtStage.Han) return "city_suffix_commandery";
        return null;
    }

    // 帝国展开时京畿那一块的地图标签：郡县以后写司州/京畿道/直隶省；周制返回空串(写本国国名)；
    // 没有朝代演进的返回 null(照旧写首都城名)
    public static string CapitalRegionLabel(Kingdom core)
    {
        HuaxiaCourtStage stage = StageOf(core);
        if (stage == HuaxiaCourtStage.Zhou) return "";
        string key = StageSuffix(stage) is string suffix ? "dynasty_capital_" + suffix : null;
        return key != null && LM.Has(key) ? LM.Get(key) : null;
    }
}

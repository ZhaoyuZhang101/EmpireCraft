using System.Collections.Generic;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GodPowers;

// 易俗(用户 2026-10-16："能不能把城市文化自由修改，神力点击一次一键转换对应文化，如果当前城市是这个国家的最后一个城市，
// 那就直接转变整个国家的制度")：每个文化一个按钮，点一座城——
//   · 城里的人物、背景人口、文化比例、地名当即改从这个文化(见 CultureService.ForceCityCulture)；
//   · 是本国最后一座城：国家连同政体、制度一并改从新俗(易俗改制，不看条件，见 CultureReformSystem.Force)；
//   · 开着"举国易俗""举朝易俗"时改整个国家 / 整个帝国(见 RealmCulturePowers)。
public static class CityCulturePower
{
    private const string Prefix = "city_culture_";
    private static readonly Dictionary<string, string> Cultures = new();

    public static string Id(string culture) => Prefix + culture.ToLowerInvariant();

    // 登记各文化的神力，返回 (神力 id, 文化)
    public static List<(string power, string culture)> Init()
    {
        var result = new List<(string, string)>();
        foreach (string culture in OnomasticsRule.ALL_CULTURE_RULE.Keys)
        {
            if (!CultureService.IsValidCulture(culture)) continue;
            string id = Id(culture);
            Cultures[id] = culture;
            // 名称、说明随文化名拼出来(文化是配置里来的，本地化文件里没有逐个的条目)
            string name = culture.GetCultureTranslate();
            LM.AddToCurrentLocale(id, string.Format(LM.Get("city_culture_power_name"), name));
            LM.AddToCurrentLocale(id + "_description", string.Format(LM.Get("city_culture_power_description"), name));
            if (AssetManager.powers.get(id) == null)
                AssetManager.powers.add(new GodPower { id = id, name = id, click_action = Apply });
            result.Add((id, culture));
        }
        return result;
    }

    private static bool Apply(WorldTile tile, string power)
    {
        if (!Cultures.TryGetValue(power ?? "", out string culture)) return false;
        City city = tile?.zone_city;
        if (city?.data == null || city.isRekt())
        {
            WorldTip.showNow(LM.Get("city_culture_none"), false, "top", 3f);
            return false;
        }
        if (RealmCulturePowers.Scope > 0) return RealmCulturePowers.ApplyRealm(city, culture);
        string cultureName = culture.GetCultureTranslate();
        Kingdom kingdom = city.kingdom;
        bool last = kingdom?.data != null && !kingdom.isRekt() && !kingdom.wild && (kingdom.cities?.Count ?? 0) == 1;
        bool same = string.Equals(CultureService.GetMainCulture(city, false), culture, global::System.StringComparison.Ordinal);
        bool realmSame = !last || string.Equals(CultureService.GetRealmCulture(kingdom), culture, global::System.StringComparison.Ordinal);
        if (same && realmSame)
        {
            WorldTip.showNow(string.Format(LM.Get("city_culture_same"), city.GetCityName(), cultureName), false, "top", 3f);
            return false;
        }
        CultureService.ForceCityCulture(city, culture);
        if (last && !realmSame && CultureReformSystem.Force(kingdom, culture))
        {
            WorldTip.showNow(string.Format(LM.Get("city_culture_realm_done"), city.GetCityName(), kingdom.GetKingdomName(), cultureName),
                false, "top", 4f);
            return true;
        }
        WorldTip.showNow(string.Format(LM.Get("city_culture_done"), city.GetCityName(), cultureName), false, "top", 3f);
        return true;
    }
}

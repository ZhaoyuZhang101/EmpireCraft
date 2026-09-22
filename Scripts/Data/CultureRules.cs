using EmpireCraft.Scripts.Enums;
using NeoModLoader.services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using UnityEngine;

namespace EmpireCraft.Scripts.Data;
public class CultureRule
{
    public string name;
    public string translate_ch;
    public string translate_cz;
    public string translate_en;
    // 文化地图图层（图层染色/悬停高亮）使用的预制颜色，"#RRGGBB" 十六进制格式。
    // 不配置时 GetCultureColor 会退化成按文化名哈希取一个固定但随意的颜色，
    // 不会报错，但同名文化每次读表颜色都一样（哈希是确定性的）。
    public string color;
    public Setting setting;
}

public class Setting
{
    public List<string> traits = new List<string>();
    public RegimeType regime;
    public CitySetting City;
    public KingdomSetting Kingdom;
    public ClanSetting Clan;
    public FamilySetting Family;
    public UnitSetting Unit;
    public string Religion = "";
}

public class UnitSetting
{
    public Dictionary<string, string> groups;
    public OnomasticsType[] rule;
    public int name_pos;
    public bool is_invert;
}

public class FamilySetting
{
    public Dictionary<string, string> groups;
    public OnomasticsType[] rule;
    public int name_pos;
    public bool has_sex_post;
    public bool use_local_as_lastname;
}

public class ClanSetting
{
    public Dictionary<string, string> groups;
    public OnomasticsType[] rule;
    public int name_pos;
    public bool has_sex_post;
    public bool use_local_as_lastname;
    public string[] sex_post_Male;
    public string[] sex_post_Female;
}

public class KingdomSetting
{
    public Dictionary<string, string> groups;
    public OnomasticsType[] rule;
    public int name_pos;
    public bool english_type_prefix = true;
}

public class CitySetting
{
    public Dictionary<string, string> groups;
    public OnomasticsType[] rule;
    public int name_pos;
}

public static class OnomasticsRule
{
    public static Dictionary<string, Setting> ALL_CULTURE_RULE = new Dictionary<string, Setting>();
    public static Dictionary<string, (string ch, string cz, string en)> ALL_CULTURE_TRANSLATE = new Dictionary<string, (string ch, string cz, string en)>();
    // 每种文化的预制染色（来自 CultureRulesConfig.json 的 color 字段），供地图上
    // 文化图层的地块底色/悬停高亮使用，避免继续用原版自带的、跟模组文化实体对不上
    // 的颜色，也避免像铭牌图标那样只是"看着还行"的哈希取色（那个是给旗帜图标用的，
    // 玩家不会拿它跟别的文化的颜色反复比较；地图底色不一样，需要真正稳定可配置）。
    public static Dictionary<string, Color> ALL_CULTURE_COLOR = new Dictionary<string, Color>();
    public static void ReadSetting()
    {
        string settingPath = Path.Combine(ModClass._declare.FolderPath, "CultureRulesConfig.json");
        string text = File.ReadAllText(settingPath);
        List<CultureRule>  cultureRules = JsonConvert.DeserializeObject<List<CultureRule>>(text);
        foreach (CultureRule cultureRule in cultureRules)
        {
            ALL_CULTURE_TRANSLATE.Add(cultureRule.name, (cultureRule.translate_ch, cultureRule.translate_cz, string.IsNullOrEmpty(cultureRule.translate_en)?cultureRule.name:cultureRule.translate_en));
            ALL_CULTURE_RULE.Add(cultureRule.name, cultureRule.setting);
            if (!string.IsNullOrEmpty(cultureRule.color) && ColorUtility.TryParseHtmlString(cultureRule.color, out Color parsedColor))
            {
                ALL_CULTURE_COLOR[cultureRule.name] = parsedColor;
            }
            LogService.LogInfo("载入文化配置"+cultureRule.name);
        }
    }

    // 文化地图图层用的预制颜色。配置里没填 color、或者填的十六进制解析失败时，
    // 退化成按文化名哈希出一个稳定（同一次读表内每次都一样，但换个文化名字就会
    // 变别的颜色）的柔和色，而不是直接报错/给纯黑——这样即使漏填颜色，地图上
    // 也不会出现一块诡异的纯黑/纯白区域，只是不一定好看，需要回去补配置。
    public static Color GetCultureColor(this string culture)
    {
        if (!string.IsNullOrEmpty(culture) && ALL_CULTURE_COLOR.TryGetValue(culture, out Color color))
        {
            return color;
        }
        int hash = Math.Abs((culture ?? "").GetHashCode());
        float hue = (hash % 360) / 360f;
        Color fallback = Color.HSVToRGB(hue, 0.55f, 0.85f);
        fallback.a = 1f;
        return fallback;
    }

    public static string GetCultureTranslate(this string culture)
    {
        var language = PlayerConfig.dict["language"].stringVal;
        var tc = ALL_CULTURE_TRANSLATE[culture];
        var translate = "";
        switch (language)
        {
            case "ch":
                translate = tc.ch;
                break;
            case "en":
                translate = tc.en;
                break;
            case "cz":
                translate = tc.cz;
                break;
            default:
                translate = tc.en;
                break;
        }
        translate = string.IsNullOrEmpty(translate) ? tc.en : translate;
        return translate;
    }
}

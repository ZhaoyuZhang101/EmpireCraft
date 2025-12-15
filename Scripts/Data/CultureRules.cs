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

namespace EmpireCraft.Scripts.Data;
public class CultureRule
{
    public string name;
    public string translate_ch;
    public string translate_cz;
    public string translate_en;
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
}

public class KingdomSetting
{
    public Dictionary<string, string> groups;
    public OnomasticsType[] rule;
    public int name_pos;
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
    public static void ReadSetting()
    {
        string settingPath = Path.Combine(ModClass._declare.FolderPath, "CultureRulesConfig.json");
        string text = File.ReadAllText(settingPath);
        List<CultureRule>  cultureRules = JsonConvert.DeserializeObject<List<CultureRule>>(text);
        foreach (CultureRule cultureRule in cultureRules)
        {
            ALL_CULTURE_TRANSLATE.Add(cultureRule.name, (cultureRule.translate_ch, cultureRule.translate_cz, string.IsNullOrEmpty(cultureRule.translate_en)?cultureRule.name:cultureRule.translate_en));
            ALL_CULTURE_RULE.Add(cultureRule.name, cultureRule.setting);
            LogService.LogInfo("载入文化配置"+cultureRule.name);
        }
    }
}
using EmpireCraft.Scripts.Enums;
using NeoModLoader.services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.Data;
public class CultureRule
{
    public string name;
    public Setting setting;
    public BeareauConfig beaurau_config;
}
public class BeareauConfig
{
    public List<OfficeConfig> CoreOffice;
    public List<OfficeConfig> Divisions;
}

public class OfficeConfig
{
    public string name;
    public OfficialLevel type;
    public string description;
    public string pre;
}

public class Setting
{
    public List<string> traits = new List<string>();
    public CitySetting City;
    public KingdomSetting Kingdom;
    public ClanSetting Clan;
    public FamilySetting Family;
    public UnitSetting Unit;
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
    public static Dictionary<string, BeareauConfig> ALL_CULTURE_CONFIG = new Dictionary<string, BeareauConfig>();
    public static List<CultureRule> ALL_CULTUREs = new List<CultureRule>();
    public static void ReadSetting()
    {
        string settingPath = Path.Combine(ModClass._declare.FolderPath, "CultureRulesConfig.json");
        string text = File.ReadAllText(settingPath);
        List<CultureRule>  cultureRules = JsonConvert.DeserializeObject<List<CultureRule>>(text);
        foreach (CultureRule cultureRule in cultureRules)
        {
            ALL_CULTURE_RULE.Add(cultureRule.name, cultureRule.setting);
            ALL_CULTURE_CONFIG.Add(cultureRule.name, cultureRule.beaurau_config);
        }
    }
}
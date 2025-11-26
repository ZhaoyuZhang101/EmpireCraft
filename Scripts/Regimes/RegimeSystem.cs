using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using NeoModLoader.General;
using NeoModLoader.services;
using Newtonsoft.Json;

namespace EmpireCraft.Scripts.Regimes;
public enum KingdomType
{
    LvLing_centre,//京畿道
    LvLing_kingdom,//藩国
    LvLing_jiedushi,//军
    LvLing_province,//道
    LvLing_jimizhou,//羁縻州

    ZhouFeudalism_empire, //朝
    ZhouFeudalism_gong, //国
    ZhouFeudalism_hou, //国
    ZhouFeudalism_bo, //国
    ZhouFeudalism_zi, //国

    Feudalism_empire, //帝国
    Feudalism_kingdom, //王国
    Feudalism_grand_duchy, //大公国
    Feudalism_duchy, //公国
    Feudalism_county, //伯爵领
    Feudalism_march, //藩侯领
    Feudalism_papal_state, //教宗国

    Arabic_caliphate, //哈里发国
    Arabic_sultanate, //苏丹国
    Arabic_emirate, //酋长国
    Arabic_province, //行省
    
    Republic_republic, //共和国
    Republic_province, //省
    Republic_state, //州
    Republic_autonomous_prefecture,//自治州

    default_country_post //国
}

public enum ArmyOfficialType
{
    Lvling_army_yuling,     //羽林将军
    Lvling_army_dudu,       //都督
    Lvling_army_zhenjiang,  //镇将
    Lvling_army_shuzhu      //戍主
}
public enum CityType
{
    LvLing_city, //县
    
    ZhouFeudalism_city, //邑
    
    Arabic_city, //市
    
    Republic_city, //市
    
    Feudalism_city, //市
    Feudalism_dirC, //帝国伯爵领
    Feudalism_religion_district //教区
}
public enum TaxLevel
{
    None,  //无
    Low,   //低
    Medium,//中
    High   //高
}

public enum LeaderSelectMethod
{
    Succession,  //世袭
    Exam,        //考试
    Vote,        //投票
    Army,        //举能
    Default
}

public enum RegimeType
{
    LvLing,        //律令      - 唐
    Feudalism,     //封建      - 神罗
    ZhouFeudalism, //分封      - 周
    Republic,      //共和      - 现代美国
    Arabic,        //阿拉伯政体 - 阿拉伯世界
}

public enum ReligionLevel
{
    None,   //无国教-自由信仰
    Low,    //有国教-自由信仰
    Medium, //有国教-限制信仰
    High    //政教合一
}

public class Regime
{
    public RegimeType  type;
    public string description;
    public string icon_url;
    public bool era_name;
    [JsonIgnore]
    public long control_kingdom_id;
    public LeaderSelectMethod leader_select_method;
    public List<FixedFaction> Factions;
    public Dictionary<string, int[]> options;
    public BureauConfig bureau_config;
    public Regime Clone()
    {
        return new Regime
        {
            type = this.type,
            description = this.description,
            control_kingdom_id = this.control_kingdom_id,
            options = this.options.ToDictionary(
                entry => entry.Key,
                entry => (int[])entry.Value.Clone()
            ),
            bureau_config = this.bureau_config,
            era_name = this.era_name,
            Factions =  this.Factions.Select(f=>f.Clone()).ToList(),
        };
    }

    public FixedFaction GetDominateFaction()
    {
        return Factions.OrderByDescending(a=>a.TotalPower).First();
    }

    public List<Actor> GetAllFactionMembers()
    {
        var res = new List<Actor>();
        foreach (var f in Factions)
        {
            res.AddRange(f.Members.Select(a=>World.world.units.get(a)));
        }
        return res;
    }
    public bool HasEraName()
    {
        return era_name;
    }
    public TaxLevel GetTaxLevel()
    {
        return (TaxLevel)options["option_tax_level"][0];
    }

    public ReligionLevel GetReligionLevel()
    {
        return (ReligionLevel)options["option_religion_type"][0];
    }

    public LeaderSelectMethod GetLeaderSelectMethod()
    {
        return (LeaderSelectMethod)options["option_leader_select_method"][0];
    }

    public void SetLeaderSelectMethod(LeaderSelectMethod value)
    {
        options["option_leader_select_method"][0] = (int)value;
    }

    public bool IsAllowDiplomacy()
    {
        return Convert.ToBoolean(options["toggle_allow_diplomacy"][0]);
    }

    public void SetAllowDiplomacy(bool value)
    {
        options["toggle_allow_diplomacy"][0] = value?1:0;
    }

    public bool IsAllowArmy()
    {
        return Convert.ToBoolean(options["toggle_allow_army"][0]);
    }

    public void SetAllowArmy(bool value)
    {
        options["toggle_allow_army"][0] = value?1:0;
    }

    public bool IsAllowSupportCenterArmy()
    {
        return Convert.ToBoolean(options["toggle_support_army_to_center"][0]);
    }
}

public static class RegimeManager
{
    public static Dictionary<RegimeType, Regime> regimes;
    private static string _folderPath = Path.Combine(ModClass._declare.FolderPath, "Scripts", "Regimes", "Configs");

    public static void init()
    {
        if (regimes != null) return;

        regimes = new Dictionary<RegimeType, Regime>();

        if (Directory.Exists(_folderPath))
        {
            // 遍历 Regimes 下所有子目录
            var subDirs = Directory.GetDirectories(_folderPath);
            foreach (var dir in subDirs)
            {
                LM.LoadLocales(Path.Combine(dir, "OfficialType.csv"));
                var filePath = Path.Combine(dir, "SystemConfig.json");
                if (File.Exists(filePath))
                {
                    var text = File.ReadAllText(filePath);
                    var dict = JsonConvert.DeserializeObject<Dictionary<RegimeType, Regime>>(text);

                    foreach (var regime in dict)
                    {
                        regime.Value.type = regime.Key;
                        regimes[regime.Key] = regime.Value; // 合并到总字典
                        LogService.LogInfo(regime.Value.bureau_config.cores.Count.ToString());
                        LogService.LogInfo("加载政体完成: " + regime.Key + " 来自 " + dir);
                    }
                }
                else
                {
                    LogService.LogInfo($"未发现政体配置文件: {filePath}");
                }
            }
        }
        else
        {
            LogService.LogInfo($"未发现政体目录: {_folderPath}");
        }
    }
}
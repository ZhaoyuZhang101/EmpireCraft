using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.services;
using Newtonsoft.Json;

namespace EmpireCraft.Scripts.Regimes;
public enum KingdomType
{
    Arabic_caliphate,
    Arabic_emirate,
    Arabic_province,
    Arabic_sultanate,
    Feudalism_county,
    Feudalism_duchy,
    Feudalism_empire,
    Feudalism_grand_duchy,
    Feudalism_kingdom,
    Feudalism_march,
    Feudalism_papal_state,
    LvLing_centre,
    LvLing_duhufu,
    LvLing_jiedushi,
    LvLing_jimizhou,
    LvLing_kingdom,
    LvLing_province,
    Modern_autonomous_prefecture,
    Modern_centre,
    Modern_province,
    Modern_state,
    Origin_centre,
    Origin_kingdom,
    YouMu_bu,
    YouMu_centre,
    YouMu_kingdom,
    ZhouFeudalism_bo,
    ZhouFeudalism_empire,
    ZhouFeudalism_gong,
    ZhouFeudalism_hou,
    ZhouFeudalism_zi,
    default_country_post,
    ClassicalRepublic_centre,
    ClassicalRepublic_city_state,
    ZhouFeudalism_jun, // 郡国并行下分封制的郡(官员选拔的行政区)；追加在末尾以保持旧存档枚举值稳定
    Feudalism_intendancy, // 中央集权君主制下西方封建制的辖区(官员选拔的行政区)
    Feudalism_diocese, // 神权国家下西方封建制的教区(官员选拔的行政区)
}

public enum ArmyOfficialType
{
    LvLing_army_yuling,
    LvLing_army_dudu,
    LvLing_army_zhenjiang,
    LvLing_army_shuzhu,
}
public enum CityType
{
    Arabic_city,
    Feudalism_city,
    Feudalism_dirC,
    Feudalism_religion_district,
    LvLing_city,
    LvLing_jimizhou,
    Modern_city,
    Origin_city,
    YouMu_city,
    ZhouFeudalism_city,
    ClassicalRepublic_city,
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
    Harem,       //妃子
    Default
}

[Flags]
public enum RegimeType
{
    LvLing,        //律令      - 唐
    Feudalism,     //封建      - 神罗
    ZhouFeudalism, //分封      - 周
    Modern,      //共和      - 现代美国
    Arabic,        //阿拉伯政体 - 阿拉伯世界
    YouMu,         //游牧政体   - 蒙古汗国
    Republic,
    Origin,
    ClassicalRepublic //古典城邦共和；追加在末尾以保持旧存档枚举值稳定
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
    public RegimeType type;
    public string description;
    public string icon_url;
    public bool era_name;
    [JsonIgnore]
    public long control_kingdom_id;
    [JsonIgnore] 
    public AutoHoriLayoutGroup FactionSpace;
    public LeaderSelectMethod leader_select_method; 
    public bool centre_empire_separate;
    public bool has_cabinet;
    public int cabinet_number;
    public KingdomType default_kingdom;
    // 分封规则由体制配置决定，避免把律令制的虚封逻辑写死到通用帝国流程中。
    public bool enfeoff_only_royal;
    public bool enfeoff_virtual_only;
    public bool enfeoff_virtual_can_use_empire_titles;
    public bool enable_auto_honorary_peerages;
    // 政体性质由配置声明，而不是在各个系统里罗列 RegimeType：
    //   is_monarchy            君主制（可以推行君主立宪、可以有议会限制君权）
    //   allows_landlord_class  允许出现地主阶层（封建/分封制土地归领主，没有自由的地主）
    public bool is_monarchy;
    public bool allows_landlord_class = true;
    public List<PeeragesLevel> virtual_peerages;
    public List<string> virtual_peerage_names;
    public List<string> virtual_honorary_peerages;
    //宗教點數
    public int religion_point = 0;
    public FixedFaction CentreMind;
    public List<FixedFaction> Factions;
    public List<FixedFaction> PlayerFactions;
    public Dictionary<string, int[]> options;
    public BureauConfig bureau_config;
    public List<LawType> laws;
    // 制度科技树曾经挂在这里，现在已经搬到 InstitutionTrees/<线 id>.json：制度归属文化
    // （进而归属科技线），跟政体不是一回事。政体配置只管"国家形态"，不再管制度。
    public double FactionChangeBlockUntil = -1f;

    public void BlockFactionChange(int years)
    {
        FactionChangeBlockUntil = World.world.getCurWorldTime() + years;
    }

    public bool IsFactionChangeBlocked()
    {
        return World.world.getCurWorldTime() < FactionChangeBlockUntil;
    }

    public List<FixedFaction> GetPlayerFactions() =>
        PlayerFactions ??= new List<FixedFaction>(
            FactionManager.Config.PlayerRegimeFactions.TryGetValue(type, out List<FixedFaction> factions)
                ? factions.Select(f => f.DeepClone()).ToList()
                : Factions.Select(f => f.DeepClone()).ToList());

    public Regime Clone(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return null;
        var res = new Regime
        {
            type = this.type,
            CentreMind = new FixedFaction()
            {
                _id = Guid.NewGuid().ToString(),
            },
            description = this.description,
            control_kingdom_id = kingdom.getID(),
            options = (this.options ?? new Dictionary<string, int[]>()).ToDictionary(
                entry => entry.Key,
                entry => (int[])entry.Value.Clone()
            ),
            bureau_config = this.bureau_config,
            laws = this.laws?.ToList() ?? new List<LawType>(),
            era_name = this.era_name,
            has_cabinet = this.has_cabinet,
            cabinet_number = this.cabinet_number,
            centre_empire_separate = this.centre_empire_separate,
            default_kingdom = this.default_kingdom,
            enfeoff_only_royal = this.enfeoff_only_royal,
            enfeoff_virtual_only = this.enfeoff_virtual_only,
            enfeoff_virtual_can_use_empire_titles = this.enfeoff_virtual_can_use_empire_titles,
            enable_auto_honorary_peerages = this.enable_auto_honorary_peerages,
            is_monarchy = this.is_monarchy,
            allows_landlord_class = this.allows_landlord_class,
            virtual_peerages = this.virtual_peerages?.ToList() ?? new List<PeeragesLevel>(),
            virtual_peerage_names = this.virtual_peerage_names?.ToList() ?? new List<string>(),
            virtual_honorary_peerages = this.virtual_honorary_peerages?.ToList() ?? new List<string>(),
            Factions = (this.Factions ?? new List<FixedFaction>()).Select(f => f.Clone()).ToList()
        };
        List<FixedFaction> factions = null;
        var hasConfig = FactionManager.Config?.PlayerRegimeFactions != null &&
                        FactionManager.Config.PlayerRegimeFactions.TryGetValue(type, out factions);
        res.PlayerFactions = hasConfig
            ? factions.Select(f => f.DeepClone()).ToList()
            : res.Factions.Select(f => f.DeepClone()).ToList();
        return res;
    }

    public void RecoverFactions()
    {
        PlayerFactions = Factions.Select(f => f.Clone()).ToList();
        Kingdom kingdom = World.world.kingdoms.get(control_kingdom_id);
        Empire empire = kingdom?.GetEmpire();
        if (kingdom == null || empire == null || kingdom != empire.CoreKingdom) return;

        foreach (FixedFaction faction in PlayerFactions)
        {
            faction.EmpireId = empire.getID();
            faction.FixMissedTemporaryFactions();
        }
        kingdom.ReconcileFactionRatios(PlayerFactions);
        PlayerFactions.ForEach(faction => faction.Update());
    }

    public FixedFaction GetDominateFaction()
    {
        if ((PlayerFactions?.Count??0)<=0) return null;
        var force = PlayerFactions.Find(f => f.Force);
        return force ?? PlayerFactions.OrderByDescending(faction => faction.CentralRatio).First();
    }

    public List<Actor> GetAllFactionMembers()
    {
        var res = new List<Actor>();
        foreach (var f in PlayerFactions)
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
    public void SetTaxLevel(TaxLevel level)
    {
        Kingdom kingdom = World.world?.kingdoms?.get(control_kingdom_id);
        if (kingdom != null && level != GetTaxLevel() &&
            !ConstitutionalEconomySystem.CanChangeTax(kingdom.GetEmpire())) return;
        options["option_tax_level"][0] = (int) level;
    }

    public ReligionLevel GetReligionLevel()
    {
        return (ReligionLevel)options["option_religion_type"][0];
    }

    public void SetReligionLevel(ReligionLevel level)
    {
        options["option_religion_type"][0] = (int) level;
    }

    public LeaderSelectMethod GetLeaderSelectMethod()
    {
        return (LeaderSelectMethod)options["option_leader_select_method"][0];
    }

    public void SetLeaderSelectMethod(LeaderSelectMethod value)
    {
        options["option_leader_select_method"][0] = (int)value;
    }

    // 继承法的持久状态存在 KingdomExtraData.SuccessionLaw 上(regime 对象本身每次读档都会重新 Clone),
    // 这里的 option 只是给 RegimeWindow 通用渲染用的镜像,读写都要和 KingdomExtraData 同步。
    public SuccessionLawType GetSuccessionLaw()
    {
        return (SuccessionLawType)options["option_succession_law"][0];
    }

    public void SetSuccessionLaw(SuccessionLawType value)
    {
        options["option_succession_law"][0] = (int)value;
        World.world.kingdoms.get(control_kingdom_id)?.SetSuccessionLaw(value);
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

    public void SetAllowSupportCenterArmy(bool value)
    {
        options["toggle_support_army_to_center"][0] = value?1:0;
    }
}

public static class RegimeManager
{
    public static Dictionary<RegimeType, Regime> regimes;

    // 政体性质一律以模板为准：kingdom 身上的 regime 是克隆出来的，旧存档里的克隆没有这些字段。
    // 模板缺失（例如配置里写了尚未实现的政体）时视为非君主制、允许地主。
    public static Regime GetTemplate(RegimeType? type) =>
        type.HasValue && regimes != null && regimes.TryGetValue(type.Value, out Regime regime) ? regime : null;

    public static bool IsMonarchy(RegimeType? type) => GetTemplate(type)?.is_monarchy == true;

    public static bool AllowsLandlordClass(RegimeType? type) => GetTemplate(type)?.allows_landlord_class ?? true;
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
                        NormalizeRegime(regime.Value);
                        regimes[regime.Key] = regime.Value; // 合并到总字典
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

        // 制度科技线是独立的一套配置（InstitutionTrees/），跟政体目录互不依赖：
        // 政体目录缺失也照样把制度线读进来，反之亦然。
        InstitutionDefinitionRegistry.Load();
    }
    private static void NormalizeRegime(Regime regime)
    {
        var bc = regime.bureau_config ?? new BureauConfig();
        bc.cores ??= new List<BureauSetting>();
        bc.division ??= new List<BureauSetting>();
        bc.harems ??= new List<BureauSetting>();
        bc.kingdoms ??= new Dictionary<KingdomType, BureauSetting>();
        bc.cities ??= new Dictionary<CityType, BureauSetting>();
        bc.armies ??= new Dictionary<ArmyOfficialType, BureauSetting>();
        foreach (var s in bc.cores)
        {
            s.powers ??= new List<OfficerPowerType>();
            s.require_traits ??= new List<string>();
            s.condition ??= new List<string>();
        }
        foreach (var s in bc.division)
        {
            s.powers ??= new List<OfficerPowerType>();
            s.require_traits ??= new List<string>();
            s.condition ??= new List<string>();
        }
        foreach (var s in bc.harems)
        {
            s.powers ??= new List<OfficerPowerType>();
            s.require_traits ??= new List<string>();
            s.condition ??= new List<string>();
        }
        foreach (var kv in bc.kingdoms)
        {
            var s = kv.Value;
            if (s == null) continue;
            s.powers ??= new List<OfficerPowerType>();
            s.require_traits ??= new List<string>();
            s.condition ??= new List<string>();
        }
        foreach (var kv in bc.cities)
        {
            var s = kv.Value;
            if (s == null) continue;
            s.powers ??= new List<OfficerPowerType>();
            s.require_traits ??= new List<string>();
            s.condition ??= new List<string>();
        }
        foreach (var kv in bc.armies)
        {
            var s = kv.Value;
            if (s == null) continue;
            s.powers ??= new List<OfficerPowerType>();
            s.require_traits ??= new List<string>();
            s.condition ??= new List<string>();
        }
        regime.bureau_config = bc;
        regime.options ??= new Dictionary<string, int[]>();
        if (!regime.options.ContainsKey("option_succession_law"))
        {
            regime.options["option_succession_law"] = new[] { 0, Enum.GetValues(typeof(SuccessionLawType)).Length };
        }
        regime.laws ??= new List<LawType>();
    }
}

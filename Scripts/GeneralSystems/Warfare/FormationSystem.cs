using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;
using NeoModLoader.services;
using Newtonsoft.Json;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 军制建制：谁组织、谁出钱、怎么补、去哪打，与兵种(CorpsArmSystem.Arm)分开存，配置见 Scripts/Regimes/Configs/Formations.json。
//   · 身份：corps.formation 是配置里的建制 id；政体与军镇类别定默认(律令的禁军、镇军、府兵……)，换政体、换文化、换装都不改；
//     只有被俘、改投别国和显式整编才改，改时记旧称(corps.former_names)。
//   · 番号：按配置的范围(全国、本建制、驻地)单调递增，计数随存档(formation_serials)，旧档先扫描已占用的号；
//     分遣队不占番号，名字是"某部先遣队"，并回就注销。军团列表、地图提示、战报都走 Name。
//   · 差别：每种建制定下兵源(source)、补员快慢(raise)、新兵冲淡老兵多少与老兵上限(training、veteran_cap)、军饷(upkeep)、
//     不能列装的兵种(denied_arms，按名字，不看枚举大小)、任务(task：field 野战、mixed、garrison 守备)、走多远(reach)和
//     听不听中央调往外地(command)。常备军这一档全取默认值，与没有建制时一样，不牵动大盘平衡。
//   · 守备军可以在家门口打，受攻的城照样去守(见 FrontSystem.Assign)；离岗后驻地没人守就是守备缺口，不另算一遍。
public sealed class FormationEchelon
{
    public float below = -1f;
    public string key = "";
}

public sealed class FormationDef
{
    public string id = "";
    public string name = "";
    public string template = "formation_template_corps";
    public string scope = "formation";
    public List<FormationEchelon> echelons = new();
    public string source = "any";
    public float raise = 1f, training = 1f, upkeep = 1f, veteran_cap = 100f;
    public List<string> denied_arms = new();
    public string task = "field";
    public float reach = -1f;
    public string command = "central";
}

public sealed class FormationRegimeMap
{
    public string central = "", district = "", prefecture = "", manual = "";
    // 穿越政权早期的建制与合并(见 TransmigrantSystem)：阶段不到 early_below_stage 用 early，到 merge_at_stage 把 early 的并进来，只并一次
    public string early = "";
    public int early_below_stage, merge_at_stage;
}

public sealed class FormationConfig
{
    public int version;
    public Dictionary<string, FormationDef> formations = new();
    public Dictionary<string, FormationRegimeMap> regimes = new();
    public FormationRegimeMap fallback = new();
}

public static class FormationSystem
{
    public const string Fubo = "fubo";
    private const int MaxFormerNames = 6;
    // 旧档认伏波军：成军时间与降临时间差不过这么久(世界时间，一年)才算降临带来的那支
    private const double LandingWindow = 60d;
    // 显式整编要重新整训
    private const float ReorganizeOrg = 0.7f, ReorganizeVeterancy = 0.85f;

    private static readonly FormationDef Baseline = new() { id = "standing" };
    private static FormationConfig _config;

    public static FormationConfig Config
    {
        get
        {
            if (_config != null) return _config;
            try
            {
                string path = Path.Combine(ModClass._declare.FolderPath, "Scripts", "Regimes", "Configs", "Formations.json");
                _config = JsonConvert.DeserializeObject<FormationConfig>(File.ReadAllText(path)) ?? new FormationConfig();
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][军制] Formations.json 读取失败，一律按常备军：{exception.Message}");
                _config = new FormationConfig();
            }
            foreach (KeyValuePair<string, FormationDef> pair in _config.formations) pair.Value.id = pair.Key;
            return _config;
        }
    }

    public static FormationDef Def(string id) =>
        !string.IsNullOrEmpty(id) && Config.formations.TryGetValue(id, out FormationDef def) ? def : null;

    private static FormationDef RulesOf(CorpsData corps) => Def(corps?.formation) ?? Baseline;

    #region 身份与番号

    private static FormationRegimeMap MapOf(Kingdom kingdom)
    {
        Regimes.Regime regime = kingdom?.GetRegime();
        return regime != null && Config.regimes.TryGetValue(regime.type.ToString(), out FormationRegimeMap map) ? map : Config.fallback;
    }

    // 这支军该归哪种建制：特殊阶段 → 政体默认 → 通用回退
    public static string Resolve(Kingdom kingdom, CorpsData corps)
    {
        if (corps == null || corps.militia || corps.order_id >= 0) return "";
        FormationRegimeMap map = MapOf(kingdom);
        // 大区这些成员国没有自己的阶段，看元老院的
        if (!string.IsNullOrEmpty(map.early) && TransmigrantSystem.SenateOf(kingdom) is Kingdom senate &&
            TransmigrantSystem.StageOf(senate) < map.early_below_stage) return map.early;
        if (corps.manual) return map.manual;
        return (CityPopulationSystem.ArmyPost)corps.post switch
        {
            CityPopulationSystem.ArmyPost.District => map.district,
            CityPopulationSystem.ArmyPost.Prefecture => map.prefecture,
            _ => map.central
        };
    }

    // 旧档还没有建制的军：只有降临带来的那支认作伏波军，别的按政体默认，手动军保持手动
    private static string Migrate(Kingdom kingdom, CorpsData corps)
    {
        if (corps.manual && !corps.militia && TransmigrantSystem.Of(kingdom) is TransmigrantState state && state.landed_at >= 0d &&
            corps.raised_at >= 0d && Math.Abs(corps.raised_at - state.landed_at) <= LandingWindow &&
            !CorpsSystem.Of(kingdom).Any(other => other != corps && other.formation == Fubo))
            return Fubo;
        return Resolve(kingdom, corps);
    }

    private static string RangeKey(FormationDef def, CorpsData corps) => def.scope switch
    {
        "kingdom" => "@kingdom",
        "region" => $"{def.id}@{corps.home_city_id}",
        _ => def.id
    };

    // 下一个番号：这个范围里单调递增；旧档第一次分配时先扫描已占用的号，同范围不重号
    private static int NextNumber(Kingdom kingdom, FormationDef def, CorpsData corps)
    {
        var data = kingdom.GetOrCreate();
        data.formation_serials ??= new Dictionary<string, int>();
        string key = RangeKey(def, corps);
        data.formation_serials.TryGetValue(key, out int last);
        foreach (CorpsData other in CorpsSystem.Of(kingdom))
        {
            if (other == corps || other.parent_id >= 0 || other.formation_serial <= last) continue;
            FormationDef otherDef = Def(other.formation);
            if (otherDef != null && RangeKey(otherDef, other) == key) last = other.formation_serial;
        }
        data.formation_serials[key] = last + 1;
        return last + 1;
    }

    // 定下建制与番号；分遣队、义军、骑士团不占番号
    public static void Ensure(Kingdom kingdom, CorpsData corps)
    {
        if (corps == null || kingdom?.data == null || corps.militia || corps.order_id >= 0 || corps.parent_id >= 0) return;
        if (string.IsNullOrEmpty(corps.formation)) corps.formation = Migrate(kingdom, corps);
        FormationDef def = Def(corps.formation);
        if (def != null && corps.formation_serial <= 0) corps.formation_serial = NextNumber(kingdom, def, corps);
    }

    // 每月：旧档按原来的军号先后补上，番号顺序不乱；都有了就只比一遍
    public static void EnsureAll(Kingdom kingdom, List<CorpsData> list)
    {
        if (list == null) return;
        bool missing = false;
        foreach (CorpsData corps in list)
        {
            if (corps.militia || corps.order_id >= 0 || corps.parent_id >= 0) continue;
            if (!string.IsNullOrEmpty(corps.formation) && corps.formation_serial > 0) continue;
            missing = true;
            break;
        }
        if (!missing) return;
        foreach (CorpsData corps in list.OrderBy(entry => entry.serial).ToList()) Ensure(kingdom, corps);
    }

    private static void Remember(CorpsData corps, string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        corps.former_names ??= new List<string>();
        corps.former_names.Add(name);
        while (corps.former_names.Count > MaxFormerNames) corps.former_names.RemoveAt(0);
    }

    // 改投别国、被收编：按新主人的军制另编，记下旧称。人、老兵值、兵种不动
    public static void Reassign(Kingdom to, CorpsData corps, string formerName)
    {
        if (corps == null || corps.militia || corps.order_id >= 0) return;
        Remember(corps, formerName);
        corps.formation = Resolve(to, corps);
        corps.formation_serial = 0;
        Ensure(to, corps);
    }

    // 显式整编：改建制、换番号、记旧称；retrain 时要重新整训(组织度、老兵值回落)。人员、来源、兵种不动
    public static bool Reorganize(Kingdom kingdom, CorpsData corps, string formation, bool retrain = true)
    {
        if (kingdom?.data == null || corps == null || corps.militia || corps.order_id >= 0 || corps.parent_id >= 0 ||
            Def(formation) == null || corps.formation == formation) return false;
        string former = CorpsSystem.Name(corps);
        Remember(corps, former);
        corps.formation = formation;
        corps.formation_serial = 0;
        Ensure(kingdom, corps);
        if (retrain)
        {
            corps.org *= ReorganizeOrg;
            corps.veterancy *= ReorganizeVeterancy;
        }
        corps.veterancy = Mathf.Min(corps.veterancy, VeteranCap(corps));
        FrontSystem.Log(kingdom, string.Format(LM.Get("corps_log_reorganized"), former, CorpsSystem.Name(corps)));
        return true;
    }

    // 穿越政权到了 merge_at_stage：早期的建制撤销番号，并入后来的建制，只并一次；人员、经验、装备和旧番号都留着
    public static int MergeEarly(Kingdom kingdom, TransmigrantState state)
    {
        if (kingdom?.data == null || state == null || state.security_merged) return 0;
        FormationRegimeMap map = MapOf(kingdom);
        if (string.IsNullOrEmpty(map.early) || map.merge_at_stage <= 0 || state.stage < map.merge_at_stage) return 0;
        state.security_merged = true;
        int merged = 0;
        // 连同帝国里的大区：它们没有自己的穿越记录，跟着元老院一起并
        var realms = new List<Kingdom> { kingdom };
        List<Kingdom> members = kingdom.GetEmpire()?.kingdoms_list;
        if (members != null) realms.AddRange(members.Where(member => member != null && member != kingdom && !member.isRekt()));
        foreach (Kingdom realm in realms)
            foreach (CorpsData corps in CorpsSystem.Of(realm).Where(entry => entry.formation == map.early).ToList())
                if (Reorganize(realm, corps, map.central, retrain: false)) merged++;
        return merged;
    }

    // 还没有伏波军标记、可以显式整编的手动军，兵多的在前(旧档认不出的走这个入口，见军事页)
    public static List<CorpsData> ReorganizeCandidates(Kingdom kingdom) =>
        TransmigrantSystem.Of(kingdom) == null ? new List<CorpsData>()
            : CorpsSystem.Of(kingdom).Where(corps => corps.manual && !corps.militia && corps.order_id < 0 && corps.parent_id < 0 &&
                                                     corps.formation != Fubo && !corps.embarked && corps.task is not ("attack" or "defend"))
                .OrderByDescending(corps => corps.strength).ToList();

    #endregion

    #region 名字

    // 正式番号全称；还没有番号的返回空(调用方退回旧写法)
    public static string Name(CorpsData corps)
    {
        if (corps == null || corps.militia) return null;
        if (corps.parent_id >= 0)
        {
            CorpsData parent = FindParent(corps);
            return parent == null ? null : string.Format(LM.Get("corps_name_detachment"), CorpsSystem.Name(parent));
        }
        FormationDef def = Def(corps.formation);
        if (def == null || corps.formation_serial <= 0) return null;
        string format = LM.Get(def.template);
        bool ordinal = LM.Get("corps_name").StartsWith("{0}");
        return string.Format(format, ordinal ? CorpsSystem.Ordinal(corps.formation_serial) : CorpsSystem.Chinese(corps.formation_serial),
            DisplayName(def), CorpsSystem.Home(corps)?.GetCityName() ?? "", EchelonName(def, corps));
    }

    private static CorpsData FindParent(CorpsData corps)
    {
        Kingdom kingdom = World.world?.kingdoms?.get(corps.kingdom_id);
        return kingdom == null ? null : CorpsSystem.Of(kingdom).FirstOrDefault(other => other.id == corps.parent_id);
    }

    public static string DisplayName(FormationDef def) =>
        def == null ? "" : LM.Get(string.IsNullOrEmpty(def.name) ? "formation_" + def.id : def.name);

    // 按满编取编制层级(扩编了就升一级)；配置没写层级的为空
    private static string EchelonName(FormationDef def, CorpsData corps)
    {
        if (def.echelons == null) return "";
        foreach (FormationEchelon echelon in def.echelons)
            if (echelon.below <= 0f || corps.full < echelon.below) return LM.Get(echelon.key);
        return "";
    }

    #endregion

    #region 差别

    public static float RaiseFactor(CorpsData corps) => RulesOf(corps).raise;
    public static float TrainingFactor(CorpsData corps) => RulesOf(corps).training;
    public static float VeteranCap(CorpsData corps) => RulesOf(corps).veteran_cap;
    public static float UpkeepFactor(CorpsData corps) => RulesOf(corps).upkeep;

    public static bool AllowsArm(CorpsData corps, CorpsArmSystem.Arm arm) => !RulesOf(corps).denied_arms.Contains(arm.ToString());

    // 只就近征兵(不从全国抽)
    public static bool LocalSource(CorpsData corps) => RulesOf(corps).source == "local";

    // 这组人够不够格当这种兵：core 只收本政权主体文化的人。花钱募来的(见 CorpsSystem.Enlist)登记后入伍，不走这道
    public static bool CanRecruit(CorpsData corps, PopGroup group)
    {
        if (RulesOf(corps).source != "core") return true;
        Kingdom kingdom = World.world?.kingdoms?.get(corps.kingdom_id);
        string culture = kingdom == null ? null : CultureService.GetRealmCulture(kingdom);
        return string.IsNullOrEmpty(culture) || string.Equals(group?.culture, culture, StringComparison.Ordinal);
    }

    public static bool IsGarrison(CorpsData corps) => RulesOf(corps).task == "garrison";

    // 地方军：举国总动员、出征也不抽调到动员范围以外
    public static bool LocalCommand(CorpsData corps) => RulesOf(corps).command == "local";

    // 出兵先后：野战军先走，守备军最后
    public static int AttackRank(CorpsData corps) => RulesOf(corps).task switch
    {
        "field" => 0,
        "mixed" => 1,
        _ => 2
    };

    // 能不能打到这座城：配置了 reach 的不离驻地太远
    public static bool WithinReach(CorpsData corps, City city)
    {
        float reach = RulesOf(corps).reach;
        if (reach <= 0f) return true;
        TileZone home = CorpsSystem.Home(corps)?.getTile()?.zone, target = city?.getTile()?.zone;
        return home == null || target == null || CorpsSystem.ZoneDistance(home, target) <= reach;
    }

    // 不渡海：配置了 reach 的不出远门
    public static bool MayEmbark(CorpsData corps) => RulesOf(corps).reach <= 0f;

    // 这个月为什么补不了员(语言键)，能补的返回空
    public static string ReinforceBlock(Kingdom kingdom, CorpsData corps)
    {
        if (corps == null || CorpsSystem.Deficit(kingdom, corps) < 1f) return null;
        if (CorpsSystem.Unpaid(kingdom)) return "formation_block_unpaid";
        if (corps.embarked) return "formation_block_embarked";
        if (corps.supply < 0.5f) return "formation_block_supply";
        return null;
    }

    #endregion
}

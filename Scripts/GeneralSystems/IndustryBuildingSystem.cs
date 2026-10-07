using System;
using System.Collections.Generic;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 矿场与伐木场分级(古代到现代，各 5 级，每级有自己的造型，见 GameResources/buildings/ec_*)：
//   矿场：1 矿坑(原版 mine) → 2 矿井 → 3 水力矿场 → 4 蒸汽煤矿 → 5 现代矿业工厂；
//   伐木场：1 伐木营 → 2 锯木坊 → 3 水力锯木厂 → 4 蒸汽锯木厂 → 5 现代木材加工厂。
// 升级按科技解锁(见 Technology/TechTree.json 的 unlock_buildings)，由城市按升级订单自动升级
// (无小人模式下由 CityConstructionSystem 出钱升级)；第 3 级起必须建在工业区(见 ZonePlanSystem)。
// 产出：矿场每年固定产石头、金属、金子，伐木场每年固定产木头，都按等级倍增，在工业区再加成。
// 每块工业区让本城多建一座矿场、一座伐木场(最多多 3 座)。
// 兼容 Modern Mod：它的 2mine_modernmod 视为矿场第 2 级，接到第 3 级上。
public static class IndustryBuildingSystem
{
    public const string LumberType = "type_lumber";
    public const int IndustryZoneTier = 3;
    private const string ModernModMine = "2mine_modernmod";

    public static readonly string[] MineChain = { "mine", "ec_mine_2", "ec_mine_3", "ec_mine_4", "ec_mine_5" };
    public static readonly string[] LumberChain = { "ec_lumber_1", "ec_lumber_2", "ec_lumber_3", "ec_lumber_4", "ec_lumber_5" };

    // 产出倍数(按等级)
    private static readonly float[] TierOutput = { 1f, 1.6f, 2.5f, 4f, 6f };
    private const float IndustryZoneBonus = 1.25f;
    private const float WoodPerLumberYear = 8f;

    private static readonly Dictionary<string, int> Tiers = new();

    public static int Tier(BuildingAsset asset) =>
        asset != null && Tiers.TryGetValue(asset.id, out int tier) ? tier : 0;

    public static bool IsMine(BuildingAsset asset) => asset?.type == "type_mine";
    public static bool IsLumber(BuildingAsset asset) => asset?.type == LumberType;

    // ---- 建筑定义(模组加载时) ----
    public static void Init()
    {
        try
        {
            BuildingAsset mine = AssetManager.buildings.get("mine");
            if (mine == null) return;
            Tiers["mine"] = 1;
            for (int i = 1; i < MineChain.Length; i++)
            {
                int tier = i + 1;
                BuildingAsset asset = Clone(MineChain[i], "mine");
                asset.cost = tier switch
                {
                    2 => new ConstructionCost(10, 20, 0, 30),
                    3 => new ConstructionCost(20, 30, 10, 60),
                    4 => new ConstructionCost(10, 40, 30, 120),
                    _ => new ConstructionCost(0, 60, 60, 200)
                };
                asset.construction_progress_needed = 150 + 100 * tier;
                asset.base_stats["health"] = 200f * tier;
                Tiers[asset.id] = tier;
            }
            for (int i = 0; i < LumberChain.Length; i++)
            {
                int tier = i + 1;
                BuildingAsset asset = Clone(LumberChain[i], "mine");
                asset.type = LumberType;
                asset.priority = 45;
                asset.burnable = tier <= 3;
                asset.draw_light_area = tier >= 4;
                asset.sound_idle = "event:/SFX/BUILDINGS_IDLE/IdleWindmill";
                asset.sound_built = "event:/SFX/BUILDINGS/SpawnBuildingWood";
                asset.sound_destroyed = "event:/SFX/BUILDINGS/DestroyBuildingWood";
                asset.cost = tier switch
                {
                    1 => new ConstructionCost(0, 5, 0, 10),
                    2 => new ConstructionCost(15, 5, 0, 20),
                    3 => new ConstructionCost(25, 15, 5, 40),
                    4 => new ConstructionCost(10, 30, 20, 90),
                    _ => new ConstructionCost(0, 40, 40, 160)
                };
                asset.construction_progress_needed = 100 + 100 * tier;
                asset.base_stats["health"] = 150f * tier;
                Tiers[asset.id] = tier;
            }
            // 牧场(大型建筑)与屠宰场，见 AnimalHusbandrySystem
            BuildingAsset pasture = Clone("ec_pasture", "mine");
            pasture.type = AnimalHusbandrySystem.PastureType;
            pasture.fundament = new BuildingFundament(3, 3, 3, 1);
            pasture.priority = 40;
            pasture.burnable = true;
            pasture.draw_light_area = false;
            pasture.cost = new ConstructionCost(20, 0, 0, 15);
            pasture.construction_progress_needed = 200;
            pasture.base_stats["health"] = 300f;
            pasture.sound_idle = "event:/SFX/BUILDINGS_IDLE/IdleWindmill";
            pasture.sound_built = "event:/SFX/BUILDINGS/SpawnBuildingWood";
            pasture.sound_destroyed = "event:/SFX/BUILDINGS/DestroyBuildingWood";
            BuildingAsset slaughterhouse = Clone("ec_slaughterhouse", "mine");
            slaughterhouse.type = AnimalHusbandrySystem.SlaughterhouseType;
            slaughterhouse.priority = 42;
            slaughterhouse.burnable = true;
            slaughterhouse.draw_light_area = false;
            slaughterhouse.cost = new ConstructionCost(15, 10, 0, 20);
            slaughterhouse.construction_progress_needed = 200;
            slaughterhouse.base_stats["health"] = 300f;
            slaughterhouse.sound_built = "event:/SFX/BUILDINGS/SpawnBuildingWood";
            slaughterhouse.sound_destroyed = "event:/SFX/BUILDINGS/DestroyBuildingWood";
            if (AssetManager.buildings.get(ModernModMine) != null) Tiers[ModernModMine] = 2;
            Link();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][矿场伐木场] 建筑定义失败: {exception}");
        }
    }

    private static BuildingAsset Clone(string id, string from)
    {
        BuildingAsset asset = AssetManager.buildings.get(id) ?? AssetManager.buildings.clone(id, from);
        asset.sprite_path = "buildings/" + id;
        // 造型里的屋顶用原版的品红占位色画，游戏里换成王国颜色(和原版民居一样)
        asset.has_kingdom_color = true;
        asset.building_sprites = null;
        asset.sprites_are_initiated = false;
        asset.has_sprites_main = true;
        asset.has_sprites_ruin = true;
        asset.has_sprite_construction = true;
        asset.has_sprites_main_disabled = false;
        asset.has_sprites_special = false;
        asset.can_be_upgraded = false;
        asset.upgrade_to = null;
        asset.upgraded_from = null;
        asset.upgrade_level = 0;
        return asset;
    }

    // ---- 升级链与建造订单(所有模组加载完以后，盖掉别的模组对原版矿场升级的设置) ----
    public static void Link()
    {
        try
        {
            if (AssetManager.buildings.get("ec_mine_2") == null) return;
            LinkChain(MineChain);
            LinkChain(LumberChain);
            // Modern Mod 的二级矿场接到第 3 级(老存档里已经升上去的照样能往上升)
            BuildingAsset modern = AssetManager.buildings.get(ModernModMine);
            if (modern != null)
            {
                Tiers[ModernModMine] = 2;
                modern.can_be_upgraded = true;
                modern.upgrade_to = MineChain[2];
                modern.upgrade_level = 1;
            }
            AddOrders();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][矿场伐木场] 升级链设置失败: {exception}");
        }
    }

    private static void LinkChain(string[] chain)
    {
        for (int i = 0; i < chain.Length; i++)
        {
            BuildingAsset asset = AssetManager.buildings.get(chain[i]);
            if (asset == null) continue;
            bool last = i == chain.Length - 1;
            asset.can_be_upgraded = !last;
            asset.upgrade_to = last ? null : chain[i + 1];
            asset.upgraded_from = i == 0 ? asset.upgraded_from : chain[i - 1];
            asset.upgrade_level = i;
        }
    }

    private static readonly Dictionary<string, string> Orders = new()
    {
        ["order_mine"] = "mine",
        ["order_ec_mine_2"] = "ec_mine_2",
        ["order_ec_mine_3"] = "ec_mine_3",
        ["order_ec_mine_4"] = "ec_mine_4",
        ["order_ec_lumber_1"] = "ec_lumber_1",
        ["order_ec_lumber_2"] = "ec_lumber_2",
        ["order_ec_lumber_3"] = "ec_lumber_3",
        ["order_ec_lumber_4"] = "ec_lumber_4",
        ["order_ec_pasture"] = "ec_pasture",
        ["order_ec_slaughterhouse"] = "ec_slaughterhouse"
    };

    private static bool _ordersAdded;

    private static void AddOrders()
    {
        foreach (ArchitectureAsset architecture in AssetManager.architecture_library.list)
        {
            if (architecture == null) continue;
            architecture.building_ids_for_construction ??= new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> pair in Orders)
                if (!architecture.building_ids_for_construction.ContainsKey(pair.Key))
                    architecture.building_ids_for_construction.Add(pair.Key, pair.Value);
        }
        if (_ordersAdded) return;
        _ordersAdded = true;
        foreach (CityBuildOrderAsset template in AssetManager.city_build_orders.list)
        {
            if (template?.list == null) continue;
            // 伐木场：每城一座(工业区越多可以越多)，人口 15、建筑 8 座以后才建
            Neutral(template.addBuilding("order_ec_lumber_1", 1, 15, 8));
            // 牧场每城一座(游牧国家可多建两座)，屠宰场每城一座(工业区越多可以越多)
            Neutral(template.addBuilding("order_ec_pasture", 1, 10, 5));
            Neutral(template.addBuilding("order_ec_slaughterhouse", 1, 20, 8));
            foreach (string order in new[] { "order_mine", "order_ec_mine_2", "order_ec_mine_3", "order_ec_mine_4",
                         "order_ec_lumber_1", "order_ec_lumber_2", "order_ec_lumber_3", "order_ec_lumber_4" })
            {
                template.addUpgrade(order);
                Neutral(template.list[template.list.Count - 1]);
            }
        }
    }

    // 不依赖架构里的其他订单(有的模组种族的架构不全，引用不存在的订单会让整个建造流程报错)
    private static void Neutral(BuildOrder order)
    {
        if (order == null) return;
        order.requirements_orders = new string[0];
        order.requirements_types = new string[0];
    }

    // ---- 规则 ----
    // 第 3 级起必须在工业区才能升上去
    public static bool CanUpgradeHere(Building building, City city)
    {
        BuildingAsset target = AssetManager.buildings.get(building?.asset?.upgrade_to ?? "");
        if (target == null || !Tiers.TryGetValue(target.id, out int tier) || tier < IndustryZoneTier) return true;
        return ZonePlanSystem.Get(city, building.current_tile?.zone) == ZoneUse.Industry;
    }

    public static int ExtraLimit(City city) => Mathf.Min(3, ZonePlanSystem.Count(city, ZoneUse.Industry));

    public static int CountIndustry(City city)
    {
        int count = 0;
        if (city?.buildings == null) return 0;
        foreach (Building building in city.buildings)
            if (building?.asset != null && (IsMine(building.asset) || IsLumber(building.asset))) count++;
        return count;
    }

    // ---- 产出(每年，按户数尺度) ----
    // 建成的矿场的产出倍数之和
    public static float MineOutputFactor(City city) => OutputFactor(city, IsMine);
    public static float LumberOutputFactor(City city) => OutputFactor(city, IsLumber);
    public static float WoodPerYear(City city) => LumberOutputFactor(city) * WoodPerLumberYear;

    // ---- 伐木：伐木场真的砍本城领地里的树 ----
    // 每座伐木场(按等级、工业区折算的产出倍数)每年砍 TreesPerLumberYear 棵，每棵出 WoodPerTree 份木头；
    // 保护区里的树不砍。领地里没树了就只剩一点枯枝杂木(两成)，木头就得从别处买了。
    // 树倒下有原版的倒树动画，砍掉的地方原版的树会慢慢再长回来
    private const float TreesPerLumberYear = 8f;
    private const float WoodPerTree = 3f;
    private const float DeadwoodShare = 0.2f;
    private static readonly Dictionary<long, float> FellCarry = new();

    public static float HarvestTrees(City city, float years)
    {
        float factor = LumberOutputFactor(city);
        if (factor <= 0f || city?.zones == null || city.data == null) return 0f;
        FellCarry.TryGetValue(city.data.id, out float carry);
        float want = factor * TreesPerLumberYear * years + carry;
        int target = Mathf.FloorToInt(want);
        FellCarry[city.data.id] = want - target;
        int cut = 0;
        if (target > 0)
        {
            // 先砍树最多的区块
            var zones = new List<(TileZone zone, int trees)>();
            foreach (TileZone zone in city.zones)
            {
                if (zone == null || ZonePlanSystem.Get(city, zone) == ZoneUse.Reserve) continue;
                int count = zone.getHashset(BuildingList.Trees)?.Count ?? 0;
                if (count > 0) zones.Add((zone, count));
            }
            zones.Sort((a, b) => b.trees.CompareTo(a.trees));
            var felled = new List<Building>();
            foreach ((TileZone zone, int _) in zones)
            {
                foreach (Building tree in zone.getHashset(BuildingList.Trees))
                {
                    if (felled.Count >= target) break;
                    if (tree?.asset == null || tree.chopped || tree.isRuin()) continue;
                    felled.Add(tree);
                }
                if (felled.Count >= target) break;
            }
            foreach (Building tree in felled)
            {
                try
                {
                    tree.chopTree();
                    cut++;
                }
                catch (Exception exception)
                {
                    LogService.LogWarning($"[EmpireCraft][伐木] 砍树失败: {exception.Message}");
                    break;
                }
            }
        }
        float shortfall = Mathf.Max(0f, target - cut);
        return cut * WoodPerTree + shortfall * WoodPerTree * DeadwoodShare;
    }

    // 森林多的城可以多建伐木场：领地里每 40 棵树多许一座，最多多 4 座
    public static int ForestExtraLimit(City city)
    {
        if (city?.zones == null) return 0;
        int trees = 0;
        foreach (TileZone zone in city.zones)
            if (zone != null && ZonePlanSystem.Get(city, zone) != ZoneUse.Reserve)
                trees += zone.getHashset(BuildingList.Trees)?.Count ?? 0;
        return Mathf.Min(4, trees / 40);
    }

    private static float OutputFactor(City city, Func<BuildingAsset, bool> kind)
    {
        float total = 0f;
        if (city?.buildings == null) return 0f;
        foreach (Building building in city.buildings)
        {
            if (building?.asset == null || !kind(building.asset) || building.isUnderConstruction()) continue;
            int tier = Mathf.Clamp(Tier(building.asset), 1, TierOutput.Length);
            float factor = TierOutput[tier - 1];
            if (ZonePlanSystem.Get(city, building.current_tile?.zone) == ZoneUse.Industry) factor *= IndustryZoneBonus;
            total += factor;
        }
        return total;
    }
}

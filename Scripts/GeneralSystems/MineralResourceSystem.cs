using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 战略矿产：铜、煤、硝石、硫磺、石油、铝土、稀土、铀。
//   - 矿场按等级开采：1 级起铜，2 级(矿井)起煤，3 级(水力矿场)起硝石、硫磺，4 级(蒸汽煤矿)起石油，
//     5 级(现代矿业工厂)起铝土、稀土、铀；产量乘矿场的产出倍数(等级、工业区，见 IndustryBuildingSystem)；
//   - 矿藏因城而异，按地形定概率、按城市编号固定地取舍(同一座城每次结果一样，地形大变才会变)：
//     山地(岩石、山脉、丘陵)多铜、铝土、稀土、铀、硫磺；荒漠草原多硝石、石油；沼泽丛林多煤、石油、铝土；
//     山林茂密多煤；沿海多石油；地狱地貌多硫磺。没有的城要靠市场从别处买，或者干脆用不上；
//   - 矿藏会枯竭：每处矿藏有储量(Reserves)，采完就枯竭，进入冷却(各矿的冷却年数见 Reserves)，
//     冷却期间不能开采；冷却结束视为找到新矿脉，储量恢复。等级越高的矿场采得越快、越早枯竭；
//   - 科技需要的材料(Technology/TechTree.json 的 materials)把真实矿产列为首选来源，原来的替代来源照旧有效。
public static class MineralResourceSystem
{
    // (资源 id, 开采所需矿场等级, 每座矿场每年产量, 有此矿藏的城市比例)
    public static readonly (string id, int tier, float perYear, float chance)[] Minerals =
    {
        ("copper", 1, 3f, 0.6f),
        ("coal", 2, 5f, 0.55f),
        ("saltpeter", 3, 2f, 0.35f),
        ("sulfur", 3, 2f, 0.35f),
        ("oil", 4, 4f, 0.3f),
        ("aluminium", 5, 3f, 0.4f),
        ("rare_earth", 5, 1f, 0.2f),
        ("uranium", 5, 0.5f, 0.15f)
    };

    // (储量, 枯竭后的冷却年数)：硝石床会再生，冷却短；石油、铀一枯竭就要很久才能找到新矿
    private static (float reserve, int cooldown) Reserves(string id) => id switch
    {
        "copper" => (300f, 20),
        "coal" => (500f, 30),
        "saltpeter" => (150f, 10),
        "sulfur" => (150f, 15),
        "oil" => (400f, 50),
        "aluminium" => (300f, 25),
        "rare_earth" => (60f, 40),
        "uranium" => (30f, 60),
        _ => (200f, 20)
    };

    // 枯竭了还在冷却：返回剩余年数，否则 0
    public static int CooldownLeft(CityPopulationData data, string id)
    {
        if (data?.deposit_depleted_at == null || !data.deposit_depleted_at.TryGetValue(id, out double at)) return 0;
        int left = Reserves(id).cooldown - Date.getYearsSince(at);
        return Mathf.Max(0, left);
    }

    // 查询(界面用)：总储量、枯竭后的冷却年数、开采所需矿场等级、剩余储量
    public static float ReserveOf(string id) => Reserves(id).reserve;
    public static int CooldownYears(string id) => Reserves(id).cooldown;

    public static int RequiredTier(string id)
    {
        foreach ((string mineral, int tier, _, _) in Minerals)
            if (mineral == id) return tier;
        return 0;
    }

    public static float RemainingAmount(CityPopulationData data, string id) =>
        RemainingShare(data, id) * Reserves(id).reserve;

    // 剩余储量比例(0~1)
    public static float RemainingShare(CityPopulationData data, string id)
    {
        if (CooldownLeft(data, id) > 0) return 0f;
        // 冷却已结束、下次开采才重置储量：此时已发现新矿脉，按储量回满显示
        if (data?.deposit_depleted_at != null && data.deposit_depleted_at.ContainsKey(id)) return 1f;
        float mined = data?.deposit_mined != null && data.deposit_mined.TryGetValue(id, out float m) ? m : 0f;
        return Mathf.Clamp01(1f - mined / Reserves(id).reserve);
    }

    public static void Init()
    {
        try
        {
            ResourceAsset template = AssetManager.resources.get("common_metals");
            if (template == null) return;
            foreach ((string id, _, _, _) in Minerals)
            {
                if (AssetManager.resources.get(id) != null) continue;
                ResourceAsset asset = AssetManager.resources.clone(id, "common_metals");
                asset.path_icon = "resources/iconRes_" + id;
                asset.restore_nutrition = 0;
                asset.restore_health = 0f;
                asset.restore_mana = 0;
                asset.restore_stamina = 0;
                asset.restore_happiness = 0;
                asset.money_cost = id switch
                {
                    "copper" or "coal" => 2,
                    "saltpeter" or "sulfur" => 3,
                    "oil" or "aluminium" => 4,
                    _ => 8
                };
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][矿产] 资源定义失败: {exception}");
        }
    }

    // 城市地形(各 0~1)，每年重算一次
    private struct TerrainProfile
    {
        public double At;
        public float Rocky, Desert, Wet, Forest, Coast, Infernal;
    }

    private static readonly Dictionary<City, TerrainProfile> Terrain = new();

    private static TerrainProfile TerrainOf(City city)
    {
        double now = World.world.getCurWorldTime();
        if (Terrain.TryGetValue(city, out TerrainProfile cached) && now >= cached.At && Date.getYearsSince(cached.At) < 1)
            return cached;
        int land = 0, water = 0, rocky = 0, desert = 0, wet = 0, infernal = 0, trees = 0;
        if (city.zones != null)
            foreach (TileZone zone in city.zones)
            {
                if (zone?.tiles == null) continue;
                trees += zone.getHashset(BuildingList.Trees)?.Count ?? 0;
                foreach (WorldTile tile in zone.tiles)
                {
                    if (tile?.Type == null) continue;
                    if (tile.Type.liquid)
                    {
                        water++;
                        continue;
                    }
                    land++;
                    string biome = tile.Type.biome_asset?.id ?? "";
                    if (tile.Type.rocks || tile.Type.mountains || biome is "biome_hill" or "biome_rocklands") rocky++;
                    else if (biome is "biome_desert" or "biome_sand" or "biome_savanna" or "biome_wasteland") desert++;
                    else if (biome is "biome_swamp" or "biome_jungle") wet++;
                    else if (biome is "biome_infernal") infernal++;
                }
            }
        int zones = Mathf.Max(1, city.zones?.Count ?? 1);
        float share(int n) => land == 0 ? 0f : Mathf.Clamp01(n * 3f / land);
        var profile = new TerrainProfile
        {
            At = now,
            Rocky = share(rocky),
            Desert = share(desert),
            Wet = share(wet),
            Infernal = share(infernal),
            Forest = Mathf.Clamp01(trees / (zones * 8f)),
            Coast = land + water == 0 ? 0f : Mathf.Clamp01(water * 4f / (land + water))
        };
        Terrain[city] = profile;
        return profile;
    }

    // 某种矿藏在本城出现的概率(按地形)
    public static float ChanceFor(City city, string id, float baseChance)
    {
        TerrainProfile t = TerrainOf(city);
        float chance = id switch
        {
            "copper" => 0.3f + 0.6f * t.Rocky,
            "coal" => 0.25f + 0.4f * t.Wet + 0.35f * t.Forest + 0.25f * t.Rocky,
            "saltpeter" => 0.1f + 0.7f * t.Desert,
            "sulfur" => 0.1f + 0.4f * t.Rocky + 0.7f * t.Infernal,
            "oil" => 0.08f + 0.5f * t.Desert + 0.4f * t.Wet + 0.3f * t.Coast,
            "aluminium" => 0.15f + 0.45f * t.Rocky + 0.35f * t.Wet,
            "rare_earth" => 0.05f + 0.35f * t.Rocky,
            "uranium" => 0.04f + 0.3f * t.Rocky + 0.15f * t.Desert,
            _ => baseChance
        };
        return Mathf.Clamp(chance, 0.02f, 0.9f);
    }

    // 城市有没有某种矿藏：按地形定概率，按城市编号和资源 id 固定地取舍；第一次判定后存进存档固定下来，
    // 之后砍树、挖湖改变地形也不会让矿藏忽有忽无
    public static bool HasDeposit(City city, string id, float baseChance)
    {
        if (city?.data == null) return false;
        CityPopulationData data = CityPopulationSystem.Get(city);
        if (data != null)
        {
            data.deposits_fixed ??= new Dictionary<string, bool>();
            if (data.deposits_fixed.TryGetValue(id, out bool known)) return known;
            bool found = RollDeposit(city, id, baseChance);
            data.deposits_fixed[id] = found;
            return found;
        }
        return RollDeposit(city, id, baseChance);
    }

    // 本城有没有任何一种模组矿藏
    public static bool HasAnyDeposit(City city)
    {
        foreach ((string id, _, _, float chance) in Minerals)
            if (HasDeposit(city, id, chance)) return true;
        return false;
    }

    private static bool RollDeposit(City city, string id, float baseChance)
    {
        float chance = ChanceFor(city, id, baseChance);
        unchecked
        {
            int hash = (int)city.data.id * 73856093 ^ id.GetHashCode() * 19349663;
            hash ^= hash >> 13;
            hash *= 0x5bd1e995;
            hash ^= hash >> 15;
            return (hash & 0xffff) / 65536f < chance;
        }
    }

    // 本城最高矿场等级
    public static int MineTier(City city)
    {
        int tier = 0;
        if (city?.buildings == null) return 0;
        foreach (Building building in city.buildings)
            if (building?.asset != null && IndustryBuildingSystem.IsMine(building.asset) && !building.isUnderConstruction())
                tier = Mathf.Max(tier, Mathf.Max(1, IndustryBuildingSystem.Tier(building.asset)));
        return tier;
    }

    // 每年开采(由人口经济结算按经过的年数调用)
    // 钢铁厂(WarBox)：无小人模式下没有工人，按座数每年用煤和金属炼钢，成品折成金属
    // (每座每年：煤 4 + 金属 4 → 金属 12，原料不够按比例减产)
    private const float SteelCoalPerYear = 4f;
    private const float SteelMetalInPerYear = 4f;
    private const float SteelMetalOutPerYear = 12f;

    public static void SmeltSteel(City city, CityPopulationData data, float years)
    {
        int mills = city?.countBuildingsType("type_steel_mill", true) ?? 0;
        if (mills <= 0 || AssetManager.resources.get("coal") == null) return;
        float wantCoal = mills * SteelCoalPerYear * years, wantMetal = mills * SteelMetalInPerYear * years;
        int coal = Mathf.Min(Mathf.FloorToInt(wantCoal), city.getResourcesAmount("coal"));
        int metal = Mathf.Min(Mathf.FloorToInt(wantMetal), city.getResourcesAmount("common_metals"));
        if (coal <= 0 || metal <= 0) return;
        float share = Mathf.Min(coal / Mathf.Max(1f, wantCoal), metal / Mathf.Max(1f, wantMetal));
        int useCoal = Mathf.Max(1, Mathf.RoundToInt(wantCoal * share));
        int useMetal = Mathf.Max(1, Mathf.RoundToInt(wantMetal * share));
        city.takeResource("coal", Mathf.Min(useCoal, coal));
        city.takeResource("common_metals", Mathf.Min(useMetal, metal));
        // 收入只算炼钢增加的价值
        PopulationEconomySystem.ConsumeInputs(city, data, "coal", Mathf.Min(useCoal, coal));
        PopulationEconomySystem.ConsumeInputs(city, data, "common_metals", Mathf.Min(useMetal, metal));
        PopulationEconomySystem.Deposit(city, data, "common_metals", mills * SteelMetalOutPerYear * years * share);
    }

    public static void Produce(City city, CityPopulationData data, float years)
    {
        SmeltSteel(city, data, years);
        int tier = MineTier(city);
        if (tier <= 0) return;
        float output = IndustryBuildingSystem.MineOutputFactor(city);
        if (output <= 0f) return;
        data.deposit_mined ??= new Dictionary<string, float>();
        data.deposit_depleted_at ??= new Dictionary<string, double>();
        foreach ((string id, int need, float perYear, float chance) in Minerals)
        {
            if (tier < need || !HasDeposit(city, id, chance) || AssetManager.resources.get(id) == null) continue;
            // 冷却中：不能开采；冷却结束：找到新矿脉，储量恢复
            if (data.deposit_depleted_at.ContainsKey(id))
            {
                if (CooldownLeft(data, id) > 0) continue;
                data.deposit_depleted_at.Remove(id);
                data.deposit_mined[id] = 0f;
            }
            (float reserve, _) = Reserves(id);
            data.deposit_mined.TryGetValue(id, out float mined);
            float amount = Mathf.Min(output * perYear * years, reserve - mined);
            if (amount <= 0f) continue;
            PopulationEconomySystem.Deposit(city, data, id, amount);
            mined += amount;
            data.deposit_mined[id] = mined;
            if (mined < reserve) continue;
            data.deposit_depleted_at[id] = World.world.getCurWorldTime();
            try
            {
                EmpireCraft.Scripts.HelperFunc.TranslateHelper.LogEventMessage(
                    string.Format(NeoModLoader.General.LM.Get("deposit_depleted"), city.GetCityName(),
                        NeoModLoader.General.LM.Get(id), Reserves(id).cooldown), city.kingdom);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][矿产] 记录枯竭失败: {exception.Message}");
            }
        }
    }

    // 城市面板：本城有哪些矿藏(能否开采、剩余储量、枯竭冷却剩余年数)
    public static List<(string id, bool minable, float remaining, int cooldown)> Deposits(City city)
    {
        var list = new List<(string, bool, float, int)>();
        int tier = MineTier(city);
        CityPopulationData data = CityPopulationSystem.Get(city);
        foreach ((string id, int need, _, float chance) in Minerals)
            if (HasDeposit(city, id, chance))
                list.Add((id, tier >= need, RemainingShare(data, id), CooldownLeft(data, id)));
        return list;
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 畜牧与屠宰：城市的肉、皮革、骨头按地形和生产力自动产出，不靠地图上的动物
// (无小人模式下全图野生动物每城只留两只点缀，见 NoCommonersPatch.WildCap)。
//   - 牧场(大型建筑，建在农耕区或空地)：牧群按本城草场(空着的可耕地)多少决定容量，每月自行繁殖补栏，
//     每年按头数产肉和皮革；领地里碰到的家畜也顺手圈进牧群；
//   - 屠宰场(建在工业区或空地，制革科技解锁)：按本城山林(树木)多少每月猎得野味，折成肉、皮革、骨头；
//     领地里的野兽一并猎取；牧群超过容量八成时宰掉多余的部分；
//   - 产出乘农业生产力(古代 1，现代 3)；
//   - 游牧制度(制度线 Youmu)的国家：牧场容量、圈养数量翻倍，牧群繁殖快一半，每城可多建两座牧场。
// 每月一次：先把全图动物按所在城市领地分组，再逐城结算(分帧)。
public static class AnimalHusbandrySystem
{
    public const string PastureType = "type_pasture";
    public const string SlaughterhouseType = "type_slaughterhouse";

    private const float HerdPerPasture = 60f;
    private const int CapturePerPasture = 15;
    private const int HuntPerSlaughterhouse = 25;
    private const float HerdGrowthPerMonth = 0.015f;
    // 牧群每头每年：肉、皮革
    private const float HerdMeatPerYear = 0.6f;
    private const float HerdLeatherPerYear = 0.25f;
    // 牧群超过容量这么多时，屠宰场宰到 SlaughterDownTo
    private const float SlaughterAbove = 0.8f;
    private const float SlaughterDownTo = 0.6f;

    // 可以圈养的家畜
    private static readonly HashSet<string> Livestock = new()
        { "sheep", "cow", "chicken", "goat", "alpaca", "buffalo", "ostrich", "rabbit", "capybara" };

    // 按野生阵营区分体型：(肉, 皮革, 骨头, 折合牧群头数)
    private static readonly HashSet<string> Large = new() { "cow", "buffalo", "rhino", "bear", "crocodile", "seal" };
    private static readonly HashSet<string> Medium = new()
    {
        "sheep", "goat", "alpaca", "ostrich", "wolf", "hyena", "dog", "fox", "monkey", "penguin", "capybara", "cat",
        "raccoon", "armadillo", "turtle", "snake"
    };
    private static readonly HashSet<string> Small = new()
        { "chicken", "rabbit", "rat", "frog", "crab", "lemon_snail", "piranha", "scorpion" };

    // 农田害虫：城市领地内农田上的每月清除(除虫)，每城最多 PestsPerCity 只
    private static readonly HashSet<string> Pests = new() { "fly", "grasshopper", "beetle", "insect" };
    private const int PestsPerCity = 60;

    private static bool IsPest(ActorAsset asset) =>
        asset != null && !asset.civ && (Pests.Contains(asset.id) || Pests.Contains(asset.kingdom_id_wild ?? ""));

    public static bool IsGame(ActorAsset asset)
    {
        if (asset == null || asset.civ || asset.is_boat) return false;
        string wild = asset.kingdom_id_wild ?? "";
        return Large.Contains(wild) || Medium.Contains(wild) || Small.Contains(wild);
    }

    private static (float meat, float leather, float bones, float head) Yield(ActorAsset asset)
    {
        string wild = asset.kingdom_id_wild ?? "";
        if (Large.Contains(wild)) return (6f, 3f, 2f, 2f);
        if (Medium.Contains(wild)) return (3f, 1.5f, 1f, 1f);
        return (1f, 0.4f, 0.3f, 0.3f);
    }

    public static bool IsNomadic(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.wild || kingdom.isRekt()) return false;
        try
        {
            return InstitutionSystem.GetCultureLine(CultureService.GetRealmCulture(kingdom)) == "Youmu";
        }
        catch
        {
            return false;
        }
    }

    public static int CountBuildings(City city, string type)
    {
        int count = 0;
        if (city?.buildings == null) return 0;
        foreach (Building building in city.buildings)
            if (building?.asset?.type == type && !building.isUnderConstruction()) count++;
        return count;
    }

    public static float HerdCapacity(City city) =>
        CountBuildings(city, PastureType) * HerdPerPasture * (IsNomadic(city?.kingdom) ? 2f : 1f) *
        (0.4f + 1.2f * Rangeland(city));

    // 地形：草场比例(城市区块里空着的可耕地占比)、山林程度(每块区块平均树木数 / 8，最多 1)。每月算一次缓存
    private static readonly Dictionary<City, (double at, float range, float wild)> Terrain = new();

    public static float Rangeland(City city) => TerrainOf(city).range;
    public static float Wildness(City city) => TerrainOf(city).wild;

    private static (double at, float range, float wild) TerrainOf(City city)
    {
        if (city?.zones == null || city.zones.Count == 0) return (0d, 0f, 0f);
        double now = World.world.getCurWorldTime();
        if (Terrain.TryGetValue(city, out (double at, float range, float wild) cached) && now >= cached.at &&
            Date.getMonthsSince(cached.at) < 1) return cached;
        int tiles = 0, open = 0, trees = 0;
        foreach (TileZone zone in city.zones)
        {
            if (zone?.tiles == null) continue;
            trees += zone.getHashset(BuildingList.Trees)?.Count ?? 0;
            foreach (WorldTile tile in zone.tiles)
            {
                if (tile?.Type == null || tile.Type.liquid) continue;
                tiles++;
                if (tile.Type.can_be_farm && !tile.Type.farm_field && !tile.hasBuilding()) open++;
            }
        }
        (double, float, float) result = (now, tiles == 0 ? 0f : (float)open / tiles,
            Mathf.Clamp01(trees / (city.zones.Count * 8f)));
        Terrain[city] = result;
        return result;
    }

    // 狩猎：每座屠宰场每月按山林程度猎得的野味(折合中型野兽只数)
    private const float HuntPerSlaughterhouseMonth = 6f;
    // 牧群每月自行补栏(占容量的比例)
    private const float RestockPerMonth = 0.04f;

    // ---- 每月一轮 ----
    private static readonly Queue<City> Pending = new();
    private static readonly Dictionary<City, List<Actor>> AnimalsByCity = new();
    private static readonly Dictionary<City, List<Actor>> PestsByCity = new();
    private static double _lastPass = -1d;
    private const double SliceBudgetMs = 1.5d;
    private static object _world;
    private static readonly HashSet<City> Wanted = new();
    private static List<City> _groupCities;
    private static List<Actor> _groupActors;
    private static int _groupCityIndex, _groupActorIndex, _groupActorLimit;
    private static bool _pestControl;

    public static void ResetWorldState()
    {
        _world = World.world;
        _lastPass = -1d;
        Pending.Clear();
        Wanted.Clear();
        AnimalsByCity.Clear();
        PestsByCity.Clear();
        Terrain.Clear();
        _groupCities = null;
        _groupActors = null;
    }

    public static void Tick()
    {
        MapBox world = World.world;
        if (world?.cities == null || Config.paused || !Config.game_loaded || SmoothLoader.isLoading()) return;
        if (!ReferenceEquals(_world, world)) ResetWorldState();
        double now = world.getCurWorldTime();
        if (Pending.Count == 0 && _groupCities == null)
        {
            if (_lastPass >= 0d && now >= _lastPass && Date.getMonthsSince(_lastPass) < 1) return;
            _lastPass = now;
            AnimalsByCity.Clear();
            PestsByCity.Clear();
            Wanted.Clear();
            _groupCities = new List<City>(world.cities);
            _groupActors = world.units.getSimpleList();
            _groupActorLimit = _groupActors.Count;
            _groupCityIndex = _groupActorIndex = 0;
            _pestControl = CityPopulationSystem.AbstractPopulationEnabled;
        }
        Stopwatch watch = Stopwatch.StartNew();
        if (_groupCities != null)
        {
            while (_groupCityIndex < _groupCities.Count && (!_pestControl ||
                   watch.Elapsed.TotalMilliseconds < SliceBudgetMs && SimulationFrameBudget.HasTime))
            {
                City city = _groupCities[_groupCityIndex++];
                if (city?.data == null || city.isRekt() || city.kingdom == null || city.kingdom.wild) continue;
                Pending.Enqueue(city);
                if (CountBuildings(city, PastureType) > 0 || CountBuildings(city, SlaughterhouseType) > 0) Wanted.Add(city);
            }
            if (_groupCityIndex < _groupCities.Count) return;
            if (Wanted.Count == 0 && !_pestControl) _groupActorIndex = _groupActorLimit;
            while (_groupActorIndex < Math.Min(_groupActorLimit, _groupActors.Count) &&
                   (!_pestControl || watch.Elapsed.TotalMilliseconds < SliceBudgetMs && SimulationFrameBudget.HasTime))
                GroupAnimal(_groupActors[_groupActorIndex++]);
            if (_groupActorIndex < Math.Min(_groupActorLimit, _groupActors.Count)) return;
            _groupCities = null;
            _groupActors = null;
            if (!_pestControl) watch.Restart(); // 普通模式维持一次分组后开始计畜牧结算预算。
        }
        while (Pending.Count > 0 && watch.Elapsed.TotalMilliseconds < SliceBudgetMs && SimulationFrameBudget.HasTime)
        {
            City city = Pending.Dequeue();
            if (city?.data == null || city.isRekt()) continue;
            try
            {
                Settle(city);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][畜牧] 结算失败({city.data?.name}): {exception.Message}");
            }
        }
        if (Pending.Count == 0)
        {
            if (Terrain.Count > world.cities.Count * 2) Terrain.Clear();
            Wanted.Clear();
            AnimalsByCity.Clear();
            PestsByCity.Clear();
        }
    }

    // 只看有牧场或屠宰场的城的领地，省得每月给所有动物分组
    private static void GroupAnimal(Actor actor)
    {
            if (actor?.data == null || actor.isRekt() || !actor.isAlive() || actor.asset == null) return;
            if (actor.isFavorite() || actor.isCameraFollowingUnit()) return;
            WorldTile tile = actor.current_tile;
            City owner = tile?.zone?.city;
            if (owner == null) return;
            // 无小人模式没有农民赶虫：农田(田地、规划农耕区)上的害虫由城市每月清除
            if (_pestControl && IsPest(actor.asset) &&
                (tile.Type != null && tile.Type.farm_field || ZonePlanSystem.Get(owner, tile.zone) == ZoneUse.Farm))
            {
                if (!PestsByCity.TryGetValue(owner, out List<Actor> pests)) PestsByCity[owner] = pests = new List<Actor>();
                if (pests.Count < PestsPerCity) pests.Add(actor);
                return;
            }
            if (!IsGame(actor.asset) || !Wanted.Contains(owner)) return;
            if (!AnimalsByCity.TryGetValue(owner, out List<Actor> list)) AnimalsByCity[owner] = list = new List<Actor>();
            list.Add(actor);
    }

    private static void Settle(City city)
    {
        if (PestsByCity.TryGetValue(city, out List<Actor> pestList))
            foreach (Actor pest in pestList)
                if (pest != null && !pest.isRekt() && pest.isAlive() && pest.current_tile?.zone?.city == city) Remove(pest);
        CityPopulationData data = CityPopulationSystem.Get(city);
        if (data == null) return;
        int pastures = CountBuildings(city, PastureType);
        int slaughterhouses = CountBuildings(city, SlaughterhouseType);
        bool nomadic = IsNomadic(city.kingdom);
        float capacity = HerdCapacity(city);
        if (data.herd > capacity) data.herd = capacity;
        AnimalsByCity.TryGetValue(city, out List<Actor> animals);
        animals?.RemoveAll(animal => animal?.asset == null || animal.isRekt() || !animal.isAlive() ||
            animal.current_tile?.zone?.city != city);
        float meat = 0f, leather = 0f, bones = 0f;

        // 牧场：圈进家畜
        if (pastures > 0 && animals != null)
        {
            int limit = pastures * CapturePerPasture * (nomadic ? 2 : 1);
            for (int i = animals.Count - 1; i >= 0 && limit > 0 && data.herd < capacity; i--)
            {
                Actor animal = animals[i];
                if (animal?.asset == null || !Livestock.Contains(animal.asset.kingdom_id_wild ?? "")) continue;
                data.herd += Yield(animal.asset).head;
                Remove(animal);
                animals.RemoveAt(i);
                limit--;
            }
        }
        // 牧群：自行补栏、繁殖、产出
        if (capacity > 0f)
            data.herd = Mathf.Min(capacity, data.herd + capacity * RestockPerMonth * (nomadic ? 1.5f : 1f) +
                                            data.herd * HerdGrowthPerMonth * (nomadic ? 1.5f : 1f));
        if (data.herd > 0f)
        {
            meat += data.herd * HerdMeatPerYear / 12f;
            leather += data.herd * HerdLeatherPerYear / 12f;
        }
        // 屠宰场：猎取领地里的野兽，宰掉多余的牧群
        if (slaughterhouses > 0)
        {
            // 按山林程度自动猎得的野味(折合中型野兽：肉 3、皮 1.5、骨 1)
            float game = slaughterhouses * HuntPerSlaughterhouseMonth * (0.3f + Wildness(city));
            meat += game * 3f;
            leather += game * 1.5f;
            bones += game;
            int limit = slaughterhouses * HuntPerSlaughterhouse;
            if (animals != null)
                for (int i = animals.Count - 1; i >= 0 && limit > 0; i--)
                {
                    Actor animal = animals[i];
                    if (animal?.asset == null) continue;
                    (float m, float l, float b, _) = Yield(animal.asset);
                    meat += m;
                    leather += l;
                    bones += b;
                    Remove(animal);
                    animals.RemoveAt(i);
                    limit--;
                }
            if (capacity > 0f && data.herd > capacity * SlaughterAbove)
            {
                float culled = data.herd - capacity * SlaughterDownTo;
                data.herd -= culled;
                meat += culled * 3f;
                leather += culled * 1.5f;
                bones += culled;
            }
        }
        float productivity = FarmlandSystem.Productivity(city);
        meat *= productivity;
        leather *= productivity;
        bones *= productivity;
        PopulationEconomySystem.Deposit(city, data, "meat", meat);
        PopulationEconomySystem.Deposit(city, data, "leather", leather);
        PopulationEconomySystem.Deposit(city, data, "bones", bones);
        data.last_husbandry_meat = meat;
    }

    // 不计入死亡统计、不留尸体
    private static void Remove(Actor animal)
    {
        try
        {
            animal.die(true, AttackType.Other, false, false);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][畜牧] 移除动物失败: {exception.Message}");
        }
    }
}

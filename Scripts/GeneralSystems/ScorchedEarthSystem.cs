using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 烧杀抢掠(无小人模式)：敌军占着一座城的区块时，若占领国的君主残暴(邪恶、嗜血、残忍、疯狂)，
// 或者仇外而这座城是异族(主流文化或物种不同)，军队纵兵烧杀——每月按敌占区块的比例：
//   杀伤背景人口 KillRatePerMonth × 敌占比例；烧掉敌占区块里的庄稼；抢走城中存粮 PlunderRate × 敌占比例。
// 死难人数记入屠城之恨(见 MassacreSystem)：日后由屠城者统治时城市忠诚下降。
// 庄稼被烧、存粮被抢、敌占区块又不能耕种(见 FarmlandSystem)，城市随之缺粮闹饥荒。
public static class ScorchedEarthSystem
{
    private const float KillRatePerMonth = 0.08f;
    private const float PlunderRate = 0.5f;
    private static readonly string[] CruelTraits = { "evil", "bloodlust", "psychopath", "madness", "savage" };

    public static bool WillScorch(Kingdom occupier, City city)
    {
        Actor king = occupier?.king;
        if (king == null || king.isRekt() || city?.kingdom == null) return false;
        foreach (string trait in CruelTraits)
            if (king.hasTrait(trait)) return true;
        if (!king.hasXenophobic()) return false;
        string cityCulture = CultureService.GetMainCulture(city, initialize: false);
        string ownCulture = CultureService.GetRealmCulture(occupier);
        bool foreignCulture = CultureService.IsValidCulture(cityCulture) && cityCulture != ownCulture;
        bool foreignSpecies = city.getSpecies() != null && king.asset != null && city.getSpecies() != king.asset.id;
        return foreignCulture || foreignSpecies;
    }

    // 敌占区块(区块 id → 占领国)；没有敌占时返回 null
    public static Dictionary<int, long> OccupiedZones(City city)
    {
        Dictionary<int, long> owners = city?.GetOrCreate().OccupiedZoneOwners;
        return owners == null || owners.Count == 0 ? null : owners;
    }

    public static bool IsOccupiedZone(City city, TileZone zone)
    {
        Dictionary<int, long> owners = OccupiedZones(city);
        return owners != null && zone != null && owners.ContainsKey(zone.id);
    }

    // 每月随无小人模式的城市结算调用
    public static void Settle(City city)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || city?.zones == null || city.zones.Count == 0) return;
        Dictionary<int, long> owners = OccupiedZones(city);
        if (owners == null) return;
        try
        {
            var byKingdom = new Dictionary<long, int>();
            foreach (long id in owners.Values) byKingdom[id] = byKingdom.TryGetValue(id, out int n) ? n + 1 : 1;
            foreach (KeyValuePair<long, int> pair in byKingdom)
            {
                Kingdom occupier = World.world.kingdoms.get(pair.Key);
                if (occupier == null || occupier.isRekt() || !occupier.isEnemy(city.kingdom) || !WillScorch(occupier, city))
                    continue;
                float share = Mathf.Clamp01(pair.Value / (float)city.zones.Count);
                int killed = Kill(city, KillRatePerMonth * share);
                BurnCrops(city, owners, pair.Key);
                Plunder(city, PlunderRate * share);
                if (killed > 0) MassacreSystem.RecordBackgroundMassacre(city, occupier, killed);
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][烧杀] 结算失败({city.data?.name}): {exception.Message}");
        }
    }

    private static int Kill(City city, float rate)
    {
        CityPopulationData data = CityPopulationSystem.Get(city);
        if (data?.groups == null || rate <= 0f) return 0;
        float killed = 0f;
        foreach (PopGroup group in data.groups)
            killed += CityPopulationSystem.RemoveBackground(group, group.Background * rate);
        return Mathf.RoundToInt(killed);
    }

    private static void BurnCrops(City city, Dictionary<int, long> owners, long occupierId)
    {
        foreach (TileZone zone in city.zones)
        {
            if (zone?.tiles == null || !owners.TryGetValue(zone.id, out long id) || id != occupierId) continue;
            foreach (WorldTile tile in zone.tiles)
            {
                Building crop = tile?.building;
                if (crop?.asset != null && crop.asset.wheat) crop.startDestroyBuilding();
            }
        }
    }

    private static void Plunder(City city, float rate)
    {
        if (rate <= 0f || AssetManager.resources?.list == null) return;
        foreach (ResourceAsset asset in AssetManager.resources.list)
        {
            if (asset == null || asset.type != ResType.Food) continue;
            int have = city.getResourcesAmount(asset.id);
            int take = Mathf.FloorToInt(have * rate);
            if (take > 0) city.takeResource(asset.id, take);
        }
    }
}

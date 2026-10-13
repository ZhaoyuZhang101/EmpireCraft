using System.Collections.Generic;
using EmpireCraft.Scripts.GeneralSystems;
using UnityEngine;

namespace EmpireCraft.Scripts.Layer;

// 纯净地图模式下的城市发展(群友建议：建筑都隐藏了，看不出城市发展到哪一步)：
//   不另画图层(以前城下画一排图标，会闪、在帝国图层上很乱)，改为并进城市铭牌——
//   铭牌上原版种族头像的位置换成一枚"城市性质"徽记，每城一枚：工业城(有工厂) > 港城(有码头) > 商城(有市场)
//   > 宗教城(有寺庙) > 军镇(有兵营) > 民居；铭牌文字后写民居最高等级(见 EmpireCraftNamePlateLibrary.showTextCity)。
//   城市铭牌只在城市、财富图层出现，帝国、国家图层不受影响。图标由 IconForge 生成(ui/icons/citydev/*)。
//   每座城的建筑统计缓存两秒。
public static class CityDevelopment
{
    private const float CacheSeconds = 2f;

    private sealed class Stats
    {
        public int house = -1, factories;
        public bool market, port, barracks, temple;
        public float at;
    }

    private static readonly Dictionary<long, Stats> Cache = new();
    private static readonly Dictionary<string, Sprite> Sprites = new();

    // 城市性质徽记(没有建筑返回 null)
    public static Sprite Emblem(City city)
    {
        Stats stats = StatsOf(city);
        if (stats == null) return null;
        string id = stats.factories > 0 ? "factory" : stats.port ? "port" : stats.market ? "market" :
            stats.temple ? "temple" : stats.barracks ? "barracks" : stats.house >= 0 ? "house" : null;
        return id == null ? null : SpriteOf(id);
    }

    // 民居最高等级(从 1 起，没有民居为 -1)
    public static int HouseTier(City city) => StatsOf(city)?.house ?? -1;

    private static Stats StatsOf(City city)
    {
        if (city?.data == null) return null;
        float now = Time.unscaledTime;
        if (Cache.TryGetValue(city.data.id, out Stats cached) && now - cached.at < CacheSeconds) return cached;
        var stats = new Stats { at = now };
        if (city.buildings != null)
            foreach (Building building in city.buildings)
            {
                BuildingAsset asset = building?.asset;
                if (asset == null || building.isUnderConstruction()) continue;
                switch (asset.type)
                {
                    case "type_house":
                        stats.house = Mathf.Max(stats.house, Tier(asset));
                        break;
                    case "type_market":
                        stats.market = true;
                        break;
                    case "type_docks":
                        stats.port = true;
                        break;
                    case "type_barracks":
                        stats.barracks = true;
                        break;
                    case "type_temple":
                        stats.temple = true;
                        break;
                }
                if (TechnologySystem.IsFactory(asset)) stats.factories++;
            }
        if (Cache.Count > 4000) Cache.Clear();
        Cache[city.data.id] = stats;
        return stats;
    }

    // 民居等级(从 1 起)：原版 house_*_N 为 N+1；近代模组的 Nhouse_*(接在原版之后)为 N+1；都不是就按升级等级
    private static int Tier(BuildingAsset asset)
    {
        string id = asset.id ?? "";
        int digits = 0;
        while (digits < id.Length && char.IsDigit(id[digits])) digits++;
        if (digits > 0 && int.TryParse(id.Substring(0, digits), out int prefix)) return prefix + 1;
        int underscore = id.LastIndexOf('_');
        if (underscore >= 0 && int.TryParse(id.Substring(underscore + 1), out int suffix)) return suffix + 1;
        return asset.upgrade_level + 1;
    }

    private static Sprite SpriteOf(string id)
    {
        if (Sprites.TryGetValue(id, out Sprite sprite)) return sprite;
        sprite = SpriteTextureLoader.getSprite("ui/icons/citydev/" + id);
        Sprites[id] = sprite;
        return sprite;
    }
}

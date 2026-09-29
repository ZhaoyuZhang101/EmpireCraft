using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GeneralSystems;
using UnityEngine;

namespace EmpireCraft.Scripts.Layer;

// 地图图层专用的城市取值缓存(理念图层的主理念、文化图层的主流文化)。
//
// 原版每约 0.01 秒就调用一次当前图层的 draw_zones，每个地块还要看上下左右四个邻居；原来每次都现算
// 城市的主理念/主流文化(统计全城人口、重建文化占比表)，城市一多就卡，而理念那边为了省算力又缓存了
// 一整个游戏月，颜色要很久才跟上。
//
// 这里改成：绘制时只查字典；每帧轮流重算一小批城市(RefreshPerFrame 座)，大约一秒把全世界刷一遍——
// 不会有某一帧集中重算的尖峰，变化也能在一秒左右反映到地图上。只影响图层显示，不影响玩法逻辑。
public static class LayerCityCache
{
    private const int RefreshPerFrame = 25;

    private sealed class Cache<T>
    {
        private readonly Func<City, T> _compute;
        private readonly Dictionary<City, T> _values = new();
        private object _world;
        private int _cursor;
        private int _lastFrame = -1;

        public Cache(Func<City, T> compute) => _compute = compute;

        public T Get(City city)
        {
            Tick();
            if (city == null) return default;
            if (_values.TryGetValue(city, out T value)) return value;
            value = _compute(city);
            _values[city] = value;
            return value;
        }

        public void Invalidate(City city)
        {
            if (city != null) _values.Remove(city);
        }

        private void Tick()
        {
            if (!ReferenceEquals(_world, World.world))
            {
                _values.Clear();
                _cursor = 0;
                _world = World.world;
            }
            int frame = Time.frameCount;
            if (frame == _lastFrame) return;
            _lastFrame = frame;
            List<City> cities = World.world?.cities?.list;
            if (cities == null || cities.Count == 0) return;
            for (int i = 0; i < RefreshPerFrame && i < cities.Count; i++)
            {
                if (_cursor >= cities.Count)
                {
                    _cursor = 0;
                    PruneDeadCities();
                }
                City city = cities[_cursor++];
                if (city == null || city.isRekt()) continue;
                _values[city] = _compute(city);
            }
        }

        private void PruneDeadCities()
        {
            foreach (City city in _values.Keys.Where(city => city == null || city.isRekt()).ToList())
                _values.Remove(city);
        }
    }

    private static readonly Cache<PartyIdeology> IdeologyCache = new(IdeologyPopulationSystem.ComputeDominant);
    private static readonly Cache<string> CultureCache = new(city => CultureService.GetMainCulture(city) ?? "");

    public static PartyIdeology Ideology(City city) => IdeologyCache.Get(city);

    public static string Culture(City city) => CultureCache.Get(city);

    // 明确知道某城变了(如玩家改了城市文化)时可以立即作废，不必等轮到它
    public static void Invalidate(City city)
    {
        IdeologyCache.Invalidate(city);
        CultureCache.Invalidate(city);
    }
}

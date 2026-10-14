using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 经济圈：富庶的城市成为一方经济中心(主城)，周边同一国家(或同一帝国)的城市成为它的卫星城。
//   · 谁是主城：按富庶度(经济繁荣 + 城市国库，乘地利，见 WealthMap.Score)从高到低排，首都另加三成；
//     全图排名前四成(殷实、富庶两档)、半径 Radius 格内还没有更富的主城的，成为主城。
//     古代多半是首都或临河临海的城，现代是工商业大城；
//   · 卫星城不会长出自己的主城格局，除非富庶度超过了它的主城(现任主城有一成的在位优势，免得来回换)；
//   · 虹吸：卫星城每月有一部分人口迁往主城(主城还有空房时，颁布逃人法的国家不流动)；
//     主城建设加快(每个卫星城 +6%，至多 +30%)，卫星城建设慢一成半。
// 城市格局(纯净地图)按这里的档位画：主城是都会(主城、门外关厢、门路上的镇、散村)，其余是县城与村落，
// 赤贫的只有几处村子(见 UrbanFabric)。
public static class EconomicCircleSystem
{
    public const float Radius = 60f;
    private const float RefreshSeconds = 10f, Incumbency = 1.1f, CapitalWeight = 1.3f;
    private const float SiphonPerMonth = 0.003f, BoostPerMember = 0.06f, MaxBoost = 0.3f, MemberSlowdown = 0.85f;

    private static readonly Dictionary<long, long> CoreOfCity = new();
    private static readonly Dictionary<long, int> MemberCount = new();
    private static readonly HashSet<long> Cores = new();
    private static float _at = -100f;
    private static object _world;

    public static void ResetWorldState()
    {
        CoreOfCity.Clear();
        MemberCount.Clear();
        Cores.Clear();
        _at = -100f;
        _world = null;
    }

    public static bool IsCore(City city)
    {
        Refresh();
        return city?.data != null && Cores.Contains(city.data.id);
    }

    // 所在经济圈的主城(主城自己返回自己，不在任何经济圈返回 null)
    public static City CoreOf(City city)
    {
        Refresh();
        if (city?.data == null || !CoreOfCity.TryGetValue(city.data.id, out long core)) return null;
        City found = World.world.cities.get(core);
        return found == null || found.isRekt() ? null : found;
    }

    public static int Members(City core)
    {
        Refresh();
        return core?.data != null && MemberCount.TryGetValue(core.data.id, out int count) ? count : 0;
    }

    // 城市格局的档位：0 赤贫(几处村子) 1 清贫 2 小康(县城，卫星城至多到这一档) 3 殷实主城 4 富庶主城
    public static int RegionTier(City city)
    {
        if (city?.data == null) return 0;
        int bucket = WealthMap.Bucket(WealthMap.Score(city));
        return IsCore(city) ? (bucket >= 4 ? 4 : 3) : Mathf.Min(bucket, 2);
    }

    // 建设快慢(见 CityConstructionSystem.Settle)
    public static float ConstructionFactor(City city)
    {
        if (city?.data == null) return 1f;
        if (IsCore(city)) return 1f + Mathf.Min(MaxBoost, Members(city) * BoostPerMember);
        return CoreOf(city) != null ? MemberSlowdown : 1f;
    }

    // 每月：卫星城的一部分人口迁往同一国家的主城(见 CityPopulationSystem.Migrate)
    public static void Siphon(City city, float corePull)
    {
        City core = CoreOf(city);
        if (core == null || core == city || core.kingdom != city.kingdom || corePull <= 0f) return;
        CityPopulationSystem.TransferBackground(city, core, SiphonPerMonth);
    }

    private static bool SameRealm(City a, City b)
    {
        if (a.kingdom == null || b.kingdom == null) return false;
        if (a.kingdom == b.kingdom) return true;
        Empire empire = a.kingdom.GetEmpire();
        return empire != null && empire == b.kingdom.GetEmpire();
    }

    private static float Weight(City city) =>
        WealthMap.Score(city) * (city.isCapitalCity() ? CapitalWeight : 1f) * (Cores.Contains(city.data.id) ? Incumbency : 1f);

    private static void Refresh()
    {
        float now = Time.unscaledTime;
        if (_world != World.world)
        {
            ResetWorldState();
            _world = World.world;
        }
        if (now - _at < RefreshSeconds && now >= _at) return;
        _at = now;
        if (World.world?.cities == null) return;
        var cities = World.world.cities.list
            .Where(city => city?.data != null && !city.isRekt() && city.kingdom != null && !city.kingdom.wild &&
                           city.getTile() != null)
            .Select(city => (city, weight: Weight(city)))
            .OrderByDescending(pair => pair.weight)
            .ToList();
        var cores = new List<City>();
        CoreOfCity.Clear();
        MemberCount.Clear();
        foreach ((City city, _) in cities)
        {
            WorldTile tile = city.getTile();
            City nearest = null;
            float best = Radius * Radius;
            foreach (City core in cores)
            {
                if (!SameRealm(city, core)) continue;
                WorldTile there = core.getTile();
                float dx = there.x - tile.x, dy = there.y - tile.y, d = dx * dx + dy * dy;
                if (d >= best) continue;
                best = d;
                nearest = core;
            }
            if (nearest != null)
            {
                CoreOfCity[city.data.id] = nearest.data.id;
                MemberCount[nearest.data.id] = (MemberCount.TryGetValue(nearest.data.id, out int count) ? count : 0) + 1;
                continue;
            }
            if (WealthMap.Bucket(WealthMap.Score(city)) < 3) continue;
            cores.Add(city);
            CoreOfCity[city.data.id] = city.data.id;
        }
        Cores.Clear();
        foreach (City core in cores) Cores.Add(core.data.id);
    }
}

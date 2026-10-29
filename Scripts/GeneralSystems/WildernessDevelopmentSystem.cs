using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 无人区开发(用户 2026-10-29 "雨林需要科技, 之后才可以慢慢拓展为可住人区域, 可以加个雨林地块开发度, 还有其他地形也是,
// 参考中国历史上是怎么改造南方的"；又 "沙漠和山脉还是固定吧"、"西伯利亚冻土完全不能住人也归为无人区"——山脉、沙漠、冻原不开发，
// 永远是无人区；问答拍板"雨林沼泽真变""自动加朝廷决议""逐步生效")：
//   · 开发度：雨林、大泽无人区的地块各有 0~100(按地块存，随存档，见 SaveData.wild_development)；永久中立区不自己开发；
//   · 谁开发(每年一次，分帧，见 Tick)：挨着某国城的地块是前沿，开发度过半的地块也带动它的四邻(一圈圈往里推)；
//     开发的是挨着的那座城(或带动它的那块地)的国家；这块无人区归了别国(WildernessSystem.OwnerOf 不是它、也不是中立)就不能开发；
//   · 科技门槛(本国文化，TechnologySystem.HasTech；科技树没在用的按够格算、不加快)，照中国开发南方：
//       雨林：冶铁(铁斧铁锸才伐得动岭南的林、开得出田——秦戍五岭、汉开交趾九真)；科学方法 ×2(奎宁、医药，瘴疠不再要命：
//             岭南山地、云贵到明清改土归流才大开，热带雨林要到近代才住得进去)；工业化 ×3(机械伐木、种植园)；
//       大泽：冶铁加营造(砖石营造或城市营造：筑堤围田——东晋南朝到唐宋，太湖、鄱阳、洞庭一带的圩田、湖田，"苏湖熟，天下足")；
//             工业化 ×2.5(机器排水)；
//     科技不够的不开发；
//   · 速度：雨林一块约六十年、大泽约九十年开完(JungleYears、SwampYears，按游戏年；大泽要先筑堤排水，慢一档)，再乘：
//       人口压力：挨着的城人口到上限九成 ×1.5、超员 ×2(人多地少往外拓)；往里推的地块按这国挨着这一片的城里最大的算(上一年记的)；
//       战乱南迁：本国别处在打仗、或王朝积弊入了危机(DynasticCycleSystem.InCrisis)，前沿的城不在前线(EnfeoffmentHelper.IsFrontCity)
//         ×1.5(永嘉之乱衣冠南渡、安史之乱后北人南迁、靖康南渡，江南一次次因北方战乱而开)；
//       有路：地块或四邻有修通的路(RoadNetwork.IsRoad)×1.5；朝廷决议(见下)×3；
//   · 朝廷决议"开发边地"(原版谋划，见 EmpireCraftPlotsAddition，同改建城池、通路之约)：国君对一片本国前沿的雨林大泽下诏，
//     每年从国库出 Cost(按城数，下诏当年先付)、记一笔工役(CorveeSystem)，这一片本国开发的地块 ×3，至多 EdictYears 年，
//     开完、付不起、国亡即止；AI 在国库够 ReserveYears 年开支、前沿的城人多地少、科技够时下诏(秦戍五岭、汉魏屯田)；
//     下诏、开完都记纪事；
//   · 逐步生效：
//       过半：军队能走(WildernessPassage 认它，同有路)；
//       格子：开发度涨了，这块地的丛林、沼泽格按比例一批批改成同一高度的草地(林木伐去；高地仍是高地)，先改挨着已开发一侧的
//         (从城那边往里推)；每帧改的格数有限(TilesPerFrame)，神力刷得再快也一批批改；
//       开发中的地块不掉出无人区：格子改掉一半，按地形早够不上门槛，照开发时的类型留着(见 WildernessSystem.Reclassify、HeldKind)；
//       开满：退出无人区，当作留给城的地(同城心、村镇，见 WildernessSystem.Regroup 的 anchored)，城照常扩张领进去；
//       宜居度：TerrainSystem.Livability 按格子算(丛林 0.6、沼泽 0.4、草地 1)，格子改成草地宜居度就随开发度涨，不另加，
//         纯荒地为零的规矩(BarrenLandSystem)不动；
//   · 神力「开发」(见 SettlementPowers)：刷到的雨林、大泽无人区地块开发度涨一截，不看科技与归属
//     (用户 2026-10-20 "手动放置建筑时候不要有任何限制")；
//   · 剧本(世界地图各时代)可直接设初始开发度(SetDevelopment，当场把格子改到相应比例)：没归城的地块头一回划区之前设也行，
//     划区时开发中的照记下的类型留在无人区；归了城的地块不照记下的类型留(见 WildernessSystem.Reclassify)，剧本开局铺满时
//     地块多已归城，所以世界地图是先把地形改回丛林沼泽、当场划一次区(从城里划出来)再设(见 WorldMap.WorldMapDevelopment)。
// 显示：悬停提示一行开发度与状态(见 WildernessOverlay.ShowTooltip)，图层上开发中的地块底色淡一点(见 Fade)。
public class WildDevelopmentData
{
    // 地块编号 → 这块地的开发
    public Dictionary<int, WildDevelopmentZone> zones = new Dictionary<int, WildDevelopmentZone>();
    // 朝廷决议"开发边地"
    public List<WildDevelopmentEdict> edicts = new List<WildDevelopmentEdict>();
}

public class WildDevelopmentZone
{
    public float dev;
    // 谁在开发(国家编号；神力刷的、剧本设的为 -1)
    public long by = -1L;
    // 开发时是哪一类(雨林、大泽)：格子改了一半也照这一类留在无人区
    public int kind;
    // 起头时丛林、沼泽格有几格(按它算开发到几成该剩几格)
    public int wet = -1;
}

public class WildDevelopmentEdict
{
    public long kingdom = -1L;
    public int region = -1;
    // 下诏时那一片的名字(纪事用：开完时那一片已不在)
    public string name;
    public double since;
    public int cost;
    // 诏令期间开满的地块数
    public int done;
}

public static class WildernessDevelopmentSystem
{
    public const string Plot = "develop_wilderness";
    public const float Full = 100f, Half = 50f;
    // 一块地只按基础速度开完要几年
    private const float JungleYears = 60f, SwampYears = 90f;
    // 科技加快：科学方法(雨林)、工业化(雨林、大泽)
    private const float ScienceBoost = 2f, IndustryJungleBoost = 3f, IndustrySwampBoost = 2.5f;
    // 人口到上限几成算人多地少；人多地少、超员、南迁、有路、诏令各乘几倍
    private const float PressureShare = 0.9f, PressureBoost = 1.5f, CrowdedBoost = 2f;
    private const float MigrationBoost = 1.5f, RoadBoost = 1.5f, EdictBoost = 3f;
    // 诏令：每年开支(基数 + 每城，夹在上下限里)、至多几年、AI 要国库够几年开支才下诏、每年记的工役
    private const int CostBase = 20, CostPerCity = 6, CostMin = 30, CostMax = 300;
    private const float EdictYears = 20f, ReserveYears = 5f, EdictCorvee = 0.5f;
    // 分帧：每帧推进几块地；改格子每帧至多改几格、看几块地，每块地每回至多改几格
    private const int ZonesPerFrame = 300, TilesPerFrame = 64, ReclaimZonesPerFrame = 48, TilesPerVisit = 8;
    // 神力：每次涨多少、同一块地隔几秒再涨(真实时间)
    private const float BrushStep = 10f, BrushInterval = 0.2f;
    // 图层上开满时底色的不透明度降到几成
    private const float FadeAtFull = 0.6f;

    private static WildDevelopmentData _data = new();
    // 每年的推进：上回推进的游戏时间；这一年要过的地块与走到哪了
    private static double _lastYear = -1d;
    private static List<int> _work;
    private static int _cursor;
    // 这一年里各国的科技、动荡，各城的人口压力与在不在前线(推进时缓存)
    private static readonly Dictionary<Kingdom, Realm> Realms = new();
    private static readonly Dictionary<City, (float pressure, bool front)> Cities = new();
    // 各国挨着各片的城里最大的人口压力(往里推的地块用)：今年记的、上一年记的
    private static Dictionary<(long, int), float> _pressureNow = new(), _pressureLast = new();
    // 等着改格子的地块
    private static readonly Queue<int> Pending = new();
    private static readonly HashSet<int> Queued = new();
    // 诏令的候选(按国家编号缓存一个月)、神力上回刷到各块的时间
    private static readonly Dictionary<long, (double at, int region, float pressure)> Candidates = new();
    private static readonly Dictionary<int, float> LastBrush = new();
    private static readonly List<WorldTile> Wet = new();
    private static readonly List<WorldTile> Anchors = new();
    private static TopTileType _grassLow, _grassHigh;

    // 有地块过半、开满(或改回)时 +1(通行据此重算，见 WildernessPassage)
    public static int Version { get; private set; }

    // 哪些无人区开发(雨林、大泽；山脉、沙漠、冻原固定)
    public static bool Develops(WildKind kind) => kind is WildKind.Jungle or WildKind.Swamp;

    private sealed class Realm
    {
        public Kingdom Kingdom, Core;
        // 科技给的倍率(0 为不够格)与缺的那项科技
        public float Jungle, Swamp;
        public string JungleMissing, SwampMissing;
        // 本国或宗主在打仗、王朝积弊入了危机
        public bool Unrest;
    }

    #region 查询

    private static WildDevelopmentZone Get(int id) => _data.zones.TryGetValue(id, out WildDevelopmentZone state) ? state : null;

    // 开发度(0~100；没开发过为 0)
    public static float DevelopmentOf(TileZone zone) => zone == null ? 0f : Get(zone.id)?.dev ?? 0f;

    // 过半：军队能走(见 WildernessPassage.Blocking)
    public static bool Passable(TileZone zone) => DevelopmentOf(zone) >= Half;

    // 开发中(没开满)的地块照开发时的类型留在无人区(见 WildernessSystem.Reclassify)；不是为 None
    public static WildKind HeldKind(int id)
    {
        WildDevelopmentZone state = Get(id);
        return state != null && state.dev > 0f && state.dev < Full && Develops((WildKind)state.kind) ? (WildKind)state.kind : WildKind.None;
    }

    // 开满了的地块：划区时留给城(见 WildernessSystem.Regroup)
    public static IEnumerable<int> DevelopedZones()
    {
        foreach (KeyValuePair<int, WildDevelopmentZone> pair in _data.zones)
            if (pair.Value != null && pair.Value.dev >= Full) yield return pair.Key;
    }

    // 图层底色的不透明度系数：没开发 1，开满 FadeAtFull
    public static float Fade(TileZone zone)
    {
        float dev = DevelopmentOf(zone);
        return dev <= 0f ? 1f : 1f - (1f - FadeAtFull) * Mathf.Min(dev, Full) / Full;
    }

    private static bool Civil(Kingdom kingdom) => kingdom?.data != null && !kingdom.isRekt() && !kingdom.wild && kingdom.isCiv();

    private static WildDevelopmentEdict EdictOf(Kingdom kingdom, int region)
    {
        foreach (WildDevelopmentEdict edict in _data.edicts)
            if (edict.kingdom == kingdom.id && edict.region == region) return edict;
        return null;
    }

    private static bool HasEdict(Kingdom kingdom) => _data.edicts.Any(edict => edict.kingdom == kingdom.id);

    #endregion

    #region 开关、存读档

    public static void ResetWorldState()
    {
        _data = new WildDevelopmentData();
        ClearRuntime();
    }

    private static void ClearRuntime()
    {
        _lastYear = -1d;
        _work = null;
        _cursor = 0;
        Realms.Clear();
        Cities.Clear();
        _pressureNow.Clear();
        _pressureLast.Clear();
        Pending.Clear();
        Queued.Clear();
        Candidates.Clear();
        LastBrush.Clear();
        Version++;
    }

    public static void Load(WildDevelopmentData saved)
    {
        _data = saved ?? new WildDevelopmentData();
        _data.zones ??= new Dictionary<int, WildDevelopmentZone>();
        _data.edicts ??= new List<WildDevelopmentEdict>();
        foreach (int id in _data.zones.Where(pair => pair.Value == null || !Develops((WildKind)pair.Value.kind)).Select(pair => pair.Key)
                     .ToList())
            _data.zones.Remove(id);
        _data.edicts.RemoveAll(edict => edict == null);
        ClearRuntime();
    }

    public static WildDevelopmentData Save() => _data;

    #endregion

    #region 每帧

    // 每帧(WildernessSystem.Tick 里，划好区以后)：改一批格子；过了一年就分帧推进一年的开发
    public static void Tick()
    {
        MapBox world = World.world;
        if (world?.zone_calculator == null) return;
        try
        {
            ReclaimStep(world.zone_calculator);
            if (_work != null)
            {
                Advance(world.zone_calculator);
                return;
            }
            double now = world.getCurWorldTime();
            if (_lastYear < 0d || now < _lastYear)
            {
                _lastYear = now;
                return;
            }
            if (Date.getYearsSince(_lastYear) < 1) return;
            _lastYear = now;
            BeginYear();
        }
        catch (Exception exception)
        {
            _work = null;
            LogService.LogWarning($"[EmpireCraft][无人区开发] 结算失败: {exception}");
        }
    }

    // 一年开头：结算诏令(付钱、到期、开完)，排出这一年要看的地块(各片雨林、大泽，不含永久中立区)
    private static void BeginYear()
    {
        Realms.Clear();
        Cities.Clear();
        (_pressureLast, _pressureNow) = (_pressureNow, _pressureLast);
        _pressureNow.Clear();
        SettleEdicts();
        var work = new List<int>();
        foreach (WildRegion region in WildernessSystem.Regions)
            if (region != null && !region.Neutral && Develops(region.Kind)) work.AddRange(region.Zones);
        _work = work.Count > 0 ? work : null;
        _cursor = 0;
    }

    private static void Advance(ZoneCalculator calc)
    {
        int end = Math.Min(_work.Count, _cursor + ZonesPerFrame);
        for (; _cursor < end; _cursor++) Grow(calc.getZoneByID(_work[_cursor]));
        if (_cursor >= _work.Count) _work = null;
    }

    // 一块地这一年的开发：前沿上有够格的国家就按它的速度涨(几国都够格的取最快的)
    private static void Grow(TileZone zone)
    {
        if (zone == null || zone.city != null) return;
        WildRegion region = WildernessSystem.RegionOf(zone);
        if (region == null || region.Neutral || !Develops(region.Kind)) return;
        WildDevelopmentZone state = Get(zone.id);
        if (state != null && state.dev >= Full) return;
        Pick pick = Evaluate(zone, region, true);
        if (pick.Kingdom == null) return;
        state ??= Start(zone, region.Kind);
        state.by = pick.Kingdom.id;
        Raise(zone, state, pick.Rate);
    }

    #endregion

    #region 谁开发、多快

    private struct Pick
    {
        // 开发这块地的国家与每年涨多少、是不是奉诏
        public Kingdom Kingdom;
        public float Rate;
        public bool Edict;
        // 没人开发时的缘由(提示用)：哪国缺哪项科技、归了哪国
        public Kingdom Lacking, Owner;
        public string Missing;
    }

    // 挨着的城(本国的城)、挨着的开发过半的地块(开发它的国家)都是候选；归了别国的、科技不够的不算。
    // pass：一年推进里(用这一年的缓存，并记下各国挨着这一片的城里最大的人口压力)；提示里现算
    private static Pick Evaluate(TileZone zone, WildRegion region, bool pass)
    {
        var pick = new Pick();
        if (zone?.neighbours == null) return pick;
        Kingdom owner = WildernessSystem.OwnerOf(zone);
        bool road = RoadNetwork.IsRoad(zone);
        foreach (TileZone next in zone.neighbours)
            if (!road && next != null && RoadNetwork.IsRoad(next)) road = true;
        foreach (TileZone next in zone.neighbours)
        {
            if (next == null) continue;
            City city = next.city;
            Kingdom kingdom;
            if (city?.data != null && !city.isRekt()) kingdom = city.kingdom;
            else
            {
                city = null;
                WildDevelopmentZone near = Get(next.id);
                if (near == null || near.dev < Half || near.by < 0) continue;
                kingdom = World.world.kingdoms.get(near.by);
            }
            if (!Civil(kingdom)) continue;
            if (owner != null && owner != kingdom)
            {
                pick.Owner ??= owner;
                continue;
            }
            Realm realm = RealmOf(kingdom, pass);
            bool jungle = region.Kind == WildKind.Jungle;
            float tech = jungle ? realm.Jungle : realm.Swamp;
            if (tech <= 0f)
            {
                if (pick.Lacking == null)
                {
                    pick.Lacking = kingdom;
                    pick.Missing = jungle ? realm.JungleMissing : realm.SwampMissing;
                }
                continue;
            }
            float rate = Full / (jungle ? JungleYears : SwampYears) * tech;
            rate *= city != null ? CityFactor(city, realm, region, pass) : InnerFactor(realm, region);
            if (road) rate *= RoadBoost;
            bool edict = EdictOf(kingdom, region.Id) != null;
            if (edict) rate *= EdictBoost;
            if (rate <= pick.Rate) continue;
            pick.Kingdom = kingdom;
            pick.Rate = rate;
            pick.Edict = edict;
        }
        return pick;
    }

    // 这国的科技门槛与倍率、动荡(见类头)
    private static Realm RealmOf(Kingdom kingdom, bool cached)
    {
        if (cached && Realms.TryGetValue(kingdom, out Realm hit)) return hit;
        var realm = new Realm { Kingdom = kingdom };
        string culture = TechnologySystem.GetCultureOf(kingdom);
        // 科技树没在用(关掉了、读档后头一回结算前)：门槛按够格算，加快的不算
        bool active = !string.IsNullOrEmpty(culture) && TechnologySystem.IsActiveFor(culture);
        bool Has(string tech) => !active || TechnologySystem.HasTech(culture, tech);
        bool Boost(string tech) => active && TechnologySystem.HasTech(culture, tech);
        if (!Has("ironworking")) realm.JungleMissing = realm.SwampMissing = "ironworking";
        else
        {
            realm.Jungle = Boost("industrialization") ? IndustryJungleBoost : Boost("scientific_method") ? ScienceBoost : 1f;
            if (!Has("masonry") && !Has("architecture")) realm.SwampMissing = "masonry";
            else realm.Swamp = Boost("industrialization") ? IndustrySwampBoost : 1f;
        }
        Empire empire = kingdom.GetEmpire();
        Kingdom core = empire?.CoreKingdom;
        realm.Core = core != null && !core.isRekt() ? core : kingdom;
        realm.Unrest = kingdom.hasEnemies() || realm.Core != kingdom && realm.Core.hasEnemies() ||
                       empire != null && !empire.isRekt() && DynasticCycleSystem.InCrisis(empire);
        if (cached) Realms[kingdom] = realm;
        return realm;
    }

    // 挨着城的前沿：人口压力 × 南迁(动荡之年、这座城不在前线)；一年推进里记下这国挨着这一片的城里最大的人口压力
    private static float CityFactor(City city, Realm realm, WildRegion region, bool pass)
    {
        if (!pass || !Cities.TryGetValue(city, out (float pressure, bool front) hit))
        {
            hit = (Pressure(city), realm.Unrest && EnfeoffmentHelper.IsFrontCity(city, realm.Core));
            if (pass) Cities[city] = hit;
        }
        if (pass)
        {
            (long, int) key = (realm.Kingdom.id, region.Id);
            if (!_pressureNow.TryGetValue(key, out float most) || most < hit.pressure) _pressureNow[key] = hit.pressure;
        }
        return hit.pressure * (realm.Unrest && !hit.front ? MigrationBoost : 1f);
    }

    // 往里推的地块(挨着开发过半的地块，不挨城)：人口压力按这国挨着这一片的城里最大的(今年已记的、上一年记的)；动荡之年都算南迁
    private static float InnerFactor(Realm realm, WildRegion region)
    {
        (long, int) key = (realm.Kingdom.id, region.Id);
        float pressure = 1f;
        if (_pressureLast.TryGetValue(key, out float last)) pressure = Mathf.Max(pressure, last);
        if (_pressureNow.TryGetValue(key, out float now)) pressure = Mathf.Max(pressure, now);
        return pressure * (realm.Unrest ? MigrationBoost : 1f);
    }

    // 人多地少：人口到上限九成 ×1.5，超员 ×2
    private static float Pressure(City city)
    {
        int max = city.getPopulationMaximum();
        if (max <= 0) return 1f;
        float people = CityPopulationSystem.AbstractPopulationEnabled ? CityPopulationSystem.GetTotal(city) : city.getPopulationPeople();
        return people >= max ? CrowdedBoost : people >= max * PressureShare ? PressureBoost : 1f;
    }

    #endregion

    #region 涨开发度、改格子

    private static WildDevelopmentZone Start(TileZone zone, WildKind kind)
    {
        var state = new WildDevelopmentZone { kind = (int)kind, wet = CountWet(zone) };
        _data.zones[zone.id] = state;
        return state;
    }

    // 涨开发度：排进改格子的队；过半、开满时通行与划区跟着变；诏令期间开满的记一块
    private static void Raise(TileZone zone, WildDevelopmentZone state, float amount)
    {
        float before = state.dev;
        state.dev = Mathf.Min(Full, before + amount);
        if (before < Half != state.dev < Half) Version++;
        if ((int)(before / 10f) != (int)(state.dev / 10f)) WildernessOverlay.MarkAround(zone);
        Queue(zone.id);
        if (state.dev < Full || before >= Full) return;
        Version++;
        WildernessSystem.OnDeveloped(zone);
        int region = WildernessSystem.RegionOf(zone)?.Id ?? -1;
        foreach (WildDevelopmentEdict edict in _data.edicts)
            if (edict.kingdom == state.by && edict.region == region) edict.done++;
    }

    private static void Queue(int id)
    {
        if (Queued.Add(id)) Pending.Enqueue(id);
    }

    // 每帧改一批格子：至多 TilesPerFrame 格、看 ReclaimZonesPerFrame 块地，每块地每回至多 TilesPerVisit 格(一批批改，看得出从城那边往里推)
    private static void ReclaimStep(ZoneCalculator calc)
    {
        int budget = TilesPerFrame;
        for (int visits = 0; visits < ReclaimZonesPerFrame && budget > 0 && Pending.Count > 0; visits++)
        {
            int id = Pending.Dequeue();
            Queued.Remove(id);
            WildDevelopmentZone state = Get(id);
            TileZone zone = state == null ? null : calc.getZoneByID(id);
            if (zone == null) continue;
            int limit = Math.Min(TilesPerVisit, budget);
            int done = Reclaim(zone, state, limit);
            budget -= done;
            // 这回改满了额度的，可能还没改完，下一轮再改
            if (done > 0 && done >= limit) Queue(id);
        }
    }

    // 丛林、沼泽格改成草地，改到剩下的不多于 起头时的格数 × (1 − 开发度)；先改挨着已开发一侧的。返回改了几格
    private static int Reclaim(TileZone zone, WildDevelopmentZone state, int limit)
    {
        if (zone?.tiles == null || state.wet <= 0 || limit <= 0) return 0;
        int keep = Mathf.RoundToInt(state.wet * (1f - Mathf.Clamp(state.dev, 0f, Full) / Full));
        Wet.Clear();
        foreach (WorldTile tile in zone.tiles)
            if (IsWet(tile)) Wet.Add(tile);
        int count = Math.Min(Wet.Count - keep, limit);
        if (count <= 0) return 0;
        // 已开发的一侧：挨着的城、开发过半的、不是无人区的陆地；都没有(神力刷在当中)从这块地当中往外改
        Anchors.Clear();
        foreach (TileZone next in zone.neighbours_all ?? Array.Empty<TileZone>())
        {
            if (next?.centerTile == null) continue;
            if (next.city != null || DevelopmentOf(next) >= Half || next.tiles_with_ground > 0 && !WildernessSystem.IsWild(next))
                Anchors.Add(next.centerTile);
        }
        if (Anchors.Count == 0 && zone.centerTile != null) Anchors.Add(zone.centerTile);
        if (Anchors.Count > 0) Wet.Sort((a, b) => Near(a).CompareTo(Near(b)));
        int changed = 0;
        for (int i = 0; i < count; i++)
            if (Clear(Wet[i])) changed++;
        Wet.Clear();
        return changed;
    }

    private static int Near(WorldTile tile)
    {
        int best = int.MaxValue;
        foreach (WorldTile anchor in Anchors)
        {
            int dx = tile.x - anchor.x, dy = tile.y - anchor.y;
            best = Math.Min(best, dx * dx + dy * dy);
        }
        return best;
    }

    // 丛林、沼泽格(顶层格是这两个生物群系的，不算水面)
    private static bool IsWet(WorldTile tile)
    {
        TileTypeBase type = tile?.Type;
        if (type == null || type.liquid || type.lava || tile.top_type == null) return false;
        return type.biome_id is "biome_jungle" or "biome_swamp";
    }

    private static int CountWet(TileZone zone)
    {
        int count = 0;
        if (zone?.tiles != null)
            foreach (WorldTile tile in zone.tiles)
                if (IsWet(tile)) count++;
        return count;
    }

    // 这块地丛林、沼泽哪样多(剧本还没划区时用)；都没有为 None
    private static WildKind WetKind(TileZone zone)
    {
        int jungle = 0, swamp = 0;
        foreach (WorldTile tile in zone.tiles)
        {
            if (!IsWet(tile)) continue;
            if (tile.Type.biome_id == "biome_jungle") jungle++;
            else swamp++;
        }
        return jungle + swamp == 0 ? WildKind.None : jungle >= swamp ? WildKind.Jungle : WildKind.Swamp;
    }

    // 改成同一高度的草地(地面格不动：高地仍是高地、丘陵仍是丘陵)，林木伐去；不闪白点(同 FarmlandSystem 开田)
    private static bool Clear(WorldTile tile)
    {
        bool high = (tile.top_type?.id ?? "").EndsWith("_high", StringComparison.Ordinal);
        _grassLow ??= AssetManager.top_tiles.get("grass_low");
        _grassHigh ??= AssetManager.top_tiles.get("grass_high");
        TopTileType grass = high && _grassHigh != null ? _grassHigh : _grassLow;
        if (grass == null) return false;
        Building building = tile.building;
        if (building?.asset != null && building.asset.building_type == BuildingType.Building_Tree) building.startDestroyBuilding();
        MapAction.terraformTop(tile, grass, TerraformLibrary.nothing);
        return true;
    }

    #endregion

    #region 剧本、神力

    // 直接设一块地的开发度(0~100；剧本按时代给初始开发度用)：雨林、大泽当场把格子改到相应比例(不分帧)；
    // 还没划区(剧本开局)的按格子里丛林、沼泽哪样多记类型，划区时开发中的照这一类留在无人区；
    // 设低了只改数，改掉的格子不复原。不是雨林、大泽(也没有丛林、沼泽格)的不记
    public static void SetDevelopment(TileZone zone, float value)
    {
        if (zone?.tiles == null || zone.id < 0) return;
        value = Mathf.Clamp(value, 0f, Full);
        WildDevelopmentZone state = Get(zone.id);
        if (state == null)
        {
            if (value <= 0f) return;
            WildKind kind = WildernessSystem.KindOf(zone);
            if (!Develops(kind)) kind = WetKind(zone);
            if (!Develops(kind)) return;
            state = Start(zone, kind);
        }
        float before = state.dev;
        state.dev = value;
        if (before < Half != value < Half || before >= Full != value >= Full) Version++;
        Reclaim(zone, state, int.MaxValue);
        if (before >= Full != value >= Full) WildernessSystem.OnDeveloped(zone);
        WildernessOverlay.MarkAround(zone);
    }

    // 神力「开发」(见 SettlementPowers)：刷到的雨林、大泽无人区地块开发度涨 BrushStep(同一块隔 BrushInterval 秒)，
    // 不看科技与归属(永久中立区也刷得动)；刷了返回 true
    public static bool Brush(TileZone zone)
    {
        if (zone == null || zone.city != null) return false;
        WildRegion region = WildernessSystem.RegionOf(zone);
        if (region == null || !Develops(region.Kind)) return false;
        WildDevelopmentZone state = Get(zone.id);
        if (state != null && state.dev >= Full) return false;
        float now = Time.unscaledTime;
        if (LastBrush.TryGetValue(zone.id, out float last) && now - last < BrushInterval && now >= last) return true;
        if (LastBrush.Count > 512) LastBrush.Clear();
        LastBrush[zone.id] = now;
        state ??= Start(zone, region.Kind);
        Raise(zone, state, BrushStep);
        return true;
    }

    #endregion

    #region 朝廷决议"开发边地"

    // 每年开支：按城数(国力)
    private static int Cost(Kingdom kingdom) => Mathf.Clamp(CostBase + CostPerCity * (kingdom.cities?.Count ?? 0), CostMin, CostMax);

    // 能下诏(玩家强行发起、谋划进行中)：国君、本国眼下没有诏令、有够科技的雨林大泽前沿、国库够一年开支
    public static bool CanDecree(Actor actor) => Candidate(actor, out _, out _, out _);

    // AI 自己下诏：另要国库够 ReserveYears 年开支、前沿的城人多地少(到上限九成)
    public static bool WantsDecree(Actor actor) =>
        Candidate(actor, out Kingdom kingdom, out _, out float pressure) && pressure >= PressureBoost &&
        kingdom.GetMoney() >= Cost(kingdom) * ReserveYears;

    // 谋划完成：先付头一年，记工役、纪事
    public static bool Decree(Actor actor)
    {
        if (!Candidate(actor, out Kingdom kingdom, out WildRegion region, out _)) return false;
        int cost = Cost(kingdom);
        kingdom.SubMoney(cost, TreasuryCategory.Construction);
        // 伐林筑堤要征发民力(秦戍五岭，发卒五十万)：每年记一笔工役(见 CorveeSystem)
        CorveeSystem.Add(kingdom, EdictCorvee, false);
        string name = WildernessSystem.DisplayName(region);
        _data.edicts.Add(new WildDevelopmentEdict
        {
            kingdom = kingdom.id, region = region.Id, name = name, since = World.world.getCurWorldTime(), cost = cost
        });
        Candidates.Remove(kingdom.id);
        EventRecorder.Record(kingdom, string.Format(LM.Get("wild_develop_decree_history"), kingdom.GetKingdomName(), name), actor);
        return true;
    }

    // 本国前沿上够得着的一片(够格的前沿地块最多的那片)与挨着它的城里最大的人口压力；按国家缓存一个月
    private static bool Candidate(Actor actor, out Kingdom kingdom, out WildRegion region, out float pressure)
    {
        region = null;
        pressure = 1f;
        kingdom = actor?.kingdom;
        if (!WildernessSystem.Enabled || !Civil(kingdom) || kingdom.king != actor || HasEdict(kingdom) ||
            kingdom.GetMoney() < Cost(kingdom)) return false;
        double now = World.world.getCurWorldTime();
        if (!Candidates.TryGetValue(kingdom.id, out var hit) || now < hit.at || Date.getMonthsSince(hit.at) >= 1)
        {
            hit = (now, -1, 1f);
            FindFrontier(kingdom, ref hit.region, ref hit.pressure);
            if (Candidates.Count > 1024) Candidates.Clear();
            Candidates[kingdom.id] = hit;
        }
        region = hit.region >= 0 ? WildernessSystem.ById(hit.region) : null;
        pressure = hit.pressure;
        return region != null && !region.Neutral;
    }

    private static void FindFrontier(Kingdom kingdom, ref int best, ref float pressure)
    {
        Realm realm = RealmOf(kingdom, false);
        if (realm.Jungle <= 0f && realm.Swamp <= 0f) return;
        var counts = new Dictionary<WildRegion, int>();
        var pressures = new Dictionary<WildRegion, float>();
        foreach (City city in kingdom.cities ?? new List<City>())
        {
            if (city?.zones == null || city.isRekt()) continue;
            float cityPressure = -1f;
            foreach (TileZone own in city.zones)
            {
                if (own?.neighbours == null) continue;
                foreach (TileZone next in own.neighbours)
                {
                    if (next == null || next.city != null) continue;
                    WildRegion region = WildernessSystem.RegionOf(next);
                    if (region == null || region.Neutral || !Develops(region.Kind)) continue;
                    if ((region.Kind == WildKind.Jungle ? realm.Jungle : realm.Swamp) <= 0f || DevelopmentOf(next) >= Full) continue;
                    Kingdom owner = WildernessSystem.OwnerOf(next);
                    if (owner != null && owner != kingdom) continue;
                    counts.TryGetValue(region, out int count);
                    counts[region] = count + 1;
                    if (cityPressure < 0f) cityPressure = Pressure(city);
                    if (!pressures.TryGetValue(region, out float most) || most < cityPressure) pressures[region] = cityPressure;
                }
            }
        }
        if (counts.Count == 0) return;
        WildRegion pick = counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key.Id).First().Key;
        best = pick.Id;
        pressure = pressures.TryGetValue(pick, out float value) ? value : 1f;
    }

    // 一年开头结算诏令：国亡、那一片开完(开完的记纪事)、到期即止；付不起当年的钱罢了；否则付钱、记工役
    private static void SettleEdicts()
    {
        for (int i = _data.edicts.Count - 1; i >= 0; i--)
        {
            WildDevelopmentEdict edict = _data.edicts[i];
            Kingdom kingdom = World.world.kingdoms.get(edict.kingdom);
            if (!Civil(kingdom))
            {
                _data.edicts.RemoveAt(i);
                continue;
            }
            WildRegion region = WildernessSystem.ById(edict.region);
            bool opened = region == null || region.Zones.All(id => (Get(id)?.dev ?? 0f) >= Full);
            if (opened && edict.done > 0)
                EventRecorder.Record(kingdom, string.Format(LM.Get("wild_develop_done_history"), kingdom.GetKingdomName(), edict.name));
            if (opened || region.Neutral || Date.getYearsSince(edict.since) >= EdictYears)
            {
                _data.edicts.RemoveAt(i);
                continue;
            }
            if (kingdom.GetMoney() < edict.cost)
            {
                LogService.LogInfo($"[EmpireCraft][无人区开发] {kingdom.GetKingdomName()} 国库付不起，罢开发{edict.name}");
                _data.edicts.RemoveAt(i);
                continue;
            }
            kingdom.SubMoney(edict.cost, TreasuryCategory.Construction);
            CorveeSystem.Add(kingdom, EdictCorvee, false);
        }
    }

    #endregion

    #region 提示

    // 悬停提示(见 WildernessOverlay.ShowTooltip)：雨林、大泽才有；value 为"35%"，status 为谁在开发、缺什么科技、诏令、能不能走
    public static bool Describe(TileZone zone, out string value, out string status)
    {
        value = status = "";
        WildRegion region = zone == null ? null : WildernessSystem.RegionOf(zone);
        if (region == null || !Develops(region.Kind)) return false;
        float dev = DevelopmentOf(zone);
        value = $"{Mathf.FloorToInt(dev)}%";
        switch (StatusOf(zone, region, out Pick pick))
        {
            case 1:
                status = string.Format(LM.Get("wild_dev_by"), pick.Kingdom.name);
                break;
            case 2:
                status = string.Format(LM.Get("wild_dev_by_edict"), pick.Kingdom.name);
                break;
            case 3:
                status = string.Format(LM.Get("wild_dev_missing"), pick.Lacking.name, LM.Get("tech_" + pick.Missing));
                break;
            case 4:
                status = string.Format(LM.Get("wild_dev_foreign"), pick.Owner.name);
                break;
            case 5:
                status = LM.Get("wild_dev_neutral");
                break;
            default:
                status = LM.Get(dev >= Full ? "wild_dev_done" : "wild_dev_remote");
                break;
        }
        if (dev >= Half && dev < Full) status += LM.Get("wild_dev_passable");
        return true;
    }

    // 提示换没换(悬停时每帧比一次，见 WildernessOverlay.Hover)：开发度的整数与状态
    public static int TipKey(TileZone zone)
    {
        WildRegion region = zone == null ? null : WildernessSystem.RegionOf(zone);
        if (region == null || !Develops(region.Kind)) return -1;
        return Mathf.FloorToInt(DevelopmentOf(zone)) * 8 + StatusOf(zone, region, out _);
    }

    // 0 没人开发、1 有国开发、2 奉诏开发、3 缺科技、4 归了别国、5 永久中立
    private static int StatusOf(TileZone zone, WildRegion region, out Pick pick)
    {
        pick = default;
        if (region.Neutral) return 5;
        if (DevelopmentOf(zone) >= Full) return 0;
        pick = Evaluate(zone, region, false);
        if (pick.Kingdom != null) return pick.Edict ? 2 : 1;
        if (pick.Lacking != null) return 3;
        return pick.Owner != null ? 4 : 0;
    }

    #endregion
}

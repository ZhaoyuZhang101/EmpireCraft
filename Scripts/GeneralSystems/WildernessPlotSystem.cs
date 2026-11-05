using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 预划县址(用户 2026-11-03 转群友 "现在无人区开发之后的城市疆域很抽象，原有的城市跨河连片，本来该断掉的地方不断，导致新城市建造的时候
// 面积被挤压，复数城市堆积在很小的一块地方。建议生成无人区时无人区内部自带分割好的空壳城市，殖民推进到那里以后城市就解锁出来供人居住"；
// 答复不建空城(原版把没人的城当废城，还要算进国家城数、成战争目标)，只记数据划县址，用户 "这样他们做地图的也省心了旧地图也能兼容了"；
// 问答 "AI 自动设县，玩家也可以用神力建城手动设")：
//   · 病根：雨林、大泽的地块开满就退出无人区，以前当即归挨着的城(WildernessDevelopmentSystem.Hand)、城再照常扩张，前沿的老城顺着开出来的地
//     一块块吞过去、跨河连片到地块上限，后来的城只剩夹缝；
//   · 划：雨林、大泽无人区(只有这两类开发，见 WildernessDevelopmentSystem.Develops)按地形分成一个个县址，一县约一座寻常城池的地盘——
//     每城地块上限(模组设置，默认 50)的四分之一，夹在 TargetMin~TargetMax 块(不设上限时 TargetUnlimited)，至多两倍(不过上限)，
//     不到三分之一的并进邻县。分法(见 Partition)：走进一块地有代价——水面占比(大河穿过)、交界两边贴着的格子对里不是干地平地的
//     (沿交界走的河、山脊)、山地、不可通行；先撒种子(每次取离已有种子按代价最远的)、同时往外长，县界落在代价陡升处(河、岭)，
//     再把种子挪到各县当中重长 LloydRounds 轮，各县更匀称；挨着城、隔着河岭的小块(城这一侧的一条边)不并进对岸的县址，
//     留给那座城(否则对岸的县城又跨河连片，反过来重演)；
//     县治(城址，见 Center)取县里到各块代价和最小的那块，偏向临水(大河、湖、海边)、宜居、不是山地、有落得下城的格(UrbanFabric.ManualGround)；
//     县名按县治最近的城的主流文化起(同城改文化时新起的城名，CultureService.GenerateCulturalName)，附近还没有文明的等有了再起；
//   · 稳定：划一次就记下(随存档，见 SaveData.wild_development 的 plots)，之后每次划区(WildernessSystem.Builds 变了)只维护——
//     还在雨林大泽无人区里的、开发过的(开发中、开满空着)、归了本县县城的留下；归了别的城的、不在无人区里又没开发过的(地形改了)去掉；
//     县治没了重选；新进无人区的地块成块的另划县址，零星几块并进挨着的县址(交界代价最低的)，再小又不挨县址的不划(照旧开满归城)。
//     旧档没有县址：读档头一回划区后按现有的各片现划(开发中的一并划进，开满了的不划、照旧归城)；
//     剧本开局(WorldMap.WorldMapDevelopment 改地形、当场划区、设开发度)做完、进世界后才走到这里(同无人区，等进世界安排做完)，
//     划的是设好开发度的那一版；剧本刷满 100 的(当场开满、退出无人区)照旧归城；
//   · 解锁：县治那块开发过半(军队能走，WildernessDevelopmentSystem.Half)，或全县平均开发到 UnlockAverage(四成；已归县城的、开满的按满算)；
//   · 设县(每年一回，跟着开发的年度推进，见 Year；每帧至多设一县)：开发这一县的国家(各块记的开发者按开发度加权，县治那块加倍)设县——
//     这一县归了别国(WildernessSystem.OwnerOf，同一帝国的不算别国)由那国设，同开发的规矩；开发者没了的由挨着这一县城最多的国家设；
//     都得是还在的文明国家；永久中立区不设；
//     从本国挨着这一县的城里人口压力(人口 ÷ 上限)最大的那座落一位官员、迁一成人口(CityPlacementSystem.Found，同神力建城)，
//     没有挨着的取本国离县治最近的城；城名照预定县名(见 OnFounded)，县治那块当即算开满(治所先开，格子照常一批批改，
//     见 WildernessDevelopmentSystem.Establish)，挂进这块地原来挂的法理(同开满另立新城)；记纪事"某国于某地置某县"；
//     本国在打仗、迁人的城在前线(EnfeoffmentHelper.IsFrontCity)或城下正在交战的不设，明年再看；
//     每国每年至多设 1 县、每 CitiesPerExtraFound 城多 1 县(剧本开局许多县址一下子都够了，也是一年年设下去)，开发得多的先设；
//     玩家用神力建城点在没设县的县址里也就是设县(城名照预定县名)；AI 移民、国家移民、殖民、穿越者的新城落在没设县的县址里，
//     就是这一县的县城(见 OnCityBuilt，城名不改)，县治挪到城心那块；
//   · 疆域：县址里的地只给这一县已设的县城(CityExpansionPatch.CanAddZone；原版扩张、村镇划地、虚拟居民代领、开满归城都经它)，
//     没设县的(没开、已解锁待设)谁也不领；已设县的县址里不另立新城(CityExpansionPatch.AfterCanStartCityHere)；
//     开满的地块(WildernessDevelopmentSystem.Hand 先问 Claim)县城挨着就当即领进，挨不着的每年一圈圈领过去(见 Absorb)；
//     县城到了每城地块上限领不下的放出县址，照旧归别的城；县城没了(毁城、灭亡)回到未设县，空出来的地仍留给这一县；
//     神力手动划地、营建照旧不受限(用户 2026-10-20 "手动放置建筑时候不要有任何限制")；
//   · 显示：无人区图层上同一片里各县址之间描一道更淡更疏的虚线(按地块、贴地形两种画法都有，见 WildernessOverlay)，
//     悬停提示一行"预定县：某某(未开/已解锁待设/已设县 某城)"。
// 只在划区之后算(几千块地几十毫秒，日志记用时)，查询按地块编号查数组；年度设县、起名都分帧、限量。
public class WildPlot
{
    public int id;
    // 划县址时那一片的类型(雨林、大泽)与编号(纪事、提示用；重划后那一片可能已并、已拆)
    public int kind;
    public int region = -1;
    public List<int> zones = new List<int>();
    // 县治(城址)那块地
    public int seat = -1;
    // 预定县名与起名所依的文化(附近还没有文明时为空，等有了再起)
    public string name;
    public string culture;
    // 已设县：县城(没设为 -1)
    public long city = -1L;
}

public static class WildernessPlotSystem
{
    // 一县的地块数(见 Sizes)：每城地块上限的四分之一，夹在 TargetMin~TargetMax；不设上限时 TargetUnlimited
    private const int TargetMin = 6, TargetMax = 14, TargetUnlimited = 12;
    // 解锁：全县平均开发到几成(县治那块过半另算)
    private const float UnlockAverage = 40f;
    // 每国每年至多设几县：1 县，每这么多城多 1 县
    private const int CitiesPerExtraFound = 20;
    // 起名：县治多远(地块)以内有城才按它的文化起(同无人区起名，世界地图不限远近)；隔几秒起一回、一回至多起几个、重名换种子至多几回、
    // 一回至多看几个没名的(附近没有文明的轮着看)
    private const int NameReach = 48, NamesPerPass = 8, NameAttempts = 6, NameScan = 64;
    private const float NameSeconds = 5f;
    // 走进一块地的代价：基础 1；水面占比 × WaterCost(大河穿过的地块)；交界两边贴着的格子对里不是干地平地的占比 × BorderCost
    // (沿交界走的河、山脊)；不可通行 + RidgeCost、山地 + MountainCost、丘陵 + HillCost
    private const float WaterCost = 6f, BorderCost = 4f, RidgeCost = 6f, MountainCost = 2f, HillCost = 0.8f;
    // 种子挪到各县当中重长几轮
    private const int LloydRounds = 2;
    // 并小、并零星地块时交界代价的上限(平地、林地一两点，过河、翻岭三点以上)：挨着城的小块隔着河、岭的不并过去，留给那座城
    private const float MergeCost = 3f;
    // 走不到的地块算多远(县址给去掉几块后断开时选县治用)
    private const float Unreached = 1000f;

    private static List<WildPlot> _plots = new();
    private static int _nextId = 1;
    private static readonly Dictionary<int, WildPlot> ById = new();
    // 按地块编号：所在县址在 _plots 里的下标(-1 不在)；县址变了重建
    private static int[] _plotOf = Array.Empty<int>();
    private static bool _indexDirty = true;
    // 上回维护时的 WildernessSystem.Builds
    private static int _synced = -1;
    // 等着设县的(县址编号)
    private static readonly Queue<int> ToFound = new();
    private static readonly HashSet<int> Queued = new();
    private static float _nameAt;
    private static int _nameCursor;

    // 县址的地块有变(划、并、去地块、放出)时 +1：图层据此重画县界(起名、设县不算，悬停提示另按 TipKey 重显)
    public static int Version { get; private set; }

    public static int NextId => _nextId;

    #region 查询

    private static TileZone Zone(int id) => id >= 0 ? World.world?.zone_calculator?.getZoneByID(id) : null;

    private static void EnsureIndex()
    {
        if (!_indexDirty) return;
        int size = 0;
        foreach (WildPlot plot in _plots)
            foreach (int id in plot.zones)
                if (id >= size) size = id + 1;
        _plotOf = new int[size];
        for (int i = 0; i < size; i++) _plotOf[i] = -1;
        ById.Clear();
        for (int index = 0; index < _plots.Count; index++)
        {
            WildPlot plot = _plots[index];
            ById[plot.id] = plot;
            foreach (int id in plot.zones)
                if (id >= 0) _plotOf[id] = index;
        }
        _indexDirty = false;
    }

    private static int IndexAt(int zoneId)
    {
        EnsureIndex();
        return zoneId >= 0 && zoneId < _plotOf.Length ? _plotOf[zoneId] : -1;
    }

    private static void SetIndex(int zoneId, int index)
    {
        if (zoneId < 0) return;
        if (zoneId >= _plotOf.Length)
        {
            int old = _plotOf.Length;
            Array.Resize(ref _plotOf, Math.Max(zoneId + 1, old * 2));
            for (int i = old; i < _plotOf.Length; i++) _plotOf[i] = -1;
        }
        _plotOf[zoneId] = index;
    }

    // 这块地划在哪个县址里(没有为 null)；无人区关着时县址不管事
    public static WildPlot PlotOf(TileZone zone)
    {
        if (zone == null || _plots.Count == 0 || !WildernessSystem.Enabled) return null;
        int index = IndexAt(zone.id);
        return index >= 0 && index < _plots.Count ? _plots[index] : null;
    }

    // 这块地所在县址的编号(图层画县界用)；不在为 -1
    public static int IdAt(int zoneId)
    {
        if (_plots.Count == 0 || !WildernessSystem.Enabled) return -1;
        int index = IndexAt(zoneId);
        return index >= 0 && index < _plots.Count ? _plots[index].id : -1;
    }

    // 已设县的县城(毁了、没了为 null)
    public static City CityOf(WildPlot plot)
    {
        if (plot == null || plot.city < 0) return null;
        City city = World.world?.cities?.get(plot.city);
        return city?.data == null || city.isRekt() ? null : city;
    }

    // 城能不能领这块地(CityExpansionPatch.CanAddZone 里问；神力手动划地、手动营建、建城与新城落脚的头一块地那里先放行了)：
    // 不在县址里的照旧；县址里的只给这一县已设的县城，没设县的谁也不给
    public static bool MayTake(City city, TileZone zone)
    {
        WildPlot plot = PlotOf(zone);
        return plot == null || city?.data != null && plot.city == city.id && CityOf(plot) == city;
    }

    // 已设县的县址(CityExpansionPatch.AfterCanStartCityHere 里问)：里面不另立新城；没设县的照选，立了就是这一县的县城(见 OnCityBuilt)
    public static bool Taken(TileZone zone) => CityOf(PlotOf(zone)) != null;

    private static bool Civil(Kingdom kingdom) => kingdom?.data != null && !kingdom.isRekt() && !kingdom.wild && kingdom.isCiv();

    private static bool SameRealm(Kingdom a, Kingdom b)
    {
        if (a == b) return true;
        if (a == null || b == null) return false;
        Empire empire = a.GetEmpire();
        return empire != null && empire == b.GetEmpire();
    }

    // 开满了、或已不在无人区里的空地(开发中途零碎得不成片的)：县城领得进来
    private static bool Opened(TileZone zone) =>
        zone != null && zone.city == null && zone.tiles_with_ground > 0 && !WildernessSystem.IsWild(zone);

    // 解锁(见类头)：县治那块开发过半，或全县平均开发到 UnlockAverage；average 为全县平均(已归城的、开满的按满算)
    private static bool Unlocked(WildPlot plot, out float average)
    {
        float sum = 0f, seat = 0f;
        int count = 0;
        foreach (int id in plot.zones)
        {
            TileZone zone = Zone(id);
            if (zone == null) continue;
            float dev = zone.city != null ? WildernessDevelopmentSystem.Full : WildernessDevelopmentSystem.DevelopmentOf(zone);
            sum += dev;
            count++;
            if (id == plot.seat) seat = dev;
        }
        average = count > 0 ? sum / count : 0f;
        return seat >= WildernessDevelopmentSystem.Half || average >= UnlockAverage;
    }

    // 一县的地块数：目标、不到就并进邻县的下限、超过就再分的上限(不过每城地块上限，县城领得下自己的县)
    private static void Sizes(out int target, out int min, out int max)
    {
        int cap = ModClass.CITY_MAX_ZONES;
        target = cap > 0 ? Mathf.Clamp(Mathf.RoundToInt(cap / 4f), TargetMin, TargetMax) : TargetUnlimited;
        max = cap > 0 ? Math.Min(target * 2, cap) : target * 2;
        target = Math.Max(1, Math.Min(target, max));
        min = Math.Max(2, target / 3);
    }

    #endregion

    #region 开关、存读档

    // 随 WildernessDevelopmentSystem 一起(新世界、读档)
    public static void ResetWorldState()
    {
        _plots = new List<WildPlot>();
        _nextId = 1;
        ClearRuntime();
    }

    private static void ClearRuntime()
    {
        ById.Clear();
        _plotOf = Array.Empty<int>();
        _indexDirty = true;
        _synced = -1;
        ToFound.Clear();
        Queued.Clear();
        _nameAt = 0f;
        _nameCursor = 0;
        Version++;
    }

    public static void Load(List<WildPlot> saved, int nextId)
    {
        _plots = saved ?? new List<WildPlot>();
        _plots.RemoveAll(plot => plot?.zones == null || plot.zones.Count == 0);
        _nextId = Math.Max(1, nextId);
        foreach (WildPlot plot in _plots)
        {
            plot.zones.RemoveAll(id => id < 0);
            _nextId = Math.Max(_nextId, plot.id + 1);
        }
        _plots.RemoveAll(plot => plot.zones.Count == 0);
        ClearRuntime();
    }

    public static List<WildPlot> Save() => _plots;

    #endregion

    #region 每帧

    // 每帧(WildernessDevelopmentSystem.Tick 里，划好区以后)：划区变了就维护、补划县址；设县的队每帧至多设一县；隔几秒给没名的县址起名
    public static void Tick()
    {
        try
        {
            if (!WildernessSystem.Enabled || World.world?.zone_calculator == null) return;
            if (WildernessSystem.Builds != _synced)
            {
                _synced = WildernessSystem.Builds;
                using (FrameProfiler.Measure("无人区·县址")) Sync();
                return;
            }
            if (ToFound.Count > 0)
            {
                int id = ToFound.Dequeue();
                Queued.Remove(id);
                EnsureIndex();
                if (ById.TryGetValue(id, out WildPlot plot)) Found(plot);
                return;
            }
            float now = Time.realtimeSinceStartup;
            if (now < _nameAt && now >= _nameAt - NameSeconds) return;
            _nameAt = now + NameSeconds;
            NamePass();
        }
        catch (Exception exception)
        {
            ToFound.Clear();
            Queued.Clear();
            LogService.LogWarning($"[EmpireCraft][县址] 结算失败: {exception}");
        }
    }

    // 每年一回(WildernessDevelopmentSystem.BeginYear 开头，在开满的地块归城之前)：县城没了的回到未设县；已设县的把挨着县城的开满地块领进来
    // (县城到了上限领不下的放出县址，同一年里就照旧归别的城)；未设县、已解锁、有国家来设的排进设县的队(每国每年至多几县，开发得多的先)
    public static void Year()
    {
        try
        {
            if (!WildernessSystem.Enabled || _plots.Count == 0 || World.world?.zone_calculator == null) return;
            EnsureIndex();
            bool released = false;
            var candidates = new List<(WildPlot plot, Kingdom founder, float average)>();
            foreach (WildPlot plot in _plots)
            {
                City city = CityOf(plot);
                if (city == null && plot.city >= 0) plot.city = -1L;
                if (city != null)
                {
                    if (Absorb(plot, city)) released = true;
                    continue;
                }
                if (Queued.Contains(plot.id) || !Unlocked(plot, out float average)) continue;
                Kingdom founder = FounderOf(plot);
                if (founder != null) candidates.Add((plot, founder, average));
            }
            if (released)
            {
                _plots.RemoveAll(plot => plot.zones.Count == 0);
                _indexDirty = true;
                Version++;
            }
            var quota = new Dictionary<long, int>();
            foreach (var candidate in candidates.OrderByDescending(item => item.average).ThenBy(item => item.plot.id))
            {
                Kingdom founder = candidate.founder;
                quota.TryGetValue(founder.id, out int used);
                if (used >= 1 + (founder.cities?.Count ?? 0) / CitiesPerExtraFound) continue;
                quota[founder.id] = used + 1;
                if (!Queued.Add(candidate.plot.id)) continue;
                ToFound.Enqueue(candidate.plot.id);
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][县址] 年度结算失败: {exception}");
        }
    }

    #endregion

    #region 维护与划分

    // 划区之后(见 Tick)：维护已有县址(见类头"稳定")、给新地块划县址
    private static void Sync()
    {
        var watch = global::System.Diagnostics.Stopwatch.StartNew();
        ZoneCalculator calc = World.world.zone_calculator;
        bool changed = false;
        int dropped = 0;
        for (int i = _plots.Count - 1; i >= 0; i--)
        {
            WildPlot plot = _plots[i];
            City city = CityOf(plot);
            int before = plot.zones.Count;
            plot.zones.RemoveAll(id => !Keeps(calc.getZoneByID(id), city));
            if (plot.zones.Count != before)
            {
                changed = true;
                dropped += before - plot.zones.Count;
            }
            if (plot.zones.Count == 0)
            {
                _plots.RemoveAt(i);
                changed = true;
                continue;
            }
            // 县治：没设县的要是县里一块空地，已设县的是县城的地
            TileZone seat = plot.zones.Contains(plot.seat) ? Zone(plot.seat) : null;
            if (seat != null && (seat.city == null || city != null && seat.city == city)) continue;
            TileZone at = city?.getTile()?.zone;
            plot.seat = at != null && plot.zones.Contains(at.id) ? at.id : Center(plot.zones, calc);
            changed = true;
        }
        if (changed) _indexDirty = true;
        EnsureIndex();
        int added = Divide(calc, out int attached);
        if (added > 0 || attached > 0) changed = true;
        if (!changed) return;
        _indexDirty = true;
        EnsureIndex();
        Version++;
        LogService.LogInfo($"[EmpireCraft][县址] 共 {_plots.Count} 县，新划 {added} 县、并进邻县 {attached} 块、去掉 {dropped} 块，" +
                           $"用时 {watch.Elapsed.TotalMilliseconds:0} ms");
    }

    // 县址里留不留这块地(见类头"稳定")
    private static bool Keeps(TileZone zone, City city)
    {
        if (zone == null) return false;
        if (zone.city != null) return city != null && zone.city == city;
        if (WildernessDevelopmentSystem.DevelopmentOf(zone) > 0f) return true;
        WildRegion region = WildernessSystem.RegionOf(zone);
        return region != null && WildernessDevelopmentSystem.Develops(region.Kind);
    }

    // 新地块：雨林大泽无人区里还没划进县址、没开满的地块，按连成的块(同一片里四邻相连)分——大块照地形分成若干县(见 Partition)；
    // 小块(不到目标的一半)并进挨着的县址(交界代价最低的，一样低的并进小的；县址已满一倍半的不并；挨着城的小块只隔着平地才并)，
    // 并不进的不划。返回新划几县、并进几块
    private static int Divide(ZoneCalculator calc, out int attached)
    {
        attached = 0;
        Sizes(out int target, out int min, out int max);
        int minNew = Math.Max(min, target / 2);
        IReadOnlyList<WildRegion> regions = WildernessSystem.Regions;
        var mark = new Dictionary<int, int>();
        for (int r = 0; r < regions.Count; r++)
        {
            WildRegion region = regions[r];
            if (region == null || !WildernessDevelopmentSystem.Develops(region.Kind)) continue;
            foreach (int id in region.Zones)
            {
                if (IndexAt(id) >= 0) continue;
                TileZone zone = calc.getZoneByID(id);
                if (zone == null || zone.city != null || WildernessDevelopmentSystem.DevelopmentOf(zone) >= WildernessDevelopmentSystem.Full)
                    continue;
                mark[id] = r;
            }
        }
        if (mark.Count == 0) return 0;
        // 连成的块(同一片里四邻相连)，大的先划
        var pieces = new List<(List<int> zones, WildRegion region)>();
        var seen = new HashSet<int>();
        var queue = new Queue<int>();
        foreach (int start in mark.Keys.OrderBy(id => id))
        {
            if (!seen.Add(start)) continue;
            int r = mark[start];
            var piece = new List<int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int id = queue.Dequeue();
                piece.Add(id);
                TileZone[] neighbours = calc.getZoneByID(id)?.neighbours;
                if (neighbours == null) continue;
                foreach (TileZone next in neighbours)
                {
                    if (next == null || !mark.TryGetValue(next.id, out int nr) || nr != r || !seen.Add(next.id)) continue;
                    queue.Enqueue(next.id);
                }
            }
            piece.Sort();
            pieces.Add((piece, regions[r]));
        }
        int added = 0;
        foreach (var piece in pieces.OrderByDescending(item => item.zones.Count).ThenBy(item => item.zones[0]))
        {
            if (piece.zones.Count < minNew)
            {
                attached += Attach(piece.zones, calc, max + target / 2, piece.zones.Any(id => TouchesCity(calc.getZoneByID(id))));
                continue;
            }
            List<List<int>> cells = piece.zones.Count <= target ? new List<List<int>> { piece.zones } : Partition(piece.zones, calc, true);
            foreach (List<int> cell in cells)
            {
                var plot = new WildPlot
                {
                    id = _nextId++, kind = (int)piece.region.Kind, region = piece.region.Id, zones = cell, seat = Center(cell, calc)
                };
                _plots.Add(plot);
                ById[plot.id] = plot;
                foreach (int id in cell) SetIndex(id, _plots.Count - 1);
                added++;
            }
        }
        return added;
    }

    // 零星几块并进挨着的县址(一圈圈往里并)；strict(这几块挨着城)：只隔着平地(交界代价不过 MergeCost)才并。返回并进几块
    private static int Attach(List<int> zones, ZoneCalculator calc, int full, bool strict)
    {
        var pending = new List<int>(zones);
        int count = 0;
        bool progress = true;
        while (progress && pending.Count > 0)
        {
            progress = false;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                TileZone zone = calc.getZoneByID(pending[i]);
                if (zone?.neighbours == null) continue;
                int best = -1;
                float bestCost = float.MaxValue;
                foreach (TileZone next in zone.neighbours)
                {
                    int index = next == null ? -1 : IndexAt(next.id);
                    if (index < 0 || index >= _plots.Count || _plots[index].zones.Count >= full) continue;
                    float cost = Barrier(next, zone);
                    if (strict && Enter(zone) + BorderCost * cost > MergeCost) continue;
                    if (cost > bestCost || cost == bestCost && _plots[index].zones.Count >= _plots[best].zones.Count) continue;
                    best = index;
                    bestCost = cost;
                }
                if (best < 0) continue;
                _plots[best].zones.Add(zone.id);
                SetIndex(zone.id, best);
                pending.RemoveAt(i);
                count++;
                progress = true;
            }
        }
        return count;
    }

    // 划分用的图：一块块地(下标)、四邻(-1 没有)与走过去的代价、县治偏好
    private sealed class Graph
    {
        public int Count;
        public int[] Ids;
        public TileZone[] Zones;
        public int[] Next;
        public float[] Cost;
        public float[] Pref;
    }

    private static Graph BuildGraph(List<int> ids, ZoneCalculator calc)
    {
        int n = ids.Count;
        var graph = new Graph { Count = n, Ids = ids.ToArray(), Next = new int[n * 4], Cost = new float[n * 4], Pref = new float[n] };
        var local = new Dictionary<int, int>(n);
        var zones = graph.Zones = new TileZone[n];
        var enter = new float[n];
        for (int i = 0; i < n; i++)
        {
            local[graph.Ids[i]] = i;
            zones[i] = calc.getZoneByID(graph.Ids[i]);
            enter[i] = Enter(zones[i]);
            graph.Pref[i] = Preference(zones[i]);
        }
        for (int i = 0; i < n; i++)
        {
            int slot = 0;
            for (int s = 0; s < 4; s++) graph.Next[i * 4 + s] = -1;
            TileZone[] neighbours = zones[i]?.neighbours;
            if (neighbours == null) continue;
            foreach (TileZone next in neighbours)
            {
                if (slot >= 4) break;
                if (next == null || !local.TryGetValue(next.id, out int j) || Math.Abs(next.x - zones[i].x) + Math.Abs(next.y - zones[i].y) != 1)
                    continue;
                graph.Next[i * 4 + slot] = j;
                graph.Cost[i * 4 + slot] = enter[j] + BorderCost * Barrier(zones[i], next);
                slot++;
            }
        }
        return graph;
    }

    // 走进这块地的代价(见常量)
    private static float Enter(TileZone zone)
    {
        if (zone?.tiles == null) return 1f + RidgeCost;
        float cost = 1f + WaterCost * WaterShare(zone);
        TerrainSystem.Of(zone, out bool impassable);
        if (impassable) cost += RidgeCost;
        else if (TerrainSystem.Mountainous(zone)) cost += MountainCost;
        else if (TerrainSystem.Of(zone) == FrontTerrain.Hills) cost += HillCost;
        return cost;
    }

    private static float WaterShare(TileZone zone)
    {
        int size = Config.CITY_ZONE_SIZE * Config.CITY_ZONE_SIZE;
        return Mathf.Clamp01(1f - zone.tiles_with_ground / (float)size);
    }

    // a、b 交界(四邻)两边贴着的格子对里，不是两边都是干地平地(水面、山地山巅、阻挡格都算挡)的占几成(同 TerrainSystem.Crossable 数路口)
    private static float Barrier(TileZone a, TileZone b)
    {
        if (a == null || b == null) return 1f;
        int dx = b.x - a.x, dy = b.y - a.y;
        if (Math.Abs(dx) + Math.Abs(dy) != 1) return 0f;
        int size = Config.CITY_ZONE_SIZE, ox = a.x * size, oy = a.y * size, open = 0;
        for (int i = 0; i < size; i++)
        {
            int x = dx > 0 ? ox + size - 1 : dx < 0 ? ox : ox + i;
            int y = dy > 0 ? oy + size - 1 : dy < 0 ? oy : oy + i;
            if (Dry(World.world.GetTile(x, y)) && Dry(World.world.GetTile(x + dx, y + dy))) open++;
        }
        return 1f - open / (float)size;
    }

    private static bool Dry(WorldTile tile)
    {
        TileTypeBase type = tile?.Type;
        if (type == null || type.liquid || type.lava || type.mountains || type.summit || type.block) return false;
        TileTypeBase main = tile.main_type;
        return main == null || !main.liquid && !main.mountains && !main.summit;
    }

    // 县治偏好(1 上下)：临水(这块地有水、或四邻有成片的水：大河、湖、海边)+0.3，宜居 +0.2×宜居度，山地 −0.4；
    // 不可通行、没有落得下城的格(UrbanFabric.ManualGround)的几乎不选
    private static float Preference(TileZone zone)
    {
        if (zone?.tiles == null || TerrainSystem.Impassable(zone) || !HasGround(zone)) return 0.05f;
        float pref = 1f + 0.2f * TerrainSystem.Livability(zone);
        bool watered = WaterShare(zone) >= 0.08f;
        if (!watered && zone.neighbours != null)
            foreach (TileZone next in zone.neighbours)
                if (next != null && WaterShare(next) >= 0.3f)
                {
                    watered = true;
                    break;
                }
        if (watered) pref += 0.3f;
        if (TerrainSystem.Mountainous(zone)) pref -= 0.4f;
        return Mathf.Max(0.05f, pref);
    }

    private static bool HasGround(TileZone zone)
    {
        if (zone?.tiles == null) return false;
        if (zone.centerTile != null && UrbanFabric.ManualGround(zone.centerTile)) return true;
        foreach (WorldTile tile in zone.tiles)
            if (UrbanFabric.ManualGround(tile)) return true;
        return false;
    }

    // 一块连着的地按地形分成若干县(见类头)：一、撒种子；二、同时往外长、种子挪到各县当中重长；三、并小(不到 min 的并进交界代价
    // 最低的邻县，一样低的并进小的；挨着城又只隔着河岭连着邻县的不并、不划，留给那座城)、拆大(超过 max 的再按这个法子分一回)
    private static List<List<int>> Partition(List<int> zones, ZoneCalculator calc, bool split)
    {
        Sizes(out int target, out int min, out int max);
        Graph graph = BuildGraph(zones, calc);
        int n = graph.Count;
        int k = Math.Max(split ? 1 : 2, Mathf.RoundToInt(n / (float)target));
        k = Math.Min(k, n);
        var heap = new WorldMap.WorldMapZones.MinHeap();
        // 一、撒种子：头一个取县治偏好最高的，之后每次取离已有种子最远的(乘一点偏好)
        var dist = new float[n];
        for (int i = 0; i < n; i++) dist[i] = float.PositiveInfinity;
        var seeds = new List<int>();
        int first = 0;
        for (int i = 1; i < n; i++)
            if (graph.Pref[i] > graph.Pref[first]) first = i;
        Seed(first);
        while (seeds.Count < k)
        {
            int best = -1;
            float bestScore = 0f;
            for (int i = 0; i < n; i++)
            {
                if (float.IsPositiveInfinity(dist[i])) continue;
                float score = dist[i] * (0.75f + 0.25f * graph.Pref[i]);
                if (score <= bestScore) continue;
                best = i;
                bestScore = score;
            }
            if (best < 0) break;
            Seed(best);
        }

        void Seed(int at)
        {
            seeds.Add(at);
            dist[at] = 0f;
            heap.Push(0f, at);
            while (heap.Count > 0)
            {
                heap.Pop(out float cost, out int u);
                if (cost > dist[u]) continue;
                for (int s = 0; s < 4; s++)
                {
                    int v = graph.Next[u * 4 + s];
                    if (v < 0) continue;
                    float total = cost + graph.Cost[u * 4 + s];
                    if (total >= dist[v]) continue;
                    dist[v] = total;
                    heap.Push(total, v);
                }
            }
        }

        // 二、同时往外长(每块地归代价最小的种子)，种子挪到各县当中，重长
        int[] owner = Grow(graph, seeds, heap);
        var tmp = new float[n];
        for (int round = 0; round < LloydRounds; round++)
        {
            bool moved = false;
            List<int>[] members = Members(owner, seeds.Count);
            for (int c = 0; c < seeds.Count; c++)
            {
                if (members[c].Count == 0) continue;
                int center = Center(graph, members[c], owner, c, tmp, heap);
                if (center == seeds[c]) continue;
                seeds[c] = center;
                moved = true;
            }
            if (!moved) break;
            owner = Grow(graph, seeds, heap);
        }

        // 三、并小
        int cells = seeds.Count;
        var sizes = new int[cells];
        for (int i = 0; i < n; i++) sizes[owner[i]]++;
        var stuck = new bool[cells];
        var loose = new bool[cells];
        while (true)
        {
            int small = -1;
            for (int c = 0; c < cells; c++)
                if (!stuck[c] && sizes[c] > 0 && sizes[c] < min && (small < 0 || sizes[c] < sizes[small])) small = c;
            if (small < 0) break;
            int into = -1;
            float bestCost = float.MaxValue;
            for (int u = 0; u < n; u++)
            {
                if (owner[u] != small) continue;
                for (int s = 0; s < 4; s++)
                {
                    int v = graph.Next[u * 4 + s];
                    if (v < 0 || owner[v] == small) continue;
                    float cost = graph.Cost[u * 4 + s];
                    if (cost > bestCost || cost == bestCost && into >= 0 && sizes[owner[v]] >= sizes[into]) continue;
                    into = owner[v];
                    bestCost = cost;
                }
            }
            if (into < 0)
            {
                stuck[small] = true;
                continue;
            }
            // 挨着城、只隔着河岭连着邻县的小块：不并，留给那座城(见类头)
            if (bestCost > MergeCost && Enumerable.Range(0, n).Any(u => owner[u] == small && TouchesCity(graph.Zones[u])))
            {
                stuck[small] = loose[small] = true;
                continue;
            }
            for (int u = 0; u < n; u++)
                if (owner[u] == small) owner[u] = into;
            sizes[into] += sizes[small];
            sizes[small] = 0;
        }

        // 拆大：超过上限的(两条河夹着的一长条、山间谷地连成的一大片)再分一回
        var result = new List<List<int>>();
        List<int>[] final = Members(owner, cells);
        for (int c = 0; c < cells; c++)
        {
            List<int> cell = final[c];
            if (cell.Count == 0 || loose[c]) continue;
            var ids = cell.Select(i => graph.Ids[i]).OrderBy(id => id).ToList();
            if (split && ids.Count > max) result.AddRange(Partition(ids, calc, false));
            else result.Add(ids);
        }
        return result;
    }

    // 各种子同时按代价往外长：每块地归代价最小的那颗(下标)
    private static int[] Grow(Graph graph, List<int> seeds, WorldMap.WorldMapZones.MinHeap heap)
    {
        int n = graph.Count;
        var owner = new int[n];
        var dist = new float[n];
        for (int i = 0; i < n; i++)
        {
            owner[i] = -1;
            dist[i] = float.PositiveInfinity;
        }
        for (int c = 0; c < seeds.Count; c++)
        {
            dist[seeds[c]] = 0f;
            owner[seeds[c]] = c;
            heap.Push(0f, seeds[c]);
        }
        while (heap.Count > 0)
        {
            heap.Pop(out float cost, out int u);
            if (cost > dist[u]) continue;
            for (int s = 0; s < 4; s++)
            {
                int v = graph.Next[u * 4 + s];
                if (v < 0) continue;
                float total = cost + graph.Cost[u * 4 + s];
                if (total >= dist[v]) continue;
                dist[v] = total;
                owner[v] = owner[u];
                heap.Push(total, v);
            }
        }
        // 连通的一块里都走得到；万一有走不到的(地块四邻变了)归第一颗
        for (int i = 0; i < n; i++)
            if (owner[i] < 0) owner[i] = 0;
        return owner;
    }

    private static List<int>[] Members(int[] owner, int cells)
    {
        var members = new List<int>[cells];
        for (int c = 0; c < cells; c++) members[c] = new List<int>();
        for (int i = 0; i < owner.Length; i++) members[owner[i]].Add(i);
        return members;
    }

    // 一县当中(县治)：县里到各块的代价和(只在这一县里走，走不到的按 Unreached)÷ 县治偏好最小的那块
    private static int Center(Graph graph, List<int> members, int[] owner, int cell, float[] dist, WorldMap.WorldMapZones.MinHeap heap)
    {
        int best = members[0];
        float bestScore = float.MaxValue;
        foreach (int from in members)
        {
            foreach (int i in members) dist[i] = float.PositiveInfinity;
            dist[from] = 0f;
            heap.Push(0f, from);
            while (heap.Count > 0)
            {
                heap.Pop(out float cost, out int u);
                if (cost > dist[u]) continue;
                for (int s = 0; s < 4; s++)
                {
                    int v = graph.Next[u * 4 + s];
                    if (v < 0 || owner[v] != cell) continue;
                    float total = cost + graph.Cost[u * 4 + s];
                    if (total >= dist[v]) continue;
                    dist[v] = total;
                    heap.Push(total, v);
                }
            }
            float sum = 0f;
            foreach (int i in members) sum += float.IsPositiveInfinity(dist[i]) ? Unreached : dist[i];
            float score = sum / graph.Pref[from];
            if (score >= bestScore) continue;
            best = from;
            bestScore = score;
        }
        return best;
    }

    // 一县地块的县治(地块编号)
    private static int Center(List<int> zones, ZoneCalculator calc)
    {
        if (zones == null || zones.Count == 0) return -1;
        if (zones.Count == 1) return zones[0];
        Graph graph = BuildGraph(zones, calc);
        var owner = new int[graph.Count];
        var members = new List<int>(graph.Count);
        for (int i = 0; i < graph.Count; i++) members.Add(i);
        int center = Center(graph, members, owner, 0, new float[graph.Count], new WorldMap.WorldMapZones.MinHeap());
        return graph.Ids[center];
    }

    #endregion

    #region 设县

    // 设县的国家(见类头)：开发者(各块记的开发者按开发度加权，县治那块加倍)；这一县归了别国(OwnerOf，同一帝国的不算)由那国设；
    // 开发者没了的由挨着这一县城最多的国家设；永久中立区不设
    private static Kingdom FounderOf(WildPlot plot)
    {
        var weights = new Dictionary<long, float>();
        Kingdom owner = null;
        bool owned = false;
        TileZone seat = Zone(plot.seat);
        if (seat != null && WildernessSystem.IsWild(seat))
        {
            if (WildernessSystem.RegionOf(seat)?.Neutral == true) return null;
            owner = WildernessSystem.OwnerOf(seat);
            owned = true;
        }
        foreach (int id in plot.zones)
        {
            TileZone zone = Zone(id);
            if (zone == null) continue;
            float dev = WildernessDevelopmentSystem.DevelopmentOf(zone);
            long by = WildernessDevelopmentSystem.DeveloperOf(zone);
            if (by > 0 && dev > 0f)
            {
                weights.TryGetValue(by, out float weight);
                weights[by] = weight + dev * (id == plot.seat ? 2f : 1f);
            }
            if (owned || !WildernessSystem.IsWild(zone)) continue;
            if (WildernessSystem.RegionOf(zone)?.Neutral == true) return null;
            owner = WildernessSystem.OwnerOf(zone);
            owned = true;
        }
        Kingdom developer = null;
        foreach (KeyValuePair<long, float> pair in weights.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key))
        {
            Kingdom kingdom = World.world.kingdoms.get(pair.Key);
            if (!Civil(kingdom)) continue;
            developer = kingdom;
            break;
        }
        if (Civil(owner)) return developer != null && SameRealm(developer, owner) ? developer : owner;
        return developer ?? Neighbour(plot);
    }

    // 挨着这一县(八邻)的城最多的文明国家
    private static Kingdom Neighbour(WildPlot plot)
    {
        var counts = new Dictionary<Kingdom, int>();
        foreach (int id in plot.zones)
        {
            TileZone zone = Zone(id);
            if (zone?.neighbours_all == null) continue;
            foreach (TileZone next in zone.neighbours_all)
            {
                City city = next?.city;
                if (city?.data == null || city.isRekt() || !Civil(city.kingdom) || PlotOf(next) == plot) continue;
                counts.TryGetValue(city.kingdom, out int count);
                counts[city.kingdom] = count + 1;
            }
        }
        return counts.Count == 0 ? null
            : counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key.id).First().Key;
    }

    // 人多地少：人口 ÷ 上限
    private static float Pressure(City city)
    {
        int max = city.getPopulationMaximum();
        if (max <= 0) return 0f;
        float people = CityPopulationSystem.AbstractPopulationEnabled ? CityPopulationSystem.GetTotal(city) : city.getPopulationPeople();
        return people / max;
    }

    // 迁人的城：本国挨着这一县(八邻)的城里人口压力最大的，一样的取近的；没有挨着的取本国离县治最近的
    private static City SourceOf(WildPlot plot, Kingdom founder, TileZone seat)
    {
        WorldTile at = seat.centerTile;
        City best = null;
        float bestPressure = -1f, bestDistance = float.MaxValue;
        var seen = new HashSet<City>();
        foreach (int id in plot.zones)
        {
            TileZone zone = Zone(id);
            if (zone?.neighbours_all == null) continue;
            foreach (TileZone next in zone.neighbours_all)
            {
                City city = next?.city;
                if (city?.data == null || city.isRekt() || city.kingdom != founder || !seen.Add(city)) continue;
                WorldTile tile = city.getTile();
                if (tile == null) continue;
                float pressure = Pressure(city);
                float distance = at == null ? 0f : Toolbox.SquaredDistTile(tile, at);
                if (pressure < bestPressure || pressure == bestPressure && distance >= bestDistance) continue;
                best = city;
                bestPressure = pressure;
                bestDistance = distance;
            }
        }
        if (best != null || at == null) return best;
        return founder.cities?.Where(city => city?.data != null && !city.isRekt() && city.getTile() != null)
            .OrderBy(city => Toolbox.SquaredDistTile(city.getTile(), at)).FirstOrDefault();
    }

    // 设县(见类头)：每帧至多一县，到时再核一遍
    private static void Found(WildPlot plot)
    {
        if (CityOf(plot) != null || !Unlocked(plot, out _)) return;
        TileZone seat = Zone(plot.seat);
        if (seat == null || seat.city != null || !HasGround(seat)) return;
        Kingdom founder = FounderOf(plot);
        if (founder == null || AncientWarfareCompatibility.OwnsObject(founder)) return;
        City source = SourceOf(plot, founder, seat);
        if (source?.data == null || AncientWarfareCompatibility.OwnsObject(source)) return;
        // 战乱：本国在打仗，迁人的城在前线或城下正在交战的不设，明年再看
        if (founder.hasEnemies() && (EnfeoffmentHelper.IsFrontCity(source, founder) || FrontSystem.Battles.ContainsKey(source.data.id)))
            return;
        WildRegion region = WildernessSystem.RegionOf(seat) ?? WildernessSystem.ById(plot.region);
        string place = region != null ? WildernessSystem.DisplayName(region) : WildernessNaming.KindName((WildKind)plot.kind) ?? "";
        // 挂进这块地原来挂的法理(设县后这块地不在无人区里了，先取)，没有就迁人的城的(同开满另立新城)
        KingdomTitle title = WildernessSystem.TitleOf(seat) ?? source.GetTitle();
        City city = CityPlacementSystem.Found(seat, founder, source, null, out string error);
        if (city == null)
        {
            LogService.LogInfo($"[EmpireCraft][县址] {founder.GetKingdomName()} 在县址 {plot.name}({plot.id}) 设县没成：{error}");
            return;
        }
        // OnCityBuilt 已认作县城、OnFounded 已照预定县名改名；万一没认上，这里补上
        if (plot.city != city.id)
        {
            plot.city = city.id;
            plot.seat = seat.id;
            Rename(plot, city);
        }
        if (title != null && !title.isRekt() && KingdomTitleManager.HasRoomFor(title)) title.addCity(city);
        WildernessDevelopmentSystem.Establish(seat, (WildKind)plot.kind, founder.id);
        Absorb(plot, city);
        string county = city.EnsureCityCoreName() ?? plot.name ?? city.GetCityName();
        EventRecorder.Record(founder, string.Format(LM.Get("wild_plot_founded_history"), founder.GetKingdomName(), place, county));
    }

    // 已设县：县址里开满(或已不在无人区里)、空着、挨着县城(四邻，一圈圈连过去)的地块领进县城；
    // 县城到了每城地块上限领不下的放出县址(照旧归别的城)。放了返回 true
    private static bool Absorb(WildPlot plot, City city)
    {
        bool grew = true;
        for (int guard = plot.zones.Count; grew && guard > 0 && !CityExpansionPatch.AtLimit(city); guard--)
        {
            grew = false;
            foreach (int id in plot.zones.ToList())
            {
                TileZone zone = Zone(id);
                // 县城挨着、走得到的(隔水翻山的不领，同原版扩张)
                if (!Opened(zone) || !Touches(city, zone) || !CityExpansionPatch.Reachable(zone, city)) continue;
                if (CityExpansionPatch.AtLimit(city)) break;
                using (WildernessSystem.Settle()) city.addZone(zone);
                if (zone.city == city) grew = true;
            }
        }
        if (!CityExpansionPatch.AtLimit(city) || plot.zones.RemoveAll(id => Opened(Zone(id))) == 0) return false;
        _indexDirty = true;
        Version++;
        return true;
    }

    // 四邻有城的地(哪座城都算)
    private static bool TouchesCity(TileZone zone)
    {
        if (zone?.neighbours == null) return false;
        foreach (TileZone next in zone.neighbours)
            if (next?.city != null) return true;
        return false;
    }

    private static bool Touches(City city, TileZone zone)
    {
        if (zone?.neighbours == null) return false;
        foreach (TileZone next in zone.neighbours)
            if (next?.city == city) return true;
        return false;
    }

    // 开满的地块归城(WildernessDevelopmentSystem.Hand 先问这里)：县址里的只归这一县——已设县、县城挨着、没到上限的当即领进；
    // 否则空着留给县城(没设县的等设县，挨不着的等县城一块块领过来，见 Absorb)。返回 true 表示这块地由县址管
    public static bool Claim(TileZone zone)
    {
        try
        {
            WildPlot plot = PlotOf(zone);
            if (plot == null) return false;
            City city = CityOf(plot);
            if (city != null && Touches(city, zone) && CityExpansionPatch.Reachable(zone, city) && !CityExpansionPatch.AtLimit(city))
                using (WildernessSystem.Settle())
                    city.addZone(zone);
            return true;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][县址] 开满的地块归县城失败: {exception.Message}");
            return false;
        }
    }

    // 新城落成(CityPatch.CityBuilt，哪条路建的都算：AI 移民、国家移民、殖民、穿越者、神力建城、本系统设县)：
    // 落在没设县的县址里就是这一县的县城，县治挪到城心那块
    public static void OnCityBuilt(City city)
    {
        try
        {
            if (city?.data == null || city.isRekt() || AncientWarfareCompatibility.OwnsObject(city)) return;
            TileZone zone = city.getTile()?.zone;
            if (zone == null && city.zones != null && city.zones.Count > 0) zone = city.zones[0];
            WildPlot plot = PlotOf(zone);
            if (plot == null || CityOf(plot) != null) return;
            plot.city = city.id;
            plot.seat = zone.id;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][县址] 新城认县失败: {exception.Message}");
        }
    }

    // 经 CityPlacementSystem.Found 建的城(神力建城、本系统设县)：是县址的县城就照预定县名改名
    public static void OnFounded(City city)
    {
        try
        {
            if (city?.data == null || city.isRekt()) return;
            WildPlot plot = PlotOf(city.getTile()?.zone);
            if (plot == null || plot.city != city.id) return;
            Rename(plot, city);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][县址] 县城改名失败: {exception.Message}");
        }
    }

    // 县城照预定县名改名(同穿越者改临高，见 TransmigrantSystem.RenameCity)：名字记进这座城主流文化的名录，换了文化再换回来还是它；
    // 还没定名的县址拿县城的名字作县名
    private static void Rename(WildPlot plot, City city)
    {
        if (city?.data == null) return;
        if (string.IsNullOrEmpty(plot.name))
        {
            plot.name = city.EnsureCityCoreName() ?? city.GetCityName();
            return;
        }
        city.data.name = plot.name;
        CityExtension.CityExtraData extra = city.GetOrCreate();
        extra.core_name = "";
        extra.core_name_source = "";
        city.EnsureCityCoreName();
        city.RefreshCityDisplayName();
        CultureService.RecordCityCulturalName(city, CultureService.GetMainCulture(city, initialize: false), city.data.name);
    }

    #endregion

    #region 起名

    // 给没名字的县址起名(隔 NameSeconds 秒一回，一回至多看 NameScan 个、起 NamesPerPass 个)：已设县的取县城的名字；别的按县治最近(NameReach 块以内，
    // 世界地图不限远近)的城的主流文化起这个文化的城名，与现有的城、别的县址不重名(重了换个种子再起，至多 NameAttempts 回)
    private static void NamePass()
    {
        List<WildPlot> pending = null;
        foreach (WildPlot plot in _plots)
            if (string.IsNullOrEmpty(plot.name)) (pending ??= new List<WildPlot>()).Add(plot);
        if (pending == null) return;
        bool earth = WorldMap.WorldMapWilderness.OnEarth;
        float reach = earth ? float.MaxValue : NameReach * Config.CITY_ZONE_SIZE;
        var seats = new List<(int x, int y, string culture)>();
        var used = new HashSet<string>();
        foreach (City city in World.world.cities)
        {
            if (city?.data == null || city.isRekt()) continue;
            if (!string.IsNullOrEmpty(city.data.name)) used.Add(city.data.name);
            WorldTile tile = city.getTile();
            string culture = CultureService.GetMainCulture(city, initialize: false);
            if (tile != null && CultureService.IsValidCulture(culture)) seats.Add((tile.x, tile.y, culture));
        }
        foreach (WildPlot plot in _plots)
            if (!string.IsNullOrEmpty(plot.name)) used.Add(plot.name);
        int tries = 0;
        // 一回至多看 NameScan 个(附近没有文明的远处县址多时轮着看)
        int count = Math.Min(pending.Count, NameScan);
        for (int k = 0; k < count && tries < NamesPerPass; k++)
        {
            WildPlot plot = pending[(_nameCursor + k) % pending.Count];
            City founded = CityOf(plot);
            if (founded != null)
            {
                Rename(plot, founded);
                continue;
            }
            WorldTile at = Zone(plot.seat)?.centerTile;
            if (at == null || seats.Count == 0) continue;
            string culture = null;
            float bestDistance = reach >= float.MaxValue ? float.MaxValue : reach * reach;
            foreach ((int x, int y, string seatCulture) in seats)
            {
                float distance = (float)(x - at.x) * (x - at.x) + (float)(y - at.y) * (y - at.y);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                culture = seatCulture;
            }
            if (culture == null) continue;
            tries++;
            string name = null;
            for (int attempt = 0; attempt < NameAttempts && name == null; attempt++)
            {
                string generated = CultureService.GenerateCulturalName(MetaType.City, plot.id * 7919L + attempt * 104729L + 17L, culture);
                if (!string.IsNullOrWhiteSpace(generated) && !used.Contains(generated)) name = generated;
            }
            // 这个文化眼下找不到活着的人借来起名(无小人模式人少)：用原版文化的城名词库起一个
            if (name == null)
            {
                string generated = CulturePatch.GetOnomasticDataSafe(CultureService.GetNativeCultureObject(culture), MetaType.City)
                    ?.generateName()?.UseLocalizedNameSeparator();
                if (!string.IsNullOrWhiteSpace(generated) && !used.Contains(generated)) name = generated;
            }
            if (name == null) continue;
            plot.name = name;
            plot.culture = culture;
            used.Add(name);
        }
        _nameCursor = (_nameCursor + count) % Math.Max(1, pending.Count);
    }

    #endregion

    #region 提示

    // 悬停提示一行(见 WildernessOverlay.ShowTooltip)：预定县名(没名的写"未定名")与状态——未开(几块、平均开发几成)、已解锁待设、已设县某城
    public static bool Describe(TileZone zone, out string value)
    {
        value = "";
        WildPlot plot = PlotOf(zone);
        if (plot == null) return false;
        string name = string.IsNullOrEmpty(plot.name) ? LM.Get("wild_plot_unnamed") : plot.name;
        City city = CityOf(plot);
        bool open = Unlocked(plot, out float average);
        if (city != null) value = string.Format(LM.Get("wild_plot_founded"), name, city.GetCityName());
        else if (open) value = string.Format(LM.Get("wild_plot_open"), name);
        else value = string.Format(LM.Get("wild_plot_locked"), name, plot.zones.Count, Mathf.FloorToInt(average));
        return true;
    }

    // 提示换没换(悬停时每帧比一次，见 WildernessOverlay.Hover)：哪个县址、平均开发的整数、状态、起没起名；不在县址里为 -1
    public static int TipKey(TileZone zone)
    {
        WildPlot plot = PlotOf(zone);
        if (plot == null) return -1;
        bool open = Unlocked(plot, out float average);
        int state = CityOf(plot) != null ? 2 : open ? 1 : 0;
        int named = string.IsNullOrEmpty(plot.name) ? 0 : 1;
        return (((plot.id & 0x1FFFF) * 128 + Mathf.Clamp(Mathf.FloorToInt(average), 0, 100)) * 4 + state) * 2 + named;
    }

    #endregion
}

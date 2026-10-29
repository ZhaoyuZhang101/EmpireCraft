using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 无人区(用户 2026-10-29 "之前的沙漠和冻土什么的地块扩展就算了，我想改成另外一种，那就是利用城市这个实体作出无人区地块，
// 可以有多个类型比如山脉冻土沙漠，将连片的沙漠或者山脉全覆盖并且依据最近的文化进行命名……主要是为了吧地图填满，
// 同时能更有效地阻隔国家军队和更容易计算控制力"；问答选"专门的无人区层""荒地块收回，城心村镇留"，
// 类型山脉、沙漠、冻土雪原、丛林、沼泽)：
//   · 归类：按地块里陆地格的构成判(见 Classify)，门槛严——是整片荒地，不是有点山、有片沙；水多(过四分之一)的不算；
//   · 连片：同类型四邻相连的地块连成一片，至少 MinZones 块才算无人区；太大的不切(一整片撒哈拉就是一片)。
//     城心那块、有村镇(村、镇、关，废墟也算)与厂区的地块留给城，区域在它们周围断开；
//   · 与城互斥(用户 2026-10-29 "无人区层基本和城市层级同级……他一旦变动了也会去挤压或者导致隔壁的城市地块变形的")：
//     一块地要么归城、要么归无人区。每次划区，划进来的地块被城占着就当场从城里划出来(邻城因此变形是预期的)，
//     不再算无人区的放回无主，城照常可以再扩进去；
//     把地给城的路子(扩张、村镇划地、改隶、迁城、边界画笔、建城)碰到无人区都挡住，统一拦在 CityExpansionPatch.CanAddZone
//     (原版 addZone 都经它)。只放行神力手动立村设镇筑关、放厂区(见 Settle)、建城与新城落脚的头一块地
//     (用户 2026-10-20 "手动放置建筑时候不要有任何限制")：那块地归城、当即退出无人区(见 OnZoneTaken)，停手一两秒后重划时区域在那里断开；
//   · 不是城：不进人口、赋税、战争目标、外交、统计这些按城算的系统；
//   · 法理(用户 2026-10-29 "这些无人地块一般会被纳入有实体城市的法理。一旦任何一个国家获取了这个法理，
//     那么这个无人地块就会自动被纳入国家范围，主要是模拟西藏俄罗斯之类的准备的")：每块地挂到离它最近(按地块走、不隔海，
//     至多 MaxAttachSteps 块)、有法理的城所在的那个法理上，一大片可以分给几个法理；持有这个法理的国家
//     (KingdomExtension.FindRealmTitleHolder，同法理之争、继承法理的口径)就算这块地的主人(OwnerOf)，地块仍不归任何城。
//     挂的结果随存档；城或法理变了(建城、法理解散、城换法理)才全部重挂，平时只给新划进来的地块挂，结果稳定；
//   · 起名：新的一片才起(WildernessNaming，按离区域中心最近的城的主流文化，没有城按全图最常见的；种子用区域编号)，
//     重划时按与旧区域重叠最多的沿用旧编号与名字；起名时全图还没有文化的，等有了城再按文化起；
//   · 何时划(用户 2026-10-29 "无人区一般会在地图加载的时候会被立马被识别""如果地形什么的变了这些区层也要更着变动")：
//     读档、新开地图、换世界后(剧本开局连同进世界安排都做完)头一帧当即划，读档先按存档恢复编号、名字、法理再按地形校一遍；
//     设地面格、顶层格(见 MapWallPatch)记下那几块，停手 DebounceSeconds 秒后重判这几块，类型真变了才重划
//     (神力刷一片也只在停手后划一次)；村镇、厂区变了(RuralSettlementSystem.Revision、FactorySystem.Revision)至多每年重划一次；
//   · 开发(见 WildernessDevelopmentSystem)：雨林、大泽的地块按开发度一批批改成草地；开发中的照开发时的类型留在无人区(Reclassify)，
//     开满的留给城(同城心、村镇，见 Regroup 的 anchored)，城照常扩张领进去；山脉、沙漠、冻原固定不开发；
//   · 模组设置「无人区」(默认开)：关掉时没有无人区(Regions 为空、IsWild 全为假；已划出来的地块不还给城，城照常再扩回去)。
// 全图归类只在读档与开关打开时做(每块地 64 格扫一遍)；连片、对旧区域、收地只看地块，几万块地几毫秒。
public enum WildKind { None = 0, Mountain = 1, Desert = 2, Tundra = 3, Jungle = 4, Swamp = 5 }

public sealed class WildRegion
{
    public int Id;                       // 稳定编号(存档里沿用)
    public WildKind Kind;
    public string Name;                  // 全名，如"撒哈拉大沙漠"(由 WildernessNaming 起)
    public string Culture;               // 起名所依的文化 id
    public readonly List<int> Zones = new(); // 地块 id
    public TileZone Center;              // 标名字的位置(区域里最靠中间的一块)
    public int MinX, MinY, MaxX, MaxY;   // 地块坐标外框
    // 这一片的底色(显示用)：按类型定色系，按编号微调，挨着的两片不至于一个颜色；编号不变颜色就不变
    public Color32 Color;
    // 永久中立区(神力手动设的，见 WildernessSystem.ToggleNeutral)：不归任何国家，法理、周边的城都不管
    public bool Neutral;
    // 周边的城全属于同一个国家时就是它(见 OwnerOf)，否则为 null；随归属一起隔几秒核一次
    public Kingdom Surround;
}

// 存档里的一片无人区(见 SaveData.wild_regions)
public class WildRegionData
{
    public int id;
    public int kind;
    public string name;
    public string culture;
    public List<int> zones = new List<int>();
    // 与 zones 一一对应：各块挂的法理编号(没挂为 -1)
    public List<long> titles = new List<long>();
    // 永久中立区
    public bool neutral;
    public bool ShouldSerializeneutral() => neutral;
}

public static class WildernessSystem
{
    // 归类门槛(占地块陆地格的比例)：
    //   · 水面、岩浆过四分之一的不算(海岸、湖、河网不是荒地，海另有海区)；陆地不到 MinLand 格的不算；
    //   · 山脉：山地、山巅格(原版的阻挡格)过半，或三成五以上且连丘陵、石地占到四分之三——
    //     战线地形的"山地"只要两成五、城市扩张的"山地区块"两成(TerrainSystem)，那是"有点山"；
    //     不可通行要阻挡格占四分之三或山脊拦腰截断，过半已是山腹，三成五加满坡丘陵是山脉的两肩；
    //   · 沙漠、冻土雪原：沙地荒漠或冻土雪地(连雪丘)占八成五——同剧本铺辖地的"整块荒漠"(WorldMapZones)，
    //     比纯沙漠纯冻土(BarrenLandSystem，一格不差)松一点，绿洲边上几格草不至于把大漠戳成筛子；两样混着的按多的那样算；
    //   · 沼泽七成五、丛林八成五(丛林宜居度不低，城常扩进去，门槛更严)；两样混着够八成五的按多的那样算；
    //   · 开过的田不算荒地(路、城墙铺在上面的看底下的地面，同 TerrainSystem)；
    //   · 已是无人区的那一类门槛降一成(Hysteresis)：草木蔓生、刷几格地，边上的地块不至于今天划进明天划出
    private const float MaxWater = 0.25f;
    private const int MinLand = 16;
    private const float MountainCore = 0.5f, MountainFlank = 0.35f, MountainRugged = 0.75f;
    private const float BarrenShare = 0.85f, SwampShare = 0.75f, JungleShare = 0.85f;
    private const float Hysteresis = 0.1f;
    // 至少几块连成一片才算无人区
    public const int MinZones = 4;
    // 外沿放宽(用户 2026-10-29 "你这无人区也没覆盖完全啊")：核心按上面的严门槛定，再一圈圈往外收这一类地形过半的地块
    // (山脉连同丘陵石地，沙漠冻原连同两样，雨林大泽连同两样)，直到收不动——山脚的丘陵带、沙漠丛林的外沿都并进来；
    // 平滑国界下只涂这一类地形的格子(见 WildernessShape)，收进来的地块里别的格交给邻城、露地形；
    // 已是这一片的地块门槛再降一成(同 Hysteresis)
    // 山脉的外沿门槛高一档(六成)：五成时山西的太原、临汾、大同盆地被并进太行，安纳托利亚、伊朗、巴尔干的河谷也被收进去
    // (按真实地形预演，见 Tools/WorldMap/preview_wilderness.py)
    private const float EdgeShare = 0.5f, MountainEdgeShare = 0.6f;
    // 改地形停手几秒后重判；一直有格子在变(草木蔓生)时至多等几秒；法理挂接与主人几秒核对一次(真实时间)
    private const float DebounceSeconds = 1.5f, MaxWaitSeconds = 10f, RefreshSeconds = 2f, RetrySeconds = 30f;
    // 村镇、厂区变了至多隔几年重划一次
    private const int AnchorYears = 1;
    // 挂法理：从有法理的城的地块往外走，至多几块(不隔海)；再远的不挂
    private const int MaxAttachSteps = 64;

    private static bool _enabled = true;
    private static List<WildRegion> _regions = new();
    private static readonly Dictionary<int, WildRegion> IdIndex = new();
    // 按地块编号：所在区域在 _regions 里的下标(-1 不是)、地形判的类型(没算过、不是都为 0)、挂的法理
    private static int[] _regionOf = new int[0];
    private static byte[] _kind = new byte[0];
    private static KingdomTitle[] _titles = new KingdomTitle[0];
    private static TileZone[] _zones = new TileZone[0];
    private static bool _built;
    private static int _nextId = 1;
    // 读档读进来、还没用上的(头一回划区时按它沿用编号名字法理)；关掉开关时也把现有的存在这里
    private static List<WildRegionData> _saved;
    // 改过格子、等着重判的地块；有地块被手动放行给了城(见 OnZoneTaken)时类型没变也要重划(_regroup)；
    // 有事等着(_pending)：头一件、最近一件的时间(真实时间，去抖)
    private static readonly HashSet<int> Dirty = new();
    private static bool _regroup, _pending;
    private static float _firstDirtyAt, _lastDirtyAt, _nextRefresh, _retryAt;
    private static double _builtAt = -1d;
    private static int _settlementRevision, _factoryRevision;
    private static long _titleSignature;
    private static readonly Dictionary<KingdomTitle, Kingdom> Owners = new();
    private static int _settling;

    public static bool Enabled => _enabled;

    // 区域每变一次 +1(显示、通行据此重算)；挂接或归属变了也 +1
    public static int Version { get; private set; }

    public static IReadOnlyList<WildRegion> Regions => _regions;

    public static int NextId => _nextId;

    public static WildRegion RegionOf(TileZone zone)
    {
        int index = Index(zone);
        return index >= 0 ? _regions[index] : null;
    }

    public static bool IsWild(TileZone zone) => Index(zone) >= 0;

    public static bool IsWildAt(int x, int y) => _regions.Count > 0 && IsWild(World.world?.GetTile(x, y)?.zone);

    public static WildKind KindOf(TileZone zone)
    {
        int index = Index(zone);
        return index >= 0 ? _regions[index].Kind : WildKind.None;
    }

    public static WildRegion ById(int id) => IdIndex.TryGetValue(id, out WildRegion region) ? region : null;

    // 这块无人区挂在哪个法理上(没有为 null)
    public static KingdomTitle TitleOf(TileZone zone)
    {
        if (Index(zone) < 0 || zone.id >= _titles.Length) return null;
        KingdomTitle title = _titles[zone.id];
        return title == null || title.isRekt() ? null : title;
    }

    // 这块无人区归哪国(用户 2026-10-29 "关于并进哪个国家我建议用法理判断, 如果他属于一个法理那么一旦有国家获得这个法理
    // 那么这个地会被百分百划入这个国家, 如果周边的几个城市全属于同一个国家这个无人区也会被划入, 否则他是中立的.
    // 也可以手动将他设置成永久中立区")：
    //   · 永久中立区(神力设的，见 ToggleNeutral)：不归任何国家；
    //   · 挂了法理、法理有持有国(KingdomExtension.FindRealmTitleHolder)：归持有国；
    //   · 否则这一片周边的城(与区域相邻的地块上的城)全属于同一个国家：归那个国家(见 RefreshOwners)；
    //   · 都不是：中立(null)
    public static Kingdom OwnerOf(TileZone zone)
    {
        int index = Index(zone);
        if (index < 0) return null;
        WildRegion region = _regions[index];
        if (region.Neutral) return null;
        KingdomTitle title = TitleOf(zone);
        if (title != null && Owners.TryGetValue(title, out Kingdom holder) && holder != null && !holder.isRekt()) return holder;
        Kingdom around = region.Surround;
        return around != null && !around.isRekt() ? around : null;
    }

    // 取得这块无人区所挂法理的国家(不管永久中立、周边的城；悬停提示里写明法理归谁，看得出为什么没并进国土)
    public static Kingdom HolderOf(TileZone zone)
    {
        KingdomTitle title = TitleOf(zone);
        return title != null && Owners.TryGetValue(title, out Kingdom holder) && holder != null && !holder.isRekt() ? holder : null;
    }

    // 这块无人区是因取得法理而归属的(不是因周边的城同属一国)：悬停提示里写明是哪个法理
    public static bool OwnedByTitle(TileZone zone)
    {
        KingdomTitle title = TitleOf(zone);
        return title != null && Owners.TryGetValue(title, out Kingdom holder) && holder != null && holder == OwnerOf(zone);
    }

    // 神力「中立区」：点一片无人区设为永久中立区，再点一次取消；返回那一片(不是无人区为 null)
    public static WildRegion ToggleNeutral(TileZone zone)
    {
        WildRegion region = RegionOf(zone);
        if (region == null) return null;
        region.Neutral = !region.Neutral;
        Version++;
        return region;
    }

    // 一块地归城就不算无人区(两者互斥)：放行给城的那一刻到下次重划之间也照这个算
    private static int Index(TileZone zone)
    {
        if (!_enabled || zone == null) return -1;
        int id = zone.id;
        if (id < 0 || id >= _regionOf.Length) return -1;
        int index = _regionOf[id];
        return index >= 0 && index < _regions.Count && zone.city == null ? index : -1;
    }

    #region 放行、改地形

    // using (WildernessSystem.Settle()) { city.addZone(...); }：神力手动立村设镇筑关、放厂区时那块无人区放行给城(见 CanTake)
    public static SettleScope Settle()
    {
        _settling++;
        return default;
    }

    public readonly struct SettleScope : IDisposable
    {
        public void Dispose()
        {
            if (_settling > 0) _settling--;
        }
    }

    // 无人区的地能不能给这座城(CityExpansionPatch.CanAddZone 里问)：神力手动营建(Settle)、手动建城(CityPlacementSystem.Placing)、
    // 新城落脚的头一块地(城心总归城；剧本、殖民、穿越者降临选址都先避开无人区，见 CityExpansionPatch.AfterCanStartCityHere)
    public static bool CanTake(City city) =>
        _settling > 0 || CityPlacementSystem.Placing || city?.zones == null || city.zones.Count == 0;

    // 地块归了城(City.addZone 之后，见 CityExpansionPatch.AfterAddZone)：当即退出无人区，停手后重划
    public static void OnZoneTaken(TileZone zone)
    {
        if (!_built || zone == null || zone.id < 0 || zone.id >= _regionOf.Length) return;
        int index = _regionOf[zone.id];
        if (index < 0 || index >= _regions.Count) return;
        _regionOf[zone.id] = -1;
        _regions[index].Zones.Remove(zone.id);
        if (zone.id < _titles.Length) _titles[zone.id] = null;
        _regroup = true;
        Pending();
        Version++;
    }

    // 一块地开发满了(或开满的又改回去，见 WildernessDevelopmentSystem)：停手后重划，开满的留给城
    public static void OnDeveloped(TileZone zone)
    {
        if (!_built || zone == null || zone.id < 0) return;
        Dirty.Add(zone.id);
        _regroup = true;
        Pending();
    }

    // 设地面格、顶层格之后(见 MapWallPatch)：记下这块地，停手后重判(加载、建图时设的格不管，那时还没划区)
    public static void OnTileChanged(TileZone zone)
    {
        if (!_built || zone == null || zone.id < 0) return;
        Dirty.Add(zone.id);
        Pending();
    }

    private static void Pending()
    {
        float now = Time.realtimeSinceStartup;
        if (!_pending)
        {
            _pending = true;
            _firstDirtyAt = now;
        }
        _lastDirtyAt = now;
    }

    #endregion

    #region 开关、存读档

    public static void SetEnabled(bool on)
    {
        if (on == _enabled) return;
        // 关掉：编号、名字、法理存起来，再打开时沿用
        if (!on && _built) _saved = Save();
        _enabled = on;
        _regions = new List<WildRegion>();
        IdIndex.Clear();
        _regionOf = new int[0];
        _titles = new KingdomTitle[0];
        Owners.Clear();
        Dirty.Clear();
        _regroup = _pending = false;
        _built = false;
        Version++;
    }

    public static void ResetWorldState()
    {
        _regions = new List<WildRegion>();
        IdIndex.Clear();
        _regionOf = new int[0];
        _kind = new byte[0];
        _titles = new KingdomTitle[0];
        _zones = new TileZone[0];
        _built = false;
        _nextId = 1;
        _saved = null;
        Dirty.Clear();
        _firstDirtyAt = _lastDirtyAt = _nextRefresh = _retryAt = 0f;
        _regroup = _pending = false;
        _builtAt = -1d;
        _titleSignature = 0L;
        Owners.Clear();
        _settling = 0;
        Version++;
    }

    public static void Load(List<WildRegionData> saved, int nextId)
    {
        _saved = new List<WildRegionData>();
        _nextId = Math.Max(1, nextId);
        if (saved == null) return;
        foreach (WildRegionData data in saved)
        {
            if (data?.zones == null || data.zones.Count == 0 || data.kind <= 0 || data.kind > (int)WildKind.Swamp) continue;
            data.titles ??= new List<long>();
            _saved.Add(data);
            _nextId = Math.Max(_nextId, data.id + 1);
        }
    }

    public static List<WildRegionData> Save()
    {
        if (!_built || !_enabled) return _saved ?? new List<WildRegionData>();
        var result = new List<WildRegionData>(_regions.Count);
        foreach (WildRegion region in _regions)
        {
            var data = new WildRegionData { id = region.Id, kind = (int)region.Kind, name = region.Name, culture = region.Culture,
                neutral = region.Neutral };
            foreach (int id in region.Zones)
            {
                KingdomTitle title = id < _titles.Length ? _titles[id] : null;
                data.zones.Add(id);
                data.titles.Add(title == null || title.isRekt() ? -1L : title.id);
            }
            result.Add(data);
        }
        return result;
    }

    #endregion

    #region 每帧

    // 每帧(ModClass.Update)：加载完头一帧划区；改过地形的停手后重判；村镇厂区变了一年一划；法理挂接与主人几秒一核
    public static void Tick()
    {
        if (!_enabled) return;
        MapBox world = World.world;
        if (world?.zone_calculator?.zones == null || world.cities == null || !Config.game_loaded || SmoothLoader.isLoading() ||
            ModClass.IS_CLEAR || WorldMap.WorldMapGenerator.Generating || WorldMap.WorldMapScenario.AfterLoadPending) return;
        float now = Time.realtimeSinceStartup;
        if (now < _retryAt) return;
        try
        {
            if (!_built)
            {
                using (FrameProfiler.Measure("无人区·划区")) Build(true, true);
                return;
            }
            if (_pending && (now - _lastDirtyAt >= DebounceSeconds || now - _firstDirtyAt >= MaxWaitSeconds))
            {
                using (FrameProfiler.Measure("无人区·重划")) Build(false, _regroup);
                return;
            }
            if ((RuralSettlementSystem.Revision != _settlementRevision || FactorySystem.Revision != _factoryRevision) && YearPassed())
            {
                using (FrameProfiler.Measure("无人区·重划")) Build(false, true);
                return;
            }
            if (now >= _nextRefresh)
            {
                _nextRefresh = now + RefreshSeconds;
                RefreshTitles();
            }
            // 附近有了文明的无名荒地起名(见 NameAll)
            if (now >= _nameAt)
            {
                _nameAt = now + NameSeconds;
                var used = new HashSet<string>(_regions.Where(region => !string.IsNullOrEmpty(region.Name)).Select(region => region.Name));
                if (NameAll(_regions, used)) Version++;
            }
            // 雨林、大泽的开发(每年一推、每帧改一批格子；出错自己兜着，不连累划区)
            WildernessDevelopmentSystem.Tick();
        }
        catch (Exception exception)
        {
            // 划区失败过一会儿再整个重来
            _built = false;
            _retryAt = now + RetrySeconds;
            LogService.LogWarning($"[EmpireCraft][无人区] 划区失败: {exception}");
        }
    }

    private static bool YearPassed()
    {
        double now = World.world.getCurWorldTime();
        return _builtAt < 0d || now < _builtAt || Date.getYearsSince(_builtAt) >= AnchorYears;
    }

    #endregion

    #region 归类

    // 格子种类的标记(按 TileTypeBase.index_id 缓存，同 TerrainSystem.Marks)
    private const int MarkWater = 1, MarkRock = 2, MarkHills = 4, MarkFrozen = 8, MarkSand = 16, MarkJungle = 32, MarkSwamp = 64,
        MarkFarm = 128, MarkPaved = 256;
    private static int[] _marks = new int[0];
    private static TileTypeBase[] _markTypes = new TileTypeBase[0];

    private static int Marks(TileTypeBase type)
    {
        int index = type.index_id;
        if (index < 0) return ComputeMarks(type);
        if (index >= _marks.Length)
        {
            int size = Math.Max(index + 1, 256);
            Array.Resize(ref _marks, size);
            Array.Resize(ref _markTypes, size);
        }
        if (ReferenceEquals(_markTypes[index], type)) return _marks[index];
        int marks = ComputeMarks(type);
        _marks[index] = marks;
        _markTypes[index] = type;
        return marks;
    }

    private static int ComputeMarks(TileTypeBase type)
    {
        int marks = 0;
        if (type.liquid || type.lava) marks |= MarkWater;
        if (type.mountains || type.summit) marks |= MarkRock;
        string id = type.id ?? "";
        if (id == "hills" || type.rocks) marks |= MarkHills;
        if (type.farm_field) marks |= MarkFarm;
        if (type.road || type.wall) marks |= MarkPaved;
        // 冻土雪地：永冻土生物群系、雪地雪丘、冻住的格(同 TerrainSystem 的雪原)
        if (type.biome_id == "biome_permafrost" || id.StartsWith("snow", StringComparison.Ordinal) ||
            id.StartsWith("frozen", StringComparison.Ordinal)) marks |= MarkFrozen;
        else
            switch (type.biome_id)
            {
                case "biome_desert":
                case "biome_sand": marks |= MarkSand; break;
                case "biome_jungle": marks |= MarkJungle; break;
                case "biome_swamp": marks |= MarkSwamp; break;
            }
        return marks;
    }

    // 一格的地形(平滑国界下无人区按格贴着地形画，见 UI.Components.WildernessShape)：口径同 Classify 的格子标记——
    // 水面岩浆、开过的田什么都不算；路、城墙看底下的地面；rock 山地山巅，hills 丘陵石地(不是山地的)；
    // 返回地面是哪一类荒地：冻土雪地为冻原，沙地荒漠为沙漠，丛林为雨林，沼泽为大泽，别的为 None(雪山既是 rock 又是冻原)
    public static WildKind GroundKind(WorldTile tile, out bool rock, out bool hills)
    {
        rock = hills = false;
        TileTypeBase type = tile?.Type;
        if (type == null) return WildKind.None;
        int marks = Marks(type);
        TileTypeBase main = tile.main_type;
        int under = main != null ? Marks(main) : 0;
        if (((marks | under) & MarkWater) != 0 || (marks & MarkFarm) != 0) return WildKind.None;
        rock = ((marks | under) & MarkRock) != 0;
        hills = !rock && ((marks | under) & MarkHills) != 0;
        int ground = (marks & MarkPaved) != 0 && main != null ? under : marks;
        if ((ground & MarkFrozen) != 0) return WildKind.Tundra;
        if ((ground & MarkSand) != 0) return WildKind.Desert;
        if ((ground & MarkJungle) != 0) return WildKind.Jungle;
        return (ground & MarkSwamp) != 0 ? WildKind.Swamp : WildKind.None;
    }

    // 剧本开局铺辖地(WorldMapZones)用：这块地按地形会不会是无人区、哪一类——两边同一套门槛，开局留空的地进世界后正好划成无人区，
    // 不会留下既不归城、也不是无人区的空地
    internal static WildKind ClassifyFresh(TileZone zone) => Classify(zone, WildKind.None);

    // 这块地按地形算哪一类无人区(门槛见类头的常量)；previous 是上回判的，已是那一类的门槛降一成
    private struct Counts
    {
        public int Total, Water, Rock, Hills, Frozen, Sand, Jungle, Swamp;
        public int Land => Total - Water;
    }

    // 一块地各类格子数(水、山地山巅、丘陵石地、冻土雪地、沙地荒漠、丛林、沼泽)
    private static Counts Count(TileZone zone)
    {
        int total = 0, water = 0, rock = 0, hills = 0, frozen = 0, sand = 0, jungle = 0, swamp = 0;
        foreach (WorldTile tile in zone.tiles)
        {
            TileTypeBase type = tile?.Type;
            if (type == null) continue;
            total++;
            int marks = Marks(type);
            TileTypeBase main = tile.main_type;
            int under = main != null ? Marks(main) : 0;
            // 冻住的湖(冰面盖在水上)也算水
            if (((marks | under) & MarkWater) != 0)
            {
                water++;
                continue;
            }
            // 开过的田不算荒地
            if ((marks & MarkFarm) != 0) continue;
            if (((marks | under) & MarkRock) != 0) rock++;
            else if (((marks | under) & MarkHills) != 0) hills++;
            // 路、城墙铺在上面的看底下的地面
            int ground = (marks & MarkPaved) != 0 && main != null ? under : marks;
            if ((ground & MarkFrozen) != 0) frozen++;
            else if ((ground & MarkSand) != 0) sand++;
            else if ((ground & MarkJungle) != 0) jungle++;
            else if ((ground & MarkSwamp) != 0) swamp++;
        }
        return new Counts
        {
            Total = total, Water = water, Rock = rock, Hills = hills, Frozen = frozen, Sand = sand, Jungle = jungle, Swamp = swamp
        };
    }

    private static WildKind Classify(TileZone zone, WildKind previous)
    {
        if (zone?.tiles == null) return WildKind.None;
        Counts c = Count(zone);
        int total = c.Total, water = c.Water, rock = c.Rock, hills = c.Hills, frozen = c.Frozen, sand = c.Sand,
            jungle = c.Jungle, swamp = c.Swamp;
        int land = total - water;
        if (land < MinLand || water > total * MaxWater) return WildKind.None;
        float Need(bool was, float share) => land * (was ? share - Hysteresis : share);
        bool wasMountain = previous == WildKind.Mountain;
        if (rock >= Need(wasMountain, MountainCore) ||
            rock >= Need(wasMountain, MountainFlank) && rock + hills >= Need(wasMountain, MountainRugged))
            return WildKind.Mountain;
        if (frozen + sand >= Need(previous is WildKind.Desert or WildKind.Tundra, BarrenShare))
            return frozen > sand ? WildKind.Tundra : WildKind.Desert;
        bool wet = previous is WildKind.Jungle or WildKind.Swamp;
        if (swamp >= Need(wet, SwampShare)) return WildKind.Swamp;
        if (jungle + swamp >= Need(wet, JungleShare)) return jungle >= swamp ? WildKind.Jungle : WildKind.Swamp;
        return WildKind.None;
    }

    private static bool Reclassify(TileZone zone)
    {
        if (zone == null || zone.id < 0 || zone.id >= _kind.Length) return false;
        WildKind previous = (WildKind)_kind[zone.id];
        byte kind = (byte)Classify(zone, previous);
        // 开发中的雨林大泽(见 WildernessDevelopmentSystem)：格子一批批改成草地，按地形早够不上门槛，没开满不掉出无人区——
        // 照原来的类型(没判过的按开发时记的)留着；已归了城的(那一片小得不成无人区后城扩进去的)不留，免得又从城里划出来
        WildKind held = kind == 0 && zone.city == null ? WildernessDevelopmentSystem.HeldKind(zone.id) : WildKind.None;
        if (held != WildKind.None) kind = (byte)(previous is WildKind.Jungle or WildKind.Swamp ? previous : held);
        if (kind == _kind[zone.id]) return false;
        _kind[zone.id] = kind;
        return true;
    }

    #endregion

    #region 划区

    // reclassifyAll：全图重判(读档、换世界、开关打开)，否则只重判改过格子的地块；force：类型没变也重新连片(村镇厂区变了、地块放行给了城)
    private static void Build(bool reclassifyAll, bool force)
    {
        var watch = global::System.Diagnostics.Stopwatch.StartNew();
        List<TileZone> all = World.world.zone_calculator.zones;
        int size = all.Count;
        foreach (TileZone zone in all)
            if (zone != null && zone.id >= size) size = zone.id + 1;
        if (_zones.Length != size)
        {
            _zones = new TileZone[size];
            _kind = new byte[size];
            _regionOf = new int[0];
            _titles = new KingdomTitle[0];
            reclassifyAll = force = true;
        }
        foreach (TileZone zone in all)
            if (zone != null && zone.id >= 0) _zones[zone.id] = zone;
        bool fromSave = !_built && _saved != null;
        // 读档(或开关重开)：存档里的类型当作上回判的(门槛留余量)，边上的地块不至于一读档就跳出去
        if (fromSave)
            foreach (WildRegionData data in _saved)
            foreach (int id in data.zones)
                if (id >= 0 && id < size) _kind[id] = (byte)data.kind;
        bool changed = false;
        if (reclassifyAll)
            for (int id = 0; id < size; id++) changed |= Reclassify(_zones[id]);
        else
            foreach (int id in Dirty)
                if (id >= 0 && id < size) changed |= Reclassify(_zones[id]);
        Dirty.Clear();
        bool regroup = _regroup;
        _regroup = _pending = false;
        _built = true;
        _builtAt = World.world.getCurWorldTime();
        _settlementRevision = RuralSettlementSystem.Revision;
        _factoryRevision = FactorySystem.Revision;
        if (!changed && !force && !regroup) return;
        int carved = Regroup(fromSave);
        _saved = null;
        Version++;
        LogService.LogInfo($"[EmpireCraft][无人区] 划了 {_regions.Count} 片 {_regions.Sum(region => region.Zones.Count)} 块，" +
                           $"从城里划出 {carved} 块，用时 {watch.Elapsed.TotalMilliseconds:0} ms");
    }

    private sealed class OldRegion
    {
        public int Id;
        public WildKind Kind;
        public string Name, Culture;
        public bool Neutral;
    }

    // 连片、对上旧区域(沿用编号名字)、起名、收地、挂法理；返回从城里划出的块数
    private static int Regroup(bool fromSave)
    {
        int size = _zones.Length;
        // 旧区域：读档后头一回按存档，平时按现有的；各块原先属哪片、挂哪个法理
        var olds = new List<OldRegion>();
        var oldOf = new int[size];
        var oldTitles = new KingdomTitle[size];
        for (int i = 0; i < size; i++) oldOf[i] = -1;
        if (fromSave)
        {
            foreach (WildRegionData data in _saved)
            {
                int index = olds.Count;
                olds.Add(new OldRegion { Id = data.id, Kind = (WildKind)data.kind, Name = data.name, Culture = data.culture, Neutral = data.neutral });
                for (int k = 0; k < data.zones.Count; k++)
                {
                    int id = data.zones[k];
                    if (id < 0 || id >= size) continue;
                    oldOf[id] = index;
                    long titleId = k < data.titles.Count ? data.titles[k] : -1L;
                    KingdomTitle title = titleId >= 0 ? ModClass.KINGDOM_TITLE_MANAGER?.get(titleId) : null;
                    oldTitles[id] = title == null || title.isRekt() ? null : title;
                }
            }
        }
        else
        {
            for (int index = 0; index < _regions.Count; index++)
            {
                WildRegion region = _regions[index];
                olds.Add(new OldRegion { Id = region.Id, Kind = region.Kind, Name = region.Name, Culture = region.Culture, Neutral = region.Neutral });
                foreach (int id in region.Zones)
                {
                    if (id < 0 || id >= size) continue;
                    oldOf[id] = index;
                    if (id < _titles.Length) oldTitles[id] = _titles[id];
                }
            }
        }

        // 留给城的地块：城心那块、有村镇(含废墟、关城)的、厂区占着的、开发满了的；古战争兼容模组的城整座不动
        var anchored = new bool[size];
        foreach (City city in World.world.cities)
        {
            if (city?.data == null || city.isRekt()) continue;
            if (AncientWarfareCompatibility.OwnsObject(city))
            {
                foreach (TileZone zone in city.zones)
                    if (zone != null && zone.id >= 0 && zone.id < size) anchored[zone.id] = true;
                continue;
            }
            TileZone seat = city.getTile()?.zone;
            if (seat != null && seat.id >= 0 && seat.id < size) anchored[seat.id] = true;
        }
        foreach (int id in RuralSettlementSystem.SettledZones())
            if (id >= 0 && id < size) anchored[id] = true;
        foreach (int id in FactorySystem.CoveredZones())
            if (id >= 0 && id < size) anchored[id] = true;
        // 开发满了的雨林大泽(见 WildernessDevelopmentSystem)：退出无人区，草木再蔓生回来也不再划进来
        foreach (int id in WildernessDevelopmentSystem.DevelopedZones())
            if (id >= 0 && id < size) anchored[id] = true;

        // 连片：同类型四邻相连
        var piece = new int[size];
        for (int i = 0; i < size; i++) piece[i] = -1;
        var groups = new List<(List<int> zones, WildKind kind)>();
        var queue = new Queue<int>();
        for (int start = 0; start < size; start++)
        {
            if (piece[start] >= 0 || _kind[start] == 0 || anchored[start] || _zones[start] == null) continue;
            byte kind = _kind[start];
            var zones = new List<int>();
            int mark = groups.Count;
            piece[start] = mark;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int id = queue.Dequeue();
                zones.Add(id);
                TileZone[] neighbours = _zones[id].neighbours;
                if (neighbours == null) continue;
                foreach (TileZone next in neighbours)
                {
                    if (next == null || next.id < 0 || next.id >= size) continue;
                    int nid = next.id;
                    if (piece[nid] >= 0 || _kind[nid] != kind || anchored[nid]) continue;
                    piece[nid] = mark;
                    queue.Enqueue(nid);
                }
            }
            groups.Add((zones, (WildKind)kind));
        }

        // 外沿放宽：大的先长，同一块地只归先长到的那片；长到同类、还没长过的小片就把它整片并进来(核心之间隔着外沿的)；
        // 长完再按 MinZones 去掉太小的
        var counted = new Dictionary<int, Counts>();
        var grown = new bool[groups.Count];
        foreach (int g in Enumerable.Range(0, groups.Count).OrderByDescending(index => groups[index].zones.Count).ToList())
        {
            if (groups[g].zones.Count == 0) continue;
            grown[g] = true;
            WildKind kind = groups[g].kind;
            List<int> zones = groups[g].zones;
            queue.Clear();
            foreach (int id in zones) queue.Enqueue(id);
            while (queue.Count > 0)
            {
                TileZone[] neighbours = _zones[queue.Dequeue()].neighbours;
                if (neighbours == null) continue;
                foreach (TileZone next in neighbours)
                {
                    if (next?.tiles == null || next.id < 0 || next.id >= size) continue;
                    int nid = next.id;
                    int other = piece[nid];
                    if (other >= 0)
                    {
                        if (other == g || grown[other] || groups[other].kind != kind) continue;
                        foreach (int id in groups[other].zones)
                        {
                            piece[id] = g;
                            zones.Add(id);
                            queue.Enqueue(id);
                        }
                        groups[other].zones.Clear();
                        grown[other] = true;
                        continue;
                    }
                    if (anchored[nid]) continue;
                    if (!counted.TryGetValue(nid, out Counts c))
                    {
                        c = Count(next);
                        counted[nid] = c;
                    }
                    if (c.Land < MinLand) continue;
                    int share = kind switch
                    {
                        WildKind.Mountain => c.Rock + c.Hills,
                        WildKind.Desert or WildKind.Tundra => c.Sand + c.Frozen,
                        _ => c.Jungle + c.Swamp
                    };
                    bool was = oldOf[nid] >= 0 && olds[oldOf[nid]].Kind == kind;
                    float edge = kind == WildKind.Mountain ? MountainEdgeShare : EdgeShare;
                    if (share < c.Land * (was ? edge - Hysteresis : edge)) continue;
                    piece[nid] = g;
                    zones.Add(nid);
                    queue.Enqueue(nid);
                }
            }
        }

        // 对旧区域：重叠最多的先配，一片旧区域只配一片(分成几片的大的那片沿用，并起来的沿用重叠多的那片)
        var overlap = new Dictionary<long, int>();
        var kept = new List<int>();
        for (int g = 0; g < groups.Count; g++)
        {
            if (groups[g].zones.Count < MinZones) continue;
            kept.Add(g);
            foreach (int id in groups[g].zones)
            {
                int old = oldOf[id];
                if (old < 0 || olds[old].Kind != groups[g].kind) continue;
                long key = (long)g << 32 | (uint)old;
                overlap.TryGetValue(key, out int count);
                overlap[key] = count + 1;
            }
        }
        var matchOf = new Dictionary<int, int>();
        var taken = new HashSet<int>();
        foreach (KeyValuePair<long, int> pair in overlap.OrderByDescending(pair => pair.Value)
                     .ThenBy(pair => olds[(int)(pair.Key & 0xffffffffL)].Id).ThenBy(pair => pair.Key >> 32))
        {
            int g = (int)(pair.Key >> 32), old = (int)(pair.Key & 0xffffffffL);
            if (matchOf.ContainsKey(g) || taken.Contains(old)) continue;
            matchOf[g] = old;
            taken.Add(old);
        }

        var regions = new List<WildRegion>();
        var used = new HashSet<string>();
        foreach (int g in kept)
        {
            var region = new WildRegion { Kind = groups[g].kind };
            if (matchOf.TryGetValue(g, out int old))
            {
                region.Id = olds[old].Id;
                region.Name = olds[old].Name;
                region.Culture = olds[old].Culture;
                region.Neutral = olds[old].Neutral;
                if (!string.IsNullOrEmpty(region.Name)) used.Add(region.Name);
            }
            else region.Id = _nextId++;
            List<int> zones = groups[g].zones;
            zones.Sort();
            region.Zones.AddRange(zones);
            regions.Add(region);
        }
        regions.Sort((a, b) => a.Id.CompareTo(b.Id));

        // 收地：划进无人区的地块被城占着就从城里划出来(城心、村镇厂区所在的早已留给城，见 anchored)
        var touched = new HashSet<City>();
        int carved = 0;
        foreach (WildRegion region in regions)
        {
            region.Zones.RemoveAll(id =>
            {
                TileZone zone = _zones[id];
                City city = zone.city;
                if (city == null) return false;
                if (city.data == null) return true;
                // 被敌军占着的：占领记在原城名下，划出来就对不上，先清掉(同村镇改隶，见 RuralSettlementSystem.Assign)
                if (city.GetTileZoneOccupier(zone) != null) city.RemoveOccupiedTileZoneFromAll(zone);
                city.removeZone(zone);
                // 划不出来(那座城只剩这一块、受保护)的留给城
                if (zone.city != null) return true;
                touched.Add(city);
                carved++;
                return false;
            });
        }
        regions.RemoveAll(region => region.Zones.Count == 0);
        var regionOf = new int[size];
        for (int i = 0; i < size; i++) regionOf[i] = -1;
        for (int index = 0; index < regions.Count; index++)
            foreach (int id in regions[index].Zones) regionOf[id] = index;
        if (touched.Count > 0)
        {
            CityDistricts.Reset();
            foreach (City city in touched) PureMapPatch.Redraw(city);
            World.world.zone_calculator?.setDrawnZonesDirty();
        }

        _regions = regions;
        _regionOf = regionOf;
        IdIndex.Clear();
        foreach (WildRegion region in regions) IdIndex[region.Id] = region;
        Shape(regions, regionOf);

        // 起名(见 NameAll)
        NameAll(regions, used);

        // 挂法理：沿用原先挂的；城或法理变了全部重挂，没挂的补挂
        var titles = new KingdomTitle[size];
        bool missing = false;
        foreach (WildRegion region in regions)
            foreach (int id in region.Zones)
            {
                KingdomTitle title = oldTitles[id];
                if (title != null && !title.isRekt()) titles[id] = title;
                else missing = true;
            }
        _titles = titles;
        long signature = TitleSignature();
        if (!fromSave && signature != _titleSignature) Attach(true);
        else if (missing) Attach(false);
        _titleSignature = signature;
        RefreshOwners();
        return carved;
    }

    // 标名字的块(区域里离边最远的一块，一样远的取离各块平均位置最近的)、外框、底色
    private static void Shape(List<WildRegion> regions, int[] regionOf)
    {
        int size = _zones.Length;
        var depth = new int[size];
        for (int i = 0; i < size; i++) depth[i] = -1;
        var queue = new Queue<int>();
        for (int index = 0; index < regions.Count; index++)
        {
            WildRegion region = regions[index];
            region.Color = ColorOf(region.Kind, region.Id);
            region.MinX = region.MinY = int.MaxValue;
            region.MaxX = region.MaxY = int.MinValue;
            float cx = 0f, cy = 0f;
            foreach (int id in region.Zones)
            {
                TileZone zone = _zones[id];
                region.MinX = Math.Min(region.MinX, zone.x);
                region.MinY = Math.Min(region.MinY, zone.y);
                region.MaxX = Math.Max(region.MaxX, zone.x);
                region.MaxY = Math.Max(region.MaxY, zone.y);
                cx += zone.x;
                cy += zone.y;
                bool edge = zone.neighbours == null || zone.neighbours.Length < 4;
                if (!edge)
                    foreach (TileZone next in zone.neighbours)
                        if (next == null || next.id < 0 || next.id >= size || regionOf[next.id] != index)
                        {
                            edge = true;
                            break;
                        }
                if (!edge) continue;
                depth[id] = 0;
                queue.Enqueue(id);
            }
            cx /= region.Zones.Count;
            cy /= region.Zones.Count;
            while (queue.Count > 0)
            {
                int id = queue.Dequeue();
                TileZone[] neighbours = _zones[id].neighbours;
                if (neighbours == null) continue;
                foreach (TileZone next in neighbours)
                {
                    if (next == null || next.id < 0 || next.id >= size || regionOf[next.id] != index || depth[next.id] >= 0) continue;
                    depth[next.id] = depth[id] + 1;
                    queue.Enqueue(next.id);
                }
            }
            TileZone best = null;
            int bestDepth = -1;
            float bestDistance = float.MaxValue;
            foreach (int id in region.Zones)
            {
                TileZone zone = _zones[id];
                float distance = (zone.x - cx) * (zone.x - cx) + (zone.y - cy) * (zone.y - cy);
                if (depth[id] < bestDepth || depth[id] == bestDepth && distance >= bestDistance) continue;
                best = zone;
                bestDepth = depth[id];
                bestDistance = distance;
            }
            region.Center = best;
        }
    }

    private static Color32 ColorOf(WildKind kind, int id)
    {
        (int r, int g, int b) = kind switch
        {
            WildKind.Mountain => (128, 112, 96),
            WildKind.Desert => (214, 190, 132),
            WildKind.Tundra => (200, 214, 224),
            WildKind.Jungle => (70, 112, 66),
            WildKind.Swamp => (104, 116, 82),
            _ => (160, 160, 160)
        };
        uint hash = unchecked((uint)id * 2654435761u);
        int shade = (int)(hash % 25u) - 12;
        int tint = (int)(hash / 25u % 9u) - 4;
        byte Clamp(int value) => (byte)Mathf.Clamp(value, 0, 255);
        return new Color32(Clamp(r + shade + tint), Clamp(g + shade), Clamp(b + shade - tint), 255);
    }

    #endregion

    #region 起名

    // 各城城心与主流文化；全图最常见的文化(没有城为 null)
    private static List<(int x, int y, string culture)> Seats(out string common)
    {
        var seats = new List<(int x, int y, string culture)>();
        var counts = new Dictionary<string, int>();
        foreach (City city in World.world.cities)
        {
            if (city?.data == null || city.isRekt()) continue;
            WorldTile seat = city.getTile();
            string culture = CultureService.GetMainCulture(city, initialize: false);
            if (seat == null || string.IsNullOrEmpty(culture)) continue;
            seats.Add((seat.x, seat.y, culture));
            counts.TryGetValue(culture, out int count);
            counts[culture] = count + 1;
        }
        common = counts.Count == 0 ? null
            : counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).First().Key;
        return seats;
    }

    // 起名要有文明(用户 2026-10-29 "这些初始名称应该是没有, 只显示地形类别, 等有文明了才会取名")：区域中心 NameReach 块以内
    // 有城(有主流文化)才按最近那座城的文化起名；没有就不起名，地图上只标类型(山脉、沙漠……，见 DisplayName)，等附近有了城再起
    // (隔 NameSeconds 秒看一次，见 Refresh)。以前没有城时按通用词库先起了名，有了城再按文化改——读档时这样的名一并去掉。
    // 世界地图(真实地球)开局就有文明，真实地名照起(见 WorldMap.WorldMapWilderness)
    private const int NameReach = 48;
    private const float NameSeconds = 10f;
    private static float _nameAt;

    // 标在地图上、提示里的名字：起了名的用名字，还没有文明起名的只写类型
    public static string DisplayName(WildRegion region) =>
        region == null ? "" : !string.IsNullOrEmpty(region.Name) ? region.Name : WildernessNaming.KindName(region.Kind) ?? "";

    // 给没起名(或以前没有文化时按通用词库起的)的各片起名：大的先挑；附近没有文明的不起(名字去掉)。有变化返回 true
    private static bool NameAll(List<WildRegion> regions, HashSet<string> used)
    {
        List<WildRegion> pending = regions.Where(region => string.IsNullOrEmpty(region.Name) || string.IsNullOrEmpty(region.Culture))
            .OrderByDescending(region => region.Zones.Count).ThenBy(region => region.Id).ToList();
        if (pending.Count == 0) return false;
        bool earth = WorldMap.WorldMapWilderness.OnEarth;
        List<(int x, int y, string culture)> seats = Seats(out string common);
        float reach = earth ? float.MaxValue : NameReach * Config.CITY_ZONE_SIZE;
        bool changed = false;
        foreach (WildRegion region in pending)
        {
            string culture = CultureNear(region, seats, earth ? common : null, reach);
            if (culture == null && !earth)
            {
                if (string.IsNullOrEmpty(region.Name)) continue;
                used.Remove(region.Name);
                region.Name = null;
                region.Culture = null;
                changed = true;
                continue;
            }
            if (!string.IsNullOrEmpty(region.Name))
            {
                // 世界地图上没有文化也照起的真实地名，文化还是没有就不必重起
                if (culture == null) continue;
                used.Remove(region.Name);
            }
            region.Culture = culture;
            region.Name = NameOf(region, used);
            used.Add(region.Name);
            changed = true;
        }
        return changed;
    }

    // 离区域中心最近(reach 格以内)的城的主流文化；没有为 fallback
    private static string CultureNear(WildRegion region, List<(int x, int y, string culture)> seats, string fallback, float reach)
    {
        WorldTile center = region.Center?.centerTile;
        if (center == null || seats.Count == 0) return fallback;
        string best = fallback;
        float bestDistance = reach >= float.MaxValue ? float.MaxValue : reach * reach;
        foreach ((int x, int y, string culture) in seats)
        {
            float distance = (float)(x - center.x) * (x - center.x) + (float)(y - center.y) * (y - center.y);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = culture;
        }
        return best;
    }

    private static string NameOf(WildRegion region, HashSet<string> used)
    {
        string name = null;
        try
        {
            // 世界地图先用真实地名(见 WorldMap.WorldMapWilderness；随机地图、没对上的为 null)
            name = WorldMap.WorldMapWilderness.Name(region, used) ??
                   WildernessNaming.Name(region.Kind, region.Culture ?? "", region.Id, used, region.Zones.Count);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][无人区] 起名失败({region.Kind}, {region.Culture}): {exception.Message}");
        }
        if (!string.IsNullOrEmpty(name)) return name;
        // 起名出错时只标类型(重名的后面加编号)
        name = LM.Get("wilderness_fallback_" + region.Kind.ToString().ToLowerInvariant());
        return used.Contains(name) ? name + region.Id : name;
    }

    #endregion

    #region 法理

    // 城与法理的对应变没变(建城、毁城、法理解散、城换法理)：按城累加，顺序无关。没有法理的城也算(它挡着远处的法理挂过来，见 Attach)
    private static long TitleSignature()
    {
        long sum = 0L;
        foreach (City city in World.world.cities)
        {
            if (city?.data == null || city.isRekt()) continue;
            KingdomTitle title = city.GetTitle();
            long titleId = title == null || title.isRekt() ? 0L : title.id;
            unchecked
            {
                sum += city.id * 1_000_003L + titleId * 7_919L + 1L;
            }
        }
        return sum;
    }

    // 从各城的地块往外按地块走(四邻、不过海)，每块无人区挂最先走到它的那座城的法理(那座城没有法理就不挂)；
    // full 为全部重挂，否则只挂没挂的
    private static void Attach(bool full)
    {
        int size = _zones.Length;
        var from = new KingdomTitle[size];
        var step = new int[size];
        for (int i = 0; i < size; i++) step[i] = -1;
        var queue = new Queue<int>();
        var titleOf = new Dictionary<City, KingdomTitle>();
        for (int id = 0; id < size; id++)
        {
            City city = _zones[id]?.city;
            if (city?.data == null || city.isRekt()) continue;
            if (!titleOf.TryGetValue(city, out KingdomTitle title))
            {
                title = city.GetTitle();
                if (title != null && title.isRekt()) title = null;
                titleOf[city] = title;
            }
            // 没有法理的城也从这里起步：离它最近的无人区不挂(由周边的城定归属)，不让远处有法理的城越过它挂过来——
            // 以前只从有法理的城起步，紧挨着祭部落(没有法理)的雨林挂到了北边远处的顾法理上(用户 2026-10-29 截图)
            from[id] = title;
            step[id] = 0;
            queue.Enqueue(id);
        }
        while (queue.Count > 0)
        {
            int id = queue.Dequeue();
            if (step[id] >= MaxAttachSteps) continue;
            TileZone[] neighbours = _zones[id].neighbours;
            if (neighbours == null) continue;
            foreach (TileZone next in neighbours)
            {
                if (next == null || next.id < 0 || next.id >= size || step[next.id] >= 0 || !Land(next)) continue;
                step[next.id] = step[id] + 1;
                from[next.id] = from[id];
                queue.Enqueue(next.id);
            }
        }
        foreach (WildRegion region in _regions)
            foreach (int id in region.Zones)
                if (full || _titles[id] == null || _titles[id].isRekt())
                    // 世界地图时代文件写了史实归属的先挂那个(见 WorldMap.WorldMapWilderness.TitleFor；随机地图为 null)
                    _titles[id] = WorldMap.WorldMapWilderness.TitleFor(_zones[id]) ?? from[id];
    }

    private static bool Land(TileZone zone) => zone.tiles_with_ground > 0 && !SeaRegionSystem.IsSea(zone);

    // 几秒一核：城与法理的对应变了全部重挂；各法理的持有国变了记下
    private static void RefreshTitles()
    {
        if (_regions.Count == 0) return;
        long signature = TitleSignature();
        bool changed = false;
        if (signature != _titleSignature)
        {
            _titleSignature = signature;
            Attach(true);
            changed = true;
        }
        if (RefreshOwners()) changed = true;
        if (changed) Version++;
    }

    // 各法理的持有国(KingdomExtension.FindRealmTitleHolder)；有变化返回 true
    private static bool RefreshOwners()
    {
        var seen = new HashSet<KingdomTitle>();
        foreach (KingdomTitle title in _titles)
            if (title != null) seen.Add(title);
        bool changed = false;
        foreach (KingdomTitle title in seen)
        {
            Kingdom holder = title.isRekt() ? null : title.FindRealmTitleHolder();
            if (holder != null && holder.isRekt()) holder = null;
            if (Owners.TryGetValue(title, out Kingdom old) && old == holder) continue;
            Owners[title] = holder;
            changed = true;
        }
        foreach (KingdomTitle stale in Owners.Keys.Where(title => !seen.Contains(title)).ToList()) Owners.Remove(stale);
        if (RefreshSurround()) changed = true;
        return changed;
    }

    // 各片周边的城(与区域相邻的地块上的城)是不是全属于同一个国家；有变化返回 true。
    // 只看地块四邻八邻上的城，几万块地也只是几毫秒；周边一座城都没有的算中立
    private static bool RefreshSurround()
    {
        bool changed = false;
        foreach (WildRegion region in _regions)
        {
            Kingdom common = null;
            bool mixed = false, any = false;
            foreach (int id in region.Zones)
            {
                TileZone zone = id >= 0 && id < _zones.Length ? _zones[id] : null;
                if (zone?.neighbours_all == null) continue;
                foreach (TileZone next in zone.neighbours_all)
                {
                    City city = next?.city;
                    if (city?.data == null || city.isRekt()) continue;
                    Kingdom kingdom = city.kingdom;
                    if (kingdom == null || kingdom.isRekt() || kingdom.wild)
                    {
                        mixed = true;
                        break;
                    }
                    any = true;
                    if (common == null) common = kingdom;
                    else if (common != kingdom)
                    {
                        mixed = true;
                        break;
                    }
                }
                if (mixed) break;
            }
            Kingdom around = any && !mixed ? common : null;
            if (region.Surround == around) continue;
            region.Surround = around;
            changed = true;
        }
        return changed;
    }

    #endregion
}

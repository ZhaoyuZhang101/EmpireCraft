using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using Newtonsoft.Json;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems.WorldMap;

// 世界地图的无人区按真实地理与史实来(用户 2026-10-29 "然后要适配预设地图得历史设定""包括真实的地理名称和归属")：
//   · 地名：WorldMaps/wilderness.json 的地理单元表(撒哈拉、塔克拉玛干、青藏高原、天山、西伯利亚……，由
//     Tools/WorldMap/wilderness_source.py 生成)。WildernessSystem 给新的一片起名时先问这里(随机地图、旧档没记地图的返回 null，
//     照旧走 WildernessNaming)：区域各块换成经纬度(EarthTerrain 的范围与大小)，找同类型、与区域重合得最多的单元——
//     重合按盖住的块数占"区域 + 单元范围(折成块数)"并起来的比例算，不光数盖住几块：撒哈拉、西伯利亚、青藏高原这类大框
//     底下另有细单元(大沙海、东萨彦岭、可可西里……)，光数块数大框总赢，拆出来的一小片也叫"西伯利亚南段"；按比例，
//     一整片配大框、一小片配框得正好的细单元(一样多取范围小的)；盖住区域过半(MajorShare)的单元优先，免得一个小圈只沾了边
//     (冰岛高地边上沾着瓦特纳冰川的圈)就把整片抢走。一块都没盖住的，取 SnapDegrees 度以内最近的单元；
//   · 叫法的先后：起名文化(离区域最近的城的主流文化)在单元里写了的文化叫法(华夏叫戈壁瀚海、葱岭、流沙……)，
//     再按开局年份的旧称(长白山在唐叫太白山)，最后用现代名；文化叫法、旧称都可限年份；
//   · 拆片：同一个单元被拆成几片时，盖得最多的那片用原名，别的按它在那片的哪个方向加方位
//     (山脉"天山东段"、别的"撒哈拉沙漠西部"，英文 Eastern …)；同方位再撞名的，山脉叫"余脉"、别的叫"边缘"；再撞就交还 WildernessNaming。
//     那片叫原名的若原名已被别处占了(读档前就有同名的)，照样往下加方位；
//   · 史实归属：时代文件的 wilderness 一节(单元 → 政权，如盛唐青藏高原归吐蕃)，开局划区挂法理时，单元盖住的无人区地块
//     都城法理只用来识别政权，实际挂接由 WildernessSystem.HistoricalTitle 寻找同政权的当地城市；wilderness_regions 可明确指定当地法理。
//     一块地落在几个写了归属的单元里，
//     取范围最小的那个；没写的单元照旧挂最近的法理。单元 → 法理编号开局头一回划区时定下、随存档保存；有效法理不因新增城市重分，
//     失效或新出现的无人区地块再按史实归属补挂；
//   · 剧本编辑器刷的归属(用户 10-30 "新的无人区在设置城市和刷地块的时候也要考虑到"；剧本的 paint，见 ScenarioWilderness)：
//     开局摆好时(BeginPaint，导入地图也算)记下各地块中心落在哪种刷的格子里——"p:国家" 挂那国都城的王国法理、"r:地区" 挂那块
//     王国法理，盖过史实归属与"挂最近法理"(TitleFor 先问它)；头一回挂法理时换成法理编号、随存档，往后全部重挂时照样先按它；
//     "n" 永久中立：只将命中的地块设为中立，随无人区存档，不扩大到整片连通区域；
//   · 存档(SaveData.world_map_wild)：地图范围、大小、时代、开局年份与上面的单元 → 法理、刷的地块 → 法理；
//     不是世界地图的为 null(导入地图刷了归属的只存刷的那两项，范围为空，读档不当地球)。
public sealed class WorldMapWildUnit
{
    public string id = "";
    // 能对上的无人区类型(mountain、desert、tundra、jungle、swamp；lava 地球上没有，不写)
    [JsonProperty("k")] public List<string> kinds = new();
    public string zh = "";
    public string tw = "";
    public string en = "";
    // 经纬框 [西, 南, 东, 北]，可几个
    public List<float[]> box = new();
    // 锚点 [经度, 纬度]，离锚点 r 度以内(经向按纬度折算)算盖住
    public List<float[]> pts = new();
    public float r = 3f;
    // 按年份的旧称："到某年为止|简体|英文|繁体"(同 cities.json)，也可写"某年~|…""某年~某年|…"
    public List<string> alt = new();
    // 按文化的叫法：文化 id(可逗号分隔几个) → 若干条，写法同上，不带年份的写"简体|英文|繁体"
    [JsonProperty("c")] public Dictionary<string, List<string>> cultures = new();

    // 范围的外框(含锚点半径)、面积(地图上的平方度，即经度×纬度，可折成块数；也用来比哪个单元更具体)、中心
    [JsonIgnore] public float West, South, East, North, Area, CenterLon, CenterLat;
}

// 存档里的世界地图(见 SaveData.world_map_wild)
public sealed class ScenarioAdministrativeOrigin
{
    public long realm = -1L, title = -1L, empire = -1L;
    public string regime = "";
}

public sealed class ScenarioLandOrigin
{
    public long city = -1L, title = -1L;
    // 手动画定的法理不随来源城市改隶；自动辖境则持续跟随来源城市。
    public bool pinned;
    public bool ShouldSerializepinned() => pinned;
}

public class WorldMapWildSave
{
    public string frame = "";
    public int w;
    public int h;
    public string era = "";
    public int year;
    // 地理单元 → 史实归属的王国法理编号(开局头一回划区时按时代文件定下)
    public Dictionary<string, long> owners = new();
    // 剧本明确指定的当地法理，独立保存；旧档缺失时不重写既有归属。
    public Dictionary<string, long> regional;
    public bool ShouldSerializeregional() => regional is { Count: > 0 };
    // 开局已明确归属的土地；转为无人区、又因地形变化退出时仍有可追溯的行政归属。
    public Dictionary<int, ScenarioLandOrigin> territory;
    public bool ShouldSerializeterritory() => territory is { Count: > 0 };
    // 州府锚点给可能的无人区地块定的行政区：地块编号 → 归本行政区的哪座城(见 WorldMapScenario.AnchorWilderness)
    public Dictionary<int, long> anchors;
    // 明确历史面与手绘范围有意允许独立部分；治所距离推算出的锚点不享有此保护。
    public HashSet<int> bounded_anchors;
    public bool ShouldSerializebounded_anchors() => bounded_anchors is { Count: > 0 };
    public int boundary_revision;
    // 隔山不领(见 WildernessSystem.Passable)：旧档挂好的辖境照存档留着，读旧档后把翻山挂上的改挂一次；新开的局一开始就照这个走
    public int mountain_revision;
    // 剧本设好的行政成员关系；实际改隶、法理变更、帝国变更后自动让位于新安排。
    public Dictionary<long, ScenarioAdministrativeOrigin> administration;
    public int administration_revision;
    // 剧本编辑器刷的归属：王国法理编号 → 地块编号；刷了永久中立、还没设上的地块(头一回划区后设上即清空)
    public Dictionary<long, List<int>> paint;
    public List<int> neutral;
    // 照原图分地的剧本(从当前世界导入的，见 WorldMapEra.exact)里归城的地块：城还领着就不划成无人区(见 WildernessSystem.Regroup)
    public List<int> kept;
    // 本剧本开无人区(见 WorldMapEra.own_wilderness)：随存档，这一局一直开着；照原图归城的地块 → 那座城
    public bool own_wild;
    public Dictionary<int, long> city_claims;
}

public static class WorldMapWilderness
{
    // 一块都没盖住时，离单元范围几度以内还算它；盖住区域多少算"过半"
    private const float SnapDegrees = 2.5f, MajorShare = 0.5f;
    // 拆片的方位：两片中心离得这么近(度)就不分方位，直接叫余脉、边缘
    private const float SameSpot = 0.3f;

    private sealed class UnitFile
    {
        public List<WorldMapWildUnit> units = new();
    }

    private static List<WorldMapWildUnit> _units;
    private static Dictionary<string, WorldMapWildUnit> _unitById = new();
    private static WorldMapWildSave _state;
    private static WorldMapFrame _frame;
    private static float _bottom, _degrees;
    // 开局还没定下法理的史实归属：单元 → 政权都城(头一回挂法理时换成都城所在的法理)
    private static Dictionary<string, City> _capitals;

    private static bool Active => _state != null && _frame != null;
    // 照原图归城的地块(见 WorldMapWildSave.kept)
    private static HashSet<int> _kept;
    // 本剧本开无人区、转成法理无人区的归城地块(见 WorldMapWildSave.own_wild、city_claims)。导入地图没有 _state，单独记
    private static bool _ownWild;
    private static Dictionary<int, long> _cityClaims;

    public static void SetOwnWild(bool on) => _ownWild = on;

    // 这些地块地形够无人区的，划走后挂这座城的法理、跟着这座城(同州府锚点，见 AnchorCity)
    public static void SetCityClaims(Dictionary<int, long> claims) => _cityClaims = claims is { Count: > 0 } ? claims : null;

    // 照原图分地的剧本开局(WorldMapScenario.Finish)：记下归城的地块
    public static void Keep(IEnumerable<int> zones)
    {
        _kept = zones == null ? null : new HashSet<int>(zones);
        if (_kept is { Count: 0 }) _kept = null;
    }

    // 这块地照原图归城(城还领着就不划成无人区)
    internal static bool Kept(int zoneId) => _kept != null && _kept.Contains(zoneId);
    internal static IEnumerable<int> KeptZones => _kept ?? (IEnumerable<int>)Array.Empty<int>();

    // 这一局是世界地图(真实地球)：开局就有文明，无人区照起真实地名(见 WildernessSystem.NameAll)
    internal static bool OnEarth => Active;

    internal static void RepairRiverSurfaces()
    {
        if (Active) EarthTerrain.RepairRiverSurfaces(_frame, new Vector2Int(_state.w, _state.h));
    }

    // 这一局是世界地图预设剧本：无人区一直开着，不看模组设置(史实归属、地名、各时代开发度靠它)。自定剧本(编号 custom/<文件夹>，手做的、
    // 从当前世界导入的)是玩家自己的图，同样照设置开关(用户 2026-11-04 选"自定剧本也听设置")
    // 自定剧本自己开了无人区的也算(见 WorldMapEra.own_wilderness)；固定版图仍只有预设剧本
    internal static bool ForcesWild => _ownWild || FixedLayout;
    internal static bool FixedLayout => Active && !(_state.era ?? "").StartsWith("custom/", StringComparison.Ordinal);

    #region 开局、存读档

    // 剧本摆好后记下州府锚点给无人区定的行政区(见 WorldMapScenario.AnchorWilderness)
    public static void SetAnchors(Dictionary<int, long> anchors, IEnumerable<int> bounded = null)
    {
        if (_state == null) return;
        _state.anchors = anchors is { Count: > 0 } ? new Dictionary<int, long>(anchors) : null;
        _state.bounded_anchors = bounded == null ? null : new HashSet<int>(bounded);
    }

    internal static bool BoundedAnchor(TileZone zone) => zone != null &&
        (_cityClaims?.ContainsKey(zone.id) == true || _state?.bounded_anchors?.Contains(zone.id) == true);
    internal static bool NeedsBoundaryRepair => Active && _state.boundary_revision < 1;
    internal static void MarkBoundaryRepaired() { if (_state != null) _state.boundary_revision = 1; }
    internal static bool NeedsMountainRepair => Active && _state.mountain_revision < 1;
    internal static void MarkMountainRepaired() { if (_state != null) _state.mountain_revision = 1; }

    // 连通修补同时更新推算锚点，防止 OriginOf 下一次查询又把修好的省界改回远处旧省。
    internal static void FollowAnchor(TileZone zone, City city)
    {
        if (zone == null || city?.data == null || city.isRekt() || BoundedAnchor(zone) || _state?.anchors == null ||
            !_state.anchors.ContainsKey(zone.id)) return;
        _state.anchors[zone.id] = city.id;
    }

    internal static void CaptureTerritory(IEnumerable<City> cities)
    {
        if (!Active || cities == null) return;
        foreach (City city in cities)
        {
            if (city?.data == null || city.isRekt() || city.zones == null) continue;
            KingdomTitle title = city.GetTitle();
            foreach (TileZone zone in city.zones)
                if (zone?.city == city) RememberTerritory(zone, title, city, false);
        }
    }

    internal static void RememberTerritory(TileZone zone, KingdomTitle title, City city, bool neutral, bool pinned = false)
    {
        if (!Active || zone == null || zone.id < 0) return;
        if (neutral)
        {
            _state.territory?.Remove(zone.id);
            return;
        }
        long cityId = city?.data != null && !city.isRekt() ? city.id : -1L;
        long titleId = title != null && !title.isRekt() ? title.id : -1L;
        if (cityId < 0 && titleId < 0) return;
        _state.territory ??= new();
        if (_state.territory.TryGetValue(zone.id, out ScenarioLandOrigin old) && old.city == cityId && old.title == titleId && old.pinned == pinned) return;
        _state.territory[zone.id] = new ScenarioLandOrigin { city = cityId, title = titleId, pinned = pinned };
    }

    internal static IEnumerable<int> KnownTerritory() => Active && WildernessSystem.Enabled && _state.territory != null
        ? _state.territory.Keys : Enumerable.Empty<int>();

    private static bool LandOrigin(TileZone zone, out City city, out KingdomTitle title)
    {
        city = null;
        title = null;
        // 有实体城市或仍属无人区时，分别由城市／无人区处理；只兜底已经退出的、有明确来源的陆地。
        if (!Active || !WildernessSystem.Enabled || zone == null || zone.city != null || zone.tiles_with_ground <= 0 || WildernessSystem.IsWild(zone) ||
            _state.territory == null || !_state.territory.TryGetValue(zone.id, out ScenarioLandOrigin land)) return false;
        city = land.city >= 0 ? World.world?.cities?.get(land.city) : null;
        if (city?.data == null || city.isRekt()) city = null;
        // 退出无人区的山谷也沿用城市现在的行政法理，不能由开局缓存把旧辖境重新画回来。
        if (!land.pinned && city != null)
        {
            KingdomTitle current = city.GetTitle();
            if (current == null || !current.isRekt()) land.title = current?.id ?? -1L;
        }
        title = land.title >= 0 ? ModClass.KINGDOM_TITLE_MANAGER?.get(land.title) : null;
        if (title != null && title.isRekt()) title = null;
        // 笔刷固定的是法理，不是后来改投其他法理的城市；行政图层也要使用固定法理下的城市。
        if (land.pinned && city?.GetTitle() != title)
            city = title?.getCities().FirstOrDefault(candidate => candidate?.data != null && !candidate.isRekt());
        return city != null || title != null;
    }

    internal static Kingdom KnownOwner(TileZone zone)
    {
        if (!LandOrigin(zone, out City city, out KingdomTitle title)) return null;
        Kingdom local = city?.kingdom;
        if (local?.data == null || local.isRekt() || local.wild || local.isNeutral()) local = null;
        Kingdom holder = title?.FindRealmTitleHolder();
        if (holder?.data == null || holder.isRekt() || holder.wild || holder.isNeutral()) holder = null;
        if (holder == null) return local;
        return local != null && (local == holder || local.GetEmpire() is Empire empire && empire == holder.GetEmpire()) ? local : holder;
    }

    internal static City KnownCity(TileZone zone)
    {
        if (!LandOrigin(zone, out City city, out _) || city == null) return null;
        Kingdom owner = KnownOwner(zone);
        return owner != null && (city.kingdom == owner || city.kingdom?.GetEmpire() is Empire empire && empire == owner.GetEmpire()) ? city : null;
    }

    internal static void CaptureAdministration(IEnumerable<City> cities)
    {
        if (!Active || cities == null) return;
        _state.administration ??= new();
        foreach (City city in cities)
        {
            if (city?.data == null || city.isRekt() || city.kingdom?.data == null || city.kingdom.isRekt()) continue;
            _state.administration[city.id] = new ScenarioAdministrativeOrigin
            {
                realm = city.kingdom.id, title = city.GetTitle()?.id ?? -1L,
                empire = city.kingdom.GetEmpire()?.id ?? -1L, regime = AdministrativeRegime(city)
            };
        }
        _state.administration_revision = 1;
    }

    internal static bool PreservesAdministration(City city)
    {
        if (!Active || city?.data == null || city.isRekt() || city.kingdom?.data == null) return false;
        // 旧档沿用当前行政安排，只防止继续漂移，不把战争后的版图恢复成开局。
        if (_state.administration_revision < 1 || _state.administration == null)
        {
            // 旧版延迟回调可能存了空表，或只补到了带无人区锚点的城市。只补缺项，
            // 不把已有记录里的真实战争／改隶重新登记成“开局未变”。
            Dictionary<long, ScenarioAdministrativeOrigin> recorded = _state.administration;
            CaptureAdministration(World.world.cities.list.Where(candidate => candidate != null &&
                (recorded == null || !recorded.ContainsKey(candidate.id))).ToList());
        }
        if (_state.administration == null || !_state.administration.TryGetValue(city.id, out ScenarioAdministrativeOrigin original)) return false;
        return original.realm == city.kingdom.id && original.title == (city.GetTitle()?.id ?? -1L) &&
               original.empire == (city.kingdom.GetEmpire()?.id ?? -1L) &&
               (string.IsNullOrEmpty(original.regime) || original.regime == AdministrativeRegime(city));
    }

    private static string AdministrativeRegime(City city) =>
        (city.kingdom.GetEmpire()?.CoreKingdom ?? city.kingdom).GetRegime()?.type.ToString() ?? "";

    internal static bool HasPreservedAdministration(Empire empire) => Active && empire?.kingdoms_list != null &&
        empire.kingdoms_list.Any(kingdom => kingdom?.cities != null && kingdom.cities.Any(PreservesAdministration));

    // 这块地按州府锚点归哪座城(没有、城没了为 null)：无人区挂它的王国法理，跟着它的行政区(见 WildernessSystem.Attach)
    internal static City AnchorCity(TileZone zone)
    {
        if (zone == null) return null;
        // 照原图归城的地块先认原来那座城
        if ((_cityClaims == null || !_cityClaims.TryGetValue(zone.id, out long id)) &&
            (_state?.anchors == null || !_state.anchors.TryGetValue(zone.id, out id))) return null;
        City city = World.world?.cities?.get(id);
        return city?.data != null && city.isAlive() ? city : null;
    }

    // 剧本摆好时(WorldMapScenario.Finish)：记下范围、大小、时代，史实归属先记都城
    public static void Begin(WorldMapFrame frame, Vector2Int size, WorldMapEra era, Dictionary<string, City> capitals,
        Dictionary<string, KingdomTitle> titles = null)
    {
        _state = new WorldMapWildSave
        {
            frame = frame?.id ?? "", w = size.x, h = size.y, era = era?.id ?? "", year = era?.year ?? 0, mountain_revision = 1
        };
        _capitals = new Dictionary<string, City>();
        if (era?.wilderness != null && capitals != null)
            foreach (KeyValuePair<string, string> pair in era.wilderness)
                if (capitals.TryGetValue(pair.Value ?? "", out City city) && city != null) _capitals[pair.Key] = city;
        if (era?.wilderness_regions != null && titles != null)
            foreach (KeyValuePair<string, string> pair in era.wilderness_regions)
            {
                if (!_capitals.TryGetValue(pair.Key, out City capital) ||
                    !titles.TryGetValue(pair.Value ?? "", out KingdomTitle title) || title == null || title.isRekt()) continue;
                // 小地图缺城或复制剧本改了国家时，不将当地法理强行交给原国家。
                if (!title.getCities().Any(city => SamePolity(city, capital))) continue;
                (_state.regional ??= new())[pair.Key] = title.id;
            }
        Bind();
        _plan = null;
        if (_state?.regional is { Count: > 0 }) WildernessSystem.RequestReattach();
    }

    public static void Load(WorldMapWildSave saved)
    {
        ResetWorldState();
        if (saved == null) return;
        saved.owners ??= new Dictionary<string, long>();
        if (saved.paint is { Count: > 0 })
        {
            _painted = new Dictionary<int, long>();
            foreach (KeyValuePair<long, List<int>> pair in saved.paint)
                foreach (int zone in pair.Value ?? new List<int>())
                    _painted[zone] = pair.Key;
        }
        if (saved.neutral is { Count: > 0 }) _neutral = new HashSet<int>(saved.neutral);
        if (saved.kept is { Count: > 0 }) _kept = new HashSet<int>(saved.kept);
        _ownWild = saved.own_wild;
        SetCityClaims(saved.city_claims);
        _state = saved;
        Bind();
        if (_painted != null || _neutral != null || _state?.regional is { Count: > 0 } || NeedsBoundaryRepair) WildernessSystem.RequestReattach();
    }

    public static WorldMapWildSave Save()
    {
        ResolvePaint();
        Dictionary<long, List<int>> paint = _painted is { Count: > 0 }
            ? _painted.GroupBy(pair => pair.Value).ToDictionary(group => group.Key, group => group.Select(pair => pair.Key).OrderBy(id => id).ToList())
            : null;
        List<int> neutral = _neutral is { Count: > 0 } ? _neutral.OrderBy(id => id).ToList() : null;
        List<int> kept = _kept is { Count: > 0 } ? _kept.OrderBy(id => id).ToList() : null;
        if (!Active)
            // 导入地图：只存刷的归属与照原图归城的地块(范围为空，读档不当地球)
            return paint == null && neutral == null && kept == null && !_ownWild && _cityClaims == null ? null
                : new WorldMapWildSave { paint = paint, neutral = neutral, kept = kept, own_wild = _ownWild, city_claims = _cityClaims };
        ResolveOwners();
        _state.paint = paint;
        _state.neutral = neutral;
        _state.kept = kept;
        _state.own_wild = _ownWild;
        _state.city_claims = _cityClaims;
        return _state;
    }

    public static void ResetWorldState()
    {
        _state = null;
        _frame = null;
        _capitals = null;
        _plan = null;
        _paintKeys = null;
        _paintCapitals = null;
        _paintTitles = null;
        _painted = null;
        _neutral = null;
        _kept = null;
        _ownWild = false;
        _cityClaims = null;
    }

    private static void Bind()
    {
        _frame = EarthTerrain.Frames.FirstOrDefault(frame => frame.id == _state?.frame);
        if (_frame == null || _state.w <= 0 || _state.h <= 0)
        {
            _state = null;
            _frame = null;
            return;
        }
        _frame.Bounds(new Vector2Int(_state.w, _state.h), out _bottom, out _degrees);
    }

    // 旧档没存长城(见 FrontierWalls)：按存档的时代、范围与大小补算这一时代的长城；不是世界地图、时代找不到为 null
    internal static List<List<Vector2>> ScenarioWalls()
    {
        if (!Active || string.IsNullOrEmpty(_state.era) || !WorldMapData.EnsureLoaded()) return null;
        WorldMapEra era = WorldMapData.Eras.Concat(WorldMapData.Custom).FirstOrDefault(item => item?.id == _state.era);
        return era == null ? null : WorldMapScenario.WallLines(era, _frame, new Vector2Int(_state.w, _state.h));
    }

    #endregion

    #region 地理单元

    private static bool EnsureUnits()
    {
        if (_units != null) return _units.Count > 0;
        _units = new List<WorldMapWildUnit>();
        string path = Path.Combine(WorldMapData.Folder, "wilderness.json");
        try
        {
            if (File.Exists(path))
                _units = JsonConvert.DeserializeObject<UnitFile>(File.ReadAllText(path))?.units ?? new List<WorldMapWildUnit>();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][世界地图] 无人区地名表读不出来 {path}: {exception.Message}");
        }
        _units.RemoveAll(unit => unit == null || string.IsNullOrEmpty(unit.id));
        foreach (WorldMapWildUnit unit in _units)
        {
            unit.kinds ??= new List<string>();
            unit.box ??= new List<float[]>();
            unit.pts ??= new List<float[]>();
            unit.alt ??= new List<string>();
            unit.cultures ??= new Dictionary<string, List<string>>();
            Measure(unit);
        }
        _unitById = _units.GroupBy(unit => unit.id).ToDictionary(group => group.Key, group => group.First());
        return _units.Count > 0;
    }

    private static void Measure(WorldMapWildUnit unit)
    {
        unit.West = unit.South = float.MaxValue;
        unit.East = unit.North = float.MinValue;
        float sumLon = 0f, sumLat = 0f, area = 0f;
        int parts = 0;
        foreach (float[] b in unit.box.Where(b => b != null && b.Length >= 4))
        {
            unit.West = Mathf.Min(unit.West, b[0]);
            unit.South = Mathf.Min(unit.South, b[1]);
            unit.East = Mathf.Max(unit.East, b[2]);
            unit.North = Mathf.Max(unit.North, b[3]);
            area += (b[2] - b[0]) * (b[3] - b[1]);
            sumLon += (b[0] + b[2]) * 0.5f;
            sumLat += (b[1] + b[3]) * 0.5f;
            parts++;
        }
        foreach (float[] p in unit.pts.Where(p => p != null && p.Length >= 2))
        {
            float pad = unit.r / Mathf.Max(0.15f, Mathf.Cos(Mathf.Min(89f, Mathf.Abs(p[1]) + unit.r) * Mathf.Deg2Rad));
            unit.West = Mathf.Min(unit.West, p[0] - pad);
            unit.East = Mathf.Max(unit.East, p[0] + pad);
            unit.South = Mathf.Min(unit.South, p[1] - unit.r);
            unit.North = Mathf.Max(unit.North, p[1] + unit.r);
            // 锚点圈经向按纬度放宽，在地图上占 πr²/cos(纬度)
            area += Mathf.PI * unit.r * unit.r / Mathf.Max(0.15f, Mathf.Cos(Mathf.Abs(p[1]) * Mathf.Deg2Rad));
            sumLon += p[0];
            sumLat += p[1];
            parts++;
        }
        unit.Area = area;
        unit.CenterLon = parts > 0 ? sumLon / parts : 0f;
        unit.CenterLat = parts > 0 ? sumLat / parts : 0f;
    }

    // 这个经纬度在不在单元的范围里
    private static bool Covers(WorldMapWildUnit unit, float lon, float lat)
    {
        if (lon < unit.West || lon > unit.East || lat < unit.South || lat > unit.North) return false;
        foreach (float[] b in unit.box)
            if (b != null && b.Length >= 4 && lon >= b[0] && lon <= b[2] && lat >= b[1] && lat <= b[3]) return true;
        float squeeze = Mathf.Cos(lat * Mathf.Deg2Rad), r2 = unit.r * unit.r;
        foreach (float[] p in unit.pts)
        {
            if (p == null || p.Length < 2) continue;
            float dx = (lon - p[0]) * squeeze, dy = lat - p[1];
            if (dx * dx + dy * dy <= r2) return true;
        }
        return false;
    }

    // 离单元范围几度(在范围里为 0)
    private static float DistanceTo(WorldMapWildUnit unit, float lon, float lat)
    {
        float squeeze = Mathf.Cos(lat * Mathf.Deg2Rad), best = float.MaxValue;
        foreach (float[] b in unit.box)
        {
            if (b == null || b.Length < 4) continue;
            float dx = Mathf.Max(0f, Mathf.Max(b[0] - lon, lon - b[2])) * squeeze, dy = Mathf.Max(0f, Mathf.Max(b[1] - lat, lat - b[3]));
            best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dy * dy));
        }
        foreach (float[] p in unit.pts)
        {
            if (p == null || p.Length < 2) continue;
            float dx = (lon - p[0]) * squeeze, dy = lat - p[1];
            best = Mathf.Min(best, Mathf.Max(0f, Mathf.Sqrt(dx * dx + dy * dy) - unit.r));
        }
        return best;
    }

    private static string KindKey(WildKind kind) => kind switch
    {
        WildKind.Mountain => "mountain",
        WildKind.Desert => "desert",
        WildKind.Tundra => "tundra",
        WildKind.Jungle => "jungle",
        WildKind.Swamp => "swamp",
        // 地球地图没有岩浆，wilderness.json 不写 lava；导入地图、从当前世界导入的剧本也把岩浆当山(见 ScenarioWorldExport.TypeOf)，
        // 这里只为对得上类型，进世界后神力放的岩浆划出来的熔岩照常按文化起名
        WildKind.Lava => "lava",
        // 高原(2026-11-10)：wilderness.json 里青藏高原、羌塘、可可西里、帕米尔的 k 带 plateau
        WildKind.Plateau => "plateau",
        _ => null
    };

    // 史实归属的范围边上弯弯曲曲(用户 10-30 "上面吐出来两块真的很丑""按照史实")：按经纬框判时取样点按低频噪声挪一挪
    // (至多一块半地，几块地一个起伏)，归属照史实，只是边不再沿经纬线切成一块块方的
    private const float JitterZones = 1.5f;

    private static void Jitter(TileZone zone, ref float lon, ref float lat)
    {
        float reach = JitterZones * 8f * _degrees;
        lon += (SmoothBorders.BorderNoise(zone.x * 4, zone.y * 4, 7717) - 0.5f) * 2f * reach;
        lat += (SmoothBorders.BorderNoise(zone.x * 4, zone.y * 4, 8831) - 0.5f) * 2f * reach;
    }

    private static void LonLat(TileZone zone, out float lon, out float lat)
    {
        WorldTile tile = zone.centerTile;
        float x = tile != null ? tile.x : zone.x * 8 + 4, y = tile != null ? tile.y : zone.y * 8 + 4;
        lon = _frame.west + (x + 0.5f) * _degrees;
        lat = _bottom + (y + 0.5f) * _degrees;
    }

    #endregion

    #region 起名

    private sealed class Match
    {
        public WorldMapWildUnit unit;
        public int covered, zones, id;
        public float distance, lon, lat;
    }

    private sealed class Plan
    {
        public object list;
        public int version;
        public readonly Dictionary<int, Match> byRegion = new();
        // 各单元盖得最多的那片
        public readonly Dictionary<WorldMapWildUnit, Match> main = new();
    }

    private static Plan _plan;

    // 区域 → 单元的对应：按区域列表与版本缓存(一次划区里起名的几片共用)
    private static Plan PlanFor()
    {
        IReadOnlyList<WildRegion> regions = WildernessSystem.Regions;
        if (_plan != null && ReferenceEquals(_plan.list, regions) && _plan.version == WildernessSystem.Version) return _plan;
        var plan = new Plan { list = regions, version = WildernessSystem.Version };
        _plan = plan;
        List<TileZone> all = World.world?.zone_calculator?.zones;
        if (all == null || !EnsureUnits()) return plan;
        var zoneById = new Dictionary<int, TileZone>(all.Count);
        foreach (TileZone zone in all)
            if (zone != null) zoneById[zone.id] = zone;
        var points = new List<Vector2>();
        // 一块地在地图上占几平方度(单元范围折成块数用)
        float zoneArea = Mathf.Max(1e-6f, 64f * _degrees * _degrees);
        foreach (WildRegion region in regions)
        {
            string kind = KindKey(region.Kind);
            if (kind == null) continue;
            List<WorldMapWildUnit> candidates = _units.Where(unit => unit.kinds.Contains(kind)).ToList();
            if (candidates.Count == 0) continue;
            points.Clear();
            float sumLon = 0f, sumLat = 0f;
            foreach (int id in region.Zones)
            {
                if (!zoneById.TryGetValue(id, out TileZone zone)) continue;
                LonLat(zone, out float lon, out float lat);
                points.Add(new Vector2(lon, lat));
                sumLon += lon;
                sumLat += lat;
            }
            if (points.Count == 0) continue;
            float centerLon = sumLon / points.Count, centerLat = sumLat / points.Count;
            var counts = new int[candidates.Count];
            foreach (Vector2 point in points)
                for (int i = 0; i < candidates.Count; i++)
                    if (Covers(candidates[i], point.x, point.y)) counts[i]++;
            WorldMapWildUnit best = null;
            int bestCount = 0;
            float bestScore = 0f, bestDistance = float.MaxValue;
            // 有单元盖住区域过半的，只在这些里挑
            bool major = counts.Any(count => count >= points.Count * MajorShare);
            for (int i = 0; i < candidates.Count; i++)
            {
                if (counts[i] <= 0 || major && counts[i] < points.Count * MajorShare) continue;
                // 重合度：盖住的块数 / (区域块数 + 单元范围折成的块数 - 盖住的块数)
                float score = counts[i] / Mathf.Max(1f, points.Count + candidates[i].Area / zoneArea - counts[i]);
                float distance = DistanceTo(candidates[i], centerLon, centerLat);
                if (best != null && (score < bestScore || score == bestScore &&
                        (candidates[i].Area > best.Area || candidates[i].Area == best.Area && distance >= bestDistance))) continue;
                best = candidates[i];
                bestCount = counts[i];
                bestScore = score;
                bestDistance = distance;
            }
            // 一块都没盖住：取 SnapDegrees 以内最近的(大区域隔几块取一块量，免得太慢)
            if (best == null)
            {
                int stride = Math.Max(1, points.Count / 64);
                foreach (WorldMapWildUnit unit in candidates)
                {
                    float distance = float.MaxValue;
                    for (int i = 0; i < points.Count; i += stride) distance = Mathf.Min(distance, DistanceTo(unit, points[i].x, points[i].y));
                    if (distance > SnapDegrees || distance >= bestDistance) continue;
                    best = unit;
                    bestDistance = distance;
                }
                if (best == null) continue;
            }
            var match = new Match
            {
                unit = best, covered = bestCount, distance = bestCount > 0 ? 0f : bestDistance, zones = region.Zones.Count,
                id = region.Id, lon = centerLon, lat = centerLat
            };
            plan.byRegion[region.Id] = match;
            if (!plan.main.TryGetValue(best, out Match main) || Better(match, main)) plan.main[best] = match;
        }
        return plan;
    }

    private static bool Better(Match a, Match b)
    {
        if (a.covered != b.covered) return a.covered > b.covered;
        if (a.distance != b.distance) return a.distance < b.distance;
        if (a.zones != b.zones) return a.zones > b.zones;
        return a.id < b.id;
    }

    // 这片无人区的真实地名(没有对上的为 null，照旧走 WildernessNaming)；used 是本世界已用的名字
    public static string Name(WildRegion region, ICollection<string> used)
    {
        if (!Active || region == null) return null;
        try
        {
            Plan plan = PlanFor();
            if (!plan.byRegion.TryGetValue(region.Id, out Match match)) return null;
            List<string> names = Names(match.unit, region.Culture);
            if (names.Count == 0) return null;
            bool Free(string name) => !string.IsNullOrWhiteSpace(name) && (used == null || !used.Contains(name));
            plan.main.TryGetValue(match.unit, out Match main);
            if (main == match)
                foreach (string name in names)
                    if (Free(name)) return name;
            // 拆出来的一片：对着叫原名的那片定方位(自己就是盖得最多的那片、原名却被占了的，对着占了原名的那片；都没有对着单元中心)
            string first = names[0];
            float refLon = match.unit.CenterLon, refLat = match.unit.CenterLat;
            if (main != null && main != match)
            {
                refLon = main.lon;
                refLat = main.lat;
            }
            else
            {
                WildRegion holder = WildernessSystem.Regions.FirstOrDefault(other => other != region && other.Name == first);
                if (holder != null && plan.byRegion.TryGetValue(holder.Id, out Match held))
                {
                    refLon = held.lon;
                    refLat = held.lat;
                }
            }
            bool range = region.Kind == WildKind.Mountain;
            string direction = Direction(match.lon - refLon, match.lat - refLat, match.lat);
            if (direction != null)
            {
                string directed = string.Format(LM.Get($"wild_real_{direction}_{(range ? "range" : "part")}"), first);
                if (Free(directed)) return directed;
            }
            string rest = string.Format(LM.Get(range ? "wild_real_spur" : "wild_real_fringe"), first);
            return Free(rest) ? rest : null;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][世界地图] 无人区真实地名起名失败({region.Kind}): {exception.Message}");
            return null;
        }
    }

    // 东西南北：经向按纬度折算后取差得多的那个方向；离得太近不分
    private static string Direction(float dLon, float dLat, float lat)
    {
        float dx = dLon * Mathf.Cos(lat * Mathf.Deg2Rad);
        if (Mathf.Abs(dx) < SameSpot && Mathf.Abs(dLat) < SameSpot) return null;
        if (Mathf.Abs(dx) >= Mathf.Abs(dLat)) return dx > 0f ? "east" : "west";
        return dLat > 0f ? "north" : "south";
    }

    // 候选名字，先后：起名文化的叫法 → 开局年份的旧称 → 现代名
    private static List<string> Names(WorldMapWildUnit unit, string culture)
    {
        var names = new List<string>();
        int year = _state.year;
        void Add(string name)
        {
            if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name)) names.Add(name);
        }
        if (!string.IsNullOrEmpty(culture))
            foreach (KeyValuePair<string, List<string>> pair in unit.cultures)
            {
                if (pair.Value == null || !(pair.Key ?? "").Split(',').Any(key => key.Trim() == culture)) continue;
                foreach (string entry in pair.Value) Add(Entry(entry, year));
            }
        foreach (string entry in unit.alt) Add(Entry(entry, year));
        Add(WorldMapData.Pick(unit.zh, unit.tw, unit.en));
        return names;
    }

    // "简体|英文|繁体" 或 "年份|简体|英文|繁体"(年份："到某年为止"、"某年~"、"某年~某年")；不在年份里的为 null
    private static string Entry(string entry, int year)
    {
        if (string.IsNullOrEmpty(entry)) return null;
        string[] parts = entry.Split('|');
        if (parts.Length == 3) return WorldMapData.Pick(parts[0], parts[2], parts[1]);
        if (parts.Length < 4 || !InYears(parts[0].Trim(), year)) return null;
        return WorldMapData.Pick(parts[1], parts[3], parts[2]);
    }

    private static bool InYears(string spec, int year)
    {
        if (spec.Length == 0) return true;
        int cut = spec.IndexOf('~');
        if (cut < 0) return int.TryParse(spec, out int until) && year <= until;
        string from = spec.Substring(0, cut).Trim(), to = spec.Substring(cut + 1).Trim();
        if (from.Length > 0 && (!int.TryParse(from, out int start) || year < start)) return false;
        return to.Length == 0 || int.TryParse(to, out int end) && year <= end;
    }

    #endregion

    #region 剧本刷的归属

    // 开局还没换成法理的：地块编号 → 刷的键("p:国家"、"r:地区"、"n")，国家 → 都城，地区 → 王国法理
    private static Dictionary<int, string> _paintKeys;
    private static Dictionary<string, City> _paintCapitals;
    private static Dictionary<string, KingdomTitle> _paintTitles;
    // 换好的：地块编号 → 王国法理编号；刷了永久中立、还没设上的地块
    private static Dictionary<int, long> _painted;
    private static HashSet<int> _neutral;

    // 剧本摆好时(WorldMapScenario.Finish，导入地图也调)：各地块中心落在哪种刷的格子里(同 PaintTerritory 按 0.2° 一格找)；
    // capitals 为剧本国家 → 都城，titles 为地区 → 王国法理
    public static void BeginPaint(WorldMapEra era, WorldMapFrame frame, Vector2Int size, Dictionary<string, City> capitals,
        Dictionary<string, KingdomTitle> titles)
    {
        _paintKeys = null;
        _paintCapitals = null;
        _paintTitles = null;
        _painted = null;
        _neutral = null;
        Dictionary<int, string> cells = ScenarioCells.ToMap(era?.paint);
        List<TileZone> zones = World.world?.zone_calculator?.zones;
        if (cells.Count == 0 || frame == null || zones == null || size.x <= 0 || size.y <= 0) return;
        frame.Bounds(size, out float bottom, out float degrees);
        var keys = new Dictionary<int, string>();
        foreach (TileZone zone in zones)
        {
            WorldTile tile = zone?.centerTile;
            if (tile == null || zone.id < 0) continue;
            float lon = frame.west + (tile.x + 0.5f) * degrees, lat = bottom + (tile.y + 0.5f) * degrees;
            if (!ScenarioBrush.ZoneValue(cells, lon, lat, degrees * 8f, ScenarioCells.LandStep, ScenarioBrush.NotExact, out string key)) continue;
            if (key == "n" || key.Length > 2 && key[1] == ':' && key[0] is 'p' or 'r') keys[zone.id] = key;
        }
        if (keys.Count == 0) return;
        _paintKeys = keys;
        _paintCapitals = capitals ?? new Dictionary<string, City>();
        _paintTitles = titles ?? new Dictionary<string, KingdomTitle>();
        WildernessSystem.RequestReattach();
        LogService.LogInfo($"[EmpireCraft][世界地图] 剧本刷的无人区归属：{keys.Count} 块地");
    }

    // 刷的键换成法理编号(头一回挂法理、存档时)：p: 取那国都城此刻的王国法理(同史实归属，见 ResolveOwners)，r: 取那块法理
    private static void ResolvePaint()
    {
        if (_paintKeys == null) return;
        _painted = new Dictionary<int, long>();
        _neutral = new HashSet<int>();
        foreach (KeyValuePair<int, string> pair in _paintKeys)
        {
            if (pair.Value == "n")
            {
                _neutral.Add(pair.Key);
                continue;
            }
            string id = pair.Value.Substring(2);
            KingdomTitle title = pair.Value[0] == 'p'
                ? _paintCapitals.TryGetValue(id, out City city) && city?.data != null && !city.isRekt() ? city.GetTitle() : null
                : _paintTitles.TryGetValue(id, out KingdomTitle found) ? found : null;
            if (title != null && !title.isRekt()) _painted[pair.Key] = title.id;
        }
        _paintKeys = null;
        _paintCapitals = null;
        _paintTitles = null;
        if (_neutral.Count == 0) _neutral = null;
        if (_painted.Count == 0) _painted = null;
    }

    // 这块无人区按剧本刷的挂哪个法理(没刷、法理没了为 null)
    internal static KingdomTitle PaintedTitle(TileZone zone)
    {
        ResolvePaint();
        if (zone == null) return null;
        if (_painted == null || !_painted.TryGetValue(zone.id, out long id)) return null;
        KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER?.get(id);
        return title == null || title.isRekt() ? null : title;
    }

    // 只在无人区已经划好后应用中立笔刷；查询法理不能提前消费尚未落地的中立设置。
    internal static void PreparePaint()
    {
        ResolvePaint();
        if (WildernessSystem.Regions.Count > 0) ApplyNeutral();
    }

    // 刷了永久中立的：盖到的那几片设为永久中立区(头一回划区挂法理时，这时各片已划好)，设过就清掉
    private static void ApplyNeutral()
    {
        if (_neutral == null) return;
        HashSet<int> zones = _neutral;
        _neutral = null;
        int count = WildernessSystem.SetNeutralZones(zones, true);
        if (count > 0) LogService.LogInfo($"[EmpireCraft][世界地图] 剧本刷的永久中立区：{count} 块");
    }

    #endregion

    #region 史实归属

    // 剧本编辑器预测无人区用(见 ScenarioWilderness)：这个经纬度上这一类无人区，按时代文件的史实归属(单元 → 政权)归哪个政权；
    // 落在几个写了归属的单元里取范围最小的(同 TitleFor)；没写为 null
    public static string HistoricalOwner(Dictionary<string, string> owners, WildKind kind, float lon, float lat)
        => HistoricalOwner(owners, kind, lon, lat, out _);

    internal static string HistoricalOwner(Dictionary<string, string> owners, WildKind kind, float lon, float lat, out string unitId)
    {
        unitId = null;
        if (owners == null || owners.Count == 0 || !EnsureUnits()) return null;
        string key = KindKey(kind);
        if (key == null) return null;
        WorldMapWildUnit best = null;
        string chosen = null;
        foreach (KeyValuePair<string, string> pair in owners)
        {
            if (string.IsNullOrEmpty(pair.Value) || !_unitById.TryGetValue(pair.Key ?? "", out WorldMapWildUnit unit) || !unit.kinds.Contains(key) ||
                !Covers(unit, lon, lat) || best != null && unit.Area >= best.Area) continue;
            best = unit;
            chosen = pair.Value;
            unitId = pair.Key;
        }
        return chosen;
    }

    // 开局头一回挂法理时：各单元的政权都城 → 都城所在的王国法理
    private static void ResolveOwners()
    {
        if (_capitals == null || _state == null) return;
        foreach (KeyValuePair<string, City> pair in _capitals)
        {
            City city = pair.Value;
            if (city?.data == null || city.isRekt()) continue;
            KingdomTitle title = city.GetTitle();
            if (title != null && !title.isRekt()) _state.owners[pair.Key] = title.id;
        }
        _capitals = null;
        if (_state.owners.Count > 0) LogService.LogInfo($"[EmpireCraft][世界地图] 无人区史实归属：{_state.owners.Count} 个地理单元按 {_state.era} 挂法理");
    }

    // 这块无人区挂哪个法理(WildernessSystem.Attach 里问)：剧本刷的先(导入地图也认)，再是史实归属；都没有、法理没了的为 null，
    // 照旧挂最近的。永久中立由挂接阶段的 PreparePaint 应用，查询不改世界。
    public static KingdomTitle TitleFor(TileZone zone)
    {
        if (zone == null) return null;
        KingdomTitle painted = PaintedTitle(zone);
        if (painted != null) return painted;
        return HistoricalTitle(zone, false);
    }

    // 明确的地区设置与编辑器笔刷高于自动州府锚点；普通国家级设置仍走原来的本地城市分配。
    internal static KingdomTitle FixedTitle(TileZone zone) => PaintedTitle(zone) ??
        (_state?.regional is { Count: > 0 } ? HistoricalTitle(zone, true) : null);

    private static bool SamePolity(City city, City capital)
    {
        Kingdom a = city?.kingdom, b = capital?.kingdom;
        if (city?.data == null || capital?.data == null || a == null || b == null || a.isRekt() || b.isRekt() || city.isRekt()) return false;
        return a == b || a.GetEmpire() is Empire empire && empire == b.GetEmpire();
    }

    private static KingdomTitle HistoricalTitle(TileZone zone, bool fixedOnly)
    {
        if (zone == null) return null;
        if (!Active) return null;
        ResolveOwners();
        if (_state.owners.Count == 0 || !EnsureUnits()) return null;
        string kind = KindKey(WildernessSystem.KindOf(zone));
        if (kind == null) return null;
        LonLat(zone, out float lon, out float lat);
        Jitter(zone, ref lon, ref lat);
        WorldMapWildUnit best = null;
        KingdomTitle chosen = null;
        foreach (KeyValuePair<string, long> pair in _state.owners)
        {
            if (!_unitById.TryGetValue(pair.Key, out WorldMapWildUnit unit) || !unit.kinds.Contains(kind) || !Covers(unit, lon, lat)) continue;
            if (best != null && unit.Area >= best.Area) continue;
            KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER?.get(pair.Value);
            if (title == null || title.isRekt())
            {
                // 首都法理消失后，仍存续的明确地方辖境不跟着失效。
                title = _state.regional != null && _state.regional.TryGetValue(pair.Key, out long local)
                    ? ModClass.KINGDOM_TITLE_MANAGER?.get(local) : null;
                if (title == null || title.isRekt()) continue;
            }
            best = unit;
            chosen = title;
        }
        if (best != null && _state.regional != null && _state.regional.TryGetValue(best.id, out long id))
        {
            KingdomTitle regional = ModClass.KINGDOM_TITLE_MANAGER?.get(id);
            if (regional != null && !regional.isRekt()) return regional;
        }
        return fixedOnly ? null : chosen;
    }

    #endregion
}

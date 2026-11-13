using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems.WorldMap;

// 剧本编辑器的数据一侧(第二、三期，见 Docs/plan-scenario-editor.md、ScenarioEditorScreen)：把一个自定剧本读进来编，
// 城、国家、法理、关系、长城关隘的增删改与开局归属的预览都在这里，保存写回 scenario.json(另出一张缩略图 preview.png)。
//   · 城与法理：用剧本自带的城表；还用着内置城表的剧本头一回增删、挪动、改名城或改法理时，把内置城表(连同地区、通用帝国法理、
//     天下名)整套拷进剧本；城的大小档只记在剧本的 ranks 里，不必拷城表；
//   · 王国法理就是城表里的地区(城归哪个地区，地区的法理名、文化、首都)；帝国法理是剧本的 cores(收哪些地区、首都、所属天下)；
//     天下是 tianxia(标识 → 名)；城可兼属另两个地区的王国法理(share)；
//   · 归属预览照开局的规矩(WorldMapScenario.Plan)：先看各国点名的城与都城(写在前面的国家优先)，再看各国认领的地区
//     (同一地区几国都认领的归最近的都城，这里按直线近似)，谁都不认领的独立；
//   · 撤销：每一步改动前存一份快照(至多 UndoLimit 份)；拖着划城、划法理是一笔，只存一份；
//   · 改名一律三种写法一起改(简体、繁体、英文同名)，城的旧称随之清掉；
//   · 行政区(用户 10-30 "都做"：编辑器里加行政区工具)：国家的 divisions(名、城、治所)，开局称帝的国家进世界后按它划省
//     (见 WorldMapScenario.Divide、AnchorTerritory)；都城留在京畿直辖，划不进行政区；一座城只在一个行政区里，
//     城换了国家、立为都城、删掉时从行政区里去掉；
//   · 刷的开发度(develop，0.2° 一格，"0"~"100")同刷的地块、地形一样放字典里改，见 WorldMapDevelopment、ScenarioWilderness。
public static class ScenarioEditing
{
    public static WorldMapEra Era { get; private set; }
    public static string Folder { get; private set; } = "";
    public static bool Dirty { get; private set; }
    // 改一次加一(编辑器据此重画)
    public static int Revision { get; private set; }

    public static List<WorldMapCity> Cities => Era?.OwnWorld == true ? Era.cities : WorldMapData.BuiltinCities;
    public static List<WorldMapRegion> Regions => Era?.OwnWorld == true ? Era.regions ?? new List<WorldMapRegion>() : WorldMapData.BuiltinRegions;

    private static Dictionary<string, WorldMapPolity> _owners = new();
    private static int _ownersRevision = -1;

    #region 打开、新建、保存

    // 从剧本文件夹重读一份来编(没保存的改动不影响列表里的那份)
    public static bool Open(WorldMapEra custom)
    {
        if (custom == null || !custom.Custom || !WorldMapData.EnsureLoaded()) return false;
        try
        {
            WorldMapEra era = JsonConvert.DeserializeObject<WorldMapEra>(File.ReadAllText(Path.Combine(custom.folder, WorldMapData.CustomFile)));
            if (era == null) return false;
            era.folder = custom.folder;
            era.id = custom.id;
            Normalize(era);
            // 导入时的原图归属：单放，不进撤销快照(快照一步一份，几万格)，存盘时才写回
            _landBase = ScenarioCells.ToMap(era.paint_base ?? (era.exact ? era.paint : null));
            era.paint_base = null;
            Era = era;
            Folder = custom.folder;
            Dirty = false;
            _stroke = _strokeSaved = false;
            Undos.Clear();
            LoadCells();
            Revision++;
            return true;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][剧本编辑] 读不出剧本 {custom.folder}: {exception.Message}");
            return false;
        }
    }

    private static void Normalize(WorldMapEra era)
    {
        era.polities ??= new List<WorldMapPolity>();
        era.relations ??= new List<WorldMapRelation>();
        era.ranks ??= new Dictionary<string, int>();
        era.cores ??= new List<WorldMapCore>();
        era.walls ??= new List<WorldMapWall>();
        era.passes ??= new List<WorldMapPass>();
        era.events ??= new List<WorldMapEvent>();
        era.culture ??= new Dictionary<string, string>();
        era.wilderness_regions ??= new Dictionary<string, string>();
        // 自带城表的剧本(手写的可能没写地区)：下面增删改都直接用这张表
        if (era.OwnWorld) era.regions ??= new List<WorldMapRegion>();
    }

    // 空白剧本：地球地图上一座城也没有，留着内置的地区与帝国法理(放城时按所在地取文化)，开局年份 1 年，记下地图范围；返回文件夹名
    public static string CreateBlank(string frame = null)
    {
        if (!WorldMapData.EnsureLoaded()) return null;
        try
        {
            Directory.CreateDirectory(WorldMapData.CustomFolder);
            string name = "blank";
            for (int i = 2; Directory.Exists(Path.Combine(WorldMapData.CustomFolder, name)); i++) name = "blank_" + i;
            string folder = Path.Combine(WorldMapData.CustomFolder, name);
            Directory.CreateDirectory(folder);
            string title = LM.Get("scenario_editor_blank_name");
            var era = new WorldMapEra
            {
                id = name, zh = title, tw = title, en = title, year = 1, focus = "east", order = 100, frame = frame, own_cities = true,
                cities = new List<WorldMapCity>(), regions = Clone(WorldMapData.BuiltinRegions),
                tianxia = new Dictionary<string, string>(WorldMapData.BuiltinTianxia), cores = Clone(WorldMapData.BuiltinCores)
            };
            File.WriteAllText(Path.Combine(folder, WorldMapData.CustomFile), Serialize(era, true));
            return name;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][剧本编辑] 新建空白剧本失败: {exception.Message}");
            return null;
        }
    }

    public static bool Save(Color32[] thumbnail = null, int thumbWidth = 0, int thumbHeight = 0)
    {
        if (Era == null || string.IsNullOrEmpty(Folder)) return false;
        try
        {
            FlushCells();
            Era.paint_base = Era.exact && !Era.base_stale && _landBase.Count > 0 ? ScenarioCells.FromMap(_landBase) : null;
            string json = Serialize(Era, true);
            Era.paint_base = null;
            File.WriteAllText(Path.Combine(Folder, WorldMapData.CustomFile), json);
            if (thumbnail != null && thumbWidth > 0 && thumbHeight > 0)
            {
                var texture = new Texture2D(thumbWidth, thumbHeight, TextureFormat.RGBA32, false);
                texture.SetPixels32(thumbnail);
                texture.Apply();
                File.WriteAllBytes(Path.Combine(Folder, "preview.png"), ImageConversion.EncodeToPNG(texture));
                UnityEngine.Object.Destroy(texture);
            }
            Dirty = false;
            WorldMapData.LoadCustom();
            return true;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][剧本编辑] 保存失败 {Folder}: {exception.Message}");
            return false;
        }
    }

    public static void Close()
    {
        Era = null;
        Folder = "";
        Dirty = false;
        _stroke = _strokeSaved = false;
        Undos.Clear();
        _owners.Clear();
        _ownersRevision = -1;
        _landCells = new Dictionary<int, string>();
        _landBase = new Dictionary<int, string>();
        _terrainCells = new Dictionary<int, string>();
        _developCells = new Dictionary<int, string>();
        _cellsDirty = false;
    }

    // 只写字段(不写 Name、East 这类算出来的属性)，空的不写
    private sealed class FieldsOnly : DefaultContractResolver
    {
        protected override List<MemberInfo> GetSerializableMembers(Type objectType) =>
            base.GetSerializableMembers(objectType).Where(member => member.MemberType == MemberTypes.Field).ToList();
    }

    private static readonly JsonSerializerSettings Pretty = new()
    {
        ContractResolver = new FieldsOnly(), NullValueHandling = NullValueHandling.Ignore, Formatting = Formatting.Indented
    };

    private static readonly JsonSerializerSettings Compact = new()
    {
        ContractResolver = new FieldsOnly(), NullValueHandling = NullValueHandling.Ignore, Formatting = Formatting.None
    };

    private static string Serialize(WorldMapEra era, bool pretty) => JsonConvert.SerializeObject(era, pretty ? Pretty : Compact);

    private static T Clone<T>(T value) => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value, Compact));

    private static void Changed()
    {
        Dirty = true;
        Revision++;
    }

    // 刷地块、地形、开发度以外的改动才变(国家、城、行政区)：预览里慢的那几步据此留缓存，拖着刷时不重算
    private static int _paintEdits;
    public static int StructureRevision => Revision - _paintEdits;

    #endregion

    #region 撤销

    private const int UndoLimit = 30;
    private static readonly LinkedList<string> Undos = new();
    private static bool _stroke, _strokeSaved;

    public static bool CanUndo => Undos.Count > 0;

    // 一笔(拖着划城、划法理)：只在头一下存快照
    public static void BeginStroke()
    {
        _stroke = true;
        _strokeSaved = false;
    }

    public static void EndStroke() => _stroke = false;

    private static void Before()
    {
        if (Era == null) return;
        if (_stroke)
        {
            if (_strokeSaved) return;
            _strokeSaved = true;
        }
        FlushCells();
        Undos.AddLast(Serialize(Era, false));
        while (Undos.Count > UndoLimit) Undos.RemoveFirst();
    }

    public static bool Undo()
    {
        if (Era == null || Undos.Count == 0) return false;
        string json = Undos.Last.Value;
        Undos.RemoveLast();
        WorldMapEra era = JsonConvert.DeserializeObject<WorldMapEra>(json);
        if (era == null) return false;
        era.folder = Era.folder;
        era.id = Era.id;
        Normalize(era);
        Era = era;
        _ownersRevision = -1;
        LoadCells();
        Changed();
        return true;
    }

    #endregion

    #region 剧本本身

    public static void SetName(string name)
    {
        if (Era == null || string.IsNullOrWhiteSpace(name) || name.Trim() == Era.Name) return;
        Before();
        Era.zh = Era.tw = Era.en = name.Trim();
        Changed();
    }

    public static void SetDescription(string text)
    {
        if (Era == null || (text ?? "").Trim() == Era.Description) return;
        Before();
        Era.dzh = Era.dtw = Era.den = (text ?? "").Trim();
        Changed();
    }

    public static void SetAuthor(string author)
    {
        if (Era == null || (author ?? "").Trim() == (Era.author ?? "")) return;
        Before();
        Era.author = (author ?? "").Trim();
        Changed();
    }

    // 地图大小(格，按 64 格取整；清空回到开局时选大中小)：只填了一边，另一边取现在预演的大小
    public const int MinMapChunks = 4, MaxMapChunks = 48;

    public static void SetMapSide(bool southNorth, string text)
    {
        if (Era == null) return;
        string value = (text ?? "").Trim();
        int at = southNorth ? 1 : 0;
        if (value.Length == 0)
        {
            if (!Era.HasSize) return;
            Before();
            Era.size = null;
            Changed();
            return;
        }
        if (!int.TryParse(value, out int tiles) || tiles <= 0) return;
        int chunks = ScenarioPreviewRules.ChunksOf(tiles, 64, MinMapChunks, MaxMapChunks);
        Vector2Int current = Era.HasSize ? Era.FixedSize : ScenarioWilderness.Size;
        if (Era.HasSize && Era.size[at] == chunks) return;
        Before();
        Era.size = new[] { Mathf.Clamp(current.x, MinMapChunks, MaxMapChunks), Mathf.Clamp(current.y, MinMapChunks, MaxMapChunks) };
        Era.size[at] = chunks;
        // 大小定死了范围也得定：一格多少度是范围除以大小，开局再选别的范围就对不上了
        if (Era.map == null && string.IsNullOrEmpty(Era.frame)) Era.frame = ScenarioWilderness.Frame?.id;
        Changed();
    }

    // 照原图分地：只按刷的地块分地(刷过归城的地块才有这一说)
    public static bool CanExact => Era?.exact == true || _landCells.Values.Any(value => value.StartsWith("c:", StringComparison.Ordinal));

    public static void ToggleExact()
    {
        if (Era == null || !CanExact) return;
        Before();
        Era.exact = !Era.exact;
        if (Era.exact && (_landBase.Count == 0 || Era.base_stale))
        {
            Era.base_stale = false;
            _landBase = _landCells.Where(pair => pair.Value.StartsWith("c:", StringComparison.Ordinal)).ToDictionary(pair => pair.Key, pair => pair.Value);
        }
        LandRevision++;
        Changed();
    }

    public static void SetYear(string text)
    {
        if (Era == null || !int.TryParse((text ?? "").Trim(), out int year) || year == 0 || year == Era.year) return;
        Before();
        Era.year = year;
        Changed();
    }

    #endregion

    #region 城

    public static WorldMapCity City(string id) => string.IsNullOrEmpty(id) ? null : Cities.FirstOrDefault(city => city.id == id);

    public static int RankOf(WorldMapCity city) => Era?.RankOf(city) ?? city?.rank ?? 2;

    public static bool Active(WorldMapCity city) => city != null && Era != null && city.ActiveIn(Era.year);

    public static string CityName(WorldMapCity city) => city == null ? "" : Era == null ? city.zh : city.NameIn(Era.year);

    // 头一回改城或法理：内置城表整套拷进剧本(地区、城、天下名；通用帝国法理并进剧本自己的 cores)
    private static void EnsureOwnWorld()
    {
        if (Era == null || Era.OwnWorld) return;
        Era.regions = Clone(WorldMapData.BuiltinRegions);
        Era.cities = Clone(WorldMapData.BuiltinCities);
        Era.tianxia = new Dictionary<string, string>(WorldMapData.BuiltinTianxia);
        var ids = new HashSet<string>(Era.cores.Select(core => core.id));
        Era.cores.AddRange(Clone(WorldMapData.BuiltinCores).Where(core => !ids.Contains(core.id)));
        _ownersRevision = -1;
    }

    // 在经纬度处立一座城：地区取最近那座城的地区；返回城编号。
    // culture 给了(用户 2026-11-12 转群友"剧本编辑器放城市怎么放其他文化的，目前貌似只有华夏的"——导入的图、空白剧本里头一座城
    // 立的是华夏的地区，后面的城都跟着它)：城的文化就是它所在王国法理的文化，所以所在的王国法理不是这种文化时，几度内有这种文化的
    // 城就跟它的王国法理，没有就给这座城另立一个(名取城名、城作法理首都)。
    // species 给了(用户 2026-11-14 转群友"这玩意能够在放置城市之前就设置吗……放进去之后才慢慢改的话要改个猴年马月")：新城直接单设这个种族
    // ownRegion(用户 2026-11-14 转群友"可以放城市的时候原地生成一个王国法理，而不是立马并入旁边的王国法理")：新城自成一个王国法理
    // (名取城名、城作法理首都，文化照选的、没选照本来要并入的那块)；本来要并入的那块在哪个帝国法理里，新法理也留在那个帝国法理里，
    // 省得放一座就掉出帝国法理一座。因文化不同另立的照旧不进帝国法理
    public static string AddCity(float lon, float lat, string culture = null, string species = null, bool ownRegion = false)
    {
        if (Era == null) return null;
        Before();
        EnsureOwnWorld();
        WorldMapCity near = Nearest(lon, lat, null);
        string region = RegionForNewCity(near, lon, lat);
        bool wanted = CultureService.IsValidCulture(culture), own = false;
        if (wanted && !string.IsNullOrEmpty(region) && RegionCulture(Region(region)) != culture)
        {
            WorldMapCity kin = Nearest(lon, lat, city => RegionCulture(Region(city.region)) == culture);
            float dx = kin == null ? 0f : (kin.lon - lon) * Mathf.Cos(lat * Mathf.Deg2Rad), dy = kin == null ? 0f : kin.lat - lat;
            if (kin != null && dx * dx + dy * dy <= NearCityDegrees * NearCityDegrees) region = kin.region;
            else own = true;
        }
        string core = null;
        if (ownRegion && !own)
        {
            if (!wanted) culture = RegionCulture(Region(region));
            if (!CultureService.IsValidCulture(culture)) culture = "Huaxia";
            core = CoreOfRegion(region)?.id;
            own = true;
        }
        if (string.IsNullOrEmpty(region) && !own)
        {
            var created = new WorldMapRegion { id = "r1", culture = wanted ? culture : "Huaxia" };
            created.zh = created.tw = created.en = LM.Get("scenario_editor_new_region");
            Era.regions ??= new List<WorldMapRegion>();
            Era.regions.Add(created);
            region = created.id;
        }
        int n = Era.cities.Count + 1;
        string id = "c" + n;
        while (Era.cities.Any(city => city.id == id)) id = "c" + ++n;
        // 按所在地区的文化起名，起不出来才叫"新城 N"(见 ScenarioNaming)
        string name = string.Format(LM.Get("scenario_editor_new_city"), n);
        string zh = name, tw = name, en = name;
        if (ScenarioNaming.TryCity(own ? culture : CultureOfRegion(region), used => Era.cities.Any(city => city.zh == used),
                out string cityZh, out string cityTw, out string cityEn))
        {
            zh = cityZh;
            tw = cityTw;
            en = cityEn;
        }
        if (own)
        {
            Era.regions ??= new List<WorldMapRegion>();
            int r = Era.regions.Count + 1;
            region = "r" + r;
            while (Era.regions.Any(entry => entry.id == region)) region = "r" + ++r;
            Era.regions.Add(new WorldMapRegion { id = region, zh = zh, tw = tw, en = en, title = WorldMapData.Pick(zh, tw, en), capital = id, culture = culture });
            if (Core(core) is WorldMapCore keep) (keep.regions ??= new List<string>()).Add(region);
        }
        Era.cities.Add(new WorldMapCity { id = id, zh = zh, tw = tw, en = en, lon = lon, lat = lat, region = region, rank = 2 });
        if (!string.IsNullOrEmpty(species)) (Era.species ??= new Dictionary<string, string>())[id] = species;
        Changed();
        return id;
    }

    // 新城的地区：几度内有城跟它；再远(空白剧本放下第一座城、跨到另一片大陆)按内置城表看这里当年属哪个地区，文化才对得上
    private const float NearCityDegrees = 6f;

    private static string RegionForNewCity(WorldMapCity near, float lon, float lat)
    {
        if (near != null)
        {
            float dx = (near.lon - lon) * Mathf.Cos(lat * Mathf.Deg2Rad), dy = near.lat - lat;
            if (dx * dx + dy * dy <= NearCityDegrees * NearCityDegrees) return near.region;
        }
        if (Era?.map == null)
        {
            WorldMapCity builtin = null;
            float best = float.MaxValue;
            foreach (WorldMapCity city in WorldMapData.BuiltinCities)
            {
                float dx = (city.lon - lon) * Mathf.Cos(lat * Mathf.Deg2Rad), dy = city.lat - lat, distance = dx * dx + dy * dy;
                if (distance >= best) continue;
                best = distance;
                builtin = city;
            }
            if (builtin != null && Region(builtin.region) != null) return builtin.region;
        }
        return near?.region ?? Regions.FirstOrDefault()?.id ?? "";
    }

    private static string CultureOfRegion(string region) => Regions.FirstOrDefault(entry => entry.id == region)?.culture;

    // 这座城的文化(它所在王国法理的文化)
    public static string CityCulture(string cityId) => RegionCulture(Region(City(cityId)?.region));

    // 改这座城的文化：王国法理里只有它一座城就改这个王国法理的文化；还有别的城就先以它另立一个王国法理(见 NewRegion)再改，
    // 别的城不跟着变。算一步撤销
    public static void SetCityCulture(string cityId, string culture)
    {
        WorldMapCity city = City(cityId);
        if (city == null || Region(city.region) == null || !CultureService.IsValidCulture(culture) || CityCulture(cityId) == culture) return;
        bool outer = _stroke;
        if (!outer) BeginStroke();
        try
        {
            if (RegionCityCount(city.region) > 1) NewRegion(cityId);
            else
            {
                Before();
                EnsureOwnWorld();
            }
            WorldMapRegion region = Region(City(cityId)?.region);
            if (region == null) return;
            region.culture = culture;
            Era.culture.Remove(region.id);
            Changed();
        }
        finally
        {
            if (!outer) EndStroke();
        }
    }

    public static void CycleCityCulture(string cityId, int step) =>
        SetCityCulture(cityId, Step(CultureService.CultureKeys.ToList(), CityCulture(cityId), step));

    public static void MoveCity(string id, float lon, float lat)
    {
        if (City(id) == null) return;
        Before();
        EnsureOwnWorld();
        WorldMapCity city = City(id);
        city.lon = lon;
        city.lat = lat;
        Changed();
    }

    public static void RenameCity(string id, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || City(id) == null || CityName(City(id)) == name.Trim()) return;
        Before();
        EnsureOwnWorld();
        WorldMapCity city = City(id);
        city.zh = city.tw = city.en = name.Trim();
        city.alt?.Clear();
        Changed();
    }

    // 城的大小档(1 大城、2 中城、3 小城；小图只放 1、2 档)：记在剧本的 ranks 里
    public static void CycleRank(string id)
    {
        WorldMapCity city = City(id);
        if (city == null || Era == null) return;
        Before();
        int next = RankOf(city) % 3 + 1;
        Era.ranks[id] = next;
        Changed();
    }

    // 删城：连同各国点名的城、法理首都、事件里点名这座城的项一并去掉；是都城的，本国另一座最大的城接着当都城。返回提示
    public static string DeleteCity(string id)
    {
        if (City(id) == null) return null;
        Before();
        var heirs = new Dictionary<WorldMapPolity, string>();
        foreach (WorldMapPolity polity in Era.polities.Where(polity => polity.capital == id))
        {
            WorldMapCity heir = Cities.Where(city => city.id != id && Active(city) && OwnerOf(city) == polity).OrderBy(RankOf).FirstOrDefault();
            if (heir != null) heirs[polity] = heir.id;
        }
        EnsureOwnWorld();
        if (Era.cities.RemoveAll(city => city.id == id) == 0) return null;
        // 照原图归这座城的地块(见 ScenarioWorldExport)一并去掉
        DropLandKey("c:" + id);
        foreach (WorldMapPolity polity in Era.polities)
        {
            polity.cities?.Remove(id);
            if (polity.capital == id) polity.capital = heirs.TryGetValue(polity, out string heir) ? heir : "";
            LeaveDivisions(polity, id);
        }
        foreach (WorldMapRegion region in Era.regions)
            if (region.capital == id) region.capital = "";
        foreach (WorldMapCore core in Era.cores)
            if (core.capital == id) core.capital = "";
        Era.ranks.Remove(id);
        Era.species?.Remove(id);
        Era.independent?.Remove(id);
        int forgotten = ForgetInEvents(city: id);
        Changed();
        return LM.Get("scenario_editor_city_deleted") + EventsNote(forgotten);
    }

    // 清空全部城(连同国家、关系)：地图上一座城也不留，城表不再回落到内置的。地区、帝国法理留着，放新城时还能接着用
    public static string ClearAllCities()
    {
        if (Era == null) return null;
        Before();
        EnsureOwnWorld();
        Era.cities.Clear();
        Era.own_cities = true;
        Era.exact = false;
        Era.base_stale = true;
        Era.independent = null;
        Era.ranks.Clear();
        Era.species = null;
        Era.polities.Clear();
        Era.relations.Clear();
        foreach (WorldMapRegion region in Era.regions ?? new List<WorldMapRegion>()) region.capital = "";
        foreach (WorldMapCore core in Era.cores) core.capital = "";
        foreach (WorldMapEvent item in Era.events)
            foreach (WorldMapEventStep step in Steps(item))
            {
                step.a = step.b = step.city = "";
                step.cities = null;
            }
        foreach (int cell in _landCells.Where(pair => pair.Value[0] is 'c' or 'p').Select(pair => pair.Key).ToList()) _landCells.Remove(cell);
        _cellsDirty = true;
        LandRevision++;
        _ownersRevision = -1;
        Changed();
        return LM.Get("scenario_editor_cities_cleared");
    }

    public static WorldMapCity Nearest(float lon, float lat, Func<WorldMapCity, bool> filter)
    {
        WorldMapCity best = null;
        float bestDistance = float.MaxValue;
        foreach (WorldMapCity city in Cities)
        {
            if (filter != null && !filter(city)) continue;
            float dx = (city.lon - lon) * Mathf.Cos(lat * Mathf.Deg2Rad), dy = city.lat - lat;
            float distance = dx * dx + dy * dy;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = city;
        }
        return best;
    }

    #endregion

    #region 国家

    public static WorldMapPolity Polity(string id) =>
        string.IsNullOrEmpty(id) ? null : Era?.polities.FirstOrDefault(polity => polity.id == id);

    // 开局时这座城归哪国(没有的独立，返回 null)
    public static WorldMapPolity OwnerOf(WorldMapCity city)
    {
        if (city == null || Era == null) return null;
        if (_ownersRevision != Revision) ComputeOwners();
        return _owners.TryGetValue(city.id, out WorldMapPolity owner) ? owner : null;
    }

    public static int CityCount(WorldMapPolity polity)
    {
        if (polity == null) return 0;
        if (_ownersRevision != Revision) ComputeOwners();
        return _owners.Values.Count(owner => owner == polity);
    }

    private static void ComputeOwners()
    {
        _owners = new Dictionary<string, WorldMapPolity>();
        _ownersRevision = Revision;
        var byId = Cities.Where(Active).GroupBy(city => city.id).ToDictionary(group => group.Key, group => group.First());
        foreach (WorldMapPolity polity in Era.polities)
            foreach (string id in (polity.cities ?? new List<string>()).Append(polity.capital))
                if (!string.IsNullOrEmpty(id) && byId.ContainsKey(id) && !_owners.ContainsKey(id))
                    _owners[id] = polity;
        foreach (WorldMapCity city in byId.Values)
        {
            if (_owners.ContainsKey(city.id)) continue;
            WorldMapPolity best = null;
            float bestDistance = float.MaxValue;
            foreach (WorldMapPolity polity in Era.polities)
            {
                if (polity.regions == null || !polity.regions.Contains(city.region) || Era.independent?.Contains(city.id) == true ||
                    !byId.TryGetValue(polity.capital ?? "", out WorldMapCity seat)) continue;
                float dx = city.lon - seat.lon, dy = city.lat - seat.lat, distance = dx * dx + dy * dy;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = polity;
            }
            if (best != null) _owners[city.id] = best;
        }
    }

    // 某国的都城(另一国的都城不能划走：先给那国另立都城)
    private static WorldMapPolity CapitalHolder(string cityId, WorldMapPolity except) =>
        Era.polities.FirstOrDefault(polity => polity != except && polity.capital == cityId);

    // 把城划给某国(写进那国点名的城，别国点名的去掉)；返回提示，划不了时说原因
    public static string Assign(string cityId, WorldMapPolity polity)
    {
        WorldMapCity city = City(cityId);
        if (city == null || polity == null || Era == null) return null;
        if (CapitalHolder(cityId, polity) is WorldMapPolity holder)
            return string.Format(LM.Get("scenario_editor_capital_locked"), CityName(city), holder.Name);
        Before();
        foreach (WorldMapPolity other in Era.polities)
        {
            other.cities?.Remove(cityId);
            // 换了国家：原来那国的行政区里去掉
            if (other != polity) LeaveDivisions(other, cityId);
        }
        polity.cities ??= new List<string>();
        polity.cities.Add(cityId);
        Era.independent?.Remove(cityId);
        Changed();
        return string.Format(LM.Get("scenario_editor_assigned"), CityName(city), polity.Name);
    }

    // 让城独立：不再归任何国家(所在地区被哪国认领也不归它，记进 independent)；是都城的先给那国另立都城
    public static string MakeIndependent(string cityId)
    {
        WorldMapCity city = City(cityId);
        if (city == null || Era == null) return null;
        if (CapitalHolder(cityId, null) is WorldMapPolity holder)
            return string.Format(LM.Get("scenario_editor_capital_locked"), CityName(city), holder.Name);
        if (OwnerOf(city) == null) return string.Format(LM.Get("scenario_editor_already_independent"), CityName(city));
        Before();
        foreach (WorldMapPolity other in Era.polities)
        {
            other.cities?.Remove(cityId);
            LeaveDivisions(other, cityId);
        }
        if (Era.polities.Any(polity => polity.regions?.Contains(city.region) == true))
        {
            Era.independent ??= new List<string>();
            if (!Era.independent.Contains(cityId)) Era.independent.Add(cityId);
        }
        Changed();
        return string.Format(LM.Get("scenario_editor_released"), CityName(city));
    }

    // 认领王国法理：里面没被别国点名的城都归这国(同地区几国认领的归最近的都城)；再点一次不再认领
    public static string ToggleClaim(WorldMapPolity polity, string regionId)
    {
        WorldMapRegion region = Region(regionId);
        if (polity == null || region == null || Era == null) return null;
        Before();
        polity.regions ??= new List<string>();
        bool had = polity.regions.Remove(regionId);
        if (!had) polity.regions.Add(regionId);
        Changed();
        return string.Format(LM.Get(had ? "scenario_editor_claim_removed" : "scenario_editor_claim_added"), polity.Name, region.TitleName);
    }

    // 改城的地区不该连带改国家：认领按地区算，城一换地区就可能翻给别国或独立，写明归原来那国(原来独立的记成独立)。
    // 调用处先记 before，改完地区再调
    private static void KeepOwner(WorldMapCity city, WorldMapPolity before)
    {
        _ownersRevision = -1;
        WorldMapPolity after = OwnerOf(city);
        if (after != before)
        {
            if (before != null) (before.cities ??= new List<string>()).Add(city.id);
            else (Era.independent ??= new List<string>()).Add(city.id);
        }
        _ownersRevision = -1;
    }

    // 以这座城为都城立一个新国家(文化取城所在地区的)
    public static WorldMapPolity NewPolity(string cityId, out string message)
    {
        message = null;
        WorldMapCity city = City(cityId);
        if (city == null || Era == null) return null;
        if (CapitalHolder(cityId, null) is WorldMapPolity holder)
        {
            message = string.Format(LM.Get("scenario_editor_capital_locked"), CityName(city), holder.Name);
            return null;
        }
        Before();
        foreach (WorldMapPolity other in Era.polities) other.cities?.Remove(cityId);
        Era.independent?.Remove(cityId);
        int n = Era.polities.Count + 1;
        string id = "p" + n;
        while (Era.polities.Any(polity => polity.id == id)) id = "p" + ++n;
        string name = CityName(city);
        string culture = Era.RegionCulture(Region(city.region));
        var polity = new WorldMapPolity
        {
            id = id, zh = name, tw = name, en = name, capital = cityId,
            culture = CultureService.IsValidCulture(culture) ? culture : "Huaxia"
        };
        Era.polities.Add(polity);
        Changed();
        message = string.Format(LM.Get("scenario_editor_polity_created"), polity.Name);
        return polity;
    }

    public static string DeletePolity(WorldMapPolity polity)
    {
        if (polity == null || Era == null || !Era.polities.Contains(polity)) return null;
        Before();
        Era.polities.RemoveAll(item => item.id == polity.id);
        Era.relations.RemoveAll(relation => relation.a == polity.id || relation.b == polity.id);
        DropLandKey("p:" + polity.id);
        foreach (WorldMapPolity other in Era.polities)
            if (other.overlord == polity.id) other.overlord = "";
        int forgotten = ForgetInEvents(polity: polity.id);
        Changed();
        return LM.Get("scenario_editor_realm_deleted") + EventsNote(forgotten);
    }

    // 立某城为某国都城(先划进来)
    public static string SetCapital(WorldMapPolity polity, string cityId)
    {
        WorldMapCity city = City(cityId);
        if (polity == null || city == null) return null;
        if (CapitalHolder(cityId, polity) is WorldMapPolity holder)
            return string.Format(LM.Get("scenario_editor_capital_locked"), CityName(city), holder.Name);
        Before();
        foreach (WorldMapPolity other in Era.polities) other.cities?.Remove(cityId);
        Era.independent?.Remove(cityId);
        polity.cities ??= new List<string>();
        if (!string.IsNullOrEmpty(polity.capital) && polity.capital != cityId && !polity.cities.Contains(polity.capital))
            polity.cities.Add(polity.capital);
        polity.capital = cityId;
        // 都城留在京畿直辖
        foreach (WorldMapPolity other in Era.polities) LeaveDivisions(other, cityId);
        Changed();
        return string.Format(LM.Get("scenario_editor_capital_set"), polity.Name, CityName(city));
    }

    public static void RenamePolity(WorldMapPolity polity, string name)
    {
        if (polity == null || string.IsNullOrWhiteSpace(name) || polity.Name == name.Trim()) return;
        Before();
        polity.zh = polity.tw = polity.en = name.Trim();
        Changed();
    }

    // 文化：在本模组各文化里前后换
    public static void CycleCulture(WorldMapPolity polity, int step)
    {
        if (polity == null) return;
        Before();
        polity.culture = Step(CultureService.CultureKeys.ToList(), polity.culture, step);
        Changed();
    }

    // 种族(原版物种；用户 2026-11-13 转群友"剧本编辑器里可以自己设置王国、城市是什么种族，而不是锁死文化")：国家、城、部落圈各可单设，
    // 空着的照旧——城随国家，国家照文化(见 WorldMapScenario.RulerSpecies、CitySpecies)。只管长相与原版天性，起名、政体、制度仍照文化，
    // 不配套的照设的来。可选的是装着的文明物种，原版四族排前头
    private static readonly string[] MainSpecies = { "human", "orc", "elf", "dwarf" };
    private static List<string> _speciesKeys;

    public static List<string> SpeciesKeys
    {
        get
        {
            if (_speciesKeys is { Count: > 0 }) return _speciesKeys;
            List<string> civ = EmpireCraft.Scripts.Data.ConfigData.AllCivSpecies.Where(asset => !string.IsNullOrEmpty(asset?.id))
                .Select(asset => asset.id).Distinct().ToList();
            _speciesKeys = MainSpecies.Where(civ.Contains).Concat(civ.Where(id => !MainSpecies.Contains(id))).ToList();
            return _speciesKeys;
        }
    }

    // 空 = 不单设(随国家/随文化)，再是各物种
    private static string StepSpecies(string current, int step) =>
        Step(new List<string> { "" }.Concat(SpeciesKeys).ToList(), current ?? "", step);

    public static string SpeciesName(string species) =>
        string.IsNullOrEmpty(species) ? "" :
        AssetManager.actor_library.has(species) ? AssetManager.actor_library.get(species).getLocalizedName() : species;

    // 这座城单设的物种(没设为空)
    public static string OwnSpecies(WorldMapCity city) =>
        city != null && Era?.species != null && Era.species.TryGetValue(city.id, out string species) ? species ?? "" : "";

    public static void CycleCitySpecies(string cityId, int step)
    {
        WorldMapCity city = City(cityId);
        if (city == null || Era == null) return;
        Before();
        string next = StepSpecies(OwnSpecies(city), step);
        if (string.IsNullOrEmpty(next)) Era.species?.Remove(city.id);
        else (Era.species ??= new Dictionary<string, string>())[city.id] = next;
        Changed();
    }

    public static void CycleSpecies(WorldMapPolity polity, int step)
    {
        if (polity == null) return;
        Before();
        string next = StepSpecies(polity.species, step);
        polity.species = string.IsNullOrEmpty(next) ? null : next;
        Changed();
    }

    public static void CycleTribesSpecies(int index, int step)
    {
        WorldMapTribes tribes = TribesOf(index);
        if (tribes == null) return;
        Before();
        string next = StepSpecies(tribes.species, step);
        tribes.species = string.IsNullOrEmpty(next) ? null : next;
        Changed();
    }

    // 国家没单设时开局君主用的物种：照本国文化(没写文化的照都城的)
    public static string AutoRealmSpecies(WorldMapPolity polity)
    {
        if (polity == null) return "";
        string culture = CultureService.IsValidCulture(polity.culture) ? polity.culture : RegionCulture(Region(City(polity.capital)?.region));
        return WorldMapScenario.SpeciesFor(culture);
    }

    // 国家开局实际用的物种(单设的没装就照文化)
    public static string RealmSpecies(WorldMapPolity polity) => WorldMapScenario.KnownSpecies(polity?.species) ?? AutoRealmSpecies(polity);

    // 城没单设时开局用的物种：所在国家单设了的随国家(fromRealm)；都城随君主，照国家的；别的城照所在法理的文化
    public static string AutoCitySpecies(WorldMapCity city, out bool fromRealm)
    {
        fromRealm = false;
        if (city == null) return "";
        WorldMapPolity owner = OwnerOf(city);
        string realm = WorldMapScenario.KnownSpecies(owner?.species);
        if (realm != null)
        {
            fromRealm = true;
            return realm;
        }
        return owner != null && owner.capital == city.id ? AutoRealmSpecies(owner) : WorldMapScenario.SpeciesFor(RegionCulture(Region(city.region)));
    }

    public static void ToggleEmpire(WorldMapPolity polity)
    {
        if (polity == null) return;
        Before();
        polity.empire = !polity.empire;
        Changed();
    }

    // 君主：姓、名、年龄(空的照本国文化起、年龄随机)
    public static void SetRuler(WorldMapPolity polity, string family, string given, string age)
    {
        if (polity == null) return;
        // 框里显示的是当前语言那一段，存的是三种写法：没改就不动(以前点一下框再离开，英文、繁体写法就丢了)
        string fam = ScenarioPreviewRules.KeepIfSame(polity.ruler?.fam, family, WorldMapData.PickParts);
        string giv = ScenarioPreviewRules.KeepIfSame(polity.ruler?.giv, given, WorldMapData.PickParts);
        int old = polity.ruler?.age ?? -1;
        int years = age == null ? old : int.TryParse(age.Trim(), out int value) && value > 0 ? value : -1;
        if (old <= 0 && years <= 0) years = old;
        if (polity.ruler == null ? fam == null && giv == null && years <= 0 : polity.ruler.fam == fam && polity.ruler.giv == giv && polity.ruler.age == years) return;
        Before();
        polity.ruler ??= new WorldMapPerson();
        polity.ruler.fam = fam;
        polity.ruler.giv = giv;
        polity.ruler.age = years;
        Changed();
    }

    #endregion

    #region 王朝积弊

    // 开局国祚、王朝积弊、六弊(群友"要不要能让玩家可以自定义积弊度""这个在剧本编辑里面能设置比较好一些")：写进国家的 decline
    // (同内置时代文件，开局称帝的国家进世界后照它定，见 WorldMapScenario.ApplyDecline)。空着的照默认(DeclineDefault)：
    // 国祚照前史、没挂前史为新开国，积弊照国祚给同岁数最少该有的，六弊补到积弊托着的底。积弊、六弊 0~100，国祚 0~1000 年；
    // 框里填不成数的不动
    public const string DeclineAge = "age", DeclineStrain = "strain";
    public static readonly string[] DeclineKeys = new[] { DeclineAge, DeclineStrain }.Concat(MaladySystem.All).ToArray();

    public static string DeclineText(WorldMapPolity polity, string key) =>
        polity?.decline != null && polity.decline.TryGetValue(key, out float value) ? DeclineFormat(key, value) : "";

    public static string DeclineFormat(string key, float value) =>
        value.ToString(key == DeclineAge ? "0" : "0.#", CultureInfo.InvariantCulture);

    // 没写时开局会给的值：国祚照前史(不看写了的国祚)；积弊照国祚(写了的国祚算数)；六弊照积弊(写了的积弊算数)
    public static float DeclineDefault(WorldMapPolity polity, string key) => key switch
    {
        DeclineAge => WorldMapScenario.HistoryAge(polity, Era),
        DeclineStrain => DynasticCycleRules.AgeFloor(WorldMapScenario.StartingAge(polity, Era)),
        _ => MaladySystem.FloorOf(WorldMapScenario.StartingStrain(polity, Era))
    };

    // 清回默认：国祚、积弊、六弊都不写
    public static string ClearDecline(WorldMapPolity polity)
    {
        if (polity?.decline == null || polity.decline.Count == 0) return null;
        Before();
        polity.decline.Clear();
        Changed();
        return LM.Get("scenario_editor_decline_cleared");
    }

    public static void SetDecline(WorldMapPolity polity, string key, string text)
    {
        if (polity == null || Array.IndexOf(DeclineKeys, key) < 0) return;
        string trimmed = (text ?? "").Trim();
        float? value = null;
        if (trimmed.Length > 0)
        {
            if (!float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out float parsed) || float.IsNaN(parsed) || float.IsInfinity(parsed)) return;
            value = key == DeclineAge ? Mathf.Clamp(Mathf.Round(parsed), 0f, 1000f) : Mathf.Clamp(parsed, 0f, 100f);
        }
        float? old = polity.decline != null && polity.decline.TryGetValue(key, out float had) ? had : (float?)null;
        if (old == value) return;
        Before();
        polity.decline ??= new Dictionary<string, float>();
        if (value.HasValue) polity.decline[key] = value.Value;
        else polity.decline.Remove(key);
        Changed();
    }

    #endregion

    #region 行政区

    // 行政区(开局称帝的国家进世界后按它划省，见 WorldMapScenario.Divide；不称帝的写了也不生效)：名为"简体|英文|繁体"，
    // 城为本国的城(都城不算，留在京畿直辖)，治所为当年州府治所的经纬度(开局按它们划各省的地界，不建城，见 AnchorTerritory)；
    // index 为 -1 指京畿(都城直辖那一块，治所记在国家的 seats 上)
    public static List<WorldMapDivision> Divisions(WorldMapPolity polity) => polity == null ? new List<WorldMapDivision>()
        : polity.divisions ??= new List<WorldMapDivision>();

    public static WorldMapDivision Division(WorldMapPolity polity, int index) =>
        polity?.divisions != null && index >= 0 && index < polity.divisions.Count ? polity.divisions[index] : null;

    // 城在这国的第几个行政区(都城、不在任何行政区为 -1)
    public static int DivisionOf(WorldMapPolity polity, string cityId)
    {
        if (polity?.divisions == null || string.IsNullOrEmpty(cityId) || polity.capital == cityId) return -1;
        for (int d = 0; d < polity.divisions.Count; d++)
            if (polity.divisions[d]?.cities?.Contains(cityId) == true) return d;
        return -1;
    }

    // 某国某行政区(或京畿)的治所
    public static List<float[]> SeatsOf(WorldMapPolity polity, int index) => index < 0 ? polity?.seats : Division(polity, index)?.seats;

    public static string DivisionName(WorldMapPolity polity, int index) =>
        index < 0 ? string.Format(LM.Get("scenario_editor_division_capital_area"), polity?.Name ?? "")
        : Division(polity, index) is WorldMapDivision division && !string.IsNullOrEmpty(division.Name) ? division.Name : "?";

    // 新立一个行政区(名"新行政区 N")；返回它的下标
    public static int NewDivision(WorldMapPolity polity)
    {
        if (polity == null || Era == null) return -1;
        Before();
        List<WorldMapDivision> divisions = Divisions(polity);
        string name = string.Format(LM.Get("scenario_editor_new_division"), divisions.Count + 1);
        divisions.Add(new WorldMapDivision { name = Parts(name), cities = new List<string>() });
        Changed();
        return divisions.Count - 1;
    }

    public static void RenameDivision(WorldMapPolity polity, int index, string name)
    {
        WorldMapDivision division = Division(polity, index);
        string text = (name ?? "").Replace("|", "").Trim();
        if (division == null || text.Length == 0 || division.Name == text) return;
        Before();
        division.name = Parts(text);
        Changed();
    }

    // 三种写法同名("简体|英文|繁体"，同 WorldMapData.PickParts)
    private static string Parts(string name) => name + "|" + name + "|" + name;

    public static void DeleteDivision(WorldMapPolity polity, int index)
    {
        if (Division(polity, index) == null) return;
        Before();
        polity.divisions.RemoveAt(index);
        Changed();
    }

    // 把城划进这国的某行政区(index 为 -1 是放回京畿)；返回提示，划不了时说原因
    public static string AssignDivision(WorldMapPolity polity, int index, string cityId)
    {
        WorldMapCity city = City(cityId);
        if (polity == null || city == null || Era == null) return null;
        if (OwnerOf(city) != polity) return string.Format(LM.Get("scenario_editor_division_not_own"), CityName(city), polity.Name);
        if (polity.capital == cityId) return string.Format(LM.Get("scenario_editor_division_capital"), CityName(city));
        if (index >= 0 && Division(polity, index) == null) return null;
        if (DivisionOf(polity, cityId) == index) return null;
        Before();
        foreach (WorldMapPolity other in Era.polities) LeaveDivisions(other, cityId);
        if (index >= 0)
        {
            WorldMapDivision division = polity.divisions[index];
            division.cities ??= new List<string>();
            division.cities.Add(cityId);
        }
        Changed();
        return string.Format(LM.Get("scenario_editor_division_assigned"), CityName(city), DivisionName(polity, index));
    }

    // 城从它所在的行政区出来(回京畿)
    public static string RemoveFromDivision(string cityId)
    {
        WorldMapCity city = City(cityId);
        if (city == null || Era == null) return null;
        if (!Era.polities.Any(polity => polity.divisions?.Any(division => division?.cities?.Contains(cityId) == true) == true))
            return LM.Get("scenario_editor_division_not_in");
        Before();
        foreach (WorldMapPolity polity in Era.polities) LeaveDivisions(polity, cityId);
        Changed();
        return string.Format(LM.Get("scenario_editor_division_removed"), CityName(city));
    }

    // 从这国各行政区里去掉这座城(调用处已存过快照)
    private static void LeaveDivisions(WorldMapPolity polity, string cityId)
    {
        if (polity?.divisions == null) return;
        foreach (WorldMapDivision division in polity.divisions) division?.cities?.Remove(cityId);
    }

    // 治所：加一点(经纬度取两位小数)
    public static void AddSeat(WorldMapPolity polity, int index, float lon, float lat)
    {
        if (polity == null || index >= 0 && Division(polity, index) == null) return;
        Before();
        var point = new[] { Mathf.Round(lon * 100f) / 100f, Mathf.Round(lat * 100f) / 100f };
        if (index < 0) (polity.seats ??= new List<float[]>()).Add(point);
        else (polity.divisions[index].seats ??= new List<float[]>()).Add(point);
        Changed();
    }

    // 去掉离经纬度最近(reach 度以内)的那个治所；去掉了返回 true
    public static bool RemoveSeat(WorldMapPolity polity, int index, float lon, float lat, float reach)
    {
        List<float[]> seats = SeatsOf(polity, index);
        if (seats == null) return false;
        int found = -1;
        float best = reach * reach;
        for (int i = 0; i < seats.Count; i++)
        {
            if (seats[i] == null || seats[i].Length < 2) continue;
            float dx = seats[i][0] - lon, dy = seats[i][1] - lat, distance = dx * dx + dy * dy;
            if (distance > best) continue;
            best = distance;
            found = i;
        }
        if (found < 0) return false;
        Before();
        seats = SeatsOf(polity, index);
        seats.RemoveAt(found);
        if (seats.Count == 0)
        {
            if (index < 0) polity.seats = null;
            else polity.divisions[index].seats = null;
        }
        Changed();
        return true;
    }

    public static void ClearSeats(WorldMapPolity polity, int index)
    {
        if (!(SeatsOf(polity, index)?.Count > 0)) return;
        Before();
        if (index < 0) polity.seats = null;
        else polity.divisions[index].seats = null;
        Changed();
    }

    #endregion

    #region 王国法理(地区)

    public static WorldMapRegion Region(string id) => string.IsNullOrEmpty(id) ? null : Regions.FirstOrDefault(region => region.id == id);

    public static string RegionCulture(WorldMapRegion region) => Era?.RegionCulture(region) ?? region?.culture ?? "";

    public static int RegionCityCount(string id) => Cities.Count(city => Active(city) && city.region == id);

    // 以选中的城另立一个王国法理(名、法理名都取城名，文化同原地区)，城划进去、作法理首都
    public static string NewRegion(string cityId)
    {
        WorldMapCity city = City(cityId);
        if (city == null) return null;
        WorldMapPolity owner = OwnerOf(city);
        Before();
        EnsureOwnWorld();
        city = City(cityId);
        WorldMapRegion old = Region(city.region);
        int n = Era.regions.Count + 1;
        string id = "r" + n;
        while (Era.regions.Any(region => region.id == id)) id = "r" + ++n;
        string name = CityName(city);
        string culture = RegionCulture(old);
        var region = new WorldMapRegion
        {
            id = id, zh = name, tw = name, en = name, title = name, capital = cityId,
            culture = CultureService.IsValidCulture(culture) ? culture : "Huaxia"
        };
        Era.regions.Add(region);
        city.region = id;
        city.share?.Remove(id);
        KeepOwner(city, owner);
        Changed();
        return id;
    }

    // 把城划进某王国法理；返回提示(本来就在里面返回 null)
    public static string AssignRegion(string cityId, string regionId)
    {
        WorldMapCity city = City(cityId);
        WorldMapRegion region = Region(regionId);
        if (city == null || region == null || city.region == regionId) return null;
        WorldMapPolity owner = OwnerOf(city);
        Before();
        EnsureOwnWorld();
        city = City(cityId);
        foreach (WorldMapRegion other in Era.regions)
            if (other.capital == cityId) other.capital = "";
        city.region = regionId;
        city.share?.Remove(regionId);
        KeepOwner(city, owner);
        Changed();
        return string.Format(LM.Get("scenario_editor_region_assigned"), CityName(city), Region(regionId).TitleName);
    }

    public static void RenameRegion(string id, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Region(id) == null || Region(id).Name == name.Trim()) return;
        Before();
        EnsureOwnWorld();
        WorldMapRegion region = Region(id);
        region.zh = region.tw = region.en = name.Trim();
        Changed();
    }

    // 法理名(华夏一般是单字国名：雍、蜀……)
    public static void SetRegionTitle(string id, string title)
    {
        if (Region(id) == null || (title ?? "").Trim() == Region(id).TitleName) return;
        Before();
        EnsureOwnWorld();
        Region(id).title = (title ?? "").Trim();
        Changed();
    }

    // 地区的文化：前后换(剧本里按时代改写的那一项也去掉，免得盖过它)
    public static void CycleRegionCulture(string id, int step)
    {
        if (Region(id) == null) return;
        Before();
        EnsureOwnWorld();
        WorldMapRegion region = Region(id);
        region.culture = Step(CultureService.CultureKeys.ToList(), RegionCulture(region), step);
        Era.culture.Remove(id);
        Changed();
    }

    // 开局法理名(见 WorldMapScenario.BuildTitles、OwnTitles)：法理首都是不称帝国家的都城时，法理名用国名(国名照主法理定)，返回那国，否则 null；
    // others 为都城也在这块法理里、开局另立一块叫国名的法理的国家
    public static WorldMapPolity RegionNamedAfter(WorldMapRegion region, out List<WorldMapPolity> others)
    {
        others = new List<WorldMapPolity>();
        if (region == null || Era == null) return null;
        List<WorldMapCity> members = Cities.Where(city => Active(city) && city.region == region.id).ToList();
        WorldMapCity seat = members.FirstOrDefault(city => city.id == region.capital) ??
                            members.Where(city => OwnerOf(city)?.capital == city.id).OrderBy(RankOf).FirstOrDefault() ??
                            members.OrderBy(RankOf).FirstOrDefault();
        WorldMapPolity owner = OwnerOf(seat);
        WorldMapPolity named = owner != null && !owner.empire && owner.capital == seat?.id ? owner : null;
        foreach (WorldMapCity city in members)
        {
            WorldMapPolity polity = OwnerOf(city);
            if (polity != null && polity != named && !polity.empire && polity.capital == city.id && !others.Contains(polity)) others.Add(polity);
        }
        return named;
    }

    // 立城为王国法理首都(城得在这个法理里)
    public static string SetRegionCapital(string regionId, string cityId)
    {
        WorldMapCity city = City(cityId);
        if (city == null || Region(regionId) == null) return null;
        if (city.region != regionId) return string.Format(LM.Get("scenario_editor_not_in_region"), CityName(city), Region(regionId).TitleName);
        Before();
        EnsureOwnWorld();
        Region(regionId).capital = cityId;
        Changed();
        return string.Format(LM.Get("scenario_editor_region_capital_set"), Region(regionId).TitleName, CityName(city));
    }

    public static string RegionCapitalName(WorldMapRegion region)
    {
        if (region == null) return "";
        WorldMapCity chosen = City(region.capital);
        if (chosen != null && chosen.region == region.id) return CityName(chosen);
        WorldMapCity largest = Cities.Where(city => Active(city) && city.region == region.id).OrderBy(RankOf).FirstOrDefault();
        return largest == null ? "" : string.Format(LM.Get("scenario_editor_auto_capital"), CityName(largest));
    }

    // 城兼属某王国法理(至多两个，见 DeJureShareSystem)：再点一次去掉
    public static string ToggleShare(string cityId, string regionId)
    {
        WorldMapCity city = City(cityId);
        WorldMapRegion region = Region(regionId);
        if (city == null || region == null) return null;
        if (city.region == regionId) return LM.Get("scenario_editor_share_own");
        bool has = city.share?.Contains(regionId) == true;
        if (!has && city.share?.Count >= 2) return LM.Get("scenario_editor_share_full");
        Before();
        EnsureOwnWorld();
        city = City(cityId);
        city.share ??= new List<string>();
        if (has) city.share.Remove(regionId);
        else city.share.Add(regionId);
        Changed();
        return string.Format(LM.Get(has ? "scenario_editor_share_removed" : "scenario_editor_share_added"), CityName(city), region.TitleName);
    }

    // 删王国法理：里面没城了才删(连同各国认领、帝国法理收的、城兼属的一并去掉)
    public static string DeleteRegion(string id)
    {
        WorldMapRegion region = Region(id);
        if (region == null) return null;
        if (Cities.Any(city => city.region == id)) return LM.Get("scenario_editor_region_not_empty");
        Before();
        EnsureOwnWorld();
        Era.regions.RemoveAll(item => item.id == id);
        foreach (WorldMapPolity polity in Era.polities) polity.regions?.Remove(id);
        foreach (WorldMapCore core in Era.cores)
        {
            core.regions?.Remove(id);
            core.share?.Remove(id);
        }
        foreach (WorldMapCity city in Era.cities) city.share?.Remove(id);
        Era.culture.Remove(id);
        DropLandKey("r:" + id);
        int forgotten = ForgetInEvents(region: id);
        Changed();
        return LM.Get("scenario_editor_region_deleted") + EventsNote(forgotten);
    }

    #endregion

    #region 帝国法理与天下

    // 帝国法理：剧本自己的在前，再是内置通用的(开局时一个地区只归先写的那个)
    public static List<WorldMapCore> Cores
    {
        get
        {
            if (Era == null) return new List<WorldMapCore>();
            if (Era.OwnWorld) return Era.cores;
            var ids = new HashSet<string>(Era.cores.Select(core => core.id));
            return Era.cores.Concat(WorldMapData.BuiltinCores.Where(core => !ids.Contains(core.id))).ToList();
        }
    }

    public static Dictionary<string, string> Tianxia =>
        Era?.OwnWorld == true ? Era.tianxia ??= new Dictionary<string, string>() : WorldMapData.BuiltinTianxia;

    public static WorldMapCore Core(string id) => string.IsNullOrEmpty(id) ? null : Cores.FirstOrDefault(core => core.id == id);

    public static WorldMapCore CoreOfRegion(string regionId) =>
        string.IsNullOrEmpty(regionId) ? null : Cores.FirstOrDefault(core => core.regions?.Contains(regionId) == true);

    public static string TianxiaName(string id) =>
        string.IsNullOrEmpty(id) ? LM.Get("scenario_editor_tianxia_none")
        : Tianxia.TryGetValue(id, out string name) ? WorldMapData.PickParts(name) : id;

    // 以某王国法理另立一个帝国法理(名取法理名，文化同地区，首都取那个法理的首都，天下同原来所在的)
    public static string NewCore(string regionId)
    {
        WorldMapRegion region = Region(regionId);
        if (region == null) return null;
        Before();
        string group = CoreOfRegion(regionId)?.group ?? "";
        EnsureOwnWorld();
        region = Region(regionId);
        foreach (WorldMapCore core in Era.cores) core.regions?.Remove(regionId);
        int n = Era.cores.Count + 1;
        string id = "k" + n;
        while (Era.cores.Any(core => core.id == id)) id = "k" + ++n;
        string name = region.TitleName;
        WorldMapCity seat = City(region.capital) ?? Cities.Where(city => city.region == regionId).OrderBy(RankOf).FirstOrDefault();
        var created = new WorldMapCore
        {
            id = id, zh = name, tw = name, en = name, culture = RegionCulture(region), capital = seat?.id ?? "",
            regions = new List<string> { regionId }, group = group
        };
        Era.cores.Add(created);
        Changed();
        return id;
    }

    // 把城所在的王国法理收进某帝国法理(从别的帝国法理里拿出来)
    public static string AddRegionToCore(string coreId, string regionId)
    {
        WorldMapRegion region = Region(regionId);
        if (Core(coreId) == null || region == null || CoreOfRegion(regionId)?.id == coreId) return null;
        Before();
        EnsureOwnWorld();
        foreach (WorldMapCore other in Era.cores) other.regions?.Remove(regionId);
        WorldMapCore core = Core(coreId);
        core.regions ??= new List<string>();
        core.regions.Add(regionId);
        Changed();
        return string.Format(LM.Get("scenario_editor_core_added"), Region(regionId).TitleName, CoreName(core));
    }

    // 王国法理退出它所在的帝国法理
    public static string RemoveRegionFromCore(string regionId)
    {
        WorldMapCore owner = CoreOfRegion(regionId);
        if (owner == null) return null;
        Before();
        EnsureOwnWorld();
        foreach (WorldMapCore core in Era.cores) core.regions?.Remove(regionId);
        Changed();
        return string.Format(LM.Get("scenario_editor_core_removed"), Region(regionId)?.TitleName ?? regionId, CoreName(owner));
    }

    public static void RenameCore(string id, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Core(id) == null || CoreName(Core(id)) == name.Trim()) return;
        Before();
        EnsureOwnWorld();
        WorldMapCore core = Core(id);
        core.zh = core.tw = core.en = name.Trim();
        // 改过的名各时代都用，不再按时代换旧称
        core.alt = new List<string>();
        Changed();
    }

    // 立城为帝国法理首都(城得在这个帝国法理收的地区里)；开局称帝的国家，它那个帝国法理的首都照它的都城
    public static string SetCoreCapital(string coreId, string cityId)
    {
        WorldMapCore core = Core(coreId);
        WorldMapCity city = City(cityId);
        if (core == null || city == null) return null;
        if (core.regions?.Contains(city.region) != true)
            return string.Format(LM.Get("scenario_editor_not_in_core"), CityName(city), CoreName(core));
        Before();
        EnsureOwnWorld();
        Core(coreId).capital = cityId;
        Changed();
        return string.Format(LM.Get("scenario_editor_core_capital_set"), CoreName(Core(coreId)), CityName(city));
    }

    // 帝国法理在本剧本年份的名字(东北那块按时代叫遼、金、满洲，见 WorldMapCore.alt)
    public static string CoreName(WorldMapCore core) => core == null ? "" : Era == null ? core.Name : core.NameIn(Era.year);

    public static string CoreCapitalName(WorldMapCore core)
    {
        if (core == null) return "";
        WorldMapCity chosen = City(core.capital);
        if (chosen != null && core.regions?.Contains(chosen.region) == true) return CityName(chosen);
        WorldMapCity largest = Cities.Where(city => Active(city) && core.regions?.Contains(city.region) == true).OrderBy(RankOf).FirstOrDefault();
        return largest == null ? "" : string.Format(LM.Get("scenario_editor_auto_capital"), CityName(largest));
    }

    // 帝国法理所属的天下：在已有的天下(连同"不属任何天下")里前后换
    public static void CycleCoreTianxia(string coreId, int step)
    {
        if (Core(coreId) == null) return;
        Before();
        EnsureOwnWorld();
        List<string> ids = new List<string> { "" }.Concat(Tianxia.Keys.OrderBy(key => key, StringComparer.Ordinal)).ToList();
        Core(coreId).group = Step(ids, Core(coreId).group ?? "", step);
        Changed();
    }

    // 新立一个天下(名取帝国法理名)，这个帝国法理归它
    public static string NewTianxia(string coreId)
    {
        if (Core(coreId) == null) return null;
        Before();
        EnsureOwnWorld();
        WorldMapCore core = Core(coreId);
        int n = Tianxia.Count + 1;
        string id = "tx" + n;
        while (Tianxia.ContainsKey(id)) id = "tx" + ++n;
        Tianxia[id] = CoreName(core);
        core.group = id;
        Changed();
        return id;
    }

    public static void RenameTianxia(string id, string name)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrWhiteSpace(name) || TianxiaName(id) == name.Trim()) return;
        Before();
        EnsureOwnWorld();
        Tianxia[id] = name.Trim();
        Changed();
    }

    public static void DeleteCore(string coreId)
    {
        if (Core(coreId) == null) return;
        Before();
        EnsureOwnWorld();
        Era.cores.RemoveAll(core => core.id == coreId);
        Changed();
    }

    #endregion

    #region 关系

    // 关系种类(DiplomacyActions.ApplyScenario 认的)；交战另选战争目标
    public static readonly string[] RelationKinds =
        { "war", "truce", "alliance", "marriage", "pact", "protect", "tribute", "vassal", "union", "friend", "feud" };
    public static readonly string[] WarGoals = { "夺城", "收复", "争法理", "劫掠", "侵略", "争夺资源", "吞并", "迫使附庸", "保护国", "租借" };
    // 不分先后的(两国之间只记一条)
    private static readonly HashSet<string> Mutual = new() { "truce", "alliance", "marriage", "pact", "friend", "feud" };

    public static string KindName(string kind) =>
        kind is "friend" or "feud" || !DiplomacyActions.ScenarioKinds.TryGetValue(kind ?? "", out string key)
            ? LM.Get("scenario_editor_kind_" + kind)
            : LM.Get(key);

    // 一条关系写成一句话，谁对谁一看就明白(A→B 在不同种类里方向不一样：朝贡是 A 称臣，附庸是 A 立 B)
    public static string RelationSentence(string kind, string a, string b) => string.Format(LM.Get("scenario_editor_relation_text_" + kind), a, b);

    // 这条关系开局做不做得成(同 DiplomacyActions 的判定里编辑器看得出来的那几条)；做得成返回 null
    public static string RelationProblem(WorldMapPolity a, WorldMapPolity b, string kind)
    {
        if (a == null || b == null) return null;
        switch (kind)
        {
            case "tribute":
                if (!b.empire) return string.Format(LM.Get("scenario_editor_relation_problem_tribute"), b.Name);
                return a.empire || !string.IsNullOrEmpty(a.overlord) ? string.Format(LM.Get("scenario_editor_relation_problem_tribute_self"), a.Name) : null;
            case "pact":
                return InEmpire(a) && InEmpire(b) ? null : LM.Get("scenario_editor_relation_problem_pact");
        }
        return null;
    }

    private static bool InEmpire(WorldMapPolity polity) => polity.empire || !string.IsNullOrEmpty(polity.overlord);

    public static string GoalName(string goal) => LM.Has("war_goal_" + goal) ? LM.Get("war_goal_" + goal) : goal;

    public static List<WorldMapRelation> RelationsOf(WorldMapPolity polity) =>
        polity == null || Era == null ? new List<WorldMapRelation>()
        : Era.relations.Where(relation => relation.a == polity.id || relation.b == polity.id).ToList();

    // 两国之间加一条关系(已有同种的就去掉)；a 是发起的一方(宣战方、保护方、称臣方、立附庸方、领市场方)
    public static string ToggleRelation(WorldMapPolity a, WorldMapPolity b, string kind, string goal)
    {
        if (a == null || b == null || a == b || Era == null || string.IsNullOrEmpty(kind)) return null;
        bool mutual = Mutual.Contains(kind);
        WorldMapRelation existing = Era.relations.FirstOrDefault(relation => relation.kind == kind &&
            (relation.a == a.id && relation.b == b.id || mutual && relation.a == b.id && relation.b == a.id));
        Before();
        if (existing != null)
        {
            Era.relations.Remove(existing);
            Changed();
            return string.Format(LM.Get("scenario_editor_relation_removed"), RelationSentence(kind, a.Name, b.Name));
        }
        Era.relations.Add(new WorldMapRelation { a = a.id, b = b.id, kind = kind, goal = kind == "war" ? goal ?? "" : "" });
        Changed();
        string problem = RelationProblem(a, b, kind);
        return string.Format(LM.Get("scenario_editor_relation_added"), RelationSentence(kind, a.Name, b.Name)) +
               (problem == null ? "" : "  " + problem.ColorString("#E9A85B"));
    }

    public static string RelationText(WorldMapRelation relation)
    {
        WorldMapPolity a = Polity(relation.a), b = Polity(relation.b);
        string text = RelationSentence(relation.kind, a?.Name ?? relation.a, b?.Name ?? relation.b);
        if (relation.kind == "war" && !string.IsNullOrEmpty(relation.goal)) text += "·" + GoalName(relation.goal);
        string problem = RelationProblem(a, b, relation.kind);
        return problem == null ? text : text + "\n  " + problem.ColorString("#E9A85B");
    }

    #endregion

    #region 长城与关隘

    public static List<WorldMapWall> Walls => Era?.walls ?? new List<WorldMapWall>();
    public static List<WorldMapPass> Passes => Era?.passes ?? new List<WorldMapPass>();

    public static string WallName(WorldMapWall wall) => wall == null ? "" : string.IsNullOrEmpty(wall.Name) ? "?" : wall.Name;

    public static int NewWall()
    {
        if (Era == null) return -1;
        Before();
        string name = string.Format(LM.Get("scenario_editor_new_wall"), Era.walls.Count + 1);
        Era.walls.Add(new WorldMapWall { name = name, pts = new List<float[]>() });
        Changed();
        return Era.walls.Count - 1;
    }

    public static void AddWallPoint(int index, float lon, float lat)
    {
        if (Era == null || index < 0 || index >= Era.walls.Count) return;
        Before();
        Era.walls[index].pts ??= new List<float[]>();
        Era.walls[index].pts.Add(new[] { lon, lat });
        Changed();
    }

    public static bool RemoveWallPoint(int index)
    {
        if (Era == null || index < 0 || index >= Era.walls.Count || !(Era.walls[index].pts?.Count > 0)) return false;
        Before();
        Era.walls[index].pts.RemoveAt(Era.walls[index].pts.Count - 1);
        Changed();
        return true;
    }

    public static void DeleteWall(int index)
    {
        if (Era == null || index < 0 || index >= Era.walls.Count) return;
        Before();
        Era.walls.RemoveAt(index);
        Changed();
    }

    public static void RenameWall(int index, string name)
    {
        if (Era == null || index < 0 || index >= Era.walls.Count || string.IsNullOrWhiteSpace(name) ||
            Era.walls[index].Name == name.Trim()) return;
        Before();
        Era.walls[index].name = name.Trim();
        Changed();
    }

    public static int AddPass(float lon, float lat)
    {
        if (Era == null) return -1;
        Before();
        // 按最近的城所在地区的文化起名(见 ScenarioNaming)；附近没城用第一个地区的文化
        string culture = CultureOfRegion(Nearest(lon, lat, null)?.region ?? Regions.FirstOrDefault()?.id ?? "");
        if (!ScenarioNaming.TryPass(culture, used => Era.passes.Any(pass => pass.name == used), out string name))
            name = string.Format(LM.Get("scenario_editor_new_pass"), Era.passes.Count + 1);
        Era.passes.Add(new WorldMapPass { name = name, at = new[] { lon, lat } });
        Changed();
        return Era.passes.Count - 1;
    }

    public static void DeletePass(int index)
    {
        if (Era == null || index < 0 || index >= Era.passes.Count) return;
        Before();
        Era.passes.RemoveAt(index);
        Changed();
    }

    public static void RenamePass(int index, string name)
    {
        if (Era == null || index < 0 || index >= Era.passes.Count || string.IsNullOrWhiteSpace(name) ||
            Era.passes[index].Name == name.Trim()) return;
        Before();
        Era.passes[index].name = name.Trim();
        Changed();
    }

    #endregion

    #region 导入地图

    public static bool UsesImport => Era?.map != null;

    // 换成导入地图：城、地区、帝国法理、天下、国家、关系、长城关隘、事件都清掉(地球上的城与经纬度用不上)，留一个空地区；
    // 换回地球：城表回到内置的，国家等同样清掉。都能撤销
    public static void SetImport(bool on)
    {
        if (Era == null || on == UsesImport) return;
        Before();
        Era.map = on ? new WorldMapImport() : null;
        Era.polities.Clear();
        Era.relations.Clear();
        Era.walls.Clear();
        Era.passes.Clear();
        Era.events.Clear();
        Era.ranks.Clear();
        Era.species = null;
        Era.culture.Clear();
        Era.cores = new List<WorldMapCore>();
        // 刷的地块、地形、开发度与部落圈按经纬度记，换了地图对不上
        Era.paint = Era.terrain = Era.develop = null;
        Era.tribes = null;
        // 换了地图，照原图分地和定死的大小都不再算数
        Era.exact = false;
        Era.base_stale = true;
        Era.size = null;
        Era.independent = null;
        Era.own_cities = false;
        LoadCells();
        if (on)
        {
            var region = new WorldMapRegion { id = "r1", culture = "Huaxia" };
            region.zh = region.tw = region.en = LM.Get("scenario_editor_new_region");
            Era.regions = new List<WorldMapRegion> { region };
            Era.cities = new List<WorldMapCity>();
            Era.tianxia = new Dictionary<string, string>();
            WorldMapImporter.WriteLegend(Folder);
        }
        else
        {
            Era.regions = null;
            Era.cities = null;
            Era.tianxia = null;
        }
        _ownersRevision = -1;
        Changed();
    }

    // 本剧本开无人区(见 WorldMapEra.own_wilderness)
    public static void ToggleOwnWilderness()
    {
        if (Era == null) return;
        Before();
        Era.own_wilderness = !Era.own_wilderness;
        Changed();
    }

    public static void CycleImportMode()
    {
        if (Era?.map == null) return;
        Before();
        Era.map.mode = Era.map.mode == "height" ? "color" : "height";
        Changed();
    }

    public static void SetImportSea(string text)
    {
        if (Era?.map == null || !float.TryParse((text ?? "").Trim(), out float sea)) return;
        sea = Mathf.Clamp(sea > 1f ? sea / 100f : sea, 0.02f, 0.98f);
        if (Mathf.Approximately(sea, Era.map.sea)) return;
        Before();
        Era.map.sea = sea;
        Changed();
    }

    #endregion

    #region 笔刷与部落圈

    // 刷的格子编辑时放字典里改(一笔几百格)，存盘、存撤销快照前才写回剧本的列表(见 FlushCells)
    private static Dictionary<int, string> _landCells = new(), _terrainCells = new(), _developCells = new();
    // 导入时的原图归属(照原图分地的剧本擦掉地块回到这里；不进撤销快照，只在开关照原图分地时才看)
    private static Dictionary<int, string> _landBase = new();
    private static bool _cellsDirty;

    public static IReadOnlyDictionary<int, string> LandCells => _landCells;
    public static IReadOnlyDictionary<int, string> TerrainCells => _terrainCells;
    public static IReadOnlyDictionary<int, string> DevelopCells => _developCells;
    // 刷的地形变一次加一(编辑器据此重铺地形预览)；刷的开发度变一次加一(无人区预测据此重算)；
    // 刷的地块归属变一次加一(照原图分地的剧本里归城的地块留给城，无人区预测据此重算)
    public static int TerrainRevision { get; private set; }
    public static int DevelopRevision { get; private set; }
    public static int LandRevision { get; private set; }

    private static void LoadCells()
    {
        _landCells = ScenarioCells.ToMap(Era?.paint);
        _terrainCells = ScenarioCells.ToMap(Era?.terrain);
        _developCells = ScenarioCells.ToMap(Era?.develop);
        _cellsDirty = false;
        LandRevision++;
        TerrainRevision++;
        DevelopRevision++;
    }

    private static void FlushCells()
    {
        if (!_cellsDirty || Era == null) return;
        Era.paint = ScenarioCells.FromMap(_landCells);
        Era.terrain = ScenarioCells.FromMap(_terrainCells);
        Era.develop = ScenarioCells.FromMap(_developCells);
        _cellsDirty = false;
    }

    // 刷一笔：key 为空就擦掉；返回改了几格
    public static int PaintLand(ICollection<int> cells, string key)
    {
        if (string.IsNullOrEmpty(key) && RestoresBase) return RestoreLand(cells);
        int count = PaintCells(_landCells, cells, key, false);
        if (count > 0) LandRevision++;
        return count;
    }

    public static int PaintTerrain(ICollection<int> cells, string type) => PaintCells(_terrainCells, cells, type, true);

    // 刷开发度：value 为 "0"~"100"，空的擦掉
    public static int PaintDevelop(ICollection<int> cells, string value)
    {
        int count = PaintCells(_developCells, cells, value, false);
        if (count > 0) DevelopRevision++;
        return count;
    }

    private static int PaintCells(Dictionary<int, string> map, ICollection<int> cells, string key, bool terrain)
    {
        if (Era == null || cells == null || cells.Count == 0) return 0;
        bool erase = string.IsNullOrEmpty(key);
        List<int> changes = cells.Where(cell => erase ? map.ContainsKey(cell) : !(map.TryGetValue(cell, out string old) && old == key)).ToList();
        if (changes.Count == 0) return 0;
        Before();
        foreach (int cell in changes)
            if (erase) map.Remove(cell);
            else map[cell] = key;
        _cellsDirty = true;
        if (terrain) TerrainRevision++;
        _paintEdits++;
        Changed();
        return changes.Count;
    }

    public static int LandCount(string key) => string.IsNullOrEmpty(key) ? 0 : _landCells.Values.Count(value => value == key);

    // 照原图分地的剧本：城界全是刷的 "c:" 格子，擦掉不能真擦空(开局那块地就没人领了)，回到导入时的样子
    public static bool RestoresBase => Era?.exact == true && Era.base_stale != true && _landBase.Count > 0;

    private static int RestoreLand(IEnumerable<int> cells)
    {
        var alive = new HashSet<string>(Cities.Select(city => city.id));
        var changes = new List<(int cell, string key)>();
        foreach (int cell in cells)
        {
            string key = ScenarioPreviewRules.BaseValue(_landBase, cell, alive);
            _landCells.TryGetValue(cell, out string current);
            if (current != key) changes.Add((cell, key));
        }
        if (changes.Count == 0) return 0;
        Before();
        foreach ((int cell, string key) in changes)
            if (key == null) _landCells.Remove(cell);
            else _landCells[cell] = key;
        _cellsDirty = true;
        LandRevision++;
        _paintEdits++;
        Changed();
        return changes.Count;
    }

    // 刷的地块有没有可清的：照原图分地的看和导入时一不一样
    public static bool LandEdited
    {
        get
        {
            if (!RestoresBase) return _landCells.Count > 0;
            var alive = new HashSet<string>(Cities.Select(city => city.id));
            foreach (KeyValuePair<int, string> pair in _landCells)
                if (ScenarioPreviewRules.BaseValue(_landBase, pair.Key, alive) != pair.Value) return true;
            foreach (int cell in _landBase.Keys)
                if (ScenarioPreviewRules.BaseValue(_landBase, cell, alive) != null && !_landCells.ContainsKey(cell)) return true;
            return false;
        }
    }

    public static void ClearLand()
    {
        if (RestoresBase)
        {
            RestoreLand(_landCells.Keys.Concat(_landBase.Keys).Distinct().ToList());
            return;
        }
        if (_landCells.Count == 0) return;
        Before();
        _landCells.Clear();
        _cellsDirty = true;
        LandRevision++;
        Changed();
    }

    public static void ClearTerrain()
    {
        if (_terrainCells.Count == 0) return;
        Before();
        _terrainCells.Clear();
        _cellsDirty = true;
        TerrainRevision++;
        Changed();
    }

    public static void ClearDevelop()
    {
        if (_developCells.Count == 0) return;
        Before();
        _developCells.Clear();
        _cellsDirty = true;
        DevelopRevision++;
        Changed();
    }

    // 删国家、删法理时把刷给它的格子一并擦掉(调用处已存过快照)
    private static void DropLandKey(string key)
    {
        List<int> cells = _landCells.Where(pair => pair.Value == key).Select(pair => pair.Key).ToList();
        if (cells.Count == 0) return;
        foreach (int cell in cells) _landCells.Remove(cell);
        _cellsDirty = true;
        LandRevision++;
    }

    public static List<WorldMapTribes> Tribes => Era?.tribes ?? new List<WorldMapTribes>();

    public static WorldMapTribes TribesOf(int index) => Era?.tribes != null && index >= 0 && index < Era.tribes.Count ? Era.tribes[index] : null;

    // 画一个部落圈：文化取圈心最近那城所在法理的文化
    public static int AddTribes(float lon, float lat, float radius)
    {
        if (Era == null || radius <= 0f) return -1;
        WorldMapCity near = Nearest(lon, lat, Active);
        string culture = near == null ? "" : RegionCulture(Region(near.region));
        if (!CultureService.IsValidCulture(culture)) culture = CultureService.CultureKeys.FirstOrDefault() ?? "";
        Before();
        Era.tribes ??= new List<WorldMapTribes>();
        Era.tribes.Add(new WorldMapTribes
        {
            at = new[] { Mathf.Round(lon * 100f) / 100f, Mathf.Round(lat * 100f) / 100f }, radius = Mathf.Round(radius * 10f) / 10f, culture = culture
        });
        Changed();
        return Era.tribes.Count - 1;
    }

    public static void DeleteTribes(int index)
    {
        if (TribesOf(index) == null) return;
        Before();
        Era.tribes.RemoveAt(index);
        if (Era.tribes.Count == 0) Era.tribes = null;
        Changed();
    }

    public static void CycleTribesCulture(int index, int step)
    {
        WorldMapTribes tribes = TribesOf(index);
        if (tribes == null) return;
        Before();
        tribes.culture = Step(CultureService.CultureKeys.ToList(), tribes.culture, step);
        Changed();
    }

    // 几个部落：自动(0)、1~12
    public static void CycleTribesCount(int index, int step)
    {
        WorldMapTribes tribes = TribesOf(index);
        if (tribes == null) return;
        Before();
        tribes.count = ((tribes.count + step) % 13 + 13) % 13;
        Changed();
    }

    // 经纬度落在哪个部落圈里(几个圈叠着取最小的)；不在返回 -1
    public static int TribesAt(float lon, float lat)
    {
        int found = -1;
        float best = float.MaxValue;
        List<WorldMapTribes> list = Tribes;
        for (int i = 0; i < list.Count; i++)
        {
            WorldMapTribes tribes = list[i];
            if (tribes?.at == null || tribes.at.Length < 2) continue;
            float dx = lon - tribes.at[0], dy = lat - tribes.at[1];
            if (dx * dx + dy * dy > tribes.radius * tribes.radius || tribes.radius >= best) continue;
            best = tribes.radius;
            found = i;
        }
        return found;
    }

    // 地图范围：不定 → 各档 → 不定
    public static void CycleFrame(int step)
    {
        if (Era == null) return;
        var options = new List<string> { "" };
        options.AddRange(EarthTerrain.Frames.Select(frame => frame.id));
        string next = Step(options, Era.frame ?? "", step);
        Before();
        Era.frame = next.Length == 0 ? null : next;
        Changed();
    }

    #endregion

    #region 剧情事件

    public static List<WorldMapEvent> Events => Era?.events ?? new List<WorldMapEvent>();

    public static WorldMapEvent Event(int index) => Era?.events != null && index >= 0 && index < Era.events.Count ? Era.events[index] : null;

    private static WorldMapEventStep Step(int index, bool effect, int step)
    {
        List<WorldMapEventStep> list = effect ? Event(index)?.then : Event(index)?.when;
        return list != null && step >= 0 && step < list.Count ? list[step] : null;
    }

    private static IEnumerable<WorldMapEventStep> Steps(WorldMapEvent item) =>
        (item?.when ?? new List<WorldMapEventStep>()).Concat(item?.then ?? new List<WorldMapEventStep>());

    // 删国、删城、删法理：事件里点名它的项清空，不留失效的编号(以前"某国已亡"对删掉的国恒成立，到点就触发；新建的对象还可能接上旧编号)。
    // 返回清了几项
    private static int ForgetInEvents(string polity = null, string city = null, string region = null)
    {
        int cleared = 0;
        foreach (WorldMapEvent item in Era.events)
            foreach (WorldMapEventStep step in Steps(item))
            {
                if (polity != null && step.a == polity)
                {
                    step.a = "";
                    cleared++;
                }
                if (polity != null && step.b == polity)
                {
                    step.b = "";
                    cleared++;
                }
                if (city != null && step.city == city)
                {
                    step.city = "";
                    cleared++;
                }
                if (city != null && step.cities?.Remove(city) == true) cleared++;
                if (region != null && step.region == region)
                {
                    step.region = "";
                    cleared++;
                }
            }
        return cleared;
    }

    private static string EventsNote(int cleared) => cleared > 0 ? string.Format(LM.Get("scenario_editor_events_cleared"), cleared) : "";

    // 这一条事件还有几项没填或已失效(开局这些做不成)
    public static int EventProblems(WorldMapEvent item)
    {
        int problems = 0;
        foreach (WorldMapEventStep step in item?.when ?? new List<WorldMapEventStep>()) problems += StepProblems(step, false);
        foreach (WorldMapEventStep step in item?.then ?? new List<WorldMapEventStep>()) problems += StepProblems(step, true);
        return problems;
    }

    private static int StepProblems(WorldMapEventStep step, bool effect)
    {
        int problems = 0;
        foreach (string slot in SlotsOf(step, effect))
        {
            switch (slot)
            {
                case "a":
                    if (Polity(step.a) == null) problems++;
                    break;
                case "b":
                    if (Polity(step.b) == null) problems++;
                    break;
                case "city":
                    // 献城改投：人、城可只写一样
                    if (City(step.city) == null && step.kind != "heir" && !(step.kind == "cede" && !string.IsNullOrWhiteSpace(step.person))) problems++;
                    break;
                case "region":
                    if (Region(step.region) == null) problems++;
                    break;
                case "person":
                    if (step.kind is "dies" or "heir" && string.IsNullOrWhiteSpace(step.person)) problems++;
                    break;
            }
        }
        return problems;
    }

    // 事件能点名的人物：剧本写了名字的君主、宗室、名臣(同 WorldMapScenario.ScenarioPeople)；不在里面的名字开局找不到人
    public static List<string> UnknownPeople(string names)
    {
        var known = new HashSet<string>();
        void Add(string family, string given)
        {
            foreach (string key in ScenarioEventSystem.NameKeys(family, given)) known.Add(key);
        }
        foreach (WorldMapPolity polity in Era?.polities ?? new List<WorldMapPolity>())
        {
            if (polity.ruler != null) Add(polity.ruler.fam, polity.ruler.giv);
            foreach (WorldMapPerson person in polity.kin ?? new List<WorldMapPerson>()) Add(person.fam, person.giv);
            foreach (WorldMapFamous person in polity.famous ?? new List<WorldMapFamous>()) Add(person.fam, person.giv);
        }
        return (names ?? "").Split(new[] { '、', ',', '，' }, StringSplitOptions.RemoveEmptyEntries).Select(name => name.Trim())
            .Where(name => name.Length > 0 && !known.Contains(name)).ToList();
    }

    // 这个剧本能选哪些结果：八旗几项只有写了八旗多元制的国家才做得成
    public static List<string> EffectKinds()
    {
        bool banner = Era?.polities.Any(polity => polity.banner) == true;
        return ScenarioEventSystem.EffectKinds.Where(kind => banner || !kind.StartsWith("banner_", StringComparison.Ordinal)).ToList();
    }

    public static int NewEvent()
    {
        if (Era == null) return -1;
        Before();
        Era.events.Add(new WorldMapEvent { name = string.Format(LM.Get("scenario_editor_new_event"), Era.events.Count + 1) });
        Changed();
        return Era.events.Count - 1;
    }

    public static void DeleteEvent(int index)
    {
        if (Event(index) == null) return;
        Before();
        Era.events.RemoveAt(index);
        Changed();
    }

    public static void RenameEvent(int index, string name)
    {
        WorldMapEvent item = Event(index);
        if (item == null || string.IsNullOrWhiteSpace(name) || item.name == name.Trim()) return;
        Before();
        item.name = name.Trim();
        Changed();
    }

    public static void SetEventText(int index, string text)
    {
        WorldMapEvent item = Event(index);
        if (item == null || (text ?? "").Trim() == (item.text ?? "")) return;
        Before();
        item.text = (text ?? "").Trim();
        Changed();
    }

    // 开局后第几年起
    public static void SetEventAfter(int index, string text)
    {
        WorldMapEvent item = Event(index);
        if (item == null || !int.TryParse((text ?? "").Trim(), out int years) || years < 0 || years == item.after) return;
        Before();
        item.after = years;
        Changed();
    }

    public static void ToggleEventRepeat(int index)
    {
        WorldMapEvent item = Event(index);
        if (item == null) return;
        Before();
        item.repeat = !item.repeat;
        Changed();
    }

    // 加一条条件(默认"某国据有某城")或结果(默认"两国交战")
    public static void AddStep(int index, bool effect)
    {
        WorldMapEvent item = Event(index);
        if (item == null) return;
        Before();
        if (effect) item.then.Add(new WorldMapEventStep { kind = "relation", relation = "war", goal = WarGoals[0] });
        else item.when.Add(new WorldMapEventStep { kind = "holds_city" });
        Changed();
    }

    public static void RemoveStep(int index, bool effect, int step)
    {
        if (Step(index, effect, step) == null) return;
        Before();
        (effect ? Event(index).then : Event(index).when).RemoveAt(step);
        Changed();
    }

    public static void CycleStepKind(int index, bool effect, int step, int delta)
    {
        WorldMapEventStep item = Step(index, effect, step);
        if (item == null) return;
        Before();
        item.kind = Step(effect ? EffectKinds() : ScenarioEventSystem.ConditionKinds.ToList(), item.kind, delta);
        if (effect && item.kind == "relation" && string.IsNullOrEmpty(item.relation)) item.relation = "war";
        Changed();
    }

    // 国(a、b)、城、地区、国号：在地图上点城或在输入框里填
    public static void SetStepSlot(int index, bool effect, int step, string slot, string value)
    {
        WorldMapEventStep item = Step(index, effect, step);
        if (item == null) return;
        string current = slot switch
        {
            "a" => item.a, "b" => item.b, "city" => item.city, "region" => item.region, "name" => item.name, "person" => item.person ?? "", _ => null
        };
        if (current == null || current == (value ?? "")) return;
        Before();
        switch (slot)
        {
            case "a": item.a = value ?? ""; break;
            case "b": item.b = value ?? ""; break;
            case "city": item.city = value ?? ""; break;
            case "region": item.region = value ?? ""; break;
            case "name": item.name = (value ?? "").Trim(); break;
            case "person": item.person = (value ?? "").Trim(); break;
        }
        Changed();
    }

    public static void SetStepValue(int index, bool effect, int step, string text)
    {
        WorldMapEventStep item = Step(index, effect, step);
        if (item == null || !int.TryParse((text ?? "").Trim(), out int value) || value == item.value) return;
        Before();
        item.value = value;
        Changed();
    }

    public static void CycleStepRelation(int index, int step, int delta)
    {
        WorldMapEventStep item = Step(index, true, step);
        if (item == null) return;
        Before();
        item.relation = Step(RelationKinds.ToList(), item.relation, delta);
        Changed();
    }

    public static void CycleStepGoal(int index, int step, int delta)
    {
        WorldMapEventStep item = Step(index, true, step);
        if (item == null) return;
        Before();
        item.goal = Step(WarGoals.ToList(), item.goal, delta);
        Changed();
    }

    public static void CycleStepCulture(int index, int step, int delta)
    {
        WorldMapEventStep item = Step(index, true, step);
        if (item == null) return;
        Before();
        item.culture = Step(CultureService.CultureKeys.ToList(), item.culture, delta);
        Changed();
    }

    // 这一种条件、结果要填哪几项
    public static List<string> SlotsOf(WorldMapEventStep step, bool effect)
    {
        if (step == null) return new List<string>();
        if (!effect)
            return step.kind switch
            {
                "holds_city" => new List<string> { "a", "city" },
                "holds_region" => new List<string> { "a", "region" },
                "at_war" => new List<string> { "a", "b" },
                "strain_above" => new List<string> { "a", "value" },
                "before" => new List<string> { "value" },
                _ => new List<string> { "a" }
            };
        return step.kind switch
        {
            "relation" => step.relation == "war" ? new List<string> { "a", "b", "relation", "goal" } : new List<string> { "a", "b", "relation" },
            "move_capital" => new List<string> { "a", "city" },
            "culture" => new List<string> { "a", "culture" },
            "proclaim" or "rename" or "adopt_huaxia" => new List<string> { "a", "name" },
            // 起兵割据：从哪国、据哪城、国号、领头人物与部将的名字(剧本人物，顿号隔开)
            "rebel" => new List<string> { "a", "city", "name", "person" },
            // 献城改投：从哪国、归哪国、哪座城、投奔的人(人、城可只写一样)；人物身故、立储：哪国的人
            "cede" => new List<string> { "a", "b", "city", "person" },
            // 身故只看人(国亡了人照样该死)
            "dies" => new List<string> { "person" },
            // 立储：城可不写(接到都城)；写了是候补，人到那座城候着
            "heir" => new List<string> { "a", "person", "city" },
            // 封官授爵：哪国、封谁、爵位(写全称，如某某王)
            "grant_title" => new List<string> { "a", "person", "name" },
            // 号召归附：入侵者、受投奔的朝廷
            "rally" => new List<string> { "a", "b" },
            // 八旗多元制：建满城、跑马圈地点名一座城(文件里可用 cities 写多座)；逃人法只要国
            "banner_city" or "banner_enclosure" => new List<string> { "a", "city" },
            _ => new List<string> { "a" }
        };
    }

    #endregion

    #region 杂项

    // 在列表里前后挪一格(不在列表里从头开始)
    private static string Step(List<string> list, string current, int step)
    {
        if (list.Count == 0) return current;
        int index = list.IndexOf(current ?? "");
        index = ((index < 0 ? 0 : index + step) % list.Count + list.Count) % list.Count;
        return list[index];
    }

    // 按名字定的颜色(国家、法理、天下，同一剧本里稳定)；行政区("d:国家:下标")取那国的颜色转色相，同一国的各省分得开，
    // 京畿("j:国家")同国家的颜色
    public static Color32 ColorOfKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return new Color32(150, 150, 150, 255);
        if (key.StartsWith("j:", StringComparison.Ordinal)) return ColorOfKey("p:" + key.Substring(2));
        // 独立的城("i:城编号")：开局自成一国，统一用灰的，不和有名有姓的国家抢眼
        if (key.StartsWith("i:", StringComparison.Ordinal))
        {
            int grey = 17;
            foreach (char c in key) grey = grey * 31 + c;
            byte tone = (byte)(135 + (grey & 0xFFFF) % 45);
            return new Color32(tone, tone, tone, 255);
        }
        int cut = key.LastIndexOf(':');
        if (key.StartsWith("d:", StringComparison.Ordinal) && cut > 2 && int.TryParse(key.Substring(cut + 1), out int index))
        {
            Color.RGBToHSV(ColorOfKey("p:" + key.Substring(2, cut - 2)), out float h, out float sat, out float v);
            Color shifted = Color.HSVToRGB((h + 0.13f + index * 0.17f) % 1f, Mathf.Clamp01(sat * 0.9f + 0.1f), Mathf.Clamp(v, 0.6f, 0.95f));
            return new Color32((byte)(shifted.r * 255), (byte)(shifted.g * 255), (byte)(shifted.b * 255), 255);
        }
        int hash = 17;
        foreach (char c in key) hash = hash * 31 + c;
        float hue = (hash & 0xFFFF) / 65535f;
        Color color = Color.HSVToRGB(hue, 0.55f + ((hash >> 16) & 3) * 0.1f, 0.8f + ((hash >> 18) & 1) * 0.1f);
        return new Color32((byte)(color.r * 255), (byte)(color.g * 255), (byte)(color.b * 255), 255);
    }

    public static Color32 ColorOf(WorldMapPolity polity) => polity == null ? new Color32(150, 150, 150, 255) : ColorOfKey("p:" + polity.id);

    #endregion
}

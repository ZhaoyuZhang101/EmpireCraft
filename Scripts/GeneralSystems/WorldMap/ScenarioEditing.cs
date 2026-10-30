using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
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
            Era = era;
            Folder = custom.folder;
            Dirty = false;
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
    }

    // 空白剧本：地球地图上的内置城各自独立、没有国家，开局年份 1 年，记下地图范围；返回文件夹名
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
            var era = new WorldMapEra { id = name, zh = title, tw = title, en = title, year = 1, focus = "east", order = 100, frame = frame };
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
            File.WriteAllText(Path.Combine(Folder, WorldMapData.CustomFile), Serialize(Era, true));
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
        Undos.Clear();
        _owners.Clear();
        _ownersRevision = -1;
        _landCells = new Dictionary<int, string>();
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
        if (Era == null || (author ?? "").Trim() == Era.author) return;
        Before();
        Era.author = (author ?? "").Trim();
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

    // 在经纬度处立一座城：地区取最近那座城的地区；返回城编号
    public static string AddCity(float lon, float lat)
    {
        if (Era == null) return null;
        Before();
        EnsureOwnWorld();
        WorldMapCity near = Nearest(lon, lat, null);
        string region = near?.region ?? Regions.FirstOrDefault()?.id ?? "";
        if (string.IsNullOrEmpty(region))
        {
            var created = new WorldMapRegion { id = "r1", culture = "Huaxia" };
            created.zh = created.tw = created.en = LM.Get("scenario_editor_new_region");
            Era.regions ??= new List<WorldMapRegion>();
            Era.regions.Add(created);
            region = created.id;
        }
        int n = Era.cities.Count + 1;
        string id = "c" + n;
        while (Era.cities.Any(city => city.id == id)) id = "c" + ++n;
        string name = string.Format(LM.Get("scenario_editor_new_city"), n);
        Era.cities.Add(new WorldMapCity { id = id, zh = name, tw = name, en = name, lon = lon, lat = lat, region = region, rank = 2 });
        Changed();
        return id;
    }

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

    // 删城：连同各国点名的城、都城、法理首都一并去掉
    public static void DeleteCity(string id)
    {
        if (City(id) == null) return;
        Before();
        EnsureOwnWorld();
        if (Era.cities.RemoveAll(city => city.id == id) == 0) return;
        foreach (WorldMapPolity polity in Era.polities)
        {
            polity.cities?.Remove(id);
            if (polity.capital == id) polity.capital = "";
            LeaveDivisions(polity, id);
        }
        foreach (WorldMapRegion region in Era.regions)
            if (region.capital == id) region.capital = "";
        foreach (WorldMapCore core in Era.cores)
            if (core.capital == id) core.capital = "";
        Era.ranks.Remove(id);
        Changed();
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
                if (polity.regions == null || !polity.regions.Contains(city.region) ||
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
        Changed();
        return string.Format(LM.Get("scenario_editor_assigned"), CityName(city), polity.Name);
    }

    // 不再点名这座城(回到按地区认领，没人认领就独立)
    public static string Release(string cityId)
    {
        WorldMapCity city = City(cityId);
        if (city == null || Era == null) return null;
        if (CapitalHolder(cityId, null) is WorldMapPolity holder)
            return string.Format(LM.Get("scenario_editor_capital_locked"), CityName(city), holder.Name);
        if (!Era.polities.Any(other => other.cities?.Contains(cityId) == true)) return LM.Get("scenario_editor_not_named");
        Before();
        foreach (WorldMapPolity other in Era.polities) other.cities?.Remove(cityId);
        Changed();
        return string.Format(LM.Get("scenario_editor_released"), CityName(city));
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

    public static void DeletePolity(WorldMapPolity polity)
    {
        if (polity == null || Era == null || !Era.polities.Contains(polity)) return;
        Before();
        Era.polities.RemoveAll(item => item.id == polity.id);
        Era.relations.RemoveAll(relation => relation.a == polity.id || relation.b == polity.id);
        DropLandKey("p:" + polity.id);
        foreach (WorldMapPolity other in Era.polities)
            if (other.overlord == polity.id) other.overlord = "";
        Changed();
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
        string fam = family == null ? polity.ruler?.fam : string.IsNullOrWhiteSpace(family) ? null : family.Trim();
        string giv = given == null ? polity.ruler?.giv : string.IsNullOrWhiteSpace(given) ? null : given.Trim();
        int years = age == null ? polity.ruler?.age ?? -1 : int.TryParse(age.Trim(), out int value) && value > 0 ? value : -1;
        if (polity.ruler != null && polity.ruler.fam == fam && polity.ruler.giv == giv && polity.ruler.age == years) return;
        Before();
        polity.ruler ??= new WorldMapPerson();
        polity.ruler.fam = fam;
        polity.ruler.giv = giv;
        polity.ruler.age = years;
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
        Changed();
        return id;
    }

    // 把城划进某王国法理；返回提示(本来就在里面返回 null)
    public static string AssignRegion(string cityId, string regionId)
    {
        WorldMapCity city = City(cityId);
        WorldMapRegion region = Region(regionId);
        if (city == null || region == null || city.region == regionId) return null;
        Before();
        EnsureOwnWorld();
        city = City(cityId);
        foreach (WorldMapRegion other in Era.regions)
            if (other.capital == cityId) other.capital = "";
        city.region = regionId;
        city.share?.Remove(regionId);
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
        Changed();
        return LM.Get("scenario_editor_region_deleted");
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
        return string.Format(LM.Get("scenario_editor_core_added"), Region(regionId).TitleName, core.Name);
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
        return string.Format(LM.Get("scenario_editor_core_removed"), Region(regionId)?.TitleName ?? regionId, owner.Name);
    }

    public static void RenameCore(string id, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Core(id) == null || Core(id).Name == name.Trim()) return;
        Before();
        EnsureOwnWorld();
        WorldMapCore core = Core(id);
        core.zh = core.tw = core.en = name.Trim();
        Changed();
    }

    // 立城为帝国法理首都(城得在这个帝国法理收的地区里)；开局称帝的国家，它那个帝国法理的首都照它的都城
    public static string SetCoreCapital(string coreId, string cityId)
    {
        WorldMapCore core = Core(coreId);
        WorldMapCity city = City(cityId);
        if (core == null || city == null) return null;
        if (core.regions?.Contains(city.region) != true)
            return string.Format(LM.Get("scenario_editor_not_in_core"), CityName(city), core.Name);
        Before();
        EnsureOwnWorld();
        Core(coreId).capital = cityId;
        Changed();
        return string.Format(LM.Get("scenario_editor_core_capital_set"), Core(coreId).Name, CityName(city));
    }

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
        Tianxia[id] = core.Name;
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
    public static readonly string[] WarGoals = { "夺城", "收复", "争法理", "劫掠", "侵略", "争夺资源", "吞并", "迫使附庸" };
    // 不分先后的(两国之间只记一条)
    private static readonly HashSet<string> Mutual = new() { "truce", "alliance", "marriage", "pact", "friend", "feud" };

    public static string KindName(string kind) =>
        kind is "friend" or "feud" || !DiplomacyActions.ScenarioKinds.TryGetValue(kind ?? "", out string key)
            ? LM.Get("scenario_editor_kind_" + kind)
            : LM.Get(key);

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
            return string.Format(LM.Get("scenario_editor_relation_removed"), a.Name, b.Name, KindName(kind));
        }
        Era.relations.Add(new WorldMapRelation { a = a.id, b = b.id, kind = kind, goal = kind == "war" ? goal ?? "" : "" });
        Changed();
        return string.Format(LM.Get("scenario_editor_relation_added"), a.Name, b.Name, KindName(kind));
    }

    public static string RelationText(WorldMapRelation relation)
    {
        WorldMapPolity a = Polity(relation.a), b = Polity(relation.b);
        string text = $"{a?.Name ?? relation.a} → {b?.Name ?? relation.b}  {KindName(relation.kind)}";
        return relation.kind == "war" && !string.IsNullOrEmpty(relation.goal) ? text + "·" + GoalName(relation.goal) : text;
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
        string name = string.Format(LM.Get("scenario_editor_new_pass"), Era.passes.Count + 1);
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
        Era.culture.Clear();
        Era.cores = new List<WorldMapCore>();
        // 刷的地块、地形、开发度与部落圈按经纬度记，换了地图对不上
        Era.paint = Era.terrain = Era.develop = null;
        Era.tribes = null;
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
    private static bool _cellsDirty;

    public static IReadOnlyDictionary<int, string> LandCells => _landCells;
    public static IReadOnlyDictionary<int, string> TerrainCells => _terrainCells;
    public static IReadOnlyDictionary<int, string> DevelopCells => _developCells;
    // 刷的地形变一次加一(编辑器据此重铺地形预览)；刷的开发度变一次加一(无人区预测据此重算)
    public static int TerrainRevision { get; private set; }
    public static int DevelopRevision { get; private set; }

    private static void LoadCells()
    {
        _landCells = ScenarioCells.ToMap(Era?.paint);
        _terrainCells = ScenarioCells.ToMap(Era?.terrain);
        _developCells = ScenarioCells.ToMap(Era?.develop);
        _cellsDirty = false;
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
    public static int PaintLand(ICollection<int> cells, string key) => PaintCells(_landCells, cells, key, false);

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
        Changed();
        return changes.Count;
    }

    public static int LandCount(string key) => string.IsNullOrEmpty(key) ? 0 : _landCells.Values.Count(value => value == key);

    public static void ClearLand()
    {
        if (_landCells.Count == 0) return;
        Before();
        _landCells.Clear();
        _cellsDirty = true;
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
        if (item == null || (text ?? "").Trim() == item.text) return;
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
        item.kind = Step((effect ? ScenarioEventSystem.EffectKinds : ScenarioEventSystem.ConditionKinds).ToList(), item.kind, delta);
        if (effect && item.kind == "relation" && string.IsNullOrEmpty(item.relation)) item.relation = "war";
        Changed();
    }

    // 国(a、b)、城、地区、国号：在地图上点城或在输入框里填
    public static void SetStepSlot(int index, bool effect, int step, string slot, string value)
    {
        WorldMapEventStep item = Step(index, effect, step);
        if (item == null) return;
        string current = slot switch { "a" => item.a, "b" => item.b, "city" => item.city, "region" => item.region, "name" => item.name, _ => null };
        if (current == null || current == (value ?? "")) return;
        Before();
        switch (slot)
        {
            case "a": item.a = value ?? ""; break;
            case "b": item.b = value ?? ""; break;
            case "city": item.city = value ?? ""; break;
            case "region": item.region = value ?? ""; break;
            case "name": item.name = (value ?? "").Trim(); break;
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
                _ => new List<string> { "a" }
            };
        return step.kind switch
        {
            "relation" => step.relation == "war" ? new List<string> { "a", "b", "relation", "goal" } : new List<string> { "a", "b", "relation" },
            "move_capital" => new List<string> { "a", "city" },
            "culture" => new List<string> { "a", "culture" },
            "proclaim" or "rename" or "adopt_huaxia" => new List<string> { "a", "name" },
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

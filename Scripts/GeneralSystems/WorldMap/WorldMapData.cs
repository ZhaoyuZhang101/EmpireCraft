using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems.WorldMap;

// 世界地图(真实地球)的数据：WorldMaps/cities.json(地区、城市)与 WorldMaps/eras/*.json(各时代格局)。
// 数据由 Tools/WorldMap 下的脚本整理(繁体名由 add_traditional.py 生成)，只在这张图里用真实地名，随机地图不受影响
public sealed class WorldMapRegion
{
    public string id = "";
    public string zh = "";
    public string tw = "";
    public string en = "";
    [JsonProperty("c")] public string culture = "";
    // 王国法理名(华夏为单字国名，如关中=雍、巴蜀=蜀)："简体|英文|繁体"；地区名本身记作法理的省份名
    [JsonProperty("t")] public string title = "";

    public string Name => WorldMapData.Pick(zh, tw, en);
    public string TitleName => string.IsNullOrEmpty(title) ? Name : WorldMapData.PickParts(title);
}

public sealed class WorldMapCity
{
    public string id = "";
    public string zh = "";
    public string tw = "";
    public string en = "";
    [JsonProperty("x")] public float lon;
    [JsonProperty("y")] public float lat;
    [JsonProperty("r")] public string region = "";
    [JsonProperty("k")] public int rank = 2;
    public int from = int.MinValue;
    public int to = int.MaxValue;
    // 旧称："到某年为止|简体|英文|繁体"，按年份从早到晚排
    public List<string> alt = new();
    // 兼属的其它地区(这些地区的王国法理也算它，历史上两属、反复易手的地方，见 DeJureShareSystem)
    public List<string> share = new();

    public bool ActiveIn(int year) => from <= year && year <= to;

    public string NameIn(int year)
    {
        foreach (string entry in alt)
        {
            string[] parts = entry.Split('|');
            if (parts.Length < 3 || !int.TryParse(parts[0], out int until) || year > until) continue;
            return WorldMapData.Pick(parts[1], parts.Length > 3 ? parts[3] : parts[1], parts[2]);
        }
        return WorldMapData.Pick(zh, tw, en);
    }
}

public sealed class WorldMapCore
{
    public string id = "";
    public string zh = "";
    public string tw = "";
    public string en = "";
    [JsonProperty("c")] public string culture = "";
    [JsonProperty("cap")] public string capital = "";
    [JsonProperty("r")] public List<string> regions = new();
    // 兼收的其它地区的王国法理(争议法理，如漢兼收秦的雍)；兼属的天下(天下标识，如西域也算中华)
    public List<string> share = new();
    public List<string> tianxia = new();
    // 所在的联合天下(天下标识：华夏七个法理同属中华，欧洲拉丁诸法理同属罗马……)；不写的自成一个天下
    [JsonProperty("tx")] public string group = "";

    public string Name => WorldMapData.Pick(zh, tw, en);
}

// 剧本人物：姓、名为"简体|英文|繁体"(姓为空串表示无姓，只称名；不写姓则按本国文化起)；
// rel 为与君主的关系(son/daughter/grandson/brother/nephew…)，of 为父亲在 kin 里的序号(-1 为君主)
public sealed class WorldMapPerson
{
    public string fam;
    public string giv;
    public int age = -1;
    public string sex = "";
    public string rel = "";
    public int of = -1;
    public bool heir;

    // 族谱：今上父亲在前史(history.json)里的族谱编号；今上自己若在前史里另有一段在位(西吉斯蒙德三世当过瑞典国王)，记他自己的编号
    [JsonProperty("f")] public string father = "";
    [JsonProperty("p")] public string self = "";

    public bool Female => sex == "f" || rel is "daughter" or "granddaughter" or "sister";
    public bool ChildOfLine => rel is "son" or "daughter" or "grandson" or "granddaughter";
}

// 开局外交关系(见 Tools/WorldMap/relations_source.py)：a、b 是本档的国 id；kind 见 DiplomacyActions.ApplyScenario；
// goal 战争目标(WarGoal 的名字，如"夺城")，value 旧谊/宿怨的分量
// 长城、边墙：name "简体|英文|繁体"，pts 是 [经度, 纬度] 折线(两点之间铺墙时沿山脊找路)
public sealed class WorldMapWall
{
    public string name = "";
    public List<float[]> pts = new();

    public string Name => WorldMapData.PickParts(name);
}

// 关隘：at 是 [经度, 纬度]，筑关城时吸到最近的城墙格作关门
public sealed class WorldMapPass
{
    public string name = "";
    public float[] at;

    public string Name => WorldMapData.PickParts(name);
}

public sealed class WorldMapRelation
{
    public string a = "", b = "", kind = "", goal = "";
    public int value;
}

// 名将名臣(见 Tools/WorldMap/famous_source.py)：kind general/minister，rank 0 良才 1 名 2 国柱，
// post chief 首席朝官 / core 中枢要职 / div 部寺 / general 统一支军 / 城 id 坐镇该城
public sealed class WorldMapFamous
{
    public string fam;
    public string giv;
    public int age = -1;
    public string sex = "";
    public string kind = "minister";
    public int rank;
    public string post = "";

    public WorldMapPerson AsPerson() => new() { fam = fam, giv = giv, age = age, sex = sex };
}

public sealed class WorldMapPolity
{
    public string id = "";
    public string zh = "";
    public string tw = "";
    public string en = "";
    [JsonProperty("c")] public string culture = "";
    [JsonProperty("cap")] public string capital = "";
    [JsonProperty("r")] public List<string> regions = new();
    public List<string> cities = new();
    // 宗主国(帝国)的 id：开局即为其成员
    [JsonProperty("of")] public string overlord = "";
    // 开局即称帝(本模组帝国)
    [JsonProperty("emp")] public bool empire;
    // 幕府将军家(临高的德川)：本国君主进世界后就任宗主帝国的征夷大将军，天皇照旧在位当虚君(见 MilitaryRegency.Appoint)
    public bool shogun;
    public string core = "";
    // 周室：开局把周室的阶段设到这里("warring" = 战国)
    public string zhou = "";
    // 周制战国：开局已称王
    public bool king;
    // 剧本君主与宗室子嗣；华夏年号与开局是年号的第几年
    public WorldMapPerson ruler;
    public List<WorldMapPerson> kin = new();
    [JsonProperty("era_name")] public string eraName = "";
    // 前史(history.json)里的朝代：开局年以前的君主写进本国史书(帝国)或原版历代君主(王国)，族谱接到今上
    public string dynasty = "";
    [JsonProperty("era_year")] public int eraYear = 1;
    // 国号是美号(明、元)：加"大"前缀，不当帝国法理名
    public bool grand;
    // 神圣之名(神圣罗马)：开局取得所在联合天下的普世之名，再授神圣
    public bool holy;
    // 迁都前的旧都(明的南京)：作留都，行多京时为陪京
    [JsonProperty("old_capital")] public string oldCapital = "";
    // 开局的行政区(明两京十三布政使司)：核心王国直辖之外的各省
    public List<WorldMapDivision> divisions = new();
    // 开局的殖民地(荷兰东印度公司、菲律宾总督府……)
    public List<WorldMapColony> colonies = new();
    // 开局在世的名将名臣
    public List<WorldMapFamous> famous = new();
    // 开局的王朝积弊("strain")与六弊(graft、annexation、bloat、military、factionalism、levies，0~100；明末之类，
    // 见 WorldMapScenario.ApplyDecline)；不写的照国祚给同岁数最少该有的
    public Dictionary<string, float> decline = new();

    public string Name => WorldMapData.Pick(zh, tw, en);
}

public sealed class WorldMapDivision
{
    public string name = "";
    public List<string> cities = new();

    public string Name => WorldMapData.PickParts(name);
}

public sealed class WorldMapColony
{
    public string name = "";
    public bool company;
    public List<string> cities = new();

    public string Name => WorldMapData.PickParts(name);
}

public sealed class WorldMapEra
{
    // 开局外交关系(在打的仗、朝贡、和亲、同盟、旧谊宿怨)
    public List<WorldMapRelation> relations = new();
    // 长城、边墙与关隘(见 Tools/WorldMap/walls_source.py、WorldMapWalls)
    public List<WorldMapWall> walls = new();
    public List<WorldMapPass> passes = new();
    public string id = "";
    public int year;
    public string focus = "east";
    public int order;
    public string zh = "";
    public string tw = "";
    public string en = "";
    public string dzh = "";
    public string dtw = "";
    public string den = "";
    // 文化 → 科技档(Technology/TechTree.json 的 tier)，"*" 为其余文化
    public Dictionary<string, int> tech = new();
    public Dictionary<string, List<string>> techExtra = new();
    public Dictionary<string, List<string>> techSkip = new();
    // 文化 → 开局直接施行的制度节点(连同前置)
    public Dictionary<string, List<string>> inst = new();
    // 地区 → 本时代的文化(覆盖地区默认文化)
    public Dictionary<string, string> culture = new();
    public List<WorldMapCore> cores = new();
    public List<WorldMapPolity> polities = new();
    // 本时代另给的城市等级(城市 id → 等级)
    public Dictionary<string, int> ranks = new();
    // 穿越者降临的城(临高剧本)
    public string transmigrant = "";

    public int RankOf(WorldMapCity city) => ranks.TryGetValue(city.id, out int rank) ? rank : city.rank;

    public string Name => WorldMapData.Pick(zh, tw, en);
    public string Description => WorldMapData.Pick(dzh, dtw, den);
    public bool East => string.Equals(focus, "east", StringComparison.OrdinalIgnoreCase);
    public string YearText => year < 0 ? string.Format(NeoModLoader.General.LM.Get("world_map_year_bc"), -year)
        : string.Format(NeoModLoader.General.LM.Get("world_map_year_ad"), year);

    public int TechTier(string cultureKey) =>
        tech.TryGetValue(cultureKey ?? "", out int tier) ? tier : tech.TryGetValue("*", out int any) ? any : 3;

    public IEnumerable<string> SkippedTechs(string cultureKey) =>
        (techSkip.TryGetValue("*", out var any) ? any : new List<string>())
        .Concat(techSkip.TryGetValue(cultureKey ?? "", out var own) ? own : new List<string>());

    public string RegionCulture(WorldMapRegion region) =>
        region == null ? "" : culture.TryGetValue(region.id, out string value) ? value : region.culture;
}

// 前史(WorldMaps/history.json，由 Tools/WorldMap/history_source.py 生成)
public sealed class WorldMapReign
{
    public string n = "", t = "", dyn = "", shi = "", shi_tw = "", miao = "", miao_tw = "", era = "", era_tw = "", p = "", f = "";
    public int a, b;
    // 在位大事："年|简体|英文|繁体"
    public List<string> ev = new();

    public IEnumerable<(int year, string text)> Events(int untilYear)
    {
        foreach (string entry in ev ?? new List<string>())
        {
            int cut = entry.IndexOf('|');
            if (cut <= 0 || !int.TryParse(entry.Substring(0, cut), out int year) || year > untilYear) continue;
            string text = WorldMapData.PickParts(entry.Substring(cut + 1));
            if (!string.IsNullOrWhiteSpace(text)) yield return (year, text);
        }
    }
}

public sealed class WorldMapAncestor
{
    public string p = "", n = "", f = "", t = "";
}

public sealed class WorldMapDynasty
{
    public string id = "", zh = "", tw = "", en = "", full = "", c = "", fam = "";
    public List<string> seat = new();
    // 称帝的王朝：亡了进已亡帝国名录
    public bool emp;
    public List<WorldMapReign> rulers = new();
    public List<WorldMapAncestor> people = new();

    public string Name => WorldMapData.Pick(zh, tw, en);
    public string FullName => string.IsNullOrEmpty(full) ? Name : WorldMapData.PickParts(full);
}

public static class WorldMapData
{
    public const string FolderName = "WorldMaps";
    private sealed class CityFile
    {
        public List<WorldMapRegion> regions = new();
        public List<WorldMapCity> cities = new();
        public List<WorldMapCore> cores = new();
        public Dictionary<string, string> tianxia = new();
    }

    public static List<WorldMapRegion> Regions { get; private set; } = new();
    public static List<WorldMapCity> Cities { get; private set; } = new();
    // 通用帝国法理(华夏秦漢晋周吴楚越，其余法兰西、德意志、意大利……)，各时代通用；时代文件里另写的优先
    public static List<WorldMapCore> Cores { get; private set; } = new();
    // 天下标识 → 天下名(Roma=罗马，Huaxia=中华)
    public static Dictionary<string, string> TianxiaNames { get; private set; } = new();
    public static List<WorldMapDynasty> Dynasties { get; private set; } = new();

    private sealed class HistoryFile
    {
        public List<WorldMapDynasty> dynasties = new();
    }

    // 各地区史载人口(万人，WorldMaps/demography.json，由 Tools/WorldMap/demography.py 生成)：开局按它定各城人口
    private sealed class DemographyFile
    {
        public List<int> years = new();
        public Dictionary<string, List<float>> pop = new();
    }

    private static DemographyFile _demography;

    // 某地区某年的人口(人)：表上两年之间按前后插值，表外取两端；没有数据为 0
    public static float RegionPopulation(string region, int year)
    {
        List<int> years = _demography?.years;
        if (years == null || years.Count == 0 || region == null || _demography.pop == null ||
            !_demography.pop.TryGetValue(region, out List<float> row) || row == null || row.Count != years.Count) return 0f;
        if (year <= years[0]) return row[0] * 10000f;
        for (int i = 1; i < years.Count; i++)
        {
            if (year > years[i]) continue;
            float t = (year - years[i - 1]) / (float)Math.Max(1, years[i] - years[i - 1]);
            return (row[i - 1] + (row[i] - row[i - 1]) * t) * 10000f;
        }
        return row[row.Count - 1] * 10000f;
    }
    public static List<WorldMapEra> Eras { get; private set; } = new();
    private static Dictionary<string, WorldMapRegion> _regionById = new();
    private static bool _loaded;

    public static string Folder => Path.Combine(ModClass._declare?.FolderPath ?? "", FolderName);

    // ch = 繁体、cz = 简体，其余语言用英文名
    public static string Pick(string zh, string tw, string en)
    {
        string language = "en";
        try
        {
            language = PlayerConfig.dict["language"].stringVal;
        }
        catch
        {
            // 取不到语言按英文
        }
        string result = language switch
        {
            "cz" => zh,
            "ch" => string.IsNullOrEmpty(tw) ? zh : tw,
            _ => en
        };
        return string.IsNullOrEmpty(result) ? zh ?? en ?? "" : result;
    }

    // "简体|英文|繁体" 按界面语言取
    public static string PickParts(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        string[] parts = text.Split('|');
        return Pick(parts[0], parts.Length > 2 ? parts[2] : parts[0], parts.Length > 1 ? parts[1] : parts[0]);
    }

    public static bool ChineseUi
    {
        get
        {
            try
            {
                string language = PlayerConfig.dict["language"].stringVal;
                return language is "cz" or "ch";
            }
            catch
            {
                return false;
            }
        }
    }

    public static WorldMapDynasty Dynasty(string id) => string.IsNullOrEmpty(id) ? null : Dynasties.FirstOrDefault(d => d.id == id);

    public static WorldMapRegion Region(string id) =>
        id != null && _regionById.TryGetValue(id, out WorldMapRegion region) ? region : null;

    public static bool EnsureLoaded()
    {
        if (_loaded) return Eras.Count > 0 && Cities.Count > 0;
        _loaded = true;
        try
        {
            var cityFile = JsonConvert.DeserializeObject<CityFile>(File.ReadAllText(Path.Combine(Folder, "cities.json")));
            Regions = cityFile?.regions ?? new List<WorldMapRegion>();
            Cities = cityFile?.cities ?? new List<WorldMapCity>();
            Cores = cityFile?.cores ?? new List<WorldMapCore>();
            TianxiaNames = cityFile?.tianxia ?? new Dictionary<string, string>();
            _regionById = Regions.Where(region => !string.IsNullOrEmpty(region.id))
                .GroupBy(region => region.id).ToDictionary(group => group.Key, group => group.First());
            string historyPath = Path.Combine(Folder, "history.json");
            if (File.Exists(historyPath))
                Dynasties = JsonConvert.DeserializeObject<HistoryFile>(File.ReadAllText(historyPath))?.dynasties ?? new List<WorldMapDynasty>();
            string demographyPath = Path.Combine(Folder, "demography.json");
            if (File.Exists(demographyPath))
                _demography = JsonConvert.DeserializeObject<DemographyFile>(File.ReadAllText(demographyPath));
            var eras = new List<WorldMapEra>();
            string eraFolder = Path.Combine(Folder, "eras");
            if (Directory.Exists(eraFolder))
                foreach (string path in Directory.GetFiles(eraFolder, "*.json"))
                {
                    try
                    {
                        WorldMapEra era = JsonConvert.DeserializeObject<WorldMapEra>(File.ReadAllText(path));
                        if (era != null && !string.IsNullOrEmpty(era.id)) eras.Add(era);
                    }
                    catch (Exception exception)
                    {
                        LogService.LogWarning($"[EmpireCraft][世界地图] 时代文件读不出来 {Path.GetFileName(path)}: {exception.Message}");
                    }
                }
            Eras = eras.OrderBy(era => era.order).ThenBy(era => era.year).ToList();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][世界地图] 城市表读不出来: {exception.Message}");
        }
        return Eras.Count > 0 && Cities.Count > 0;
    }
}

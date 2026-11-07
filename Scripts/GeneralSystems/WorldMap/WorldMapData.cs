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
    // 王国法理的首都(城编号，剧本编辑器里定；不写取地区里的国都，再不然最大的城，见 WorldMapScenario.BuildTitles)
    [JsonProperty("cap")] public string capital = "";

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
// rel 为与君主的关系(son/daughter/grandson/brother/nephew…)，of 为父亲在 kin 里的序号(-1 为君主)；
// 后妃(用户 10-30 "为什么妃子和配偶没按历史来"，见 Tools/WorldMap/consorts_source.py)：rel wife 正妻(皇后、王后)、consort 妃嫔，
// of 为丈夫在 kin 里的序号(-1 为君主)；mo 为生母在 kin 里的序号；娘家 in 为与 kin 里这一位同一家(哲哲与布木布泰)，
// house 为娘家是某国(本档的国 id)的君主家(德川和子嫁天皇，娘家是江户德川)，都不写的按本人的姓自立一家
public sealed class WorldMapPerson
{
    public string fam;
    public string giv;
    public int age = -1;
    public string sex = "";
    public string rel = "";
    public int of = -1;
    public bool heir;
    [JsonProperty("mo")] public int mother = -1;
    [JsonProperty("in")] public int sameHouse = -1;
    public string house = "";

    // 族谱：今上父亲在前史(history.json)里的族谱编号；今上自己若在前史里另有一段在位(西吉斯蒙德三世当过瑞典国王)，记他自己的编号
    [JsonProperty("f")] public string father = "";
    [JsonProperty("p")] public string self = "";

    public bool Spouse => rel is "wife" or "consort";
    public bool Female => sex == "f" || Spouse && sex != "m" || rel is "daughter" or "granddaughter" or "sister";
    public bool ChildOfLine => rel is "son" or "daughter" or "grandson" or "granddaughter";
}

// 开局外交关系(见 Tools/WorldMap/relations_source.py)：a、b 是本档的国 id；kind 见 DiplomacyActions.ApplyScenario；
// goal 战争目标(WarGoal 的名字，如"夺城")，value 旧谊/宿怨的分量
// 笔刷记的一种归属或地形与它的格子(格子编号见 ScenarioCells.Pack)
public sealed class WorldMapPaint
{
    public string key = "";
    public List<int> cells = new();
}

// 原始部落圈(用户 10-30 转述："画一个圈，里面就是相对文化的原始部落")：圆心经纬度、半径(度)、文化、几个部落(0 按圈的大小自动)
public sealed class WorldMapTribes
{
    public float[] at;
    public float radius = 3f;
    public string culture = "";
    public int count;
}

// 笔刷的格子：按经纬度的绝对网格编号(与地图范围无关)，地块归属 0.2° 一格、地形 0.1° 一格
public static class ScenarioCells
{
    public const float LandStep = 0.2f, TerrainStep = 0.1f;

    public static int Pack(int i, int j) => (j + 2000) * 10000 + (i + 5000);

    public static void Unpack(int key, out int i, out int j)
    {
        i = key % 10000 - 5000;
        j = key / 10000 - 2000;
    }

    public static int Key(float lon, float lat, float step) => Pack((int)Math.Floor(lon / step), (int)Math.Floor(lat / step));

    public static Dictionary<int, string> ToMap(List<WorldMapPaint> list)
    {
        var map = new Dictionary<int, string>();
        foreach (WorldMapPaint paint in list ?? new List<WorldMapPaint>())
            if (paint?.cells != null && !string.IsNullOrEmpty(paint.key))
                foreach (int cell in paint.cells) map[cell] = paint.key;
        return map;
    }

    public static List<WorldMapPaint> FromMap(Dictionary<int, string> map) =>
        map == null || map.Count == 0 ? null
        : map.GroupBy(pair => pair.Value).OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new WorldMapPaint { key = group.Key, cells = group.Select(pair => pair.Key).OrderBy(cell => cell).ToList() })
            .ToList();
}

// 剧情事件(剧本编辑器第四期，用户 10-30 "剧情事件：要，选项拼搭")：时机 + 条件(全部满足) + 结果，进世界后每年查一次
// (见 ScenarioEventSystem)。after 为开局后第几年起；repeat 为满足就再触发(否则只触发一次)；text 写进史书与屏幕提示(空的用 name)
public sealed class WorldMapEvent
{
    public string name = "";
    public string text = "";
    public int after;
    public bool repeat;
    public List<WorldMapEventStep> when = new();
    public List<WorldMapEventStep> then = new();
}

// 事件的一条条件或结果：kind 定种类，其余按种类用(a、b 国家编号，city 城编号，region 地区编号，value 数，name 国号，
// relation 关系种类、goal 战争目标，culture 文化；起兵割据另用 person 领头人物与部将的名字("安禄山、史思明")、cities 另带的城)
public sealed class WorldMapEventStep
{
    public string kind = "";
    public string a = "";
    public string b = "";
    public string city = "";
    public string region = "";
    public int value;
    public string name = "";
    public string relation = "";
    public string goal = "";
    public string culture = "";
    public string person = "";
    // submit：入侵者(写进史书"迫于某兵势")
    public string c = "";
    public List<string> cities;
}

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
// post chief 首席朝官 / core 中枢要职 / div 部寺 / general 统一支军 / 城 id 坐镇该城；
// in 为与本国 kin 里这一位后妃同一家(杨国忠入杨贵妃的杨氏，外戚就连得上，见 consorts_source.py)
public sealed class WorldMapFamous
{
    public string fam;
    public string giv;
    public int age = -1;
    public string sex = "";
    public string kind = "minister";
    public int rank;
    public string post = "";
    [JsonProperty("in")] public int sameHouse = -1;

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
    // 都城直辖那一块(不属于任何行政区)的州府治所经纬度；没有行政区的国家写全境的(见 WorldMapScenario.AnchorTerritory)
    public List<float[]> seats;
    public List<List<List<float[]>>> boundary;
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
    // 八旗多元制(明清鼎革的大清，见 BannerSystem)：称帝以后立这个政体
    public bool banner;
    // 开局就用这个文化的朝廷(金写 Huaxia，国号大金朝，见 CompositeEmpireService.EstablishCourt)
    public string court = "";
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
    // 入主华夏时的国号(美号，加"大")与据有即算入关的城(后金：清、北京，见 WorldMapScenario.ScriptConquests)
    public string conquest = "";
    [JsonProperty("conquest_city")] public string conquestCity = "";
    // 开局的王朝积弊("strain")与六弊(graft、annexation、bloat、military、factionalism、levies，0~100；明末之类，
    // 见 WorldMapScenario.ApplyDecline)；不写的照国祚给同岁数最少该有的
    public Dictionary<string, float> decline = new();

    public string Name => WorldMapData.Pick(zh, tw, en);
}

public sealed class WorldMapDivision
{
    public string name = "";
    public List<string> cities = new();
    // 当年这个行政区各州府治所的经纬度(只用来划界，不建城；见 WorldMapScenario.AnchorTerritory、Tools/WorldMap/anchors_source.py)
    public List<float[]> seats;
    // 自定剧本可直接给本行政区的历史范围(GeoJSON MultiPolygon 经纬度坐标)。没有面数据时按治所与地形推算。
    public List<List<List<float[]>>> boundary;

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
    // 自定剧本(剧本编辑器，见 Docs/plan-scenario-editor.md、WorldMapData.LoadCustom)：作者；另带的整套城表(架空地图，
    // 不带的用内置 cities.json；带了的帝国法理写在本剧本的 cores 里)；以下三项内置时代文件都不写
    public string author = "";
    public List<WorldMapRegion> regions;
    public List<WorldMapCity> cities;
    public Dictionary<string, string> tianxia;
    // 导入地图(剧本文件夹里的 map.png，见 WorldMapImporter)；不写用地球地图
    public WorldMapImport map;
    // 照原图分地(从当前世界导入的剧本，见 ScenarioWorldExport；用户 2026-11-03 转群友"当前地图的城市边界是怎么样的，那么导入的剧本编辑器
    // 里面城市无人区法理边界就是什么样的，以防止到时候导入直接破坏图")：各城的地块照刷的 "c:城编号" 分，开局不再按地形代价往外铺、
    // 不补小国与部落；刷的地块以外的地照常判无人区
    public bool exact;
    // 本剧本开无人区：不看模组设置「无人区」，那个设置可以一直关着保旧存档。照原图分地的剧本里城的形状不变，
    // 刷给城的地块地形够无人区的转成这座城的法理无人区，不再留给城(见 WorldMapScenario.Finish、WorldMapWilderness.SetCityClaims)
    public bool own_wilderness;
    // 导入时的原图归属(剧本编辑器里擦掉地块回到这里，见 ScenarioEditing；开局不读)
    public List<WorldMapPaint> paint_base;
    // 清空过全部城、换过地图，上面那份原图归属作废了(撤销时跟着回来，所以记在这里)
    public bool base_stale;
    // 剧本定死的地图大小(区块数 [东西, 南北]，一区块 64 格)：写了开局就按它，不再选大中小；从当前世界导入的写原图大小
    public int[] size;
    // 城表本来就可以是空的(新建的空白剧本、清空过全部城)：不回落到内置城表
    public bool own_cities;
    // 点名独立的城：所在地区被哪国认领也不归它(见 WorldMapScenario.Plan、ScenarioEditing.MakeIndependent)
    public List<string> independent;
    // 剧本编辑器的笔刷(见 ScenarioCells)：刷的地块归属(0.2° 一格，key 为 "p:国家编号" 或 "r:地区编号"，开局先归那国或那块法理最近的城；
    // 刷在无人区上的定这片地挂哪个法理——p: 那国都城的王国法理、r: 那块王国法理，见 WorldMapWilderness.BeginPaint；
    // key 为 "n" 的是永久中立区，盖到的那一片无人区开局即永久中立)
    // 与地形(0.1° 一格，key 为地块写法，开局铺地表时盖过网格)；原始部落圈(见 WorldMapScenario.PlanTribes)
    public List<WorldMapPaint> paint;
    public List<WorldMapPaint> terrain;
    // 刷的雨林大泽开发度(0.2° 一格，key 为 "0"~"100")：盖过时代开发度表，开局把格子里的草木平地改成丛林沼泽再设开发度，
    // 刷满 100 的开满(见 WorldMapDevelopment.Apply)
    public List<WorldMapPaint> develop;
    public List<WorldMapTribes> tribes;
    // 地图范围(EarthTerrain.Frames 的编号)：选这个剧本时默认选这档，编辑器里画出框；不写不改
    public string frame;
    // 从哪个预设时代复制来的(预设的编号，如 "warring_states"；存为自定剧本、导入当前世界时写)：按时代的数据(雨林大泽开发度……)
    // 照它取，见 WorldMapData.BaseOf；空白剧本、导入地图不写
    public string @base;
    // 读进来时定：剧本文件夹(内置时代为空)、对不上的地方(开局时跳过)
    [JsonIgnore] public string folder = "";
    [JsonIgnore] public List<string> problems = new();
    [JsonIgnore] public bool Custom => !string.IsNullOrEmpty(folder);
    // 自带城表：写了城，或用导入地图(导入地图上没有内置的地球城，城表可以是空的)
    [JsonIgnore] public bool OwnWorld => cities != null && (cities.Count > 0 || map != null || own_cities);
    [JsonIgnore] public bool HasSize => size is { Length: >= 2 } && size[0] > 0 && size[1] > 0;
    [JsonIgnore] public UnityEngine.Vector2Int FixedSize => HasSize ? new UnityEngine.Vector2Int(size[0], size[1]) : default;
    // 开局外交关系(在打的仗、朝贡、和亲、同盟、旧谊宿怨)
    public List<WorldMapRelation> relations = new();
    // 丝绸之路(陆路修成路、沿途市场互市，见 Tools/WorldMap/silkroad_source.py、SilkRoadSystem)
    public List<WorldMapSilkRoute> silk = new();
    // 长城、边墙与关隘(见 Tools/WorldMap/walls_source.py、WorldMapWalls)
    public List<WorldMapWall> walls = new();
    // 剧情事件(剧本编辑器第四期，见 ScenarioEventSystem)
    public List<WorldMapEvent> events = new();
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
    // 无人区的史实归属：地理单元 → 政权 id；都城法理识别政权，再从同政权的当地城市分配。
    // (见 WorldMapWilderness；Tools/WorldMap/wilderness_source.py 写入)
    public Dictionary<string, string> wilderness = new();
    // 同一政权内部明确指定的当地法理：地理单元 → 地区 id；没有可用治所时沿用原有自动挂接。
    public Dictionary<string, string> wilderness_regions = new();
    public bool ShouldSerializewilderness_regions() => wilderness_regions is { Count: > 0 };

    public int RankOf(WorldMapCity city) => ranks.TryGetValue(city.id, out int rank) ? rank : city.rank;

    public string Name => WorldMapData.Pick(zh, tw, en);
    public string Description => WorldMapData.Pick(dzh, dtw, den);
    public bool East => string.Equals(focus, "east", StringComparison.OrdinalIgnoreCase);
    public string YearText => year < 0 ? string.Format(NeoModLoader.General.LM.Get("world_map_year_bc"), -year)
        : string.Format(NeoModLoader.General.LM.Get("world_map_year_ad"), year);

    public int TechTier(string cultureKey) =>
        tech.TryGetValue(KeyFor(tech, cultureKey), out int tier) ? tier : tech.TryGetValue("*", out int any) ? any : 3;

    public IEnumerable<string> SkippedTechs(string cultureKey) =>
        (techSkip.TryGetValue("*", out var any) ? any : new List<string>())
        .Concat(techSkip.TryGetValue(KeyFor(techSkip, cultureKey), out var own) ? own : new List<string>());

    public List<string> ExtraTechs(string cultureKey) =>
        techExtra.TryGetValue(KeyFor(techExtra, cultureKey), out var extra) ? extra : new List<string>();

    public List<string> Institutions(string cultureKey) =>
        inst.TryGetValue(KeyFor(inst, cultureKey), out var nodes) ? nodes : new List<string>();

    // 剧本没单写这个文化(后来加的文化包，如女真)：照同一条制度线上写了的文化取——先取与线同名的(游牧线取游牧)，
    // 再取别的同线文化；以前没写的文化从部落起步、科技照"*"
    public static string KeyFor<T>(Dictionary<string, T> table, string cultureKey)
    {
        cultureKey ??= "";
        if (table == null || table.ContainsKey(cultureKey)) return cultureKey;
        string line = EmpireCraft.Scripts.Data.OnomasticsRule.GetInstitutionLine(cultureKey);
        if (string.IsNullOrEmpty(line)) return cultureKey;
        if (table.ContainsKey(line)) return line;
        foreach (string key in table.Keys.OrderBy(key => key, StringComparer.Ordinal))
            if (key != "*" && EmpireCraft.Scripts.Data.OnomasticsRule.GetInstitutionLine(key) == line) return key;
        return cultureKey;
    }

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

    // 内置的城表(切到带自己城表的自定剧本再切回来用)
    private static List<WorldMapRegion> _builtinRegions = new();
    private static List<WorldMapCity> _builtinCities = new();
    private static List<WorldMapCore> _builtinCores = new();
    private static Dictionary<string, string> _builtinTianxia = new();

    // 内置城表(剧本编辑器头一回改城时整套拷进剧本，见 ScenarioEditing.EnsureOwnWorld)
    public static List<WorldMapRegion> BuiltinRegions => _builtinRegions;
    public static List<WorldMapCity> BuiltinCities => _builtinCities;
    public static List<WorldMapCore> BuiltinCores => _builtinCores;
    public static Dictionary<string, string> BuiltinTianxia => _builtinTianxia;

    // 开局用哪套城表：自定剧本带了自己的城表就用它的(帝国法理只用剧本里的)，否则用内置的
    public static void Activate(WorldMapEra era)
    {
        if (!EnsureLoaded()) return;
        bool own = era?.OwnWorld == true;
        Regions = own ? era.regions ?? new List<WorldMapRegion>() : _builtinRegions;
        Cities = own ? era.cities : _builtinCities;
        Cores = own ? new List<WorldMapCore>() : _builtinCores;
        TianxiaNames = own && era.tianxia != null ? era.tianxia : _builtinTianxia;
        _regionById = Regions.Where(region => !string.IsNullOrEmpty(region.id))
            .GroupBy(region => region.id).ToDictionary(group => group.Key, group => group.First());
    }

    #region 自定剧本

    // 自定剧本(剧本编辑器第一期，见 Docs/plan-scenario-editor.md)：mods_config/EmpireCraft/scenarios/<剧本名>/scenario.json，
    // 写法同一个时代文件(eras/*.json)，可另带作者与自己的城表；整个文件夹拷给别人放进同一目录就能用
    public const string CustomFile = "scenario.json";
    public static List<WorldMapEra> Custom { get; private set; } = new();

    public static string CustomFolder => Path.Combine(ModClass.ModsConfigFolder(), "EmpireCraft", "scenarios");

    // 重新扫一遍剧本文件夹(打开世界地图窗口、点刷新时)
    public static void LoadCustom()
    {
        var list = new List<WorldMapEra>();
        try
        {
            if (Directory.Exists(CustomFolder))
                foreach (string dir in Directory.GetDirectories(CustomFolder).OrderBy(path => path, StringComparer.Ordinal))
                {
                    string path = Path.Combine(dir, CustomFile);
                    if (!File.Exists(path)) continue;
                    try
                    {
                        WorldMapEra era = JsonConvert.DeserializeObject<WorldMapEra>(File.ReadAllText(path));
                        if (era == null) continue;
                        era.folder = dir;
                        era.id = "custom/" + Path.GetFileName(dir);
                        if (string.IsNullOrEmpty(era.zh) && string.IsNullOrEmpty(era.en)) era.zh = era.en = era.tw = Path.GetFileName(dir);
                        era.problems = Check(era);
                        list.Add(era);
                    }
                    catch (Exception exception)
                    {
                        LogService.LogWarning($"[EmpireCraft][世界地图] 自定剧本读不出来 {path}: {exception.Message}");
                    }
                }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][世界地图] 剧本文件夹读不出来 {CustomFolder}: {exception.Message}");
        }
        Custom = list;
    }

    // 剧本里对不上的地方(都城、地区不在城表里，国家编号重复，关系里写了不存在的国家)：只提示，开局时那几条跳过
    private static List<string> Check(WorldMapEra era)
    {
        var problems = new List<string>();
        IEnumerable<WorldMapCity> cities = era.OwnWorld ? era.cities : _builtinCities;
        IEnumerable<WorldMapRegion> regions = era.OwnWorld ? era.regions ?? new List<WorldMapRegion>() : _builtinRegions;
        var cityIds = new HashSet<string>(cities.Select(city => city.id));
        var regionIds = new HashSet<string>(regions.Select(region => region.id));
        var polityIds = new HashSet<string>();
        foreach (WorldMapPolity polity in era.polities ?? new List<WorldMapPolity>())
        {
            if (!polityIds.Add(polity.id))
                problems.Add(string.Format(NeoModLoader.General.LM.Get("world_map_custom_problem_duplicate"), polity.id));
            if (!string.IsNullOrEmpty(polity.capital) && !cityIds.Contains(polity.capital))
                problems.Add(string.Format(NeoModLoader.General.LM.Get("world_map_custom_problem_capital"), polity.Name, polity.capital));
            foreach (string region in polity.regions ?? new List<string>())
                if (!regionIds.Contains(region))
                    problems.Add(string.Format(NeoModLoader.General.LM.Get("world_map_custom_problem_region"), polity.Name, region));
        }
        foreach (WorldMapRelation relation in era.relations ?? new List<WorldMapRelation>())
            foreach (string id in new[] { relation.a, relation.b })
                if (!polityIds.Contains(id ?? ""))
                    problems.Add(string.Format(NeoModLoader.General.LM.Get("world_map_custom_problem_relation"), id));
        return problems.Distinct().ToList();
    }

    // 这个剧本开局有多少城(小地图只放大城、中城，同 WorldMapScenario.Plan)
    public static int CityCount(WorldMapEra era, bool smallMap)
    {
        IEnumerable<WorldMapCity> cities = era.OwnWorld ? era.cities : _builtinCities;
        return cities?.Count(city => city.ActiveIn(era.year) && (!smallMap || era.RankOf(city) <= 2)) ?? 0;
    }

    // 同上，只数放得下的：范围外、离图边四格以内的城开局不建(同 WorldMapScenario.Plan)
    public static int CityCount(WorldMapEra era, WorldMapFrame frame, UnityEngine.Vector2Int size, bool smallMap)
    {
        IEnumerable<WorldMapCity> cities = era.OwnWorld ? era.cities : _builtinCities;
        if (cities == null || frame == null || size.x <= 0 || size.y <= 0) return CityCount(era, smallMap);
        frame.Bounds(size, out float bottom, out float degrees);
        int width = size.x * 64, height = size.y * 64;
        return cities.Count(city =>
        {
            if (!city.ActiveIn(era.year) || smallMap && era.RankOf(city) > 2) return false;
            int x = (int)Math.Floor((city.lon - frame.west) / degrees), y = (int)Math.Floor((city.lat - bottom) / degrees);
            return x >= 4 && y >= 4 && x < width - 4 && y < height - 4;
        });
    }

    // 删除路径把关：只认剧本文件夹正下方一层、里面确有 scenario.json 的目录，路径出错也删不到别处
    public static bool CanDeleteFolder(string root, string folder)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(folder)) return false;
        char[] slashes = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        string fullRoot = Path.GetFullPath(root).TrimEnd(slashes);
        string fullFolder = Path.GetFullPath(folder).TrimEnd(slashes);
        return string.Equals(Path.GetDirectoryName(fullFolder), fullRoot, StringComparison.OrdinalIgnoreCase) &&
               File.Exists(Path.Combine(fullFolder, CustomFile));
    }

    // 删除自定剧本：整个剧本文件夹(连同预览图、导入的地图)，删完重读列表；失败返回 false
    public static bool DeleteCustom(WorldMapEra era)
    {
        if (era == null || !era.Custom || !CanDeleteFolder(CustomFolder, era.folder)) return false;
        try
        {
            Directory.Delete(era.folder, true);
            LogService.LogInfo($"[EmpireCraft][世界地图] 删除自定剧本 {era.folder}");
            LoadCustom();
            return true;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][世界地图] 删除自定剧本失败 {era.folder}: {exception.Message}");
            LoadCustom();
            return false;
        }
    }

    // 把一个预设剧本复制成自定剧本(原样拷时代文件，改编号、剧本名，加作者一栏)；返回新剧本的文件夹名，失败返回 null
    public static string CopyAsCustom(WorldMapEra preset, string frame = null)
    {
        if (preset == null || preset.Custom) return null;
        try
        {
            string source = Path.Combine(Folder, "eras", preset.id + ".json");
            if (!File.Exists(source)) return null;
            Directory.CreateDirectory(CustomFolder);
            string name = preset.id;
            for (int i = 2; Directory.Exists(Path.Combine(CustomFolder, name)); i++) name = preset.id + "_" + i;
            string dir = Path.Combine(CustomFolder, name);
            Directory.CreateDirectory(dir);
            var data = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(source));
            data["id"] = name;
            string suffix = NeoModLoader.General.LM.Get("world_map_custom_copy_suffix");
            foreach (string key in new[] { "zh", "tw", "en" })
                if (data[key] != null) data[key] = (string)data[key] + suffix;
            data["author"] = "";
            data["base"] = preset.id;
            if (!string.IsNullOrEmpty(frame)) data["frame"] = frame;
            File.WriteAllText(Path.Combine(dir, CustomFile), data.ToString(Formatting.Indented));
            return name;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][世界地图] 存为自定剧本失败: {exception.Message}");
            return null;
        }
    }

    // 剧本按哪个预设时代取按时代的数据(雨林大泽开发度表 WorldMaps/development.json 的 dev 按时代编号写)：预设就是它自己；
    // 自定剧本(编号 custom/<文件夹>)取 base；没写 base 的旧剧本按文件夹名(存为自定剧本时取的预设编号，重名加的 _2、_3 去掉)，
    // 对得上预设才算；空白剧本、导入地图为 null
    public static string BaseOf(WorldMapEra era)
    {
        if (era == null) return null;
        if (!era.Custom && !(era.id ?? "").StartsWith("custom/", StringComparison.Ordinal)) return era.id;
        if (!string.IsNullOrEmpty(era.@base)) return era.@base;
        if (era.map != null) return null;
        string name = Path.GetFileName((era.folder ?? "").TrimEnd('/', '\\'));
        int cut = name.LastIndexOf('_');
        if (cut > 0 && int.TryParse(name.Substring(cut + 1), out _)) name = name.Substring(0, cut);
        return Eras.Any(item => item.id == name) ? name : null;
    }

    #endregion

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
            _builtinRegions = Regions;
            _builtinCities = Cities;
            _builtinCores = Cores;
            _builtinTianxia = TianxiaNames;
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

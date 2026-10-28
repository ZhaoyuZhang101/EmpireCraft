using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems.WorldMap;

// 按所选时代在地球地图上摆城建国，并按本模组规则落好文化、科技、制度、王国法理(一地区一法理)、帝国法理、天下：
// 1. 分城(与 Tools/WorldMap/check_eras.py 一致)：国家点名的城先归它，其余归"认领了该地区、都城最近"的国家，谁都不认领的各自独立；
// 2. 各文化先定科技档与制度节点(新建国家按文化定政体，所以要在建国之前)；
// 3. 逐国建城：都城由开国之君立国，其余城由同国族人建城；城心若是纯沙漠改成绿洲，免得成了住不了人的荒地；
// 4. 地区立王国法理、时代给的帝国核心挂上法理，再称帝、附庸入帝国、周室入战国，最后排天下、补科技首扫
public static class WorldMapScenario
{
    // 开局摆城时为真：新城不按部落分迁、城邦殖民分出去(见 CityPatch.CityBuilt)
    public static bool Building { get; private set; }

    private static readonly int[] Households = { 12, 12, 7, 4 };
    // 分城时从都城往外算地形代价的上限(区块代价，平原一块算 1)
    private const float CostLimit = 600f;
    // 开局辖地：按城的等级能铺多远(地形代价)、最多几块(再受模组设置的每城上限)
    private static readonly float[] TerritoryBudget = { 9f, 9f, 7f, 5f };
    private static readonly int[] TerritoryCap = { 40, 40, 28, 16 };

    private sealed class PlannedCity
    {
        public WorldMapCity data;
        public WorldMapRegion region;
        // 本时代的城市等级(时代文件可另给，如 1628 年的莫斯科、维也纳)
        public int rank;
        public string name;
        public string culture;
        public Vector2Int tile;
        public PlannedPolity owner;
        public City city;
        // 补空立的随机小国：没有史料，城名国名按本地文化随机起
        public bool random;
    }

    private sealed class PlannedPolity
    {
        public WorldMapPolity data;
        public string name;
        public string culture;
        public PlannedCity capital;
        public readonly List<PlannedCity> cities = new();
        public Kingdom kingdom;
        public Empire empire;
        public readonly List<(Actor actor, WorldMapPerson person)> kin = new();
    }

    private static WorldMapEra _era;
    private static int _sizeIndex;
    private static int _fillerStart;
    private static List<PlannedCity> _cities = new();
    private static List<PlannedPolity> _polities = new();
    private static readonly Dictionary<string, Subspecies> _subspecies = new();
    private static readonly HashSet<TileZone> _usedZones = new();

    public static void Schedule(WorldMapFrame frame, Vector2Int size, int sizeIndex, WorldMapEra era,
        Action<Action, string, bool> step)
    {
        step(SetupLaws, "World Map: Plan", false);
        step(() => Plan(frame, size, sizeIndex, era), "World Map: Plan", false);
        step(SetupCultures, "World Map: Cultures", false);
        step(() => BuildBatch(0), "World Map: Realms", false);
        step(() => SettleRealms(0), "World Map: Settle", false);
        step(SpreadTerritory, "World Map: Territory", false);
        step(PlanFillers, "World Map: Fill", false);
        step(() => BuildBatch(_fillerStart), "World Map: Fill", false);
        step(() => SettleRealms(_fillerStart), "World Map: Fill", false);
        step(FillTerritory, "World Map: Territory", false);
        step(BuildTitles, "World Map: De Jure", false);
        step(BuildEmpires, "World Map: Empires", false);
        step(Finish, "World Map: Tianxia", false);
    }

    #region 分城

    private static void Plan(WorldMapFrame frame, Vector2Int size, int sizeIndex, WorldMapEra era)
    {
        _era = era;
        _sizeIndex = sizeIndex;
        _fillerStart = 0;
        _cities = new List<PlannedCity>();
        _polities = new List<PlannedPolity>();
        _subspecies.Clear();
        _usedZones.Clear();
        int width = size.x * 64, height = size.y * 64;
        var byId = new Dictionary<string, PlannedCity>();
        foreach (WorldMapCity data in WorldMapData.Cities)
        {
            WorldMapRegion region = WorldMapData.Region(data.region);
            int rank = era.RankOf(data);
            if (region == null || !data.ActiveIn(era.year) || sizeIndex == 0 && rank > 2) continue;
            Vector2Int tile = EarthTerrain.TileOf(frame, size, data.lon, data.lat);
            if (tile.x < 4 || tile.y < 4 || tile.x >= width - 4 || tile.y >= height - 4) continue;
            var planned = new PlannedCity
            {
                data = data, region = region, rank = rank, name = data.NameIn(era.year), tile = tile,
                culture = era.RegionCulture(region)
            };
            _cities.Add(planned);
            byId[data.id] = planned;
        }
        var allData = WorldMapData.Cities.GroupBy(city => city.id).ToDictionary(group => group.Key, group => group.First());
        var polityById = new Dictionary<string, PlannedPolity>();
        foreach (WorldMapPolity data in era.polities)
        {
            var polity = new PlannedPolity { data = data, name = data.Name };
            _polities.Add(polity);
            polityById[data.id] = polity;
            foreach (string id in data.cities.Append(data.capital))
                if (id != null && byId.TryGetValue(id, out PlannedCity city) && city.owner == null)
                {
                    city.owner = polity;
                    polity.cities.Add(city);
                }
        }
        // 同一地区几国都认领的，按地形代价归最近的都城(翻山、过漠、跨海都更远)；全都走不到才按直线
        WorldMapZones.Build();
        var costs = new Dictionary<PlannedPolity, float[]>();
        foreach (PlannedCity city in _cities.Where(city => city.owner == null))
        {
            PlannedPolity best = null;
            float bestCost = float.PositiveInfinity, bestDistance = float.MaxValue;
            int target = WorldMapZones.IndexOf(city.tile);
            foreach (PlannedPolity polity in _polities)
            {
                if (!polity.data.regions.Contains(city.region.id) ||
                    !allData.TryGetValue(polity.data.capital ?? "", out WorldMapCity seat)) continue;
                if (!costs.TryGetValue(polity, out float[] cost))
                {
                    int source = WorldMapZones.IndexOf(EarthTerrain.TileOf(frame, size, seat.lon, seat.lat));
                    costs[polity] = cost = WorldMapZones.CostFrom(source, CostLimit);
                }
                float distance = Distance(city.data, seat);
                if (cost[target] < bestCost || float.IsInfinity(bestCost) && float.IsInfinity(cost[target]) && distance < bestDistance)
                {
                    bestCost = cost[target];
                    bestDistance = distance;
                    best = polity;
                }
            }
            if (best == null) continue;
            city.owner = best;
            best.cities.Add(city);
        }
        // 谁都不认领的城各自独立，以城名为国名
        foreach (PlannedCity city in _cities.Where(city => city.owner == null))
        {
            var polity = new PlannedPolity { name = city.name, culture = city.culture };
            polity.cities.Add(city);
            city.owner = polity;
            _polities.Add(polity);
        }
        foreach (PlannedPolity polity in _polities)
        {
            polity.capital = polity.data != null && byId.TryGetValue(polity.data.capital ?? "", out PlannedCity seat) &&
                             seat.owner == polity
                ? seat
                : polity.cities.OrderBy(city => city.rank).FirstOrDefault();
            polity.culture ??= CultureService.IsValidCulture(polity.data?.culture) ? polity.data.culture : polity.capital?.culture;
            // 都城排第一个建
            if (polity.capital != null)
            {
                polity.cities.Remove(polity.capital);
                polity.cities.Insert(0, polity.capital);
            }
        }
        _polities.RemoveAll(polity => polity.capital == null);
        LogService.LogInfo($"[EmpireCraft][世界地图] {era.id}：{_cities.Count} 城，{_polities.Count} 国");
    }

    private static float Distance(WorldMapCity a, WorldMapCity b)
    {
        float lat = (a.lat + b.lat) * 0.5f * Mathf.Deg2Rad;
        float dx = (a.lon - b.lon) * Mathf.Cos(lat);
        float dy = a.lat - b.lat;
        return dx * dx + dy * dy;
    }

    #endregion

    #region 文化：科技档与制度

    // 新世界不走读档，上一个世界的文化科技、制度状态还在：先清空，再按本时代给
    private static void SetupCultures()
    {
        InstitutionSystem.ResetWorldState();
        TechnologySystem.ResetWorldState();
        var cultures = new HashSet<string>(_cities.Select(city => city.culture)
            .Concat(_polities.Select(polity => polity.culture))
            .Where(CultureService.IsValidCulture));
        List<TechNodeConfig> techs = TechnologySystem.Techs.ToList();
        foreach (string culture in cultures)
        {
            try
            {
                int tier = _era.TechTier(culture);
                var skip = new HashSet<string>(_era.SkippedTechs(culture));
                foreach (TechNodeConfig tech in techs.Where(tech => tech.tier <= tier && !skip.Contains(tech.id)))
                    TechnologySystem.ForceResearch(culture, tech.id);
                if (_era.techExtra.TryGetValue(culture, out List<string> extra))
                    foreach (string id in extra) TechnologySystem.ForceResearch(culture, id);
                if (_era.inst.TryGetValue(culture, out List<string> nodes))
                    foreach (string node in nodes) InstitutionSystem.ForceEnactNode(culture, node);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][世界地图] {culture} 定科技制度失败: {exception.Message}");
            }
        }
    }

    #endregion

    #region 建城建国

    // 每步建 8 国，没建完就把下一批插在本步之后(加载中再加的步骤排在当前步骤后面)，加载画面不至于卡住一大帧
    private const int PolitiesPerStep = 8;

    private static void BuildBatch(int start)
    {
        int end = Mathf.Min(start + PolitiesPerStep, _polities.Count);
        for (int i = start; i < end; i++) BuildPolity(i);
        if (end >= _polities.Count) return;
        SmoothLoader.add(delegate
        {
            try
            {
                BuildBatch(end);
            }
            catch (Exception exception)
            {
                LogService.LogError($"[EmpireCraft][世界地图] 建国失败: {exception}");
            }
        }, "World Map: Realms");
    }

    private static void BuildPolity(int index)
    {
        if (index >= _polities.Count) return;
        PlannedPolity polity = _polities[index];
        Building = true;
        try
        {
            Actor founder = null;
            WorldMapPerson ruler = polity.data?.ruler;
            foreach (PlannedCity planned in polity.cities)
            {
                TileZone zone = FindZone(planned.tile);
                if (zone == null) continue;
                _usedZones.Add(zone);
                MakeOasis(zone);
                WorldTile tile = LandTile(zone);
                string species = SpeciesFor(polity.kingdom == null ? polity.culture : planned.culture);
                Actor actor = Spawn(species, tile);
                if (actor == null) continue;
                if (polity.kingdom == null)
                {
                    // 开国之君：剧本写了照剧本(姓名、年龄、性别)，没写的按本国文化起名，一律男主(剧本里的女主除外)
                    SetSexAge(actor, ruler, true);
                    polity.kingdom = World.world.kingdoms.makeNewCivKingdom(actor, null, false);
                    planned.city = World.world.cities.buildFirstCivilizationCity(actor);
                    // 先换成本国文化、按本国模板起名，再建家族：家族名取这人的姓，起名前没换文化就成了种族默认的西式姓
                    PrepareName(actor, polity.culture);
                    ApplyNames(actor, ruler?.fam, ruler?.giv);
                    actor.createDefaultCultureAndLanguageAndClan(planned.city.name);
                    if (ruler?.fam == "") actor.GetModName().familyName = "";
                    polity.kingdom.setUnitMetas(actor);
                    planned.city.setUnitMetas(actor);
                    actor.GetModName().SetName(actor);
                    founder = actor;
                }
                else
                {
                    actor.joinKingdom(polity.kingdom);
                    if (founder?.language != null) actor.setLanguage(founder.language);
                    if (founder?.religion != null) actor.setReligion(founder.religion);
                    planned.city = World.world.cities.buildNewCity(actor, zone);
                    actor.joinCity(planned.city);
                    PrepareName(actor, planned.culture);
                    planned.city.setUnitMetas(actor);
                    actor.GetModName().SetName(actor);
                }
                planned.city.GetOrCreate().placed_by_hand = true;
            }
            if (polity.kingdom != null && polity.capital?.city != null) polity.kingdom.setCapital(polity.capital.city);
            if (founder != null && polity.capital?.city != null && polity.data?.kin?.Count > 0) CreateKin(polity, founder);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][世界地图] 建国 {polity.name} 失败: {exception.Message}");
        }
        finally
        {
            Building = false;
        }
    }

    // 宗室：剧本里的储君、兄弟、子孙生在都城，同一家族，子孙认父(君主或 kin 里 of 指的那位)
    private static void CreateKin(PlannedPolity polity, Actor founder)
    {
        City seat = polity.capital.city;
        string surname = founder.GetModName().familyName;
        var made = new List<Actor>();
        foreach (WorldMapPerson person in polity.data.kin)
        {
            Actor actor = null;
            try
            {
                actor = Spawn(SpeciesFor(polity.culture), seat.getTile() ?? founder.current_tile);
                if (actor != null)
                {
                    SetSexAge(actor, person, false);
                    actor.joinKingdom(polity.kingdom);
                    actor.joinCity(seat);
                    PrepareName(actor, polity.culture);
                    if (founder.clan != null) actor.setClan(founder.clan);
                    ApplyNames(actor, person.fam ?? surname, person.giv);
                    if (person.fam == "") actor.GetModName().familyName = "";
                    if (person.ChildOfLine)
                    {
                        Actor parent = person.of >= 0 && person.of < made.Count ? made[person.of] : founder;
                        if (parent != null) actor.setParent1(parent);
                    }
                    actor.GetModName().SetName(actor);
                    if (person.heir) polity.kingdom.SetHeir(actor);
                }
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][世界地图] {polity.name} 宗室 {person.giv} 失败: {exception.Message}");
            }
            made.Add(actor);
            if (actor != null) polity.kin.Add((actor, person));
        }
    }

    private static void SetSexAge(Actor actor, WorldMapPerson person, bool monarch)
    {
        if (person != null && person.Female) actor.data.sex = ActorSex.Female;
        else if (monarch || person != null) actor.data.sex = ActorSex.Male;
        int age = person != null && person.age >= 0 ? person.age : monarch ? Randy.randomInt(28, 50) : -1;
        if (age >= 0) actor.data.age_overgrowth = age;
    }

    // 换成本国文化(没有就以此人为创立者新建，套本国起名模板)，清掉造人时按种族起的名，按本国模板重起
    private static void PrepareName(Actor actor, string culture)
    {
        Culture native = NativeCulture(culture, actor);
        Name name = actor.GetModName();
        if (name == null) return;
        name.firstName = null;
        name.familyName = null;
        if (native != null && actor.culture != native) actor.setCulture(native);
        actor.initializeActorName();
        if (!name.hasFirstName(actor) && native != null)
        {
            string first = CulturePatch.GetOnomasticDataSafe(native, MetaType.Unit)
                ?.generateName(actor.isSexMale() ? ActorSex.Male : ActorSex.Female);
            if (!string.IsNullOrEmpty(first)) actor.SetFirstName(first.UseLocalizedNameSeparator());
        }
    }

    private static void ApplyNames(Actor actor, string family, string given)
    {
        Name name = actor.GetModName();
        if (name == null) return;
        if (!string.IsNullOrEmpty(family)) name.familyName = WorldMapData.PickParts(family);
        if (!string.IsNullOrEmpty(given)) name.firstName = WorldMapData.PickParts(given);
    }

    private static Culture NativeCulture(string culture, Actor founder)
    {
        if (!CultureService.IsValidCulture(culture)) return null;
        Culture native = CultureService.GetNativeCultureObject(culture);
        if (native != null || founder?.city == null) return native;
        native = World.world.cultures.newCulture(founder, true);
        if (native != null) CulturePatch.insertCultureTemplate(native, culture);
        return native;
    }

    // 目标格所在区块附近找一块空地：先找四周都还没城的，再退一步只要本区块空着
    private static TileZone FindZone(Vector2Int tile)
    {
        int zx = tile.x / 8, zy = tile.y / 8;
        for (int pass = 0; pass < 2; pass++)
        {
            int limit = pass == 0 ? 3 : 6;
            for (int radius = 0; radius <= limit; radius++)
            {
                TileZone best = null;
                int bestScore = int.MinValue;
                for (int dx = -radius; dx <= radius; dx++)
                for (int dy = -radius; dy <= radius; dy++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != radius) continue;
                    TileZone zone = ZoneAt(zx + dx, zy + dy);
                    if (zone == null || zone.city != null || _usedZones.Contains(zone)) continue;
                    if (pass == 0 && zone.neighbours_all.Any(near => near.city != null)) continue;
                    int land = zone.tiles.Count(Buildable);
                    if (land < 32 || !Buildable(zone.centerTile)) continue;
                    int score = land - (Mathf.Abs(dx) + Mathf.Abs(dy)) * 4;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = zone;
                    }
                }
                if (best != null) return best;
            }
        }
        return null;
    }

    private static TileZone ZoneAt(int zx, int zy)
    {
        int x = zx * 8 + 4, y = zy * 8 + 4;
        if (x < 0 || y < 0 || x >= MapBox.width || y >= MapBox.height) return null;
        return World.world.GetTileSimple(x, y)?.zone;
    }

    private static bool Buildable(WorldTile tile)
    {
        TileTypeBase type = tile?.Type;
        return type != null && type.ground && !type.liquid && !type.mountains && !type.summit && !type.lava;
    }

    private static WorldTile LandTile(TileZone zone) =>
        Buildable(zone.centerTile) ? zone.centerTile : zone.tiles.FirstOrDefault(Buildable) ?? zone.centerTile;

    // 城心那一块若是纯沙漠(麦加、敦煌、帕尔米拉这类绿洲城)，改成草地：整块全是沙漠的地方本模组按住不了人的荒地算
    private static void MakeOasis(TileZone zone)
    {
        TopTileType grass = AssetManager.top_tiles.get("grass_low");
        foreach (WorldTile tile in zone.tiles)
        {
            if (tile?.Type == null || tile.Type.liquid) continue;
            string biome = tile.Type.biome_id;
            if (biome != "biome_desert" && biome != "biome_sand") continue;
            if (tile.Type.mountains || tile.Type.rocks) continue;
            tile.setTileTypes("soil_low", grass);
            tile.Height = tile.Type.height_min;
        }
    }

    private static string SpeciesFor(string culture)
    {
        try
        {
            if (ConfigData.speciesCulturePair.TryGetValue("human", out string human) && human == culture) return "human";
            foreach (KeyValuePair<string, string> pair in ConfigData.speciesCulturePair)
                if (pair.Value == culture && AssetManager.actor_library.get(pair.Key) != null &&
                    // 夏人只在装了古战三(连同夏人种族)时才算华夏，见 AncientWarfareCompatibility
                    (pair.Key != EmpireCraft.Scripts.Compatibility.AncientWarfareRules.XiaSpecies ||
                     EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Loaded))
                    return pair.Key;
        }
        catch
        {
            // 取不到配对就用人类
        }
        return "human";
    }

    private static Actor Spawn(string species, WorldTile tile)
    {
        _subspecies.TryGetValue(species, out Subspecies subspecies);
        if (subspecies != null && subspecies.isRekt()) subspecies = null;
        Actor actor = World.world.units.createNewUnit(species, tile, false, 0f, subspecies, null, false, true, false, true);
        if (actor == null) return species == "human" ? null : Spawn("human", tile);
        if (actor.subspecies != null) _subspecies[species] = actor.subspecies;
        return actor;
    }

    #endregion

    #region 定名、文化、人口

    private static void SettleRealms(int start)
    {
        foreach (PlannedPolity polity in _polities.Skip(start))
        {
            if (polity.kingdom == null || polity.kingdom.isRekt()) continue;
            foreach (PlannedCity planned in polity.cities.Where(planned => planned.city != null && !planned.city.isRekt()))
            {
                try
                {
                    City city = planned.city;
                    if (planned.random) RandomNames(planned, polity);
                    city.data.name = planned.name;
                    city.data.custom_name = true;
                    var extra = city.GetOrCreate();
                    extra.core_name = planned.name;
                    extra.core_name_source = planned.name;
                    if (CultureService.IsValidCulture(planned.culture)) CultureService.ForceCityCulture(city, planned.culture);
                    if (CityPopulationSystem.AbstractPopulationEnabled)
                        CityPopulationSystem.AddBackground(city, SocialClass.Peasant, planned.culture ?? "",
                            city.leader?.asset?.id ?? SpeciesFor(planned.culture), IdeologyPopulationSystem.GetDominant(city),
                            Households[Mathf.Clamp(planned.rank, 0, 3)] * CityPopulationSystem.PeoplePerSlot(city));
                }
                catch (Exception exception)
                {
                    LogService.LogWarning($"[EmpireCraft][世界地图] 城 {planned.name} 定文化失败: {exception.Message}");
                }
            }
            try
            {
                if (CultureService.IsValidCulture(polity.culture))
                    CultureService.SetRealmCulture(polity.kingdom, polity.culture, true, false);
                // 国名走模组的命名(国名 + 类别后缀)；以前写成自定义国名，后缀(王国、朝、帝国)全没了
                polity.kingdom.SetKingdomName(polity.name);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][世界地图] 国 {polity.name} 定文化失败: {exception.Message}");
            }
        }
    }

    // 随机小国：城名、国名按本地文化的起名模板起；一国自成一个王国法理(地区名记都城名，法理名记国名)
    private static void RandomNames(PlannedCity planned, PlannedPolity polity)
    {
        Culture native = NativeCulture(planned.culture, null) ?? planned.city.getCulture();
        string city = Generate(native, MetaType.City);
        planned.name = string.IsNullOrWhiteSpace(city) ? planned.city.data.name : city;
        if (planned != polity.capital) return;
        string kingdom = Generate(native, MetaType.Kingdom);
        polity.name = string.IsNullOrWhiteSpace(kingdom) ? planned.name : kingdom;
        planned.region.zh = planned.region.tw = planned.region.en = planned.name;
        planned.region.title = polity.name;
    }

    private static string Generate(Culture culture, MetaType type)
    {
        try
        {
            return culture == null ? null : CulturePatch.GetOnomasticDataSafe(culture, type)?.generateName().UseLocalizedNameSeparator();
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region 补空、铺满

    // 补空：开局辖地铺完后，离谁都远(地形代价 GapCost 以上)的宜居空地补立随机小城，文化取最近地区的(按本时代摆了城的地区中心)；
    // 相近的同文化小城合成一国(至多 CitiesPerFiller 座，一国一个王国法理)，不至于冒出几百个一城小国。
    // 一城地块有上限(模组设置，默认 50)，所以间距不能太大，否则大片冻土、草原、沙漠铺不满
    // 用户 2026-10-28 "能不能像这样至少把地图填满""没有历史的可以用随机的只要文化符合那个地域就行"
    private const float GapCost = 5f;
    private static readonly int[] MaxFillers = { 60, 160, 240 };
    private const int CitiesPerFiller = 4;
    private const int FillerJoinTiles = 80;

    private static void PlanFillers()
    {
        _fillerStart = _polities.Count;
        // 地形缓存按区块编号存，可能还是上一个世界的(城心改成绿洲也没刷新)，先清掉再判荒地
        TerrainSystem.ResetWorldState();
        WorldMapZones.Build();
        var centers = _cities.Where(city => city.city != null && !city.random && CultureService.IsValidCulture(city.culture))
            .GroupBy(city => city.region.id)
            .Select(group => (culture: group.First().culture, x: (float)group.Average(city => city.tile.x),
                y: (float)group.Average(city => city.tile.y)))
            .ToList();
        if (centers.Count == 0) return;
        List<int> gaps = WorldMapZones.Gaps(GapCost, MaxFillers[Mathf.Clamp(_sizeIndex, 0, MaxFillers.Length - 1)], index =>
        {
            TileZone zone = WorldMapZones.Zone(index);
            return zone?.centerTile != null && !_usedZones.Contains(zone) && Buildable(zone.centerTile) &&
                   zone.tiles.Count(Buildable) >= 32 && BarrenLandSystem.KindOf(zone) == BarrenLandSystem.None;
        });
        var fillers = new List<PlannedPolity>();
        foreach (int index in gaps)
        {
            TileZone zone = WorldMapZones.Zone(index);
            var tile = new Vector2Int(zone.centerTile.x, zone.centerTile.y);
            string culture = centers.OrderBy(center => (center.x - tile.x) * (center.x - tile.x) + (center.y - tile.y) * (center.y - tile.y))
                .First().culture;
            PlannedPolity polity = fillers.Where(other => other.culture == culture && other.cities.Count < CitiesPerFiller &&
                                                          (other.capital.tile - tile).sqrMagnitude <= FillerJoinTiles * FillerJoinTiles)
                .OrderBy(other => (other.capital.tile - tile).sqrMagnitude).FirstOrDefault();
            string id = "filler_" + _cities.Count(city => city.random);
            var planned = new PlannedCity
            {
                data = new WorldMapCity { id = id, region = polity?.capital.region.id ?? id, rank = 3 }, rank = 3,
                region = polity?.capital.region ?? new WorldMapRegion { id = id, culture = culture },
                tile = tile, culture = culture, random = true
            };
            if (polity == null)
            {
                polity = new PlannedPolity { culture = culture, capital = planned };
                fillers.Add(polity);
                _polities.Add(polity);
            }
            planned.owner = polity;
            polity.cities.Add(planned);
            _cities.Add(planned);
        }
        LogService.LogInfo($"[EmpireCraft][世界地图] 补空立了 {fillers.Count} 国 {gaps.Count} 城");
    }

    // 补的小国先按小城的预算铺自己的辖地，再把剩下有陆地的区块(连山地、荒漠、沿海零碎地块)分给走得到的最近的城；
    // 地块上限照模组设置，纯沙漠、纯冻土照荒地规矩可到上限的倍数(见 BarrenLandSystem)
    private static void FillTerritory()
    {
        int limit = ModClass.CITY_MAX_ZONES > 0 ? ModClass.CITY_MAX_ZONES : int.MaxValue;
        var seeds = _cities.Where(planned => planned.random && planned.city != null && !planned.city.isRekt())
            .Select(planned => (planned.city, TerritoryBudget[3], Mathf.Min(limit, TerritoryCap[3])))
            .ToList();
        int claimed = 0;
        if (seeds.Count > 0)
        {
            WorldMapZones.Build();
            WorldMapZones.Fill(seeds, (city, zone) =>
            {
                city.addZone(zone);
                claimed++;
            });
        }
        WorldMapZones.Build();
        claimed += WorldMapZones.FillAll(
            (city, zone) => city.zones.Count < (TerrainSystem.BarrenKind(zone) != BarrenLandSystem.None ? BarrenLandSystem.Limit : limit),
            (city, zone) => city.addZone(zone));
        World.world.zone_calculator?.setDrawnZonesDirty();
        LogService.LogInfo($"[EmpireCraft][世界地图] 铺满又分了 {claimed} 块");
    }

    #endregion

    // 开局辖地：各城按地形代价同时往外铺，国界停在山脊、大漠、海峡上(见 WorldMapZones.Fill)
    private static void SpreadTerritory()
    {
        int limit = ModClass.CITY_MAX_ZONES > 0 ? ModClass.CITY_MAX_ZONES : int.MaxValue;
        var seeds = _cities.Where(planned => planned.city != null && !planned.city.isRekt())
            .Select(planned => (planned.city, TerritoryBudget[Mathf.Clamp(planned.rank, 0, 3)],
                Mathf.Min(limit, TerritoryCap[Mathf.Clamp(planned.rank, 0, 3)])))
            .ToList();
        int claimed = 0;
        WorldMapZones.Fill(seeds, (city, zone) =>
        {
            city.addZone(zone);
            claimed++;
        });
        World.world.zone_calculator?.setDrawnZonesDirty();
        LogService.LogInfo($"[EmpireCraft][世界地图] 开局辖地铺了 {claimed} 块");
    }

    #region 王国法理与帝国法理

    private static readonly Dictionary<string, KingdomTitle> _titleByRegion = new();
    private static readonly List<(string region, KingdomTitle title)> _ownTitles = new();

    // 都城不是地区法理首府的剧本国家(神罗里的巴伐利亚、萨克森，意大利的萨伏依、热那亚……)：没有法理就按都城城名起国号
    // (慕尼黑国、德累斯顿国)。以都城连同本国在这个地区的城另立一块王国法理、叫国名，这些城兼属原地区的法理(争议法理)
    private static void OwnTitles()
    {
        _ownTitles.Clear();
        foreach (PlannedPolity polity in _polities.Where(polity => polity.data != null && !polity.data.empire && polity.kingdom != null &&
                                                                  polity.capital?.city != null && !polity.capital.city.isRekt()))
        {
            if (polity.kingdom.GetMainTitle() != null) continue;
            City capital = polity.capital.city;
            KingdomTitle regional = _titleByRegion.TryGetValue(polity.capital.region.id, out KingdomTitle found) ? found : null;
            KingdomTitle own = ModClass.KINGDOM_TITLE_MANAGER.newKingdomTitle(capital);
            if (own == null) continue;
            foreach (PlannedCity city in polity.cities.Where(city => city != polity.capital && city.city != null &&
                                                                     city.region == polity.capital.region))
                own.addCity(city.city);
            own.data.name = polity.name;
            own.data.province_name = polity.capital.region.Name;
            if (CultureService.IsValidCulture(polity.culture)) CultureService.SetTitleCulture(own, polity.culture, true, false);
            if (polity.kingdom.hasKing())
            {
                own.owner = polity.kingdom.king;
                polity.kingdom.king.AddOwnedTitle(own);
                polity.kingdom.SetMainTitle(own);
            }
            if (regional != null && !regional.isRekt())
                foreach (City city in own.getCities().ToList())
                    DeJureShareSystem.ShareTitle(city, regional, true);
            _ownTitles.Add((polity.capital.region.id, own));
        }
    }
    private static readonly Dictionary<string, EmpireCore> _coreById = new();
    private static readonly Dictionary<string, EmpireCore> _coreByRegion = new();
    private static readonly List<(WorldMapCore data, EmpireCore core)> _cores = new();

    // 一地区一王国法理：法理名是国名(华夏单字，如关中=雍、巴蜀=蜀)，地区名记作省份名；首府取地区内的都城(没有就取最大的城)，
    // 归首府所在国的君主。之后城的兼属(历史上两属的城另算进邻区法理)、帝国法理(时代文件另写的优先，其余用通用的)、
    // 法理兼收(争议法理)
    private static void BuildTitles()
    {
        _titleByRegion.Clear();
        _coreById.Clear();
        _coreByRegion.Clear();
        _cores.Clear();
        if (ModClass.KINGDOM_TITLE_MANAGER == null) return;
        foreach (IGrouping<string, PlannedCity> group in _cities.Where(city => city.city != null && !city.city.isRekt())
                     .GroupBy(city => city.region.id))
        {
            try
            {
                PlannedCity seat = group.Where(city => city.owner?.capital == city).OrderBy(city => city.rank).FirstOrDefault() ??
                                   group.OrderBy(city => city.rank).First();
                KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER.newKingdomTitle(seat.city);
                if (title == null) continue;
                foreach (PlannedCity city in group.Where(city => city != seat)) title.addCity(city.city);
                // 剧本国家(不是帝国)以都城所在地区的王国法理为主法理时，法理叫国名(东吁、阿瑜陀耶、朝鲜……)：
                // 国名照主法理定(见 EmpireCraftKingdomBehCheckKingdomType)，不然显示成地区法理名(骠国、堕罗钵底、辰韩)
                PlannedPolity realm = seat.owner;
                title.data.name = realm?.data != null && !realm.data.empire && realm.capital == seat ? realm.name : seat.region.TitleName;
                title.data.province_name = seat.region.Name;
                if (CultureService.IsValidCulture(seat.culture)) CultureService.SetTitleCulture(title, seat.culture, true, false);
                Kingdom kingdom = seat.city.kingdom;
                if (kingdom != null && kingdom.hasKing())
                {
                    title.owner = kingdom.king;
                    kingdom.king.AddOwnedTitle(title);
                    if (kingdom.capital != null && group.Any(city => city.city == kingdom.capital)) kingdom.SetMainTitle(title);
                }
                _titleByRegion[group.Key] = title;
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][世界地图] 地区 {group.Key} 立法理失败: {exception.Message}");
            }
        }
        foreach (PlannedCity planned in _cities.Where(city => city.city != null && city.data.share.Count > 0))
            foreach (string region in planned.data.share)
                if (_titleByRegion.TryGetValue(region, out KingdomTitle shared))
                    Guard($"城 {planned.name} 兼属 {region}", () => DeJureShareSystem.ShareTitle(planned.city, shared, true));
        Guard("封国法理", OwnTitles);

        var taken = new HashSet<string>();
        foreach (WorldMapCore data in _era.cores.Concat(WorldMapData.Cores))
        {
            List<string> regions = data.regions.Where(id => !taken.Contains(id) && _titleByRegion.ContainsKey(id)).ToList();
            if (regions.Count == 0) continue;
            try
            {
                List<KingdomTitle> titles = regions.Select(id => _titleByRegion[id]).ToList();
                List<PlannedCity> members = _cities.Where(city => city.city != null && regions.Contains(city.region.id)).ToList();
                PlannedCity seat = members.FirstOrDefault(city => city.data.id == data.capital) ??
                                   members.OrderBy(city => city.rank).FirstOrDefault();
                KingdomTitle first = seat != null && _titleByRegion.TryGetValue(seat.region.id, out KingdomTitle own) ? own : titles[0];
                EmpireCore core = EmpireCoreManager.CreateCoreFromTitle(first);
                if (core == null) continue;
                foreach (KingdomTitle title in titles.Where(title => title != first)) EmpireCoreManager.AddTitleToCore(core, title);
                core.name = data.Name;
                // 帝国法理的文化取本时代辖下城的主流文化(同一块地，罗马时代的西班牙是罗马文化，十字军时代南边是阿拉伯)，天下按它归属
                string culture = members.GroupBy(city => city.culture).OrderByDescending(g => g.Count()).Select(g => g.Key)
                    .FirstOrDefault(CultureService.IsValidCulture) ?? data.culture;
                if (CultureService.IsValidCulture(culture)) core.default_culture = culture;
                if (seat?.city != null) core.SetCoreCapital(seat.city);
                _coreById[data.id] = core;
                _cores.Add((data, core));
                foreach (string region in regions)
                {
                    taken.Add(region);
                    _coreByRegion[region] = core;
                }
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][世界地图] 帝国法理 {data.id} 失败: {exception.Message}");
            }
        }
        // 封国自立的法理也归所在地区的帝国法理
        foreach ((string region, KingdomTitle title) in _ownTitles)
            if (_coreByRegion.TryGetValue(region, out EmpireCore owner))
                Guard($"封国法理 {title.data.name} 入帝国法理", () => EmpireCoreManager.AddTitleToCore(owner, title));
        // 争议法理：帝国法理兼收邻区的王国法理(漢兼收雍、秦兼收梁与蜀、德意志兼收伦巴第……)
        foreach ((WorldMapCore data, EmpireCore core) in _cores)
            foreach (string region in data.share)
                if (_titleByRegion.TryGetValue(region, out KingdomTitle title) &&
                    (!_coreByRegion.TryGetValue(region, out EmpireCore owner) || owner != core))
                    Guard($"{data.id} 兼收 {region}", () => DeJureShareSystem.ShareCore(title, core, true));
    }

    private static void Guard(string what, Func<bool> action) => Guard(what, () => { action(); });

    private static void Guard(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][世界地图] {what} 失败: {exception}");
        }
    }

    #endregion

    #region 称帝、附庸、周室

    private static void BuildEmpires()
    {
        var byId = _polities.Where(polity => polity.data != null).ToDictionary(polity => polity.data.id, polity => polity);
        foreach (PlannedPolity polity in byId.Values.Where(polity => polity.data.empire))
        {
            if (polity.kingdom == null || polity.kingdom.isRekt() || ModClass.EMPIRE_MANAGER == null) continue;
            try
            {
                // 挂都城所在的帝国法理(蜀汉挂漢、孙吴挂吴、唐挂秦、宋挂吴、金挂晋)；时代文件点名的优先
                if (!_coreById.TryGetValue(polity.data.core ?? "", out EmpireCore core) && polity.capital != null)
                    _coreByRegion.TryGetValue(polity.capital.region.id, out core);
                polity.empire = ModClass.EMPIRE_MANAGER.NewEmpire(polity.kingdom, false, true, false, null, true, core);
                if (polity.empire == null) continue;
                // 美号(明)：加"大"前缀，国号不当帝国法理名
                if (polity.data.grand)
                {
                    polity.empire.data.directPre = RealmNamingSystem.GrandPrefix;
                    polity.empire.data.name_source = RealmNamingSystem.SourceGrandName;
                }
                polity.empire.SetEmpireName(polity.name);
                polity.kingdom.SetKingdomName(polity.name);
                AnchorCapital(polity);
                // 华夏年号：开局即是年号的第几年(一年 60 秒)
                if (!string.IsNullOrEmpty(polity.data.eraName))
                {
                    polity.empire.data.has_year_name = true;
                    polity.empire.data.year_name = WorldMapData.PickParts(polity.data.eraName);
                    polity.empire.data.newEmperor_timestamp = World.world.getCurWorldTime() - Mathf.Max(0, polity.data.eraYear - 1) * 60d;
                }
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][世界地图] {polity.name} 称帝失败: {exception.Message}");
            }
        }
        foreach (PlannedPolity polity in byId.Values.Where(polity => !string.IsNullOrEmpty(polity.data.overlord)))
        {
            if (polity.kingdom == null || polity.kingdom.isRekt() || !byId.TryGetValue(polity.data.overlord, out PlannedPolity lord) ||
                lord.empire == null || lord.empire.isRekt()) continue;
            try
            {
                lord.empire.join(polity.kingdom, true, true);
                polity.kingdom.SetKingdomName(polity.name);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][世界地图] {polity.name} 入 {lord.name} 失败: {exception.Message}");
            }
        }
        // 周室：诸侯先入宗法分封的周室，再把阶段推到战国(诸侯脱离、自行其是)，七国称王
        foreach (PlannedPolity royal in byId.Values.Where(polity => polity.data.zhou == "warring" && polity.empire != null))
        {
            try
            {
                if (!ZhouEraSystem.Applies(royal.empire)) continue;
                ZhouEraState state = ZhouEraSystem.State(royal.empire);
                state.era = ZhouEraSystem.Spring;
                ZhouEraSystem.EnterWarring(royal.empire, state);
                foreach (PlannedPolity vassal in byId.Values.Where(polity => polity.data.overlord == royal.data.id && polity.data.king))
                {
                    if (vassal.kingdom == null || vassal.kingdom.isRekt()) continue;
                    ZhouEraSystem.ProclaimKing(royal.empire, state, vassal.kingdom, false);
                    vassal.kingdom.SetKingdomName(vassal.name);
                }
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][世界地图] 周室入战国失败: {exception.Message}");
            }
        }
    }

    // 帝国法理记的都城(EmpireCore.CoreCapital，"还于旧都"、多京以它为京师)照开局实际的都城——等于开局之前已经迁过都
    // (明成祖迁北京)；剧本另记旧都的(明的南京)记成留都，多京时作陪京(见 MultiCapitalSystem.CandidateCapitals)
    private static void AnchorCapital(PlannedPolity polity)
    {
        EmpireCore core = EmpireCoreManager.Get(polity.empire);
        City capital = polity.kingdom.capital;
        if (core == null || capital == null || capital.isRekt()) return;
        City old = _cities.FirstOrDefault(city => city.data.id == polity.data.oldCapital)?.city;
        core.former_capital = old != null && !old.isRekt() && old != capital ? old.id : -1L;
        core.SetCoreCapital(capital);
    }

    private static void Finish()
    {
        Guard("法理更新", () => ModClass.KINGDOM_TITLE_MANAGER?.update(-1f));
        Guard("城的帝国法理", () => { EmpireCoreManager.ReconcileCityCores(); });
        Guard("前史", () => WorldMapHistory.Apply(_era, _polities.Select(polity => new WorldMapHistory.Realm
        {
            data = polity.data, kingdom = polity.kingdom, empire = polity.empire, kin = polity.kin.ToList()
        }).ToList(), _coreByRegion));
        Guard("天下", ArrangeTianxia);
        Guard("科技首扫", TechnologySystem.TryYearlyScan);
        Guard("帝国归属核对", () => VerifyEmpires("收尾"));
        Guard("进世界后的安排", QueueAfterLoad);
        LogService.LogInfo($"[EmpireCraft][世界地图] {_era?.id} 摆好：{World.world.kingdoms.Count()} 国，{World.world.cities.Count()} 城");
        _cities = new List<PlannedCity>();
        _polities = new List<PlannedPolity>();
        _titleByRegion.Clear();
        _coreById.Clear();
        _coreByRegion.Clear();
        _cores.Clear();
        _usedZones.Clear();
        _subspecies.Clear();
    }

    // 剧本的帝国：核心王国的帝国编号得还指着它(出过开局一个月明的核心国不再归明、帝国被当成空壳解散的事)
    private static readonly List<(Kingdom kingdom, Empire empire)> _scenarioEmpires = new();

    private static void VerifyEmpires(string when)
    {
        if (when == "收尾")
        {
            _scenarioEmpires.Clear();
            _scenarioEmpires.AddRange(_polities.Where(polity => polity.empire != null && polity.kingdom != null)
                .Select(polity => (polity.kingdom, polity.empire)));
        }
        foreach ((Kingdom kingdom, Empire empire) in _scenarioEmpires)
        {
            if (kingdom == null || kingdom.isRekt() || empire?.data == null || empire.isRekt() || empire.IsArchived()) continue;
            if (kingdom.GetEmpireID() == empire.id && empire.CoreKingdom == kingdom && kingdom.GetOrCreate().isEmpire) continue;
            LogService.LogWarning($"[EmpireCraft][世界地图]({when}) {empire.GetEmpireName()} 的核心国归属不对：" +
                                  $"帝国编号 {kingdom.GetEmpireID()}(应为 {empire.id})，核心国 {empire.CoreKingdom?.data?.name}，" +
                                  $"中央标记 {kingdom.GetOrCreate().isEmpire}；已改回");
            kingdom.SetEmpireID(empire.id);
            kingdom.GetOrCreate().isEmpire = true;
            empire.TryRepairState("world map");
        }
    }

    #region 进世界之后

    // 进了世界再做的(加载途中屏幕提示、军团这些还没就绪，出过错把后面的收尾全打断)：
    // 行政区、多京、神圣之名、殖民地、穿越者降临。加载界面看到加载走完时执行(见 WorldMapGenerator.MarkDone)
    private static readonly List<(string what, Action action)> _afterLoad = new();
    private static object _afterLoadWorld;

    private static void QueueAfterLoad()
    {
        _afterLoad.Clear();
        _afterLoadWorld = World.world;
        foreach (PlannedPolity polity in _polities.Where(polity => polity.data != null && polity.kingdom != null))
        {
            PlannedPolity p = polity;
            List<(WorldMapDivision data, List<City> cities)> divisions = p.data.divisions
                .Select(division => (division, CitiesOf(division.cities))).Where(entry => entry.Item2.Count > 0).ToList();
            if (divisions.Count > 0 && p.empire != null)
                _afterLoad.Add(($"{p.name} 行政区", () =>
                {
                    Divide(p.empire, divisions);
                    if (!_divided) Reorganize(p.empire);
                }));
            foreach (WorldMapColony colony in p.data.colonies)
            {
                WorldMapColony c = colony;
                List<City> cities = CitiesOf(c.cities);
                if (cities.Count > 0) _afterLoad.Add(($"{p.name} 殖民地 {c.Name}", () => FoundColony(p.kingdom, c, cities)));
            }
            if (p.data.holy && p.empire != null)
            {
                EmpireCore lead = _tianxiaLeads.TryGetValue("Roma", out EmpireCore roma) ? roma
                    : TianxiaSystem.Lead(EmpireCoreManager.Get(p.empire));
                _afterLoad.Add(($"{p.name} 神圣之名", () => Hallow(p.empire, lead)));
            }
        }
        // 别的帝国(剧本没写行政区的)按王国法理设行政区，走帝国自己的行政区重组(见 AdministrationReorganizationSystem)
        var explicitDivisions = new HashSet<Empire>(_polities.Where(polity => polity.empire != null && polity.data?.divisions.Count > 0)
            .Select(polity => polity.empire));
        foreach ((Kingdom _, Empire empire) in _scenarioEmpires)
        {
            Empire e = empire;
            if (!explicitDivisions.Contains(e)) _afterLoad.Add(("行政区重组", () => Reorganize(e)));
            _afterLoad.Add(("多京", () => MultiCapitalSystem.Yearly(e)));
        }
        if (!string.IsNullOrEmpty(_era?.transmigrant))
        {
            City site = _cities.FirstOrDefault(city => city.data.id == _era.transmigrant && city.city != null && !city.city.isRekt())?.city;
            if (site != null) _afterLoad.Add(("穿越者降临", () => LandTransmigrants(site)));
        }
        _afterLoad.Add(("帝国归属核对", () => VerifyEmpires("进世界")));
    }

    public static void RunAfterLoad()
    {
        if (_afterLoad.Count == 0) return;
        List<(string what, Action action)> actions = _afterLoad.ToList();
        _afterLoad.Clear();
        if (!ReferenceEquals(_afterLoadWorld, World.world)) return;
        foreach ((string what, Action action) in actions) Guard(what, action);
    }

    private static List<City> CitiesOf(IEnumerable<string> ids)
    {
        var wanted = new HashSet<string>(ids ?? Enumerable.Empty<string>());
        return _cities.Where(city => wanted.Contains(city.data.id) && city.city != null && !city.city.isRekt())
            .Select(city => city.city).ToList();
    }

    // 行政区：核心王国直辖的城按剧本划省(明两京十三布政使司；陪京那一省多京时改为直隶，见 MultiCapitalSystem.Zhili)
    private static bool _divided;

    private static void Divide(Empire empire, List<(WorldMapDivision data, List<City> cities)> divisions)
    {
        _divided = false;
        if (empire?.CoreKingdom == null || empire.isRekt()) return;
        var titled = new HashSet<KingdomTitle>();
        foreach ((WorldMapDivision data, List<City> cities) in divisions)
        {
            List<City> region = cities.Where(city => city.kingdom == empire.CoreKingdom && city != empire.CoreKingdom.capital).ToList();
            if (region.Count == 0) continue;
            KingdomTitle title = region[0].GetTitle();
            if (title == null || title.isRekt() || !titled.Add(title)) title = null;
            Kingdom province = empire.EstablishAdministrativeDivision(region, title);
            if (province == null)
            {
                HashSet<long> kept = empire.GetProtectedDirectCityIds();
                LogService.LogWarning($"[EmpireCraft][世界地图] {empire.GetEmpireName()} 设 {data.Name} 没成：{region.Count} 城，" +
                                      $"京畿留着的 {region.Count(city => kept.Contains(city.id))} 城，战时 {empire.HasTerritorialWar()}，" +
                                      $"政体 {empire.CoreKingdom.GetRegime()?.type}，城里可任官的人 {region.Sum(city => city.units?.Count(unit => unit != null && unit.isAlive() && unit.isAdult() && !unit.isKing()) ?? 0)}");
                continue;
            }
            province.SetKingdomName(data.Name);
            _divided = true;
        }
    }

    // 剧本没写行政区的帝国：核心王国直辖的城按王国法理划出去(同行政区重组，见 AdministrationReorganizationSystem；
    // 重组要正统八十以上、不在打仗，开局的帝国多半够不上)，京畿(都城与主法理)留在直辖；行政区还是封国照各政体的规矩
    private static void Reorganize(Empire empire)
    {
        if (empire?.CoreKingdom == null || empire.isRekt() || empire.IsArchived()) return;
        Kingdom core = empire.CoreKingdom;
        HashSet<long> kept = empire.GetProtectedDirectCityIds();
        int made = 0;
        foreach (IGrouping<KingdomTitle, City> group in core.cities.ToList()
                     .Where(city => city != null && !city.isRekt() && city != core.capital && !kept.Contains(city.id) && city.hasTitle())
                     .GroupBy(city => city.GetTitle()).Where(group => group.Key != null && !group.Key.isRekt()).ToList())
            if (empire.EstablishTerritorialDivision(group.ToList(), group.Key) != null) made++;
        LogService.LogInfo($"[EmpireCraft][世界地图] {empire.GetEmpireName()} 按王国法理划出 {made} 块(行政区或封国)，直辖留 {core.cities.Count} 城");
    }

    private static void FoundColony(Kingdom metropole, WorldMapColony data, List<City> cities)
    {
        List<City> held = cities.Where(city => city.kingdom == metropole).ToList();
        if (held.Count == 0 || metropole.isRekt()) return;
        ColonySystem.Establish(metropole, held, data.company, data.Name);
    }

    // 神圣罗马：开局即持所在联合天下(罗马)的普世之名，再授神圣(教廷在就由教宗加冕，不在就自行加冕，见 PapacySystem.GrantHoly)
    private static void Hallow(Empire empire, EmpireCore lead)
    {
        if (empire?.data == null || empire.isRekt() || lead == null) return;
        RealmNamingSystem.GrantUniversalAtStart(empire, lead);
        PapacySystem.GrantHoly(empire);
    }

    #endregion

    // 临高剧本：穿越者在剧本指定的城(临高)旁降临，走穿越降临权能同一套(见 TransmigrantSystem.Land)
    private static void LandTransmigrants(City site)
    {
        WorldTile tile = site?.getTile();
        if (tile == null) return;
        string error = TransmigrantSystem.Land(tile, out Kingdom _);
        if (error != null) LogService.LogWarning($"[EmpireCraft][世界地图] 穿越者没能降临: {NeoModLoader.General.LM.Get(error)}");
    }

    // 天下：剧本的帝国法理按分组(cities.json 的 tx)并成联合天下——华夏七法理同属中华、游牧诸法理同属草原、
    // 阿拉伯波斯埃及同属阿拉伯、拉丁欧洲同属罗马、斯拉夫诸法理同属斯拉夫……；以据城最多的法理作主，按剧本给名；
    // 锁定(年度自动并分不再动它，见 TianxiaSystem.AutoArrange)。不在组里的(兼属中华的西域、蒙古……)兼属所写的天下
    private static readonly Dictionary<string, EmpireCore> _tianxiaLeads = new();

    private static void ArrangeTianxia()
    {
        _tianxiaLeads.Clear();
        foreach (IGrouping<string, (WorldMapCore data, EmpireCore core)> group in _cores
                     .Where(entry => entry.core != null && !string.IsNullOrEmpty(entry.data.group)).GroupBy(entry => entry.data.group))
        {
            EmpireCore lead = group.Select(entry => entry.core)
                .OrderByDescending(core => EmpireCoreManager.GetCities(core).Count).ThenBy(core => core.id).First();
            Guard($"天下 {group.Key}", () =>
            {
                if (TianxiaSystem.Lead(lead) != lead) TianxiaSystem.Leave(lead);
                foreach ((WorldMapCore _, EmpireCore core) in group)
                    if (core != lead && TianxiaSystem.Lead(core) != lead)
                    {
                        if (TianxiaSystem.Lead(core) != core) TianxiaSystem.Leave(core);
                        TianxiaSystem.Join(core, lead);
                    }
                lead.tianxia_locked = true;
            });
            if (WorldMapData.TianxiaNames.TryGetValue(group.Key, out string name)) TianxiaSystem.Rename(lead, WorldMapData.PickParts(name));
            _tianxiaLeads[group.Key] = lead;
        }
        foreach ((WorldMapCore data, EmpireCore core) in _cores)
            foreach (string id in data.tianxia)
                if (_tianxiaLeads.TryGetValue(id, out EmpireCore target) && TianxiaSystem.Lead(core) != target)
                    Guard($"{data.id} 兼属天下 {id}", () => DeJureShareSystem.ShareTianxia(core, target, true));
    }

    // 世界地图开局的世界法则：简化铭牌、城市不毁、地块固定(关原版边界侵蚀与领土扩张，开局按地形铺好的辖地不再变)
    private static void SetupLaws()
    {
        SetLaw("empirecraft_law_simplify_nameplates", true);
        SetLaw("empirecraft_law_prevent_city_destroy", true);
        SetLaw("world_law_border_stealing", false);
        SetLaw("world_law_kingdom_expansion", false);
    }

    private static void SetLaw(string id, bool value)
    {
        WorldLaws laws = World.world.world_laws;
        if (laws?.dict == null || !laws.dict.TryGetValue(id, out PlayerOptionData option)) return;
        option.boolVal = value;
        laws.updateCaches();
    }

    #endregion
}

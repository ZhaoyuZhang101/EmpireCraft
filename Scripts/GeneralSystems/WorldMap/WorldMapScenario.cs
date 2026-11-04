using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
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
    // 开局人口：本地区当年的史载人口(见 WorldMapData.RegionPopulation)按城的等级分给本地区各城，
    // 再按 PeopleScale 折成游戏里的人数(一城装不下一州一府的人)，限在 MinPeople~MaxPeople；没有史料的地区照旧按等级给几户
    private const float PeopleScale = 100f, MinPeople = 300f, MaxPeople = 60000f;
    private static readonly float[] RankWeight = { 3f, 2f, 1.2f, 0.8f };
    private static readonly Dictionary<string, float> _regionWeight = new();
    // 开局要按人口盖房、补粮的城(进世界后分帧做，见 TickDevelop)
    private static readonly Queue<(City city, float people)> _develop = new();
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
        // 开国之君与 kin 按剧本序号排的人(没造成的为空)：后妃的丈夫、子女的生母、娘家按序号找
        public Actor founder;
        public readonly List<Actor> kinByIndex = new();
        public readonly List<(Actor actor, WorldMapFamous person)> famous = new();
        // 原始部落圈里立的部落(进世界后改原始政体，见 MakeTribes)
        public bool tribe;
    }

    private static WorldMapEra _era;
    // 这张图的经纬框与区块数(长城、关隘按经纬度落格，见 WorldMapWalls)
    private static WorldMapFrame _frame;
    private static Vector2Int _size;
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
        step(AbsorbEnclosedFillers, "World Map: Fill", false);
        step(BuildTitles, "World Map: De Jure", false);
        step(BuildEmpires, "World Map: Empires", false);
        step(Finish, "World Map: Tianxia", false);
    }

    #region 分城

    private static void Plan(WorldMapFrame frame, Vector2Int size, int sizeIndex, WorldMapEra era)
    {
        _era = era;
        // 开局铺设就要按开着无人区算(见 WorldMapEra.own_wilderness)
        WorldMapWilderness.SetOwnWild(era?.own_wilderness == true);
        _frame = frame;
        _size = size;
        frame.Bounds(size, out _, out float degreesPerTile);
        WorldMapScale.SetFromFrame(degreesPerTile);
        _regionWeight.Clear();
        _develop.Clear();
        _sizeIndex = sizeIndex;
        _fillerStart = 0;
        // 这一时代的长城、边墙作开局的农牧分界(见 WorldMapZones.SetWalls)
        WorldMapZones.SetWalls(WallLines(era, frame, size));
        _wallLines = null;
        _defaultAdministrativeCities.Clear();
        _cityTerrainReach.Clear();
        _wildAnchors.Clear();
        _fillerSites.Clear();
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
                TileZone zone = FindZone(planned.tile, planned.random && !polity.tribe);
                if (zone == null) continue;
                _usedZones.Add(zone);
                MakeOasis(zone);
                MakeValley(zone);
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
                    // 剧本没写姓、前史里本朝有家族名的，姓就取家族名(不留模板起的姓，免得人与王室宗族对不上)
                    string house = ruler?.fam == null ? HouseOf(polity.data) : null;
                    ApplyNames(actor, ruler?.fam ?? (string.IsNullOrEmpty(house) ? null : house), ruler?.giv);
                    actor.createDefaultCultureAndLanguageAndClan(planned.city.name);
                    if (ruler?.fam == "")
                    {
                        // 没有姓的君主(保加利亚、回鹘……)：本人不带姓，家族叫前史里的家族名(杜洛、药罗葛)；
                        // 以前家族按造人时模板起的姓叫，人和家族对不上
                        actor.GetModName().familyName = "";
                        NameClan(actor.clan, HouseOf(polity.data));
                    }
                    // 剧本没写姓的：姓取自家族(郁久闾氏的郁久闾)
                    else if (string.IsNullOrEmpty(actor.GetModName().familyName) && actor.clan != null)
                        actor.GetModName().familyName = actor.clan.GetMemberSurname();
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
            polity.founder = founder;
            if (founder != null && polity.capital?.city != null && polity.data?.kin?.Count > 0) CreateKin(polity, founder);
            if (founder != null && polity.capital?.city != null && polity.data?.famous?.Count > 0) CreateFamous(polity, founder);
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

    // 宗室：剧本里的储君、兄弟、子孙生在都城，同一家族，子孙认父(君主或 kin 里 of 指的那位)；
    // 后妃也生在都城，不入皇族，按娘家另立一家(同 kin 里 in 指的那位一家；house 指别国君主家的，收尾时再并过去，见 Marry)
    private static void CreateKin(PlannedPolity polity, Actor founder)
    {
        City seat = polity.capital.city;
        string surname = founder.GetModName().familyName;
        List<Actor> made = polity.kinByIndex;
        made.Clear();
        foreach (WorldMapPerson person in polity.data.kin)
        {
            Actor actor = null;
            try
            {
                actor = Spawn(SpeciesFor(polity.culture), seat.getTile() ?? founder.current_tile);
                if (actor != null && person.Spouse)
                {
                    SetSexAge(actor, person, false);
                    actor.joinKingdom(polity.kingdom);
                    actor.joinCity(seat);
                    PrepareName(actor, polity.culture);
                    ApplyNames(actor, person.fam, person.giv);
                    Actor kin = person.sameHouse >= 0 && person.sameHouse < made.Count ? made[person.sameHouse] : null;
                    if (kin?.clan != null) actor.setClan(kin.clan);
                    else Household(actor, person.fam, person.giv);
                    if (person.fam == "") actor.GetModName().familyName = "";
                    actor.GetModName().SetName(actor);
                }
                else if (actor != null)
                {
                    SetSexAge(actor, person, false);
                    actor.joinKingdom(polity.kingdom);
                    actor.joinCity(seat);
                    PrepareName(actor, polity.culture);
                    if (founder.clan != null) actor.setClan(founder.clan);
                    ApplyNames(actor, person.fam ?? surname, person.giv);
                    // 君主没有姓的，宗室也不带模板起的姓(以前同一家族里各姓各的)
                    if (person.fam == "" || person.fam == null && string.IsNullOrEmpty(surname)) actor.GetModName().familyName = "";
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

    // 后妃的娘家：另立一家，家族名就是她的姓(没有姓的以名为家名，同名臣)
    private static void Household(Actor actor, string fam, string giv)
    {
        string family = WorldMapData.PickParts(fam);
        if (string.IsNullOrEmpty(family)) actor.GetModName().familyName = "";
        World.world.clans.newClan(actor, true);
        NameClan(actor.clan, string.IsNullOrEmpty(family) ? WorldMapData.PickParts(giv) : family);
        if (string.IsNullOrEmpty(family)) actor.GetModName().familyName = "";
    }

    // 后妃(收尾、各国都建好之后)：正妻与丈夫结为夫妻(君主的配偶就是皇后、王后，皇后位、外戚按她算)，妃嫔记为丈夫的妾室，
    // 子女接上生母(后宫争储按生母算宠妃，见 HaremSuccessionSystem)；娘家是别国君主家的并入那一族(德川和子入江户德川)。
    // 先给丈夫立好宗族身份：子女按宗法随父入皇族，不随母
    private static void Marry(List<PlannedPolity> polities)
    {
        var kings = polities.Where(polity => polity.data != null && polity.founder?.data != null && !polity.founder.isRekt())
            .GroupBy(polity => polity.data.id).ToDictionary(group => group.Key, group => group.First().founder);
        foreach (PlannedPolity polity in polities.Where(polity => polity.founder != null && polity.kinByIndex.Count > 0))
        {
            List<WorldMapPerson> people = polity.data.kin;
            List<Actor> made = polity.kinByIndex;
            Actor At(int index) => index >= 0 && index < made.Count && made[index]?.data != null && !made[index].isRekt() ? made[index] : null;
            for (int i = 0; i < people.Count && i < made.Count; i++)
            {
                WorldMapPerson person = people[i];
                Actor actor = At(i);
                if (actor == null || !person.Spouse) continue;
                try
                {
                    Actor husband = person.of >= 0 ? At(person.of) : polity.founder;
                    if (husband?.data == null || husband.isRekt()) continue;
                    if (!string.IsNullOrEmpty(person.house) && kings.TryGetValue(person.house, out Actor king) && king.clan != null &&
                        actor.clan != king.clan)
                    {
                        king.CheckSpecificClan(false);
                        actor.setClan(king.clan);
                        PersonalClanIdentity own = actor.GetPersonalIdentity();
                        if (own != null && king.GetSpecificClan() is SpecificClan home) SpecificClanManager.MoveToClan(own, home);
                    }
                    husband.CheckSpecificClan(false);
                    actor.CheckSpecificClan(false);
                    PersonalClanIdentity identity = husband.GetPersonalIdentity();
                    // 开局前就成的婚：族谱照记夫妻、妾室，个人经历里不记一条开局那年的"成婚""纳妾"
                    if (person.rel == "wife")
                    {
                        if (husband.lover != null && husband.lover != actor) continue;
                        identity?.setLover(actor, recordHistory: false);
                        husband.becomeLoversWith(actor);
                    }
                    else
                    {
                        identity?.setLover(actor, isCus: true, recordHistory: false);
                        actor.lover = husband;
                    }
                }
                catch (Exception exception)
                {
                    LogService.LogWarning($"[EmpireCraft][世界地图] {polity.name} 后妃 {person.giv} 失败: {exception.Message}");
                }
            }
            for (int i = 0; i < people.Count && i < made.Count; i++)
            {
                int m = people[i].mother;
                Actor child = At(i), mother = At(m);
                if (child == null || mother == null || !people[m].Spouse) continue;
                try
                {
                    child.setParent2(mother);
                    PersonalClanIdentity identity = child.GetPersonalIdentity(), parent = mother.GetPersonalIdentity();
                    if (identity != null && parent != null && identity.mother <= 0) identity.setParent(parent, false);
                }
                catch (Exception exception)
                {
                    LogService.LogWarning($"[EmpireCraft][世界地图] {polity.name} {people[i].giv} 认生母失败: {exception.Message}");
                }
            }
        }
    }

    // 妃嫔进后宫(进世界后，帝国的后宫官职设好了)：剧本的妃嫔依次坐进空着的妃位(皇后位按君主配偶自选)。
    // 年过三十五的不坐(后宫照例另选年轻的，坐进去也当场被换)
    private static void SeatConsorts(Empire empire, List<Actor> consorts)
    {
        CenterOffice center = empire?.data?.centerOffice;
        if (center?.Harems == null) return;
        var free = center.Harems.Where(id => OfficeManager.Offices.TryGetValue(id, out OfficeObject office) && office.officeType != 13 &&
                                             office.GetActor() == null).ToList();
        foreach (Actor consort in consorts)
        {
            if (free.Count == 0) return;
            if (consort?.data == null || consort.isRekt() || !consort.isAlive() || consort.getAge() > 35 ||
                consort.kingdom?.GetEmpire() != empire || consort.IsOnOffice()) continue;
            if (!OfficeManager.Offices.TryGetValue(free[0], out OfficeObject office)) continue;
            office.SetActor(consort);
            free.RemoveAt(0);
        }
    }

    // 名将名臣：生在都城、照剧本起名(各自一家，不入宗室)；进世界后再任官、统兵、坐镇(见 SeatFamous)
    private static void CreateFamous(PlannedPolity polity, Actor founder)
    {
        City seat = polity.capital.city;
        foreach (WorldMapFamous person in polity.data.famous)
        {
            try
            {
                Actor actor = Spawn(SpeciesFor(polity.culture), seat.getTile() ?? founder.current_tile);
                if (actor == null) continue;
                SetSexAge(actor, person.AsPerson(), false);
                actor.joinKingdom(polity.kingdom);
                actor.joinCity(seat);
                PrepareName(actor, polity.culture);
                ApplyNames(actor, person.fam, person.giv);
                // 当场立家族，家族名就是剧本的姓(诸葛亮入诸葛氏)：以前等任官时才立，没姓的(扬古利)被安上模板起的家族名与姓
                string family = WorldMapData.PickParts(person.fam);
                if (string.IsNullOrEmpty(family)) actor.GetModName().familyName = "";
                // 后妃的娘家人(杨国忠之于杨贵妃)：入她那一家，外戚才连得上(见 ConsortKinSystem)
                Actor house = person.sameHouse >= 0 && person.sameHouse < polity.kinByIndex.Count ? polity.kinByIndex[person.sameHouse] : null;
                if (house?.clan != null) actor.setClan(house.clan);
                if (actor.clan == null) World.world.clans.newClan(actor, true);
                if (string.IsNullOrEmpty(family))
                {
                    actor.GetModName().familyName = "";
                    NameClan(actor.clan, WorldMapData.PickParts(person.giv));
                }
                actor.GetModName().SetName(actor);
                polity.famous.Add((actor, person));
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][世界地图] {polity.name} 名人 {person.giv} 失败: {exception.Message}");
            }
        }
    }

    // 进世界后(各国有城、有军、有官署了)：名臣任中枢要职，名将统一支军或坐镇一城，并记成名将名臣(照剧本的良才、名、国柱)
    private static void SeatFamous(Kingdom kingdom, Empire empire, List<(Actor actor, WorldMapFamous person)> people)
    {
        CenterOffice center = empire?.data?.centerOffice;
        var used = new HashSet<long>();
        bool OfficeFree(long id) => !used.Contains(id) && OfficeManager.Offices.TryGetValue(id, out OfficeObject office) &&
                                    office.GetActor() == null;
        FamousTalentSystem.Quiet = true;
        try
        {
            foreach ((Actor actor, WorldMapFamous person) in people)
            {
                if (actor?.data == null || actor.isRekt() || !actor.isAlive()) continue;
                try
                {
                    TalentKind kind = person.kind == "general" ? TalentKind.General : TalentKind.Minister;
                    for (int i = 0; i <= Mathf.Clamp(person.rank, 0, 2); i++) FamousTalentSystem.Bestow(actor, kind);
                    switch (person.post)
                    {
                        case "chief":
                        case "core":
                        case "div":
                        {
                            List<long> pool = person.post == "div" ? center?.Divisions : center?.CoreOffices;
                            long id = pool == null ? -1L
                                : person.post == "chief" && pool.Count > 0 && OfficeFree(pool[0]) ? pool[0]
                                : pool.FirstOrDefault(OfficeFree);
                            if (pool != null && pool.Contains(id) && OfficeManager.Offices.TryGetValue(id, out OfficeObject office))
                            {
                                office.SetActor(actor);
                                used.Add(id);
                            }
                            break;
                        }
                        case "general":
                        {
                            CorpsData corps = CorpsSystem.Of(kingdom).Where(entry => entry.general_id < 0 && !entry.militia && entry.order_id < 0)
                                .OrderByDescending(entry => entry.strength).FirstOrDefault();
                            if (corps != null) CorpsSystem.AssignGeneral(kingdom, corps, actor);
                            break;
                        }
                        default:
                        {
                            City city = _famousCities.TryGetValue(person.post, out City found) ? found : null;
                            if (city?.data == null || city.isRekt() || city.kingdom == null) break;
                            if (actor.kingdom != city.kingdom) actor.joinKingdom(city.kingdom);
                            actor.joinCity(city);
                            city.GetOffice()?.SetActor(actor);
                            if (city.leader != actor) city.setLeader(actor, true);
                            break;
                        }
                    }
                }
                catch (Exception exception)
                {
                    LogService.LogWarning($"[EmpireCraft][世界地图] 名人 {actor.getName()} 任职失败: {exception.Message}");
                }
            }
        }
        finally
        {
            FamousTalentSystem.Quiet = false;
        }
    }

    // 老王朝(前史里开国早于开局年)：国祚接着算(dynastic_age_offset)，不算新开国、没有开国气象；王朝积弊取剧本给的，
    // 没给照同岁数最少该有的；六弊取剧本给的，没给的补到积弊对应的底线
    private static void ApplyDecline(List<PlannedPolity> polities)
    {
        foreach (PlannedPolity polity in polities)
        {
            Empire empire = polity.empire;
            if (empire?.data == null || empire.isRekt() || !DynasticCycleSystem.Applies(empire)) continue;
            WorldMapDynasty dynasty = string.IsNullOrEmpty(polity.data.dynasty) ? null
                : WorldMapData.Dynasties.FirstOrDefault(item => item.id == polity.data.dynasty && item.rulers.Count > 0);
            var decline = polity.data.decline ?? new Dictionary<string, float>();
            // 前史里一个朝代跨了几个王室的(拜占庭、神圣罗马、东汉接西汉)，剧本另给"age"：现王室立了多少年
            int age = decline.TryGetValue("age", out float setAge) ? Mathf.RoundToInt(setAge)
                : dynasty == null || _era == null ? 0 : Math.Max(0, _era.year - dynasty.rulers.Min(reign => reign.a));
            if (age <= 0 && decline.Count == 0) continue;
            ConstitutionalEconomyState state = empire.data.constitutional_economy ??= new ConstitutionalEconomyState();
            double now = World.world.getCurWorldTime();
            state.dynastic_since = now;
            state.dynastic_age_offset = age;
            state.last_dynastic_update = now;
            if (age > Empire.FounderGraceYears) empire.data.inherited_decline = true;
            float strain = decline.TryGetValue("strain", out float given) ? given : DynasticCycleRules.AgeFloor(age);
            state.dynastic_strain = Mathf.Clamp(Mathf.Max(state.dynastic_strain, strain), 0f, 100f);
            empire.data.maladies ??= new Dictionary<string, float>();
            float floor = MaladySystem.Floor(empire);
            foreach (string key in MaladySystem.All)
            {
                float value = decline.TryGetValue(key, out float set) ? set : floor;
                empire.data.maladies[key] = Mathf.Clamp(Mathf.Max(MaladySystem.Get(empire, key), value), 0f, 100f);
            }
            MonarchyLegitimacy.Invalidate(empire);
            LogService.LogInfo($"[EmpireCraft][世界地图] {empire.GetEmpireFullName()} 国祚 {age} 年，王朝积弊 {state.dynastic_strain:0}，" +
                               $"六弊均 {MaladySystem.Average(empire):0}");
        }
    }

    private static void ApplyRelations(List<WorldMapRelation> relations, Dictionary<string, Kingdom> realms)
    {
        int done = 0;
        foreach (WorldMapRelation relation in relations)
        {
            if (!realms.TryGetValue(relation.a ?? "", out Kingdom a) || !realms.TryGetValue(relation.b ?? "", out Kingdom b) ||
                a?.data == null || b?.data == null || a.isRekt() || b.isRekt())
            {
                LogService.LogInfo($"[EmpireCraft][世界地图] 开局外交 {relation.a}→{relation.b} {relation.kind}：国不在");
                continue;
            }
            WarGoal goal = Enum.TryParse(relation.goal ?? "", out WarGoal parsed) ? parsed : WarGoal.夺城;
            string reason = DiplomacyActions.ApplyScenario(relation.kind, a, b, goal, relation.value);
            if (reason == null) done++;
            else LogService.LogInfo($"[EmpireCraft][世界地图] 开局外交 {a.GetKingdomName()}→{b.GetKingdomName()} {relation.kind} 没成：{reason}");
        }
        LogService.LogInfo($"[EmpireCraft][世界地图] 开局外交：{done}/{relations.Count} 条");
    }

    // 名人坐镇的城(城 id → 城)：收尾时清掉剧本的城表前记下
    private static readonly Dictionary<string, City> _famousCities = new();

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
        // 中文界面：原版按种族起的名(兽人的"Zoko Bak")混进来就成了"Zoko Bak乌吉斯古楞"，拉丁字母的名、姓都不要，
        // 名按本国文化重起，姓随后取自家族(见 BuildPolity)
        if (WorldMapData.ChineseUi)
        {
            if (Latin(name.familyName)) name.familyName = null;
            if (Latin(name.firstName)) name.firstName = null;
        }
        if (!name.hasFirstName(actor) && native != null)
        {
            string first = CulturePatch.GetOnomasticDataSafe(native, MetaType.Unit)
                ?.generateName(actor.isSexMale() ? ActorSex.Male : ActorSex.Female);
            if (!string.IsNullOrEmpty(first)) actor.SetFirstName(first.UseLocalizedNameSeparator());
        }
    }

    private static bool Latin(string text) => !string.IsNullOrEmpty(text) && text.Any(c => c < 128 && char.IsLetter(c));

    // 这一国王室的家族名：剧本写的姓，没写取前史里本朝的家族名(杜洛、药罗葛)
    internal static string HouseOf(WorldMapPolity polity)
    {
        string family = WorldMapData.PickParts(polity?.ruler?.fam);
        if (!string.IsNullOrEmpty(family) || string.IsNullOrEmpty(polity?.dynasty)) return family;
        return WorldMapData.PickParts(WorldMapData.Dynasties.FirstOrDefault(dynasty => dynasty.id == polity.dynasty)?.fam);
    }

    // 原版氏族改叫这个家族名(某某氏/家)；宗族另由 WorldMapHistory.NameHouse 改
    private static void NameClan(Clan clan, string house)
    {
        if (clan?.data == null || string.IsNullOrWhiteSpace(house)) return;
        clan.data.name = OverallHelperFunc.JoinNameParts(house, NeoModLoader.General.LM.Get("Clan"));
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

    // 目标格所在区块附近找一块空地：先找四周都还没城的，再退一步只要本区块空着。
    // 平地优先(用户 2026-10-29："为什么城市会建造在山上")：以前丘陵、雪丘也算能建的地，又在找到的第一圈就停，
    // 克什米尔谷地这类被山夹着的城常落在山坡上；现在在找到的那一圈再往外看两圈，按平地多少、山多少、离史实位置多远挑
    private const int MinFlat = 16;

    private static TileZone FindZone(Vector2Int tile, bool filler = false)
    {
        int zx = tile.x / 8, zy = tile.y / 8;
        for (int pass = 0; pass < 2; pass++)
        {
            int limit = pass == 0 ? 3 : 6;
            TileZone best = null;
            int bestScore = int.MinValue, stopAt = int.MaxValue;
            for (int radius = 0; radius <= limit && radius <= stopAt; radius++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                for (int dy = -radius; dy <= radius; dy++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != radius) continue;
                    TileZone zone = ZoneAt(zx + dx, zy + dy);
                    if (zone == null || zone.city != null || _usedZones.Contains(zone)) continue;
                    if (filler && !_fillerSites.Contains(WorldMapZones.IndexOf(zone))) continue;
                    if (pass == 0 && zone.neighbours_all.Any(near => near.city != null)) continue;
                    int land = 0, flat = 0, rough = 0;
                    foreach (WorldTile t in zone.tiles)
                    {
                        if (Buildable(t))
                        {
                            land++;
                            if (Flat(t)) flat++;
                        }
                        else if (t?.Type != null && (t.Type.mountains || t.Type.summit)) rough++;
                    }
                    if (land < 32 || !Buildable(zone.centerTile)) continue;
                    int score = flat * 3 + land - rough * 2 - (Mathf.Abs(dx) + Mathf.Abs(dy)) * 30 - (flat < MinFlat ? 120 : 0);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = zone;
                    }
                }
                if (best != null && stopAt == int.MaxValue) stopAt = radius + 1;
            }
            if (best != null) return best;
        }
        return null;
    }

    // 平地：能建、不是丘陵雪丘碎石
    private static bool Flat(WorldTile tile) =>
        Buildable(tile) && !tile.Type.rocks && tile.Type.id != "hills" && tile.Type.id != "snow_hills" && tile.main_type?.id != "hills";

    // 城心一带仍是山坡(四面是山的谷地城)：城心四五格内的山、丘陵平成河谷台地，城建在平地上
    private static void MakeValley(TileZone zone)
    {
        WorldTile center = zone?.centerTile;
        if (center == null || zone.tiles.Count(Flat) >= MinFlat * 2) return;
        TopTileType grass = AssetManager.top_tiles.get("grass_high") ?? AssetManager.top_tiles.get("grass_low");
        if (grass == null) return;
        for (int dx = -6; dx <= 6; dx++)
        for (int dy = -6; dy <= 6; dy++)
        {
            float r = 4.2f + 0.9f * Mathf.Sin(dx * 1.7f + dy * 0.9f);
            if (dx * dx + dy * dy > r * r) continue;
            WorldTile tile = World.world.GetTileSimple(center.x + dx, center.y + dy);
            TileTypeBase type = tile?.Type;
            if (type == null || type.liquid || type.lava || Flat(tile)) continue;
            if (!(type.mountains || type.summit || type.rocks || type.id == "hills" || type.id == "snow_hills")) continue;
            tile.setTileTypes("soil_high", grass);
            tile.Height = tile.Type.height_min;
        }
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
            // 只改荒漠，沙滩留着(沙地是沙滩，不算荒漠，见 WildernessSystem.ComputeMarks)
            if (biome != "biome_desert") continue;
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

    private static float TargetPeople(PlannedCity planned, City city)
    {
        float fallback = Households[Mathf.Clamp(planned.rank, 0, 3)] * CityPopulationSystem.PeoplePerSlot(city);
        string region = planned.region?.id ?? planned.data?.region;
        float real = WorldMapData.RegionPopulation(region, _era.year);
        if (real <= 0f) return fallback;
        if (_regionWeight.Count == 0)
            foreach (PlannedCity other in _cities)
            {
                string key = other.region?.id ?? other.data?.region;
                if (key == null) continue;
                _regionWeight.TryGetValue(key, out float sum);
                _regionWeight[key] = sum + RankWeight[Mathf.Clamp(other.rank, 0, 3)];
            }
        float total = _regionWeight.TryGetValue(region, out float weight) && weight > 0f ? weight : 1f;
        return Mathf.Clamp(real * RankWeight[Mathf.Clamp(planned.rank, 0, 3)] / total / PeopleScale, MinPeople, MaxPeople);
    }

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
                    {
                        float people = TargetPeople(planned, city);
                        float missing = people - CityPopulationSystem.GetTotal(city);
                        if (missing > 0f)
                            CityPopulationSystem.AddBackground(city, SocialClass.Peasant, planned.culture ?? "",
                                city.leader?.asset?.id ?? SpeciesFor(planned.culture), IdeologyPopulationSystem.GetDominant(city), missing);
                        _develop.Enqueue((city, people));
                    }
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
    // 补空小国的名字不许撞上剧本里的真地名、真国名(用户 2026-10-29 "有些位置是不对的"：华夏起名模板里有"朝鲜""夏"这类古国名，
    // 补在黑龙江、中亚的小国就叫了朝鲜、夏，看着像剧本把朝鲜、西夏摆错了地方)；撞上就重起，起几次都撞就用原版起的名
    private static HashSet<string> _reservedNames;

    private static HashSet<string> ReservedNames()
    {
        if (_reservedNames != null) return _reservedNames;
        var names = new HashSet<string>();
        void Add(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            foreach (string part in text.Split('|')) if (!string.IsNullOrWhiteSpace(part)) names.Add(part.Trim());
        }
        foreach (WorldMapRegion region in WorldMapData.Regions)
        {
            Add(region.zh); Add(region.tw); Add(region.title);
        }
        foreach (WorldMapCity city in WorldMapData.Cities)
        {
            Add(city.zh); Add(city.tw);
            foreach (string alt in city.alt ?? new List<string>()) Add(alt.Substring(alt.IndexOf('|') + 1));
        }
        foreach (WorldMapCore core in WorldMapData.Cores) { Add(core.zh); Add(core.tw); }
        foreach (WorldMapEra era in WorldMapData.Eras)
            foreach (WorldMapPolity polity in era.polities) { Add(polity.zh); Add(polity.tw); }
        foreach (WorldMapDynasty dynasty in WorldMapData.Dynasties) { Add(dynasty.zh); Add(dynasty.tw); Add(dynasty.full); }
        return _reservedNames = names;
    }

    private static string FreshName(Culture native, MetaType type)
    {
        string name = null;
        for (int i = 0; i < 8; i++)
        {
            name = Generate(native, type);
            if (string.IsNullOrWhiteSpace(name) || !ReservedNames().Contains(name.Trim())) return name;
        }
        return null;
    }

    private static void RandomNames(PlannedCity planned, PlannedPolity polity)
    {
        Culture native = NativeCulture(planned.culture, null) ?? planned.city.getCulture();
        string city = FreshName(native, MetaType.City);
        planned.name = string.IsNullOrWhiteSpace(city) ? planned.city.data.name : city;
        if (planned != polity.capital) return;
        string kingdom = FreshName(native, MetaType.Kingdom);
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
    private const int MinFillerZones = 8;
    private static readonly HashSet<int> _fillerSites = new();

    // 地都分完了：只挨着一家的补空小国并进那一国(规则见 ScenarioFillerRules)。排在立法理之前，城直接算那国最近的城所在的地区，
    // 不留下一块只属于小国的法理；预设里被大国包着的"桂国""代国"这类飞地就没有了
    private static void AbsorbEnclosedFillers()
    {
        if (_era?.exact == true) return;
        List<PlannedPolity> fillers = _polities.Where(polity => polity.capital?.random == true && !polity.tribe && polity.kingdom?.data != null).ToList();
        if (fillers.Count == 0) return;
        var byKingdom = new Dictionary<Kingdom, PlannedPolity>();
        foreach (PlannedPolity polity in _polities)
            if (polity.kingdom?.data != null) byKingdom[polity.kingdom] = polity;
        var neighbours = new Dictionary<PlannedPolity, HashSet<PlannedPolity>>();
        foreach (PlannedPolity filler in fillers)
        {
            var around = new HashSet<PlannedPolity>();
            foreach (PlannedCity planned in filler.cities)
            {
                if (planned.city?.zones == null) continue;
                foreach (TileZone zone in planned.city.zones)
                    foreach (TileZone next in zone.neighbours_all ?? global::System.Array.Empty<TileZone>())
                    {
                        Kingdom other = next?.city?.kingdom;
                        if (other != null && other != filler.kingdom && byKingdom.TryGetValue(other, out PlannedPolity polity)) around.Add(polity);
                    }
            }
            neighbours[filler] = around;
        }
        int merged = 0;
        foreach (KeyValuePair<PlannedPolity, PlannedPolity> pair in ScenarioFillerRules.Absorptions(neighbours))
            Guard($"补空小国 {pair.Key.name} 并入 {pair.Value.name}", () =>
            {
                MergeFiller(pair.Key, pair.Value);
                merged++;
            });
        World.world.zone_calculator?.setDrawnZonesDirty();
        LogService.LogInfo($"[EmpireCraft][世界地图] 补空小国并入大国 {merged} 个");
    }

    private static void MergeFiller(PlannedPolity filler, PlannedPolity target)
    {
        Kingdom gone = filler.kingdom;
        foreach (PlannedCity planned in filler.cities.ToList())
        {
            if (planned.city == null || planned.city.isRekt()) continue;
            PlannedCity near = target.cities.OrderBy(city => (city.tile - planned.tile).sqrMagnitude).FirstOrDefault();
            if (near == null) continue;
            planned.region = near.region;
            planned.data.region = near.region.id;
            planned.owner = target;
            planned.random = false;
            planned.city.joinAnotherKingdom(target.kingdom);
            target.cities.Add(planned);
        }
        filler.cities.Clear();
        _polities.Remove(filler);
        // 国家没有城、没有人就由原版移除
        if (gone?.units != null)
            foreach (Actor unit in gone.units.ToList())
                if (unit?.data != null && !unit.isRekt() && unit.isAlive()) unit.die(true, AttackType.Other, false, false);
    }

    // 无人区划完：地块全成了碎片的补空小国不立国，能并给挨着的城的地并过去，并不了的留空(规则见 ScenarioFillerRules)
    private static void DissolveShatteredFillers(List<Kingdom> fillers)
    {
        int zonesWide = MapBox.width / 8, gone = 0;
        foreach (Kingdom kingdom in fillers)
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.cities == null || kingdom.cities.Count == 0) continue;
            List<TileZone> zones = kingdom.cities.Where(city => city?.zones != null).SelectMany(city => city.zones).ToList();
            if (!ScenarioFillerRules.Shattered(zones.Select(zone => zone.id), zonesWide)) continue;
            List<KingdomTitle> titles = kingdom.cities.Select(city => city.GetTitle()).Where(title => title != null).Distinct().ToList();
            foreach (City city in kingdom.cities.ToList()) CityDissolution.Dissolve(city, wipeKingdom: true);
            foreach (KingdomTitle title in titles)
                if (!title.isRekt() && !title.getCities().Any(city => city?.data != null && !city.isRekt())) ModClass.KINGDOM_TITLE_MANAGER.dissolveTitle(title);
            using (CityExpansionPatch.Manual())
                foreach (TileZone zone in zones)
                {
                    if (zone.city != null) continue;
                    City next = zone.neighbours_all?.Select(other => other?.city).FirstOrDefault(city => city?.data != null && !city.isRekt());
                    next?.addZone(zone);
                }
            gone++;
        }
        if (gone == 0) return;
        WildernessSystem.RequestReattach();
        World.world.zone_calculator?.setDrawnZonesDirty();
        LogService.LogInfo($"[EmpireCraft][世界地图] 被切碎的补空小国不立国 {gone} 个");
    }

    private static void PlanFillers()
    {
        _fillerStart = _polities.Count;
        // 照原图分地(从当前世界导入的剧本)：原图的国家都写进剧本了，不补小国、部落
        if (_era?.exact == true) return;
        // 地形缓存按区块编号存，可能还是上一个世界的(城心改成绿洲也没刷新)，先清掉再判荒地
        TerrainSystem.ResetWorldState();
        WorldMapZones.Build();
        _fillerSites.Clear();
        _fillerSites.UnionWith(WorldMapZones.FillerSites(MinFillerZones));
        PlanTribes();
        var centers = _cities.Where(city => city.city != null && !city.random && CultureService.IsValidCulture(city.culture))
            .GroupBy(city => city.region.id)
            .Select(group => (culture: group.First().culture, x: (float)group.Average(city => city.tile.x),
                y: (float)group.Average(city => city.tile.y)))
            .ToList();
        if (centers.Count == 0) return;
        // 文化先取走得到的最近的剧本城(按地形代价、不过边墙：长城外的取草原那边的)，走不到的才按直线取最近的地区
        List<PlannedCity> anchors = _cities.Where(city => city.city != null && !city.random && CultureService.IsValidCulture(city.culture)).ToList();
        int[] reach = WorldMapZones.NearestOf(anchors.Select(city => WorldMapZones.IndexOf(city.tile)).ToList());
        // 原始部落圈里不另补小国(圈里只有部落)，补空时离部落也要够远
        List<int> gaps = WorldMapZones.Gaps(GapCost, MaxFillers[Mathf.Clamp(_sizeIndex, 0, MaxFillers.Length - 1)], index =>
        {
            TileZone zone = WorldMapZones.Zone(index);
            return _fillerSites.Contains(index) && zone?.centerTile != null && !_usedZones.Contains(zone) && !_tribeArea.Contains(index) && Buildable(zone.centerTile) &&
                   zone.tiles.Count(Buildable) >= 32 && BarrenLandSystem.KindOf(zone) == BarrenLandSystem.None &&
                   !WorldMapWalls.OnPlanned(zone);
        }, _tribeSeeds);
        var fillers = new List<PlannedPolity>();
        foreach (int index in gaps)
        {
            TileZone zone = WorldMapZones.Zone(index);
            var tile = new Vector2Int(zone.centerTile.x, zone.centerTile.y);
            int from = index >= 0 && index < reach.Length ? reach[index] : -1;
            string culture = from >= 0
                ? anchors[from].culture
                : centers.OrderBy(center => (center.x - tile.x) * (center.x - tile.x) + (center.y - tile.y) * (center.y - tile.y)).First().culture;
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

    // 原始部落圈(剧本编辑器「部落」工具)：圈里挑能建城的空地(离别的城至少 TribeSpacing 块)，各立一个那种文化的部落，
    // 照补空小国一起建；进世界后改原始政体(华夏一系为华夏原始政体，见 MakeTribes)。count 为 0 时按圈里空地多少定(每 25 块一个，1~12)
    private const int TribeSpacing = 3;
    private static readonly HashSet<int> _tribeArea = new();
    private static readonly List<int> _tribeSeeds = new();

    private static void PlanTribes()
    {
        _tribeArea.Clear();
        _tribeSeeds.Clear();
        if (_era?.tribes == null || _era.tribes.Count == 0 || _frame == null) return;
        _frame.Bounds(_size, out float bottom, out float degrees);
        int zw = MapBox.width / 8, zh = MapBox.height / 8;
        var taken = _cities.Select(city => city.tile).ToList();
        int made = 0;
        foreach (WorldMapTribes area in _era.tribes)
        {
            if (area?.at == null || area.at.Length < 2 || area.radius <= 0f || !CultureService.IsValidCulture(area.culture)) continue;
            var candidates = new List<(TileZone zone, Vector2Int tile)>();
            for (int zy = 0; zy < zh; zy++)
            for (int zx = 0; zx < zw; zx++)
            {
                var tile = new Vector2Int(zx * 8 + 4, zy * 8 + 4);
                float lon = _frame.west + tile.x * degrees, lat = bottom + tile.y * degrees;
                float dx = (lon - area.at[0]) * Mathf.Cos(lat * Mathf.Deg2Rad), dy = lat - area.at[1];
                if (dx * dx + dy * dy > area.radius * area.radius) continue;
                TileZone zone = ZoneAt(zx, zy);
                if (zone != null) _tribeArea.Add(WorldMapZones.IndexOf(zone));
                if (zone?.centerTile == null || zone.city != null || _usedZones.Contains(zone) || !Buildable(zone.centerTile) ||
                    zone.tiles.Count(Buildable) < 32) continue;
                candidates.Add((zone, tile));
            }
            int want = area.count > 0 ? area.count : Mathf.Clamp(Mathf.RoundToInt(candidates.Count / 25f), 1, 12);
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = Randy.randomInt(0, i + 1);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }
            int placed = 0;
            foreach ((TileZone zone, Vector2Int tile) in candidates)
            {
                if (placed >= want) break;
                if (taken.Any(other => (other - tile).sqrMagnitude < TribeSpacing * 8 * TribeSpacing * 8)) continue;
                string id = "tribe_" + made;
                var region = new WorldMapRegion { id = id, culture = area.culture };
                var planned = new PlannedCity
                {
                    data = new WorldMapCity { id = id, region = id, rank = 3 }, rank = 3, region = region, tile = tile,
                    culture = area.culture, random = true
                };
                var polity = new PlannedPolity { culture = area.culture, capital = planned, tribe = true };
                planned.owner = polity;
                polity.cities.Add(planned);
                _cities.Add(planned);
                _polities.Add(polity);
                _usedZones.Add(zone);
                _tribeSeeds.Add(WorldMapZones.IndexOf(zone));
                taken.Add(tile);
                placed++;
                made++;
            }
        }
        if (made > 0) LogService.LogInfo($"[EmpireCraft][世界地图] 原始部落圈立了 {made} 个部落");
    }

    // 部落改原始政体(华夏一系为华夏原始政体)
    private static void MakeTribes(List<Kingdom> kingdoms)
    {
        foreach (Kingdom kingdom in kingdoms)
        {
            if (kingdom?.data == null || kingdom.isRekt()) continue;
            string culture = CultureService.GetRealmCulture(kingdom);
            kingdom.SetRegimeType(InstitutionSystem.GetCultureLine(culture) == "Huaxia" ? RegimeType.HuaxiaOrigin : RegimeType.Origin);
            kingdom.LoadRegime();
            EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(kingdom);
        }
    }

    // 剧本编辑器刷的地块归属(见 ScenarioCells)：刷了的地块先归那国(或那块王国法理)离它最近的城，铺辖地时别家不再领；
    // "c:城编号" 直接归那座城(从当前世界导入的剧本照原图写的，见 ScenarioWorldExport)。归城的这些地块记进 PaintedCityZones，
    // 头一回划无人区时留给城(见 WildernessSystem.Regroup)，原图城的地不被划走
    internal static readonly HashSet<int> PaintedCityZones = new();

    // 照原图归城的地块此刻归哪座城
    private static Dictionary<int, long> PaintedCityOwners()
    {
        var owners = new Dictionary<int, long>();
        List<TileZone> zones = World.world?.zone_calculator?.zones;
        if (zones == null) return owners;
        foreach (TileZone zone in zones)
            if (zone != null && PaintedCityZones.Contains(zone.id) && zone.city?.data != null) owners[zone.id] = zone.city.data.id;
        return owners;
    }

    private static int PaintTerritory()
    {
        Dictionary<int, string> paint = ScenarioCells.ToMap(_era?.paint);
        if (paint.Count == 0 || _frame == null) return 0;
        _frame.Bounds(_size, out float bottom, out float degrees);
        var byPolity = new Dictionary<string, List<PlannedCity>>();
        var byRegion = new Dictionary<string, List<PlannedCity>>();
        var byCity = new Dictionary<string, List<PlannedCity>>();
        PaintedCityZones.Clear();
        foreach (PlannedCity city in _cities.Where(city => city.city != null && !city.city.isRekt()))
        {
            if (!string.IsNullOrEmpty(city.data?.id)) byCity[city.data.id] = new List<PlannedCity> { city };
            if (city.owner?.data != null)
                (byPolity.TryGetValue(city.owner.data.id, out var list) ? list : byPolity[city.owner.data.id] = new List<PlannedCity>()).Add(city);
            (byRegion.TryGetValue(city.region.id, out var members) ? members : byRegion[city.region.id] = new List<PlannedCity>()).Add(city);
        }
        int zw = MapBox.width / 8, zh = MapBox.height / 8, claimed = 0;
        var owner = new PlannedCity[zw * zh];
        var exact = new bool[zw * zh];
        for (int zy = 0; zy < zh; zy++)
        for (int zx = 0; zx < zw; zx++)
        {
            var tile = new Vector2Int(zx * 8 + 4, zy * 8 + 4);
            if (!paint.TryGetValue(ScenarioCells.Key(_frame.west + tile.x * degrees, bottom + tile.y * degrees, ScenarioCells.LandStep),
                    out string key) || key.Length < 3) continue;
            bool exactCity = key.StartsWith("c:");
            List<PlannedCity> owners = key.StartsWith("p:") ? byPolity.TryGetValue(key.Substring(2), out var p) ? p : null
                : key.StartsWith("r:") ? byRegion.TryGetValue(key.Substring(2), out var r) ? r : null
                : exactCity ? byCity.TryGetValue(key.Substring(2), out var c) ? c : null : null;
            TileZone zone = ZoneAt(zx, zy);
            // 照原图归城的地块：水面多的(原图里城也领着的港湾)也归它
            if (owners == null || owners.Count == 0 || zone?.centerTile == null || zone.city != null ||
                !exactCity && zone.centerTile.Type?.liquid == true) continue;
            owner[zy * zw + zx] = owners.OrderBy(city => (city.tile - tile).sqrMagnitude).First();
            exact[zy * zw + zx] = exactCity;
        }
        SoftenPaint(owner, zw, zh, ScenarioCells.LandStep / (degrees * 8f));
        for (int zy = 0; zy < zh; zy++)
        for (int zx = 0; zx < zw; zx++)
        {
            PlannedCity city = owner[zy * zw + zx];
            if (city == null) continue;
            TileZone zone = ZoneAt(zx, zy);
            city.city.addZone(zone);
            if (exact[zy * zw + zx]) PaintedCityZones.Add(zone.id);
            claimed++;
        }
        if (claimed > 0) LogService.LogInfo($"[EmpireCraft][世界地图] 剧本刷的地块归属 {claimed} 块");
        return claimed;
    }

    // 地块归属按 0.2° 的格子记，比例尺大的图一格盖好几块地，城界成了整块整块的方格，平滑国界只管一块地的范围，只能把方格的角磨圆
    // (群友 2026-11-04 截图"变成这种稍微圆弧的方块了，正常情况下应该是斜线才对")。一格盖不止一块地时，边界上的地块归周围最多的那座城，
    // 方格的台阶就磨成斜线；没刷的地块(空、水面)不算也不占。图的比例尺够细、一格不到 1.25 块地的不动
    private const int SoftenPasses = 2, SoftenMaxRadius = 4;

    private static void SoftenPaint(PlannedCity[] owner, int zw, int zh, float zonesPerCell)
    {
        if (zonesPerCell < 1.25f) return;
        int radius = Mathf.Clamp(Mathf.CeilToInt(zonesPerCell), 1, SoftenMaxRadius);
        var counts = new Dictionary<PlannedCity, int>();
        for (int pass = 0; pass < SoftenPasses; pass++)
        {
            var next = (PlannedCity[])owner.Clone();
            for (int zy = 0; zy < zh; zy++)
            for (int zx = 0; zx < zw; zx++)
            {
                PlannedCity self = owner[zy * zw + zx];
                if (self == null) continue;
                counts.Clear();
                for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int x = zx + dx, y = zy + dy;
                    if (x < 0 || y < 0 || x >= zw || y >= zh || dx * dx + dy * dy > radius * radius) continue;
                    PlannedCity around = owner[y * zw + x];
                    if (around != null) counts[around] = counts.TryGetValue(around, out int count) ? count + 1 : 1;
                }
                if (counts.Count < 2) continue;
                PlannedCity best = self;
                int most = counts[self];
                foreach (KeyValuePair<PlannedCity, int> pair in counts)
                    if (pair.Value > most)
                    {
                        most = pair.Value;
                        best = pair.Key;
                    }
                next[zy * zw + zx] = best;
            }
            Array.Copy(next, owner, owner.Length);
        }
    }

    // 补的小国先按小城的预算铺自己的辖地，再把剩下有陆地的区块(山麓、沿海零碎地块)分给走得到的最近的城，
    // 大山腹地与整块的荒漠、冻土留空，进世界后划成无人区(见 WorldMapZones.FillAll、WildernessSystem)。
    // 不看每城地块上限(用户 2026-11-04 "所有地盘都是规定好的""也没有什么扩张的机会")：剧本开局就定好地盘，之后领土扩张法则也关着(见 SetupLaws)，
    // 以前周围的城都满了上限(铺满时 1.5 倍)，补空的小国也用完了名额，草原上成片的地块留空、再没有人来领
    private static void FillTerritory()
    {
        using CityExpansionPatch.ManualScope manual = CityExpansionPatch.Manual();
        // 照原图分地(从当前世界导入的剧本)：刷的地块以外不再铺满，照常判无人区
        if (_era?.exact == true)
        {
            // 原图没挂法理的无人区照旧不挂：不按州府锚点给无人区定默认归属
            _wildAnchors.Clear();
            return;
        }
        var seeds = _cities.Where(planned => planned.random && planned.city != null && !planned.city.isRekt())
            .Select(planned => (planned.city, TerritoryBudget[3], TerritoryCap[3]))
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
        claimed += WorldMapZones.FillAll((city, zone) => city.addZone(zone));
        World.world.zone_calculator?.setDrawnZonesDirty();
        LogService.LogInfo($"[EmpireCraft][世界地图] 铺满又分了 {claimed} 块");
        AnchorWilderness();
        _cityTerrainReach.Clear();
    }

    #endregion

    // 开局辖地：各城按地形代价同时往外铺，国界停在山脊、大漠、海峡上(见 WorldMapZones.Fill)
    // 开局定下的长城走向(化简成格坐标折线，见 WorldMapWalls.Plan)：铺辖地、分行政区都按它分两边，随存档(见 FrontierWalls)
    private static List<List<Vector2>> _wallLines;

    private static void SpreadTerritory()
    {
        using CityExpansionPatch.ManualScope manual = CityExpansionPatch.Manual();
        // 城都立好了：先把长城的走向定下(沿山脊、河谷弯折，不在两点之间拉直线；用户 10-30 "长城也不要这么直，要自然一点")，
        // 之后铺辖地、补空、铺满、分行政区都照这条实际走向分两边，进世界后也照它铺墙
        if (_era?.walls is { Count: > 0 })
        {
            _wallLines = WorldMapWalls.Plan(_era, _frame, _size);
            WorldMapZones.SetWalls(_wallLines);
        }
        var seeds = _cities.Where(planned => planned.city != null && !planned.city.isRekt())
            .Select(planned => (planned.city, TerritoryBudget[Mathf.Clamp(planned.rank, 0, 3)],
                TerritoryCap[Mathf.Clamp(planned.rank, 0, 3)]))
            .ToList();
        // 剧本编辑器刷的地块先归刷给的那国或那块法理
        int claimed = PaintTerritory();
        // 照原图分地(从当前世界导入的剧本)：只按刷的分，不再往外铺
        if (_era?.exact == true)
        {
            World.world.zone_calculator?.setDrawnZonesDirty();
            LogService.LogInfo($"[EmpireCraft][世界地图] 照原图分地 {claimed} 块");
            return;
        }
        // 建城时改过地块(绿洲、平出河谷)：按现在的地形重算区块代价
        WorldMapZones.Build();
        // 补了州府治所锚点的国家先按锚点分好各行政区的地
        claimed += PaintAdministrativeDefaults();
        claimed += AnchorTerritory();
        WorldMapZones.Fill(seeds, (city, zone) =>
        {
            city.addZone(zone);
            claimed++;
        });
        World.world.zone_calculator?.setDrawnZonesDirty();
        LogService.LogInfo($"[EmpireCraft][世界地图] 开局辖地铺了 {claimed} 块");
    }

    // 州府治所锚点(用户 10-30 "同一时代下你的行政区划的位置和形状不符合历史"，选"补州府治所锚点"，"网上多对比对比已有的各个时代的
    // 行政区划图")：剧本给国家的各行政区与都城直辖那一块补了当年州府治所的经纬度(divisions[].seats、polities[].seats，
    // 见 Tools/WorldMap/anchors_source.py)。铺辖地前所有的城连同这些锚点一起按地形代价分地(不过边墙、交界要有路口，见
    // WorldMapZones.Partition)：分给有锚点的国家某个行政区的地块，归那个行政区里离它最近的城(没有城的归都城一块)——行政区的位置、
    // 形状照当年的州府走，不再由区里寥寥几座城的远近决定；别的国家的城也一起分，锚点不越过它们的城抢地。没补锚点的国家照旧按城铺。
    // 治所沿用原有预算；历史范围通过默认地块赋值，不通过改变自动分区的预算塑形。
    private const float SeatBudget = 7f;

    // 锚点分地的标签：有锚点的国家每个行政区一个(都城直辖记作 -1 号)，别的国家整国一个；各标签的城
    private sealed class AnchorPlan
    {
        public readonly List<PlannedPolity> anchored = new();
        public readonly HashSet<PlannedPolity> set = new();
        public readonly List<(PlannedPolity polity, int division)> groups = new();
        public readonly Dictionary<(PlannedPolity, int), int> labels = new();
        public readonly List<(int index, int label, float budget)> seeds = new();
        public readonly List<(int index, int label)> roots = new();
        public readonly Dictionary<int, List<PlannedCity>> members = new();

        public int LabelOf(PlannedPolity polity, int division)
        {
            if (labels.TryGetValue((polity, division), out int label)) return label;
            labels[(polity, division)] = label = groups.Count;
            groups.Add((polity, division));
            return label;
        }

        // 分给这个标签的地块归哪几座城(没有城的归都城)；不是有锚点的国家的标签为 null
        public List<PlannedCity> CitiesOf(int label)
        {
            if (label < 0 || label >= groups.Count || !set.Contains(groups[label].polity)) return null;
            if (members.TryGetValue(label, out List<PlannedCity> cities) && cities.Count > 0) return cities;
            PlannedCity capital = groups[label].polity.capital;
            return capital?.city != null && !capital.city.isRekt() ? members[label] = new List<PlannedCity> { capital } : null;
        }
    }

    // 所有的城连同锚点作起点；城、锚点各能走多远
    private static AnchorPlan PlanAnchors(Func<PlannedCity, float> cityBudget, float seatBudget)
    {
        var plan = new AnchorPlan();
        plan.anchored.AddRange(_polities.Where(polity => polity.data != null && polity.cities.Any(city => city.city != null) &&
                                                         (polity.data.seats is { Count: > 0 } ||
                                                          polity.data.divisions?.Any(division => division.seats is { Count: > 0 }) == true)));
        if (plan.anchored.Count == 0 || _frame == null) return null;
        foreach (PlannedPolity polity in plan.anchored) plan.set.Add(polity);
        foreach (PlannedCity city in _cities.Where(city => city.city != null && !city.city.isRekt()))
        {
            int division = city.owner != null && plan.set.Contains(city.owner) ? DivisionOf(city.owner, city) : -2;
            int index = WorldMapZones.IndexOf(Seat(city)), label = plan.LabelOf(city.owner, division);
            plan.seeds.Add((index, label, cityBudget(city)));
            plan.roots.Add((index, label));
        }
        foreach (PlannedPolity polity in plan.anchored)
        {
            AddSeats(plan.seeds, polity.data.seats, plan.LabelOf(polity, -1), seatBudget);
            for (int d = 0; d < (polity.data.divisions?.Count ?? 0); d++)
                AddSeats(plan.seeds, polity.data.divisions[d].seats, plan.LabelOf(polity, d), seatBudget);
        }
        // 各行政区的城(都城直辖：都城与不属于任何行政区的城)
        foreach (PlannedPolity polity in plan.anchored)
            foreach (PlannedCity city in polity.cities.Where(city => city.city != null && !city.city.isRekt()))
            {
                int label = plan.LabelOf(polity, DivisionOf(polity, city));
                (plan.members.TryGetValue(label, out var list) ? list : plan.members[label] = new List<PlannedCity>()).Add(city);
            }
        return plan;
    }

    // 同行政区内按可定居陆路的地形距离分给城市；不可达的地不跨山、跨海硬挂给直线最近的城市。
    // 无人区的行政锚点沿用原有距离规则；明确历史范围可用它记录虚拟行政归属，不强迫城市收地。
    private static readonly Dictionary<string, int[]> _cityTerrainReach = new();

    private static PlannedCity NearestOf(List<PlannedCity> cities, TileZone zone, int index, bool terrain = true)
    {
        if (terrain)
        {
            List<PlannedCity> available = cities.Where(city => !WorldMapZones.Behind(Seat(city), index)).ToList();
            if (available.Count == 0) return null;
            string key = string.Join(",", available.Select(city => city.city.id));
            if (!_cityTerrainReach.TryGetValue(key, out int[] owners))
            {
                // 大型自定剧本可能有许多不同的城群／墙侧组合，限制临时整图缓存的份数。
                if (_cityTerrainReach.Count >= 64) _cityTerrainReach.Clear();
                _cityTerrainReach[key] = owners = WorldMapZones.NearestOf(available.Select(city => WorldMapZones.IndexOf(Seat(city))).ToList(), true);
            }
            if (index < 0 || index >= owners.Length || owners[index] < 0 || owners[index] >= available.Count) return null;
            return available[owners[index]];
        }
        var at = new Vector2Int(zone.centerTile.x, zone.centerTile.y);
        return cities.Where(city => !WorldMapZones.Behind(Seat(city), index)).OrderBy(city => (city.tile - at).sqrMagnitude).FirstOrDefault();
    }

    private static Vector2Int Seat(PlannedCity city)
    {
        WorldTile tile = city.city?.getTile();
        return tile == null ? city.tile : new Vector2Int(tile.x, tile.y);
    }

    // 锚点分给某行政区、可那个行政区的城都在长城那边的地块(用户 10-30 "这个行政区都跨长城了"：盛唐关内道墙南的延、庆、鄜、坊诸州锚点
    // 分到的地，道里只有墙北的丰州、灵州，以前一股脑归了灵州，行政区跨过秦长城伸到长安北面)：归本国在墙这一侧、按地形走得到的最近的城
    // (这些地块离本国各城另走一遍，见 WorldMapZones.Partition)；墙这一侧本国也没有城的不归，留给铺辖地、铺满
    private const float StrayBudget = 60f;

    private static Dictionary<int, PlannedCity> SameSide(AnchorPlan plan, int[] owner, List<int> stray, bool wild)
    {
        var result = new Dictionary<int, PlannedCity>();
        foreach (IGrouping<PlannedPolity, int> group in stray.GroupBy(index => plan.groups[owner[index]].polity))
        {
            List<PlannedCity> cities = group.Key.cities.Where(city => city.city != null && !city.city.isRekt()).ToList();
            if (cities.Count == 0) continue;
            int[] near = WorldMapZones.Partition(cities.Select((city, n) => (WorldMapZones.IndexOf(city.tile), n, StrayBudget)).ToList(), wild);
            foreach (int index in group)
            {
                int n = near[index];
                if (n >= 0 && !WorldMapZones.Behind(Seat(cities[n]), index)) result[index] = cities[n];
            }
        }
        return result;
    }

    private static int AnchorTerritory()
    {
        AnchorPlan plan = PlanAnchors(city => TerritoryBudget[Mathf.Clamp(city.rank, 0, 3)], SeatBudget);
        if (plan == null) return 0;
        int[] owner = WorldMapZones.RootedPartition(plan.seeds, plan.roots);
        int claimed = 0;
        var stray = new List<int>();
        for (int i = 0; i < owner.Length; i++)
        {
            List<PlannedCity> cities = plan.CitiesOf(owner[i]);
            TileZone zone = WorldMapZones.Zone(i);
            if (cities == null || zone?.centerTile == null || zone.city != null) continue;
            PlannedCity city = NearestOf(cities, zone, i);
            if (city == null) { stray.Add(i); continue; }
            city.city.addZone(zone);
            claimed++;
        }
        foreach (KeyValuePair<int, PlannedCity> pair in SameSide(plan, owner, stray, false))
        {
            TileZone zone = WorldMapZones.Zone(pair.Key);
            if (zone == null || zone.city != null) continue;
            pair.Value.city.addZone(zone);
            claimed++;
        }
        LogService.LogInfo($"[EmpireCraft][世界地图] 州府治所锚点：{plan.anchored.Count} 国，分了 {claimed} 块");
        return claimed;
    }

    // 直接读取历史范围作为开局地块默认值；不参与平滑采样、不修改通用距离权重。
    private static readonly Dictionary<int, PlannedCity> _defaultAdministrativeCities = new();

    private static int PaintAdministrativeDefaults()
    {
        int count = WorldMapZones.Width * WorldMapZones.Height;
        _frame.Bounds(_size, out float bottom, out float degrees);
        int shapes = 0, claimed = 0;
        var existingCities = _cities.Where(city => city.city != null && !city.city.isRekt())
            .GroupBy(city => city.city).ToDictionary(group => group.Key, group => group.First());
        foreach (PlannedPolity polity in _polities.Where(polity => polity.data != null))
        for (int division = -1; division < (polity.data.divisions?.Count ?? 0); division++)
        {
            WorldMapDivision data = division >= 0 ? polity.data.divisions[division] : null;
            string name = data == null ? "_core" : (data.name ?? "").Split('|')[0];
            List<PlannedCity> cities = polity.cities.Where(city => city.city != null && !city.city.isRekt() &&
                DivisionOf(polity, city) == division).ToList();
            if (cities.Count == 0) continue;
            var polygons = new List<List<List<float[]>>>();
            var custom = data == null ? polity.data.boundary : data.boundary;
            if (custom != null) polygons.AddRange(custom);
            else foreach (WorldMapAdministrativeBoundary area in WorldMapAdministrativeBoundaries.For(_era, polity.data.id, name))
                polygons.AddRange(area.polygons);
            if (!AdministrativeBoundaryRules.Bounds(polygons, out float west, out float south, out float east, out float north)) continue;
            shapes++;
            for (int i = 0; i < count; i++)
            {
                if (_defaultAdministrativeCities.ContainsKey(i)) continue;
                float lon = _frame.west + (i % WorldMapZones.Width * 8 + 4) * degrees;
                float lat = bottom + (i / WorldMapZones.Width * 8 + 4) * degrees;
                if (lon < west || lon > east || lat < south || lat > north) continue;
                if (!AdministrativeBoundaryRules.Contains(polygons, lon, lat)) continue;
                TileZone zone = WorldMapZones.Zone(i);
                if (zone?.centerTile == null || zone.tiles_with_ground < 16) continue;
                if (zone.city != null)
                {
                    // 手绘地块、已有城心优先于默认值。
                    if (existingCities.TryGetValue(zone.city, out PlannedCity existing)) _defaultAdministrativeCities[i] = existing;
                    continue;
                }
                PlannedCity reachable = NearestOf(cities, zone, i);
                PlannedCity nearest = reachable ?? NearestOf(cities, zone, i, false);
                if (nearest?.city?.data == null) continue;
                _defaultAdministrativeCities[i] = nearest;
                if (reachable != null && WorldMapZones.SettledLand(i))
                {
                    nearest.city.addZone(zone);
                    if (zone.city == nearest.city) claimed++;
                }
            }
        }
        LogService.LogInfo($"[EmpireCraft][世界地图] 默认历史地块：{shapes} 份范围，{_defaultAdministrativeCities.Count} 块(城市辖地 {claimed} 块)");
        return claimed;
    }

    // 没有明确默认数据的区域沿用原有锚点挂接，默认历史范围在最后覆盖推算结果。
    private static readonly Dictionary<int, long> _wildAnchors = new();
    private const float WildSeatBudget = 24f;

    private static void AnchorWilderness()
    {
        _wildAnchors.Clear();
        AnchorPlan plan = PlanAnchors(_ => WildSeatBudget, WildSeatBudget);
        if (plan != null)
        {
            int[] owner = WorldMapZones.RootedPartition(plan.seeds, plan.roots, true);
            var stray = new List<int>();
            for (int i = 0; i < owner.Length; i++)
            {
                List<PlannedCity> cities = plan.CitiesOf(owner[i]);
                TileZone zone = WorldMapZones.Zone(i);
                if (cities == null || zone?.centerTile == null || zone.city != null && !WorldMapZones.Rough(i)) continue;
                PlannedCity nearest = NearestOf(cities, zone, i, false);
                if (nearest == null) stray.Add(i);
                else if (nearest.city?.data != null) _wildAnchors[zone.id] = nearest.city.data.id;
            }
            foreach (KeyValuePair<int, PlannedCity> pair in SameSide(plan, owner, stray, true))
                if (pair.Value.city?.data != null && WorldMapZones.Zone(pair.Key) is TileZone zone) _wildAnchors[zone.id] = pair.Value.city.data.id;
        }
        foreach (KeyValuePair<int, PlannedCity> pair in _defaultAdministrativeCities)
            if (pair.Value.city?.data != null && WorldMapZones.Zone(pair.Key) is TileZone zone)
                _wildAnchors[zone.id] = pair.Value.city.data.id;
        LogService.LogInfo($"[EmpireCraft][世界地图] 无人区默认归属：{_wildAnchors.Count} 块");
    }

    private static void AddSeats(List<(int index, int label, float budget)> seeds, List<float[]> seats, int label, float budget)
    {
        if (seats == null) return;
        foreach (float[] seat in seats)
        {
            if (seat == null || seat.Length < 2) continue;
            // 范围外的锚点不要(IndexOf 会把它夹到地图边上)
            Vector2Int tile = EarthTerrain.TileOf(_frame, _size, seat[0], seat[1]);
            if (tile.x < 0 || tile.y < 0 || tile.x >= MapBox.width || tile.y >= MapBox.height) continue;
            seeds.Add((WorldMapZones.IndexOf(tile), label, budget));
        }
    }

    // 城属于这个国家的第几个行政区(都城与不在任何行政区的城为 -1)
    private static int DivisionOf(PlannedPolity polity, PlannedCity city)
    {
        List<WorldMapDivision> divisions = polity?.data?.divisions;
        if (divisions == null || city?.data == null || polity.capital == city) return -1;
        for (int d = 0; d < divisions.Count; d++)
            if (divisions[d].cities?.Contains(city.data.id) == true) return d;
        return -1;
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
                // 法理首都：剧本定了的(剧本编辑器)，再是地区里的国都，再是最大的城
                string chosen = group.First().region.capital;
                PlannedCity seat = (string.IsNullOrEmpty(chosen) ? null : group.FirstOrDefault(city => city.data.id == chosen)) ??
                                   group.Where(city => city.owner?.capital == city).OrderBy(city => city.rank).FirstOrDefault() ??
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
        // 补空小国的王国法理归最近的剧本地区的帝国法理(西伯利亚归蒙古、非洲腹地归苏丹、僧祇……)，天下也就跟着那边：
        // 以前一个也不归，天下图层上整片是"游牧(未立核心)""埃及(未立核心)"
        // 按走得到的最近(地形代价、不过边墙：长城外的归草原那边的帝国法理)，走不到的按直线
        var anchors = _cities.Where(city => !city.random && city.city != null && _coreByRegion.ContainsKey(city.region.id)).ToList();
        int[] reach = anchors.Count == 0 ? new int[0] : WorldMapZones.NearestOf(anchors.Select(city => WorldMapZones.IndexOf(city.tile)).ToList());
        foreach (IGrouping<string, PlannedCity> filler in _cities.Where(city => city.random && city.city != null).GroupBy(city => city.region.id))
        {
            if (!_titleByRegion.TryGetValue(filler.Key, out KingdomTitle title) || anchors.Count == 0) continue;
            Vector2Int at = filler.First().tile;
            int index = WorldMapZones.IndexOf(at);
            PlannedCity nearest = index >= 0 && index < reach.Length && reach[index] >= 0
                ? anchors[reach[index]]
                : anchors.OrderBy(city => (city.tile - at).sqrMagnitude).First();
            if ((nearest.tile - at).magnitude > FillerCoreTiles) continue;
            EmpireCore near = _coreByRegion[nearest.region.id];
            Guard($"补空法理 {title.data.name} 入帝国法理", () => EmpireCoreManager.AddTitleToCore(near, title));
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

    // 补空小国离最近的剧本城多远以内才归那边的帝国法理(格)
    private const float FillerCoreTiles = 420f;

    // 这一时代的长城、边墙(格坐标的折线；也给旧档补算，见 WorldMapWilderness.ScenarioWalls)
    internal static List<List<Vector2>> WallLines(WorldMapEra era, WorldMapFrame frame, Vector2Int size)
    {
        var lines = new List<List<Vector2>>();
        foreach (WorldMapWall wall in era?.walls ?? new List<WorldMapWall>())
        {
            List<Vector2> line = (wall?.pts ?? new List<float[]>()).Where(point => point != null && point.Length >= 2)
                .Select(point => (Vector2)EarthTerrain.TileOf(frame, size, point[0], point[1])).ToList();
            if (line.Count >= 2) lines.Add(line);
        }
        return lines;
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
                Empire.ScriptedFoundingName = polity.name;
                try { polity.empire = ModClass.EMPIRE_MANAGER.NewEmpire(polity.kingdom, false, true, false, null, true, core); }
                finally { Empire.ScriptedFoundingName = null; }
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

    // 剧本定下的入主(用户 10-29 "剧本里面让后金入关后走中华体制百分百叫大清")：记在本国上，据有那座城(北京)即算入关，
    // 采纳华夏制度走兼用，国号照剧本(见 CompositeEmpireService.ScriptedConquestName)
    private static void ScriptConquests()
    {
        foreach (PlannedPolity polity in _polities)
        {
            if (polity.kingdom?.data == null || string.IsNullOrEmpty(polity.data?.conquest)) continue;
            City seat = _cities.FirstOrDefault(city => city.data.id == polity.data.conquestCity)?.city;
            var extra = polity.kingdom.GetOrCreate();
            extra.scripted_conquest_name = WorldMapData.PickParts(polity.data.conquest);
            extra.scripted_conquest_city_id = seat?.data != null && !seat.isRekt() ? seat.data.id : -1L;
            LogService.LogInfo($"[EmpireCraft][世界地图] {polity.name} 入主国号 {extra.scripted_conquest_name}，据 {seat?.GetCityName() ?? "无"} 即入关");
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
        // 剧本的两京：多京不看据不据有天下一半(见 MultiCapitalSystem.Eligible)
        polity.empire.data.scripted_jing = core.former_capital >= 0;
        core.SetCoreCapital(capital);
    }

    // 开局的君主、储君加长保底寿数；年轻皇帝族里没人的补子嗣，免得开局几年就寿终、绝嗣裂土(见 RealisticAgeSystem.OnScenarioStart)
    private static void SettleRulers()
    {
        foreach (PlannedPolity polity in _polities)
        {
            if (polity.founder?.data == null || polity.founder.isRekt()) continue;
            RealisticAgeSystem.OnScenarioStart(polity.founder);
            foreach ((Actor actor, WorldMapPerson person) in polity.kin)
                if (person.heir) RealisticAgeSystem.OnScenarioStart(actor);
            if (polity.empire != null) VirtualGenealogySystem.EnsureYoungRulerHouse(polity.founder);
        }
    }

    private static void Finish()
    {
        Guard("法理更新", () => ModClass.KINGDOM_TITLE_MANAGER?.update(-1f));
        Guard("入主国号", ScriptConquests);
        Guard("城的帝国法理", () => { EmpireCoreManager.ReconcileCityCores(); });
        Guard("后妃", () => Marry(_polities));
        Guard("前史", () => WorldMapHistory.Apply(_era, _polities.Select(polity => new WorldMapHistory.Realm
        {
            data = polity.data, kingdom = polity.kingdom, empire = polity.empire, kin = polity.kin.ToList()
        }).ToList(), _coreByRegion));
        Guard("开局君主", SettleRulers);
        Guard("宗教", () => WorldMapReligion.Apply(_era.year,
            _cities.Where(planned => planned.city != null).Select(planned => new WorldMapReligion.Place
            {
                id = planned.data.id, region = planned.region?.id ?? planned.data.region, rank = planned.rank, city = planned.city
            }).ToList(),
            _polities.Where(polity => polity.kingdom != null).Select(polity => (polity.kingdom, polity.capital?.city, polity.empire)).ToList()));
        Guard("天下", ArrangeTianxia);
        Guard("科技首扫", TechnologySystem.TryYearlyScan);
        Guard("帝国归属核对", () => VerifyEmpires("收尾"));
        Guard("进世界后的安排", QueueAfterLoad);
        // 无人区的真实地名与史实归属：记下范围、大小、时代与各政权都城(进世界划区时起名、挂法理，见 WorldMapWilderness)
        // 导入地图不是地球，无人区不按地球地理起名(照常走 WildernessNaming)
        if (!EarthTerrain.Imported)
        Guard("无人区地名与归属", () => WorldMapWilderness.Begin(_frame, _size, _era, _polities
            .Where(polity => polity.data != null && polity.capital?.city != null && !polity.capital.city.isRekt())
            .GroupBy(polity => polity.data.id).ToDictionary(group => group.Key, group => group.First().capital.city), _titleByRegion));
        if (!EarthTerrain.Imported) Guard("无人区的行政区", () => WorldMapWilderness.SetAnchors(_wildAnchors,
            _defaultAdministrativeCities.Keys.Select(index => WorldMapZones.Zone(index)?.id ?? -1).Where(id => id >= 0)));
        // 长城随存档留着：之后设行政区、重组行政区都以它为界(见 FrontierWalls)
        Guard("长城为界", () => FrontierWalls.Set(_wallLines ?? WallLines(_era, _frame, _size)));
        // 剧情事件：记下剧本里的国、城、地区各对上了哪个国家、城、王国法理，剧本人物的名字对上了哪个人(见 ScenarioEventSystem)
        // 剧本编辑器刷在无人区上的归属与永久中立区(导入地图也算，见 WorldMapWilderness.BeginPaint)
        Guard("无人区刷的归属", () => WorldMapWilderness.BeginPaint(_era, _frame, _size, _polities
                .Where(polity => polity.data != null && polity.capital?.city != null && !polity.capital.city.isRekt())
                .GroupBy(polity => polity.data.id).ToDictionary(group => group.Key, group => group.First().capital.city),
            _titleByRegion.Where(pair => pair.Value != null && !pair.Value.isRekt()).ToDictionary(pair => pair.Key, pair => pair.Value)));
        // 照原图分地的剧本：归城的地块记下，城还领着就不划成无人区(见 WorldMapWilderness.Keep)
        // 本剧本开了无人区的不留：城的形状不变，地形够无人区的转成这座城的法理无人区(见 WorldMapEra.own_wilderness)
        Guard("照原图归城的地块", () =>
        {
            bool own = _era?.own_wilderness == true;
            WorldMapWilderness.SetOwnWild(own);
            WorldMapWilderness.SetCityClaims(own && _era.exact ? PaintedCityOwners() : null);
            WorldMapWilderness.Keep(_era?.exact == true && !own ? PaintedCityZones : null);
        });
        Guard("剧情事件", () => ScenarioEventSystem.Begin(_era,
            _polities.Where(polity => polity.data != null && polity.kingdom?.data != null)
                .GroupBy(polity => polity.data.id).ToDictionary(group => group.Key, group => group.First().kingdom.data.id),
            _cities.Where(city => city.city?.data != null).GroupBy(city => city.data.id)
                .ToDictionary(group => group.Key, group => group.First().city.data.id),
            _titleByRegion.Where(pair => pair.Value?.data != null).ToDictionary(pair => pair.Key, pair => pair.Value.data.id),
            ScenarioPeople()));
        LogService.LogInfo($"[EmpireCraft][世界地图] {_era?.id} 摆好：{World.world.kingdoms.Count()} 国，{World.world.cities.Count()} 城");
        _cities = new List<PlannedCity>();
        _polities = new List<PlannedPolity>();
        _titleByRegion.Clear();
        _coreById.Clear();
        _coreByRegion.Clear();
        _cores.Clear();
        _usedZones.Clear();
        _subspecies.Clear();
        _fillerSites.Clear();
    }

    // 剧本人物的名字 → 人物编号(君主、宗室后妃、名将名臣；同名的先到先得)
    private static Dictionary<string, long> ScenarioPeople()
    {
        var people = new Dictionary<string, long>();
        void Add(Actor actor, string family, string given)
        {
            if (actor?.data == null || actor.isRekt()) return;
            foreach (string key in ScenarioEventSystem.NameKeys(family, given))
                if (!people.ContainsKey(key)) people[key] = actor.data.id;
        }
        foreach (PlannedPolity polity in _polities.Where(polity => polity.data != null))
        {
            if (polity.data.ruler != null) Add(polity.founder, polity.data.ruler.fam, polity.data.ruler.giv);
            foreach ((Actor actor, WorldMapPerson person) in polity.kin) Add(actor, person.fam, person.giv);
            foreach ((Actor actor, WorldMapFamous person) in polity.famous) Add(actor, person.fam, person.giv);
        }
        return people;
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
        _afterLoadQueued = true;
        _afterLoadQueuedAt = Time.realtimeSinceStartup;
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
            // 幕府：将军家的君主就任征夷大将军(天皇是宗主帝国的君主；开国之君在位本来不立幕府，剧本照史当场立)
            if (p.data.shogun) _afterLoad.Add(($"{p.name} 征夷大将军", () => Shogunate(p)));
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
        // 名将名臣：在开局兵力之后(统兵要有军可统)
        _famousCities.Clear();
        foreach (PlannedPolity polity in _polities.Where(polity => polity.famous.Count > 0 && polity.kingdom != null))
        {
            foreach ((Actor _, WorldMapFamous person) in polity.famous)
            {
                City city = CitiesOf(new[] { person.post }).FirstOrDefault();
                if (city != null) _famousCities[person.post] = city;
            }
        }
        // 原始部落圈里立的部落：改原始政体
        List<Kingdom> tribes = _polities.Where(polity => polity.tribe && polity.kingdom?.data != null).Select(polity => polity.kingdom).ToList();
        if (tribes.Count > 0) _afterLoad.Add(("原始部落", () => MakeTribes(tribes)));
        // 长城与关隘：先铺墙后筑关(关城压着城墙的那格成关门，见 WorldMapWalls)
        if (_era != null && (_era.walls.Count > 0 || _era.passes.Count > 0))
        {
            WorldMapEra wallEra = _era;
            WorldMapFrame wallFrame = _frame;
            Vector2Int wallSize = _size;
            _afterLoad.Add(("长城关隘", () => WorldMapWalls.Build(wallEra, wallFrame, wallSize)));
        }
        // 丝绸之路：长城关隘之后修(城墙只从关门过)，沿途各站对上城(见 SilkRoadSystem)
        if (_era?.silk is { Count: > 0 } && !EarthTerrain.Imported)
        {
            WorldMapEra silkEra = _era;
            WorldMapFrame silkFrame = _frame;
            Vector2Int silkSize = _size;
            _afterLoad.Add(("丝绸之路", () => SilkRoadSystem.Build(silkEra, silkFrame, silkSize)));
        }
        // 王朝积弊：老王朝国祚照史实接着算，积弊、六弊照时代给(用户 10-29 "这个是明末, 明朝过于强了点, 这个弊政都是0")。
        // 放在开局兵力之前：武备废弛压兵源
        var declining = _polities.Where(polity => polity.data != null && polity.empire != null).ToList();
        if (declining.Count > 0) _afterLoad.Add(("王朝积弊", () => ApplyDecline(declining)));
        // 开局兵力：各国照本国军制当场编军、补满(以前要等每月一成成补员，开局各国一兵没有)。人口照史料给了，兵也就照人口、军制有多有少
        _afterLoad.Add(("开局兵力", () =>
        {
            int realms = 0;
            float men = 0f;
            foreach (Kingdom kingdom in World.world.kingdoms.ToList())
            {
                if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild || kingdom.isNeutral()) continue;
                men += CorpsSystem.Prefill(kingdom);
                realms++;
            }
            LogService.LogInfo($"[EmpireCraft][世界地图] 开局兵力：{realms} 国共 {men:0} 人");
        }));
        foreach (PlannedPolity polity in _polities.Where(polity => polity.famous.Count > 0 && polity.kingdom != null))
        {
            PlannedPolity p = polity;
            var people = p.famous.ToList();
            _afterLoad.Add(($"{p.name} 名将名臣", () => SeatFamous(p.kingdom, p.empire, people)));
        }
        // 君主的妃嫔进后宫妃位(皇后位按君主配偶自选)
        foreach (PlannedPolity polity in _polities.Where(polity => polity.empire != null))
        {
            Empire e = polity.empire;
            List<Actor> consorts = polity.kin.Where(entry => entry.person.rel == "consort" && entry.person.of < 0).Select(entry => entry.actor).ToList();
            if (consorts.Count > 0) _afterLoad.Add(($"{polity.name} 后宫", () => SeatConsorts(e, consorts)));
        }
        // 开局外交(照史实：在打的仗、朝贡圈、和亲、同盟、旧谊宿怨)：称帝、宗藩、殖民、编军都做完之后逐条套，走外交界面同一套判定
        var realms = _polities.Where(polity => polity.data != null && polity.kingdom != null)
            .GroupBy(polity => polity.data.id).ToDictionary(group => group.Key, group => group.First().kingdom);
        var relations = _era?.relations?.ToList() ?? new List<WorldMapRelation>();
        if (relations.Count > 0) _afterLoad.Add(("开局外交", () => ApplyRelations(relations, realms)));
        // 商品市场当场结算一轮：市场图层、外交界面的市场页一进世界就有数
        _afterLoad.Add(("市场开市", CommodityMarketSystem.SettleNow));
        // 开局铺好相邻城之间的干道(路网规划算完后修通，见 RoadBuildingSystem.SeedAtScenarioStart)
        _afterLoad.Add(("开局干道", RoadBuildingSystem.SeedAtScenarioStart));
        _afterLoad.Add(("保存剧本默认领土", () => WorldMapWilderness.CaptureTerritory(World.world?.cities)));
        _afterLoad.Add(("保存历史范围归属", () =>
        {
            foreach (KeyValuePair<int, PlannedCity> pair in _defaultAdministrativeCities)
            {
                TileZone zone = WorldMapZones.Zone(pair.Key);
                City city = pair.Value.city;
                if (zone?.city == null && city?.data != null && !city.isRekt())
                    WorldMapWilderness.RememberTerritory(zone, city.GetTitle(), city, false);
            }
        }));
        // 雨林大泽开发度按时代(用户 10-29 "沼泽雨林什么的开发度也要在剧本模式里面体现出来, 比如春秋战国到漢南方都没怎么开发的")：
        // 当年没开出来的地改回丛林沼泽，当场划一次无人区再设开发度，所以排在最后(见 WorldMapDevelopment)
        if (_era != null)
        {
            WorldMapEra devEra = _era;
            WorldMapFrame devFrame = _frame;
            Vector2Int devSize = _size;
            _afterLoad.Add(("雨林大泽开发度", () => WorldMapDevelopment.Apply(devEra, devFrame, devSize)));
        }
        _afterLoad.Add(("无人区归属", WildernessSystem.EnsureBuilt));
        List<Kingdom> fillerKingdoms = _polities.Where(polity => polity.capital?.random == true && !polity.tribe && polity.kingdom != null)
            .Select(polity => polity.kingdom).ToList();
        if (fillerKingdoms.Count > 0) _afterLoad.Add(("碎片补空小国", () => DissolveShatteredFillers(fillerKingdoms)));
        // 城记的帝国法理按法理对齐(收尾时对过一回，设行政区、重组这些进世界的安排之后各城记的核心又空了，帝国法理图层开局头一年
        // 画成王国法理的颜色，等天下大势年结才对上——用户 10-30 "我的帝国核心层级好像也消失了""只有文字""图层还是王国的")
        _afterLoad.Add(("城的帝国法理", () => EmpireCoreManager.ReconcileCityCores()));
        // Finish 随后会清空 _cities；延迟回调必须持有独立快照，不能执行时再读临时城表。
        List<City> administrativeCities = _cities.Where(city => city.owner?.data?.divisions is { Count: > 0 })
            .Select(city => city.city).Where(city => city != null).ToList();
        _afterLoad.Add(("保存剧本行政辖境", () => WorldMapWilderness.CaptureAdministration(administrativeCities)));
    }

    // 用户 2026-10-29 "日本幕府的天皇去哪儿了""天皇不应该是万世一系的吗"：以前德川家光直接坐了日本的君位，天皇家没生出来
    private static void Shogunate(PlannedPolity polity)
    {
        Empire empire = polity.kingdom?.GetEmpire();
        Actor shogun = polity.kingdom?.king;
        if (empire != null && shogun != null && MilitaryRegency.Appoint(empire, shogun)) return;
        LogService.LogInfo($"[EmpireCraft][世界地图] {polity.name} 就任将军没成：帝国 {empire?.GetEmpireName() ?? "无"}，" +
                           $"政体 {empire?.CoreKingdom?.GetRegime()?.type}，君主 {shogun?.getName() ?? "无"}");
    }

    // 开局按人口盖房(繁荣)：人口越多的城盖得越多、民居升得越高(到本文化科技许可的一级为止)，住房装不下的另给容量(同神力"增加人口")；
    // 再给一年口粮，免得一开局就闹饥荒。一帧只做几毫秒(四五百座城一次做完要卡好几秒)
    private const float PeoplePerFlourish = 8000f, DevelopMilliseconds = 6f;
    private const int MaxFlourish = 6;

    private static void TickDevelop()
    {
        if (_develop.Count == 0) return;
        var watch = global::System.Diagnostics.Stopwatch.StartNew();
        while (_develop.Count > 0 && watch.Elapsed.TotalMilliseconds < DevelopMilliseconds)
        {
            (City city, float people) = _develop.Dequeue();
            if (city?.data == null || city.isRekt()) continue;
            try
            {
                int rounds = Mathf.Clamp(Mathf.CeilToInt(people / PeoplePerFlourish), 1, MaxFlourish);
                for (int i = 0; i < rounds; i++) CityConstructionSystem.Flourish(city);
                CityPopulationData data = city.GetOrCreate().population;
                if (data == null) continue;
                float housing = Mathf.Max(0f, city.getPopulationMaximum() - Mathf.Max(0f, data.brush_capacity));
                data.brush_capacity = Mathf.Max(data.brush_capacity, CityPopulationSystem.GetTotal(city) - housing);
                float households = CityPopulationSystem.GetTotal(city) / Mathf.Max(1, CityPopulationSystem.PeoplePerSlot(city));
                if (households >= 1f) PopulationEconomySystem.Deposit(city, data, "wheat", households, income: false, made: false);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][世界地图] {city.data?.name} 开局发展失败: {exception.Message}");
            }
        }
    }

    // 加载走完时原版还没重排各国的城、各城的人(王国的城表、城里的人要过一两帧才有)，这时划行政区一座城也划不出、
    // 一个可任官的人也没有，存续检查也会把帝国当成空城(试玩日志：大明"直辖留 0 城""城里可任官的人 0")。
    // 所以加载走完只是记下，等进了世界、剧本各帝国的中央都有城了再做(最多等 20 秒)，由战略调度每帧问一次
    private static bool _afterLoadArmed;
    private static float _afterLoadArmedAt;
    private const float AfterLoadWaitSeconds = 20f;

    // 剧本摆好(QueueAfterLoad)到加载界面看到加载走完(MarkDone → RunAfterLoad)之间也算没做完：生成流程收尾的镜头那一步就复位了
    // WorldMapGenerator.Generating，加载走完到加载界面来叫的这一两帧里，无人区(WildernessSystem.Tick 等它)会抢在进世界安排之前划区；
    // 加载界面出了岔子不来叫的，过 QueuedWaitSeconds 也不再等
    private static bool _afterLoadQueued;
    private static float _afterLoadQueuedAt;
    private const float QueuedWaitSeconds = 60f;

    // 加载界面揭开前要等的进世界安排还没做完(见 WorldMapLoadingScreen)
    public static bool AfterLoadPending => _afterLoadArmed || _afterLoadQueued && ReferenceEquals(_afterLoadWorld, World.world) &&
        Time.realtimeSinceStartup - _afterLoadQueuedAt < QueuedWaitSeconds;

    internal static int AfterLoadRemaining => _develop.Count + _afterLoad.Count;

    public static void RunAfterLoad()
    {
        _afterLoadQueued = false;
        if (_afterLoad.Count == 0) return;
        _afterLoadArmed = true;
        _afterLoadArmedAt = Time.realtimeSinceStartup;
    }

    public static void TickAfterLoad()
    {
        if (!_afterLoadArmed) return;
        if (!ReferenceEquals(_afterLoadWorld, World.world))
        {
            _afterLoadArmed = false;
            _afterLoad.Clear();
            _develop.Clear();
            return;
        }
        bool ready = _scenarioEmpires.All(entry => entry.kingdom == null || entry.kingdom.isRekt() || entry.kingdom.cities?.Count > 0);
        if (!ready && Time.realtimeSinceStartup - _afterLoadArmedAt < AfterLoadWaitSeconds) return;
        // 先把各城的房盖好、粮补上，再划行政区、编军
        if (_develop.Count > 0)
        {
            TickDevelop();
            return;
        }
        _afterLoadArmed = false;
        List<(string what, Action action)> actions = _afterLoad.ToList();
        _afterLoad.Clear();
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
            List<City> all = cities.Where(city => city.kingdom == empire.CoreKingdom && city != empire.CoreKingdom.capital).ToList();
            if (all.Count == 0) continue;
            // 明确的历史辖境优先；只有没有范围数据的推算行政区才按边墙分设。
            string polity = _polities.FirstOrDefault(item => item.empire == empire)?.data?.id ?? "";
            List<List<City>> parts = DivisionParts(polity, data, all);
            if (parts.Count > 1)
                LogService.LogInfo($"[EmpireCraft][世界地图] {empire.GetEmpireName()} {data.Name} 隔着长城分设 {parts.Count} 区：" +
                                   string.Join("；", parts.Select(part => string.Join("、", part.Select(city => city.GetCityName())))));
            KingdomTitle shared = null;
            for (int part = 0; part < parts.Count; part++)
            {
                List<City> region = parts[part];
                // 行政区要有人主政(城主或城里的人)：无小人模式下城里多半只有背景人口，先补城主
                foreach (City city in region) CityPopulationSystem.EnsureLeader(city);
                KingdomTitle title = region[0].GetTitle();
                if (title == null || title.isRekt() || title != shared && !titled.Add(title)) title = null;
                if (part == 0) shared = title;
                Kingdom province = empire.EstablishAdministrativeDivision(region, title, true);
                if (province == null)
                {
                    HashSet<long> kept = empire.GetProtectedDirectCityIds();
                    LogService.LogWarning($"[EmpireCraft][世界地图] {empire.GetEmpireName()} 设 {data.Name} 没成：{region.Count} 城，" +
                                          $"京畿留着的 {region.Count(city => kept.Contains(city.id))} 城，战时 {empire.HasTerritorialWar()}，" +
                                          $"政体 {empire.CoreKingdom.GetRegime()?.type}，城里可任官的人 {region.Sum(city => city.units?.Count(unit => unit != null && unit.isAlive() && unit.isAdult() && !unit.isKing()) ?? 0)}");
                    continue;
                }
                _divided = true;
                // 隔墙另设的那片照治所城起名(不叫剧本的道名、不当都护府)
                if (part > 0) continue;
                // 都护府、长史府(用户 10-30 "我安西都护府哪儿去了""把他作为一个汉化的行政区，独立后可以恢复自己的法理名字")：
                // 汉化的行政区——记下汉地的地名(安西)与这个帝国，有外交权、按羁縻管理，类型是都护府，国名照"地名 + 都护府"由政区类型起；
                // 离了这个帝国照自己的法理改名(见 KingdomExtension.AdministrativeFront)。以前把整个名硬写成国名，叛去后成了
                // "安西都护府苏丹国"。别的行政区照常按法理名加类型起名(明的南直隶、各布政使司)
                if (Protectorate(data.name))
                {
                    (string place, string back) = SplitProtectorate(data.name);
                    KingdomExtension.KingdomExtraData extra = province.GetOrCreate();
                    extra.admin_name = place;
                    extra.admin_back = back;
                    extra.admin_empire = empire.id;
                    extra.jimi_mode = (int)JimiMode.Jimi;
                    province.GetRegime()?.SetAllowDiplomacy(true);
                    EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(province);
                }
                else
                {
                    // 保存剧本行政名，不能下一轮按王国法理把河南改回中原、陕西改回关中。
                    KingdomExtension.KingdomExtraData extra = province.GetOrCreate();
                    extra.admin_name = data.name;
                    extra.admin_back = "";
                    extra.admin_empire = empire.id;
                    province.SetKingdomName(data.Name);
                }
            }
        }
    }

    // 山西本有大同，明确的历史辖境不能因为一道边墙自动另立“大同省”。
    // 缺乏明确范围的推算行政区仍沿用原有隔墙分设规则。
    private static List<List<City>> DivisionParts(string polity, WorldMapDivision data, List<City> cities)
    {
        bool bounded = data.boundary is { Count: > 0 } ||
            WorldMapAdministrativeBoundaries.For(_era, polity, (data.name ?? "").Split('|')[0]).Count > 0;
        return bounded ? new List<List<City>> { cities }
            : FrontierWalls.Split(cities).OrderByDescending(part => part.Count).ToList();
    }

    // 都护府、长史府、都督府这类统辖羁縻州的(照中文名)
    private static bool Protectorate(string name)
    {
        string zh = (name ?? "").Split('|')[0];
        return zh.EndsWith("都护府") || zh.EndsWith("长史府") || zh.EndsWith("都督府");
    }

    // "安西都护府|Anxi Protectorate|安西都護府" → 地名"安西|Anxi|安西"；后缀是都护府的照政区类型起(空串)，
    // 长史府、都督府另记("长史府|Protectorate|長史府")
    private static (string place, string back) SplitProtectorate(string name)
    {
        string[] parts = (name ?? "").Split('|');
        string zh = parts[0], en = parts.Length > 1 ? parts[1] : zh, tw = parts.Length > 2 ? parts[2] : zh;
        string suffix = zh.Length > 3 ? zh.Substring(zh.Length - 3) : "";
        string Cut(string text, params string[] tails)
        {
            foreach (string tail in tails)
                if (text.EndsWith(tail) && text.Length > tail.Length) return text.Substring(0, text.Length - tail.Length).Trim();
            return text.Trim();
        }
        string place = $"{Cut(zh, "都护府", "长史府", "都督府")}|{Cut(en, " Protectorate", " Chief Clerk", " Commandery")}|" +
                       $"{Cut(tw, "都護府", "長史府", "都督府")}";
        string back = suffix switch
        {
            "长史府" => "长史府|Protectorate|長史府",
            "都督府" => "都督府|Governorate|都督府",
            _ => ""
        };
        return (place, back);
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
        {
            foreach (City city in group) CityPopulationSystem.EnsureLeader(city);
            // 法理里隔着长城的城分设(见 FrontierWalls)：法理首府那片在前，别的几片是行政区就共用法理，是封国就不带法理
            List<List<City>> parts = FrontierWalls.Split(group.OrderBy(city => city == group.Key.title_capital ? 0 : 1));
            for (int part = 0; part < parts.Count; part++)
                if (empire.EstablishTerritorialDivision(parts[part], group.Key, part > 0) != null) made++;
        }
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
        // 金玺诏书以后开局的先改选帝制(见 HolyRomanSystem.GoldenBullAtStart)
        HolyRomanSystem.GoldenBullAtStart(empire, _era?.year ?? 0);
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
    // 锁定(年度自动并分不再动它，见 TianxiaSystem.AutoArrange)。另写了 tianxia 的(西域兼属中华与阿拉伯)兼属所写的天下。
    // 中华与草原只在长城一线兼属(用户 10-29 "中华和草原的天下重叠也太大了")：漠南(晋兼收)、河套、辽东、河西、河湟(秦兼收)
    // 两边的帝国法理都算，漠北、准噶尔、满洲、卫藏不再整块挂进中华
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

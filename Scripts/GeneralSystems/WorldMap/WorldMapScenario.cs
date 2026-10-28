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
        public readonly List<(Actor actor, WorldMapFamous person)> famous = new();
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
        step(BuildTitles, "World Map: De Jure", false);
        step(BuildEmpires, "World Map: Empires", false);
        step(Finish, "World Map: Tianxia", false);
    }

    #region 分城

    private static void Plan(WorldMapFrame frame, Vector2Int size, int sizeIndex, WorldMapEra era)
    {
        _era = era;
        _frame = frame;
        _size = size;
        frame.Bounds(size, out _, out float degreesPerTile);
        WorldMapScale.SetFromFrame(degreesPerTile);
        _regionWeight.Clear();
        _develop.Clear();
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

    private static TileZone FindZone(Vector2Int tile)
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

    // 补的小国先按小城的预算铺自己的辖地，再把剩下有陆地的区块(山麓、沿海零碎地块，荒漠冻土只给收这种地的城)分给走得到的
    // 最近的城，大山腹地与没人收的荒漠、冻土留空(见 WorldMapZones.FillAll)；
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
            (city, zone) => city.addZone(zone),
            // 最后一轮：同侧的城可超上限一半把剩下的领完(不翻山)，免得留给山那边的城
            (city, zone) => city.zones.Count < (TerrainSystem.BarrenKind(zone) != BarrenLandSystem.None ? BarrenLandSystem.Limit : limit) * 3 / 2);
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
        // 建城时改过地块(绿洲、平出河谷)：按现在的地形重算区块代价
        WorldMapZones.Build();
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
        // 补空小国的王国法理归最近的剧本地区的帝国法理(西伯利亚归蒙古、非洲腹地归苏丹、僧祇……)，天下也就跟着那边：
        // 以前一个也不归，天下图层上整片是"游牧(未立核心)""埃及(未立核心)"
        var anchors = _cities.Where(city => !city.random && city.city != null && _coreByRegion.ContainsKey(city.region.id)).ToList();
        foreach (IGrouping<string, PlannedCity> filler in _cities.Where(city => city.random && city.city != null).GroupBy(city => city.region.id))
        {
            if (!_titleByRegion.TryGetValue(filler.Key, out KingdomTitle title) || anchors.Count == 0) continue;
            Vector2Int at = filler.First().tile;
            PlannedCity nearest = anchors.OrderBy(city => (city.tile - at).sqrMagnitude).First();
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

    private static void Finish()
    {
        Guard("法理更新", () => ModClass.KINGDOM_TITLE_MANAGER?.update(-1f));
        Guard("入主国号", ScriptConquests);
        Guard("城的帝国法理", () => { EmpireCoreManager.ReconcileCityCores(); });
        Guard("前史", () => WorldMapHistory.Apply(_era, _polities.Select(polity => new WorldMapHistory.Realm
        {
            data = polity.data, kingdom = polity.kingdom, empire = polity.empire, kin = polity.kin.ToList()
        }).ToList(), _coreByRegion));
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
        // 长城与关隘：先铺墙后筑关(关城压着城墙的那格成关门，见 WorldMapWalls)
        if (_era != null && (_era.walls.Count > 0 || _era.passes.Count > 0))
        {
            WorldMapEra wallEra = _era;
            WorldMapFrame wallFrame = _frame;
            Vector2Int wallSize = _size;
            _afterLoad.Add(("长城关隘", () => WorldMapWalls.Build(wallEra, wallFrame, wallSize)));
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
        // 开局外交(照史实：在打的仗、朝贡圈、和亲、同盟、旧谊宿怨)：称帝、宗藩、殖民、编军都做完之后逐条套，走外交界面同一套判定
        var realms = _polities.Where(polity => polity.data != null && polity.kingdom != null)
            .GroupBy(polity => polity.data.id).ToDictionary(group => group.Key, group => group.First().kingdom);
        var relations = _era?.relations?.ToList() ?? new List<WorldMapRelation>();
        if (relations.Count > 0) _afterLoad.Add(("开局外交", () => ApplyRelations(relations, realms)));
        // 商品市场当场结算一轮：市场图层、外交界面的市场页一进世界就有数
        _afterLoad.Add(("市场开市", CommodityMarketSystem.SettleNow));
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

    // 加载界面揭开前要等的进世界安排还没做完(见 WorldMapLoadingScreen)
    public static bool AfterLoadPending => _afterLoadArmed;

    public static void RunAfterLoad()
    {
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
            List<City> region = cities.Where(city => city.kingdom == empire.CoreKingdom && city != empire.CoreKingdom.capital).ToList();
            if (region.Count == 0) continue;
            // 行政区要有人主政(城主或城里的人)：无小人模式下城里多半只有背景人口，先补城主
            foreach (City city in region) CityPopulationSystem.EnsureLeader(city);
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
        {
            foreach (City city in group) CityPopulationSystem.EnsureLeader(city);
            if (empire.EstablishTerritorialDivision(group.ToList(), group.Key) != null) made++;
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

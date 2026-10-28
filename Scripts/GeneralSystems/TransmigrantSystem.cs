using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 穿越政体(用户 2026-10-28 按小说《临高启明》："单独的穿越政体，国号涵盖澳宋各阶段和体制变更""要体现出他们真的是瞬间出现在这个世界"
// "五百元老无法绝嗣，因为是元老院固定席位""人名与官职要与小说对得上，有就对得上，没有就自动"
// "阶段性散播原世界的书籍，但会计划性地不威胁自己统治")：
//   · 降临(权能「穿越降临」，见 MainTab)：点选处最近的空海岸凭空出现一座成形的城、一个新国家——五百二十一名元老、首批归化民、
//     伏波军、船只、国库与机器物资；文化是澳洲人(Culture_Aosong)，科技直接到火药时代、工业时代各项先有一半进度，
//     码头、厂区、近代城貌当场就有。地图上已有叫临高的城或村镇时落在它旁边，营地叫百仞、国号仍是临高元老院；
//     没有就把落脚的城叫临高。一个世界同时只有一支穿越者；
//   · 元老院五百二十一席固定：元老故去，席位由子女一人继承(共同纲领"穿越者的席位……由其指定子女一人继承")，永不空缺。
//     名字、职务照原著名册(Scripts/Regimes/Configs/Transmigrant/Senators.csv，取自读者整理的人物记)：
//     降临时按临时执委会的分组、第一届执委会时按早期职务、共同纲领以后按现任职务；名册以外的席位用现代中国人名自动起名；
//   · 执委会主席由元老院全体大会从元老里选，任期四年、可连任一次，连任两届后隔一届才能再选(原著执委制度)；
//     主席故去或出缺时当场补选(见 OfficeSelector.Select 的 Vote 一支)；只落成主席、驻县办主任和几位名臣名将，其余元老只在名册上；
//   · 阶段(制度线 InstitutionTrees/Aosong.json)：降临·临时执委会 → 第一届执委会(两城起) → 共同纲领·第二届执委会(五城起)
//     → 大宋澳洲行在(十城起，国号改大宋)；每到一阶段记一条大事；
//   · 出版指导小组(施行后)：每 PublishYears 年从大图书馆挑书刊行，只放出比本国落后两个时代以上、不涉军事政治的技术
//     (冶金、营造、农工、格致)，接触到的文化与治下各城的文化得进度；兵书、政论锁在大图书馆里不外传；
//   · 降临即组建政府进帝国层；流民投奔、开拓新城、伏波军保编、对弱邻用兵，元老几乎杀不死、不投别国(见 TransmigrantSystem.Growth)。
public static partial class TransmigrantSystem
{
    public const string Culture = "Aosong";
    public const string PowerId = "transmigrant_landing";
    public const int SeatCount = 521;
    // 原班之外首批收容、检疫后编入的归化民
    private const int Settlers = 1479;
    private const string Species = "human";
    private const int KnownTier = 6;
    private const float HeadStart = 0.5f;
    private const int Treasury = 5000;
    private const float ArmyStrength = 500f;
    private const int Warships = 3, Transports = 2;
    private const int ChairmanTermYears = 4, ChairmanMaxTerms = 2, ChairmanMinAge = 21;
    private const int PublishYears = 5, PublishPerRound = 2;
    private const float PublishProgress = 0.3f;
    // 名册前这几行是执委会的核心人物(文德嗣、马千瞩、萧子山……)，选举与讣告优先
    private const int CoreRoster = 13;
    // 地图上的临高离落脚处多远以内算"就在旁边"(地块)
    private const int LingaoReach = 24;
    private const string SenateBanner = "ui/banners/aosong_senate", NationalBanner = "ui/banners/aosong_national";
    // 启明星旗：原版调色板里旗面最接近深蓝、纹样最接近白的一套；地图色另用自定义深蓝
    private static readonly Color FlagField = new(44 / 255f, 56 / 255f, 134 / 255f), FlagInk = Color.white;
    private const string MapColor = "#2C3886";
    private static readonly HashSet<string> SafeBranches = new() { "material", "metallurgy", "construction", "science" };

    // 落地建城时挡住建城后的钩子(城邦分出、部落分迁，见 CityPatch.CityBuilt)
    public static bool Landing { get; private set; }

    public static TransmigrantState Of(Kingdom kingdom) =>
        kingdom?.data == null || kingdom.isRekt() ? null : kingdom.GetOrCreate().transmigrant;

    public static bool IsTransmigrant(Kingdom kingdom) => Of(kingdom) != null;

    public static Kingdom Active() =>
        World.world?.kingdoms?.list?.FirstOrDefault(kingdom => kingdom != null && !kingdom.isRekt() && Of(kingdom) != null);

    #region 权能与旗帜

    public static void InitPower()
    {
        if (AssetManager.powers.get(PowerId) != null) return;
        AssetManager.powers.add(new GodPower { id = PowerId, name = PowerId, click_action = OnClick });
    }

    private static bool OnClick(WorldTile tile, string power)
    {
        string error = Land(tile, out Kingdom kingdom);
        WorldTip.showNow(error == null ? string.Format(LM.Get("transmigrant_landed_tip"), kingdom.GetKingdomFullName()) : LM.Get(error),
            false, "top", 3f);
        return error == null;
    }

    // 原版王国旗帜：每个物种的纹样表末尾补上元老院旗、启明星旗两枚(加在末尾，已有国家的旗帜序号不变)
    public static void AppendBanners(BannerAsset asset)
    {
        if (asset?.icons == null) return;
        if (!asset.icons.Contains(SenateBanner)) asset.icons.Add(SenateBanner);
        if (!asset.icons.Contains(NationalBanner)) asset.icons.Add(NationalBanner);
    }

    private static void ApplyFlag(Kingdom kingdom)
    {
        ColorAsset best = null;
        float bestScore = float.MaxValue;
        foreach (ColorAsset asset in AssetManager.kingdom_colors_library.list)
        {
            if (asset == null || !ColorUtility.TryParseHtmlString(asset.color_main_2, out Color field) ||
                !ColorUtility.TryParseHtmlString(asset.color_banner, out Color ink)) continue;
            float score = Distance(field, FlagField) + Distance(ink, FlagInk);
            if (score < bestScore)
            {
                bestScore = score;
                best = asset;
            }
        }
        if (best != null) kingdom.updateColor(best);
        CustomColorSystem.Set(kingdom, MapColor);
        BannerAsset banner = AssetManager.kingdom_banners_library.get(kingdom.getActorAsset().banner_id);
        AppendBanners(banner);
        int icon = banner?.icons?.IndexOf(NationalBanner) ?? -1;
        if (icon >= 0) kingdom.data.banner_icon_id = icon;
    }

    private static float Distance(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);

    #endregion

    #region 降临

    public static string Land(WorldTile tile, out Kingdom kingdom)
    {
        kingdom = null;
        if (tile?.zone == null || World.world == null) return "transmigrant_error_tile";
        if (Active() != null) return "transmigrant_error_once";
        TileZone zone = FindLandingZone(tile.zone, out City carveFrom);
        if (zone == null) return "transmigrant_error_no_coast";
        if (carveFrom != null) carveFrom.removeZone(zone);
        (string lingao, bool native) = LingaoNear(zone);
        string baseName = native ? LM.Get("transmigrant_bairen") : lingao;

        Actor founder = World.world.units.createNewUnit(Species, zone.centerTile, pAdultAge: true);
        if (founder == null) return "transmigrant_error_tile";
        founder.ClearDisposedKingdom();
        City city;
        Landing = true;
        try
        {
            kingdom = World.world.kingdoms.makeNewCivKingdom(founder);
            city = World.world.cities.buildNewCity(founder, zone);
            founder.joinCity(city);
            // 新建的城不自动当都城：组建政府(称帝)时要读都城，没有就报"读取帝国首都失败"
            if (kingdom.capital == null) kingdom.setCapital(city);
            founder.createDefaultCultureAndLanguageAndClan(city.name);
            kingdom.setUnitMetas(founder);
            city.setUnitMetas(founder);
        }
        finally
        {
            Landing = false;
        }
        if (kingdom == null || city == null) return "transmigrant_error_tile";

        double now = World.world.getCurWorldTime();
        int year = Date.getCurrentYear();
        var state = new TransmigrantState
        {
            landed_at = now, landed_year = year, base_city_id = city.id, lingao_name = lingao, lingao_native = native,
            next_publish_at = now + PublishYears * 60d
        };
        kingdom.GetOrCreate().transmigrant = state;

        // 文化、政体：澳洲人、穿越政体(制度线的根节点就是穿越政体，手选标记挡住别的文化改政体)
        RealmCultureSystem.Convert(kingdom, Culture, wholeEmpire: false);
        kingdom.SetRegimeType(RegimeType.Transmigrant);
        kingdom.LoadRegime();
        kingdom.GetOrCreate().regime_manually_selected = true;

        RenameCity(city, baseName);
        kingdom.SetCustomCountryName(lingao);
        kingdom.SetCustomCountrySuffix(LM.Get("transmigrant_senate_suffix"));
        ApplyFlag(kingdom);

        Populate(city);
        GrantTechs(kingdom);
        kingdom.AddMoney(Treasury, TreasuryCategory.Gift);
        BuildTown(city);

        CreateSeats(kingdom, state, year);
        BindLeaders(kingdom, state, city, founder);
        FormGovernment(kingdom);
        CorpsSystem.RaiseLanding(kingdom, city, ArmyStrength, CorpsArmSystem.Arm.Rifle);
        LaunchFleet(kingdom, city);

        string text = string.Format(LM.Get("transmigrant_landed_history"), year, city.GetCityName(), SeatCount, founder.getName());
        TranslateHelper.LogMajor(text, kingdom);
        LogService.LogInfo($"[EmpireCraft][穿越] {kingdom.GetKingdomFullName()} 降临于 {city.GetCityName()}" +
                           (carveFrom != null ? $"(从 {carveFrom.GetCityName()} 切出岸边一块)" : "") +
                           (native ? $"，旁边就是 {lingao}" : ""));
        return null;
    }

    // 从点选处向外找最近的空海岸(地块够大、没有城、可以建城)；找不到就从别人城里切一块不是城中心的岸边地块
    private static TileZone FindLandingZone(TileZone start, out City carveFrom)
    {
        carveFrom = null;
        var seen = new HashSet<TileZone> { start };
        var queue = new Queue<TileZone>();
        queue.Enqueue(start);
        TileZone fallback = null;
        City fallbackCity = null;
        int visited = 0;
        while (queue.Count > 0 && visited++ < 2500)
        {
            TileZone zone = queue.Dequeue();
            if (Coastal(zone))
            {
                if (zone.city == null && zone.canStartCityHere()) return zone;
                if (fallback == null && zone.city != null && !zone.city.isRekt() && zone.city.zones.Count > 4 &&
                    zone.city.getTile()?.zone != zone && zone.tiles_with_ground >= 48)
                {
                    fallback = zone;
                    fallbackCity = zone.city;
                }
            }
            foreach (TileZone next in zone.neighbours_all)
                if (next != null && seen.Add(next)) queue.Enqueue(next);
        }
        carveFrom = fallbackCity;
        return fallback;
    }

    private static bool Coastal(TileZone zone) =>
        zone != null && zone.tiles_with_ground >= 32 && SeaLanding.Waters(zone).Any();

    // 地图上落脚处附近的临高(城或村镇)；没有就用临高这个名字给自己的城
    private static (string name, bool native) LingaoNear(TileZone zone)
    {
        string lingao = LM.Get("transmigrant_lingao");
        bool IsLingao(string name) => !string.IsNullOrEmpty(name) &&
                                     (name.StartsWith(lingao) || name.StartsWith("临高") || name.StartsWith("臨高"));
        bool Near(TileZone other) => other != null && Mathf.Abs(other.x - zone.x) + Mathf.Abs(other.y - zone.y) <= LingaoReach;
        foreach (City city in World.world.cities.list)
            if (city?.data != null && !city.isRekt() && IsLingao(city.data.name) && Near(city.getTile()?.zone))
                return (lingao, true);
        foreach (RuralSettlement settlement in RuralSettlementSystem.Save())
            if (settlement != null && !settlement.cleared && settlement.ruined_at < 0d && IsLingao(settlement.name) &&
                Near(World.world.GetTile(settlement.x, settlement.y)?.zone))
                return (lingao, true);
        return (lingao, false);
    }

    private static void RenameCity(City city, string name)
    {
        city.data.name = name;
        var extra = city.GetOrCreate();
        extra.core_name = "";
        extra.core_name_source = "";
        city.EnsureCityCoreName();
        city.RefreshCityDisplayName();
        CultureService.RecordCityCulturalName(city, Culture, name);
    }

    private static void Populate(City city)
    {
        CityPopulationSystem.AddBackground(city, SocialClass.Noble, Culture, Species, default, SeatCount);
        CityPopulationSystem.AddBackground(city, SocialClass.Labour, Culture, Species, default, Settlers);
        // 住房装不下的那部分另给容量，免得降临的人口按住房回落(同人口笔刷，见 CityResourcePowers.ChangePopulation)
        CityPopulationData data = CityPopulationSystem.Get(city);
        if (data != null)
        {
            float housing = Mathf.Max(0f, city.getPopulationMaximum() - Mathf.Max(0f, data.brush_capacity));
            data.brush_capacity = Mathf.Max(data.brush_capacity, CityPopulationSystem.GetTotal(city) - housing);
        }
        CityPopulationSystem.InvalidateHouseholdCaches();
        LayerCityCache.Invalidate(city);
    }

    // 知识带来了，工业底子要自己建：火药时代以前全会，随身带来的枪械、炸药、发电等几项现代技术(BroughtTechs，连同前置)直接会，
    // 工业、电气时代的其余各项先有一半进度
    private static void GrantTechs(Kingdom kingdom)
    {
        foreach (TechNodeConfig tech in TechnologySystem.Techs.ToList())
            if (tech.tier <= KnownTier) TechnologySystem.ForceResearch(Culture, tech.id);
        foreach (string id in BroughtTechs) TechnologySystem.ForceResearch(Culture, id);
        foreach (TechNodeConfig tech in TechnologySystem.Techs.Where(tech => tech.tier > KnownTier && tech.tier <= KnownTier + 2).ToList())
            if (!TechnologySystem.HasTech(Culture, tech.id)) TechnologySystem.AddTechProgressFraction(Culture, tech.id, HeadStart);
        TechnologySystem.GetState(Culture).bootstrapped = true;
        RealmTechState realm = TechnologySystem.GetRealmState(kingdom);
        foreach (string id in TechnologySystem.GetState(Culture).researched_techs.ToList()) realm.Adopt(id);
    }

    // 码头、各类建筑一次建好，一处厂区，城貌按工业时代画
    private static void BuildTown(City city)
    {
        try
        {
            BuildingAsset dock = city.getActorAsset()?.getBuildingDockAsset();
            WorldTile dockTile = dock == null ? null : ai.behaviours.CityBehBuild.getDockTile(city);
            if (dockTile != null) World.world.buildings.addBuilding(dock, dockTile);
            CityConstructionSystem.Flourish(city);
            TileZone center = city.getTile()?.zone;
            TileZone yard = city.zones.FirstOrDefault(zone => zone != center && zone.tiles_with_ground >= 48);
            if (yard != null) FactorySystem.Place(yard.centerTile, 2, out _, manual: true);
            var extra = city.GetOrCreate();
            extra.urban_peak_progress = Mathf.Max(extra.urban_peak_progress, 1f);
            extra.urban_peak_housing = Math.Max(extra.urban_peak_housing, 6);
            PureMapPatch.Redraw(city);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][穿越] 营建落脚城失败: {exception.Message}");
        }
    }

    private static void LaunchFleet(Kingdom kingdom, City city)
    {
        SeaRegionSystem.Region water = SeaRegionSystem.RegionsAround(city).FirstOrDefault();
        if (water == null) return;
        var data = kingdom.GetOrCreate();
        data.fleets ??= new List<FleetData>();
        data.fleets.Add(new FleetData
        {
            id = OverallHelperFunc.IdGenerator.NextId(), serial = ++data.fleet_serial, kingdom_id = kingdom.id,
            home_city_id = city.id, region_id = water.Id, target_region_id = water.Id, zone_id = water.Center?.id ?? -1,
            warships = Warships, transports = Transports
        });
    }

    #endregion

    #region 元老院名册

    private sealed class RosterRow
    {
        public string name = "", group = "", landing = "", early = "", post = "", cabinet = "";
        public bool male = true;
    }

    private static List<RosterRow> _roster;

    // 原著名册(读者整理的人物记，截至第五卷)：姓名、性别、临时执委会的分组、早期职务、现任职务
    private static List<RosterRow> Roster
    {
        get
        {
            if (_roster != null) return _roster;
            _roster = new List<RosterRow>();
            try
            {
                string path = Path.Combine(ModClass._declare.FolderPath, "Scripts", "Regimes", "Configs", "Transmigrant", "Senators.csv");
                foreach (string line in File.ReadAllLines(path).Skip(1))
                {
                    string[] cells = line.Split(',');
                    if (cells.Length < 7 || string.IsNullOrWhiteSpace(cells[1])) continue;
                    _roster.Add(new RosterRow
                    {
                        name = cells[1].Trim(), male = cells[2].Trim() != "F", group = cells[3].Trim(), landing = cells[4].Trim(),
                        early = cells[5].Trim(), post = cells[6].Trim(), cabinet = cells.Length > 7 ? cells[7].Trim() : ""
                    });
                }
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][穿越] 读元老名册失败: {exception.Message}");
            }
            return _roster;
        }
    }

    private static readonly string[] CompoundSurnames =
        { "东门", "南宫", "司马", "欧阳", "上官", "诸葛", "慕容", "公孙", "令狐", "皇甫", "独孤", "长孙", "宇文", "西门", "北炜" };

    private static (string surname, string given) SplitName(string name)
    {
        int dot = name.IndexOf('·');
        if (dot > 0) return (name.Substring(dot + 1), name.Substring(0, dot));
        foreach (string compound in CompoundSurnames)
            if (name.Length > compound.Length && name.StartsWith(compound)) return (compound, name.Substring(compound.Length));
        return name.Length > 1 ? (name.Substring(0, 1), name.Substring(1)) : (name, "");
    }

    public static string FullName(SenatorSeat seat) =>
        seat == null ? "" : seat.surname.Length > 0 && seat.given.Length > 0 && seat.roster >= 0 && Roster[seat.roster].name.Contains('·')
            ? Roster[seat.roster].name : seat.surname + seat.given;

    // 这一席的元老在当前阶段的职务(原著名册里有的才有)
    public static string PostOf(TransmigrantState state, SenatorSeat seat)
    {
        if (seat?.roster < 0 || seat.roster >= Roster.Count || seat.generation > 1) return "";
        RosterRow row = Roster[seat.roster];
        return state.stage switch
        {
            1 => !string.IsNullOrEmpty(row.landing) ? row.landing : row.early,
            2 => !string.IsNullOrEmpty(row.early) ? row.early : row.post,
            >= 5 when !string.IsNullOrEmpty(row.cabinet) => row.cabinet,
            _ => !string.IsNullOrEmpty(row.post) ? row.post : row.early
        };
    }

    private static void CreateSeats(Kingdom kingdom, TransmigrantState state, int year)
    {
        List<RosterRow> roster = Roster;
        for (int i = 1; i <= SeatCount; i++)
        {
            var seat = new SenatorSeat { seat = i, roster = i - 1 < roster.Count ? i - 1 : -1 };
            if (seat.roster >= 0)
            {
                RosterRow row = roster[seat.roster];
                (seat.surname, seat.given) = SplitName(row.name);
                seat.male = row.male;
            }
            else
            {
                seat.male = Unit(kingdom.id, i, 1) < 0.75d;
                seat.surname = Pick("China:FamilyNames", kingdom.id, i, 2);
                seat.given = GivenName(seat.male, kingdom.id, i, 3);
            }
            // 穿越时二十二到六十岁
            seat.birth_year = year - (22 + (int)(Unit(kingdom.id, i, 4) * 39d));
            state.seats.Add(seat);
        }
    }

    private static string GivenName(bool male, long a, long b, long c)
    {
        string first = Pick(male ? "China:UnitNamesMale1" : "China:UnitNamesFemale1", a, b, c);
        return Unit(a, b, c + 7) < 0.6d ? first + Pick(male ? "China:UnitNamesMale2" : "China:UnitNamesFemale2", a, b, c + 11) : first;
    }

    private static readonly Dictionary<string, List<string>> Pools = new();

    private static string Pick(string group, long a, long b, long c)
    {
        if (!Pools.TryGetValue(group, out List<string> pool))
            Pools[group] = pool = PartySystem.GetCultureWordPool(Culture, group).Where(word => word.Length <= 2).ToList();
        return pool.Count == 0 ? "" : pool[(int)(Unit(a, b, c) * pool.Count) % pool.Count];
    }

    // 按三个数散列成 [0,1)
    private static double Unit(long a, long b, long c)
    {
        ulong x = (ulong)a * 0x9E3779B97F4A7C15UL ^ (ulong)b * 0xC2B2AE3D27D4EB4FUL ^ (ulong)c * 0x165667B19E3779F9UL;
        x ^= x >> 30;
        x *= 0xBF58476D1CE4E5B9UL;
        x ^= x >> 27;
        x *= 0x94D049BB133111EBUL;
        x ^= x >> 31;
        return (x >> 11) * (1d / 9007199254740992d);
    }

    // 落成实体：主席(建国的那个人)、驻县办主任，执委会里的几位册为名臣名将
    private static void BindLeaders(Kingdom kingdom, TransmigrantState state, City city, Actor founder)
    {
        SenatorSeat chairman = state.seats[0];
        Bind(state, chairman, founder, city);
        state.chairman_seat = chairman.seat;
        state.chairman_elected_at = World.world.getCurWorldTime();
        state.chairman_terms = 1;
        Actor commander = Realize(kingdom, state, state.seats[1], city);
        if (commander != null && city.leader == null) city.setLeader(commander, true);
        foreach ((int index, TalentKind kind) in new[] { (2, TalentKind.Minister), (5, TalentKind.Minister), (8, TalentKind.General) })
        {
            Actor actor = Realize(kingdom, state, state.seats[index], city);
            if (actor != null) FamousTalentSystem.Bestow(actor, kind);
        }
    }

    private static Actor Realize(Kingdom kingdom, TransmigrantState state, SenatorSeat seat, City city)
    {
        Actor existing = seat.actor_id >= 0 ? World.world.units.get(seat.actor_id) : null;
        if (existing != null && existing.isAlive()) return existing;
        if (city == null || city.isRekt()) city = kingdom.capital;
        if (city == null) return null;
        Actor actor = CityPopulationSystem.SpawnPerson(city, Species, Culture);
        if (actor == null) return null;
        Bind(state, seat, actor, city);
        return actor;
    }

    private static void Bind(TransmigrantState state, SenatorSeat seat, Actor actor, City city)
    {
        SenatorIds.Add(actor.id);
        actor.data.sex = seat.male ? ActorSex.Male : ActorSex.Female;
        int age = Date.getCurrentYear() - seat.birth_year;
        actor.data.age_overgrowth += age - actor.getAge();
        actor.SetFamilyName(seat.surname);
        actor.SetFirstName(seat.given);
        actor.GetModName().SetName(actor);
        seat.actor_id = actor.id;
        string post = PostOf(state, seat);
        actor.RecordPersonalHistory(string.Format(LM.Get(string.IsNullOrEmpty(post) ? "transmigrant_member_history" : "transmigrant_member_post_history"),
            city?.GetCityName() ?? "", post, seat.seat));
    }

    // 穿越政体的殖民地总督：从名册上还没落成的元老里挑一位(原班、三十岁以上优先)，落成在殖民地首府(见 ColonySystem)
    public static Actor RealizeSenator(Kingdom metropole, City city)
    {
        TransmigrantState state = Of(metropole);
        if (state == null || city == null) return null;
        int year = Date.getCurrentYear();
        SenatorSeat seat = state.seats
            .Where(candidate => candidate.actor_id < 0 && year - candidate.birth_year >= 30)
            .OrderBy(candidate => candidate.generation).ThenBy(candidate => candidate.roster < 0 ? 1 : 0)
            .ThenBy(candidate => Unit(metropole.id, candidate.seat, year * 17L)).FirstOrDefault();
        if (seat == null) return null;
        Actor actor = CityPopulationSystem.SpawnPerson(city, Species, Culture);
        if (actor == null) return null;
        Bind(state, seat, actor, city);
        return actor;
    }

    private static SenatorSeat SeatOf(TransmigrantState state, Actor actor) =>
        actor == null ? null : state.seats.FirstOrDefault(seat => seat.actor_id == actor.id);

    // 实体元老故去(见 ActorPatch.Die)：席位当场由子女继承
    public static void OnActorDied(Actor actor)
    {
        if (actor == null) return;
        foreach (Kingdom kingdom in World.world?.kingdoms?.list ?? new List<Kingdom>())
        {
            TransmigrantState state = Of(kingdom);
            SenatorSeat seat = state == null ? null : SeatOf(state, actor);
            if (seat == null) continue;
            Succeed(kingdom, state, seat);
            return;
        }
    }

    // 席位继承：同姓的子女一人，十八到三十四岁；继承人不带原著职务
    private static void Succeed(Kingdom kingdom, TransmigrantState state, SenatorSeat seat)
    {
        string deceased = FullName(seat);
        string post = PostOf(state, seat);
        bool notable = seat.roster >= 0 && seat.roster < CoreRoster || seat.seat == state.chairman_seat;
        int year = Date.getCurrentYear();
        seat.generation++;
        seat.male = Unit(kingdom.id, seat.seat, seat.generation * 31) < 0.6d;
        seat.given = GivenName(seat.male, kingdom.id, seat.seat, seat.generation * 37);
        seat.birth_year = year - InheritAge(kingdom, seat);
        seat.actor_id = -1L;
        seat.roster = -1;
        if (notable)
            TranslateHelper.LogEventOnly(string.Format(LM.Get("transmigrant_seat_inherited"), deceased,
                string.IsNullOrEmpty(post) ? LM.Get("transmigrant_senator") : post, FullName(seat)), kingdom);
    }

    // 元老的天年：原班穿越者是带着现代医药来的现代人，平均活到 FounderMeanAge；继承席位的子孙生在穿越政权治下，
    // 不低于 HeirMeanAge(本文化时代的平均天年更长就照本文化)。按"已经活到这个岁数"截尾取(原班按降临时的年纪，
    // 继承人按继承时的年纪)，不会降临头几年就有年纪大的成批故去
    private const float FounderMeanAge = 78f, HeirMeanAge = 72f;

    private static int DeathAge(Kingdom kingdom, TransmigrantState state, SenatorSeat seat)
    {
        long seed = kingdom.id * 7919L + seat.seat * 104729L + seat.generation * 15485863L;
        float era = RealisticAgeSystem.Enabled ? RealisticAgeSystem.MeanDeathAge(Culture) : HeirMeanAge;
        float mean = Mathf.Max(seat.generation <= 1 ? FounderMeanAge : HeirMeanAge, era);
        int anchor = seat.generation <= 1 ? state.landed_year - seat.birth_year : InheritAge(kingdom, seat);
        return RealisticAgeSystem.SurvivorDeathAge(seed, mean, Mathf.Max(0, anchor));
    }

    // 继承时的年纪(十八到三十四，见 Succeed)
    private static int InheritAge(Kingdom kingdom, SenatorSeat seat) =>
        18 + (int)(Unit(kingdom.id, seat.seat, seat.generation * 41) * 17d);

    // 落成实体的元老(主席、阁员、总督、名将)寿终也按席位的天年(见 RealisticAgeSystem.AgeDeathDue)
    public static bool SenatorDeathAge(Actor actor, out int deathAge)
    {
        deathAge = 0;
        if (actor?.data == null || !SenatorIds.Contains(actor.id)) return false;
        Kingdom kingdom = Active();
        TransmigrantState state = Of(kingdom);
        SenatorSeat seat = state == null ? null : SeatOf(state, actor);
        if (seat == null) return false;
        deathAge = DeathAge(kingdom, state, seat);
        return true;
    }

    private static bool AgesOut => RealisticAgeSystem.Enabled || RealisticAgeSystem.OldAgeEnabled;

    private static bool Alive(SenatorSeat seat, out Actor actor)
    {
        actor = seat.actor_id >= 0 ? World.world.units.get(seat.actor_id) : null;
        return actor == null || actor.isAlive();
    }

    #endregion

    #region 执委会主席

    // 选执委会主席(主席出缺时由 OfficeSelector.Select 调用；任满时由年度结算调用)：从元老里按名望选，
    // 原著执委会的核心人物、原班穿越者、现任(未满两届)占优；连任两届卸任的隔一届不能再选
    public static Actor ElectChairman(Kingdom kingdom) => Elect(kingdom, Of(kingdom), termEnd: false);

    private static Actor Elect(Kingdom kingdom, TransmigrantState state, bool termEnd)
    {
        if (state == null || kingdom?.data == null || kingdom.isRekt()) return null;
        int year = Date.getCurrentYear();
        // 任满改选才论现任(连任、隔届)；出缺补选时原主席的席位已由子女继承，不算现任
        SenatorSeat incumbent = state.seats.FirstOrDefault(seat => seat.seat == state.chairman_seat);
        bool incumbentAlive = termEnd && incumbent != null && Alive(incumbent, out Actor sitting) && sitting != null &&
                              sitting == kingdom.king;
        SenatorSeat best = null;
        double bestScore = double.MinValue;
        foreach (SenatorSeat seat in state.seats)
        {
            if (year - seat.birth_year < ChairmanMinAge || seat.seat == state.barred_seat || !Alive(seat, out _)) continue;
            bool isIncumbent = seat == incumbent && incumbentAlive;
            if (isIncumbent && (!termEnd || state.chairman_terms >= ChairmanMaxTerms)) continue;
            double score = Unit(kingdom.id, seat.seat, year * 13L) * 30d;
            if (seat.roster >= 0 && seat.roster < CoreRoster) score += 40d - seat.roster * 2d;
            if (seat.generation == 1) score += 10d;
            if (isIncumbent) score += 30d;
            // 元老院内阁时期：原著里的元老院主席占优
            if (state.stage >= 5 && seat.roster >= 0 && seat.roster < Roster.Count && Roster[seat.roster].cabinet == RosterChairman)
                score += 60d;
            if (score > bestScore)
            {
                bestScore = score;
                best = seat;
            }
        }
        if (best == null) return null;
        double now = World.world.getCurWorldTime();
        bool reelected = best == incumbent && incumbentAlive;
        if (reelected) state.chairman_terms++;
        else
        {
            state.barred_seat = incumbentAlive && state.chairman_terms >= ChairmanMaxTerms ? incumbent.seat : -1;
            state.chairman_terms = 1;
        }
        state.chairman_seat = best.seat;
        state.chairman_elected_at = now;
        Actor actor = Realize(kingdom, state, best, kingdom.capital);
        if (actor == null) return null;
        string name = actor.getName();
        string text = reelected ? string.Format(LM.Get("transmigrant_chairman_reelected"), name)
            : termEnd && incumbent != null ? string.Format(LM.Get("transmigrant_chairman_elected"), name, FullName(incumbent))
            : string.Format(LM.Get("transmigrant_chairman_vacancy"), name);
        TranslateHelper.LogEventMessage(text, kingdom);
        actor.RecordPersonalHistory(text);
        return actor;
    }

    // 换上新的首领(执委会主席、殖民地总督)：原首领卸任留在原地
    public static void InstallRuler(Kingdom kingdom, Actor actor)
    {
        if (actor == null || kingdom.king == actor) return;
        if (actor.isCityLeader() && actor.city != null) actor.city.removeLeader();
        if (kingdom.hasKing()) kingdom.removeKing();
        OfficeObject office = kingdom.GetOffice();
        if (office != null)
        {
            office.meta_object = kingdom;
            office.SetActor(actor);
        }
        if (kingdom.king != actor) kingdom.setKing(actor);
    }

    #endregion

    #region 年度：席位、改选、阶段、出版

    private static int _year = int.MinValue;
    private static object _world;
    // 元老不投别国：每 HomeCheckSeconds(约一个月)查一次
    private const double HomeCheckSeconds = 5d;
    private static double _homeCheckAt;

    public static void Tick()
    {
        if (World.world == null) return;
        int year = Date.getCurrentYear();
        if (!ReferenceEquals(_world, World.world))
        {
            _world = World.world;
            _year = year;
            _roster = null;
            Pools.Clear();
            _homeCheckAt = 0d;
            SenatorIds.Clear();
            return;
        }
        double now = World.world.getCurWorldTime();
        if (now - _homeCheckAt >= HomeCheckSeconds)
        {
            _homeCheckAt = now;
            KeepSenatorsHome();
        }
        if (year == _year) return;
        _year = year;
        TryRandomArrival();
        ReleaseCulture();
        foreach (Kingdom kingdom in World.world.kingdoms.list.ToList())
        {
            TransmigrantState state = Of(kingdom);
            if (state == null) continue;
            try
            {
                Yearly(kingdom, state, year);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][穿越] 年度结算失败: {exception.Message}");
            }
        }
    }

    private static void Yearly(Kingdom kingdom, TransmigrantState state, int year)
    {
        int inherited = 0;
        foreach (SenatorSeat seat in state.seats)
        {
            if (seat.actor_id >= 0)
            {
                Actor actor = World.world.units.get(seat.actor_id);
                if (actor != null && actor.isAlive()) continue;
                // 实体不在了又没经过 OnActorDied：并回了人口，人还在，回到名册上
                seat.actor_id = -1L;
            }
            if (!AgesOut || year - seat.birth_year < DeathAge(kingdom, state, seat)) continue;
            Succeed(kingdom, state, seat);
            inherited++;
        }
        if (inherited > 0) LogService.LogInfo($"[EmpireCraft][穿越] {kingdom.GetKingdomFullName()} 今年 {inherited} 位元老故去，席位由子女继承");

        if (World.world.getCurWorldTime() - state.chairman_elected_at >= ChairmanTermYears * 60d)
            InstallRuler(kingdom, Elect(kingdom, state, termEnd: true));
        FormGovernment(kingdom);
        Stage(kingdom, state);
        Publish(kingdom, state);
        Naturalize(kingdom);
        Grow(kingdom, state);
    }

    private static readonly string[] StageNodes =
        { "aosong_first_committee", "aosong_common_program", "aosong_great_song", "aosong_cabinet", "aosong_restoration" };
    private const string RosterChairman = "元老院主席";

    public static int StageOf(Kingdom kingdom) => Of(kingdom)?.stage ?? 0;

    // 阶段对应的制度节点(第一阶段是降临时的元老院)，名册窗口用
    public static string StageNode(int stage) =>
        stage <= 1 ? "aosong_senate" : StageNodes[Mathf.Min(stage, StageNodes.Length + 1) - 2];

    public static string NextStageNode(int stage) => stage - 1 < StageNodes.Length ? StageNodes[Mathf.Max(0, stage - 1)] : null;

    public const int TermYears = ChairmanTermYears;

    // ───── 名册窗口用 ─────
    public static bool IsCore(SenatorSeat seat) => seat != null && seat.roster >= 0 && seat.roster < CoreRoster;

    public static string GroupOf(SenatorSeat seat) =>
        seat != null && seat.roster >= 0 && seat.roster < Roster.Count ? Roster[seat.roster].group : "";

    public static Actor ActorOf(SenatorSeat seat)
    {
        Actor actor = seat != null && seat.actor_id >= 0 ? World.world.units.get(seat.actor_id) : null;
        return actor != null && actor.isAlive() ? actor : null;
    }

    public static int AgeOf(SenatorSeat seat) => seat == null ? 0 : Mathf.Max(0, Date.getCurrentYear() - seat.birth_year);

    // 特别市：元老院内阁以后的都城与经济重镇(一万五千人以上)
    public static bool SpecialCity(City city) =>
        city?.kingdom != null && StageOf(city.kingdom) >= 5 &&
        (city.isCapitalCity() || CityPopulationSystem.GetTotal(city) >= 15000f);

    private static void Stage(Kingdom kingdom, TransmigrantState state)
    {
        int stage = 1;
        for (int i = 0; i < StageNodes.Length; i++)
            if (InstitutionSystem.IsEnacted(Culture, StageNodes[i])) stage = i + 2;
        if (stage <= state.stage) return;
        string before = kingdom.GetKingdomFullName();
        int previous = state.stage;
        state.stage = stage;
        if (stage >= 4)
        {
            kingdom.SetCustomCountryName(LM.Get("transmigrant_song_name"));
            // 北伐功成：行在还都，国号去掉"澳洲行在"
            kingdom.SetCustomCountrySuffix(stage >= 6 ? "" : LM.Get("transmigrant_song_suffix"));
            SyncEmpireName(kingdom);
        }
        TranslateHelper.LogMajor(string.Format(LM.Get($"transmigrant_stage_{stage}_history"), before, kingdom.GetKingdomFullName()), kingdom);
        // 元老院内阁：第三次全体大会当场改选，城名改设特别市
        if (previous < 5 && stage >= 5)
        {
            InstallRuler(kingdom, Elect(kingdom, state, termEnd: true));
            foreach (City city in kingdom.cities.ToList()) city.RefreshCityDisplayName();
        }
    }

    // 检疫营(第一届执委会以后)：本国与殖民地各城每年把 NaturalizeShare 的外族背景人口编为归化民(澳洲人)，城的文化占比随之挪
    private const float NaturalizeShare = 0.08f, NaturalizeShift = 4f;

    private static void Naturalize(Kingdom kingdom)
    {
        if (!InstitutionSystem.IsEnacted(Culture, "aosong_first_committee")) return;
        var realms = new List<Kingdom> { kingdom };
        realms.AddRange(ColonySystem.ColoniesOf(kingdom));
        float moved = 0f;
        foreach (Kingdom realm in realms)
            foreach (City city in realm.cities.ToList())
            {
                CityPopulationData data = CityPopulationSystem.Get(city);
                if (data?.groups == null) continue;
                foreach (PopGroup group in data.groups.Where(group => group != null && group.culture != Culture && group.Background > 0f).ToList())
                {
                    float removed = CityPopulationSystem.RemoveBackground(group, group.Background * NaturalizeShare);
                    if (removed <= 0f) continue;
                    CityPopulationSystem.AddBackground(city, group.social_class, Culture, group.species, group.ideology, removed);
                    moved += removed;
                }
                CultureService.ApplyAssimilation(city, Culture, NaturalizeShift);
            }
        if (moved >= 1f) CityPopulationSystem.InvalidateHouseholdCaches();
    }

    // 华夏诸国(穿越者以外的华夏天下文化)打穿越政体：剿髡之战
    public static void NameWar(War war)
    {
        string name = WarName(war);
        if (name != null) war.data.name = name;
    }

    // 战争目标定下后按目标重起名时也先看这个(见 WarCatalog.Compose)
    public static string WarName(War war)
    {
        if (war?.data == null) return null;
        Kingdom attacker = war.getMainAttacker(), defender = war.getMainDefender();
        if (attacker == null || !IsTransmigrant(defender)) return null;
        string culture = CultureService.GetRealmCulture(attacker);
        if (culture == Culture || !CultureService.IsValidCulture(culture) || OnomasticsRule.GetTianxiaId(culture) != HomeCulture)
            return null;
        return string.Format(LM.Get("transmigrant_war_name"), attacker.GetKingdomName());
    }

    // 降临即是一国之政府(同现代政体组建政府，见 ModernStateFormationSystem.FormGovernment)：进帝国层，享有帝国的全部机制。
    // 降临时建；没建成(或帝国散了)的每年补建
    private static void FormGovernment(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt() || !kingdom.hasKing() || kingdom.IsEmpire() || kingdom.IsInEmpire()) return;
        Empire empire = ModClass.EMPIRE_MANAGER?.NewEmpire(kingdom, allowCultureRival: true, forceNewCore: true,
            suppressFoundingLog: true);
        if (empire == null) return;
        SyncEmpireName(kingdom);
        if (kingdom.GetRegime()?.type != RegimeType.Transmigrant)
        {
            kingdom.SetRegimeType(RegimeType.Transmigrant);
            kingdom.LoadRegime();
        }
        EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(kingdom);
    }

    // 帝国名就是国号(临高、大宋)，后缀照国家的(元老院、澳洲行在)
    private static void SyncEmpireName(Kingdom kingdom)
    {
        Empire empire = kingdom.GetEmpire();
        if (empire?.CoreKingdom == kingdom && !string.IsNullOrWhiteSpace(kingdom.GetCustomCountryName()))
            empire.SetEmpireName(kingdom.GetCustomCountryName());
    }

    // 澳洲人文化只归穿越政权与它的殖民地：别国打下澳洲人的城、分出去的部落随了城里的文化，改记华夏，
    // 免得成了没有元老院席位的"元老院"政体(见 CultureService.SetRealmCulture)
    private const string HomeCulture = "Huaxia";

    public static string AdoptableCulture(Kingdom kingdom, string culture)
    {
        if (culture != Culture || Landing || IsTransmigrant(kingdom) || IsTransmigrant(ColonySystem.MetropoleOf(kingdom)))
            return culture;
        return CultureService.IsValidCulture(HomeCulture) ? HomeCulture : culture;
    }

    // 以前已经随了澳洲人文化的别国改回华夏
    private static void ReleaseCulture()
    {
        foreach (Kingdom kingdom in World.world.kingdoms.list.ToList())
        {
            if (kingdom?.data == null || kingdom.isRekt() || CultureService.GetRealmCulture(kingdom) != Culture) continue;
            string culture = AdoptableCulture(kingdom, Culture);
            if (culture != Culture) CultureService.SetRealmCulture(kingdom, culture, updatePoliticalSystem: true);
        }
    }

    // 世界法则"穿越者随机降临"：世上有文化进了中世纪以后，每年 ArrivalChance；先找地图上的临高，没有就找华夏一处海边的城
    private const float ArrivalChance = 0.1f;
    private const int ArrivalEra = 5;

    private static void TryRandomArrival()
    {
        if (EmpireCraft.Scripts.GameLibrary.EmpireCraftWorldLawLibrary.empirecraft_law_transmigrant_arrival?.isEnabled() != true) return;
        if (Active() != null || UnityEngine.Random.value >= ArrivalChance) return;
        if (!World.world.kingdoms.list.Any(kingdom => kingdom?.data != null && !kingdom.isRekt() &&
                TechnologySystem.GetEraTier(TechnologySystem.GetCultureOf(kingdom)) >= ArrivalEra)) return;
        WorldTile target = LingaoTile() ?? HuaxiaCoast();
        if (target == null) return;
        string error = Land(target, out Kingdom kingdom);
        if (error != null) LogService.LogInfo($"[EmpireCraft][穿越] 随机降临没成：{LM.Get(error)}");
    }

    private static WorldTile LingaoTile()
    {
        foreach (City city in World.world.cities.list)
            if (city?.data?.name != null && !city.isRekt() && (city.data.name.StartsWith("临高") || city.data.name.StartsWith("臨高")))
                return city.getTile();
        foreach (RuralSettlement settlement in RuralSettlementSystem.Save())
            if (settlement != null && !settlement.cleared && settlement.ruined_at < 0d && settlement.name != null &&
                (settlement.name.StartsWith("临高") || settlement.name.StartsWith("臨高")))
                return World.world.GetTile(settlement.x, settlement.y);
        return null;
    }

    private static WorldTile HuaxiaCoast()
    {
        List<City> coast = World.world.cities.list
            .Where(city => city?.kingdom != null && !city.isRekt() && city.zones.Any(zone => SeaLanding.Waters(zone).Count > 0))
            .ToList();
        List<City> huaxia = coast.Where(city => OnomasticsRule.GetTianxiaId(CultureService.GetMainCulture(city)) == "Huaxia").ToList();
        List<City> pool = huaxia.Count > 0 ? huaxia : coast;
        return pool.Count == 0 ? null : pool[UnityEngine.Random.Range(0, pool.Count)].getTile();
    }

    // 出版指导小组：只放比本国落后两个时代以上、不涉军事政治的技术(兵书、政论锁在大图书馆里)
    private static void Publish(Kingdom kingdom, TransmigrantState state)
    {
        double now = World.world.getCurWorldTime();
        if (!InstitutionSystem.IsEnacted(Culture, "aosong_publishing") || now < state.next_publish_at) return;
        state.next_publish_at = now + PublishYears * 60d;
        int ceiling = TechnologySystem.GetEraTier(Culture) - 2;
        List<TechNodeConfig> books = TechnologySystem.Techs
            .Where(tech => tech.tier <= ceiling && SafeBranches.Contains(tech.branch) && TechnologySystem.HasTech(Culture, tech.id) &&
                           !state.released_techs.Contains(tech.id))
            .OrderBy(tech => tech.tier).ThenBy(tech => tech.id).Take(PublishPerRound).ToList();
        if (books.Count == 0) return;
        var readers = new HashSet<string>(TechnologySystem.GetContacts(Culture));
        foreach (City city in kingdom.cities) readers.Add(CultureService.GetMainCulture(city));
        readers.Remove(Culture);
        readers.RemoveWhere(culture => !CultureService.IsValidCulture(culture) || !TechnologySystem.IsActiveFor(culture));
        foreach (TechNodeConfig book in books)
        {
            state.released_techs.Add(book.id);
            int reached = 0;
            foreach (string culture in readers)
            {
                if (TechnologySystem.HasTech(culture, book.id)) continue;
                TechnologySystem.AddTechProgressFraction(culture, book.id, PublishProgress);
                reached++;
            }
            string title = string.Format(LM.Get("transmigrant_book_title"), TechnologySystem.GetTechName(book.id));
            TranslateHelper.LogEventOnly(string.Format(LM.Get("transmigrant_publish_history"), title, reached), kingdom);
        }
    }

    #endregion
}

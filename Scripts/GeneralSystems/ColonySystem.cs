using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 殖民体系(用户 2026-10-28："把殖民系统也给写了，以后还要加公司政体(东印度公司)、总督什么的""公司政体应该是研究了金融学以后才有的"；
// 问答定：殖民地是附属政权、海外城自动编入加主动殖民、公司政体一起做、研究远洋航海的都能殖民)：
//   · 殖民地自成一国(总督府或特许公司)，记着宗主国(KingdomExtraData.colony)；不是封建附庸(附庸隔海就放走)，也不入帝国；
//     宗主国与殖民地、同一宗主的殖民地之间不开战(见 Bound，接在 WarGate、CanDeclareExternalWar、WarReach、参战闸)，
//     殖民地随宗主出征、宗主保护殖民地(见 FeudalVassalService.ExpandSide)；地图上是自己与宗主的合成色；不再认别的宗主、不称帝、不入帝国；
//   · 编入：研究出远洋航海的国家，离都城够远的海外城(不在都城那座岛、相距 ColonyDistance 地块以上、不是半壁江山)每年按岛编成殖民地，
//     那座岛上已有本国殖民地的并进去；
//   · 主动殖民：每年有机会派殖民船去海路够得着的无主海岸(别的岛)，航行几个月后建城、迁去 Settlers 口人，编进殖民地；
//     研究出金融以后，派出的殖民船多半由特许公司经营；
//   · 总督府：总督由宗主国任免，任期 GovernorTermYears 年，出缺当场补派(穿越政体的宗主国派元老去当总督)；每年把经常收入的
//     RemitShare 解送宗主国；
//   · 特许公司：董事会选总督(同投票)，自己养兵、开战；国库超出储备的余钱每年一半分红给宗主国；做到 NationalizeCities 座城，
//     或亏空成债，就被宗主国收归国有、改设总督府；
//   · 离心：路远、立得久、宗主不如自己强、宗主在打仗就涨，满了起兵独立(复用"附庸独立"这类战争)，打赢就脱离，打输压回去。
public static class ColonySystem
{
    public const string OceanTech = "ocean_navigation", CompanyTech = "banking";
    private const int ColonyDistance = 36;            // 地块
    private const int GovernorTermYears = 6;
    private const float RemitShare = 0.3f, DividendShare = 0.5f;
    private const int NationalizeCities = 6;
    private const int VoyageCooldownYears = 8, VoyageCost = 600, Settlers = 300, VoyageMinCapital = 1500;
    private const float VoyageChance = 0.35f, CompanyChance = 0.6f;
    private const int RiseMinYears = 30, RiseCooldownYears = 10;
    private const int StartingGrant = 200;

    // 建殖民城时挡住建城后的钩子(见 CityPatch.CityBuilt)；殖民地起兵独立时放行开战闸(见 WarReach.MayDeclare)
    public static bool Founding { get; private set; }
    [ThreadStatic] private static bool _rising;
    public static bool Rising => _rising;

    #region 关系

    public static ColonyState Of(Kingdom kingdom) => kingdom?.data == null || kingdom.isRekt() ? null : kingdom.GetOrCreate().colony;

    public static Kingdom MetropoleOf(Kingdom kingdom)
    {
        ColonyState state = Of(kingdom);
        if (state == null || state.metropole_id < 0 || World.world?.kingdoms == null) return null;
        Kingdom metropole = World.world.kingdoms.get(state.metropole_id);
        return metropole != null && metropole != kingdom && !metropole.isRekt() ? metropole : null;
    }

    public static bool IsColony(Kingdom kingdom) => MetropoleOf(kingdom) != null;

    public static IEnumerable<Kingdom> ColoniesOf(Kingdom metropole) =>
        metropole == null || World.world?.kingdoms == null ? Enumerable.Empty<Kingdom>()
            : World.world.kingdoms.list.Where(kingdom => kingdom != null && MetropoleOf(kingdom) == metropole);

    // 宗主与殖民地、同一宗主的两块殖民地：互不开战
    public static bool Bound(Kingdom a, Kingdom b)
    {
        if (a == null || b == null || a == b) return false;
        Kingdom ma = MetropoleOf(a), mb = MetropoleOf(b);
        return ma == b || mb == a || ma != null && ma == mb;
    }

    // 这场仗是不是殖民地起兵独立(攻方是殖民地、守方是它的宗主)
    public static bool IsIndependenceWar(War war) =>
        war?.data != null && MetropoleOf(war.getMainAttacker()) is Kingdom metropole && metropole == war.getMainDefender();

    // 参战闸：不站到自己宗主、自己殖民地的对面(殖民地自己的独立战争除外)
    public static bool JoinsAgainstKin(War war, Kingdom kingdom, bool attackers)
    {
        if (war?.data == null || kingdom == null || IsIndependenceWar(war)) return false;
        IEnumerable<long> opposite = attackers ? war.data.list_defenders : war.data.list_attackers;
        if (opposite == null) return false;
        foreach (long id in opposite)
        {
            Kingdom other = World.world.kingdoms.get(id);
            if (other != null && Bound(kingdom, other)) return true;
        }
        return false;
    }

    #endregion

    #region 年度

    private static int _year = int.MinValue;
    private static object _world;
    private static readonly Queue<Kingdom> Pending = new();
    private const int PerFrame = 6;

    public static void Tick()
    {
        if (World.world == null) return;
        int year = Date.getCurrentYear();
        if (!ReferenceEquals(_world, World.world))
        {
            _world = World.world;
            _year = year;
            Pending.Clear();
            return;
        }
        if (year != _year)
        {
            _year = year;
            foreach (Kingdom kingdom in World.world.kingdoms.list) Pending.Enqueue(kingdom);
        }
        for (int i = 0; i < PerFrame && Pending.Count > 0; i++)
        {
            Kingdom kingdom = Pending.Dequeue();
            if (kingdom?.data == null || kingdom.isRekt()) continue;
            try
            {
                if (Of(kingdom) != null) Colony(kingdom);
                else
                {
                    Incorporate(kingdom);
                    Voyage(kingdom);
                }
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][殖民] {kingdom.GetKingdomName()} 年度结算失败: {exception.Message}");
            }
        }
    }

    // 能当宗主国：独立国或帝国核心、不是别人的殖民地、本国采纳了远洋航海
    private static bool CanColonize(Kingdom kingdom) =>
        kingdom?.capital != null && !kingdom.isRekt() && kingdom.isCiv() && !IsColony(kingdom) &&
        (!kingdom.IsInEmpire() || kingdom.IsEmpire()) && TechnologySystem.RealmHasTech(kingdom, OceanTech);

    private static object IslandOf(City city) => city?.getTile()?.region?.island;

    private static int Distance(City a, City b)
    {
        TileZone za = a?.getTile()?.zone, zb = b?.getTile()?.zone;
        return za == null || zb == null ? 0 : Mathf.Abs(za.x - zb.x) + Mathf.Abs(za.y - zb.y);
    }

    #endregion

    #region 编入与设立

    private static void Incorporate(Kingdom realm)
    {
        if (!CanColonize(realm)) return;
        object home = IslandOf(realm.capital);
        if (home == null) return;
        int homeCount = realm.cities.Count(city => IslandOf(city) == home);
        // 海外看行政控制力算出的"必须走海路才连得上"(见 AdministrativeControlSystem)：原版的岛会被山脉、高峰切开，
        // 同一块大陆上隔着山的城不能当成海外
        var groups = realm.cities
            .Where(city => city != null && !city.isRekt() && city != realm.capital && IslandOf(city) is object island &&
                           island != home && AdministrativeControlSystem.Known(city) && city.GetOrCreate().admin_overseas &&
                           Distance(city, realm.capital) >= ColonyDistance)
            .GroupBy(IslandOf).ToList();
        foreach (var group in groups)
        {
            // 那边才是大半壁江山的不算殖民地
            List<City> cities = group.OrderByDescending(city => city.getPopulationPeople()).ToList();
            if (cities.Count > 1 && cities.Count * 2 > homeCount) continue;
            Kingdom existing = ColoniesOf(realm).FirstOrDefault(colony => IslandOf(colony.capital) == group.Key);
            if (existing != null)
            {
                foreach (City city in cities) city.joinAnotherKingdom(existing);
                continue;
            }
            Found(realm, cities, ColonyState.Governorate);
        }
    }

    // 剧本开局直接设的殖民地(世界地图：荷兰东印度公司、菲律宾总督府……)；给了名字用它作国号，后缀照总督府、公司
    public static Kingdom Establish(Kingdom metropole, List<City> cities, bool company, string name)
    {
        Kingdom colony = Found(metropole, cities, company ? ColonyState.Company : ColonyState.Governorate);
        if (colony != null && !string.IsNullOrWhiteSpace(name)) colony.SetCustomCountryName(name);
        return colony;
    }

    // 一组城拆出去立为殖民地(总督府或特许公司)
    private static Kingdom Found(Kingdom metropole, List<City> cities, int kind) =>
        AdministrativeControlSystem.ReleaseToLocal(metropole, cities, colony => Setup(colony, metropole, kind), out _);

    private static string Setup(Kingdom colony, Kingdom metropole, int kind)
    {
        double now = World.world.getCurWorldTime();
        var state = new ColonyState { metropole_id = metropole.id, kind = kind, founded_at = now, remitted_at = now };
        colony.GetOrCreate().colony = state;
        string culture = CultureService.GetRealmCulture(metropole);
        if (CultureService.IsValidCulture(culture))
            CultureService.SetRealmCulture(colony, culture, updatePoliticalSystem: false, allowLegacyInstitutions: false);
        ApplyRegime(colony, kind);
        Name(colony, metropole, kind);
        VassalColorService.Apply(colony, metropole);
        if (metropole.GetMoney() > StartingGrant * 2)
        {
            metropole.SubMoney(StartingGrant, TreasuryCategory.Construction);
            colony.AddMoney(StartingGrant, TreasuryCategory.InternalTransfer);
        }
        string ruler;
        if (kind == ColonyState.Governorate)
        {
            Actor governor = AppointGovernor(colony, metropole);
            TransmigrantSystem.InstallRuler(colony, governor);
            state.governor_id = governor?.id ?? -1L;
            state.governor_since = now;
            ruler = governor?.getName() ?? "";
        }
        else ruler = colony.king?.getName() ?? "";
        TranslateHelper.LogEventMessage(string.Format(LM.Get(kind == ColonyState.Company ? "colony_company_founded" : "colony_founded"),
            metropole.GetKingdomFullName(), colony.GetKingdomFullName(), ruler), metropole);
        return "colony";
    }

    private static void ApplyRegime(Kingdom colony, int kind)
    {
        colony.SetRegimeType(kind == ColonyState.Company ? RegimeType.Company : RegimeType.Colonial);
        colony.LoadRegime();
        colony.GetOrCreate().regime_manually_selected = true;
    }

    // 总督府叫"<首府>总督府"；特许公司叫"<宗主国><首府>公司"
    private static void Name(Kingdom colony, Kingdom metropole, int kind)
    {
        string seat = colony.capital?.GetCityName() ?? colony.GetKingdomName();
        string core = colony.capital?.GetOrCreate().core_name;
        if (!string.IsNullOrEmpty(core)) seat = core;
        if (kind == ColonyState.Company)
        {
            colony.SetCustomCountryName(metropole.GetKingdomName() + seat);
            colony.SetCustomCountrySuffix(LM.Get("colony_company_suffix"));
        }
        else
        {
            colony.SetCustomCountryName(seat);
            colony.SetCustomCountrySuffix(LM.Get("colony_governorate_suffix"));
        }
    }

    #endregion

    #region 总督

    // 总督府出缺(见 OfficeSelector.Select)：宗主国派人；公司由董事会照投票选，这里不管
    public static Actor PickGovernor(Kingdom colony)
    {
        ColonyState state = Of(colony);
        Kingdom metropole = MetropoleOf(colony);
        if (state == null || metropole == null || state.kind != ColonyState.Governorate) return null;
        Actor governor = AppointGovernor(colony, metropole);
        if (governor == null) return null;
        state.governor_id = governor.id;
        state.governor_since = World.world.getCurWorldTime();
        return governor;
    }

    private static Actor AppointGovernor(Kingdom colony, Kingdom metropole)
    {
        City seat = colony.capital ?? colony.cities.FirstOrDefault();
        if (seat == null) return null;
        Actor governor = TransmigrantSystem.IsTransmigrant(metropole) ? TransmigrantSystem.RealizeSenator(metropole, seat) : null;
        governor ??= CityPopulationSystem.SpawnPerson(seat, metropole.getSpecies(), CultureService.GetRealmCulture(metropole));
        governor?.RecordPersonalHistory(string.Format(LM.Get("colony_governor_history"), metropole.GetKingdomFullName(),
            colony.GetKingdomFullName()));
        return governor;
    }

    #endregion

    #region 殖民地的年度：任期、解送、分红、收归国有、离心独立

    private static void Colony(Kingdom colony)
    {
        ColonyState state = Of(colony);
        Kingdom metropole = MetropoleOf(colony);
        if (metropole == null)
        {
            // 宗主亡了：殖民地就此独立
            Release(colony, string.Format(LM.Get("colony_orphaned"), colony.GetKingdomFullName()));
            return;
        }
        double now = World.world.getCurWorldTime();
        VassalColorService.Apply(colony, metropole);
        if (state.kind == ColonyState.Governorate)
        {
            if (state.governor_since >= 0d && now - state.governor_since >= GovernorTermYears * 60d && !colony.hasEnemies())
            {
                Actor next = AppointGovernor(colony, metropole);
                if (next != null)
                {
                    string former = colony.king?.getName() ?? "";
                    TransmigrantSystem.InstallRuler(colony, next);
                    state.governor_id = next.id;
                    state.governor_since = now;
                    TranslateHelper.LogEventOnly(string.Format(LM.Get("colony_governor_rotated"), colony.GetKingdomFullName(),
                        next.getName(), former), metropole);
                }
            }
            int remit = Mathf.Min(Mathf.Max(0, colony.GetMoney()),
                (int)(StateDebtSystem.AnnualStableIncome(colony) * RemitShare));
            if (remit > 0)
            {
                colony.SubMoney(remit, TreasuryCategory.Tribute);
                metropole.AddMoney(remit, TreasuryCategory.Tribute);
            }
        }
        else
        {
            long surplus = TreasuryHoardSystem.Surplus(colony);
            int dividend = (int)Math.Min(int.MaxValue, (long)(surplus * DividendShare));
            if (dividend > 0)
            {
                colony.SubMoney(dividend, TreasuryCategory.Tribute);
                metropole.AddMoney(dividend, TreasuryCategory.Tribute);
                state.dividends += dividend;
            }
            if (colony.cities.Count >= NationalizeCities || colony.GetTreasuryBalance() < 0 && now - state.founded_at >= 10 * 60d)
            {
                Nationalize(colony, metropole, state);
                return;
            }
        }
        state.remitted_at = now;
        Drift(colony, metropole, state, now);
    }

    // 特许公司收归国有，改设总督府
    private static void Nationalize(Kingdom colony, Kingdom metropole, ColonyState state)
    {
        string before = colony.GetKingdomFullName();
        state.kind = ColonyState.Governorate;
        ApplyRegime(colony, state.kind);
        Name(colony, metropole, state.kind);
        Actor governor = AppointGovernor(colony, metropole);
        TransmigrantSystem.InstallRuler(colony, governor);
        state.governor_id = governor?.id ?? -1L;
        state.governor_since = World.world.getCurWorldTime();
        TranslateHelper.LogEventMessage(string.Format(LM.Get("colony_nationalized"), metropole.GetKingdomFullName(), before,
            colony.GetKingdomFullName()), metropole);
    }

    private static void Drift(Kingdom colony, Kingdom metropole, ColonyState state, double now)
    {
        float gain = 2f + Mathf.Min(4f, Distance(colony.capital, metropole.capital) / 40f);
        double own = colony.GetNationalPower(), lord = metropole.GetNationalPower();
        if (lord < own * 1.5d) gain += 4f;
        if (metropole.hasEnemies()) gain += 3f;
        if (state.kind == ColonyState.Company) gain *= 0.7f;
        state.autonomy = Mathf.Clamp(state.autonomy + gain, 0f, 100f);
        if (state.autonomy < 100f || now - state.founded_at < RiseMinYears * 60d || colony.hasEnemies() || !colony.hasKing() ||
            state.rising_at >= 0d && now - state.rising_at < RiseCooldownYears * 60d) return;
        // 宗主强过自己三倍又不在打仗的，不敢起兵
        if (lord > own * 3d && !metropole.hasEnemies()) return;
        War war;
        _rising = true;
        try
        {
            war = World.world.diplomacy.startWar(colony, metropole, WarTypeLibrary.normal);
        }
        finally
        {
            _rising = false;
        }
        if (war == null) return;
        war.SetEmpireWarType(EmpireWarType.附庸独立);
        state.rising_at = now;
        TranslateHelper.LogEventMessage(string.Format(LM.Get("colony_rising"), colony.GetKingdomFullName(), metropole.GetKingdomFullName()),
            metropole);
    }

    // 独立战争结束(见 FeudalVassalService.ResolveIndependenceWar)：殖民地胜则脱离，败则离心压回去
    public static void ResolveIndependenceWar(War war, WarWinner winner)
    {
        if (!IsIndependenceWar(war)) return;
        Kingdom colony = war.getMainAttacker();
        if (winner == WarWinner.Attackers) Release(colony, string.Format(LM.Get("colony_independent"), colony.GetKingdomFullName(),
            war.getMainDefender().GetKingdomFullName()));
        else if (Of(colony) is ColonyState state) state.autonomy = 30f;
    }

    // 殖民地脱离宗主：按本文化重定政体、去掉殖民地名号与合成色
    public static void Release(Kingdom colony, string reason)
    {
        if (colony?.data == null || Of(colony) == null) return;
        colony.GetOrCreate().colony = null;
        colony.GetOrCreate().regime_manually_selected = false;
        string culture = CultureService.GetRealmCulture(colony);
        if (CultureService.IsValidCulture(culture))
            CultureService.SetRealmCulture(colony, culture, updatePoliticalSystem: true, allowLegacyInstitutions: false);
        colony.SetCustomCountryName("");
        colony.SetCustomCountrySuffix("");
        VassalColorService.Clear(colony);
        if (!string.IsNullOrEmpty(reason)) TranslateHelper.LogEventMessage(reason, colony);
    }

    #endregion

    #region 主动殖民

    private static void Voyage(Kingdom realm)
    {
        if (!CanColonize(realm)) return;
        var data = realm.GetOrCreate();
        double now = World.world.getCurWorldTime();
        if (data.colonial_voyage_zone >= 0)
        {
            if (now >= data.colonial_voyage_arrive) Arrive(realm, data);
            return;
        }
        if (data.colonial_voyage_at >= 0d && now < data.colonial_voyage_at) return;
        if (realm.hasEnemies() || realm.GetMoney() < VoyageCost * 2 ||
            CityPopulationSystem.GetBackgroundTotal(realm.capital) < VoyageMinCapital || UnityEngine.Random.value >= VoyageChance) return;
        TileZone target = FindSite(realm, out int distance);
        data.colonial_voyage_at = now + VoyageCooldownYears * 60d;
        if (target == null) return;
        realm.SubMoney(VoyageCost, TreasuryCategory.Construction);
        data.colonial_voyage_zone = target.id;
        data.colonial_voyage_arrive = now + (6 + distance / 40) * 5d;
        TranslateHelper.LogEventOnly(string.Format(LM.Get("colony_voyage_departed"), realm.GetKingdomFullName(),
            realm.capital.GetCityName()), realm);
    }

    // 海路够得着(本国港口出发、不穿敌方制海、在航程以内)的别的岛上的无主海岸，按地力挑
    private static TileZone FindSite(Kingdom realm, out int distance)
    {
        distance = 0;
        Dictionary<int, int> reach = SeaLanding.Reachable(realm);
        if (reach == null || reach.Count == 0) return null;
        int range = NavySystem.MaxRange(realm);
        object home = IslandOf(realm.capital);
        TileZone capital = realm.capital.getTile()?.zone;
        var options = new List<(TileZone zone, float score, int distance)>();
        foreach (TileZone zone in World.world.zone_calculator.zones)
        {
            if (zone == null || zone.city != null || zone.tiles_with_ground < 48 || !zone.canStartCityHere()) continue;
            if (zone.centerTile?.region?.island == home) continue;
            int far = capital == null ? 0 : Mathf.Abs(zone.x - capital.x) + Mathf.Abs(zone.y - capital.y);
            if (far < ColonyDistance / 2) continue;
            bool reachable = false;
            foreach (SeaRegionSystem.Region water in SeaLanding.Waters(zone))
                if (reach.TryGetValue(water.Id, out int hops) && hops <= range)
                {
                    reachable = true;
                    break;
                }
            if (!reachable || zone.neighbours_all.Any(next => next?.city != null)) continue;
            float score = zone.tiles_with_ground + MarketDemand(realm, zone) * 40f + UnityEngine.Random.value * 20f;
            options.Add((zone, score, far));
        }
        if (options.Count == 0) return null;
        var best = options.OrderByDescending(option => option.score).First();
        distance = best.distance;
        return best.zone;
    }

    // 本国市场缺、这块地出的物资(0~1，见 CommodityMarketSystem.ColonyDemand)：殖民就有了去处
    private static float MarketDemand(Kingdom realm, TileZone zone) => CommodityMarketSystem.ColonyDemand(realm, zone);

    private static void Arrive(Kingdom realm, KingdomExtension.KingdomExtraData data)
    {
        TileZone zone = World.world.zone_calculator.getZoneByID(data.colonial_voyage_zone);
        data.colonial_voyage_zone = -1;
        if (zone == null || zone.city != null || !zone.canStartCityHere() || realm.capital == null) return;
        Actor founder = realm.capital.leader ?? realm.king;
        if (founder == null) return;
        City city;
        Founding = true;
        try
        {
            city = World.world.cities.buildNewCity(founder, zone);
        }
        finally
        {
            Founding = false;
        }
        if (city == null) return;
        string culture = CultureService.GetRealmCulture(realm);
        CityPopulationSystem.TransferCivilianBackground(realm.capital, city, Settlers);
        if (CultureService.IsValidCulture(culture)) CultureService.SetCityMainCulture(city, culture);
        CityPopulationData population = CityPopulationSystem.Get(city);
        if (population != null) CityPopulationSystem.Census(city, population, keepBackground: true, preserveSmallGroups: true);
        CityPopulationSystem.EnsureLeader(city);
        if (population != null) CityPopulationSystem.Census(city, population, keepBackground: true, preserveSmallGroups: true);
        CityConstructionSystem.StartSettlement(city);
        TranslateHelper.LogEventOnly(string.Format(LM.Get("colony_voyage_arrived"), realm.GetKingdomFullName(), city.GetCityName()), realm);
        Kingdom existing = ColoniesOf(realm).FirstOrDefault(colony => IslandOf(colony.capital) == IslandOf(city));
        if (existing != null)
        {
            city.joinAnotherKingdom(existing);
            return;
        }
        int kind = TechnologySystem.RealmHasTech(realm, CompanyTech) && UnityEngine.Random.value < CompanyChance
            ? ColonyState.Company : ColonyState.Governorate;
        Found(realm, new List<City> { city }, kind);
    }

    #endregion
}

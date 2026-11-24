using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 公司(用户 2026-11-25："还有公司""国家可以成立国企和私企，具体会显示在地图上，像素的建筑附上公司的名字可以点开查看经营业务"
// "按历史上的资本主义来发展""企业可以互相并购，哄抬市场价格彼此之间互相竞技"
// "外国也可以通过并购他国的能源企业达到控制国家或者影响国家决策的效果，或者关键企业")。
// 公司绑在厂区上(FactorySite.company_id)：工业时代起，有近代厂的厂区都归一家公司经营，地图上厂区标公司的简称，点开看它的业务。
//   · 国企与私企：国库出钱办的、收归国有的厂区归国企(一国一业一家)；民间出钱办的各成一家私企，东家是当地的一个宗族。
//     厂区小窗口里改官营民营、诉求"国有化""私有化"都跟着改；名字按总部那座城的文化起(见 CompanyNaming)；
//   · 账：每年按年产值、声誉、行情、加价算盈利，私企攒成资本；声誉逐年涨，萧条里跌；
//   · 照资本主义的路子走：
//       工业时代是自由竞争——一片厂区一家小公司，萧条里声誉差、赔光了本的倒闭，厂子易主；
//       电气时代起是兼并与垄断——同业的大公司拿资本并购小公司，倒闭的由同业接盘；一家(或一个财阀)占到行业五成就哄抬市价，
//       占得越多抬得越高(至多三成)：厂的产值多，市民、劳工怨气涨，"反托拉斯"的呼声也涨；头两名规模相近时可能打价格战，
//       当年压价、两败俱伤，输光了的被对手吞并；财阀跨行业收购别的私企(控股，见 ConglomerateSystem)；
//   · 外资：电气时代起，资本最厚的私企每年有机会收购通商的弱国里的关键企业(钢铁、化工、重工、飞机、重型船厂)，
//     利润四成汇回。外国在一国关键行业里控到三成，那国就受制于它(不向它开战、不同它打关税战、好感高)；
//     控到六成，连通商政策都改不成保护关税、闭关，市民、劳工怨"利权外溢"。出路是国有化——没收外资，
//     被没收的一方记恨二十年、当场打起关税战。
// 股票与股市另见 StockMarketSystem，财阀干政见 ConglomerateSystem。数值都是模组自己的规则。
public static class CompanySystem
{
    public const int TrustEra = 8;
    private const float Margin = 0.1f, StartReputation = 25f;
    private const float MergeChance = 0.25f, EarlyMergeChance = 0.05f, PriceWarChance = 0.15f, FailChance = 0.2f, GroupBuyChance = 0.15f;
    private const float GougeFrom = 0.5f, GougePerShare = 0.6f, GougeBase = 0.05f, MaxGouge = 0.3f, PriceWarCut = -0.08f;
    private const float ForeignBidChance = 0.15f, ForeignRemit = 0.4f, WeakerShare = 0.8f;
    public const float InfluenceAt = 0.3f, ControlAt = 0.6f;
    private const int SeizureGrudgeYears = 20, MaxNotes = 8, MinGroupScale = 4;
    public const int BeholdenOpinion = 20, SeizureOpinion = -40;

    // 关键行业：钢铁、化工(炼油)、重工、飞机、重型船厂
    public static readonly HashSet<string> Strategic = new()
        { "steel_mill", "chemical_plant", "heavy_factory", "light_aircraft_factory", "heavy_aircraft_factory", "heavy_shipyard" };

    private static CompanyBook _book = new();
    private static readonly Dictionary<int, CompanyData> Index = new();
    // 帝国编号 → 行业 → 加价(负 = 价格战)；每年重算，不随存档(读档后头一年按不加价)
    private static readonly Dictionary<long, Dictionary<string, float>> Markups = new();

    #region 存取

    public static void Load(CompanyBook book)
    {
        _book = book ?? new CompanyBook();
        _book.companies ??= new List<CompanyData>();
        _book.exchanges ??= new List<StockExchangeData>();
        Index.Clear();
        foreach (CompanyData company in _book.companies)
            if (company != null) Index[company.id] = company;
        Markups.Clear();
        _clansYear = int.MinValue;
    }

    public static CompanyBook Save() => _book;
    public static void ResetWorldState() => Load(null);
    internal static CompanyBook Book => _book;
    public static IReadOnlyList<CompanyData> All => _book.companies;

    public static CompanyData ById(int id) => id > 0 && Index.TryGetValue(id, out CompanyData company) ? company : null;
    public static CompanyData Of(FactorySite site) => site == null ? null : ById(site.company_id);

    public static City Home(CompanyData company)
    {
        City city = company == null || company.city_id < 0 ? null : World.world?.cities?.get(company.city_id);
        return city?.data == null || city.isRekt() ? null : city;
    }

    // 公司的国籍：总部那座城眼下归谁
    public static Empire RealmOf(CompanyData company) => Home(company)?.kingdom?.GetEmpire();

    private static Empire Living(long id)
    {
        Empire empire = id < 0 ? null : ModClass.EMPIRE_MANAGER?.get(id);
        return empire?.data == null || empire.isRekt() || empire.IsArchived() || empire.CoreKingdom == null ? null : empire;
    }

    public static Empire ForeignOwner(CompanyData company) => company == null ? null : Living(company.foreign_empire_id);

    public static List<FactorySite> Sites(CompanyData company)
    {
        var list = new List<FactorySite>();
        Empire realm = RealmOf(company);
        if (realm == null) return list;
        foreach (City city in ClaimPolicySystem.Cities(realm))
            foreach (FactorySite site in FactorySystem.InCity(city))
                if (site.company_id == company.id) list.Add(site);
        return list;
    }

    private static int ScaleOf(FactorySite site) => site.units.Sum(unit => Math.Max(1, unit.level));
    private static float ValueOf(FactorySite site) => site.units.Sum(FactorySystem.ValueOf);

    // 身价：上市的按市值，没上市的按盈利与家底估
    public static float Worth(CompanyData company) => company == null ? 0f
        : company.listed ? company.price * StockMarketSystem.Shares
        : Mathf.Max(0f, company.profit * 8f + company.capital) + company.value * 0.5f;

    public static string OwnershipName(CompanyData company)
    {
        if (company == null) return "";
        if (company.state) return LM.Get("company_kind_state");
        if (ForeignOwner(company) is { } foreign) return string.Format(LM.Get("company_kind_foreign"), foreign.GetEmpireFullName());
        return LM.Get(company.group_clan_id >= 0 ? "company_kind_group" : "company_kind_private");
    }

    // 地图上厂区标的名字(见 MapLabelOverlay)
    public static string MapLabel(FactorySite site) => Of(site)?.short_name ?? "";

    public static void Rename(CompanyData company, string name)
    {
        string value = (name ?? "").Trim();
        if (company == null || value.Length == 0 || value == company.name) return;
        company.name = value.Length > 16 ? value.Substring(0, 16) : value;
        company.short_name = company.name.Length > 8 ? company.name.Substring(0, 8) : company.name;
        Relabel(company);
    }

    // 这座城的地图标注重排(独立核心没有地图，不必)
    private static void RelabelCity(long cityId)
    {
#if !EC_SERVER
        MapLabelOverlay.Invalidate(cityId);
#endif
    }

    private static void Relabel(CompanyData company)
    {
        foreach (FactorySite site in Sites(company))
            if (FactorySystem.CityOf(site) is { } city) RelabelCity(city.id);
    }

    internal static void Note(CompanyData company, string text)
    {
        company.notes ??= new List<CompanyNote>();
        company.notes.Add(new CompanyNote { at = World.world.getCurWorldTime(), text = text });
        while (company.notes.Count > MaxNotes) company.notes.RemoveAt(0);
    }

    #endregion

    #region 年度

    private static int Era(Empire empire) => TechnologySystem.GetEraTier(InstitutionSystem.GetPrimaryCulture(empire));

    // 每年(ConstitutionalEconomySystem，先于财阀)
    public static void Update(Empire empire, ConstitutionalEconomyState state)
    {
        if (empire?.CoreKingdom == null || state == null) return;
        state.cartel_sectors ??= new List<string>();
        state.foreign_control ??= new Dictionary<long, float>();
        state.seizures ??= new Dictionary<long, double>();
        if (!ModernPolicySystem.Industrial(empire)) return;
        double now = World.world.getCurWorldTime();
        int era = Era(empire);
        List<CompanyData> own = _book.companies.Where(company => RealmOf(company) == empire).ToList();
        // 财阀没了(拆了、垮了、绝了)的旗下公司各自独立；控股的外国亡了的回到本地资本
        foreach (CompanyData company in own)
        {
            if (company.group_clan_id >= 0 && state.conglomerates?.Any(house => house.clan_id == company.group_clan_id) != true)
                company.group_clan_id = -1L;
            if (company.foreign_empire_id >= 0 && (ForeignOwner(company) == null || ForeignOwner(company) == empire))
            {
                company.foreign_empire_id = -1L;
                company.foreign_company_id = 0;
            }
        }
        var used = new HashSet<string>(own.Select(company => company.short_name));
        var sites = new Dictionary<int, List<FactorySite>>();
        foreach (City city in ClaimPolicySystem.Cities(empire).ToList())
            foreach (FactorySite site in FactorySystem.InCity(city))
            {
                // 手工工场不成公司
                if (!site.units.Any(unit => FactorySystem.IsModern(unit.type))) continue;
                // 国有化的国家没有民营的厂
                if (state.nationalization >= ModernPolicySystem.Nationalized) site.state = true;
                CompanyData company = Of(site);
                if (company == null || company.state != site.state || !own.Contains(company))
                    company = Assign(empire, state, city, site, company, own, used, now);
                if (!sites.TryGetValue(company.id, out List<FactorySite> list)) sites[company.id] = list = new List<FactorySite>();
                list.Add(site);
            }
        // 厂区没了的公司散掉
        foreach (CompanyData company in own.Where(company => !sites.ContainsKey(company.id)).ToList())
        {
            own.Remove(company);
            Remove(company);
        }
        if (own.Count == 0)
        {
            state.price_gouging = 0f;
            state.foreign_control.Clear();
            Markups.Remove(empire.id);
            return;
        }
        foreach (CompanyData company in own) Account(empire, state, company, sites[company.id]);
        Compete(empire, state, own, sites, era, now);
        if (era >= TrustEra)
        {
            GroupBuys(empire, state, own);
            BidAbroad(empire, own, now);
        }
        ControlLedger(state, own);
        StockMarketSystem.Update(empire, state, own, now);
    }

    // 厂区小窗口里改了官营民营：当场换东家(见 FactoryWindow)
    public static void Reassign(FactorySite site)
    {
        City city = FactorySystem.CityOf(site);
        Empire empire = city?.kingdom?.GetEmpire();
        ConstitutionalEconomyState state = empire?.data?.constitutional_economy;
        if (state == null || !ModernPolicySystem.Industrial(empire) || !site.units.Any(unit => FactorySystem.IsModern(unit.type))) return;
        state.seizures ??= new Dictionary<long, double>();
        state.foreign_control ??= new Dictionary<long, float>();
        List<CompanyData> own = _book.companies.Where(company => RealmOf(company) == empire).ToList();
        Assign(empire, state, city, site, Of(site), own, new HashSet<string>(own.Select(company => company.short_name)),
            World.world.getCurWorldTime());
    }

    private static string MainSector(FactorySite site) => site.units.Where(unit => FactorySystem.IsModern(unit.type))
        .OrderByDescending(FactorySystem.ValueOf).Select(unit => unit.type).FirstOrDefault() ?? "";

    // 这片厂区归哪家公司：国企并进本国同业的那一家，私企另立一家
    private static CompanyData Assign(Empire empire, ConstitutionalEconomyState state, City city, FactorySite site, CompanyData previous,
        List<CompanyData> own, HashSet<string> used, double now)
    {
        string sector = MainSector(site);
        // 外资的厂被收归国有：没收(见 Seize)
        if (previous != null && site.state && !previous.state && previous.foreign_empire_id >= 0 && own.Contains(previous))
            Seize(empire, state, previous, now);
        CompanyData company = site.state ? own.FirstOrDefault(other => other.state && other.sector == sector) : null;
        if (company == null)
        {
            string culture = CultureService.GetMainCulture(city) ?? "";
            long seed = site.id * 7919L + city.id;
            company = new CompanyData
            {
                id = _book.next_id++, sector = sector, culture = culture, state = site.state, city_id = city.id, founded = now,
                reputation = StartReputation + Math.Abs(seed) % 15, capital = ValueOf(site) * 0.5f
            };
            if (site.state)
                (company.name, company.short_name) = CompanyNaming.State(culture, city.GetCityName(), sector);
            else
            {
                SpecificClan clan = PickOwner(empire, city, seed);
                company.clan_id = clan?.id ?? -1L;
                company.owner = Surname(clan);
                (company.name, company.short_name) = CompanyNaming.Private(culture, city.GetCityName(), sector, company.owner, seed, used,
                    !RepublicSystem.IsRepublic(empire));
            }
            used.Add(company.short_name);
            _book.companies.Add(company);
            Index[company.id] = company;
            own.Add(company);
            Note(company, string.Format(LM.Get(site.state ? "company_note_founded_state" : "company_note_founded"), city.GetCityName()));
        }
        site.company_id = company.id;
        RelabelCity(city.id);
        return company;
    }

    public static string Surname(SpecificClan clan) =>
        clan == null ? "" : !string.IsNullOrEmpty(clan.shi) ? clan.shi.TrimEnd('氏') : clan.name ?? "";

    // 东家：祖籍在这座城的宗族里挑一家(皇室不算)，没有就看城主的宗族
    private static readonly Dictionary<long, List<SpecificClan>> ClansByCity = new();
    private static int _clansYear = int.MinValue;

    private static SpecificClan PickOwner(Empire empire, City city, long seed)
    {
        int year = Date.getCurrentYear();
        if (year != _clansYear)
        {
            _clansYear = year;
            ClansByCity.Clear();
            foreach (SpecificClan clan in SpecificClanManager._specificClans)
            {
                if (clan == null || clan.ancestral_city_id < 0 || clan.Count <= 0) continue;
                if (!ClansByCity.TryGetValue(clan.ancestral_city_id, out List<SpecificClan> list))
                    ClansByCity[clan.ancestral_city_id] = list = new List<SpecificClan>();
                list.Add(clan);
            }
        }
        long royal = empire.Emperor != null && !RepublicSystem.IsRepublic(empire) ? empire.Emperor.GetSpecificClan()?.id ?? -1L : -1L;
        if (ClansByCity.TryGetValue(city.id, out List<SpecificClan> local))
        {
            List<SpecificClan> fit = local.Where(clan => clan.id != royal).ToList();
            if (fit.Count > 0) return fit[(int)(Math.Abs(seed) % fit.Count)];
        }
        SpecificClan leader = city.leader != null && !city.leader.isRekt() ? city.leader.GetSpecificClan() : null;
        return leader != null && leader.id != royal ? leader : null;
    }

    private static void Remove(CompanyData company)
    {
        _book.companies.Remove(company);
        Index.Remove(company.id);
        // 它名下的外国子公司放回本地资本
        foreach (CompanyData child in _book.companies)
            if (child.foreign_company_id == company.id)
            {
                child.foreign_company_id = 0;
                child.foreign_empire_id = -1L;
            }
    }

    // 一年的账：规模、产值、盈利、资本、声誉
    private static void Account(Empire empire, ConstitutionalEconomyState state, CompanyData company, List<FactorySite> sites)
    {
        company.scale = sites.Sum(ScaleOf);
        company.value = sites.Sum(ValueOf);
        // 总部那座城不再有它的厂：挪到厂最大的那座城
        if (!sites.Any(site => FactorySystem.CityOf(site)?.id == company.city_id) &&
            FactorySystem.CityOf(sites.OrderByDescending(ScaleOf).First()) is { } seat) company.city_id = seat.id;
        bool crisis = state.cycle_phase is EconomicCycleSystem.Panic or EconomicCycleSystem.Depression;
        float cycle = state.cycle_phase switch
        {
            EconomicCycleSystem.Boom => 1.1f,
            EconomicCycleSystem.Overheat => 1.2f,
            EconomicCycleSystem.Panic => -0.5f,
            EconomicCycleSystem.Depression => 0.3f,
            _ => 1f
        };
        float gouge = Markup(empire.id, company.sector);
        company.profit = company.value * Margin * cycle * (1f + 2f * gouge) * (0.6f + 0.8f * company.reputation / 100f);
        if (company.state) company.capital = 0f;
        else if (ById(company.foreign_company_id) is { } parent && company.profit > 0f)
        {
            // 外资：利润四成汇回母公司
            parent.capital += company.profit * ForeignRemit;
            company.capital += company.profit * (1f - ForeignRemit);
        }
        else company.capital += company.profit;
        float gain = company.state ? 1.5f : 3f;
        if (state.cycle_phase is EconomicCycleSystem.Boom or EconomicCycleSystem.Overheat) gain += 2f;
        if (crisis && !company.state) gain = -6f;
        company.reputation = Mathf.Clamp(company.reputation + gain, 5f, 100f);
    }

    // 谁说了算：财阀旗下的并作一家算，外资按控股国并，其余各算各的
    private static long ControllerKey(CompanyData company) =>
        company.group_clan_id >= 0 ? company.group_clan_id + 1_000_000_000L
        : company.foreign_empire_id >= 0 ? -company.foreign_empire_id - 2L : -1_000_000_000L - company.id;

    private static bool Independent(CompanyData company) =>
        !company.state && company.group_clan_id < 0 && company.foreign_empire_id < 0;

    private static string ProductName(string sector)
    {
        if (CommodityMarketSystem.Table.factories.TryGetValue(sector, out CommodityRecipe recipe) && recipe.outputs.Count > 0 &&
            CommodityMarketSystem.TryGetGood(recipe.outputs.OrderByDescending(pair => pair.Value).First().Key, out CommodityConfig good))
            return DiplomacyLedger.GoodName(good);
        return CompanyNaming.SectorName(sector);
    }

    // 行业里的较量：哄抬、价格战、倒闭、并购
    private static void Compete(Empire empire, ConstitutionalEconomyState state, List<CompanyData> own,
        Dictionary<int, List<FactorySite>> sites, int era, double now)
    {
        var markups = new Dictionary<string, float>();
        bool crisis = state.cycle_phase is EconomicCycleSystem.Panic or EconomicCycleSystem.Depression;
        bool cooled = state.antitrust_at >= 0d && Date.getYearsSince(state.antitrust_at) < 10;
        float weighted = 0f, weight = 0f;
        foreach (IGrouping<string, CompanyData> trade in own.GroupBy(company => company.sector).ToList())
        {
            List<CompanyData> all = trade.ToList();
            int total = all.Sum(company => company.scale);
            if (total <= 0) continue;
            List<CompanyData> firms = all.Where(company => !company.state).OrderByDescending(company => company.scale).ToList();
            float gouge = 0f;
            if (firms.Count > 0)
            {
                IGrouping<long, CompanyData> lead = firms.GroupBy(ControllerKey).OrderByDescending(group => group.Sum(company => company.scale)).First();
                float top = lead.Sum(company => company.scale) / (float)total;
                if (top >= GougeFrom) gouge = Mathf.Min(MaxGouge, GougeBase + (top - GougeFrom) * GougePerShare);
                CompanyData boss = lead.OrderByDescending(company => company.scale).First();
                if (gouge >= 0.1f && !state.cartel_sectors.Contains(trade.Key))
                {
                    state.cartel_sectors.Add(trade.Key);
                    string text = string.Format(LM.Get("company_gouging_history"), boss.name, empire.GetEmpireFullName(),
                        CompanyNaming.SectorName(trade.Key), ProductName(trade.Key));
                    Note(boss, text);
                    EventRecorder.RecordRoutine(empire, text);
                }
                else if (gouge < 0.05f) state.cartel_sectors.Remove(trade.Key);
            }
            // 价格战：头两名规模相近，各有各的东家
            if (gouge <= 0f && firms.Count >= 2 && firms[1].scale >= firms[0].scale * 0.6f &&
                ControllerKey(firms[0]) != ControllerKey(firms[1]) && UnityEngine.Random.value < PriceWarChance)
            {
                gouge = PriceWarCut;
                CompanyData a = firms[0], b = firms[1];
                foreach (CompanyData side in new[] { a, b })
                {
                    side.capital -= side.value * 0.1f;
                    side.reputation = Mathf.Max(5f, side.reputation - 3f);
                }
                string text = string.Format(LM.Get("company_price_war_history"), a.name, b.name, ProductName(trade.Key));
                Note(a, text);
                Note(b, text);
                EventRecorder.RecordRoutine(empire, text);
                CompanyData loser = a.capital <= b.capital ? a : b, winner = loser == a ? b : a;
                if (loser.capital < 0f && era >= TrustEra && Independent(loser))
                {
                    Merge(empire, winner, loser, sites, "company_merged_price_war_history");
                    own.Remove(loser);
                    firms.Remove(loser);
                }
            }
            // 倒闭：赔光了本的，萧条里声誉差的
            foreach (CompanyData firm in firms.ToList())
            {
                if (!Independent(firm)) continue;
                bool broke = firm.capital < -firm.value * 0.3f || crisis && firm.reputation < 30f && UnityEngine.Random.value < FailChance;
                if (!broke) continue;
                CompanyData rescuer = era >= TrustEra
                    ? firms.Where(other => other != firm && other.capital >= 0f).OrderByDescending(other => other.reputation).FirstOrDefault()
                    : null;
                if (rescuer != null)
                {
                    Merge(empire, rescuer, firm, sites, "company_merged_bankrupt_history");
                    own.Remove(firm);
                    firms.Remove(firm);
                }
                else Reorganize(empire, firm, sites[firm.id], own);
            }
            // 并购：最大的一家拿资本买下最小的独立私企
            firms = firms.OrderByDescending(company => company.scale).ToList();
            if (firms.Count >= 2)
            {
                CompanyData buyer = firms[0];
                CompanyData target = firms.Where(other => other != buyer && Independent(other)).OrderBy(other => other.scale).FirstOrDefault();
                float chance = (era >= TrustEra ? MergeChance : EarlyMergeChance) * (crisis ? 2f : 1f) *
                               (state.finance_policy > 0 ? 1.5f : 1f) * (cooled ? 0.3f : 1f);
                if (target != null && buyer.foreign_empire_id < 0)
                {
                    float price = Worth(target) * 1.3f;
                    if (buyer.capital >= price && UnityEngine.Random.value < chance)
                    {
                        buyer.capital -= price;
                        Merge(empire, buyer, target, sites, "company_merged_history");
                        own.Remove(target);
                    }
                }
            }
            markups[trade.Key] = gouge;
            weighted += gouge * total;
            weight += total;
        }
        Markups[empire.id] = markups;
        state.price_gouging = weight > 0f ? weighted / weight : 0f;
    }

    // 并购：被并的厂区、家底归买家，名号就此消失
    private static void Merge(Empire empire, CompanyData buyer, CompanyData target, Dictionary<int, List<FactorySite>> sites, string key)
    {
        if (sites.TryGetValue(target.id, out List<FactorySite> moved))
        {
            foreach (FactorySite site in moved)
            {
                site.company_id = buyer.id;
                if (FactorySystem.CityOf(site) is { } city) RelabelCity(city.id);
            }
            if (!sites.TryGetValue(buyer.id, out List<FactorySite> list)) sites[buyer.id] = list = new List<FactorySite>();
            list.AddRange(moved);
            sites.Remove(target.id);
        }
        int scale = Math.Max(1, buyer.scale + target.scale);
        buyer.reputation = Mathf.Clamp((buyer.reputation * buyer.scale + target.reputation * target.scale) / scale - 3f, 5f, 100f);
        buyer.scale = scale;
        buyer.value += target.value;
        if (target.capital > 0f) buyer.capital += target.capital;
        string text = string.Format(LM.Get(key), buyer.name, target.name);
        Note(buyer, text);
        Remove(target);
        EventRecorder.RecordRoutine(empire, text);
    }

    // 自由竞争的年代倒闭：厂子易主，另起名号
    private static void Reorganize(Empire empire, CompanyData company, List<FactorySite> sites, List<CompanyData> own)
    {
        City city = Home(company);
        if (city == null) return;
        string old = company.name;
        long seed = company.id * 104729L + (long)World.world.getCurWorldTime();
        SpecificClan clan = PickOwner(empire, city, seed);
        company.clan_id = clan?.id ?? -1L;
        company.owner = Surname(clan);
        var used = new HashSet<string>(own.Select(other => other.short_name));
        (company.name, company.short_name) = CompanyNaming.Private(company.culture, city.GetCityName(), company.sector, company.owner, seed, used,
            !RepublicSystem.IsRepublic(empire));
        company.reputation = 20f;
        company.capital = 0f;
        company.listed = false;
        company.prices?.Clear();
        string text = string.Format(LM.Get("company_bankrupt_history"), old, company.name);
        Note(company, text);
        EventRecorder.RecordRoutine(empire, text);
        foreach (FactorySite site in sites)
            if (FactorySystem.CityOf(site) is { } where) RelabelCity(where.id);
    }

    #endregion

    #region 财阀

    public static List<CompanyData> GroupCompanies(Empire empire, long clanId) => clanId < 0 ? new List<CompanyData>()
        : _book.companies.Where(company => company.group_clan_id == clanId && RealmOf(company) == empire).ToList();

    // 这个财阀占本国工业几成(本国还没有公司返回 -1，财阀照旧按抽象的份额算)
    public static float GroupShare(Empire empire, long clanId)
    {
        int total = 0, held = 0;
        foreach (CompanyData company in _book.companies)
        {
            if (RealmOf(company) != empire) continue;
            total += company.scale;
            if (company.group_clan_id == clanId) held += company.scale;
        }
        return total <= 0 ? -1f : held / (float)total;
    }

    // 财阀起家的那家公司：规模最大、有东家的独立私企(见 ConglomerateSystem.Found)
    public static CompanyData FlagshipCandidate(Empire empire, ICollection<long> taken, long royal) =>
        _book.companies.Where(company => Independent(company) && company.clan_id >= 0 && company.clan_id != royal &&
                                         !taken.Contains(company.clan_id) && company.scale >= MinGroupScale && RealmOf(company) == empire)
            .OrderByDescending(company => company.scale).FirstOrDefault();

    // 世家出资办实业：买下最大的一家独立私企
    public static CompanyData LargestIndependent(Empire empire) =>
        _book.companies.Where(company => Independent(company) && RealmOf(company) == empire)
            .OrderByDescending(company => company.scale).FirstOrDefault();

    public static void JoinGroup(CompanyData company, long clanId, string groupName)
    {
        if (company == null) return;
        company.group_clan_id = clanId;
        Note(company, string.Format(LM.Get("company_note_joined_group"), groupName));
    }

    // 反托拉斯、财阀垮台：旗下公司各自独立。返回放出来几家
    public static int ReleaseGroup(Empire empire, long clanId, string groupName)
    {
        List<CompanyData> held = GroupCompanies(empire, clanId);
        foreach (CompanyData company in held)
        {
            company.group_clan_id = -1L;
            Note(company, string.Format(LM.Get("company_note_left_group"), groupName));
        }
        return held.Count;
    }

    // 财阀跨行业收购：拿旗下的资本买别的行业里最大的独立私企(控股，不并名号)
    private static void GroupBuys(Empire empire, ConstitutionalEconomyState state, List<CompanyData> own)
    {
        if (state.conglomerates == null) return;
        foreach (ConglomerateData house in state.conglomerates)
        {
            List<CompanyData> held = own.Where(company => company.group_clan_id == house.clan_id).ToList();
            if (held.Count == 0 || UnityEngine.Random.value >= GroupBuyChance) continue;
            var trades = new HashSet<string>(held.Select(company => company.sector));
            CompanyData target = own.Where(company => Independent(company) && !trades.Contains(company.sector))
                .OrderByDescending(company => company.scale).FirstOrDefault();
            if (target == null) continue;
            float price = Worth(target) * 1.3f, purse = held.Sum(company => Mathf.Max(0f, company.capital));
            if (purse < price) continue;
            foreach (CompanyData company in held)
                if (company.capital > 0f) company.capital -= price * company.capital / purse;
            JoinGroup(target, house.clan_id, house.name);
            EventRecorder.RecordRoutine(empire, string.Format(LM.Get("company_group_bought_history"), house.name, target.name));
        }
    }

    #endregion

    #region 外资

    // 资本最厚的本国私企去通商的弱国收购关键企业
    private static void BidAbroad(Empire empire, List<CompanyData> own, double now)
    {
        CompanyData buyer = own.Where(company => !company.state && company.foreign_empire_id < 0)
            .OrderByDescending(company => company.capital).FirstOrDefault();
        if (buyer == null || buyer.capital <= 0f || UnityEngine.Random.value >= ForeignBidChance) return;
        Kingdom core = empire.CoreKingdom;
        CommodityMarketSystem.Market market = CommodityMarketSystem.MarketOf(core);
        if (market == null) return;
        var hosts = new HashSet<Empire>();
        foreach (Kingdom member in market.members)
            if (member?.GetEmpire() is { } inside && inside != empire) hosts.Add(inside);
        foreach (long leaderId in market.linked)
            if (World.world.kingdoms.get(leaderId)?.GetEmpire() is { } outside && outside != empire) hosts.Add(outside);
        hosts.RemoveWhere(host => !OpenToForeign(empire, host));
        if (hosts.Count == 0) return;
        CompanyData best = null;
        float bestPrice = 0f;
        foreach (CompanyData company in _book.companies)
        {
            if (!Independent(company) || !Strategic.Contains(company.sector) || !(RealmOf(company) is { } host) || !hosts.Contains(host)) continue;
            float price = Worth(company) * (company.listed ? 1.3f : 1.5f);
            if (buyer.capital < price || best != null && company.scale <= best.scale) continue;
            best = company;
            bestPrice = price;
        }
        if (best == null) return;
        Empire seat = RealmOf(best);
        buyer.capital -= bestPrice;
        best.foreign_empire_id = empire.id;
        best.foreign_company_id = buyer.id;
        best.foreign_since = now;
        string text = string.Format(LM.Get("company_foreign_bought_history"), buyer.name, empire.GetEmpireFullName(), best.name,
            seat.GetEmpireFullName(), CompanyNaming.SectorName(best.sector));
        Note(best, text);
        Note(buyer, text);
        WorldOrderSystem.Record(text, seat, new[] { empire });
    }

    // 这个国家的企业买不买得：没有国有化、没有闭关、没在打仗、国力不及买方八成、没收过买方的产业还没过二十年的不卖
    private static bool OpenToForeign(Empire buyer, Empire host)
    {
        ConstitutionalEconomyState state = host?.data?.constitutional_economy;
        if (state == null || host.CoreKingdom == null || host.isRekt() || host.IsArchived()) return false;
        if (state.nationalization >= ModernPolicySystem.Nationalized || buyer.CoreKingdom.isEnemy(host.CoreKingdom)) return false;
        Kingdom leader = CommodityMarketSystem.MarketOf(host.CoreKingdom)?.leader ?? host.CoreKingdom;
        if (CommodityMarketSystem.PolicyOf(leader) == TradePolicy.Closed) return false;
        if (state.seizures != null && state.seizures.TryGetValue(buyer.id, out double at) && Date.getYearsSince(at) < SeizureGrudgeYears)
            return false;
        return host.GetNationalPower() < buyer.GetNationalPower() * WeakerShare;
    }

    // 各外国在本国关键行业里控着几成
    private static void ControlLedger(ConstitutionalEconomyState state, List<CompanyData> own)
    {
        state.foreign_control.Clear();
        int total = own.Where(company => Strategic.Contains(company.sector)).Sum(company => company.scale);
        if (total <= 0) return;
        foreach (CompanyData company in own)
        {
            if (company.foreign_empire_id < 0 || !Strategic.Contains(company.sector) || ForeignOwner(company) == null) continue;
            state.foreign_control.TryGetValue(company.foreign_empire_id, out float share);
            state.foreign_control[company.foreign_empire_id] = share + company.scale / (float)total;
        }
    }

    public static float ForeignControl(Empire host, Empire foreign) =>
        host?.data?.constitutional_economy?.foreign_control is { } ledger && foreign != null &&
        ledger.TryGetValue(foreign.id, out float share) ? share : 0f;

    // 受制于人：对方在本国关键行业里控到三成(见 WarGate、TradeWarSystem、好感)
    public static bool Beholden(Kingdom host, Kingdom foreign)
    {
        Empire a = host?.GetEmpire(), b = foreign?.GetEmpire();
        return a != null && b != null && a != b && ForeignControl(a, b) >= InfluenceAt;
    }

    // 命脉操于人手：有外国控到六成，返回那个国家
    public static Empire Master(Empire host)
    {
        if (!(host?.data?.constitutional_economy?.foreign_control is { Count: > 0 } ledger)) return null;
        KeyValuePair<long, float> top = ledger.OrderByDescending(pair => pair.Value).First();
        return top.Value >= ControlAt ? Living(top.Key) : null;
    }

    // 没收外资(国有化、厂区改官营)：被没收的一方记恨二十年，当场打起关税战
    private static void Seize(Empire host, ConstitutionalEconomyState state, CompanyData company, double now)
    {
        Empire foreign = ForeignOwner(company);
        company.foreign_empire_id = -1L;
        company.foreign_company_id = 0;
        if (foreign == null) return;
        state.seizures[foreign.id] = now;
        state.foreign_control.Remove(foreign.id);
        WorldOrderSystem.Record(string.Format(LM.Get("company_seized_history"), host.GetEmpireFullName(), company.name,
            foreign.GetEmpireFullName()), host, new[] { foreign });
        TradeWarSystem.Start(foreign, host, "seizure");
    }

    // 好感：受制于对方 +20；自己的产业被对方没收过(二十年内) −40
    public static int OpinionOf(Kingdom main, Kingdom target)
    {
        Empire a = main?.GetEmpire(), b = target?.GetEmpire();
        if (a == null || b == null || a == b) return 0;
        int opinion = ForeignControl(a, b) >= InfluenceAt ? BeholdenOpinion : 0;
        if (b.data?.constitutional_economy?.seizures is { } seized && seized.TryGetValue(a.id, out double at) &&
            Date.getYearsSince(at) < SeizureGrudgeYears) opinion += SeizureOpinion;
        return opinion;
    }

    #endregion

    #region 效果

    public static float Markup(long empireId, string sector) =>
        Markups.TryGetValue(empireId, out Dictionary<string, float> table) && table.TryGetValue(sector ?? "", out float value) ? value : 0f;

    // 一座厂的产值(见 FactorySystem.Produce)：公司的声誉、垄断加价或价格战、外资把利润汇走
    public static float OutputFactor(City city, FactorySite site, string type)
    {
        CompanyData company = Of(site);
        if (company == null) return 1f;
        float factor = 1f + (company.state ? 0.06f : 0.12f) * company.reputation / 100f;
        if (company.foreign_empire_id >= 0) factor *= 0.9f;
        Empire empire = city?.kingdom?.GetEmpire();
        return empire == null ? factor : factor * (1f + Markup(empire.id, type));
    }

    // 阶层怨气的目标值加减(见 InstitutionSystem)：垄断抬价；命脉操于外人之手
    public static float GrievanceShift(Empire empire, SocialClass socialClass)
    {
        if (socialClass is not (SocialClass.Citizen or SocialClass.Labour)) return 0f;
        ConstitutionalEconomyState state = empire?.data?.constitutional_economy;
        if (state == null) return 0f;
        return state.price_gouging * 40f + (Master(empire) != null ? 6f : 0f);
    }

    #endregion
}

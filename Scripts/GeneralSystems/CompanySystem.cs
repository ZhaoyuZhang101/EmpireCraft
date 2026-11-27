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
//     被没收的一方记恨二十年、当场打起关税战。没有买得起的企业时，就去通商的国家设厂开分公司(至多三处)；
//   · 国家持股：国库可以入股私企，照股份分利润，入到三成外资买不走，五成起国家控股(见"国家持股"一段)。
// 股票与股市另见 StockMarketSystem，财阀干政见 ConglomerateSystem。
// 古代(用户 2026-11-28 "可是古代的公司不是什么股份有限公司什么的""能不能模拟历史"，问答定"古代也有商号")：
//   · 窑、纸坊、书坊、冶铁、木船厂、火药坊这类手工工场各归一家商号(华夏某某号、日本某某屋、西方某某商行)，铸钱与官府出钱办的归官局；
//   · 商号不并购、不打价格战：同业到几家结行会定价(加一点价)；官府每年有机会把一个行业特许给最大的商号当皇商专营，
//     皇商多得几成价、每年把盈利的几成交官府，同业别家声誉受挤；商号不上市；
//   · 企业形态随本国时代改组(商号 → 公司、局 → 有限公司 → 集团，见 CompanyRules.forms)：字号留着、名号换格式，本国进到新的一档那年记一笔。
// 特许公司(东印度公司这类殖民地政权，见 ColonySystem)也算宗主国的公司：上宗主国的交易所、分红按股份分(见 CompanySystem.Charter.cs)。
// 数值、形态、起名格式都在 CompanyRules.json(政体编辑器「杂项配置 → 公司与商号」可改)，都是模组自己的规则。
public static partial class CompanySystem
{
    private static CompanyNumbers N => CompanyRules.Numbers;
    public static int TrustEra => N.trust_era;
    private static float Margin => N.margin;
    private static float StartReputation => N.start_reputation;
    private static float MergeChance => N.merge_chance;
    private static float EarlyMergeChance => N.early_merge_chance;
    private static float PriceWarChance => N.price_war_chance;
    private static float FailChance => N.fail_chance;
    private static float GroupBuyChance => N.group_buy_chance;
    private static float GougeFrom => N.gouge_from;
    private static float GougePerShare => N.gouge_per_share;
    private static float GougeBase => N.gouge_base;
    private static float MaxGouge => N.max_gouge;
    private static float PriceWarCut => N.price_war_cut;
    private static float ForeignBidChance => N.foreign_bid_chance;
    private static float ForeignRemit => N.foreign_remit;
    private static float WeakerShare => N.weaker_share;
    // 去别国设厂：至少这么多资本、或自己年产值的这么多成；一家公司至多几处分公司
    private static float BranchMinCost => N.branch_min_cost;
    private static float BranchCostShare => N.branch_cost_share;
    private static int MaxBranches => N.max_branches;
    public static float InfluenceAt => N.influence_at;
    public static float ControlAt => N.control_at;
    private static int SeizureGrudgeYears => N.seizure_grudge_years;
    private static int MinGroupScale => N.min_group_scale;
    private const int MaxNotes = 8;
    public static int BeholdenOpinion => N.beholden_opinion;
    public static int SeizureOpinion => N.seizure_opinion;

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
        IndexCharters();
        Markups.Clear();
        _clansYear = int.MinValue;
    }

    public static CompanyBook Save() => _book;

    public static void ResetWorldState()
    {
        Load(null);
        CompanyRules.Reload();
    }
    internal static CompanyBook Book => _book;
    public static IReadOnlyList<CompanyData> All => _book.companies;

    public static CompanyData ById(int id) => id > 0 && Index.TryGetValue(id, out CompanyData company) ? company : null;
    public static CompanyData Of(FactorySite site) => site == null ? null : ById(site.company_id);

    public static City Home(CompanyData company)
    {
        if (company != null && company.charter_kingdom_id >= 0) return CharterColony(company)?.capital;
        City city = company == null || company.city_id < 0 ? null : World.world?.cities?.get(company.city_id);
        return city?.data == null || city.isRekt() ? null : city;
    }

    // 公司的国籍：总部那座城眼下归谁；特许公司算宗主国的
    public static Empire RealmOf(CompanyData company)
    {
        if (company == null) return null;
        if (company.charter_kingdom_id >= 0) return ColonySystem.MetropoleOf(CharterColony(company))?.GetEmpire();
        return Home(company)?.kingdom?.GetEmpire();
    }

    public static bool IsCharter(CompanyData company) => company != null && company.charter_kingdom_id >= 0;

    private static Empire Living(long id)
    {
        Empire empire = id < 0 ? null : ModClass.EMPIRE_MANAGER?.get(id);
        return empire?.data == null || empire.isRekt() || empire.IsArchived() || empire.CoreKingdom == null ? null : empire;
    }

    public static Empire ForeignOwner(CompanyData company) => company == null ? null : Living(company.foreign_empire_id);

    public static List<FactorySite> Sites(CompanyData company)
    {
        var list = new List<FactorySite>();
        Empire realm = IsCharter(company) ? null : RealmOf(company);
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
        if (IsCharter(company)) return LM.Get("company_kind_charter");
        // 商号：官办的叫官办，皇商另标
        if (CompanyRules.FormOf(company).guild)
            return LM.Get(company.state ? "company_kind_state_shop" : Licensed(company) ? "company_kind_licensed" : "company_kind_shop");
        if (company.state) return LM.Get("company_kind_state");
        if (ForeignOwner(company) is { } foreign) return string.Format(LM.Get("company_kind_foreign"), foreign.GetEmpireFullName());
        if (company.state_share >= ControlStake) return LM.Get("company_kind_state_held");
        return LM.Get(company.group_clan_id >= 0 ? "company_kind_group" : "company_kind_private");
    }

    // 地图上厂区标的名字(见 MapLabelOverlay)
    public static string MapLabel(FactorySite site) => Of(site)?.short_name ?? "";

    public static void Rename(CompanyData company, string name)
    {
        string value = (name ?? "").Trim();
        if (company == null || value.Length == 0 || value == company.name) return;
        company.name = value.Length > 16 ? value.Substring(0, 16) : value;
        company.short_name = CompanyNaming.Brief(company.name);
        // 改过名的不随改组换名
        company.renamed = true;
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

    // 办得了商号、公司的国家：穿越政权自有一套经济(见 TransmigrantSystem)；时代够了(min_era，默认从头就有)
    private static bool Commercial(Empire empire) =>
        empire?.CoreKingdom != null && !empire.isRekt() && !TransmigrantSystem.Governs(empire) && Era(empire) >= N.min_era;

    // 一国眼下的企业形态(界面里"商号"还是"公司"的叫法跟它走)
    public static CompanyForm RealmForm(Empire empire) => CompanyRules.Form(CompanyRules.RealmForm(empire == null ? 0 : Era(empire)));

    // 每年(ConstitutionalEconomySystem，先于财阀)
    public static void Update(Empire empire, ConstitutionalEconomyState state)
    {
        if (empire?.CoreKingdom == null || state == null) return;
        state.cartel_sectors ??= new List<string>();
        state.foreign_control ??= new Dictionary<long, float>();
        state.seizures ??= new Dictionary<long, double>();
        double now = World.world.getCurWorldTime();
        // 特许公司(宗主国的公司，经营的是殖民地不是厂区)：先对一遍账
        List<CompanyData> charters = SyncCharters(empire, now);
        if (!Commercial(empire))
        {
            if (charters.Count > 0) StockMarketSystem.Update(empire, state, charters, now);
            return;
        }
        int era = Era(empire);
        Reform(empire, state, era);
        List<CompanyData> own = _book.companies.Where(company => !IsCharter(company) && RealmOf(company) == empire).ToList();
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
                // 有厂的厂区都有东家：古代的手工工场归商号、官局，近代厂归公司
                if (site.units.Count == 0) continue;
                // 国有化的国家没有民营的厂
                if (state.nationalization >= ModernPolicySystem.Nationalized) site.state = true;
                CompanyData company = Of(site);
                if (company == null || company.state != site.state || !own.Contains(company))
                    company = Assign(empire, state, city, site, company, own, used, now, era);
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
            if (charters.Count > 0) StockMarketSystem.Update(empire, state, charters, now);
            return;
        }
        foreach (CompanyData company in own) Account(empire, state, company, sites[company.id]);
        // 随时代改组(字号留着、名号换格式)
        bool crowned = !RepublicSystem.IsRepublic(empire);
        foreach (CompanyData company in own) Refit(company, era, crowned, used);
        Compete(empire, state, own, sites, era, now);
        Licenses(empire, own, now);
        if (era >= TrustEra)
        {
            GroupBuys(empire, state, own);
            BidAbroad(empire, own, now);
        }
        StateStakes(empire, state, own);
        ControlLedger(state, own);
        StockMarketSystem.Update(empire, state, charters.Count == 0 ? own : own.Concat(charters).ToList(), now);
    }

    // 本国进到新的一档企业形态：记一笔(商号改称公司、颁行公司法、改组集团)；头一回看到的只记下不报
    private static void Reform(Empire empire, ConstitutionalEconomyState state, int era)
    {
        int current = CompanyRules.RealmForm(era), before = CompanyRules.IndexOf(state.company_form);
        state.company_form = CompanyRules.Form(current).id;
        if (before < 0 || current <= before) return;
        string text = WorldMap.WorldMapData.PickParts(CompanyRules.Form(current).reform);
        if (!string.IsNullOrEmpty(text)) EventRecorder.RecordRoutine(empire, text.Replace("{realm}", empire.GetEmpireFullName()));
    }

    // 改组：形态只往上走(时代倒退、规模缩了照旧)；旧档的公司头一回只定下形态与字号，名字照旧；改过名的不换名
    private static void Refit(CompanyData company, int era, bool crowned, HashSet<string> used)
    {
        int target = CompanyRules.FormFor(era, company.scale), current = CompanyRules.IndexOf(company.form);
        if (current < 0)
        {
            company.form = CompanyRules.Form(target).id;
            if (string.IsNullOrEmpty(company.head))
                company.head = company.state ? Home(company)?.GetCityName() ?? "" : CompanyNaming.GuessHead(company);
            return;
        }
        if (target <= current) return;
        string before = company.name;
        company.form = CompanyRules.Form(target).id;
        string label = CompanyRules.Label(CompanyRules.Form(target));
        if (company.renamed)
        {
            Note(company, string.Format(LM.Get("company_note_reformed"), label));
            return;
        }
        used.Remove(company.short_name);
        CompanyNaming.Name(company, Home(company)?.GetCityName() ?? company.head, crowned, used, company.id * 31L + target);
        used.Add(company.short_name);
        if (company.name != before) Note(company, string.Format(LM.Get("company_note_reorganized"), before, company.name, label));
        Relabel(company);
    }

    // 厂区小窗口里改了官营民营：当场换东家(见 FactoryWindow)
    public static void Reassign(FactorySite site)
    {
        City city = FactorySystem.CityOf(site);
        Empire empire = city?.kingdom?.GetEmpire();
        ConstitutionalEconomyState state = empire?.data?.constitutional_economy;
        if (state == null || !Commercial(empire) || site.units.Count == 0) return;
        state.seizures ??= new Dictionary<long, double>();
        state.foreign_control ??= new Dictionary<long, float>();
        List<CompanyData> own = _book.companies.Where(company => !IsCharter(company) && RealmOf(company) == empire).ToList();
        Assign(empire, state, city, site, Of(site), own, new HashSet<string>(own.Select(company => company.short_name)),
            World.world.getCurWorldTime(), Era(empire));
    }

    private static string MainSector(FactorySite site) => site.units.Where(unit => FactorySystem.IsModern(unit.type))
        .OrderByDescending(FactorySystem.ValueOf).Select(unit => unit.type).FirstOrDefault() ?? "";

    // 这片厂区归哪家公司：国企并进本国同业的那一家，私企另立一家
    private static CompanyData Assign(Empire empire, ConstitutionalEconomyState state, City city, FactorySite site, CompanyData previous,
        List<CompanyData> own, HashSet<string> used, double now, int era)
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
                reputation = StartReputation + Math.Abs(seed) % 15, capital = ValueOf(site) * 0.5f,
                form = CompanyRules.Form(CompanyRules.FormFor(era, ScaleOf(site))).id
            };
            if (!site.state)
            {
                SpecificClan clan = PickOwner(empire, city, seed);
                company.clan_id = clan?.id ?? -1L;
                company.owner = Surname(clan);
            }
            CompanyNaming.Name(company, city.GetCityName(), !RepublicSystem.IsRepublic(empire), used, seed);
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
        if (company.charter_kingdom_id >= 0) CharterIndex.Remove(company.charter_kingdom_id);
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
        // 皇商专营多得几成价
        float gouge = Markup(empire.id, company.sector) + (Licensed(company) ? G.license_markup : 0f);
        company.profit = company.value * Margin * cycle * (1f + 2f * gouge) * (0.6f + 0.8f * company.reputation / 100f);
        if (company.state) company.capital = 0f;
        else if (ById(company.foreign_company_id) is { } parent && company.profit > 0f)
        {
            // 外资：利润四成汇回母公司
            parent.capital += company.profit * ForeignRemit;
            company.capital += company.profit * (1f - ForeignRemit);
        }
        else company.capital += company.profit;
        // 皇商：每年把盈利的几成交官府
        if (Licensed(company) && company.profit > 0f && empire.CoreKingdom != null)
        {
            int fee = Mathf.RoundToInt(company.profit * G.license_fee);
            if (fee > 0)
            {
                empire.CoreKingdom.AddMoney(fee, TreasuryCategory.CommercialTax);
                company.capital -= fee;
            }
        }
        float gain = company.state ? 1.5f : 3f;
        if (state.cycle_phase is EconomicCycleSystem.Boom or EconomicCycleSystem.Overheat) gain += 2f;
        if (crisis && !company.state) gain = -6f;
        company.reputation = Mathf.Clamp(company.reputation + gain, 5f, 100f);
    }

    // 谁说了算：财阀旗下的并作一家算，外资按控股国并，其余各算各的
    private static long ControllerKey(CompanyData company) =>
        company.group_clan_id >= 0 ? company.group_clan_id + 1_000_000_000L
        : company.foreign_empire_id >= 0 ? -company.foreign_empire_id - 2L : -1_000_000_000L - company.id;

    // 独立的私企(倒闭、被并购、被收购只找它们)：国家控股的不算
    private static bool Independent(CompanyData company) =>
        !company.state && company.group_clan_id < 0 && company.foreign_empire_id < 0 && company.state_share < ControlStake &&
        company.charter_kingdom_id < 0;

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
            // 商号的年代：不并购、不打价格战，同业结行会定价、皇商挤占(见 Guild)
            if (firms.Count > 0 && CompanyRules.FormOf(firms[0]).guild)
            {
                gouge = Guild(empire, firms, sites, own, crisis);
                markups[trade.Key] = gouge;
                weighted += gouge * total;
                weight += total;
                continue;
            }
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
        var used = new HashSet<string>(own.Where(other => other != company).Select(other => other.short_name));
        // 易主另起字号
        company.head = "";
        company.renamed = false;
        company.licensed_at = -1d;
        CompanyNaming.Name(company, city.GetCityName(), !RepublicSystem.IsRepublic(empire), used, seed);
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

    #region 行会与皇商(古代)

    private static GuildNumbers G => CompanyRules.Guild;

    public static bool Licensed(CompanyData company) => company != null && company.licensed_at >= 0d;

    // 一个行业里商号的较量：同业到几家结行会定价；有皇商的，别家每年声誉受挤；赔光了本的易主另起字号(不并购)。返回这一行的加价
    private static float Guild(Empire empire, List<CompanyData> firms, Dictionary<int, List<FactorySite>> sites, List<CompanyData> own, bool crisis)
    {
        CompanyData licensee = firms.FirstOrDefault(Licensed);
        if (licensee != null)
            foreach (CompanyData other in firms)
                if (other != licensee) other.reputation = Mathf.Max(5f, other.reputation - G.license_squeeze);
        foreach (CompanyData firm in firms.ToList())
        {
            if (!Independent(firm)) continue;
            bool broke = firm.capital < -firm.value * 0.3f || crisis && firm.reputation < 30f && UnityEngine.Random.value < FailChance;
            if (broke && sites.TryGetValue(firm.id, out List<FactorySite> held)) Reorganize(empire, firm, held, own);
        }
        return firms.Count(Independent) >= G.guild_min_firms ? G.guild_markup : 0f;
    }

    // 一个行业里的行会：同业到几家才有(界面用)
    public static bool HasGuild(Empire empire, string sector) =>
        _book.companies.Count(company => !company.state && company.sector == sector && Independent(company) &&
                                         CompanyRules.FormOf(company).guild && RealmOf(company) == empire) >= G.guild_min_firms;

    // 每年：皇商到期、改组成公司、收归官办的撤销；有机会把一个两家以上商号、还没有皇商的行业特许给最大的一家
    private static void Licenses(Empire empire, List<CompanyData> own, double now)
    {
        foreach (CompanyData company in own)
        {
            if (!Licensed(company)) continue;
            // 改组成公司(不再有皇商)、收归官办的随之撤销；期满的撤销
            if (company.state || !CompanyRules.FormOf(company).license) Unlicense(empire, company, "company_license_ended_history");
            else if (Date.getYearsSince(company.licensed_at) >= G.license_years) Unlicense(empire, company, "company_license_lapsed_history");
        }
        if (UnityEngine.Random.value >= G.license_chance) return;
        CompanyData pick = own.Where(company => !company.state && CompanyRules.FormOf(company).license)
            .GroupBy(company => company.sector).Where(trade => trade.Count() >= 2 && !trade.Any(Licensed))
            .Select(trade => trade.OrderByDescending(company => company.scale).First())
            .OrderByDescending(company => company.scale).FirstOrDefault();
        if (pick != null) Grant(empire, pick, now);
    }

    private static void Grant(Empire empire, CompanyData company, double now)
    {
        company.licensed_at = now;
        string text = string.Format(LM.Get("company_license_granted_history"), empire.GetEmpireFullName(), company.name,
            CompanyNaming.SectorName(company.sector));
        Note(company, text);
        EventRecorder.RecordRoutine(empire, text);
    }

    private static void Unlicense(Empire empire, CompanyData company, string key)
    {
        company.licensed_at = -1d;
        string text = string.Format(LM.Get(key), empire.GetEmpireFullName(), company.name);
        Note(company, text);
        EventRecorder.RecordRoutine(empire, text);
    }

    // 界面：特许这家商号当皇商(同行原来的皇商撤销)；办不成返回原因键
    public static string GrantLicense(CompanyData company)
    {
        Empire realm = RealmOf(company);
        if (company == null || realm == null || company.state || IsCharter(company)) return "diplomacy_reason_invalid";
        if (!CompanyRules.FormOf(company).license) return "company_license_era";
        if (Licensed(company)) return "company_license_already";
        foreach (CompanyData other in _book.companies.Where(other => other != company && Licensed(other) && other.sector == company.sector &&
                                                                     RealmOf(other) == realm).ToList())
            Unlicense(realm, other, "company_license_revoked_history");
        Grant(realm, company, World.world.getCurWorldTime());
        return null;
    }

    public static string RevokeLicense(CompanyData company)
    {
        Empire realm = RealmOf(company);
        if (company == null || realm == null || !Licensed(company)) return "diplomacy_reason_invalid";
        Unlicense(realm, company, "company_license_revoked_history");
        return null;
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
            if (IsCharter(company) || RealmOf(company) != empire) continue;
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

    // 资本最厚的本国私企出海：先看通商的弱国里有没有买得起的关键企业，没有就去通商的国家设厂开分公司
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
        double power = empire.GetNationalPower();
        CompanyData best = null;
        float bestPrice = 0f;
        foreach (CompanyData company in _book.companies)
        {
            // 国家入了三成股的买不走；国力不及买方八成的国家，企业才会落到外人手里
            if (!Independent(company) || company.state_share >= ShieldStake || !Strategic.Contains(company.sector) ||
                !(RealmOf(company) is { } host) || !hosts.Contains(host) || host.GetNationalPower() >= power * WeakerShare) continue;
            float price = Worth(company) * (company.listed ? 1.3f : 1.5f);
            if (buyer.capital < price || best != null && company.scale <= best.scale) continue;
            best = company;
            bestPrice = price;
        }
        if (best == null)
        {
            BuildAbroad(empire, buyer, hosts, now);
            return;
        }
        Empire seat = RealmOf(best);
        // 东道国国库出手入股，挡下这笔收购
        if (Defend(seat, best, empire)) return;
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

    // 设厂：去通商的国家里厂最多的几座城挑一座放得下的，新建一片厂区，成立分公司(利润照外资汇回)
    private static void BuildAbroad(Empire empire, CompanyData buyer, HashSet<Empire> hosts, double now)
    {
        float price = Mathf.Max(BranchMinCost, buyer.value * BranchCostShare);
        if (buyer.capital < price || _book.companies.Count(company => company.foreign_company_id == buyer.id) >= MaxBranches) return;
        foreach (Empire host in hosts.OrderByDescending(item => item.GetNationalPower()).Take(3))
            foreach (City city in ClaimPolicySystem.Cities(host).OrderByDescending(FactorySystem.Units).Take(4).ToList())
            {
                FactorySite site = FactorySystem.FoundBranch(city, buyer.sector);
                if (site == null) continue;
                buyer.capital -= price;
                var branch = new CompanyData
                {
                    id = _book.next_id++, sector = buyer.sector, culture = buyer.culture, city_id = city.id, founded = now,
                    clan_id = buyer.clan_id, owner = buyer.owner, foreign_empire_id = empire.id, foreign_company_id = buyer.id,
                    foreign_since = now, reputation = Mathf.Max(StartReputation, buyer.reputation * 0.7f)
                };
                (branch.name, branch.short_name) = CompanyNaming.Branch(buyer.culture, buyer.short_name, city.GetCityName());
                _book.companies.Add(branch);
                Index[branch.id] = branch;
                site.company_id = branch.id;
                RelabelCity(city.id);
                string text = string.Format(LM.Get("company_branch_built_history"), buyer.name, empire.GetEmpireFullName(),
                    city.GetCityName(), host.GetEmpireFullName(), branch.name);
                Note(branch, text);
                Note(buyer, text);
                WorldOrderSystem.Record(text, host, new[] { empire });
                return;
            }
    }

    // 这个国家容不容外国资本：没有国有化、没有闭关、没在同买方打仗，没收过买方的产业还没过二十年的不容
    private static bool OpenToForeign(Empire buyer, Empire host)
    {
        ConstitutionalEconomyState state = host?.data?.constitutional_economy;
        if (state == null || host.CoreKingdom == null || host.isRekt() || host.IsArchived()) return false;
        if (state.nationalization >= ModernPolicySystem.Nationalized || buyer.CoreKingdom.isEnemy(host.CoreKingdom)) return false;
        Kingdom leader = CommodityMarketSystem.MarketOf(host.CoreKingdom)?.leader ?? host.CoreKingdom;
        if (CommodityMarketSystem.PolicyOf(leader) == TradePolicy.Closed) return false;
        return !(state.seizures != null && state.seizures.TryGetValue(buyer.id, out double at) && Date.getYearsSince(at) < SeizureGrudgeYears);
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

    #region 国家持股

    // 国库入股私企(用户 2026-11-25 "公司的可以做"：国家持股)：一回买一成，至多九成(要全资就收归国有)。
    //   · 钱从国库出、进总部那座城的民间(见 EnterpriseSystem.ReturnToLocal)，公司得一笔资本；减持时从民间现金里收回；
    //   · 国家的股份折成官营的份数，照官营那样从工业产值里分利润上缴(见 EnterpriseSystem.TakePublicProfit)；
    //   · 入到三成外资就买不走，五成起是国家控股——不倒闭、不被并购；
    //   · AI：外国来收购关键企业时，不信自由放任的当局国库宽裕就入股三成挡下；萧条里以兴办工程为对策的，
    //     给赔了本的最大私企入股一成；定了私有化的国家每年减持一成。公司窗口里也能直接入股、减持。
    public static float StakeStep => N.stake_step;
    public static float MaxStake => N.max_stake;
    public static float ControlStake => N.control_stake;
    public static float ShieldStake => N.shield_stake;

    private static int StakePrice(CompanyData company, float share) => Mathf.Max(1, Mathf.RoundToInt(Worth(company) * share));

    // 入股：办不成返回原因键
    public static string BuyStake(CompanyData company, float share = -1f)
    {
        if (share <= 0f) share = StakeStep;
        Empire realm = RealmOf(company);
        if (company == null || realm?.CoreKingdom == null) return "diplomacy_reason_invalid";
        if (company.state) return "company_stake_state";
        if (IsCharter(company)) return "company_stake_charter";
        // 商号这类形态国库不入股
        if (!CompanyRules.FormOf(company).stake) return "company_stake_era";
        if (company.foreign_empire_id >= 0) return "company_stake_foreign";
        if (company.state_share + share > MaxStake + 0.001f) return "company_stake_max";
        int price = StakePrice(company, share);
        if (realm.CoreKingdom.GetMoney() < price) return "company_stake_no_money";
        realm.CoreKingdom.SubMoney(price, TreasuryCategory.PublicEnterprise);
        if (Home(company) is { } home) EnterpriseSystem.ReturnToLocal(home, price);
        company.capital += price;
        company.state_share = Mathf.Min(MaxStake, company.state_share + share);
        Note(company, string.Format(LM.Get("company_note_stake_bought"), Mathf.RoundToInt(company.state_share * 100f)));
        return null;
    }

    // 减持：办不成返回原因键
    public static string SellStake(CompanyData company, float share = -1f)
    {
        if (share <= 0f) share = StakeStep;
        Empire realm = RealmOf(company);
        if (company == null || realm?.CoreKingdom == null) return "diplomacy_reason_invalid";
        if (company.state_share <= 0.001f) return "company_stake_none";
        share = Mathf.Min(share, company.state_share);
        int price = StakePrice(company, share);
        CityPopulationData people = Home(company) is { } home ? CityPopulationSystem.Get(home) : null;
        long got = people == null ? 0L : EnterpriseSystem.TakeCash(people, price);
        if (got > 0) realm.CoreKingdom.AddMoney((int)Math.Min(got, int.MaxValue), TreasuryCategory.PublicEnterprise);
        company.state_share = Mathf.Max(0f, company.state_share - share);
        Note(company, string.Format(LM.Get("company_note_stake_sold"), Mathf.RoundToInt(company.state_share * 100f)));
        return null;
    }

    // 外国来收购：东道国不信自由放任、国库又宽裕(三倍于入股款)的，入股三成挡下
    private static bool Defend(Empire host, CompanyData target, Empire buyer)
    {
        PartyIdeology ideology = IdeologyFamilies.StateIdeology(host);
        if (ideology is PartyIdeology.Libertarianism or PartyIdeology.Capitalism or PartyIdeology.ConservativeLiberalism) return false;
        float need = ShieldStake - target.state_share;
        if (need <= 0f) return true;
        if (host.CoreKingdom.GetMoney() < StakePrice(target, need) * 3 || BuyStake(target, need) != null) return false;
        WorldOrderSystem.Record(string.Format(LM.Get("company_stake_defended_history"), host.GetEmpireFullName(), target.name,
            buyer.GetEmpireFullName()), host, new[] { buyer });
        return true;
    }

    // 每年：萧条里以兴办工程为对策的救一家赔了本的私企；定了私有化的减持
    private static void StateStakes(Empire empire, ConstitutionalEconomyState state, List<CompanyData> own)
    {
        if (state.nationalization <= ModernPolicySystem.Privatized)
        {
            foreach (CompanyData company in own)
                if (company.state_share > 0f) SellStake(company);
            return;
        }
        if (state.cycle_phase != EconomicCycleSystem.Depression || state.cycle_response != EconomicCycleSystem.Stimulus) return;
        CompanyData failing = own.Where(company => !company.state && company.foreign_empire_id < 0 && company.capital < 0f &&
                                                   company.state_share < ControlStake && CompanyRules.FormOf(company).stake)
            .OrderByDescending(company => company.scale).FirstOrDefault();
        if (failing == null || empire.CoreKingdom.GetMoney() < StakePrice(failing, StakeStep) * 3 || BuyStake(failing) != null) return;
        EventRecorder.RecordRoutine(empire, string.Format(LM.Get("company_stake_rescue_history"), empire.GetEmpireFullName(), failing.name));
    }

    // 这座城的私营厂区里国家的股份折成几份官营(见 EnterpriseSystem.TakePublicProfit)
    public static float StateStake(City city)
    {
        if (_book.companies.Count == 0 || city == null) return 0f;
        float stake = 0f;
        foreach (FactorySite site in FactorySystem.InCity(city))
            if (!site.state && Of(site) is { state_share: > 0f } company) stake += site.units.Count * company.state_share;
        return stake;
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
        if (Licensed(company)) factor *= 1f + G.license_markup;
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

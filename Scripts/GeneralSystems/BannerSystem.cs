using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 八旗多元制(明清鼎革剧本的大清，用户 2026-11-05 "他拥有特殊的政体""八旗制度""旗人阶级""满城""跑马圈地""逃人法")：
//   · 政体：朝廷照华夏制度走多元帝国的兼用路线(CompositeEmpireService)，年号、官职、后宫都用律令那套，汉地各省律令、草原各部游牧，
//     本族旧制记在军事传统里；政体记在 KingdomExtraData.banner_regime，只在显示、官名、军制上和律令分开。国号"大清朝"。
//     路线固定兼用，不改分治、不北归，不算离草(见 CompositeEmpireService.EstablishCourt)；
//   · 八旗：军制见 Formations.json 的 Banner 槽位——中央禁旅八旗只收旗人，地方绿营不收旗人；老兵值封顶随八旗战力，承平日久渐渐下滑；
//   · 旗人：SocialClass.Banner，只在背景人口里。世代在旗，不随阶层流动(见 CityPopulationSystem.DriftClasses)；
//     开局在旗下的城三成编旗，满城八分；不纳粮，每年从国库领旗饷，发不出就有怨气；城丢了每年一成出旗为民；
//   · 满城：驻防重镇。每几年挑一座治下人口最多的外族大城建一座，也可以由事件点名；城不因鞭长莫及失稳，城里多出旗人，
//     原住的汉民被迁出一部分，农民添怨；
//   · 跑马圈地：事件点名(banner_enclosure)，或入关后按时间表自动圈(见 BannerSystem.Conquest)。旗地占城中土地的份额进 LandEconomySystem，民田少了失地的就多，那份田赋收不上来，
//     百姓逃散一些，农民、地主添怨，兼并添一笔；
//   · 逃人法：事件颁行(banner_law)或入关后自动颁行，久安以后放宽(见 BannerSystem.Conquest)，走已有的 LawType.逃人法 与 fugitive_household_law_enacted，百姓不能迁徙，每年再添一点怨气。
//
// 女真(满洲)文化从制度树里长出八旗(用户 2026-11-06 选"从制度树里长出来")：文化带政治特性 banner(八旗传统)，
// 游牧线的千户制在女真叫猛安谋克，后接只给 banner 文化的「八旗制度」(特性 eight_banners)。两档：
//   · 编旗(Organized)：统治族群施行了八旗制度——本族之城成丁编旗、军制换八旗、八旗战力随战和起落；入关前不发旗饷、不圈地；
//   · 八旗多元制(Practices)：编旗的王朝入主、朝廷采纳华夏制度时自动改行(见 TryEnter)，路线固定兼用；北归、不再是复合帝国时退回编旗。
public static partial class BannerSystem
{
    public const string Trait = "banner";
    public const string Feature = "eight_banners";

    public sealed class Summary
    {
        // 八旗多元制(入主以后)；否则只是编旗，没有旗饷、满城、圈地、逃人法
        public bool multi;
        public float bannermen;
        public float people;
        public int enclosed;
        public bool fugitiveLaw;
        public float vigor;
        public int stipend;
        public int unpaidYears;
        // 入关三政：入关几年(-1 未入关)、剃发易服、永停圈地、放宽逃人法
        public float enteredYears = -1f;
        public bool queued, enclosureStopped, lawRelaxed;
        // 外藩几部、其中与中央有姻亲的几部；封禁：0 未入关、1 封禁、2 弛禁(闯关东)
        public int leagues, leaguesMarried, seal;
        // 生计：闲散几成、汉军出旗、办了勇营的行省
        public float idle;
        public bool hanjunReleased;
        public List<string> yongying = new();
        public List<string> garrisons = new();
    }

    private const float EnclosePeasantGrievance = 12f, EncloseLandlordGrievance = 6f, EncloseBannerRelief = 8f, EncloseAnnexation = 8f;
    private const float GarrisonGrievance = 2f, GarrisonDisplaced = 0.05f, UnpaidGrievance = 6f, LawGrievance = 0.5f;

    public static bool Practices(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return false;
        Kingdom core = kingdom.GetEmpire()?.CoreKingdom ?? kingdom;
        return core?.data != null && !core.isRekt() && core.GetOrCreate().banner_regime;
    }

    public static bool Practices(Empire empire) =>
        empire?.CoreKingdom?.data != null && !empire.CoreKingdom.isRekt() && empire.CoreKingdom.GetOrCreate().banner_regime;

    private static Kingdom CoreOf(Kingdom kingdom) => kingdom?.GetEmpire()?.CoreKingdom ?? kingdom;

    // 本国(帝国按核心)的统治族群：征服王朝按统治文化，别的按核心的文化
    public static string RulingCultureOf(Kingdom kingdom)
    {
        Kingdom core = CoreOf(kingdom);
        if (core?.data == null || core.isRekt()) return "";
        Empire empire = core.GetEmpire();
        return CompositeEmpireService.IsComposite(empire)
            ? CompositeEmpireService.GetRulingCulture(empire)
            : CultureService.GetRealmCulture(core);
    }

    // 编旗：八旗多元制，或统治族群施行了八旗制度
    public static bool Organized(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return false;
        return Practices(kingdom) || InstitutionSystem.HasFeature(RulingCultureOf(kingdom), Feature);
    }

    // 政体叫法：国情、官制这些地方显示八旗多元制，别的照旧
    public static string RegimeName(Kingdom kingdom, string fallback) =>
        Practices(kingdom) && (kingdom.GetEmpire()?.CoreKingdom ?? kingdom) == kingdom ? LM.Get("regime_type_Banner") : fallback;

    #region 开局

    // 立政体：开局就是多元帝国的兼用路线；开局手里的城都算旗下旧地
    public static void Establish(Kingdom core, Empire empire)
    {
        if (core?.data == null || empire?.data == null) return;
        var extra = core.GetOrCreate();
        extra.banner_regime = true;
        extra.banner_vigor = BannerRules.VigorMax;
        string ruling = CultureService.GetRealmCulture(core);
        CompositeEmpireService.EstablishCourt(empire, ruling, "Huaxia");
        foreach (City city in core.cities ?? new List<City>())
            if (city?.data != null && !city.isRekt()) city.GetOrCreate().banner_home = true;
        LogService.LogInfo($"[EmpireCraft][八旗] {empire.GetEmpireFullName()} 立八旗多元制，政体 {core.GetRegime()?.type}，{core.cities?.Count ?? 0} 城");
    }

    // 编旗的王朝入主(采纳华夏制度，见 CompositeEmpireService.AdoptCentralInstitutions)：改行八旗多元制。
    // 朝廷、国号照入主的常规走(不像剧本那样定死)，只是路线固定兼用；本族之城算旗下旧地
    public static bool TryEnter(Empire empire)
    {
        Kingdom core = empire?.CoreKingdom;
        if (core?.data == null || core.isRekt() || Practices(empire) || !CompositeEmpireService.IsComposite(empire)) return false;
        string ruling = CompositeEmpireService.GetRulingCulture(empire);
        if (!InstitutionSystem.HasFeature(ruling, Feature) ||
            InstitutionSystem.GetCultureLine(CompositeEmpireService.GetInstitutionalCulture(empire)) != "Huaxia") return false;
        var extra = core.GetOrCreate();
        extra.banner_regime = true;
        extra.banner_vigor = Mathf.Max(extra.banner_vigor, BannerRules.VigorEntry);
        foreach (City city in CitiesOf(empire).ToList())
            if (string.Equals(CultureService.GetMainCulture(city, initialize: false), ruling, global::System.StringComparison.Ordinal))
                city.GetOrCreate().banner_home = true;
        string text = string.Format(LM.Get("banner_enter_history"), empire.GetEmpireName());
        empire.RecordHistory(directContent: text, kingdomId: core.id);
        TranslateHelper.LogEventMessage(text, core);
        LogService.LogInfo($"[EmpireCraft][八旗] {empire.GetEmpireName()} 入主，改行八旗多元制");
        return true;
    }

    // 不再是复合帝国(北归、帝国散了)：退回编旗，满城、旗地、逃人法跟着作废
    private static void Leave(Empire empire)
    {
        var extra = empire.CoreKingdom.GetOrCreate();
        extra.banner_regime = false;
        extra.banner_fugitive_law = false;
        foreach (City city in CitiesOf(empire).ToList())
        {
            var cityExtra = city.GetOrCreate();
            cityExtra.manchu_city = false;
            cityExtra.banner_land = 0f;
        }
        LogService.LogInfo($"[EmpireCraft][八旗] {empire.GetEmpireName()} 不再是征服王朝，退回编旗");
    }

    // 进世界后、开局兵力之前当场编旗，禁旅八旗才有兵可补
    public static void SettleAll(Empire empire)
    {
        foreach (City city in CitiesOf(empire).ToList())
        {
            CityPopulationData data = CityPopulationSystem.Get(city);
            if (data?.groups != null) Settle(city, data, 1f / BannerRules.SettleRate);
        }
    }

    #endregion

    #region 旗人

    private static float TotalOf(CityPopulationData data) => data.groups.Sum(group => group.Background);

    // 每年(背景人口自然增减时)：编旗或出旗
    public static void Settle(City city, CityPopulationData data, float years)
    {
        if (city?.data == null || data?.groups == null) return;
        var extra = city.GetOrCreate();
        bool organized = Organized(city.kingdom);
        if (!organized && !extra.banner_home && !extra.manchu_city && !data.groups.Any(group => group.social_class == SocialClass.Banner)) return;
        float have = CityPopulationSystem.BackgroundOfClass(city, SocialClass.Banner);
        SocialClass commoner = GuoyeSystem.CommonerOf(city);
        if (!organized)
        {
            if (have > 0f) SlaverySystem.Convert(city, data, SocialClass.Banner, commoner, BannerRules.ToRelease(have, years));
            return;
        }
        TickVigor(CoreOf(city.kingdom));
        // 入关前：本族为主的城都是旗下旧地(出则为兵、入则为民)，入主后照旧算旧地
        if (!extra.banner_home && !Practices(city.kingdom) &&
            string.Equals(CultureService.GetMainCulture(city, initialize: false), RulingCultureOf(city.kingdom), global::System.StringComparison.Ordinal))
            extra.banner_home = true;
        float enroll = BannerRules.ToEnroll(TotalOf(data), have, BannerRules.TargetShare(extra.banner_home, extra.manchu_city), years);
        if (enroll > 0f) Enroll(city, data, commoner, enroll);
    }

    // 税收折扣(乘在税率上)：旗人不纳粮，旗地不交田赋
    public static float TaxFactor(City city)
    {
        if (city?.data == null || !Practices(city.kingdom)) return 1f;
        CityPopulationData data = CityPopulationSystem.Get(city);
        float total = data?.groups == null ? 0f : TotalOf(data);
        float share = total > 0f ? CityPopulationSystem.BackgroundOfClass(city, SocialClass.Banner) / total : 0f;
        return BannerRules.TaxFactor(share, city.GetOrCreate().banner_land);
    }

    // 旗地占城中土地的百分比(见 LandEconomySystem)
    public static float LandShare(City city) => city?.data == null ? 0f : city.GetOrCreate().banner_land * 100f;

    // 征兵：禁旅八旗只收旗人(旗下还没编满时也收本族百姓)，别的军不收旗人
    public static bool MayRecruit(bool bannerCorps, string rulingCulture, PopGroup group)
    {
        if (group == null) return false;
        if (!bannerCorps) return group.social_class != SocialClass.Banner;
        return group.social_class == SocialClass.Banner || group.culture == rulingCulture;
    }

    public static string RulingCulture(Kingdom kingdom) => RulingCultureOf(kingdom);

    public static float Vigor(Kingdom kingdom)
    {
        Kingdom core = CoreOf(kingdom);
        return core?.data == null ? BannerRules.VigorMax : core.GetOrCreate().banner_vigor;
    }

    // 八旗战力一年一算(编旗的城结算人口时、帝国例行检查时，谁先到谁算)
    private static void TickVigor(Kingdom core)
    {
        if (core?.data == null || core.isRekt()) return;
        var extra = core.GetOrCreate();
        if (extra.banner_vigor_at >= 0d && Date.getYearsSince(extra.banner_vigor_at) < 1) return;
        extra.banner_vigor_at = World.world.getCurWorldTime();
        // 闲散多了战力败得更快(见 BannerSystem.Livelihood)
        float idle = Practices(core) ? BannerRules.IdleShare(Bannermen(core.GetEmpire()), extra.banner_quota) : 0f;
        extra.banner_vigor = BannerRules.NextVigor(extra.banner_vigor, core.hasEnemies(), idle);
    }

    private static IEnumerable<City> CitiesOf(Empire empire)
    {
        foreach (Kingdom kingdom in empire?.kingdoms_list?.ToList() ?? new List<Kingdom>())
        {
            if (kingdom?.cities == null || kingdom.isRekt()) continue;
            foreach (City city in kingdom.cities)
                if (city?.data != null && !city.isRekt()) yield return city;
        }
    }

    #endregion

    #region 每年

    // 每年(帝国例行检查，见 EmpireCraftEmpireBehCheckMandate)
    public static void Yearly(Empire empire)
    {
        if (empire?.CoreKingdom?.data == null || empire.CoreKingdom.isRekt()) return;
        // 编旗的王朝入主以后才改行八旗多元制(剧本的大清开局就是)；不再是征服王朝就退回编旗
        if (!Practices(empire)) TryEnter(empire);
        else if (!CompositeEmpireService.IsComposite(empire)) Leave(empire);
        if (!Organized(empire.CoreKingdom)) return;
        Kingdom core = empire.CoreKingdom;
        var extra = core.GetOrCreate();
        TickVigor(core);
        LeagueYearly(empire);
        if (!Practices(empire)) return;
        float bannermen = Bannermen(empire);
        LivelihoodYearly(empire, core, extra, bannermen);
        PayStipend(empire, core, extra, bannermen);
        if (extra.banner_fugitive_law)
        {
            ApplyFugitiveLaw(empire);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Peasant, LawGrievance);
        }
        TryBuildGarrison(empire, core, extra);
        ConquestYearly(empire, core, extra);
        SealYearly(empire, core, extra);
        MilitiaYearly(empire, core, extra);
    }

    // 旗饷按缺发(闲散不领，见 BannerSystem.Livelihood)
    private static void PayStipend(Empire empire, Kingdom core, KingdomExtension.KingdomExtraData extra, float bannermen)
    {
        int due = BannerRules.StipendDue(bannermen, extra.banner_quota);
        int paid = BannerRules.StipendPaid(due, core.GetMoney());
        if (paid > 0) core.SubMoney(paid, TreasuryCategory.Military);
        extra.banner_stipend = paid;
        if (paid >= due)
        {
            extra.banner_unpaid_years = 0;
            return;
        }
        extra.banner_unpaid_years++;
        ClaimPolicySystem.AddGrievance(empire, SocialClass.Banner, UnpaidGrievance);
    }

    private static void TryBuildGarrison(Empire empire, Kingdom core, KingdomExtension.KingdomExtraData extra)
    {
        if (extra.banner_city_at >= 0d && Date.getYearsSince(extra.banner_city_at) < BannerRules.CityIntervalYears) return;
        List<City> cities = CitiesOf(empire).ToList();
        if (cities.Count(city => city.GetOrCreate().manchu_city) >= BannerRules.CityLimit(cities.Count)) return;
        City best = cities.Where(city => !city.GetOrCreate().manchu_city && !city.GetOrCreate().banner_home &&
                                         Date.getYearsSince(city.data.timestamp_kingdom) >= 1)
            .OrderByDescending(CityPopulationSystem.CivilianBackground).FirstOrDefault();
        if (best == null) return;
        int cost = Mathf.Max(100, (int)(core.GetMoney() / 20));
        if (!TreasurySystem.TrySpend(core, cost, TreasuryCategory.Military)) return;
        BuildGarrison(core, best);
    }

    #endregion

    #region 满城、圈地、逃人法

    // 建满城(自动挑或事件点名)：已经有的不重建
    public static bool BuildGarrison(Kingdom realm, City city)
    {
        Empire empire = realm?.GetEmpire();
        if (city?.data == null || empire?.data == null || !Practices(empire) || city.GetOrCreate().manchu_city) return false;
        city.GetOrCreate().manchu_city = true;
        Kingdom core = empire.CoreKingdom;
        core.GetOrCreate().banner_city_at = World.world.getCurWorldTime();
        // 圈出内城驻防，原住百姓迁出一部分
        City refuge = Neighbor(city);
        if (refuge != null) CityPopulationSystem.TransferBackground(city, refuge, GarrisonDisplaced);
        ClaimPolicySystem.AddGrievance(empire, SocialClass.Peasant, GarrisonGrievance);
        EventRecorder.Record(empire, string.Format(LM.Get("banner_city_history"), empire.GetEmpireFullName(), city.GetCityName()));
        return true;
    }

    // 跑马圈地：事件点名，或入关后自动圈(见 BannerSystem.Conquest)；永停圈地以后不再圈。返回圈了几座城
    public static int Enclose(Kingdom realm, IEnumerable<City> cities)
    {
        Empire empire = realm?.GetEmpire();
        if (empire?.data == null || !Practices(empire) || empire.CoreKingdom.GetOrCreate().banner_enclosure_stopped) return 0;
        int done = 0;
        foreach (City city in cities)
        {
            if (city?.data == null || city.isRekt()) continue;
            var extra = city.GetOrCreate();
            float next = BannerRules.NextLand(extra.banner_land);
            if (next <= extra.banner_land) continue;
            extra.banner_land = next;
            City refuge = Neighbor(city);
            if (refuge != null) CityPopulationSystem.TransferBackground(city, refuge, BannerRules.EnclosureFlight);
            done++;
        }
        if (done == 0) return 0;
        empire.CoreKingdom.GetOrCreate().banner_enclosures++;
        ClaimPolicySystem.AddGrievance(empire, SocialClass.Peasant, EnclosePeasantGrievance);
        ClaimPolicySystem.AddGrievance(empire, SocialClass.Landlord, EncloseLandlordGrievance);
        ClaimPolicySystem.AddGrievance(empire, SocialClass.Banner, -EncloseBannerRelief);
        empire.data.maladies ??= new Dictionary<string, float>();
        empire.data.maladies.TryGetValue("annexation", out float annexation);
        empire.data.maladies["annexation"] = Mathf.Clamp(annexation + EncloseAnnexation, 0f, 100f);
        return done;
    }

    // 逃人法：记在核心上，每年补给新添的成员(换政体重载会丢)
    public static void EnactFugitiveLaw(Kingdom realm)
    {
        Empire empire = realm?.GetEmpire();
        if (empire?.CoreKingdom?.data == null) return;
        empire.CoreKingdom.GetOrCreate().banner_fugitive_law = true;
        ApplyFugitiveLaw(empire);
    }

    private static void ApplyFugitiveLaw(Empire empire)
    {
        foreach (Kingdom kingdom in empire.kingdoms_list?.ToList() ?? new List<Kingdom>())
        {
            if (kingdom?.data == null || kingdom.isRekt()) continue;
            kingdom.GetOrCreate().fugitive_household_law_enacted = true;
            Regime regime = kingdom.GetRegime();
            if (regime == null) continue;
            regime.laws ??= new List<LawType>();
            if (!regime.laws.Contains(LawType.逃人法)) regime.laws.Add(LawType.逃人法);
        }
    }

    private static City Neighbor(City city)
    {
        Vector2 at = city.city_center;
        return city.kingdom?.cities?.Where(other => other?.data != null && other != city && !other.isRekt() && !other.GetOrCreate().manchu_city)
            .OrderBy(other => (other.city_center - at).sqrMagnitude).FirstOrDefault();
    }

    #endregion

    public static Summary SummaryOf(Empire empire)
    {
        var summary = new Summary();
        if (empire?.CoreKingdom == null || !Organized(empire.CoreKingdom)) return summary;
        summary.multi = Practices(empire);
        var extra = empire.CoreKingdom.GetOrCreate();
        summary.fugitiveLaw = extra.banner_fugitive_law;
        summary.vigor = extra.banner_vigor;
        summary.stipend = extra.banner_stipend;
        summary.unpaidYears = extra.banner_unpaid_years;
        summary.enteredYears = YearsSince(extra.banner_entered_at);
        summary.queued = extra.banner_queue_at >= 0d;
        summary.enclosureStopped = extra.banner_enclosure_stopped;
        summary.lawRelaxed = extra.banner_law_relaxed;
        foreach (Kingdom league in LeaguesOf(empire))
        {
            summary.leagues++;
            if (RoyalMarriageSystem.Allied(league, empire.CoreKingdom)) summary.leaguesMarried++;
        }
        summary.seal = extra.banner_entered_at < 0d ? 0 : extra.banner_seal_lifted ? 2 : 1;
        summary.hanjunReleased = extra.banner_hanjun_released;
        foreach (Kingdom member in empire.kingdoms_list ?? new List<Kingdom>())
            if (member?.data != null && !member.isRekt() && member.GetOrCreate().banner_yongying) summary.yongying.Add(member.GetKingdomName());
        foreach (City city in CitiesOf(empire))
        {
            summary.bannermen += CityPopulationSystem.BackgroundOfClass(city, SocialClass.Banner);
            CityPopulationData data = CityPopulationSystem.Get(city);
            if (data?.groups != null) summary.people += TotalOf(data);
            var cityExtra = city.GetOrCreate();
            if (cityExtra.banner_land > 0f) summary.enclosed++;
            if (cityExtra.manchu_city) summary.garrisons.Add(city.GetCityName());
        }
        if (summary.multi) summary.idle = BannerRules.IdleShare(summary.bannermen, extra.banner_quota);
        return summary;
    }
}

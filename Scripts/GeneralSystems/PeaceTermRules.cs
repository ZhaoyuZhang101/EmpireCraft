using System;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;

namespace EmpireCraft.Scripts.GeneralSystems;

// 和约明码标价(用户 2026-11-09"可以把一些东西数值化，赔款礼金，土地，附庸什么的都标上数值"，参照全面战争：三国的交易估价；
// 11-10 定议和台，见 PeaceTable)。一切折成"户"：
//   · 城价按户数(用户选"城的价值按人口吧，因为金融也会影响人口")：与国力同一口径(CityPopulationSystem.Households)，
//     无小人模式与有小人模式都适配。国都不另加权——它通常本来就是户数最多的城，自己就贵；
//   · 钱按付方的户均年入折：MoneyYearsPerHousehold 年的户均年入顶一户(钱是一次性的，城是永久的)，
//     大国小国、古代近代都按各自的家底算，不用拍一个"一千金值几座城"；
//   · 称臣、扶立、改宗、通商、岁供按出的一方全境价值的几成。
// 一方肯不肯签(意愿，见 PeaceTable.Evaluate) = 所得 − 所失 + 战局 + 疲敝 − 势头 − 死战：
//   · 筹码 = 从对方手里打下的城 + 占着的城(按占了几成) + 兵力优势(封顶)；战局 = 对方筹码 − 己方筹码——
//     打赢的一方要拿回打下来的本钱，打输的一方得让出这么多；
//   · 疲敝：最短战期之后每多打一年、欠饷、缺粮、兵败如山倒，越拖越想停；
//   · 势头：近一年(上次议和以来)新打下来的本钱，正打得顺的不肯停——没有这一项，两国一满最短战期就总能谈拢；
//   · 死战：割都城、或割走一半以上的城，除非已经兵败如山倒(同 WarGoalSystem.Excessive)。
// 数字全在下面的常量里，进游戏看着不对直接改。
public static class PeaceTermRules
{
    // 兵力优势只算到几倍为止(以前不封顶，六十多倍的兵力就折出七千多，比对方全境还贵)；到顶时折对方全境价值的几成
    public const float StrengthCap = 4f;
    public const double StrengthShare = 0.25d;
    // 钱折户：几年的户均年入顶一户
    public const double MoneyYearsPerHousehold = 10d;
    // 疲敝：最短战期之前不记；之后每多打一年、每欠饷一月、缺粮(按缺口比例)、兵败如山倒，各折本国全境价值的几成；合计封顶
    public const double WearPerYear = 0.04d, WearPerUnpaidMonth = 0.02d, WearFood = 0.15d, WearCollapse = 0.15d, WearCap = 0.6d;
    // 死战：割都城、或割走一半以上的城，出的一方另记全境价值的几成
    public const double DesperateShare = 0.5d;
    // 称臣、扶立、改宗、通商各折出的一方全境价值的几成；岁供按矿的贵贱(WarObjective.Value，二到十)每档折一成的十分之一
    public const double VassalShare = 0.5d, InstallShare = 0.3d, ConvertShare = 0.1d, TradeShare = 0.04d,
        TributeSharePerGrade = 0.01d;
    // 赔款三档：当场付国库的几成，是否另按三年月入两成分付(同 WarReparationsSystem)；中间一档就是战争目标"赔款"
    public static readonly (double share, bool installments)[] IndemnityTiers = { (0.1d, false), (0.25d, true), (0.5d, true) };
    public const int StandardIndemnity = 1;

    public static int CityValue(City city) =>
        city?.data == null || city.isRekt() ? 0 : Math.Max(1, CityPopulationSystem.Households(city));

    // 一国全境的价值(只算这一国自己的城，帝国的成员国各算各的)
    public static int RealmValue(Kingdom kingdom) =>
        kingdom?.cities == null || kingdom.isRekt() ? 0 : kingdom.cities.Sum(CityValue);

    // 一座城的平均价值：按"丢了几座城"折算打下的城时用(战争只记城数，不记是哪几座)
    public static int AverageCityValue(Kingdom kingdom)
    {
        int cities = kingdom?.cities?.Count ?? 0;
        return cities <= 0 ? 0 : Math.Max(1, RealmValue(kingdom) / cities);
    }

    // 钱折户：按付方的户均年入
    public static int MoneyValue(Kingdom payer, long money)
    {
        if (money <= 0L || payer?.data == null) return 0;
        long income = StateDebtSystem.AnnualStableIncome(payer);
        int households = CityPopulationSystem.Households(payer);
        // 没有收入记录的(国库制度没开、刚建国)：按以前的口径，一千金五十户
        if (income <= 0L || households <= 0) return (int)Math.Round(money / 1000d * 50d);
        return (int)Math.Round(money / (income / (double)households) / MoneyYearsPerHousehold);
    }

    // 这一档赔款合计多少钱(当场 + 分付)
    public static long IndemnityAmount(Kingdom payer, int tier)
    {
        if (payer?.data == null || payer.isRekt()) return 0L;
        (double share, bool installments) = IndemnityTiers[Math.Max(0, Math.Min(IndemnityTiers.Length - 1, tier))];
        long amount = (long)(Math.Max(0L, payer.GetTreasuryBalance()) * share);
        return installments ? amount + WarReparationsSystem.InstallmentTotal(payer) : amount;
    }

    public static int Share(Kingdom kingdom, double share) => (int)Math.Round(RealmValue(kingdom) * share);

    #region 民族意识、立傀儡、占领(近代)

    // 用户 2026-11-10 "近代占领民族国家, 不是直接占领土地因为民族意识很强, 需要先设置傀儡政府""开战的目标中砝码里面民族主义
    // 建议也是筹码的一部分, 然后傀儡吞并之间所需要的砝码数值也不同""大国的占领成本和小国的也明显不一样"：
    //   · 民族意识：近代国家割走他国本土(见 OccupationPuppetSystem.NationalLand)，失地一方另记一笔 = 城价 × 民族情绪 × 大小之比
    //     × 吞并或立傀儡的倍数——吞并要的砝码最多，割地立傀儡(名义上还是本族人的政府)少得多；
    //   · 大小之比：失地一国连同成员全境 ÷ 战胜一方全境，开方后夹在 NationSizeMin~NationSizeMax——大国割地、被占的代价都重得多；
    //   · 立傀儡的战胜一方只得城价的 PuppetGainShare(间接统治)；
    //   · 占领：打仗时直接军管着他国本土，按城价 × 城中民族情绪 × 大小之比的 OccupationShare 记进占领方的疲敝(封顶全境的 OccupationCap)
    public const double NationAnnexFactor = 1d, NationPuppetFactor = 0.4d, PuppetGainShare = 0.7d;
    public const float NationSizeMin = 0.5f, NationSizeMax = 2.5f;
    // 民族情绪的底：本文化有了民族主义(科技)的，城里情绪再低也按这么多算
    public const float NationFloor = 0.3f;
    public const double OccupationShare = 0.25d, OccupationCap = 0.5d;

    public static float SizeFactor(int nationValue, int occupierValue) =>
        Math.Max(NationSizeMin, Math.Min(NationSizeMax, (float)Math.Sqrt(Math.Max(0, nationValue) / (double)Math.Max(1, occupierValue))));

    public static int NationResistance(int cityValue, float sentiment, float size, bool puppet) =>
        (int)Math.Round(Math.Max(0, cityValue) * Math.Max(0f, Math.Min(1f, sentiment)) * size * (puppet ? NationPuppetFactor : NationAnnexFactor));

    public static int OccupationWear(int occupiedResistance, int realmValue) =>
        (int)Math.Round(Math.Min(Math.Max(0, occupiedResistance) * OccupationShare, Math.Max(0, realmValue) * OccupationCap));

    // 一国连同帝国成员的全境价值(民族国家的大小)
    public static int NationValue(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return 0;
        Layer.Empire empire = kingdom.GetEmpire();
        return empire?.CoreKingdom == kingdom && empire.cities_list != null
            ? empire.cities_list.Where(city => city?.data != null && !city.isRekt()).Sum(CityValue)
            : RealmValue(kingdom);
    }

    #endregion

    #region 筹码

    public struct Leverage
    {
        // 从对方手里打下的城、占着的城(按占了几成)、兵力优势
        public int taken, occupied, strength;
        public float ratio;
        public int Total => taken + occupied + strength;
        public int Cities => taken + occupied;
    }

    // attackers = 攻方一边的筹码，否则守方一边
    public static Leverage LeverageOf(War war, bool attackers)
    {
        var result = new Leverage();
        Kingdom side = attackers ? war?.main_attacker : war?.main_defender, foe = attackers ? war?.main_defender : war?.main_attacker;
        if (war?.data == null || side?.data == null || foe?.data == null || side.isRekt() || foe.isRekt()) return result;
        result.taken = Math.Max(0, WarSituation.CitiesLost(war, foe)) * AverageCityValue(foe);
        double occupied = 0d;
        foreach (City city in foe.cities)
            if (city?.data != null && !city.isRekt()) occupied += CityValue(city) * OccupiedShare(war, city, attackers);
        result.occupied = (int)Math.Round(occupied);
        // StrengthRatio(core, enemy) 给的是 enemy / core，所以这里传 (foe, side) 才是"我方比对方"
        result.ratio = WarSituation.StrengthRatio(foe, side);
        if (result.ratio > 1f)
            result.strength = (int)Math.Round((Math.Min(result.ratio, StrengthCap) - 1f) / (StrengthCap - 1f) * StrengthShare *
                                              RealmValue(foe));
        return result;
    }

    // 攻方一边(attackers)或守方一边占了这座城几成地块
    public static float OccupiedShare(War war, City city, bool attackers)
    {
        if (attackers) return WarObjective.HeldShareOf(war, city);
        if (war?.data == null || city?.data == null || city.isRekt()) return 0f;
        if (city.kingdom != null && war._list_defenders.Contains(city.kingdom)) return 1f;
        int total = city.zones?.Count ?? 0;
        if (total == 0) return 0f;
        int held = 0;
        foreach (Kingdom kingdom in war._list_defenders)
            if (kingdom?.data != null && !kingdom.isRekt()) held += city.GetOccupiedTileZoneCount(kingdom);
        return Math.Min(1f, held / (float)total);
    }

    #endregion

    #region 疲敝、势头

    public struct Wear
    {
        public int years, unpaid, food, collapse, occupation;
        public int yearCount, unpaidMonths, occupiedCities;
        public float foodShort;
        public int Total => years + unpaid + food + collapse + occupation;
    }

    public static Wear WearOf(War war, bool attackers)
    {
        var result = new Wear();
        Kingdom side = attackers ? war?.main_attacker : war?.main_defender, foe = attackers ? war?.main_defender : war?.main_attacker;
        if (war?.data == null || side?.data == null || side.isRekt()) return result;
        int value = RealmValue(side);
        Kingdom realm = FrontSystem.Realm(side) ?? side;
        var data = realm.GetOrCreate();
        result.yearCount = (int)WarSituation.WarYears(war);
        result.unpaidMonths = Math.Max(0, data.corps_unpaid_months);
        result.foodShort = data.war_food_need > 0f && data.war_food_short > 0f ? Math.Min(1f, data.war_food_short / data.war_food_need) : 0f;
        double years = Math.Max(0, result.yearCount - (WarPeaceRules.MinWarYears - 1)) * WearPerYear;
        double unpaid = result.unpaidMonths * WearPerUnpaidMonth;
        double food = result.foodShort * WearFood;
        double collapse = foe?.data != null && FrontCollapse.Collapsing(side, foe) ? WearCollapse : 0d;
        double sum = years + unpaid + food + collapse;
        // 合计封顶，各项按比例缩
        double scale = sum > WearCap ? WearCap / sum : 1d;
        result.years = (int)Math.Round(value * years * scale);
        result.unpaid = (int)Math.Round(value * unpaid * scale);
        result.food = (int)Math.Round(value * food * scale);
        result.collapse = (int)Math.Round(value * collapse * scale);
        // 占领成本：直接军管着他国本土(另算，不进上面的封顶)
        (result.occupation, result.occupiedCities) = OccupationPuppetSystem.OccupationCost(realm, foe);
        return result;
    }

    // 势头：上次议和(每年一次，见 PeaceTable.Talks)以来新打下来的本钱
    public static int Momentum(War war, bool attackers, Leverage leverage)
    {
        var data = war?.GetOrCreate();
        if (data == null) return 0;
        int before = data.peace_talks_at < 0d ? 0 : attackers ? data.peace_momentum_attacker : data.peace_momentum_defender;
        return Math.Max(0, leverage.Cities - before);
    }

    #endregion
}

using System;

namespace EmpireCraft.Scripts.GeneralSystems;

// 草原压力、结盟、劫掠的纯算式(只依赖 System，便于单测；规则见 NomadRaidSystem)：
//   · 纪元(只算游牧)：寒冬纪元(冰河、绝望)20 + 15×纪元进度(寒冬越久牲畜死得越多)；不长庄稼的纪元(黑暗)10 + 10×进度；
//   · 牲畜粮食(白灾)：闹饥荒的城占比 ×25 + (游牧)牧群不满栏的比例 ×15，至多 30；
//   · 国库：欠饷 25；不欠饷时国库不到安全储备，差几成算几成 ×15，经常收支还亏 +5，至多 25；
//   · 互市岁币(只算游牧)：十年内岁币停付 +12、互市断绝 +8、朝贡赏赐断绝 +12，至多 20；
//   · 合计到 RaidThreshold(40)就要南下劫掠。
public static class SteppeRules
{
    public const float RaidThreshold = 40f;
    public const float WinterBase = 20f, WinterRamp = 15f, DarkBase = 10f, DarkRamp = 10f;
    public const float FamineWeight = 25f, HerdWeight = 15f, HerdCap = 30f;
    public const float UnpaidPressure = 25f, ReserveWeight = 15f, DeficitPressure = 5f, TreasuryCap = 25f;
    public const float TributeCutPressure = 12f, MarketCutPressure = 8f, GiftCutPressure = 12f, MarketCap = 20f;

    public static float Era(bool nomad, bool winter, bool cropsGrow, float progress)
    {
        if (!nomad) return 0f;
        float p = Clamp01(progress);
        if (winter) return WinterBase + WinterRamp * p;
        return cropsGrow ? 0f : DarkBase + DarkRamp * p;
    }

    public static float Herd(bool nomad, float famineShare, float herdFill) =>
        Math.Min(HerdCap, FamineWeight * Clamp01(famineShare) + (nomad ? HerdWeight * (1f - Clamp01(herdFill)) : 0f));

    // reserveRatio：国库余额 / 安全储备(储备为 0 时传 1)
    public static float Treasury(bool unpaid, float reserveRatio, bool deficit)
    {
        if (unpaid) return UnpaidPressure;
        float shortfall = reserveRatio < 1f ? ReserveWeight * (1f - Math.Max(0f, reserveRatio)) : 0f;
        return Math.Min(TreasuryCap, shortfall + (deficit ? DeficitPressure : 0f));
    }

    public static float Market(bool nomad, bool tributeCut, bool marketCut, bool giftCut) =>
        !nomad ? 0f : Math.Min(MarketCap, (tributeCut ? TributeCutPressure : 0f) + (marketCut ? MarketCutPressure : 0f) +
                                          (giftCut ? GiftCutPressure : 0f));

    public static float Total(float era, float herd, float treasury, float market) =>
        Math.Min(100f, Math.Max(0f, era + herd + treasury + market));

    // 应召入盟的机会：自身压力越大越想去；同文化 +0.2、姻亲 +0.25、同一汗国(或同奉一主) +0.3、比盟主弱一半以上 +0.1(依附强者)；
    // 对盟主好感很差(−20 以下)−0.25。夹 0.05~0.9
    public static float JoinChance(float pressure, bool sameCulture, bool inLaw, bool sameRealm, bool muchWeaker, bool hostile)
    {
        float chance = 0.1f + Math.Max(0f, pressure) / 100f + (sameCulture ? 0.2f : 0f) + (inLaw ? 0.25f : 0f) +
                       (sameRealm ? 0.3f : 0f) + (muchWeaker ? 0.1f : 0f) - (hostile ? 0.25f : 0f);
        return Math.Max(0.05f, Math.Min(0.9f, chance));
    }

    // 抢够：单国一城；联盟每多一部多抢一城，至多四城
    public static float SackGoal(int members) => Math.Max(1, Math.Min(4, members));

    // 长城关隘之固：边上有墙的地块占比 ×0.7，接壤的城里有完好关城 +0.3，至多 1
    public static float Defense(float wallShare, bool fort) => Math.Min(1f, 0.7f * Clamp01(wallShare) + (fort ? 0.3f : 0f));

    // 洗劫一城拿走几成：金帛三成(旧规"劫掠只拿三成国库")、粮三成、背景人口八分之一成；关隘之固减半为止；
    // 屯田实边的边城守得住：金帛七折、粮只剩四分之一、人口减半
    public const float MoneyShare = 0.3f, FoodShare = 0.3f, PeopleShare = 0.08f;

    public static float LootFactor(float defense) => 1f - 0.5f * Clamp01(defense);

    // 选目标的打分：财富 × 强弱比(至多 3) × 关隘(至多打六折) × 断了互市、岁币的那家加成
    public static double TargetScore(double wealth, double powerRatio, float defense, bool grievance) =>
        Math.Max(0d, wealth) * Math.Min(3d, Math.Max(0d, powerRatio)) * (1d - 0.6d * Clamp01(defense)) * (grievance ? 2d : 1d);

    // 被劫一方每月买和的机会：已被洗劫的城越多越想买；对方比自己强、手上还有别的仗更想买；自己强出一倍不买。夹 0~0.6
    public static float BuyOffChance(float sacked, double raiderOverPayer, bool otherWars)
    {
        float chance = 0.08f + 0.12f * Math.Max(0f, sacked) + (raiderOverPayer > 1d ? 0.15f : 0f) + (otherWars ? 0.1f : 0f) -
                       (raiderOverPayer < 0.5d ? 0.15f : 0f);
        return Math.Max(0f, Math.Min(0.6f, chance));
    }

    // 争草场的机会(每年每对)：0.02 + 0.005×两边牲畜粮食压力之和；攻方强出一半 ×1.5、比对方弱 ×0.5；至多 0.25
    public static float FeudChance(float herdA, float herdB, double powerRatio)
    {
        float chance = 0.02f + 0.005f * (Math.Max(0f, herdA) + Math.Max(0f, herdB));
        chance *= powerRatio >= 1.5d ? 1.5f : powerRatio < 1d ? 0.5f : 1f;
        return Math.Min(0.25f, chance);
    }

    // 盟主转雄主的机会(每回得胜之后掷一次)：威望 2 起 10%，每多一回 +10%，至多 60%
    public static float HeroChance(int prestige) => prestige < 2 ? 0f : Math.Min(0.6f, 0.1f * (prestige - 1));

    private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
}

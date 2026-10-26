using System;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 君主资质(群友反馈"每代帝王都是明君，政治清明根本不会有腐败")：
//   即位时按才干、品性、成长环境定为 明君 / 中主 / 庸主 / 昏君(昏聩或荒淫) / 暴君，记作特质
//   (明君 wiseRuler、庸弱 weakRuler、昏聩 fatuousRuler、荒淫 dissoluteRuler、暴虐 tyrantRuler；中主不记)：
//   · 才干：治国 + 智力；品性：原版正负特质(懒惰、愚蠢偏昏聩，贪婪偏荒淫，邪恶、嗜血、疯狂偏暴虐)；
//   · 成长环境：开国前后在马上、民间长大的头两三代偏好；生在深宫的后代随王朝年岁(每百年)与王朝周期压力逐代变差；
//     幼年即位的更易庸弱(原型汉朝前几代明君，后几代多早逝，幼主相继)；
//   · 开国雄主、中兴之主、中兴雄主都算明君；有了庸弱、昏聩、荒淫、暴虐的不再得中兴之主。
// 效果(各处调用本类)：
//   · 腐败：中央每年 明君 -3%、庸弱 +1%、昏聩 +3.5%、荒淫 +3%、暴虐 +1.5%；昏聩、荒淫不整顿吏治，明君更常整顿；
//   · 正统：原来固定的"君主在位 +10"改按资质：明君 +14、中主 +10、庸弱 +3、昏聩与荒淫 -5、暴虐 -8；
//   · 批红：昏聩之君怠政，奏章交权臣或议程官代批；荒淫、暴虐之君偏好加税、不肯赈济与整顿吏治；
//   · 宫廷开支：荒淫之君每年多花一成五年度经常收入；
//   · 寿命：荒淫、昏聩短寿(特质数值)，后几代常早死；
//   · 王朝周期：庸弱每年 +0.15，昏聩、荒淫 +0.35，暴虐 +0.45；明君不再拉回(用户 2026-10-26 "积弊只能延缓")，
//     StrainDelta 的负值只表示明君，给当年增长打六折(见 DynasticCycleRules)；
//   · 苛政：暴虐之君在位 +15；
//   · 权臣、外戚：庸弱、昏聩之君在位，权臣控制度每月多涨 2，荒淫多涨 1。
public static class RulerQualitySystem
{
    public const string Wise = "wiseRuler", Weak = "weakRuler", Fatuous = "fatuousRuler",
        Dissolute = "dissoluteRuler", Tyrant = "tyrantRuler";

    public enum Grade { Average, Wise, Weak, Fatuous, Dissolute, Tyrant }

    private static readonly string[] Bad = { Weak, Fatuous, Dissolute, Tyrant };
    private const float PalaceSpending = 0.15f;

    public static Grade Of(Actor ruler)
    {
        if (ruler?.data == null) return Grade.Average;
        if (ruler.hasTrait(Tyrant)) return Grade.Tyrant;
        if (ruler.hasTrait(Dissolute)) return Grade.Dissolute;
        if (ruler.hasTrait(Fatuous)) return Grade.Fatuous;
        if (ruler.hasTrait(Weak)) return Grade.Weak;
        if (ruler.hasTrait(Wise) || ruler.hasTrait(RulerTraitSystem.Founder) || ruler.hasTrait(RulerTraitSystem.Restorer) ||
            ruler.hasTrait(RulerTraitSystem.GreatRestorer)) return Grade.Wise;
        return Grade.Average;
    }

    public static bool IsBad(Actor ruler) => ruler?.data != null && Bad.Any(ruler.hasTrait);

    // 怠政：昏聩(不理朝政)
    public static bool Neglects(Actor ruler) => Of(ruler) == Grade.Fatuous;

    // 不整顿吏治：昏聩、荒淫
    public static bool Indulgent(Actor ruler) => Of(ruler) is Grade.Fatuous or Grade.Dissolute;

    private static string TraitOf(Grade grade) => grade switch
    {
        Grade.Wise => Wise,
        Grade.Weak => Weak,
        Grade.Fatuous => Fatuous,
        Grade.Dissolute => Dissolute,
        Grade.Tyrant => Tyrant,
        _ => null
    };

    #region 即位定性

    // 即位时定性(RulerTraitSystem.OnCrowned 在开国雄主、中兴之主判定之后调用；旧档在位的君主由每年的检查补定)；
    // 只定一次(ActorExtraData.ruler_assessed)，中主也记下，不年年重掷
    public static void Assess(Empire empire, Actor ruler)
    {
        if (empire?.data == null || ruler?.data == null || !ruler.isAlive()) return;
        var data = ruler.GetOrCreate();
        if (data.ruler_assessed) return;
        data.ruler_assessed = true;
        if (Of(ruler) != Grade.Average) return;
        string trait = TraitOf(Roll(empire, ruler));
        if (trait != null) ruler.addTrait(trait);
    }

    private static Grade Roll(Empire empire, Actor ruler)
    {
        float talent = Mathf.Clamp((ruler.stats["stewardship"] + ruler.stats["intelligence"] - 20f) / 10f, -1.5f, 1.5f);
        int balance = 0;
        foreach (ActorTrait trait in ruler.traits)
        {
            if (trait.type == TraitType.Positive) balance++;
            else if (trait.type == TraitType.Negative) balance--;
        }
        float score = talent * 0.6f + Mathf.Clamp(balance, -3, 3) * 0.3f - PalaceDecay(empire, ruler);
        bool child = !ruler.isAdult();
        float up = Mathf.Exp(score), down = Mathf.Exp(-score);
        float wise = 22f * up, average = 45f;
        float weak = 14f * down * (child ? 2f : 1f);
        float fatuous = 8f * down * (ruler.hasTrait("lazy") ? 2f : 1f) * (ruler.hasTrait("stupid") ? 1.5f : 1f);
        float dissolute = 7f * down * (ruler.hasTrait("greedy") ? 2f : 1f);
        float tyrant = 4f * Mathf.Exp(-0.5f * score) *
                       (ruler.hasTrait("evil") || ruler.hasTrait("bloodlust") || ruler.hasTrait("madness") ? 3f : 1f) *
                       (ruler.hasTrait("paranoid") ? 1.5f : 1f);
        float roll = UnityEngine.Random.value * (wise + average + weak + fatuous + dissolute + tyrant);
        if ((roll -= wise) < 0f) return Grade.Wise;
        if ((roll -= average) < 0f) return Grade.Average;
        if ((roll -= weak) < 0f) return Grade.Weak;
        if ((roll -= fatuous) < 0f) return Grade.Fatuous;
        if ((roll -= dissolute) < 0f) return Grade.Dissolute;
        return Grade.Tyrant;
    }

    private const int EarlyGenerationYears = 60;

    // 深宫之弊：王朝开国后出生的(生在深宫)按王朝年岁与周期压力变差；开国前后出生的头几代偏好
    private static float PalaceDecay(Empire empire, Actor ruler)
    {
        int dynastyAge = DynasticCycleSystem.GetAge(empire);
        int ruleAge = Math.Max(0, (int)Math.Round((double)ruler.getAge()));
        bool palaceRaised = dynastyAge > ruleAge;
        if (!palaceRaised) return -0.3f;
        // 历史周期律：立国头 60 年里长大的二三代还带着开国的家风(文景、贞观之后)，小有加成；之后随年岁、积弊变差
        float pressure = DynasticCycleSystem.GetPressure(empire) / 150f;
        if (dynastyAge < EarlyGenerationYears) return -0.1f + pressure;
        return Mathf.Min(1.5f, 0.1f + (dynastyAge - EarlyGenerationYears) / 100f + pressure);
    }

    #endregion

    #region 效果

    public static double CorruptionDelta(Actor ruler) => Of(ruler) switch
    {
        Grade.Wise => -0.03d,
        Grade.Weak => 0.01d,
        Grade.Fatuous => 0.035d,
        Grade.Dissolute => 0.03d,
        Grade.Tyrant => 0.015d,
        _ => 0d
    };

    // 正统里"君主在位"一项
    public static (string key, int value) LegitimacyItem(Actor ruler) => Of(ruler) switch
    {
        Grade.Wise => ("mandate_ruler_wise", 14),
        Grade.Weak => ("mandate_ruler_weak", 3),
        Grade.Fatuous => ("mandate_ruler_fatuous", -5),
        Grade.Dissolute => ("mandate_ruler_dissolute", -5),
        Grade.Tyrant => ("mandate_ruler_tyrant", -8),
        _ => ("mandate_ruler_order", 10)
    };

    public static float StrainDelta(Actor ruler) => Of(ruler) switch
    {
        Grade.Wise => -0.3f,
        Grade.Weak => 0.15f,
        Grade.Fatuous => 0.35f,
        Grade.Dissolute => 0.35f,
        Grade.Tyrant => 0.45f,
        _ => 0f
    };

    // 苛政：暴虐之君
    public static float TyrannyBurden(Actor ruler) => Of(ruler) == Grade.Tyrant ? 15f : 0f;

    // 权臣(与外戚)控制度每月多涨
    public static int MinisterBonus(Actor ruler) => Of(ruler) switch
    {
        Grade.Weak or Grade.Fatuous => 2,
        Grade.Dissolute => 1,
        _ => 0
    };

    // 皇帝本人对议题的立场：荒淫、暴虐之君偏好加税，不肯赈济、整顿吏治
    public static float Stance(Actor ruler, TemporaryFactionType issue)
    {
        Grade grade = Of(ruler);
        if (grade is not (Grade.Dissolute or Grade.Tyrant or Grade.Fatuous)) return 0f;
        return issue switch
        {
            TemporaryFactionType.提高赋税 => grade == Grade.Fatuous ? 0f : 1f,
            TemporaryFactionType.降低赋税 => -1f,
            TemporaryFactionType.赈济灾荒 => -1f,
            TemporaryFactionType.整顿吏治 => -1f,
            _ => 0f
        };
    }

    // 每年(ConstitutionalEconomySystem 年度结算)：荒淫之君的宫廷开支
    public static void Yearly(Empire empire)
    {
        Kingdom core = empire?.CoreKingdom;
        Actor ruler = empire?.Emperor;
        if (core?.data == null || ruler?.data == null || Of(ruler) != Grade.Dissolute) return;
        int cost = (int)Math.Min(Math.Max(0L, core.GetMoney()), (long)(StateDebtSystem.AnnualStableIncome(core) * PalaceSpending));
        if (cost > 0) core.SubMoney(cost, TreasuryCategory.Palace);
    }

    #endregion
}

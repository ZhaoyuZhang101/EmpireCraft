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
//     幼年即位先待成年，不把摄政的治理成果当成幼主本人的资质；
//   · 资质与开国、中兴功绩分开；幼主暂不定性，成年后评一次；有负面资质的不再获得中兴称号。
// 效果(各处调用本类)：
//   · 腐败：中央每年 明君 -3%、庸弱 +1%、昏聩 +3.5%、荒淫 +3%、暴虐 +1.5%；昏聩、荒淫不整顿吏治，明君更常整顿；
//   · 正统：原来固定的"君主在位 +10"改按资质：明君 +14、中主 +10、庸弱 +3、昏聩与荒淫 -5、暴虐 -8；
//   · 批红：昏聩之君怠政，奏章交权臣或议程官代批；荒淫、暴虐之君偏好加税、不肯赈济与整顿吏治；
//   · 宫廷开支：荒淫之君每年多花一成五年度经常收入；
//   · 寿命：荒淫、昏聩短寿(特质数值)，后几代常早死；
//   · 王朝周期：庸弱每年 +0.15，昏聩、荒淫 +0.35，暴虐 +0.45；明君不再拉回(用户 2026-10-26 "积弊只能延缓")，
//     StrainDelta 的负值只表示明君，给当年增长打六折(见 DynasticCycleRules)；
//   · 苛政：暴虐之君在位 +15；
//   · 权臣、外戚：庸弱、昏聩之君在位，权臣控制度每月多涨 2，荒淫多涨 1；
//   · 开战(用户 2026-11-05 转群友"开国雄主平静地死去，荒淫无道之昏君反而在打仗")：帝国主动谋划开战的意愿按资质，见 WillsWarThisYear。
public static class RulerQualitySystem
{
    public const string Wise = "wiseRuler", Weak = "weakRuler", Fatuous = "fatuousRuler",
        Dissolute = "dissoluteRuler", Tyrant = "tyrantRuler";

    public enum Grade { Average, Wise, Weak, Fatuous, Dissolute, Tyrant, Minor }

    private static readonly string[] Bad = { Weak, Fatuous, Dissolute, Tyrant };
    private const float PalaceSpending = 0.15f;

    public static Grade Of(Actor ruler)
    {
        if (ruler?.data == null) return Grade.Average;
        if (ruler.hasTrait(Tyrant)) return Grade.Tyrant;
        if (ruler.hasTrait(Dissolute)) return Grade.Dissolute;
        if (ruler.hasTrait(Fatuous)) return Grade.Fatuous;
        if (ruler.hasTrait(Weak)) return Grade.Weak;
        if (RealisticAgeSystem.IsMinor(ruler)) return Grade.Minor;
        if (ruler.hasTrait(Wise)) return Grade.Wise;
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

    // 即位时先评资质，再检查功绩；幼主等成年后评一次。中主也记下，不年年重掷。
    public static void Assess(Empire empire, Actor ruler)
    {
        if (empire?.data == null || ruler?.data == null || !ruler.isAlive()) return;
        var data = ruler.GetOrCreate();
        // 手动改成负面资质后，旧明君特质不再并存；不改写玩家指定的负面资质。
        if (IsBad(ruler))
        {
            ruler.removeTrait(Wise);
            data.ruler_assessed = true;
            return;
        }
        if (RealisticAgeSystem.IsMinor(ruler))
        {
            // 迁移旧版幼年即位时自动定下的明君；成年后重新按本人资质评估。
            if (data.ruler_assessed) ruler.removeTrait(Wise);
            data.ruler_assessed = false;
            return;
        }
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
            // 官位与开国、中兴功绩不是品性，不能反过来抬高资质抽取权重。
            if (trait.id == RulerTraitSystem.Founder || trait.id == RulerTraitSystem.Restorer ||
                trait.id == RulerTraitSystem.GreatRestorer) continue;
            if (trait.type == TraitType.Positive) balance++;
            else if (trait.type == TraitType.Negative) balance--;
        }
        float score = talent * 0.6f + Mathf.Clamp(balance, -3, 3) * 0.3f - PalaceDecay(empire, ruler);
        float up = Mathf.Exp(score), down = Mathf.Exp(-score);
        float wise = 22f * up, average = 45f;
        float weak = 14f * down;
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
        // 分裂出的朝廷按它自己的年岁算，不把新一代算成几百年老朝的深宫子弟(见 DynasticCycleSystem.CourtAge)
        int dynastyAge = DynasticCycleSystem.CourtAge(empire);
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
        Grade.Minor => ("mandate_ruler_minor", 3),
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

    // 帝国主动谋划开战的意愿(每年有多大可能动这个念头)：开国雄主、中兴雄主、明君、暴君不打折，中主七成，庸弱四成，
    // 昏聩、荒淫、幼主两成。问鼎天下的统一战争、朝堂奏议不走这里(奏议看 Stance)
    private static float WarWill(Actor ruler)
    {
        if (ruler.hasTrait(RulerTraitSystem.Founder) || ruler.hasTrait(RulerTraitSystem.GreatRestorer)) return 1f;
        return Of(ruler) switch
        {
            Grade.Wise or Grade.Tyrant => 1f,
            Grade.Average => 0.7f,
            Grade.Weak => 0.4f,
            _ => 0.2f
        };
    }

    // 同一位君主同一年结果一样：开战谋划一年要查很多次，每次掷机会等于没有门槛
    public static bool WillsWarThisYear(Actor ruler)
    {
        if (ruler?.data == null) return true;
        float will = WarWill(ruler);
        if (will >= 1f) return true;
        ulong mix = unchecked((ulong)ruler.data.id * 0x9E3779B97F4A7C15UL + (ulong)Date.getCurrentYear() * 0xC2B2AE3D27D4EB4FUL);
        mix ^= mix >> 29;
        mix = unchecked(mix * 0xBF58476D1CE4E5B9UL);
        mix ^= mix >> 32;
        return (mix & 0xFFFF) / 65536f < will;
    }

    // 皇帝本人对议题的立场：荒淫、暴虐之君偏好加税，不肯赈济、整顿吏治；暴君好战，昏聩荒淫不愿对外用兵
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
            TemporaryFactionType.对外扩张 => grade == Grade.Tyrant ? 1f : -1f,
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

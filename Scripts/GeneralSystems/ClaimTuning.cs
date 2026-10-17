using System;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 诉求的施行时长、经费与完成结果：按诉求轻重分档，统一在这里调
//   · 时长(月)：宣战、改税、推动改革这类一纸诏令 24；削藩、教化、改继承法 48；改换国本(土改、游牧化、金玺诏书、共和与复辟) 72；
//   · 经费：以"一年一档福利"为一份(户数/100，至少 5)，兴兵、改制、赈济各按份数计；准奏施行时从国库支出，国库不够不施行；
//     地方推动的按该地户数计，由该地出钱；
//   · 结果：完成时写进史书与朝局纪事(诉求自己写了 CompletionOutcome 的以它为准，否则用 claim_outcome_诉求名)。
public static class ClaimTuning
{
    public const float LightMonths = 24f, MediumMonths = 48f, HeavyMonths = 72f;

    public static float Duration(TemporaryFactionType type) => type switch
    {
        TemporaryFactionType.降低赋税 or TemporaryFactionType.提高赋税 or TemporaryFactionType.提供岁币 or
            TemporaryFactionType.割让城池 or TemporaryFactionType.对外扩张 or TemporaryFactionType.迫使朝贡 or
            TemporaryFactionType.劫掠 or TemporaryFactionType.游牧扩张 or TemporaryFactionType.扩张地盘 or
            TemporaryFactionType.收回地盘 or TemporaryFactionType.谋求统一 or TemporaryFactionType.允许诸侯自由开战 or
            TemporaryFactionType.夺取诸侯开战权 or TemporaryFactionType.颁布帝国治安令 or
            TemporaryFactionType.确认诸侯特权 or TemporaryFactionType.供养宗室 or TemporaryFactionType.禁党 or
            TemporaryFactionType.国_打击贪腐 or TemporaryFactionType.整顿吏治 or TemporaryFactionType.赈济灾荒 or
            TemporaryFactionType.册立储君 or TemporaryFactionType.开科取士 or TemporaryFactionType.推行三省六部 or
            TemporaryFactionType.设立内阁 or TemporaryFactionType.转天朝制度 or TemporaryFactionType.推行普选 or
            TemporaryFactionType.开放党禁 or TemporaryFactionType.索取皇位 or TemporaryFactionType.诛宦官 or
            TemporaryFactionType.党锢 or TemporaryFactionType.大赦天下 or TemporaryFactionType.盐铁专营 or
            TemporaryFactionType.重农抑商 or TemporaryFactionType.封禅 or TemporaryFactionType.裁省用度 or
            TemporaryFactionType.独尊官学 => LightMonths,
        TemporaryFactionType.土地改革 or TemporaryFactionType.游牧化 or TemporaryFactionType.颁布金玺诏书 or
            TemporaryFactionType.恢复世袭皇权 or TemporaryFactionType.建立共和 or TemporaryFactionType.复辟帝制 or
            TemporaryFactionType.转周制 => HeavyMonths,
        _ => MediumMonths
    };

    // 经费份数
    public static float BudgetShares(TemporaryFactionType type) => type switch
    {
        TemporaryFactionType.对外扩张 or TemporaryFactionType.游牧扩张 or TemporaryFactionType.谋求统一 => 4f,
        TemporaryFactionType.迫使朝贡 or TemporaryFactionType.提供岁币 or TemporaryFactionType.推行三省六部 or
            TemporaryFactionType.转天朝制度 or TemporaryFactionType.土地改革 or TemporaryFactionType.赈济灾荒 or
            TemporaryFactionType.恢复圣地 or TemporaryFactionType.复辟帝制 or TemporaryFactionType.兴修水利 or
            TemporaryFactionType.封禅 => 3f,
        TemporaryFactionType.屯田实边 => 2f,
        TemporaryFactionType.扩张地盘 or TemporaryFactionType.收回地盘 or
            TemporaryFactionType.开科取士 or TemporaryFactionType.设立内阁 or TemporaryFactionType.推行普选 or
            TemporaryFactionType.供养宗室 or TemporaryFactionType.整顿吏治 or TemporaryFactionType.国_打击贪腐 or
            TemporaryFactionType.削藩 or TemporaryFactionType.神授君权 or TemporaryFactionType.恢复世袭皇权 or
            TemporaryFactionType.输出革命 or TemporaryFactionType.扶持革命党 or TemporaryFactionType.拓展金融霸权 or
            TemporaryFactionType.清除移民 or TemporaryFactionType.设置行政区 => 2f,
        TemporaryFactionType.劫掠 or TemporaryFactionType.开放党禁 or TemporaryFactionType.确立国教 or
            TemporaryFactionType.分封 or TemporaryFactionType.文化同化 or TemporaryFactionType.宗教同化 or
            TemporaryFactionType.游牧化 or TemporaryFactionType.转军府 or TemporaryFactionType.撤销军府 or
            TemporaryFactionType.册立储君 or TemporaryFactionType.颁布金玺诏书 or
            TemporaryFactionType.颁布帝国治安令 or TemporaryFactionType.开放移民 => 1f,
        _ => 0f
    };

    // 一份经费 = 一年一档福利的开销
    public static int Share(Empire empire, Kingdom localPayer = null)
    {
        int households = localPayer != null
            ? CityPopulationSystem.AbstractPopulationEnabled ? CityPopulationSystem.FiscalHouseholds(localPayer)
                : localPayer.getPopulationTotal()
            : empire == null ? 0
                : CityPopulationSystem.AbstractPopulationEnabled ? CityPopulationSystem.FiscalHouseholds(empire)
                    : empire.CountPopulation();
        return Math.Max(5, households / 100);
    }

    public static int Cost(Empire empire, TemporaryFactionType type, Kingdom localPayer = null)
    {
        float shares = BudgetShares(type);
        return shares <= 0f || empire == null && localPayer == null
            ? 0 : (int)Math.Round(shares * Share(empire, localPayer));
    }

    public static string OutcomeKey(TemporaryFactionType type) => "claim_outcome_" + type;

    // 没有专门结果文本的诉求返回 null
    public static string Outcome(TemporaryFactionType type)
    {
        string key = OutcomeKey(type);
        string text = LM.Get(key);
        return string.IsNullOrWhiteSpace(text) || text == key ? null : text;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;

namespace EmpireCraft.Scripts.GeneralSystems;

// 议题：派系诉求在朝堂上就是议题。
//   · 公共议题：赋税、战和、取士这类国是，每个派系都可以上奏，也都有立场；
//   · 独特议题：派系类型自带的诉求(削藩、分封、确立国教……)，只有本派能上奏，其他派系照样表态；
//   · 直接行动：索取皇位这类挑战皇权的诉求不经朝议，照旧由派系直接发动。
// 立场 = Σ(本派对各阶层的亲和度 × 议题对该阶层的利弊) ＋ 本派清单上有此议题 ＋ 本派清单上有相反议题(减)。
public static class CourtIssues
{
    public static readonly TemporaryFactionType[] PublicIssues =
    {
        TemporaryFactionType.提高赋税, TemporaryFactionType.降低赋税, TemporaryFactionType.对外扩张,
        TemporaryFactionType.提供岁币, TemporaryFactionType.开科取士, TemporaryFactionType.册立储君,
        TemporaryFactionType.整顿吏治, TemporaryFactionType.赈济灾荒
    };

    private static readonly (TemporaryFactionType a, TemporaryFactionType b)[] Opposites =
    {
        (TemporaryFactionType.提高赋税, TemporaryFactionType.降低赋税),
        (TemporaryFactionType.削藩, TemporaryFactionType.分封),
        (TemporaryFactionType.削藩, TemporaryFactionType.确认诸侯特权),
        (TemporaryFactionType.夺取诸侯开战权, TemporaryFactionType.允许诸侯自由开战),
        (TemporaryFactionType.对外扩张, TemporaryFactionType.提供岁币),
        (TemporaryFactionType.对外扩张, TemporaryFactionType.割让城池),
        (TemporaryFactionType.迫使朝贡, TemporaryFactionType.提供岁币),
        (TemporaryFactionType.转军府, TemporaryFactionType.撤销军府),
        (TemporaryFactionType.开放移民, TemporaryFactionType.清除移民),
        (TemporaryFactionType.自由信仰, TemporaryFactionType.确立国教),
        (TemporaryFactionType.拓展金融霸权, TemporaryFactionType.缩减金融霸权),
        (TemporaryFactionType.恢复世袭皇权, TemporaryFactionType.颁布金玺诏书),
        (TemporaryFactionType.转世袭, TemporaryFactionType.强者继承法),
        (TemporaryFactionType.宗教同化, TemporaryFactionType.宗教融入),
        (TemporaryFactionType.文化同化, TemporaryFactionType.文化转化),
        (TemporaryFactionType.文化同化, TemporaryFactionType.制度融入),
        (TemporaryFactionType.设置行政区, TemporaryFactionType.分封),
        (TemporaryFactionType.设置行政区, TemporaryFactionType.确认诸侯特权),
        (TemporaryFactionType.转天朝制度, TemporaryFactionType.转周制),
        (TemporaryFactionType.推行三省六部, TemporaryFactionType.转周制),
        (TemporaryFactionType.建立共和, TemporaryFactionType.复辟帝制),
        (TemporaryFactionType.渴望共和, TemporaryFactionType.神授君权),
        (TemporaryFactionType.宗教同化, TemporaryFactionType.自由信仰),
        (TemporaryFactionType.加强神权, TemporaryFactionType.自由信仰),
        (TemporaryFactionType.削藩, TemporaryFactionType.允许诸侯自由开战),
        (TemporaryFactionType.颁布帝国治安令, TemporaryFactionType.确认诸侯特权),
        (TemporaryFactionType.颁布帝国治安令, TemporaryFactionType.允许诸侯自由开战),
        (TemporaryFactionType.争夺主教叙任权, TemporaryFactionType.划地给教廷),
        (TemporaryFactionType.输出革命, TemporaryFactionType.禁党),
        (TemporaryFactionType.开放党禁, TemporaryFactionType.禁党),
        (TemporaryFactionType.土地改革, TemporaryFactionType.分封),
        (TemporaryFactionType.劫掠, TemporaryFactionType.制度融入),
        (TemporaryFactionType.提高赋税, TemporaryFactionType.赈济灾荒)
    };

    public const float SupportThreshold = 5f;

    public static bool IsPublic(TemporaryFactionType issue) => Array.IndexOf(PublicIssues, issue) >= 0;

    // 挑战皇权的诉求不经朝议
    public static bool IsDirectAction(TemporaryFactionType issue) => issue == TemporaryFactionType.索取皇位;

    public static IEnumerable<TemporaryFactionType> OppositesOf(TemporaryFactionType issue)
    {
        foreach ((TemporaryFactionType a, TemporaryFactionType b) in Opposites)
        {
            if (a == issue) yield return b;
            else if (b == issue) yield return a;
        }
    }

    // 本派系类型自带的诉求(配置清单)，不含公共议题与直接行动
    public static List<TemporaryFactionType> UniqueIssues(FixedFaction faction) =>
        (faction?.TemporaryFactionTypes ?? new List<TemporaryFactionType>())
        .Where(issue => !IsPublic(issue) && !IsDirectAction(issue)).Distinct().ToList();

    private static bool Holds(FixedFaction faction, TemporaryFactionType issue) =>
        faction?.TemporaryFactionTypesRecord?.Contains(issue) == true ||
        faction?.TemporaryFactionTypes?.Contains(issue) == true;

    // 立场分(约 -30 ~ 30)：正为支持，负为反对
    public static float Stance(FixedFaction faction, TemporaryFactionType issue)
    {
        if (faction == null) return 0f;
        FactionClassSystem.EnsureProfile(faction);
        float score = 0f;
        foreach (KeyValuePair<SocialClass, float> pair in faction.ClassAffinities)
            score += pair.Value / 100f * FactionClassSystem.GetClaimClassEffect(issue, pair.Key);
        if (Holds(faction, issue) && (!IsPublic(issue) || faction.TemporaryFactionTypes?.Contains(issue) == true))
            score += 12f;
        if (OppositesOf(issue).Any(opposite => faction.TemporaryFactionTypes?.Contains(opposite) == true))
            score -= 12f;
        return Math.Max(-30f, Math.Min(30f, score));
    }

    public static int StanceLevel(float score) =>
        score >= SupportThreshold ? 1 : score <= -SupportThreshold ? -1 : 0;

    public static int StanceLevel(FixedFaction faction, TemporaryFactionType issue) =>
        StanceLevel(Stance(faction, issue));

    // 带目标的立场：议题立场 ＋ 诉求针对的具体目标引起的立场(如册立储君立的是哪一派的皇子)
    public static float Stance(FixedFaction faction, TemporaryFaction claim) =>
        claim == null ? 0f : Math.Max(-30f, Math.Min(30f, Stance(faction, claim.type) + claim.TargetStance(faction)));

    public static int StanceLevel(FixedFaction faction, TemporaryFaction claim) =>
        StanceLevel(Stance(faction, claim));
}

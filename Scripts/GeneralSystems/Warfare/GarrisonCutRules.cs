using System;

namespace EmpireCraft.Scripts.GeneralSystems;

// 养不起兵时先裁谁(群友 2026-11-16："在国库缺钱时可以设法裁军，保留边境接壤常备军和京畿地区(两京)禁军，
// 内部常备军裁撤，仅在支援邻省时可以招募新军")：
//   以前量入为出(见 CorpsSystem.BudgetFactor)是各军镇一律按同一个成数缩编，边关和腹地裁得一样多。现在按军镇的位置分先后：
//     · 留着的：京畿(本势力的都城、陪都)、边境(邻城里有本势力以外的国)、驰援(战时本城或邻城正在交战)；
//     · 内地：其余的军镇。养得起的兵额先给留着的军镇配足，剩下的才分给内地；一点不剩时内地只留骨架(每个军团一人)，
//       兵逐月裁撤；等邻城打起来算成"驰援"，再逐月补员；
//     · 帝国里的内地省(成员国的军镇)：中央打不起仗(欠饷，或国库见底还在亏)时只留 InlandSkeleton 成的兵额，
//       哪怕本省自己还养得起——朝廷缺钱，先撤腹地的常备军。
//   国库养得起全部兵额时(budget ≥ 1)谁也不裁。军镇怎么分类见 CorpsSystem.KeepsStrength。
public static class GarrisonCutRules
{
    public const float InlandSkeleton = 0.25f;

    // keepNeed、inlandNeed：留着的军镇、内地军镇按人口该有的兵额；budget：养得起其中几成(0~1)；
    // realmStrained：帝国里的成员省而中央打不起仗。返回两类军镇各编几成
    public static (float keep, float inland) Split(float keepNeed, float inlandNeed, float budget, bool realmStrained)
    {
        budget = Math.Max(0f, Math.Min(1f, budget));
        float allowed = (Math.Max(0f, keepNeed) + Math.Max(0f, inlandNeed)) * budget;
        float keep = keepNeed <= 0f ? 1f : Math.Min(1f, allowed / keepNeed);
        float left = Math.Max(0f, allowed - Math.Max(0f, keepNeed));
        float inland = inlandNeed <= 0f ? 1f : Math.Min(1f, left / inlandNeed);
        if (realmStrained) inland = Math.Min(inland, InlandSkeleton);
        return (keep, inland);
    }
}

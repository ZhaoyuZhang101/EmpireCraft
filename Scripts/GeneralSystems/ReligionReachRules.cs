using System;

namespace EmpireCraft.Scripts.GeneralSystems;

// 宗教的辐射圈(群友 2026-11-16，用户转述："宗教扩张不要那么大范围那么广，古代没那么强的传播力，哪怕现代都没有")：
//   以前出了本天下的传教只打折、不设边界——势能 ×0.25(普世)/×0.05(民族)、几率再减半，可只要压得过本城惯性，
//   年复一年总会改宗，一千年下来一支宗教照样铺过几个文明。现在每支宗教从圣地(教派、地方教会从自己的中心)起算一个辐射半径：
//     · 半径 = 地图对角线 × 成数：普世宗教 UniversalShare、民族宗教 EthnicShare，再按圣地那座城的文化时代
//       从 EraFloor 涨到 1(石器时代最小、最后一个时代最大；到了头也只有对角线的两成)；
//     · 圈内不打折，出圈按距离线性衰减，到两倍半径处为零——再往外传不过去；
//     · 只管出了本天下的城：本天下之内照旧(本土大教在本天下立得住)；没有圣地的宗教不设圈；
//     · 邻城势能按它打折；国教势能(朝廷强推、殖民带过去的)至少留 StateFloor。
//   数值是模组自己的规则，调范围改这里的四个常量(见 BackgroundReligionSystem.RangeFactor)。
public static class ReligionReachRules
{
    public const float UniversalShare = 0.2f, EthnicShare = 0.1f, EraFloor = 0.5f, StateFloor = 0.35f;

    // 辐射半径(格)：diagonal 地图对角线(格)，eraTier 圣地那座城的文化时代档(1 起)，maxTier 最后一个时代的档
    public static float Radius(float diagonal, bool universal, int eraTier, int maxTier)
    {
        if (diagonal <= 0f) return 0f;
        float progress = maxTier <= 1 ? 1f : Math.Max(0f, Math.Min(1f, (eraTier - 1) / (float)(maxTier - 1)));
        return diagonal * (universal ? UniversalShare : EthnicShare) * (EraFloor + (1f - EraFloor) * progress);
    }

    // 离圣地 distance 格的城受到的势能倍数：圈内 1，两倍半径外 0；radius ≤ 0(没有圣地、算不出)不设圈
    public static float Factor(float distance, float radius)
    {
        if (radius <= 0f || distance <= radius) return 1f;
        return distance >= radius * 2f ? 0f : 1f - (distance - radius) / radius;
    }
}

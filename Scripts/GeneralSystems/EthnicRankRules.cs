using System;

namespace EmpireCraft.Scripts.GeneralSystems;

// 族群等第的数值规则(见 EthnicRankSystem)，不碰游戏对象，单独测
public static class EthnicRankRules
{
    // 等第：国族/旗人、色目/藩部、汉人/民人、南人(旗民分治没有第四等)
    public const int Ruling = 0, Allied = 1, Early = 2, Late = 3, Count = 4;

    public enum Scheme { None, FourClasses, BannerCivil }

    // 各等赋税轻重：四等人里国族多免、色目包税从轻、南人江南重赋；旗民分治里旗人的豁免归八旗(见 BannerSystem)，藩部因俗而治只纳贡
    private static readonly float[] FourTax = { 0.6f, 0.9f, 1.0f, 1.15f };
    private static readonly float[] BannerTax = { 1.0f, 0.6f, 1.0f, 1.0f };

    // 各等对稳定的影响：等第越低越不服；旗民分治远比四等人宽
    private static readonly float[] FourStability = { 2f, 0f, -3f, -6f };
    private static readonly float[] BannerStability = { 1f, 0f, -1.5f, -1.5f };

    // 团练：四等人禁汉人、南人执兵器，这两等的城团练只剩六成
    public const float DisarmedMilitia = 0.6f;

    // 中枢择官：四等人七成半的缺先尽国族、色目；满汉复职各半
    public const float FourOfficeQuota = 0.75f, BannerOfficeQuota = 0.5f;

    // 每年融合少几分：四等人防汉化
    public const int FourIntegrationDrag = 1;

    public static float TaxWeight(Scheme scheme, int rank) => Pick(scheme, FourTax, BannerTax, rank, 1f);

    public static float StabilityWeight(Scheme scheme, int rank) => Pick(scheme, FourStability, BannerStability, rank, 0f);

    private static float Pick(Scheme scheme, float[] four, float[] banner, int rank, float none)
    {
        if (rank < 0 || rank >= Count) return none;
        return scheme switch
        {
            Scheme.FourClasses => four[rank],
            Scheme.BannerCivil => banner[rank],
            _ => none
        };
    }

    // 一城按各等人口占比加权：shares 是四等各占几成(和可以不为 1，按和归一)
    public static float Weighted(Scheme scheme, float[] shares, Func<Scheme, int, float> weight, float none)
    {
        if (scheme == Scheme.None || shares == null) return none;
        float sum = 0f, value = 0f;
        for (int rank = 0; rank < Count && rank < shares.Length; rank++)
        {
            float share = Math.Max(0f, shares[rank]);
            sum += share;
            value += share * weight(scheme, rank);
        }
        return sum > 0f ? value / sum : none;
    }

    public static float TaxFactor(Scheme scheme, float[] shares) => Weighted(scheme, shares, TaxWeight, 1f);

    public static float Stability(Scheme scheme, float[] shares) => Weighted(scheme, shares, StabilityWeight, 0f);

    // 团练折扣：四等人里汉人、南人占几成就按几成缴械
    public static float MilitiaFactor(Scheme scheme, float[] shares)
    {
        if (scheme != Scheme.FourClasses || shares == null || shares.Length < Count) return 1f;
        float sum = 0f;
        foreach (float share in shares) sum += Math.Max(0f, share);
        if (sum <= 0f) return 1f;
        float disarmed = (Math.Max(0f, shares[Early]) + Math.Max(0f, shares[Late])) / sum;
        return 1f - disarmed * (1f - DisarmedMilitia);
    }

    // 这一次择官要不要先尽上等(roll 是 0~1 的随机数)
    public static bool FavorsRuling(Scheme scheme, float roll) => scheme switch
    {
        Scheme.FourClasses => roll < FourOfficeQuota,
        Scheme.BannerCivil => roll < BannerOfficeQuota,
        _ => false
    };

    // 四等人：禁军(中央的军)不收南人，南人只编新附军(地方)
    public static bool CentralRecruits(Scheme scheme, int rank) => !(scheme == Scheme.FourClasses && rank == Late);
}

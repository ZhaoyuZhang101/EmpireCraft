using System;

namespace EmpireCraft.Scripts.GeneralSystems;

// 八旗多元制的数值规则(见 BannerSystem)，不碰游戏对象，单独测
public static class BannerRules
{
    public const float HomeShare = 0.30f, GarrisonShare = 0.08f, SettleRate = 0.35f, ReleaseRate = 0.10f;
    public const float EnclosureStep = 0.30f, EnclosureCap = 0.60f, EnclosureFlight = 0.06f, LandTaxLoss = 0.60f;
    public const float StipendPerHead = 0.02f, StipendTreasuryCap = 0.15f;
    public const float VigorFloor = 30f, VigorMax = 100f, VigorPeaceDecay = 0.6f, VigorWarGain = 0.4f;
    public const int CityIntervalYears = 3, CityMax = 14, CitiesPerGarrison = 10;

    // 一座城旗人该占几成：旗人世代驻防，旧地(开局就在旗下的城)成丁皆兵，满城次之，别的城不编旗
    public static float TargetShare(bool home, bool manchuCity) => home ? HomeShare : manchuCity ? GarrisonShare : 0f;

    // 今年该有多少百姓编入旗下：只补不减，已经在旗的不赶走
    public static float ToEnroll(float total, float have, float target, float years)
    {
        float want = total * target - have;
        return want <= 0f ? 0f : want * Math.Min(1f, Math.Max(0f, SettleRate * years));
    }

    // 不在八旗多元制治下的城，旗人每年出旗为民
    public static float ToRelease(float have, float years) => have * Math.Min(1f, Math.Max(0f, ReleaseRate * years));

    // 圈一次地，旗地占到几成
    public static float NextLand(float land) => Math.Min(EnclosureCap, Math.Max(0f, land) + EnclosureStep);

    // 税收打几折：旗人不纳粮，旗地不交田赋(民田那部分照交)
    public static float TaxFactor(float bannerShare, float landShare)
    {
        float lost = Math.Max(0f, bannerShare) + Math.Max(0f, landShare) * LandTaxLoss;
        return Math.Max(0.2f, 1f - lost);
    }

    // 今年该发的旗饷，和国库能拿出来的；不够发的欠着，旗人要闹
    public static int StipendDue(float bannermen) => (int)Math.Max(0f, bannermen * StipendPerHead);

    public static int StipendPaid(int due, long treasury) =>
        (int)Math.Min(due, Math.Max(0L, (long)(treasury * StipendTreasuryCap)));

    // 八旗战力：承平日久渐渐不能打，打仗才磨得回来，老兵值封顶跟着它走
    public static float NextVigor(float vigor, bool atWar)
    {
        float next = vigor + (atWar ? VigorWarGain : -VigorPeaceDecay);
        return Math.Min(VigorMax, Math.Max(VigorFloor, next));
    }

    // 现在能有几座满城：城越多驻防越多，有个上限
    public static int CityLimit(int cities) => Math.Min(CityMax, 1 + Math.Max(0, cities) / CitiesPerGarrison);
}

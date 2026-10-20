using System;

namespace EmpireCraft.Scripts.GeneralSystems;

// 帝国权威(封建制帝国的皇权刻度，见 HolyRomanSystem.Authority)的纯算式(只依赖 System，便于单测)：
//   · 刻度 0~100，整数；档位：80 以上隆盛、60~79 稳固、40~59 平常、20~39 式微、20 以下名存实亡；
//   · 新帝国 50；旧档按现状折算：45 + 已加冕 10 + 帝国改革已施行 10 − 已订和约 30
//     + 家族领地(选帝制：两成五以上 +5、一成以下 −10) − 2×min(5, 让步条数)，夹 10~80；
//   · 软顶：权威到 80 以上时，所有"涨"减半(向上取整)，防止长期太平顶到 100；
//   · 作用：议会每票 +(权威−50)/200(100 时 +0.25、0 时 −0.25)；抗命几率 ×(1.5 − 权威/100)，夹 0.5~1.5。
public static class ImperialAuthorityRules
{
    public const int Min = 0, Max = 100, NewEmpire = 50;
    public const int Glorious = 80, Firm = 60, Ordinary = 40, Waning = 20;
    public const int TierNominal = 0, TierWaning = 1, TierOrdinary = 2, TierFirm = 3, TierGlorious = 4;
    public const int LegacyBase = 45, LegacyCrowned = 10, LegacyReform = 10, LegacySovereign = -30;
    public const int LegacyStrongHouse = 5, LegacyWeakHouse = -10, LegacyPerConcession = -2, LegacyConcessionCap = 5;
    public const int LegacyFloor = 10, LegacyCeiling = 80;
    public const float StrongHouseShare = 0.25f, WeakHouseShare = 0.1f;

    public static int Clamp(int value) => Math.Max(Min, Math.Min(Max, value));

    public static int Tier(int authority) =>
        authority >= Glorious ? TierGlorious
        : authority >= Firm ? TierFirm
        : authority >= Ordinary ? TierOrdinary
        : authority >= Waning ? TierWaning
        : TierNominal;

    // 旧档折算(elective = 选帝制，才算家族领地)
    public static int Initial(bool crowned, bool reformEnacted, bool sovereignPrinces, bool elective, float houseShare,
        int concessions)
    {
        int value = LegacyBase + (crowned ? LegacyCrowned : 0) + (reformEnacted ? LegacyReform : 0) +
                    (sovereignPrinces ? LegacySovereign : 0) +
                    LegacyPerConcession * Math.Min(LegacyConcessionCap, Math.Max(0, concessions));
        if (elective)
            value += houseShare >= StrongHouseShare ? LegacyStrongHouse : houseShare < WeakHouseShare ? LegacyWeakHouse : 0;
        return Math.Max(LegacyFloor, Math.Min(LegacyCeiling, value));
    }

    // 软顶：已在隆盛(80 以上)时涨幅减半、向上取整；跌不受影响
    public static int Gain(int current, int delta) => delta > 0 && current >= Glorious ? (delta + 1) / 2 : delta;

    // 涨跌之后的权威(含软顶与夹值)
    public static int Apply(int current, int delta) => Clamp(current + Gain(current, delta));

    // 议会每票的加减
    public static float StanceBonus(int authority) => (Clamp(authority) - 50) / 200f;

    // 抗命几率系数
    public static float RefusalFactor(int authority) => Math.Max(0.5f, Math.Min(1.5f, 1.5f - Clamp(authority) / 100f));

    // —— 诸侯私战与帝国法院(见 HolyRomanSystem.Feuds) ——
    public const float FeudBase = 0.05f, CourtBase = 0.35f, DefyBase = 0.25f;

    // 私战几率(每年每个够格攻方)：5% × (2 − 权威/50)，系数夹 0.2~2——权威 50 时 5%、25 时 7.5%、80 时 2%
    public static float FeudChance(int authority) => FeudBase * Math.Max(0.2f, Math.Min(2f, 2f - Clamp(authority) / 50f));

    // 法院判给原告的几率：0.35 + 原告是选帝侯 0.15 − 被告是选帝侯 0.10 + 原告与皇帝同族 0.10 − 被告与皇帝同族 0.10
    //   + (权威−50)/200 + 打点 0.10 + extra(陪臣化请愿另加)，夹 0.1~0.8
    public static float CourtGrantChance(bool plaintiffElector, bool defendantElector, bool plaintiffImperial,
        bool defendantImperial, int authority, bool bribed, float extra = 0f)
    {
        float chance = CourtBase + (plaintiffElector ? 0.15f : 0f) - (defendantElector ? 0.1f : 0f) +
                       (plaintiffImperial ? 0.1f : 0f) - (defendantImperial ? 0.1f : 0f) + StanceBonus(authority) +
                       (bribed ? 0.1f : 0f) + extra;
        return Math.Max(0.1f, Math.Min(0.8f, chance));
    }

    // —— 陪臣化(见 HolyRomanSystem.Immediacy) ——
    public const float PetitionBase = 0.08f;

    // 大诸侯请求并吞一城小邦的几率(每帝国每年)：8% × (1.5 − 权威/100)——权威 50 时 8%、0 时 12%、100 时 4%
    public static float PetitionChance(int authority) => PetitionBase * (1.5f - Clamp(authority) / 100f);

    // 被告抗判的几率：0.25 × (1.5 − 权威/100) + 被告兵力是原告两倍以上 0.2，夹 0~0.7
    public static float DefyRulingChance(int authority, bool defendantMuchStronger) =>
        Math.Max(0f, Math.Min(0.7f, DefyBase * (1.5f - Clamp(authority) / 100f) + (defendantMuchStronger ? 0.2f : 0f)));
}

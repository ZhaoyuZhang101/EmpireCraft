using System;

namespace EmpireCraft.Scripts.GeneralSystems;

// 附庸博弈的天平数字(见 VassalReduction)。砝码统一折成"户"(议和台的口径，PeaceTermRules)：basis = 那一国全境的户数。
// 只有算术，不碰游戏对象，回归里单独拿出来测(tests/VassalReduction.Tests.ps1)。
//   对方的抗拒：
//     立国之本  户数 × 身份(羁縻三成、帝国里的封国四成半、封建附庸五成半、起义军六成、朝贡国七成、接壤的独立小国十成)
//     兵力      户数 × 五成 × 对方兵力 ÷ 朝廷兵力(至多一倍半)
//     离心      户数 × 三成 ×(20 − 对朝廷的好感)%：好感二十以上不算，负八十算满
//     世袭已久  户数 × 三成 × 立国年数 ÷ 60(满六十年算满)
//     风俗不同  户数 × 两成(文化与朝廷不同)
//   朝廷的砝码：
//     正统      户数 × 六成 × 正统%
//     兵威      户数 × 五成 ×(朝廷兵力 ÷ 对方兵力 − 1)÷ 4：兵力相当不算，五倍算满
//     恩义      户数 × 三成 × 对朝廷的好感%
//     许以库银  朝廷库银的一成 / 两成半 / 五成，照议和台赔款的折法折成户(另算，见 VassalReduction.Extras)
//     授以爵衔  户数 × 一成半(朝廷还有空着的虚衔才摆得出)
//   朝廷的砝码不轻于对方的抗拒才谈得成。
//   代价：改设之费(朝廷库银的 8%)；其余藩属自危(好感 −10，强推 −25，二十年里淡去，至多叠到 −60)；
//         新设的行政区不稳(各城自然稳定度当年 −6，十年里渐渐服帖，同改土归流)
public static class VassalReductionRules
{
    public const int Jimi = 0, Member = 1, Vassal = 2, Tributary = 3, Neighbour = 4, Rebel = 5;
    public const double FeeShare = 0.08d;
    public const int AlarmPeace = 10, AlarmForce = 25, AlarmMax = 60;
    public const float AlarmFadeYears = 20f;

    public static double BaseShare(int kind) => kind switch
    {
        Jimi => 0.3d, Member => 0.45d, Vassal => 0.55d, Rebel => 0.6d, Tributary => 0.7d, _ => 1d
    };

    private static int Share(int basis, double share) => (int)Math.Round(Math.Max(0, basis) * share);

    private static double Clamp(double value, double low, double high) => value < low ? low : value > high ? high : value;

    public static int Standing(int basis, int kind) => Share(basis, BaseShare(kind));

    public static int Arms(int basis, double theirs, double ours) => Share(basis, 0.5d * Clamp(theirs / Math.Max(1d, ours), 0d, 1.5d));

    public static int Estrangement(int basis, int opinion) => Share(basis, 0.3d * Clamp((20 - opinion) / 100d, 0d, 1d));

    public static int Lineage(int basis, int years) => Share(basis, 0.3d * Clamp(years / 60d, 0d, 1d));

    public static int Custom(int basis, bool differs) => differs ? Share(basis, 0.2d) : 0;

    public static int Legitimacy(int basis, int legitimacy) => Share(basis, 0.6d * Clamp(legitimacy, 0d, 100d) / 100d);

    public static int Might(int basis, double theirs, double ours) => Share(basis, 0.5d * Clamp((ours / Math.Max(1d, theirs) - 1d) / 4d, 0d, 1d));

    public static int Grace(int basis, int opinion) => Share(basis, 0.3d * Clamp(opinion / 100d, 0d, 1d));

    public static int Title(int basis) => Share(basis, 0.15d);

    public static long Fee(long treasury) => treasury <= 0L ? 0L : (long)(treasury * FeeShare);

    // 接壤的独立国算不算"小国"：全境户数不到朝廷直辖之地的三分之一
    public static bool Small(int theirs, int ours) => theirs > 0 && theirs * 3L <= ours;

    // 藩属自危：眼下还剩多少(正数)，再添一笔之后是多少
    public static int AlarmNow(int value, float years) =>
        value <= 0 ? 0 : (int)Math.Round(value * Clamp(1d - years / AlarmFadeYears, 0d, 1d));

    public static int AlarmAdd(int now, int add) => Math.Min(AlarmMax, Math.Max(0, now) + Math.Max(0, add));
}

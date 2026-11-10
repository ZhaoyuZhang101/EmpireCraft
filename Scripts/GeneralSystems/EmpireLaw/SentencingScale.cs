using System;

namespace EmpireCraft.Scripts.GeneralSystems.EmpireLaw;

// 量刑天平(用户 2026-11-10："皇室犯罪要减轻""参考 CK3 那套流程，将他的一些罪名和政绩、叛乱、自立等行为做成数值，然后就像
// 战斗系统那样有砝码""皇室在新的系统里是有基础判罪数值的""影响力也是砝码的一部分")：
//   罪责 = 前科 × 叛逆(起兵叛乱、拥兵自立、犯过十恶) × 严苛之政 × 与君主有宿怨
//   情面 = 皇室(储君、近支、宗室) × 议贵(爵位) × 议功(勋级) × 政绩 × 影响力(名望比君主) × 宠信 × 宽仁之政
//   天平 = 罪责 ÷ 情面：每过 Step 倍加、减一等(四舍五入)，加至多 MaxHeavier 等(加不至死，见 EmpireLawSystem.Escalate)，
//   减至多 MaxLighter 等。十恶只看罪责、不论情面(十恶不在八议)。
// 这里只有算术，不碰游戏对象(取数、施刑见 EmpireLawSystem.BuildScale)
public static class SentencingScale
{
    public const float Step = 1.6f;
    public const int MaxHeavier = 1;
    public const int MaxLighter = 3;
    // 减到这么多等：免抄家(改罚金)；免削爵
    public const int SpareEstateAt = 2;
    public const int SparePeerageAt = 3;

    // 皇室的基础砝码
    public const float RoyalHeir = 3.0f;   // 储君
    public const float RoyalClose = 2.5f;  // 近支：君主的子女、兄弟、父母
    public const float RoyalKin = 1.8f;    // 宗室远支
    // 议贵：公以上；侯伯与封号
    public const float PeerHigh = 1.4f;
    public const float PeerLow = 1.2f;
    // 议功：勋级护军以上
    public const float GreatMerit = 1.4f;
    // 宠信：本朝宠臣
    public const float Favourite = 1.5f;
    // 宿怨：与君主结仇
    public const float RulerRival = 1.3f;

    // 每有一条前科罪责加两成，至多四条
    public static float PriorFactor(int convictions) => 1f + 0.2f * Clamp(convictions, 0, 4);

    // 每次起兵叛乱、拥兵自立或犯过十恶罪责加三成，至多四次
    public static float DefianceFactor(int count) => 1f + 0.3f * Clamp(count, 0, 4);

    // 宽严(-1 宽仁 ~ +1 严苛)：严苛压在罪责一边，宽仁压在情面一边，满了各 ×1.4
    public static float HarshFactor(float severity) => 1f + 0.4f * Math.Max(0f, Math.Min(1f, severity));
    public static float LenientFactor(float severity) => 1f + 0.4f * Math.Max(0f, Math.Min(1f, -severity));

    // 政绩(累计)：每 5000 加一倍的情面，-750 ~ +1500 之间(劣绩反而减情面)，即 ×0.85 ~ ×1.30
    public static float PerformanceFactor(double totalPerformance) =>
        1f + (float)(Math.Max(-750d, Math.Min(1500d, totalPerformance)) / 5000d);

    // 影响力(名望)：比君主的名望，到了君主那么高 ×1.5；名望不足 MinRenown 不算。没有君主时按名望本身(300 以上 ×1.3、120 以上 ×1.15)
    public const int MinRenown = 30;
    public static float InfluenceFactor(int renown, int rulerRenown, bool hasRuler)
    {
        if (renown < MinRenown) return 1f;
        if (!hasRuler) return renown >= 300 ? 1.3f : renown >= 120 ? 1.15f : 1f;
        float ratio = Math.Min(1f, renown / (float)Math.Max(1, rulerRenown));
        return 1f + 0.5f * ratio;
    }

    // 赃额：每 200 两加五成罪责，至多 ×1.5(未发之赃按每回贪墨累计，见 EmpireLawSystem.RecordCrime)
    public static float LootFactor(int amount) => 1f + 0.5f * Math.Min(1f, Math.Max(0, amount) / 200f);

    // 告发者的影响力(名望)比犯人：告发者至少有 MinRenown 名望，到了犯人那么高罪责 ×1.3
    public static float AccuserFactor(int accuserRenown, int renown)
    {
        if (accuserRenown < MinRenown) return 1f;
        return 1f + 0.3f * Math.Min(1f, accuserRenown / (float)Math.Max(1, renown));
    }

    // 君主性情(见 RulerQualitySystem)：暴君情面不上秤，罪责再 ×1.6(一律从重)；昏君偏袒宠臣，宠信 ×3
    public const float TyrantGuilt = 1.6f;
    public const float FatuousFavourite = 3.0f;

    // 硬判：单看情面本该减 HardlineMercyGrades 等以上，却因罪责、暴君、十恶少减了这么多等，又判了徒以上
    public const int HardlineMercyGrades = 2;
    // 放过：罪责到了这么重，却因情面减了等
    public const float LenientGuiltWeight = 1.6f;

    // 天平：十恶不论情面
    public static float Balance(float guilt, float mercy, bool grave) =>
        Math.Max(0.0001f, guilt) / (grave ? 1f : Math.Max(0.0001f, mercy));

    // 单看情面能减几等(负数)：硬判的代价按它与实际减等的差算
    public static int MercyShift(float mercy) => Shift(1f / Math.Max(0.0001f, mercy));

    // 加减几等：正为加、负为减
    public static int Shift(float balance)
    {
        int shift = (int)Math.Round(Math.Log(Math.Max(0.0001f, balance)) / Math.Log(Step), MidpointRounding.AwayFromZero);
        return Clamp(shift, -MaxLighter, MaxHeavier);
    }

    private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
}

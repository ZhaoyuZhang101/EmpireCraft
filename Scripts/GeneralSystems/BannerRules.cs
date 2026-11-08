using System;

namespace EmpireCraft.Scripts.GeneralSystems;

// 八旗多元制的数值规则(见 BannerSystem)，不碰游戏对象，单独测
public static class BannerRules
{
    // 旗下旧地五成编旗(用户 2026-11-09 "很多汉人都被编入汉八旗了"：辽东汉民编作汉军、包衣，以前三成)
    public const float HomeShare = 0.50f, GarrisonShare = 0.08f, SettleRate = 0.35f, ReleaseRate = 0.10f;
    public const float EnclosureStep = 0.30f, EnclosureCap = 0.60f, EnclosureFlight = 0.06f, LandTaxLoss = 0.60f;
    public const float StipendPerHead = 0.02f, StipendTreasuryCap = 0.15f;
    public const float VigorFloor = 30f, VigorMax = 100f, VigorEntry = 90f, VigorPeaceDecay = 0.6f, VigorWarGain = 0.4f;
    public const int CityIntervalYears = 3, CityMax = 14, CitiesPerGarrison = 10;

    // 守老家(八旗、开局定下朝廷的征服王朝)：旗下旧地，或法理地的文化就是统治文化的，留作直辖，不按当地文化分成封国、也不设省
    public static bool KeepsHome(string titleCulture, string rulingCulture, bool bannerHome) =>
        bannerHome || !string.IsNullOrEmpty(rulingCulture) && string.Equals(titleCulture, rulingCulture, StringComparison.Ordinal);

    // 八旗交战加成：禁旅八旗锐气满时 ×(1+EliteBonus)，锐气落到谷底(VigorFloor)就和别的军一样
    public const float EliteBonus = 0.5f;

    public static float CombatFactor(float vigor) =>
        1f + EliteBonus * Math.Min(1f, Math.Max(0f, (vigor - VigorFloor) / (VigorMax - VigorFloor)));

    // 旗人皆兵：征兵时城里的旗人多算这么多份进兵源
    public const float ExtraLevy = 1f;

    // 入关前旗兵自备鞍马、以旗地为生，只领行粮：军饷按这一成数算
    public const float PreEntryUpkeep = 0.3f;
    // 入关后旗兵钱粮逐年定制：EntryPayYears 年里从行粮涨到全饷(用户 2026-11-09 清一入关就按全饷养兵，军费系数一年内从 1 掉到 0.01，
    // 欠饷到投朝鲜)
    public const float EntryPayYears = 20f;

    public static float EntryPay(float yearsSinceEntry) =>
        yearsSinceEntry < 0f ? PreEntryUpkeep
            : PreEntryUpkeep + (1f - PreEntryUpkeep) * Math.Min(1f, yearsSinceEntry / EntryPayYears);

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

    // 八旗战力：承平日久渐渐不能打，打仗才磨得回来，老兵值封顶跟着它走；闲散多了败得更快
    public static float NextVigor(float vigor, bool atWar, float idle = 0f)
    {
        float next = vigor + (atWar ? VigorWarGain : -VigorPeaceDecay * (1f + Math.Max(0f, idle) * IdleVigorDrag));
        return Math.Min(VigorMax, Math.Max(VigorFloor, next));
    }

    // 八旗生计(见 BannerSystem.Livelihood)：旗缺入关时按旗人数定，此后每年只添千分之三；旗人生齿日繁，多出来的是闲散，
    // 不领饷、添怨、拖累战力、典卖旗地；闲散过三成汉军出旗
    public const float QuotaGrowth = 0.003f, IdleGrievance = 8f, IdleVigorDrag = 1f, IdleLandLoss = 0.03f, IdleAnnexation = 3f, ReleaseIdle = 0.3f;

    public static float NextQuota(float quota, float bannermen, float years) =>
        quota <= 0f ? Math.Max(0f, bannermen) : quota * (1f + QuotaGrowth * Math.Max(0f, years));

    public static float IdleShare(float bannermen, float quota) =>
        bannermen <= 0f || quota <= 0f ? 0f : Math.Min(1f, Math.Max(0f, (bannermen - quota) / bannermen));

    // 旗饷按缺发：闲散不领
    public static int StipendDue(float bannermen, float quota) =>
        StipendDue(quota > 0f ? Math.Min(bannermen, quota) : bannermen);

    // 典卖旗地：一年流失几成
    public static float NextLandAfterSale(float land, float idle) => Math.Max(0f, land - Math.Max(0f, idle) * IdleLandLoss);

    public static bool ReleaseDue(float idle, bool released) => !released && idle >= ReleaseIdle;

    // 团练成勇营(见 BannerSystem.Livelihood)：民变未平、八旗战力不足五十；五年一处，至多四处；天命跌破三十，办过勇营的督抚拥兵自重
    public const float MilitiaVigor = 50f;
    public const int MilitiaIntervalYears = 5, MilitiaMax = 4, AutonomyMandate = 30;

    public static bool MilitiaDue(int rebellions, float vigor, float yearsSinceLast, int raised) =>
        rebellions > 0 && vigor < MilitiaVigor && raised < MilitiaMax && (yearsSinceLast < 0f || yearsSinceLast >= MilitiaIntervalYears);

    // 现在能有几座满城：城越多驻防越多，有个上限
    public static int CityLimit(int cities) => Math.Min(CityMax, 1 + Math.Max(0, cities) / CitiesPerGarrison);

    // 入关三政的时间表(入关后第几年，见 BannerSystem.Conquest)：三次圈地(1644、1645、1647 起，剧本事件圈过的算数)，
    // 次年剃发易服、颁逃人法；二十五年永停圈地、退还一半旗地(康熙八年)，四十年天下无事放宽逃人法
    public static readonly int[] EnclosureYears = { 1, 3, 5 };
    public const int QueueYear = 1, LawYear = 2, StopEnclosureYear = 25, RelaxLawYear = 40, EnclosureCities = 4;
    public const float QueueStabilityMax = 6f, QueueFadeYears = 15f, StopReturnShare = 0.5f;

    public static bool EncloseDue(int done, float years, bool stopped) =>
        !stopped && done >= 0 && done < EnclosureYears.Length && years >= EnclosureYears[done];

    public static bool QueueDue(float years, bool issued) => !issued && years >= QueueYear;

    public static bool LawDue(float years, bool lawOn, bool relaxed) => !lawOn && !relaxed && years >= LawYear;

    public static bool StopDue(float years, bool stopped) => !stopped && years >= StopEnclosureYear;

    public static bool RelaxDue(float years, bool lawOn, bool relaxed, bool atWar) => lawOn && !relaxed && !atWar && years >= RelaxLawYear;

    // 剃发易服以后属地之城的不服：当年扣满，十五年里渐渐淡去
    public static float QueueStability(float years) =>
        years < 0f ? 0f : QueueStabilityMax * Math.Max(0f, 1f - years / QueueFadeYears);
}

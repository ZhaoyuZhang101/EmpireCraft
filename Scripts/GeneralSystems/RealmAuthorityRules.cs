using System;

namespace EmpireCraft.Scripts.GeneralSystems;

// 君威的纯算式(只依赖 System，便于单测)。独立王国/部落没有正统，推制度改革时拿君威比节点的 mandate_required：
//   基数 55 + 君主资质(中主 +10，见 RulerQualitySystem；无君 −20) + 国库(有余 +5、亏空 −5)
//   + 腐败(0~−15) + 苛政(负担过二十后每四点 −1) + 声望(君主声望每 50 点 +1，至多 +8)
//   + 天命(受着天下的天命 +8) − 叛乱(叛乱未平 −10)，夹在 0~100。
// 国库、腐败、苛政三项与帝国正统同一套折算(见 MonarchyLegitimacyRules)。
public static class RealmAuthorityRules
{
    public const int Base = 55;
    public const int Interregnum = -20;
    public const int RenownPerPoint = 50;
    public const int PrestigeCap = 8;
    public const int MandateHolder = 8;
    public const int Rebellion = -10;

    // 王国没有破产年数的记录，亏空按一年算(−5)
    public static int Treasury(long money) =>
        MonarchyLegitimacyRules.Treasury(money < 0 ? -1 : money > 0 ? 1 : 0, 0);

    public static int Corruption(double rate) => MonarchyLegitimacyRules.Corruption(rate);

    public static int Misrule(float burden) => MonarchyLegitimacyRules.Misrule(burden);

    public static int Prestige(int renown) => Math.Max(0, Math.Min(PrestigeCap, renown / RenownPerPoint));

    public static int Clamp(int value) => Math.Max(0, Math.Min(100, value));

    // ruler：君主资质那一项(无君时传 Interregnum)
    public static int Total(int ruler, long money, double corruptionRate, float harshBurden, int renown,
        bool mandateHolder, bool inRebellion) =>
        Clamp(Base + ruler + Treasury(money) + Corruption(corruptionRate) + Misrule(harshBurden) + Prestige(renown) +
              (mandateHolder ? MandateHolder : 0) + (inRebellion ? Rebellion : 0));
}

using System;

namespace EmpireCraft.Scripts.GeneralSystems;

// 治理状况的有界贡献，不按亏空年数或反复 AI 调用无限累扣。
internal static class MonarchyLegitimacyRules
{
    public const int Base = 55;
    public static int Dynasty(int eventScore) => (int)Math.Round((Math.Max(0, Math.Min(100, eventScore)) - 50) * 0.2,
        MidpointRounding.AwayFromZero);
    public static int Treasury(int money, int debtYears) => money < 0
        ? -(5 + Math.Min(5, Math.Max(0, debtYears)) * 2) : money > 0 ? 5 : 0;
    public static int Misrule(float burden) => -(int)Math.Round(Math.Max(0f, Math.Min(100f, burden) - 20f) * 0.25f,
        MidpointRounding.AwayFromZero);
    public static int Corruption(double rate) => -(int)Math.Round(Math.Max(0d, Math.Min(1d, rate)) * 15d,
        MidpointRounding.AwayFromZero);
}

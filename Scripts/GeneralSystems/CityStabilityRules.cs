using System;

namespace EmpireCraft.Scripts.GeneralSystems;

public static class CityStabilityRules
{
    public const float RebellionThreshold = 40f;
    public const int WithdrawalMonths = 24;
    public static float Clamp(float value, float low = 0f, float high = 100f) => Math.Max(low, Math.Min(high, value));
    public static float Natural(int loyalty, int legitimacy, float corruption, float sentiment,
        float landless, bool famine, float grievance) => Clamp(55f + Clamp(loyalty, -100f, 100f) * 0.3f +
        (legitimacy < 0 ? 0f : (Clamp(legitimacy) - 50f) * 0.15f) - Clamp(corruption, 0f, 1f) * 10f -
        Clamp(sentiment) * 0.12f - Clamp(landless, 0f, 1f) * 15f - (famine ? 25f : 0f) - Clamp(grievance) * 0.18f);

    public static int RequiredGarrison(int population, float natural, float policy) => population <= 0 || policy <= 0f
        ? 0 : (int)Math.Min(population, Math.Ceiling(population * (0.002d + (100d - Clamp(natural)) * 0.00018d) * policy));
    public static double GovernanceAnnual(int households, float natural, float corruption, float policy) =>
        policy <= 0f || households <= 0 ? 0d : Math.Max(1d, households * 0.12d) *
        (1d + (100d - Clamp(natural)) / 100d) * (1d + Clamp(corruption, 0f, 1f) * 0.5d) * policy;
    public static double GarrisonAnnual(int people, float natural) => Math.Max(0, people) * 0.3d *
        (1d + (100d - Clamp(natural)) / 100d * 1.5d);
    public static double MilitaryAnnual(int people, bool atWar) => Math.Max(0, people) * (atWar ? 0.45d : 0.3d);
    // 野战军团的军饷(按人头)：平时轮番操练每兵每年 0.1，战时 0.3
    public static double CorpsAnnual(float soldiers, bool atWar) => Math.Max(0f, soldiers) * (atWar ? 0.3d : 0.1d);

    // 小额月费积累尾数，十二个月的实际扣款与年报价相符，不强制每城每月至少一金币。
    public static int Invoice(double annual, ref double carry)
    {
        double amount = Math.Max(0d, annual) / 12d + Math.Max(0d, Math.Min(1d, carry));
        int bill = (int)Math.Min(int.MaxValue, Math.Floor(amount + 0.0000001d));
        carry = Math.Max(0d, amount - bill);
        return bill;
    }

    // 每月朝自然稳定度走至多 2 点；months = 这回补算的月数(见 CityStabilitySystem.UpdatePrepared)
    public static float Advance(float stability, float natural, float governanceQuality, int months = 1) =>
        Clamp(stability + Clamp(Clamp(natural + Clamp(governanceQuality) * 0.12f) - stability, -2f * months, 2f * months));
    public static float Suppression(int actual, int required, float funding) => required <= 0 ? 0f :
        20f * Clamp(actual / (float)required, 0f, 1f) * Clamp(funding, 0f, 1f);
    public static bool ShouldWithdraw(int months, float stability, float change, bool annualAccounts,
        long operatingBalance, int cash, double monthlyCost) => months >= WithdrawalMonths &&
        stability < RebellionThreshold && change <= 0f && annualAccounts && operatingBalance <= 0 &&
        cash < Math.Max(1d, monthlyCost * 6d);
}

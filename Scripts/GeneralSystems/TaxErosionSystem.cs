using System;
using EmpireCraft.Scripts.Layer;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 税基流失(王朝的财政周期)：开国时吏治清明、户口田亩清楚，税收得上来；积弊一深，税就越收越少——
//   · 隐田逃户：土地兼并越重，豪强隐匿田亩、百姓投献依附，土地税至多少收四成五、居民税至多少收两成五；
//     苛捐杂税越重，百姓逃亡越多，居民税再至多少收一成五；
//   · 胥吏舞弊：吏治越坏，各项税都有人少报瞒报，至多少收两成；
//   · 地方截留：吏治越坏，各省把该解送中央的那份截下，至多截三成，留在省里；
//   (征收之后各城的贪墨照旧按城市腐败率扣，见 TreasurySystem.CollectResidentTax)
//   合计流失至多七成。只算税(居民、土地、工业、商业)，官营产业利润不算。
// 于是王朝中后期收入萎缩、冗费又涨，国库由盈转亏→举债、违约(见 EmpireBankruptcySystem)→加派苛捐→民变，
// 直到整顿吏治、改朝换代把积弊清掉(见 MaladySystem)，财政才重新好转。
// 帝国财政页"税赋征收"卡列出上一年应征、实收、征收率与各环节的流失(EmpireData.fiscal_*，每年结转)。
public static class TaxErosionSystem
{
    private const float HiddenLand = 0.45f, FleeingHouseholds = 0.25f, LeviesFlight = 0.15f, ClerkFraud = 0.2f;
    private const float Withhold = 0.3f, MaxLoss = 0.7f;

    public static bool IsTax(TreasuryCategory category) =>
        category is TreasuryCategory.ResidentTax or TreasuryCategory.LandTax or TreasuryCategory.IndustrialTax or
            TreasuryCategory.CommercialTax;

    // 这一笔税在征收前流失的比例：隐田逃户、胥吏舞弊
    public static (float hidden, float fraud) Evasion(Empire empire, TreasuryCategory category)
    {
        if (empire?.data == null || !IsTax(category) || !MaladySystem.Applies(empire)) return (0f, 0f);
        float annexation = MaladySystem.Severity(empire, MaladySystem.Annexation);
        float levies = MaladySystem.Severity(empire, MaladySystem.Levies);
        float graft = MaladySystem.Severity(empire, MaladySystem.Graft);
        float hidden = category switch
        {
            TreasuryCategory.LandTax => HiddenLand * annexation,
            TreasuryCategory.ResidentTax => FleeingHouseholds * annexation + LeviesFlight * levies,
            _ => 0f
        };
        float fraud = ClerkFraud * graft;
        float total = hidden + fraud;
        if (total > MaxLoss)
        {
            hidden *= MaxLoss / total;
            fraud *= MaxLoss / total;
        }
        return (hidden, fraud);
    }

    // 各省截留中央那份的比例
    public static float Withholding(Empire empire) =>
        empire?.data == null || !MaladySystem.Applies(empire) ? 0f : Withhold * MaladySystem.Severity(empire, MaladySystem.Graft);

    // 随机舍入(小额税款按期望值流失，不会因取整一直是 0)
    public static int Round(double value) =>
        value <= 0d ? 0 : (int)Math.Min(int.MaxValue, Math.Floor(value + UnityEngine.Random.value));

    #region 账目

    public static void RecordAssessed(Empire empire, long assessed, long hidden, long fraud)
    {
        if (empire?.data == null) return;
        Roll(empire);
        empire.data.fiscal_assessed += assessed;
        empire.data.fiscal_hidden += hidden;
        empire.data.fiscal_fraud += fraud;
    }

    public static void RecordStolen(Empire empire, long stolen)
    {
        if (empire?.data == null || stolen <= 0) return;
        Roll(empire);
        empire.data.fiscal_stolen += stolen;
    }

    public static void RecordWithheld(Empire empire, long withheld)
    {
        if (empire?.data == null || withheld <= 0) return;
        Roll(empire);
        empire.data.fiscal_withheld += withheld;
    }

    // 满一年把本年账目结转为"上一年"，重新记
    private static void Roll(Empire empire)
    {
        if (World.world == null) return;
        double now = World.world.getCurWorldTime();
        EmpireData data = empire.data;
        if (data.fiscal_year_start < 0d || now < data.fiscal_year_start)
        {
            data.fiscal_year_start = now;
            return;
        }
        if (Date.getYearsSince(data.fiscal_year_start) < 1) return;
        data.fiscal_last_assessed = data.fiscal_assessed;
        data.fiscal_last_hidden = data.fiscal_hidden;
        data.fiscal_last_fraud = data.fiscal_fraud;
        data.fiscal_last_stolen = data.fiscal_stolen;
        data.fiscal_last_withheld = data.fiscal_withheld;
        data.fiscal_assessed = data.fiscal_hidden = data.fiscal_fraud = data.fiscal_stolen = data.fiscal_withheld = 0L;
        data.fiscal_year_start = now;
    }

    // 上一年(没有就用本年至今)的账：应征、隐田逃户、胥吏舞弊、贪墨、截留
    public static (long assessed, long hidden, long fraud, long stolen, long withheld) Ledger(Empire empire)
    {
        if (empire?.data == null) return default;
        EmpireData data = empire.data;
        return data.fiscal_last_assessed > 0
            ? (data.fiscal_last_assessed, data.fiscal_last_hidden, data.fiscal_last_fraud, data.fiscal_last_stolen, data.fiscal_last_withheld)
            : (data.fiscal_assessed, data.fiscal_hidden, data.fiscal_fraud, data.fiscal_stolen, data.fiscal_withheld);
    }

    #endregion
}

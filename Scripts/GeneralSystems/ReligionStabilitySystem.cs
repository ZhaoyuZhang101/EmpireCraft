using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;

namespace EmpireCraft.Scripts.GeneralSystems;

// 宗教与稳定(用户 2026-10-17 选"宗教影响稳定")：
//   · 国教：帝国确立的国教(见 TempFac_确立国教)，没有的看帝国中央、独立国家看本国的信仰；
//   · 异教之城(城的信仰与国教不同)：定国策的那一国(帝国看中央)限制信仰时稳定度 -6，政教合一 -10；
//     无国教或信仰自由(律令默认，游牧施行宗教宽容后)不扣——宽容还是不宽容，各有取舍；
//   · 人头税(制度特性 head_tax，阿拉伯人丁税)：异教之城多纳两成税，换来一半的宽容(稳定度只扣一半)；
//   · 异教之城起事打"宗教叛乱"、异族之城打"民族叛乱"(以前这两种从没出现过)，同信仰、同文化的邻城可以加入。
public static class ReligionStabilitySystem
{
    public const string HeadTaxFeature = "head_tax";
    private const float RestrictedLoss = 6f, TheocracyLoss = 10f, HeadTaxRelief = 0.5f, HeadTaxRate = 1.2f;

    public static Religion StateReligion(Kingdom kingdom)
    {
        if (kingdom?.data == null) return null;
        Empire empire = kingdom.GetEmpire();
        if (empire?.Religion != null) return empire.Religion;
        return (empire?.CoreKingdom ?? kingdom).religion;
    }

    // 异教之城：城的信仰与国教不同
    public static bool Heterodox(City city)
    {
        if (city?.data == null || city.isRekt() || city.kingdom == null || city.kingdom.wild) return false;
        Religion faith = city.religion, state = StateReligion(city.kingdom);
        return faith != null && state != null && faith != state;
    }

    private static bool HeadTax(Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        return empire != null
            ? InstitutionSystem.HasFeature(empire, HeadTaxFeature)
            : InstitutionSystem.HasFeature(CultureService.GetRealmCulture(kingdom), HeadTaxFeature);
    }

    // 异教之城的稳定度损失(见 CityStabilitySystem)
    public static float StabilityLoss(City city)
    {
        if (!Heterodox(city)) return 0f;
        Kingdom ruler = city.kingdom.GetEmpire()?.CoreKingdom ?? city.kingdom;
        ReligionLevel? level = ruler.GetRegime()?.GetReligionLevel();
        float loss = level == ReligionLevel.High ? TheocracyLoss : level == ReligionLevel.Medium ? RestrictedLoss : 0f;
        return HeadTax(city.kingdom) ? loss * HeadTaxRelief : loss;
    }

    // 人头税：异教之城的税率倍数(见 PopulationEconomySystem.PayTaxes)
    public static float TaxFactor(City city) => Heterodox(city) && HeadTax(city.kingdom) ? HeadTaxRate : 1f;
}

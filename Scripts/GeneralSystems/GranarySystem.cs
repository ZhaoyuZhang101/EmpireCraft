using System.Collections.Generic;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 国家粮仓(无小人模式)：按粮食征收政策(宪法条款，见 ConstitutionGrain；没有宪法的国家按"征收粮赋")，
// 各城收割的粮食一部分交国家粮仓——留归地方 0、征收粮赋三成、统购统销七成。
// 粮仓按帝国(没有帝国就按王国)存放，每年损耗 SpoilagePerYear；饥荒时开仓赈灾，向缺粮的城市调粮，每年记一次赈灾。
// 粮食单位与城市仓库相同(户数尺度)。
public static class GranarySystem
{
    private const float SpoilagePerYear = 0.05f;
    private static readonly Dictionary<Kingdom, (int cities, float amount)> ReliefThisYear = new();

    public static Kingdom Realm(Kingdom kingdom) => kingdom?.GetEmpire()?.CoreKingdom ?? kingdom;

    // 推行郡县制(各文明的中央集权官僚制度，制度特性 state_granary)之后才有国家粮仓；现代国家都有。
    // 之前的分封时代收成全留地方，也没有国家赈灾
    public const string FeatureStateGranary = "state_granary";

    public static bool Enabled(Kingdom kingdom)
    {
        Kingdom realm = Realm(kingdom);
        if (realm == null || realm.isRekt() || realm.wild) return false;
        if (ModernStability.IsModern(realm)) return true;
        string culture = CultureService.GetRealmCulture(realm);
        return CultureService.IsValidCulture(culture) && InstitutionSystem.HasFeature(culture, FeatureStateGranary);
    }

    public static ConstitutionGrain Policy(Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        return empire == null ? ConstitutionGrain.Tribute
            : ConstitutionSystem.GetClauses(empire)?.grain ?? ConstitutionGrain.Tribute;
    }

    public static float LevyShare(ConstitutionGrain policy) => policy switch
    {
        ConstitutionGrain.Local => 0f,
        ConstitutionGrain.Central => 0.7f,
        _ => 0.3f
    };

    public static float Stock(Kingdom kingdom)
    {
        Kingdom realm = Realm(kingdom);
        return realm == null || realm.isRekt() ? 0f : realm.GetOrCreate().granary;
    }

    // 收成：按政策交国家粮仓的部分存进粮仓，返回留给本城的部分
    public static float Collect(City city, float harvest)
    {
        Kingdom realm = Realm(city?.kingdom);
        if (harvest <= 0f || !Enabled(city?.kingdom)) return harvest;
        float levied = harvest * LevyShare(Policy(city.kingdom));
        if (levied <= 0f) return harvest;
        Spoil(realm);
        realm.GetOrCreate().granary += levied;
        return harvest - levied;
    }

    // 赈灾：缺粮的城市从国家粮仓调粮，返回调到的数量
    public static float Relieve(City city, float shortage)
    {
        Kingdom realm = Realm(city?.kingdom);
        if (shortage <= 0f || !Enabled(city?.kingdom)) return 0f;
        Spoil(realm);
        KingdomExtension.KingdomExtraData data = realm.GetOrCreate();
        float relief = Mathf.Min(shortage, data.granary);
        if (relief < 1f) return 0f;
        data.granary -= relief;
        ReliefThisYear.TryGetValue(realm, out var total);
        ReliefThisYear[realm] = (total.cities + 1, total.amount + relief);
        return relief;
    }

    // 年度损耗；顺便把上一年的赈灾写进史书
    private static void Spoil(Kingdom realm)
    {
        KingdomExtension.KingdomExtraData data = realm.GetOrCreate();
        double now = World.world?.getCurWorldTime() ?? 0d;
        if (data.granary_last_spoil < 0d || now < data.granary_last_spoil)
        {
            data.granary_last_spoil = now;
            return;
        }
        int years = Date.getYearsSince(data.granary_last_spoil);
        if (years < 1) return;
        data.granary_last_spoil = now;
        data.granary *= Mathf.Pow(1f - SpoilagePerYear, Mathf.Min(years, 10));
        if (!ReliefThisYear.TryGetValue(realm, out var relief) || relief.cities == 0) return;
        ReliefThisYear.Remove(realm);
        string text = string.Format(LM.Get("granary_relief_history"), realm.GetKingdomName(), relief.cities,
            Mathf.RoundToInt(relief.amount));
        Empire empire = realm.GetEmpire();
        if (empire != null) EventRecorder.Record(empire, text);
        else EventRecorder.Record(realm, text);
    }

    public static void ResetWorldState() => ReliefThisYear.Clear();
}

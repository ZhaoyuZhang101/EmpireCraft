using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 华夏民间教门起事(用户 2026-10-26 选"华夏民间教门起事"：黄巾、白莲一类)：
//   · 华夏一线(制度线 Huaxia)的国家，王朝末世(DynasticCycleSystem.InCrisis)、饥荒(FamineUnrestSystem.InFamine)、
//     苛政(负担 ≥ HarshThreshold)有其一时，稳定低于起事线的城(不是都城、十年内没起过事，见 ReligiousTension.CanRevolt)
//     每年 Chance 机会由秘密教门举事；每个政权(帝国按中央)每年至多一处；打仗时也会；
//   · 教门不是宗教对象，不能立为国教：新国照旧奉本城之教，国号、战名、史书写教门名(太平道、白莲教……，见 sect_name_*)。
public static class SectUprisingSystem
{
    private const float Chance = 0.03f;
    private const float HarshThreshold = 40f;
    private const int SectNames = 8;

    private static Kingdom Realm(Kingdom kingdom) => kingdom?.GetEmpire()?.CoreKingdom ?? kingdom;

    public static void YearlyPass()
    {
        if (World.world?.cities == null || ModClass.IS_CLEAR) return;
        var risen = new HashSet<Kingdom>();
        foreach (City city in World.world.cities.list.ToList())
        {
            Kingdom kingdom = city?.kingdom;
            if (city?.data == null || city.isRekt() || kingdom?.data == null || kingdom.wild ||
                EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(city)) continue;
            Kingdom realm = Realm(kingdom);
            if (risen.Contains(realm) || InstitutionSystem.GetCultureLine(CultureService.GetRealmCulture(realm)) != "Huaxia" ||
                !Distressed(city, kingdom) || !ReligiousTension.CanRevolt(city) || UnityEngine.Random.value >= Chance) continue;
            string sect = LM.Get("sect_name_" + UnityEngine.Random.Range(1, SectNames + 1));
            string cityName = city.GetCityName();
            if (!ReligiousTension.StartRevolt(city, "rebellion_reason_sect", sect,
                    string.Format(LM.Get("sect_revolt_kingdom_name"), cityName, sect),
                    string.Format(LM.Get("sect_revolt_war_name"), cityName, sect),
                    string.Format(LM.Get("sect_revolt_history"), cityName, sect))) continue;
            risen.Add(realm);
            LogService.LogInfo($"[EmpireCraft][宗教] {realm.GetKingdomName()} {cityName} {sect}起事");
        }
    }

    // 宗教图层提示用：华夏一线、末世饥荒苛政有其一、稳定低于起事线(教门可能趁乱起事)
    public static bool AtRisk(City city)
    {
        Kingdom kingdom = city?.kingdom;
        if (city?.data == null || city.isRekt() || kingdom?.data == null || kingdom.wild) return false;
        return InstitutionSystem.GetCultureLine(CultureService.GetRealmCulture(Realm(kingdom))) == "Huaxia" && Distressed(city, kingdom) &&
               CityStabilitySystem.Effective(city) < CityStabilityRules.RebellionThreshold;
    }

    // 末世、饥荒、苛政有其一
    private static bool Distressed(City city, Kingdom kingdom)
    {
        Empire empire = kingdom.GetEmpire();
        if (empire != null && DynasticCycleSystem.InCrisis(empire)) return true;
        if (FamineUnrestSystem.InFamine(city)) return true;
        float burden = empire != null ? HarshRuleSystem.GetBurden(empire) : HarshRuleSystem.GetBurden(kingdom);
        return burden >= HarshThreshold;
    }
}

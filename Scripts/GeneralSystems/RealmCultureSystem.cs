using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 一键易俗(用户 2026-10-19："一键变更对应文化的权能，从统治者到制度科技等全部变更"，问答选"两套都要")：
//   举国：一个国家的全部城(百姓、人物、地名)、名下法理、国家文化与政体、君主宗室与在任官员(本非同一文化圈的整支改姓)、
//         制度、科技(按新文化重新登记采纳)一并改从这个文化；是帝国核心的连帝国的文化(法理默认文化、统治文化)一起改；
//   举朝：整个帝国(核心与各成员国)都这样改，帝国法理的文化也改；征服王朝的双重身份就此作罢。不在帝国里的同举国。
// 法理的初始文化不动(可以用"复旧法理"恢复，见 CultureService.RestoreInitialTitleCulture)。
public static class RealmCultureSystem
{
    public static (int kingdoms, int cities) Convert(Kingdom kingdom, string culture, bool wholeEmpire)
    {
        if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild || !CultureService.IsValidCulture(culture)) return (0, 0);
        Empire empire = kingdom.GetEmpire();
        if (empire != null && (empire.isRekt() || empire.IsArchived())) empire = null;
        bool touchesCore = empire != null && (wholeEmpire || empire.CoreKingdom == kingdom);
        var scope = new List<Kingdom>();
        if (wholeEmpire && empire?.kingdoms_list != null)
        {
            if (empire.CoreKingdom != null) scope.Add(empire.CoreKingdom);
            scope.AddRange(empire.kingdoms_list.Where(member => member?.data != null && !member.isRekt() && member != empire.CoreKingdom));
        }
        else scope.Add(kingdom);

        if (touchesCore) PrepareEmpire(empire, culture);
        int cities = 0;
        foreach (Kingdom member in scope)
        {
            try
            {
                cities += ConvertKingdom(member, culture);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][一键易俗] {member.GetKingdomName()} 改文化失败: {exception.Message}");
            }
        }
        if (touchesCore) FinishEmpire(empire, culture, wholeEmpire);
        EmpireCraft.Scripts.GameLibrary.EmpireCraftNamePlateLibrary.RequestRefresh();
        LogService.LogInfo($"[EmpireCraft][一键易俗] {kingdom.GetKingdomName()} → {culture}，{scope.Count} 国 {cities} 城");
        return (scope.Count, cities);
    }

    private static int ConvertKingdom(Kingdom kingdom, string culture)
    {
        // 先改国家文化、政体、君主与旧俗官员(见 CultureReformSystem.Force)，国家本已是这个文化的、别的文化出身的官员另行改过来，
        // 再改各城——以前先改城，城里的君主、官员已随百姓改了文化，改姓、宗族记的文化都判成"不用改"，异族出身的整支不改姓
        CultureReformSystem.Force(kingdom, culture);
        CultureReformSystem.ConvertRulers(kingdom, culture);
        int cities = 0;
        foreach (City city in (kingdom.cities ?? new List<City>()).ToList())
        {
            if (city?.data == null || city.isRekt()) continue;
            CultureService.ForceCityCulture(city, culture);
            cities++;
        }
        foreach (KingdomTitle title in kingdom.GetControlledTitles().ToList())
            CultureService.ConvertTitleCulture(title, culture);
        // 一键易俗连制度一并改，不留旧制(见 LegacyInstitutionSystem)
        CultureService.SetRealmCulture(kingdom, culture, updatePoliticalSystem: kingdom.GetEmpire()?.CoreKingdom != kingdom,
            allowLegacyInstitutions: false);
        // 科技按新文化重新登记采纳(见 TechnologySystem.GetRealmState)
        kingdom.GetOrCreate().realm_tech = null;
        return cities;
    }

    // 征服王朝的双重身份作罢；帝国的统治文化、开国族群改成这个文化
    private static void PrepareEmpire(Empire empire, string culture)
    {
        if (CompositeEmpireService.IsComposite(empire)) CompositeEmpireService.Dissolve(empire);
        empire.data.conquest_founder_culture = culture;
        empire.data.ruling_culture = culture;
    }

    private static void FinishEmpire(Empire empire, string culture, bool wholeEmpire)
    {
        EmpireCore core = EmpireCoreManager.Get(empire);
        if (core != null)
        {
            core.default_culture = culture;
            if (wholeEmpire)
                foreach (KingdomTitle title in EmpireCoreManager.GetTitles(core).ToList())
                    CultureService.ConvertTitleCulture(title, culture);
        }
        empire.data.empire_type_key = "";
        empire.data.realm_tech = null;
        InstitutionSystem.ForgetRulingMajority(empire);
        InstitutionSystem.ResyncCulture(empire);
    }
}

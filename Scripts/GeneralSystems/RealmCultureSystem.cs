using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 一键易俗(用户 2026-10-19："一键变更对应文化的权能，从统治者到制度科技等全部变更"，问答选"两套都要")：
//   举国：一个国家的全部城(百姓、人物、地名)、名下法理、国家文化与政体、君主宗室与在任官员(本非同一文化圈的整支改姓)、
//         制度、科技(按新文化重新登记采纳)一并改从这个文化；是帝国核心的连帝国的文化(法理默认文化、统治文化)一起改；
//   举朝：整个帝国(核心与各成员国)都这样改，帝国法理的文化也改；征服王朝的双重身份就此作罢。不在帝国里的同举国。
// 法理的初始文化不动(可以用"复旧法理"恢复，见 CultureService.RestoreInitialTitleCulture)。
// 整体转化(文化窗口)：一个文化整个改从另一个文化，见 ConvertCulture。
public static class RealmCultureSystem
{
    // onlyFrom：整体转化时只改主流文化是它的城、文化是它的法理(国家本身照改)；空 = 全改
    public static (int kingdoms, int cities) Convert(Kingdom kingdom, string culture, bool wholeEmpire, string onlyFrom = null)
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
                cities += ConvertKingdom(member, culture, onlyFrom, scope);
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

    // scope：这次易俗的各国(举朝为整个帝国)；宗族跨国的只改这些国家里的族人(见 CultureReformSystem 类头)
    private static int ConvertKingdom(Kingdom kingdom, string culture, string onlyFrom, IEnumerable<Kingdom> scope)
    {
        // 先改国家文化、政体、君主与旧俗官员(见 CultureReformSystem.Force)，国家本已是这个文化的、别的文化出身的官员另行改过来，
        // 再改各城——以前先改城，城里的君主、官员已随百姓改了文化，改姓、宗族记的文化都判成"不用改"，异族出身的整支不改姓
        CultureReformSystem.Force(kingdom, culture);
        CultureReformSystem.ConvertRulers(kingdom, culture, scope);
        int cities = 0;
        foreach (City city in (kingdom.cities ?? new List<City>()).ToList())
        {
            if (city?.data == null || city.isRekt()) continue;
            if (onlyFrom != null && !string.Equals(CultureService.GetMainCulture(city, initialize: false), onlyFrom,
                    StringComparison.Ordinal)) continue;
            CultureService.ForceCityCulture(city, culture);
            cities++;
        }
        foreach (KingdomTitle title in kingdom.GetControlledTitles().ToList())
            if (onlyFrom == null || string.Equals(CultureService.GetEffectiveTitleCulture(title), onlyFrom, StringComparison.Ordinal))
                CultureService.ConvertTitleCulture(title, culture);
        // 一键易俗连制度一并改，不留旧制(见 LegacyInstitutionSystem)
        CultureService.SetRealmCulture(kingdom, culture, updatePoliticalSystem: kingdom.GetEmpire()?.CoreKingdom != kingdom,
            allowLegacyInstitutions: false);
        // 科技按新文化重新登记采纳(见 TechnologySystem.GetRealmState)
        kingdom.GetOrCreate().realm_tech = null;
        return cities;
    }

    // 整体转化(用户 2026-10-20，文化窗口「整体转化」)：from 这个文化整个改从 to，逐国逐城当场改，不留旧制——
    //   · 国家文化是 from 的国家：国家文化、政体与制度、君主宗室与在任官员、名下 from 的法理与城一并改(同举国易俗)；
    //     是帝国核心的连帝国的文化一起改，成员国文化不是 from 的不动；
    //   · 主流文化是 from 的城，不论属于哪国：百姓、人物、地名改过来；别处城里 from 的少数(文化比例、背景人口)、
    //     散在各处的 from 人物也改成 to；
    //   · 帝国的默认文化、统治文化还是 from 的一并改；沿用 from 旧制的国家旧制作罢，照本国文化之制。
    // 改完 from 没人用了就算消亡(不再出现在本局文化里)。返回 (国数, 城数)
    public static (int kingdoms, int cities) ConvertCulture(string from, string to)
    {
        if (!CultureService.IsValidCulture(from) || !CultureService.IsValidCulture(to) ||
            string.Equals(from, to, StringComparison.Ordinal) || World.world == null) return (0, 0);
        int kingdoms = 0, cities = 0;
        // 先改国家(君主、官员按国家原来的文化判改姓)，再改各城——同 ConvertKingdom 的次序
        List<Kingdom> realms = World.world.kingdoms
            .Where(kingdom => kingdom?.data != null && !kingdom.isRekt() && !kingdom.wild &&
                              string.Equals(CultureService.GetRealmCulture(kingdom), from, StringComparison.Ordinal))
            .ToList();
        foreach (Kingdom kingdom in realms)
        {
            (int converted, int count) = Convert(kingdom, to, false, from);
            kingdoms += converted;
            cities += count;
        }
        foreach (Empire empire in (ModClass.EMPIRE_MANAGER ?? Enumerable.Empty<Empire>()).ToList())
        {
            if (empire?.data == null || empire.isRekt() || empire.IsArchived() || empire.CoreKingdom == null) continue;
            if (!string.Equals(CultureService.GetEmpireDefaultCulture(empire), from, StringComparison.Ordinal) &&
                !string.Equals(empire.data.ruling_culture, from, StringComparison.Ordinal)) continue;
            PrepareEmpire(empire, to);
            FinishEmpire(empire, to, false);
        }
        foreach (City city in World.world.cities.ToList())
        {
            if (city?.data == null || city.isRekt()) continue;
            if (string.Equals(CultureService.GetMainCulture(city, initialize: false), from, StringComparison.Ordinal))
            {
                CultureService.ForceCityCulture(city, to);
                cities++;
            }
            else ConvertMinority(city, from, to);
            // 城里记着的待转文化、占领带来的文化、可复的旧俗是 from 的，一并改记 to
            CityExtension.CityExtraData extra = city.GetOrCreate();
            if (string.Equals(extra.culture_shift_candidate, from, StringComparison.Ordinal))
            {
                extra.culture_shift_candidate = "";
                extra.culture_shift_candidate_since = -1d;
            }
            if (string.Equals(extra.occupation_pressure_culture, from, StringComparison.Ordinal)) extra.occupation_pressure_culture = "";
            if (string.Equals(extra.restoration_culture, from, StringComparison.Ordinal)) extra.restoration_culture = to;
        }
        // 散在各处(不在城里、或住在别的文化的城里)的人物
        Culture native = CultureService.GetNativeCultureObject(to);
        if (native != null)
            foreach (Actor actor in World.world.units.ToList())
                if (actor?.data != null && !actor.isRekt() && actor.isAlive() &&
                    string.Equals(CultureService.GetActorCulture(actor), from, StringComparison.Ordinal))
                    actor.setCulture(native);
        // 沿用 from 旧制的国家：旧制作罢，照本国文化之制(帝国核心由制度系统重算)
        foreach (Kingdom kingdom in World.world.kingdoms.ToList())
        {
            if (kingdom?.data == null || kingdom.isRekt() ||
                !string.Equals(LegacyInstitutionSystem.Stored(kingdom), from, StringComparison.Ordinal)) continue;
            Empire empire = kingdom.GetEmpire();
            bool core = empire?.data != null && !empire.isRekt() && !empire.IsArchived() && empire.CoreKingdom == kingdom;
            CultureService.SetRealmCulture(kingdom, CultureService.GetRealmCulture(kingdom), updatePoliticalSystem: !core,
                allowLegacyInstitutions: false);
            if (core) InstitutionSystem.ResyncCulture(empire);
        }
        EmpireCraft.Scripts.GameLibrary.EmpireCraftNamePlateLibrary.RequestRefresh();
        TranslateHelper.LogEventOnly(string.Format(LM.Get("culture_convert_all_log"), from.GetCultureTranslate(),
            to.GetCultureTranslate(), kingdoms, cities));
        LogService.LogInfo($"[EmpireCraft][整体转化] {from} -> {to}，{kingdoms} 国 {cities} 城");
        return (kingdoms, cities);
    }

    // 别的文化的城里 from 的少数：文化比例并入 to，背景人口改成 to
    private static void ConvertMinority(City city, string from, string to)
    {
        Dictionary<string, float> shares = CultureService.GetCityCultureShares(city, initialize: false);
        if (shares.TryGetValue(from, out float share))
        {
            shares.Remove(from);
            if (share > 0f) shares[to] = (shares.TryGetValue(to, out float current) ? current : 0f) + share;
        }
        CityPopulationData data = CityPopulationSystem.Get(city);
        if (data?.groups == null) return;
        foreach (PopGroup group in data.groups.Where(group => string.Equals(group.culture, from, StringComparison.Ordinal) &&
                                                              group.Background > 0f).ToList())
        {
            float moved = CityPopulationSystem.RemoveBackground(group, group.Background);
            if (moved > 0f) CityPopulationSystem.AddBackground(city, group.social_class, to, group.species, group.ideology, moved);
        }
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

using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 古代理念(三)：百家争鸣与定于一尊(用户 2026-10-16 定的第三期"界面打磨、百家争鸣与定于一尊等事件"，排在世家之后)。只在华夏：
//   · 某家学说头一次在本文化流传(科技门槛见 TechTree.json 的 ideology:<理念>)，史册记"某学兴于某城"(本朝最大的城)；
//     头一回结算(旧存档)只记下已有的几家，不补记；
//   · 百家争鸣：本文化同时流传三家以上学说、还没定于一尊时开启，各家学说传播快一半(见 SpreadFactor)；
//   · 定于一尊(华夏制度树，郡国并行之后、需律令)：施行后以本朝官员里信者最多的一家为正统(官员里没人信学说就看全国人口)，
//     史册记"定于一尊，独尊某学，罢黜百家"；此后正统传播快八成、别家学说慢四成，百家争鸣到此为止；本朝主正统的朋党占比 +8。
//   · 与天命挂钩(见 TianxiaSystem)：受天命的治世里学说朋党可请"独尊官学"改尊本家(TempFac_独尊官学，见 SetOrthodox)；
//     本朝所在的天下无主(乱世)时正统每年两成机会废弛，重开百家争鸣("百家复起")，乱世里不再自动定于一尊。
public static class AncientSchoolsSystem
{
    public const string OrthodoxyNode = "huaxia_orthodoxy";
    private const int ContendAt = 3, OrthodoxCliqueRatio = 8;
    private const float ContendFactor = 1.5f, OrthodoxFactor = 1.8f, SuppressedFactor = 0.6f;

    private static bool Huaxia(string culture)
    {
        if (!CultureService.IsValidCulture(culture)) return false;
        try
        {
            return InstitutionSystem.GetCultureLine(culture) == "Huaxia";
        }
        catch
        {
            return false;
        }
    }

    private static CultureInstitutionState State(string culture) =>
        Huaxia(culture) ? InstitutionSystem.GetOrCreateCultureState(culture) : null;

    public static PartyIdeology? Orthodox(string culture)
    {
        CultureInstitutionState state = State(culture);
        return state != null && Enum.TryParse(state.orthodox_school, out PartyIdeology school) && IdeologyFamilies.IsSchool(school)
            ? school : null;
    }

    public static bool Contending(string culture)
    {
        CultureInstitutionState state = State(culture);
        return state != null && state.hundred_schools_since >= 0d && string.IsNullOrEmpty(state.orthodox_school);
    }

    // 传播快慢(乘在改信机会上，见 IdeologyPopulationSystem.ContactCity 与 IdeologyParallelContact)。
    // 每人每次交往都要问，按文化每帧缓存一次
    private static readonly Dictionary<string, (int frame, PartyIdeology? orthodox, bool contending)> Cache = new();

    public static float SpreadFactor(string culture, PartyIdeology target)
    {
        if (!IdeologyFamilies.IsSchool(target) || string.IsNullOrEmpty(culture)) return 1f;
        int frame = Time.frameCount;
        if (!Cache.TryGetValue(culture, out var cached) || cached.frame != frame)
        {
            cached = (frame, Orthodox(culture), Contending(culture));
            Cache[culture] = cached;
        }
        if (cached.orthodox.HasValue) return target == cached.orthodox.Value ? OrthodoxFactor : SuppressedFactor;
        return cached.contending ? ContendFactor : 1f;
    }

    // 这一家学说要哪些科技(理念图鉴用)
    public static List<string> Requirements(PartyIdeology ideology)
    {
        string feature = IdeologySpreadSystem.FeatureKey(ideology);
        return TechnologySystem.Config?.institution_links?.Where(link => link.feature == feature)
            .SelectMany(link => link.requires_techs).Distinct().Select(TechnologySystem.GetTechName).ToList() ?? new List<string>();
    }

    private static List<PartyIdeology> Available(string culture) =>
        Enum.GetValues(typeof(PartyIdeology)).Cast<PartyIdeology>()
            .Where(ideology => IdeologyFamilies.IsSchool(ideology) &&
                               TechnologySystem.AreFeatureTechsMet(culture, IdeologySpreadSystem.FeatureKey(ideology)))
            .ToList();

    // 每年(帝国例行检查，见 EmpireCraftEmpireBehCheckMandate)
    public static void Yearly(Empire empire)
    {
        if (empire?.data == null || empire.isRekt() || empire.IsArchived() || empire.CoreKingdom == null) return;
        string culture = InstitutionSystem.GetPrimaryCulture(empire);
        CultureInstitutionState state = State(culture);
        if (state == null) return;
        List<PartyIdeology> available = Available(culture);
        state.schools_seen ??= new List<string>();
        if (!state.schools_initialized)
        {
            state.schools_initialized = true;
            state.schools_seen.AddRange(available.Select(school => school.ToString()).Where(key => !state.schools_seen.Contains(key)));
        }
        City seat = empire.kingdoms_list.Where(kingdom => kingdom?.cities != null).SelectMany(kingdom => kingdom.cities)
            .Where(city => city?.data != null && !city.isRekt()).OrderByDescending(city => city.getPopulationPeople())
            .FirstOrDefault() ?? empire.CoreKingdom.capital;
        foreach (PartyIdeology school in available)
        {
            if (state.schools_seen.Contains(school.ToString())) continue;
            state.schools_seen.Add(school.ToString());
            Record(empire, string.Format(LM.Get("school_rise_history"), PartySystem.GetIdeologyName(school), seat?.GetCityName() ?? ""));
        }
        if (string.IsNullOrEmpty(state.orthodox_school) && state.hundred_schools_since < 0d && available.Count >= ContendAt)
        {
            state.hundred_schools_since = World.world.getCurWorldTime();
            Record(empire, LM.Get("hundred_schools_history"));
        }
        bool chaos = TianxiaSystem.PhaseOf(empire.CoreKingdom) == TianxiaSystem.Chaos;
        if (!string.IsNullOrEmpty(state.orthodox_school) && chaos && UnityEngine.Random.value < LapseChance)
        {
            string name = Enum.TryParse(state.orthodox_school, out PartyIdeology lapsed) ? PartySystem.GetIdeologyName(lapsed) : state.orthodox_school;
            state.orthodox_school = "";
            state.orthodox_since = -1d;
            state.hundred_schools_since = World.world.getCurWorldTime();
            Record(empire, string.Format(LM.Get("clique_school_lapsed_history"), name), log: true);
            return;
        }
        if (string.IsNullOrEmpty(state.orthodox_school) && !chaos && InstitutionSystem.IsEnacted(culture, OrthodoxyNode))
            Establish(empire, state, available);
    }

    private const float LapseChance = 0.2f;

    // 改尊一家为正统(独尊官学，见 TempFac_独尊官学)：主这一家的朋党占比 +8
    public static bool SetOrthodox(Empire empire, PartyIdeology school)
    {
        string culture = InstitutionSystem.GetPrimaryCulture(empire);
        CultureInstitutionState state = State(culture);
        if (state == null || !IdeologyFamilies.IsSchool(school) || state.orthodox_school == school.ToString()) return false;
        state.orthodox_school = school.ToString();
        state.orthodox_since = World.world.getCurWorldTime();
        state.hundred_schools_since = -1d;
        foreach (FixedFaction clique in empire.CoreKingdom.GetRegime()?.GetPlayerFactions()
                     ?.Where(faction => faction != null && faction.IsClique && !faction.Ban && faction.Ideology == school)
                     .ToList() ?? new List<FixedFaction>())
            empire.CoreKingdom.TryIncreaseFactionRatio(clique, OrthodoxCliqueRatio);
        Record(empire, string.Format(LM.Get("orthodoxy_history"), PartySystem.GetIdeologyName(school)), log: true);
        return true;
    }

    // 本朝定了于一尊的制度(独尊官学的前提)
    public static bool OrthodoxyEnacted(Empire empire)
    {
        string culture = InstitutionSystem.GetPrimaryCulture(empire);
        return State(culture) != null && InstitutionSystem.IsEnacted(culture, OrthodoxyNode);
    }

    // 定于一尊：本朝官员里信者最多的一家；官员里没人信学说就看全国人口
    private static void Establish(Empire empire, CultureInstitutionState state, List<PartyIdeology> available)
    {
        PartyIdeology? school = empire.getUnits()
            .Where(actor => actor?.data != null && !actor.isRekt() && actor.isAlive() && actor.IsOnOffice())
            .Select(IdeologyPopulationSystem.Get).Where(IdeologyFamilies.IsSchool)
            .GroupBy(ideology => ideology).OrderByDescending(group => group.Count()).Select(group => (PartyIdeology?)group.Key)
            .FirstOrDefault();
        school ??= IdeologyPopulationSystem.GetEmpireCounts(empire).Where(pair => IdeologyFamilies.IsSchool(pair.Key))
            .OrderByDescending(pair => pair.Value).Select(pair => (PartyIdeology?)pair.Key).FirstOrDefault();
        school ??= available.Contains(PartyIdeology.Confucianism) ? PartyIdeology.Confucianism : available.FirstOrDefault();
        if (!school.HasValue || !IdeologyFamilies.IsSchool(school.Value)) return;
        state.orthodox_school = school.Value.ToString();
        state.orthodox_since = World.world.getCurWorldTime();
        foreach (FixedFaction clique in empire.CoreKingdom.GetRegime()?.GetPlayerFactions()
                     ?.Where(faction => faction != null && faction.IsClique && !faction.Ban && faction.Ideology == school.Value)
                     .ToList() ?? new List<FixedFaction>())
            empire.CoreKingdom.TryIncreaseFactionRatio(clique, OrthodoxCliqueRatio);
        Record(empire, string.Format(LM.Get("orthodoxy_history"), PartySystem.GetIdeologyName(school.Value)), log: true);
    }

    private static void Record(Empire empire, string content, bool log = false)
    {
        LogService.LogInfo("[EmpireCraft][百家] " + content);
        empire.RecordHistory(directContent: content, kingdomId: empire.CoreKingdom?.id ?? -1L);
        if (log) EmpireCraft.Scripts.HelperFunc.TranslateHelper.LogEventMessage(content, empire.CoreKingdom);
    }
}

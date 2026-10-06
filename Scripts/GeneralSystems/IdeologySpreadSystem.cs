using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 理念传播。本文化第一次有帝国以某理念改制共和，这个理念就成为本文化的主导理念；
// 之后每年，主导理念向接壤的异文化传播；达到阈值时先影响当地居民。
// 文化制度节点仍须由本国执政党推进，政党能否成立则取决于本国人口支持。
public static class IdeologySpreadSystem
{
    private const float ExposurePerBorder = 10f;
    private const float MaxExposurePerYear = 30f;
    private const float UnlockExposure = 100f;
    private static double _lastScan = -1d;

    public static void ResetWorldState() => _lastScan = -1d;

    public static string FeatureKey(PartyIdeology ideology) => $"ideology:{ideology}";

    public static bool TryParseFeature(string feature, out PartyIdeology ideology)
    {
        ideology = default;
        return feature != null && feature.StartsWith("ideology:", StringComparison.Ordinal) &&
               Enum.TryParse(feature.Substring("ideology:".Length), out ideology);
    }

    public static void SetStateIdeology(string culture, PartyIdeology ideology, Empire pioneer)
    {
        CultureInstitutionState state = InstitutionSystem.GetOrCreateCultureState(culture);
        if (state == null || !string.IsNullOrEmpty(state.state_ideology)) return;
        state.state_ideology = ideology.ToString();
        state.state_ideology_since = World.world.getCurWorldTime();
        TranslateHelper.LogEventMessage(string.Format(LM.Get("ideology_state_ideology_log"),
            culture.GetCultureTranslate(), PartySystem.GetIdeologyName(ideology), pioneer?.GetEmpireName() ?? ""),
            pioneer?.CoreKingdom);
    }

    public static bool TryGetStateIdeology(string culture, out PartyIdeology ideology)
    {
        ideology = default;
        CultureInstitutionState state = InstitutionSystem.GetOrCreateCultureState(culture);
        return state != null && Enum.TryParse(state.state_ideology, out ideology);
    }

    // 挂在城市更新上，这里自己限流成一年一次
    public static void TryYearlyScan()
    {
        if (World.world == null || ModClass.IS_CLEAR || ModClass.EMPIRE_MANAGER == null) return;
        if (TechnologySystem.PremodernLocked) return; // 禁止近代化：理念不再传播
        IdeologyPopulationSystem.TickContact(); // 上一轮年度交往分帧处理中
        double now = World.world.getCurWorldTime();
        if (_lastScan < 0d || now < _lastScan)
        {
            _lastScan = now;
            return;
        }
        if (Date.getYearsSince(_lastScan) < 1) return;
        _lastScan = now;
        try
        {
            Spread();
            IdeologyPopulationSystem.TryYearlyContact();
        }
        catch (Exception exception)
        {
            NeoModLoader.services.LogService.LogError($"理念传播失败: {exception}");
        }
    }

    private static void Spread()
    {
        // 目标文化 → (理念 → 今年新增接触度)
        var gains = new Dictionary<string, Dictionary<PartyIdeology, float>>();
        foreach (Empire empire in ModClass.EMPIRE_MANAGER.ToList())
        {
            if (empire?.data == null || empire.isRekt() || empire.IsArchived() || empire.CoreKingdom == null) continue;
            string source = InstitutionSystem.GetPrimaryCulture(empire);
            if (!TryGetStateIdeology(source, out PartyIdeology ideology)) continue;
            var borders = new HashSet<(long, long)>();
            foreach (Kingdom kingdom in empire.kingdoms_hashset.Where(kingdom => kingdom != null && !kingdom.isRekt()))
            foreach (City city in kingdom.cities.Where(city => city != null && !city.isRekt()))
            foreach (City neighbour in (IEnumerable<City>)city.neighbours_cities ?? Array.Empty<City>())
            {
                Kingdom other = neighbour?.kingdom;
                if (other == null || other.isRekt() || other.GetEmpire() == empire) continue;
                string target = CultureService.GetRealmCulture(other);
                if (!CultureService.IsValidCulture(target) || target == source) continue;
                if (!borders.Add((kingdom.id, other.id))) continue;
                if (!gains.TryGetValue(target, out Dictionary<PartyIdeology, float> perIdeology))
                    gains[target] = perIdeology = new Dictionary<PartyIdeology, float>();
                perIdeology[ideology] = Math.Min(MaxExposurePerYear,
                    (perIdeology.TryGetValue(ideology, out float value) ? value : 0f) + ExposurePerBorder);
            }
        }

        foreach (KeyValuePair<string, Dictionary<PartyIdeology, float>> culture in gains)
        foreach (KeyValuePair<PartyIdeology, float> gain in culture.Value)
            ApplyExposure(culture.Key, gain.Key, gain.Value);
    }

    private static void ApplyExposure(string culture, PartyIdeology ideology, float gain)
    {
        // 基本理念随开放党禁而来，不单独播种。
        if (PartySystem.BaseIdeologies.Contains(ideology) ||
            InstitutionSystem.GetFeature(culture, FeatureKey(ideology)) > 0f) return;
        CultureInstitutionState state = InstitutionSystem.GetOrCreateCultureState(culture);
        if (state == null) return;
        state.ideology_exposure ??= new Dictionary<string, float>();
        string key = ideology.ToString();
        float current = state.ideology_exposure.TryGetValue(key, out float previous) ? previous : 0f;
        if (current < 0f)
        {
            if (IdeologyPopulationSystem.IsIdeaAvailable(culture, ideology)) return;
            current = UnlockExposure; // 旧存档可能把零人改信也记成了传播完成，允许重试。
        }
        float exposure = Math.Min(UnlockExposure, current + Math.Max(0f, gain));
        if (exposure >= UnlockExposure && !TechnologySystem.AreFeatureTechsMet(culture, FeatureKey(ideology)))
            exposure = UnlockExposure - 0.01f;
        state.ideology_exposure[key] = exposure;
        if (exposure < UnlockExposure) return;
        int changed = IdeologyPopulationSystem.IntroduceToCulture(culture, ideology);
        if (changed <= 0) return; // 暂无读者或未说服任何人时，接触度保留在门槛，下一次继续。
        state.ideology_exposure[key] = -1f;
        TranslateHelper.LogEventMessage(string.Format(LM.Get("ideology_spread_log"),
            PartySystem.GetIdeologyName(ideology), culture.GetCultureTranslate()));
    }
}

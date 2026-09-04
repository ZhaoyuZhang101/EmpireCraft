using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.Layer;

public static class EmpireCoreManager
{
    public static Dictionary<long, EmpireCore> EmpireCores = new Dictionary<long, EmpireCore>();

    public static void SyncCitiesFromTitles(EmpireCore core)
    {
        if (core == null) return;
        HashSet<City> targetCities = GetCities(core).Where(c => c != null && !c.isRekt()).ToHashSet();

        foreach (var city in World.world.cities.list)
        {
            if (city == null || city.isRekt()) continue;
            if (targetCities.Contains(city))
            {
                city.SetEmpireCore(core);
            }
            else if (city.GetEmpireCoreID() == core.id)
            {
                city.SetEmpireCore(null);
            }
        }
    }

    public static EmpireCore newEmpireCore(Empire empire)
    {
        var empireCore = new EmpireCore()
        {
            id = OverallHelperFunc.IdGenerator.NextId(),
            empire_id = empire.id,
            culture = empire.CoreKingdom.culture.id,
            name =  empire.GetEmpireName().AppendWithNarrowSpace("EmpireText".GetLocal()),
            create_timestamp = empire.data.created_time,
            titlesRecord = empire.CoreKingdom.GetControlledTitles().Select(t=>(World.world.getCurWorldTime(), t.id)).ToList(),
            empire_history_ids = new List<long>(),
            CoreCapital = empire.CoreKingdom.capital.id
        };
        EmpireCores[empireCore.id] = empireCore;
        empire.data.empire_core_id = empireCore.id;
        RegisterEmpireHistory(empireCore, empire.id);
        SyncCitiesFromTitles(empireCore);
        return empireCore;
    }

    public static EmpireCore Get(long id)
    {
        return id <= 0 ? null : (EmpireCores.TryGetValue(id, out var core) ? core : null);
    }

    public static EmpireCore Get(Empire empire)
    {
        return empire == null ? null : Get(empire.data?.empire_core_id ?? -1L);
    }

    public static List<City> GetCities(EmpireCore core)
    {
        List<City> cities = new();
        foreach (var title in GetTitles(core))
        {
            foreach (var city in title.getCities())
            {
                if (city == null || city.isRekt()) continue;
                if (!cities.Contains(city))
                {
                    cities.Add(city);
                }
            }
        }
        return cities;
    }

    public static List<Empire> GetEmpires(EmpireCore core)
    {
        if (core == null) return new List<Empire>();
        return ModClass.EMPIRE_MANAGER.ToList()
            .Where(e => e != null && !e.IsArchived() && e.data?.empire_core_id == core.id &&
                !EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(e.CoreKingdom))
            .ToList();
    }

    public static int GetActiveEmpireCount(EmpireCore core)
    {
        return GetEmpires(core).Count;
    }

    public static void RegisterEmpireHistory(EmpireCore core, long empireId)
    {
        if (core == null || empireId <= 0) return;
        core.empire_history_ids ??= new List<long>();
        if (!core.empire_history_ids.Contains(empireId))
        {
            core.empire_history_ids.Add(empireId);
        }
    }

    public static City GetRepresentativeCity(EmpireCore core)
    {
        if (core == null) return null;
        var city = core.GetCoreCapital();
        if (city == null)
        {
            KingdomTitle firstTitle = GetTitles(core).FirstOrDefault(t => t?.title_capital != null && !t.title_capital.isRekt());
            if (firstTitle == null) return null;
            core.SetCoreCapital(firstTitle.title_capital);
        }
        city = core.GetCoreCapital();
        return city;
    }

    public static KingdomTitle GetColorTitle(EmpireCore core)
    {
        if (core == null) return null;
        KingdomTitle capitalTitle = GetRepresentativeCity(core)?.GetTitle();
        if (capitalTitle != null && !capitalTitle.isRekt() && ContainsTitle(core, capitalTitle))
        {
            return capitalTitle;
        }

        return GetTitles(core).FirstOrDefault(t => t != null && !t.isRekt());
    }

    public static bool GenerateColor(EmpireCore core)
    {
        KingdomTitle colorTitle = GetColorTitle(core);
        if (colorTitle == null) return false;
        colorTitle.generateColor();
        return true;
    }

    public static string GetCultureDisplayName(EmpireCore core)
    {
        if (core == null) return "";
        Culture culture = World.world.cultures.get(core.culture);
        if (culture != null && !string.IsNullOrWhiteSpace(culture.data?.name))
        {
            return culture.data.name;
        }

        City city = GetRepresentativeCity(core);
        return city?.kingdom?.GetEmpireCraftCulture(true) ?? "";
    }

    public static string GetDisplayName(EmpireCore core)
    {
        if (core == null) return "";
        if (!string.IsNullOrWhiteSpace(core.name))
        {
            return core.name;
        }

        string cultureName = GetCultureDisplayName(core);
        if (!string.IsNullOrWhiteSpace(cultureName))
        {
            return cultureName[0] + "\u200A" + LM.Get("EmpireText");
        }

        return LM.Get("EmpireText");
    }

    public static string GetPlateName(EmpireCore core)
    {
        if (core == null) return "";
        string cultureName = GetCultureDisplayName(core);
        if (!string.IsNullOrWhiteSpace(cultureName))
        {
            return cultureName[0] + "\u200A" + LM.Get("EmpireText");
        }

        return GetDisplayName(core);
    }

    public static List<string> GetCurrentEmpireNames(EmpireCore core)
    {
        return GetEmpires(core)
            .Where(e => e != null)
            .Select(e => e.GetEmpireName())
            .Distinct()
            .ToList();
    }

    public static string GetFoundingEmpireName(EmpireCore core)
    {
        if (core == null) return "";
        foreach (long empireId in core.empire_history_ids ?? new List<long>())
        {
            Empire activeEmpire = ModClass.EMPIRE_MANAGER.get(empireId);
            if (activeEmpire != null && !activeEmpire.isRekt())
            {
                return activeEmpire.GetEmpireName();
            }

            if (ModClass.ALL_HISTORY_DATA.TryGetValue(empireId, out var histories) && histories != null && histories.Count > 0)
            {
                return histories[0].empire_name;
            }
        }
        return "";
    }

    public static List<EmpireCraftHistory> GetAllHistories(EmpireCore core)
    {
        List<EmpireCraftHistory> histories = new();
        if (core == null) return histories;
        HashSet<long> historyIds = new();

        foreach (long empireId in core.empire_history_ids ?? new List<long>())
        {
            Empire activeEmpire = ModClass.EMPIRE_MANAGER.get(empireId);
            if (activeEmpire != null && !activeEmpire.isRekt() && activeEmpire.data?.history != null)
            {
                foreach (var history in activeEmpire.data.history)
                {
                    if (history == null) continue;
                    if (historyIds.Add(history.id))
                    {
                        histories.Add(history);
                    }
                }
            }

            if (ModClass.ALL_HISTORY_DATA.TryGetValue(empireId, out var oldHistories) && oldHistories != null)
            {
                foreach (var history in oldHistories)
                {
                    if (history == null) continue;
                    if (historyIds.Add(history.id))
                    {
                        histories.Add(history);
                    }
                }
            }
        }

        return histories
            .OrderBy(h => h?.descriptions?.FirstOrDefault()?.time ?? "")
            .ToList();
    }

    public static List<KingdomTitle> GetTitles(EmpireCore core)
    {
        List<KingdomTitle> titles = new();
        if (core?.titlesRecord == null) return titles;
        foreach (var record in core.titlesRecord)
        {
            KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER.get(record.titleId);
            if (title == null || title.isRekt()) continue;
            if (!titles.Contains(title))
            {
                titles.Add(title);
            }
        }
        return titles;
    }

    public static bool ContainsTitle(EmpireCore core, KingdomTitle title)
    {
        if (core == null || title == null || title.isRekt()) return false;
        return GetTitles(core).Contains(title);
    }

    public static EmpireCore GetRiseCandidateCore(Kingdom kingdom)
    {
        if (kingdom == null) return null;
        KingdomTitle mainTitle = kingdom.GetMainTitle();
        if (mainTitle?.title_capital != null)
        {
            EmpireCore mainCore = mainTitle.title_capital.GetEmpireCore();
            if (mainCore != null) return mainCore;
        }

        if (kingdom.capital != null)
        {
            EmpireCore capitalCore = kingdom.capital.GetEmpireCore();
            if (capitalCore != null) return capitalCore;
        }

        foreach (var title in kingdom.GetControlledTitle())
        {
            if (title?.title_capital == null) continue;
            EmpireCore core = title.title_capital.GetEmpireCore();
            if (core != null) return core;
        }

        return null;
    }

    public static int GetControlledCoreTitleCount(EmpireCore core, Kingdom kingdom)
    {
        if (core == null || kingdom == null) return 0;
        HashSet<KingdomTitle> controlledTitles = kingdom.GetControlledTitle().Where(t => t != null && !t.isRekt()).ToHashSet();
        return GetTitles(core).Count(t => controlledTitles.Contains(t));
    }

    public static int GetRequiredRiseTitleCount(EmpireCore core)
    {
        int total = GetTitles(core).Count;
        if (total <= 0) return int.MaxValue;
        return (int)Math.Ceiling(total / 2.0);
    }

    public static int GetAssimilationCost(Empire empire, KingdomTitle title)
    {
        if (empire?.CoreKingdom == null || title?.title_capital?.kingdom == null) return int.MaxValue;
        Kingdom source = title.title_capital.kingdom;
        if (source.getSpecies() != empire.CoreKingdom.getSpecies()) return 3000;
        if (source.GetEmpireCraftCulture() != empire.CoreKingdom.GetEmpireCraftCulture()) return 1500;
        return 10000;
    }

    public static bool TryAbsorbTitle(Empire empire, KingdomTitle title)
    {
        if (empire?.CoreKingdom == null || title == null || title.isRekt()) return false;
        EmpireCore core = Get(empire) ?? newEmpireCore(empire);
        if (ContainsTitle(core, title)) return false;
        if (EmpireCores.Values.Any(other => other != null && other.id != core.id && ContainsTitle(other, title))) return false;
        if (!empire.kingdoms_hashset.Any(k => k != null && !k.isRekt() && title.getCities().Any(c => c != null && !c.isRekt() && c.kingdom == k))) return false;
        int cost = GetAssimilationCost(empire, title);
        if (empire.CurrentMoney < cost) return false;
        empire.CoreKingdom.SubMoney(cost);
        bool result = AddTitleToCore(core, title);
        if (result)
        {
            TranslateHelper.LogEmpireCoreAbsorbTitle(empire, title, core);
        }
        return result;
    }

    public static void RebindEmpire(Empire empire, EmpireCore core)
    {
        if (empire == null || core == null) return;
        long previousEmpireId = core.empire_id;
        core.empire_id = empire.id;
        empire.data.empire_core_id = core.id;
        RegisterEmpireHistory(core, empire.id);
        SyncCitiesFromTitles(core);
        if (previousEmpireId > 0 && previousEmpireId != empire.id)
        {
            if (ModClass.ALL_HISTORY_DATA.TryGetValue(previousEmpireId, out var oldHistory) && oldHistory != null && oldHistory.Count > 0)
            {
                empire.data.history ??= new List<EmpireCraftHistory>();
                if (empire.data.history.Count == 0)
                {
                    empire.data.history = new List<EmpireCraftHistory>(oldHistory);
                }
            }
        }
    }

    public static EmpireCore CreateCoreFromTitle(KingdomTitle title)
    {
        if (title == null || title.isRekt() || title.title_capital == null || title.title_capital.isRekt())
        {
            return null;
        }

        EmpireCore existing = title.title_capital.GetEmpireCore();
        if (existing != null)
        {
            return existing;
        }

        City capital = title.title_capital;
        Kingdom kingdom = capital.kingdom;
        EmpireCore core = new EmpireCore
        {
            id = OverallHelperFunc.IdGenerator.NextId(),
            empire_id = -1L,
            culture = kingdom?.culture?.id ?? -1L,
            name = string.IsNullOrWhiteSpace(title.data?.name) ? (kingdom?.name ?? capital.GetCityName()) : title.data.name,
            create_timestamp = World.world.getCurWorldTime(),
            titlesRecord = new List<(double time, long titleId)>()
        };
        EmpireCores[core.id] = core;
        AddTitleToCore(core, title);
        return core;
    }

    public static bool AddTitleToCore(EmpireCore core, KingdomTitle title)
    {
        if (core == null || title == null || title.isRekt()) return false;
        bool changed = false;

        EmpireCore previousCore = title.title_capital?.GetEmpireCore();
        if (previousCore != null && previousCore.id != core.id)
        {
            RemoveTitleFromCore(previousCore, title);
        }

        if (core.AddTitle(title))
        {
            changed = true;
            foreach (Empire empire in GetEmpires(core))
            {
                empire.data.last_legal_peerage_timestamp = -1L;
            }
        }

        SyncCitiesFromTitles(core);
        return changed;
    }

    public static bool RemoveTitleFromCore(EmpireCore core, KingdomTitle title)
    {
        if (core == null || title == null || title.isRekt()) return false;
        bool changed = core.RemoveTitle(title);
        SyncCitiesFromTitles(core);
        return changed;
    }

    public static bool DestroyEmpireCore(EmpireCore core)
    {
        if (core == null) return false;
        foreach (var city in World.world.cities.list)
        {
            if (city == null || city.isRekt()) continue;
            if (city.GetEmpireCoreID() == core.id)
            {
                city.SetEmpireCore(null);
            }
        }

        foreach (var empire in ModClass.EMPIRE_MANAGER.ToList())
        {
            if (empire == null || empire.IsArchived()) continue;
            if (empire.data?.empire_core_id == core.id)
            {
                empire.data.empire_core_id = -1L;
            }
        }

        return EmpireCores.Remove(core.id);
    }
}

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

    public static EmpireCore newEmpireCore(Empire empire)
    {
        var empireCore = new EmpireCore()
        {
            id = OverallHelperFunc.IdGenerator.NextId(),
            empire_id = empire.id,
            culture = empire.CoreKingdom.culture.id,
            name =  empire.CoreKingdom.name,
            create_timestamp = empire.data.created_time,
            citiesRecord = empire.cities_list.Select(c=>(World.world.getCurWorldTime(), c.id)).ToList(),
            empire_history_ids = new List<long>()
        };
        EmpireCores[empireCore.id] = empireCore;
        empire.data.empire_core_id = empireCore.id;
        RegisterEmpireHistory(empireCore, empire.id);
        foreach (var city in empire.cities_list)
        {
            if (city == null || city.isRekt()) continue;
            city.SetEmpireCore(empireCore);
        }
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
        if (core?.citiesRecord == null) return cities;
        foreach (var record in core.citiesRecord)
        {
            City city = World.world.cities.get(record.cityId);
            if (city == null || city.isRekt()) continue;
            if (city.GetEmpireCoreID() != core.id) continue;
            if (!cities.Contains(city))
            {
                cities.Add(city);
            }
        }
        return cities;
    }

    public static List<Empire> GetEmpires(EmpireCore core)
    {
        if (core == null) return new List<Empire>();
        return ModClass.EMPIRE_MANAGER.ToList()
            .Where(e => e != null && !e.IsArchived() && e.data?.empire_core_id == core.id)
            .ToList();
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
        foreach (var empire in GetEmpires(core))
        {
            if (empire?.CoreKingdom?.capital != null && !empire.CoreKingdom.capital.isRekt())
            {
                return empire.CoreKingdom.capital;
            }
        }

        foreach (var title in GetTitles(core))
        {
            if (title?.title_capital != null && !title.title_capital.isRekt())
            {
                return title.title_capital;
            }
        }

        return GetCities(core).FirstOrDefault(c => c != null && !c.isRekt());
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
        foreach (var city in GetCities(core))
        {
            KingdomTitle title = city.GetTitle();
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
        foreach (var city in title.getCities())
        {
            if (city == null || city.isRekt()) continue;
            city.SetEmpireCore(core);
            core.AddCity(city);
        }
        return true;
    }

    public static void RebindEmpire(Empire empire, EmpireCore core)
    {
        if (empire == null || core == null) return;
        long previousEmpireId = core.empire_id;
        core.empire_id = empire.id;
        empire.data.empire_core_id = core.id;
        RegisterEmpireHistory(core, empire.id);
        foreach (var city in empire.cities_list)
        {
            if (city == null || city.isRekt()) continue;
            city.SetEmpireCore(core);
            core.AddCity(city);
        }
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
            citiesRecord = new List<(double time, long cityId)>()
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

        foreach (var city in title.getCities())
        {
            if (city == null || city.isRekt()) continue;
            city.SetEmpireCore(core);
            if (core.AddCity(city))
            {
                changed = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(title.data?.name))
        {
            core.name = title.data.name;
        }
        return changed;
    }

    public static bool RemoveTitleFromCore(EmpireCore core, KingdomTitle title)
    {
        if (core == null || title == null || title.isRekt()) return false;
        bool changed = false;
        foreach (var city in title.getCities())
        {
            if (city == null || city.isRekt()) continue;
            if (city.GetEmpireCoreID() != core.id) continue;
            city.SetEmpireCore(null);
            if (core.RemoveCity(city))
            {
                changed = true;
            }
        }
        return changed;
    }

    public static bool DestroyEmpireCore(EmpireCore core)
    {
        if (core == null) return false;
        foreach (var city in GetCities(core))
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

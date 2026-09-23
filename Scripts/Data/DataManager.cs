
using System;
using System.IO;
using NeoModLoader.services;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using EmpireCraft.Scripts.Layer;
using static EmpireCraft.Scripts.GameClassExtensions.ActorExtension;
using static EmpireCraft.Scripts.GameClassExtensions.CityExtension;
using static EmpireCraft.Scripts.GameClassExtensions.KingdomExtension;
using static EmpireCraft.Scripts.GameClassExtensions.ClanExtension;
using static EmpireCraft.Scripts.GameClassExtensions.WarExtension;
using db;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.Enums;

namespace EmpireCraft.Scripts.Data;

public static class DataManager
{
    public const string EmpireCraftSaveFileName = "EmpireCraftModData.json";
    public static string CurrentSaveDataPath { get; private set; } = "";

    public static void ResetCurrentSaveDataPath()
    {
        CurrentSaveDataPath = "";
    }

    public static void LoadAll(string loadRootPath)
    {
        InstitutionSystem.ResetWorldState();
        string loadPath = Path.Combine(loadRootPath, EmpireCraftSaveFileName);
        CurrentSaveDataPath = loadPath;
        NormalizeLoadedNameSeparators();
        if (!File.Exists(loadPath))
        {
            foreach (var worldKingdom in World.world.kingdoms)
            {
                worldKingdom.InitialRegime();
            }
            LogService.LogInfo("没有找到任何保存数据。");
            return;
        }
        var json = File.ReadAllText(loadPath);
        FactionRatioConverter.ResetLegacyDiscardCount();
        var saveData = JsonConvert.DeserializeObject<SaveData>(json);
        NormalizeSaveData(saveData);
        InstitutionSystem.ImportCultureStates(saveData?.cultureInstitutionStates);
        if (FactionRatioConverter.DiscardedLegacyEntryCount > 0)
        {
            LogService.LogWarning($"已迁移旧存档中 {FactionRatioConverter.DiscardedLegacyEntryCount} 条无法识别的派系占比；将在加载后按当前派系配置重建。");
        }
        LogService.LogInfo("初始化模组数据模板");
        bool isOldSave = saveData == null || saveData.mod_version < ModClass.MOD_DATA_VERSION;

        if (saveData == null || saveData.actorsExtraData == null || saveData.actorsExtraData.Count == 0)
        {
            LogService.LogInfo("没有找到任何保存数据。");
            return;
        }
        List<string> a = new List<string>();
        var unitById = World.world.units.ToDictionary(u => u.getID());
        var kingdomById = World.world.kingdoms.ToDictionary(k => k.getID());
        var cityById = World.world.cities.ToDictionary(c => c.getID());
        var clanById = World.world.clans.ToDictionary(c => c.getID());
        var warById = World.world.wars.ToDictionary(w => w.getID());
        var religionById = World.world.religions.ToDictionary(r => r.getID());
        CulturePatch.ImportCultureBindings(saveData.cultureBindings);
        LogService.LogInfo("准备各项数据");
        OfficeManager.Offices = saveData.officeObjects;
        // 批量同步
        foreach (var entry in saveData.actorsExtraData)
        {
            if (entry == null) continue;
            if (unitById.TryGetValue(entry.id, out Actor actor))
                actor.SyncData(entry);
        }
        LogService.LogInfo("Sync Actor Data");
        foreach (var entry in saveData.kingdomExtraData)
        {
            if (entry == null) continue;
            if (kingdomById.TryGetValue(entry.id, out var kingdom))
            {
                kingdom.SyncData(entry);
                if (kingdom.GetOfficeID() == -1L)
                {
                    var culture = CultureService.GetRealmCulture(kingdom);
                    RegimeType regimeType = InstitutionSystem.TryResolveCultureRegime(culture, out RegimeType resolvedRegime)
            ? resolvedRegime
            : OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting)
                ? setting.regime
                : RegimeType.Feudalism;
                    kingdom.SetRegimeType(regimeType);
                    kingdom.LoadRegime();
                    EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(kingdom);
                }
            }
        }
        LogService.LogInfo("Sync Kingdom Data");
        foreach (var entry in saveData.cityExtraData)
        {
            if (entry == null) continue;
            if (cityById.TryGetValue(entry.id, out var city))
            {
                city.SyncData(entry);
                if (city.GetOfficeID() == -1L && city.kingdom != null && !city.kingdom.isRekt())
                {
                    Regime regime = city.kingdom.GetRegime();
                    if (regime?.bureau_config?.cities != null)
                    {
                        CityType cityType = EmpireCraftKingdomBehCheckKingdomType.CalcCityType(city.kingdom);
                        if (!regime.bureau_config.cities.TryGetValue(cityType, out BureauSetting citySetting))
                        {
                            continue;
                        }
                        OfficeObject officeObject = city.GetOffice();
                        if (officeObject != null)
                        {
                            officeObject.InitialOffice(citySetting, isNew:false);
                            officeObject.regimeType = regime.type;
                        }
                        else
                        {
                            officeObject = new OfficeObject();
                            officeObject.InitialOffice(citySetting);
                            officeObject.regimeType = regime.type;
                            city.SetOffice(officeObject);
                        }
                    }
                }
            }
        }
        LogService.LogInfo("Sync City Data");
        foreach (var entry in saveData.clanExtraData)
        {
            if (entry == null) continue;
            if (clanById.TryGetValue(entry.id, out var clan))
                clan.SyncData(entry);
        }
        LogService.LogInfo("Sync Clan Data");
        foreach (var entry in saveData.warExtraData)
        {
            if (entry == null) continue;
            if (warById.TryGetValue(entry.id, out var war))
                war.SyncData(entry);
        }
        LogService.LogInfo("Sync War Data");
        foreach (var entry in saveData.religionExtraData)
        {
            if (entry == null) continue;
            if (religionById.TryGetValue(entry.id, out var religion))
                religion.SyncData(entry);
        }
        LogService.LogInfo("Sync Religion Data");
        foreach (EmpireData empireData in saveData.empireDatas)
        {
            if (empireData == null) continue;
            try
            {
                NormalizeEmpireData(empireData);
                Empire empire = new Empire();
                empire.loadData(empireData);
                ModClass.EMPIRE_MANAGER.addObject(empire);
                LogService.LogInfo($"加载帝国{empire.name}");
                if (empire.data.centerOffice == null && empire.CoreKingdom != null)
                {
                    empire.data.centerOffice = new CenterOffice();
                    empire.data.centerOffice.Init(empire.CoreKingdom);
                    empire.CoreKingdom.SetLevel(0);
                }
                if (empire.data.centerOffice != null && empire.CoreKingdom != null)
                {
                    empire.data.centerOffice.SyncMetaObject(empire.CoreKingdom);
                }
            }
            catch (Exception e)
            {
                LogService.LogError($"加载帝国数据失败，已跳过: {e}");
            }
        }
        ModClass.EMPIRE_MANAGER.update(-1L);
        LogService.LogInfo("Sync Empire Data");
        EmpireCoreManager.EmpireCores = BuildEmpireCoreMap(saveData.empireCoreDatas);

        List<KingdomTitleData> titleDatas = saveData.kingdomTitleDatas;
        if (titleDatas == null || titleDatas.Count == 0)
        {
            titleDatas = RebuildKingdomTitleDataFromCityData(saveData, cityById);
        }
        Dictionary<long, long> savedCityTitles = saveData.cityExtraData
            .Where(cityData => cityData != null)
            .GroupBy(cityData => cityData.id)
            .ToDictionary(group => group.Key, group => group.Last().title_id);

        foreach (KingdomTitleData kingdomTitleData in titleDatas)
        {
            if (kingdomTitleData == null) continue;
            try
            {
                NormalizeKingdomTitleData(kingdomTitleData, savedCityTitles, cityById);
                if (kingdomTitleData.cities.Count == 0) continue;
                KingdomTitle kt = new KingdomTitle();
                kt.loadData(kingdomTitleData);
                if (kt.getCities().Any())
                {
                    kt.isBeenControlled();
                }
                ModClass.KINGDOM_TITLE_MANAGER.addObject(kt);
            }
            catch (Exception e)
            {
                LogService.LogError($"加载法理头衔数据失败，已跳过: {e}");
            }

        }
        ModClass.KINGDOM_TITLE_MANAGER.update(-1L);
        CultureService.MigrateWorldCultureData();
        SpecificClanManager._specificClans = saveData.specificClans;
        SpecificClanManager.RebuildCache();
        LogService.LogInfo("Sync Titles Data");
        ConfigData.yearNameSubspecies = saveData.yearNameSubspecies;
        LogService.LogInfo("Sync history Data");
        ModClass.ALL_HISTORY_DATA = saveData.all_history;
        ModClass.CULTURE_NO_EMPIRE_SINCE = saveData.culture_no_empire_since ?? new Dictionary<string, double>();
        ModClass.FEUDAL_EMPIRE_LINEAGE = saveData.feudal_empire_lineage ?? new Dictionary<string, List<long>>();
        ModClass.MOD_ALLIANCE_IDS = new HashSet<long>(saveData.mod_alliance_ids ?? new List<long>());
        if (PlayerConfig.dict.TryGetValue("switch_real_num", out PlayerOptionData realNumOption))
        {
            realNumOption.boolVal = saveData.switch_real_num;
        }
        ModClass.REAL_NUM_SWITCH = saveData.switch_real_num;
        if (PlayerConfig.dict.TryGetValue("switch_simple_nameplate", out PlayerOptionData simpleNameplateOption))
        {
            simpleNameplateOption.boolVal = saveData.switch_simple_nameplate;
        }
        ModClass.SIMPLE_NAMEPLATE_SWITCH = saveData.switch_simple_nameplate;
        if (PlayerConfig.dict.TryGetValue("switch_empire_show_alliance", out PlayerOptionData empireAllianceOption))
        {
            empireAllianceOption.boolVal = saveData.switch_empire_show_alliance;
        }
        ModClass.EMPIRE_SHOW_ALLIANCE_SWITCH = saveData.switch_empire_show_alliance;
        foreach (var worldKingdom in World.world.kingdoms)
        {
            if (worldKingdom == null || worldKingdom.isRekt()) continue;
            if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(worldKingdom)) continue;
            if (isOldSave)
            {
                worldKingdom.InitialRegime();
            }
            worldKingdom.SyncRealmTitlesFromRuler();
            if (worldKingdom.hasKing()) worldKingdom.TransferRealmTitlesToRuler(worldKingdom.king);
            worldKingdom.ReconcileMainTitle();
            worldKingdom.GetInitialRandomKingdomName();
            EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(worldKingdom);
        }
        foreach (Empire empire in ModClass.EMPIRE_MANAGER
                     .Where(value => value != null && !value.IsArchived() && !value.isRekt()).ToList())
        {
            try
            {
                CompositeEmpireService.SynchronizeRegionalInstitutions(empire);
                CompositeEmpireService.TryAdoptInstitutionalCultureName(empire);
                InstitutionSystem.MigrateEmpire(empire);
            }
            catch (Exception exception)
            {
                LogService.LogError($"帝国文化与制度存档迁移失败，保留原制度: {exception}");
            }
        }
    }

    private static void NormalizeLoadedNameSeparators()
    {
        if (World.world == null) return;

        foreach (Actor actor in World.world.units)
        {
            if (actor?.data == null || AncientWarfareCompatibility.OwnsObject(actor)) continue;
            actor.data.name = actor.data.name.UseLocalizedNameSeparator();
            if (actor.clan?.data != null)
                actor.clan.data.name = actor.clan.data.name.UseLocalizedNameSeparator();
            if (actor.family?.data != null)
                actor.family.data.name = actor.family.data.name.UseLocalizedNameSeparator();
        }

        foreach (Kingdom kingdom in World.world.kingdoms)
        {
            if (kingdom?.data == null || AncientWarfareCompatibility.Owns(kingdom)) continue;
            kingdom.data.name = kingdom.data.name.UseLocalizedNameSeparator();
        }

        foreach (City city in World.world.cities)
        {
            if (city?.data == null || AncientWarfareCompatibility.Owns(city.kingdom)) continue;
            city.data.name = city.data.name.UseLocalizedNameSeparator();
        }
    }
    public static void SaveAll(string saveRootPath)
    {
        if (string.IsNullOrEmpty(saveRootPath))
        {
            LogService.LogError("保存路径为空，无法保存mod数据");
            return;
        }
        string savePath = Path.Combine(saveRootPath, EmpireCraftSaveFileName);
        CurrentSaveDataPath = savePath;
        CultureService.MigrateWorldCultureData();
        SaveData saveData = new SaveData();
        saveData.actorsExtraData = World.world.units.Select(a=>a.GetExtraData<Actor, ActorExtraData>(true)).Where(ed=>ed!=null).ToList();
        saveData.cityExtraData = World.world.cities.Select(a => a.GetExtraData<City, CityExtraData>(true)).Where(ed => ed != null).ToList();
        saveData.religionExtraData = World.world.religions.Select(a => a.GetExtraData<Religion, ReligionExtension.ReligionExtraData>(true)).Where(ed => ed != null).ToList();
        saveData.kingdomExtraData = World.world.kingdoms.Select(a => a.GetExtraData<Kingdom, KingdomExtraData>(true)).Where(ed => ed != null).ToList(); ;
        saveData.warExtraData = World.world.wars.Select(a => a.GetExtraData<War, WarExtraData>(true)).Where(ed => ed != null).ToList(); ;
        saveData.clanExtraData = World.world.clans.Select(a => a.GetExtraData<Clan, ClanExtraData>(true)).Where(ed => ed != null).ToList(); ;
        saveData.empireDatas = new List<EmpireData>(ModClass.EMPIRE_MANAGER.Count);
        saveData.empireCoreDatas = EmpireCoreManager.EmpireCores.Values.Where(c => c != null).ToList();
        saveData.kingdomTitleDatas = new List<KingdomTitleData>(ModClass.KINGDOM_TITLE_MANAGER.Count);
        saveData.cultureBindings = CulturePatch.ExportCultureBindings();
        saveData.cultureInstitutionStates = InstitutionSystem.ExportCultureStates();
        ModClass.EMPIRE_MANAGER.update(-1L);
        ModClass.KINGDOM_TITLE_MANAGER.update(-1L);
        saveData.officeObjects = OfficeManager.Offices;
        saveData.mod_version = ModClass.MOD_DATA_VERSION;
        foreach (Empire empire in ModClass.EMPIRE_MANAGER)
        {
            try
            {
                empire.save();
                saveData.empireDatas.Add(empire.data);
            } catch (Exception e)
            {
                LogService.LogError($"帝国存档更新失败，保留现有数据: {e}");
                if (empire?.data != null && !saveData.empireDatas.Contains(empire.data))
                {
                    saveData.empireDatas.Add(empire.data);
                }
            }

        }
        foreach (KingdomTitle kt in ModClass.KINGDOM_TITLE_MANAGER)
        {
            try
            {
                kt.save();
                saveData.kingdomTitleDatas.Add(kt.data);
            } 
            catch (Exception e)
            {
                LogService.LogError($"头衔存档更新失败，保留现有数据: {e}");
                if (kt?.data != null && !saveData.kingdomTitleDatas.Contains(kt.data))
                {
                    saveData.kingdomTitleDatas.Add(kt.data);
                }
            }

        }
        saveData.yearNameSubspecies = ConfigData.yearNameSubspecies;
        saveData.all_history = ModClass.ALL_HISTORY_DATA;
        saveData.culture_no_empire_since = ModClass.CULTURE_NO_EMPIRE_SINCE;
        saveData.feudal_empire_lineage = ModClass.FEUDAL_EMPIRE_LINEAGE;
        saveData.mod_alliance_ids = ModClass.MOD_ALLIANCE_IDS.ToList();
        saveData.specificClans = SpecificClanManager._specificClans;
        saveData.switch_real_num = ModClass.REAL_NUM_SWITCH;
        saveData.switch_simple_nameplate = ModClass.SIMPLE_NAMEPLATE_SWITCH;
        saveData.switch_empire_show_alliance = ModClass.EMPIRE_SHOW_ALLIANCE_SWITCH;
        string json = JsonConvert.SerializeObject(saveData, Formatting.None);
        LogService.LogInfo($"Save Data: actors={saveData.actorsExtraData.Count}, wars={saveData.warExtraData.Count}, kingdoms={saveData.kingdomExtraData.Count}, cities={saveData.cityExtraData.Count}, religions={saveData.religionExtraData.Count}");
        File.WriteAllText(savePath, json);
        LogService.LogInfo("Save Finished");
    }

    private static void NormalizeSaveData(SaveData saveData)
    {
        if (saveData == null) return;
        saveData.actorsExtraData ??= new List<ActorExtraData>();
        saveData.kingdomExtraData ??= new List<KingdomExtraData>();
        saveData.cityExtraData ??= new List<CityExtraData>();
        saveData.clanExtraData ??= new List<ClanExtraData>();
        saveData.warExtraData ??= new List<WarExtraData>();
        saveData.religionExtraData ??= new List<ReligionExtension.ReligionExtraData>();
        saveData.empireDatas ??= new List<EmpireData>();
        saveData.empireCoreDatas ??= new List<EmpireCore>();
        saveData.kingdomTitleDatas ??= new List<KingdomTitleData>();
        saveData.cultureBindings ??= new Dictionary<long, string>();
        saveData.cultureInstitutionStates ??= new Dictionary<string, CultureInstitutionState>();
        foreach (CultureInstitutionState state in saveData.cultureInstitutionStates.Values)
        {
            InstitutionStateNormalizer.Normalize(state);
        }
        saveData.yearNameSubspecies ??= new List<string>();
        saveData.all_history ??= new Dictionary<long, List<EmpireCraftHistory>>();
        saveData.specificClans = saveData.specificClans?.Where(sc => sc != null).ToList() ?? new List<SpecificClan>();
        saveData.officeObjects ??= new Dictionary<long, OfficeObject>();
        foreach (EmpireData empireData in saveData.empireDatas)
        {
            NormalizeEmpireData(empireData);
        }
        foreach (KingdomTitleData titleData in saveData.kingdomTitleDatas)
        {
            NormalizeKingdomTitleCollections(titleData);
        }
    }

    private static void NormalizeEmpireData(EmpireData data)
    {
        if (data == null) return;
        data.CabinetMembers ??= new List<long>();
        data.PreviousYearsMoney ??= new List<int>();
        data.history ??= new List<EmpireCraftHistory>();
        data.given_Kingdoms ??= new List<long>();
        data.taken_Kingdoms ??= new List<long>();
        data.additions ??= new EmpireAddition();
        data.additions.addition ??= new Dictionary<OfficerPowerType, int>();
        foreach (OfficerPowerType powerType in OfficeManager.AllPower ?? new List<OfficerPowerType>())
        {
            if (!data.additions.addition.ContainsKey(powerType))
            {
                data.additions.addition[powerType] = 0;
            }
        }
        data.kingdoms ??= new List<long>();
        data.cities ??= new List<long>();
        data.history_emperrors ??= new List<string>();
        data.honorary_peerage_holders ??= new Dictionary<string, long>();
        data.honorary_peerage_holder_identities ??= new Dictionary<string, long>();
        data.honorary_peerage_holder_names ??= new Dictionary<string, string>();
        data.defeated_empire_houses ??= new List<DefeatedEmpireHouse>();
        data.legal_peerage_holders ??= new Dictionary<long, long>();
        data.legal_peerage_holder_identities ??= new Dictionary<long, long>();
        data.legal_peerage_types ??= new Dictionary<long, string>();
        data.legal_peerage_kingdoms ??= new Dictionary<long, long>();
        data.conquest_founder_culture ??= "";
        data.ruling_culture ??= "";
        data.institutional_culture ??= "";
        data.external_identity_culture ??= "";
        data.military_tradition ??= "";
        data.composite_cultural_name ??= "";
        data.composite_cultural_name_culture ??= "";
        data.composite_cultural_name_source ??= "";
        data.institution_state ??= new InstitutionEmpireState();
        InstitutionStateNormalizer.Normalize(data.institution_state);
        data.composite_integration = Math.Max(0, Math.Min(100, data.composite_integration));
        data.central_plains_legitimacy = Math.Max(0, Math.Min(100, data.central_plains_legitimacy));
        data.ruling_tradition_legitimacy = Math.Max(0, Math.Min(100, data.ruling_tradition_legitimacy));
        foreach (EmpireCraftHistory history in data.history)
        {
            if (history == null) continue;
            history.initial_cities ??= new List<string>();
            history.descriptions ??= new List<HistoryDescription>();
            foreach (HistoryDescription description in history.descriptions)
            {
                if (description == null) continue;
                description.cities ??= new List<string>();
            }
        }
    }

    private static Dictionary<long, EmpireCore> BuildEmpireCoreMap(IEnumerable<EmpireCore> cores)
    {
        Dictionary<long, EmpireCore> result = new Dictionary<long, EmpireCore>();
        if (cores == null) return result;
        foreach (EmpireCore core in cores)
        {
            if (core == null || core.id <= 0) continue;
            core.titlesRecord ??= new List<(double time, long titleId)>();
            core.empire_history_ids ??= new List<long>();
            result[core.id] = core;
        }
        return result;
    }

    private static void NormalizeKingdomTitleCollections(KingdomTitleData data)
    {
        if (data == null) return;
        data.cities ??= new List<long>();
        data.history_emperrors ??= new List<string>();
        data.jurisdiction_history ??= new List<KingdomTitleJurisdictionRecord>();
        data.title_name_history ??= new Dictionary<string, string>();
        data.province_name_history ??= new Dictionary<string, string>();
    }

    private static void NormalizeKingdomTitleData(KingdomTitleData data, Dictionary<long, long> savedCityTitles,
        Dictionary<long, City> cityById)
    {
        if (data == null)
        {
            return;
        }

        NormalizeKingdomTitleCollections(data);
        HashSet<long> cityIds = new HashSet<long>();
        foreach (long cityId in data.cities)
        {
            if (cityById.ContainsKey(cityId) &&
                (savedCityTitles == null || !savedCityTitles.TryGetValue(cityId, out long savedTitleId) ||
                 savedTitleId == data.id))
            {
                cityIds.Add(cityId);
            }
        }

        if (savedCityTitles != null)
        {
            foreach (var cityData in savedCityTitles)
            {
                if (cityData.Value != data.id)
                {
                    continue;
                }

                if (cityById.ContainsKey(cityData.Key))
                {
                    cityIds.Add(cityData.Key);
                }
            }
        }

        data.cities = cityIds.ToList();

        if (!cityIds.Contains(data.title_capital) && data.cities.Count > 0)
        {
            data.title_capital = data.cities.First();
        }

        if (string.IsNullOrWhiteSpace(data.original_actor_asset) &&
            data.title_capital > 0 &&
            cityById.TryGetValue(data.title_capital, out City capital))
        {
            data.original_actor_asset = capital.kingdom?.king?.asset?.id ?? capital.kingdom?.asset?.id ?? capital.getSpecies();
        }
    }

    private static List<KingdomTitleData> RebuildKingdomTitleDataFromCityData(SaveData saveData, Dictionary<long, City> cityById)
    {
        List<KingdomTitleData> result = new List<KingdomTitleData>();
        if (saveData?.cityExtraData == null)
        {
            return result;
        }

        Dictionary<long, List<long>> titleCities = new Dictionary<long, List<long>>();
        foreach (var cityData in saveData.cityExtraData)
        {
            if (cityData == null || cityData.title_id <= 0 || !cityById.ContainsKey(cityData.id))
            {
                continue;
            }

            if (!titleCities.TryGetValue(cityData.title_id, out var cities))
            {
                cities = new List<long>();
                titleCities[cityData.title_id] = cities;
            }

            cities.Add(cityData.id);
        }

        foreach (var pair in titleCities)
        {
            long capitalId = pair.Value.FirstOrDefault();
            cityById.TryGetValue(capitalId, out City capital);
            string name = capital?.SelectKingdomName();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = capital?.kingdom?.GetKingdomName() ?? capital?.GetCityName() ?? "";
            }

            result.Add(new KingdomTitleData
            {
                id = pair.Key,
                cities = pair.Value.Distinct().ToList(),
                title_capital = capitalId,
                province_name = capital?.GetCityName(),
                name = name,
                original_actor_asset = capital?.kingdom?.king?.asset?.id ?? capital?.kingdom?.asset?.id ?? capital?.getSpecies(),
                banner_background_id = capital?.kingdom?.data?.banner_background_id ?? 0,
                banner_icon_id = capital?.kingdom?.data?.banner_icon_id ?? 0,
                founder_kingdom_id = capital?.kingdom?.id ?? -1L,
                founder_kingdom_name = capital?.kingdom?.name
            });
        }

        return result;
    }

}

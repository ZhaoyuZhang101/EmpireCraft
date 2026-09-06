
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

namespace EmpireCraft.Scripts.Data;

public static class DataManager
{
    public static void LoadAll(string loadRootPath)
    {
        string loadPath = Path.Combine(loadRootPath, "EmpireCraftModData.json");
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
        var saveData = JsonConvert.DeserializeObject<SaveData>(json);
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
        LogService.LogInfo("准备各项数据");
        OfficeManager.Offices = saveData.officeObjects;
        // 批量同步
        foreach (var entry in saveData.actorsExtraData)
        {
            if (unitById.TryGetValue(entry.id, out Actor actor))
                actor.SyncData(entry);
        }
        LogService.LogInfo("Sync Actor Data");
        foreach (var entry in saveData.kingdomExtraData)
        {
            if (kingdomById.TryGetValue(entry.id, out var kingdom))
            {
                kingdom.SyncData(entry);
                if (kingdom.GetOfficeID() == -1L)
                {
                    var culture = ConfigData.speciesCulturePair.TryGetValue(kingdom.asset.id, out string speciesCulture)? speciesCulture : "Western";
                    RegimeType regimeType = OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting)
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
            if (cityById.TryGetValue(entry.id, out var city))
            {
                city.SyncData(entry);
                if (city.GetOfficeID() == -1L)
                {
                    Regime regime = city.kingdom.GetRegime();
                    if (regime != null)
                    {
                        CityType cityType = EmpireCraftKingdomBehCheckKingdomType.CalcCityType(city.kingdom);
                        BureauSetting citySetting = regime.bureau_config.cities[cityType];
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
            if (clanById.TryGetValue(entry.id, out var clan))
                clan.SyncData(entry);
        }
        LogService.LogInfo("Sync Clan Data");
        foreach (var entry in saveData.warExtraData)
        {
            if (warById.TryGetValue(entry.id, out var war))
                war.SyncData(entry);
        }
        LogService.LogInfo("Sync War Data");
        foreach (var entry in saveData.religionExtraData)
        {
            if (religionById.TryGetValue(entry.id, out var religion))
                religion.SyncData(entry);
        }
        LogService.LogInfo("Sync Religion Data");
        foreach (EmpireData empireData in saveData.empireDatas)
        {
            if (empireData == null) continue;
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
        ModClass.EMPIRE_MANAGER.update(-1L);
        LogService.LogInfo("Sync Empire Data");
        EmpireCoreManager.EmpireCores = saveData.empireCoreDatas?.Where(c => c != null).ToDictionary(c => c.id) ?? new Dictionary<long, EmpireCore>();

        List<KingdomTitleData> titleDatas = saveData.kingdomTitleDatas;
        if (titleDatas == null || titleDatas.Count == 0)
        {
            titleDatas = RebuildKingdomTitleDataFromCityData(saveData, cityById);
        }

        foreach (KingdomTitleData kingdomTitleData in titleDatas)
        {
            if (kingdomTitleData == null) continue;
            NormalizeKingdomTitleData(kingdomTitleData, saveData, cityById);
            KingdomTitle kt = new KingdomTitle();
            kt.loadData(kingdomTitleData);
            if (kt.getCities().Any())
            {
                kt.isBeenControlled();
            }
            ModClass.KINGDOM_TITLE_MANAGER.addObject(kt);

        }
        ModClass.KINGDOM_TITLE_MANAGER.update(-1L);
        SpecificClanManager._specificClans = saveData.specificClans;
        SpecificClanManager.RebuildCache();
        LogService.LogInfo("Sync Titles Data");
        ConfigData.yearNameSubspecies = saveData.yearNameSubspecies;
        LogService.LogInfo("Sync history Data");
        ModClass.ALL_HISTORY_DATA = saveData.all_history ?? new Dictionary<long, List<EmpireCraftHistory>>();
        PlayerConfig.dict["switch_real_num"].boolVal = saveData.switch_real_num;
        if (isOldSave)
        {
            foreach (var worldKingdom in World.world.kingdoms)
            {
                worldKingdom.InitialRegime();
                EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(worldKingdom);
            }
        }
    }
    public static void SaveAll(string saveRootPath)
    {
        string savePath = Path.Combine(saveRootPath, "EmpireCraftModData.json");
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
        saveData.specificClans = SpecificClanManager._specificClans;
        saveData.switch_real_num = ModClass.REAL_NUM_SWITCH;
        string json = JsonConvert.SerializeObject(saveData, Formatting.Indented);
        LogService.LogInfo("" + saveData.actorsExtraData.Count());
        LogService.LogInfo("" + saveData.warExtraData.Count());
        LogService.LogInfo("" + saveData.kingdomExtraData.Count());
        LogService.LogInfo("" + saveData.cityExtraData.Count());
        LogService.LogInfo("" + saveData.religionExtraData.Count());
        File.WriteAllText(savePath, json);
        LogService.LogInfo("Save Finished");
    }

    private static void NormalizeKingdomTitleData(KingdomTitleData data, SaveData saveData, Dictionary<long, City> cityById)
    {
        if (data == null)
        {
            return;
        }

        data.cities ??= new List<long>();
        HashSet<long> cityIds = new HashSet<long>();
        foreach (long cityId in data.cities)
        {
            if (cityById.ContainsKey(cityId))
            {
                cityIds.Add(cityId);
            }
        }

        if (cityIds.Count == 0 && saveData?.cityExtraData != null)
        {
            foreach (var cityData in saveData.cityExtraData)
            {
                if (cityData == null || cityData.title_id != data.id)
                {
                    continue;
                }

                if (cityById.ContainsKey(cityData.id))
                {
                    cityIds.Add(cityData.id);
                }
            }
        }

        if (cityIds.Count > 0)
        {
            data.cities = cityIds.ToList();
        }

        if ((data.title_capital <= 0 || !cityById.ContainsKey(data.title_capital)) && data.cities.Count > 0)
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

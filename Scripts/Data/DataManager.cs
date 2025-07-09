using System;
using System.IO;
using NeoModLoader.api;
using NeoModLoader.api.attributes;
using NeoModLoader.General.UI.Tab;
using UnityEngine;
using NeoModLoader.services;
using System.Reflection;
using EmpireCraft.Scripts.GamePatches;
using NeoModLoader.General;
using EmpireCraft.Scripts.UI;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Enums;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using static UnityEngine.EventSystems.EventTrigger;
using EmpireCraft.Scripts.Layer;
using System.Threading.Tasks;
using static EmpireCraft.Scripts.GameClassExtensions.ActorExtension;
using static EmpireCraft.Scripts.GameClassExtensions.CityExtension;
using static EmpireCraft.Scripts.GameClassExtensions.KingdomExtension;
using static EmpireCraft.Scripts.GameClassExtensions.ClanExtension;

namespace EmpireCraft.Scripts.Data;

public static class DataManager
{
    public static void LoadAll(string loadRootPath)
    {
        string loadPath = Path.Combine(loadRootPath, "EmpireCraftModData.json");
        PlayerConfig.dict["prevent_city_destroy"].boolVal = false;
        LogService.LogInfo(loadPath);
        if (!File.Exists(loadPath))
        {
            LogService.LogInfo("没有找到任何保存数据。");
            return;
        }
        var json = File.ReadAllText(loadPath);
        var saveData = JsonConvert.DeserializeObject<SaveData>(json);
        LogService.LogInfo("初始化模组数据模板");

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
        LogService.LogInfo("准备各项数据");

        // 批量同步
        foreach (var entry in saveData.actorsExtraData)
            if (unitById.TryGetValue(entry.id, out Actor actor))
                if(actor != null)
                    if(actor.isActor())
                    {
                        actor.syncData(entry);
                    }
        LogService.LogInfo("Sync Actor Data");
        foreach (var entry in saveData.kingdomExtraData)
            if (kingdomById.TryGetValue(entry.id, out var kingdom))
                kingdom.syncData(entry);
        LogService.LogInfo("Sync Kingdom Data");
        foreach (var entry in saveData.cityExtraData)
            if (cityById.TryGetValue(entry.id, out var city))
                city.syncData(entry);
        LogService.LogInfo("Sync City Data");
        foreach (var entry in saveData.clanExtraData)
            if (clanById.TryGetValue(entry.id, out var clan))
                clan.syncData(entry);
        LogService.LogInfo("Sync Clan Data");
        foreach (var entry in saveData.warExtraData)
            if (warById.TryGetValue(entry.id, out var war))
                war.syncData(entry);
        LogService.LogInfo("Sync War Data");
        foreach (EmpireData empireData in saveData.empireDatas)
        {
            if (empireData == null) continue;
            Empire empire = new Empire();
            empire.loadData(empireData);
            ModClass.EMPIRE_MANAGER.addObject(empire);
        }
        ModClass.EMPIRE_MANAGER.update(-1L);
        LogService.LogInfo("Sync Empire Data");
        foreach (KingdomTitleData kingdomTitleData in saveData.kingdomTitleDatas)
        {
            KingdomTitle kt = new KingdomTitle();
            kt.loadData(kingdomTitleData);
            kt.isBeenControlled();
            ModClass.KINGDOM_TITLE_MANAGER.addObject(kt);
        }
        ModClass.KINGDOM_TITLE_MANAGER.update(-1L);
        foreach (ProvinceData provinceData in saveData.provinceDatas)
        {
            Province p = new Province();
            p.loadData(provinceData);
            ModClass.PROVINCE_MANAGER.addObject(p);
        }
        ModClass.PROVINCE_MANAGER.update(-1L);
        LogService.LogInfo("Sync Titles Data");
        ConfigData.yearNameSubspecies = saveData.yearNameSubspecies;
        ConfigData.speciesCulturePair = saveData.speciesCulturePair;
        LogService.LogInfo("Sync history Data");
        ModClass.ALL_HISTORY_DATA = saveData.all_history ?? new Dictionary<long, List<EmpireCraftHistory>>();
        ConfigData.PREVENT_CITY_DESTROY = saveData.prevent_city_destroy;
        PlayerConfig.dict["prevent_city_destroy"].boolVal = saveData.prevent_city_destroy;

    }
    public static void SaveAll(string saveRootPath)
    {
        string savePath = Path.Combine(saveRootPath, "EmpireCraftModData.json");
        SaveData saveData = new SaveData();
        saveData.actorsExtraData = World.world.units.Select(a=>a.getExtraData(true)).Where(ed=>ed!=null).ToList();
        saveData.cityExtraData = World.world.cities.Select(a => a.getExtraData(true)).Where(ed => ed != null).ToList(); ;
        saveData.kingdomExtraData = World.world.kingdoms.Select(a => a.getExtraData(true)).Where(ed => ed != null).ToList(); ;
        saveData.warExtraData = World.world.wars.Select(a => a.getExtraData(true)).Where(ed => ed != null).ToList(); ;
        saveData.empireDatas = new List<EmpireData>(ModClass.EMPIRE_MANAGER.Count);
        saveData.kingdomTitleDatas = new List<KingdomTitleData>(ModClass.KINGDOM_TITLE_MANAGER.Count);
        saveData.provinceDatas = new List<ProvinceData>(ModClass.PROVINCE_MANAGER.Count);
        ModClass.EMPIRE_MANAGER.update(-1L);
        ModClass.KINGDOM_TITLE_MANAGER.update(-1L);
        foreach (Empire empire in ModClass.EMPIRE_MANAGER)
        {
            if (empire != null)
            {
                if (empire.data != null)
                {
                    empire.save();
                    saveData.empireDatas.Add(empire.data);
                }
            }
        }
        foreach (KingdomTitle kt in ModClass.KINGDOM_TITLE_MANAGER)
        {
            if (kt != null)
            {
                if (kt.data != null)
                {
                    kt.save();
                    saveData.kingdomTitleDatas.Add(kt.data);
                }
            }
        }
        foreach(Province p in ModClass.PROVINCE_MANAGER)
        {
            if (p != null)
            {
                if (p.data!= null)
                {
                    p.save();
                    saveData.provinceDatas.Add(p.data);
                }
            }
        }
        saveData.yearNameSubspecies = ConfigData.yearNameSubspecies;
        saveData.speciesCulturePair = ConfigData.speciesCulturePair;
        saveData.all_history = ModClass.ALL_HISTORY_DATA;
        saveData.prevent_city_destroy = ConfigData.PREVENT_CITY_DESTROY;
        string json = JsonConvert.SerializeObject(saveData, Formatting.Indented);
        LogService.LogInfo("" + saveData.actorsExtraData.Count());
        LogService.LogInfo("" + saveData.warExtraData.Count());
        LogService.LogInfo("" + saveData.kingdomExtraData.Count());
        LogService.LogInfo("" + saveData.cityExtraData.Count());
        File.WriteAllText(savePath, json);
        LogService.LogInfo("Save Finished");
    }
}
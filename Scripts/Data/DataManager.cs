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
        LogService.LogInfo(loadPath);
        if (!File.Exists(loadPath))
        {
            LogService.LogInfo("没有找到任何保存数据。");
            return;
        }
        
        var json = File.ReadAllText(loadPath);
        var saveData = JsonConvert.DeserializeObject<SaveData>(json);


        if (saveData == null || saveData.actorsExtraData == null || saveData.actorsExtraData.Count == 0)
        {
            LogService.LogInfo("没有找到任何保存数据。");
            return;
        }
        var unitById = World.world.units.ToDictionary(u => u.getID());
        var kingdomById = World.world.kingdoms.ToDictionary(k => k.getID());
        var cityById = World.world.cities.ToDictionary(c => c.getID());
        var clanById = World.world.clans.ToDictionary(c => c.getID());
        var warById = World.world.wars.ToDictionary(w => w.getID());

        // 批量同步
        foreach (var entry in saveData.actorsExtraData)
            if (unitById.TryGetValue(entry.id, out var actor))
                if(actor.isActor())
                {
                    actor.syncData(entry);
                }

        foreach (var entry in saveData.kingdomExtraData)
            if (kingdomById.TryGetValue(entry.id, out var kingdom))
                kingdom.syncData(entry);

        foreach (var entry in saveData.cityExtraData)
            if (cityById.TryGetValue(entry.id, out var city))
                city.syncData(entry);

        foreach (var entry in saveData.clanExtraData)
            if (clanById.TryGetValue(entry.id, out var clan))
                clan.syncData(entry);

        foreach (var entry in saveData.warExtraData)
            if (warById.TryGetValue(entry.id, out var war))
                war.syncData(entry);

        foreach (EmpireData empireData in saveData.empireDatas)
        {
            Empire empire = new Empire();
            empire.loadData(empireData);
            ModClass.EMPIRE_MANAGER.addObject(empire);
        }

        foreach (KingdomTitleData kingdomTitleData in saveData.kingdomTitleDatas)
        {
            KingdomTitle kt = new KingdomTitle();
            kt.loadData(kingdomTitleData);
            ModClass.KINGDOM_TITLE_MANAGER.addObject(kt);
        }
        ConfigData.yearNameSubspecies = saveData.yearNameSubspecies;
        ConfigData.speciesCulturePair = saveData.speciesCulturePair;
        ModClass.IS_CLEAR = false;


    }
    public static void SaveAll(string saveRootPath)
    {
        string savePath = Path.Combine(saveRootPath, "EmpireCraftModData.json");
        Task.Run(() =>
        {
            SaveData saveData = new SaveData();
            saveData.actorsExtraData = World.world.units.Select(a=>a.getExtraData()).ToList();
            saveData.cityExtraData = World.world.cities.Select(a => a.getExtraData()).ToList(); ;
            saveData.kingdomExtraData = World.world.kingdoms.Select(a => a.getExtraData()).ToList(); ;
            saveData.warExtraData = World.world.wars.Select(a => a.getExtraData()).ToList(); ;
            saveData.empireDatas = new List<EmpireData>(ModClass.EMPIRE_MANAGER.Count);
            saveData.kingdomTitleDatas = new List<KingdomTitleData>(ModClass.KINGDOM_TITLE_MANAGER.Count);
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
            saveData.yearNameSubspecies = ConfigData.yearNameSubspecies;
            saveData.speciesCulturePair = ConfigData.speciesCulturePair;

            string json = JsonConvert.SerializeObject(saveData, Formatting.Indented);

            File.WriteAllText(savePath, json);
            LogService.LogInfo("存档完成");
        });
        
    }
}
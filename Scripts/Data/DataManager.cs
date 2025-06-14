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

namespace EmpireCraft.Scripts.Data;

public static class DataManager
{
    public static void LoadAll(string loadRootPath)
    {
        string loadPath = Path.Combine(loadRootPath, "modData.json");
        LogService.LogInfo(loadPath);
        if (!File.Exists(loadPath))
        {
            return;
        }
        
        var json = File.ReadAllText(loadPath);
        var saveData = JsonConvert.DeserializeObject<SaveData>(json);
        int restored = 0;
        if (saveData == null || saveData.actorsExtraData == null || saveData.actorsExtraData.Count == 0)
        {
            LogService.LogInfo("没有找到任何保存数据。");
            return;
        }
        //加载角色数据
        foreach (ExtraActorData entry in saveData.actorsExtraData)
        {
            Actor[] actors = World.world.units.Where(a => a.isActor() && a.getID() == entry.actorId).ToArray();
            if (actors != null && actors.Length > 0)
            {
                actors[0].SetPeeragesLevel(entry.peerage);
                restored++;
            }
        }
        //加载王国数据
        foreach (ExtraKingdomData entry in saveData.kingdomExtraData)
        {
            Kingdom[] kingdoms = World.world.kingdoms.Where(a => a.getID() == entry.kingdomId).ToArray();
            if (kingdoms != null && kingdoms.Length > 0)
            {
                kingdoms[0].SetCountryLevel(entry.kingdomLevel);
                kingdoms[0].SetVassaledKingdomID(entry.vassaled_kingdom_id);
                kingdoms[0].SetEmpireID(entry.empire_id);
                kingdoms[0].SetTimestampEmpire(entry.timestamp_empire);
                restored++;
            }
        }

        //加载城市数据
        foreach (ExtraCityData entry in saveData.cityExtraData)
        {
            City[] cities = World.world.cities.Where(a => a.getID() == entry.cityId).ToArray();
            if (cities != null && cities.Length > 0)
            {
                cities[0].SetKingdomNames(entry.kingdomNames);
                restored++;
            }
        }

        foreach (EmpireData empireData in saveData.empireDatas)
        {
            Empire empire = new Empire();
            empire.loadData(empireData);
            ModClass.EMPIRE_MANAGER.addObject(empire);
        }

        
    }
    public static void SaveAll(string saveRootPath)
    {
        string savePath = Path.Combine(saveRootPath, "modData.json");
        SaveData saveData = new SaveData();
        saveData.actorsExtraData = new List<ExtraActorData>(World.world.units.Count);
        saveData.cityExtraData = new List<ExtraCityData>(World.world.cities.Count);
        saveData.empireDatas = new List<EmpireData>(ModClass.EMPIRE_MANAGER.Count);
        foreach (Actor actor in World.world.units.Where(a => a.isActor()))
        {
        var lvl = actor.GetPeeragesLevel();
            ExtraActorData extraData = new ExtraActorData()
            {
                actorId = actor.getID(),
                peerage = lvl
            };
            saveData.actorsExtraData.Add(extraData);
        }

        foreach (Kingdom kingdom in World.world.kingdoms)
        {
        var lvl = kingdom.GetCountryLevel();
            ExtraKingdomData extraData2 = new ExtraKingdomData()
            {
                kingdomId = kingdom.getID(),
                kingdomLevel = lvl,
                vassaled_kingdom_id = kingdom.GetVassaledKingdomID(),
                empire_id = kingdom.GetEmpireID(),
                timestamp_empire = kingdom.GetTimestampEmpire()
            };
            saveData.kingdomExtraData.Add(extraData2);
            
        }
        foreach (City city in World.world.cities)
        {
            var lvl = city.GetKingdomNames;
            ExtraCityData extraData3 = new ExtraCityData()
            {
                cityId = city.getID(),
                kingdomNames = city.GetKingdomNames()
            };
            saveData.cityExtraData.Add(extraData3);

        }
        List<Empire> empireToRemove = new List<Empire>();
        foreach (Empire empire in ModClass.EMPIRE_MANAGER)
        {
            if (empire.data != null)
            {
                empire.save();
                saveData.empireDatas.Add(empire.data);
            }
        }

        string json = JsonConvert.SerializeObject(saveData, Formatting.Indented);
        Task.Run(() =>
        {
            File.WriteAllText(savePath, json);
            LogService.LogInfo("存档完成");
        });
        
    }
}
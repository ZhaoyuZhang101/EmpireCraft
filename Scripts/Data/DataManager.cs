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
        LogService.LogInfo("同步角色数据");
        foreach (var entry in saveData.kingdomExtraData)
            if (kingdomById.TryGetValue(entry.id, out var kingdom))
                kingdom.syncData(entry);
        LogService.LogInfo("同步国家数据");
        foreach (var entry in saveData.cityExtraData)
            if (cityById.TryGetValue(entry.id, out var city))
                city.syncData(entry);
        LogService.LogInfo("同步城市数据");
        foreach (var entry in saveData.clanExtraData)
            if (clanById.TryGetValue(entry.id, out var clan))
                clan.syncData(entry);
        LogService.LogInfo("同步氏族数据");
        foreach (var entry in saveData.warExtraData)
            if (warById.TryGetValue(entry.id, out var war))
                war.syncData(entry);
        LogService.LogInfo("同步战争数据");
        foreach (EmpireData empireData in saveData.empireDatas)
        {
            Empire empire = new Empire();
            empire.loadData(empireData);
            ModClass.EMPIRE_MANAGER.addObject(empire);
        }
        LogService.LogInfo("同步帝国数据");
        foreach (KingdomTitleData kingdomTitleData in saveData.kingdomTitleDatas)
        {
            KingdomTitle kt = new KingdomTitle();
            kt.loadData(kingdomTitleData);
            ModClass.KINGDOM_TITLE_MANAGER.addObject(kt);
        }
        LogService.LogInfo("同步法理数据");
        ConfigData.yearNameSubspecies = saveData.yearNameSubspecies;
        ConfigData.speciesCulturePair = saveData.speciesCulturePair;

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
        saveData.yearNameSubspecies = ConfigData.yearNameSubspecies;
        saveData.speciesCulturePair = ConfigData.speciesCulturePair;

        string json = JsonConvert.SerializeObject(saveData, Formatting.Indented);
        LogService.LogInfo("" + saveData.actorsExtraData.Count());
        LogService.LogInfo("" + saveData.warExtraData.Count());
        LogService.LogInfo("" + saveData.kingdomExtraData.Count());
        LogService.LogInfo("" + saveData.cityExtraData.Count());
        File.WriteAllText(savePath, json);
        LogService.LogInfo("存档完成");
    }
}
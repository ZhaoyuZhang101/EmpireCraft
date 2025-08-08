
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
using EmpireCraft.Scripts.HelperFunc;

namespace EmpireCraft.Scripts.Data;

public static class DataManager
{
    public static void LoadAll(string loadRootPath)
    {
        string loadPath = Path.Combine(loadRootPath, "EmpireCraftModData.json");
        PlayerConfig.dict["prevent_city_destroy"].boolVal = false;
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
        {
            if (unitById.TryGetValue(entry.id, out Actor actor))
                actor.SyncData<Actor, ActorExtraData>(entry);
        }
        LogService.LogInfo("Sync Actor Data");
        foreach (var entry in saveData.cityExtraData)
        {
            if (cityById.TryGetValue(entry.id, out var city))
                city.SyncData<City, CityExtraData>(entry);
        }
        LogService.LogInfo("Sync City Data");
        foreach (var entry in saveData.kingdomExtraData)
        {
            if (kingdomById.TryGetValue(entry.id, out var kingdom))
                kingdom.SyncData<Kingdom, KingdomExtraData>(entry);
        }
        LogService.LogInfo("Sync Kingdom Data");
        foreach (var entry in saveData.clanExtraData)
        {
            if (clanById.TryGetValue(entry.id, out var clan))
                clan.SyncData<Clan, ClanExtraData>(entry);
        }
        LogService.LogInfo("Sync Clan Data");
        foreach (var entry in saveData.warExtraData)
        {
            if (warById.TryGetValue(entry.id, out var war))
                war.SyncData<War, WarExtraData>(entry);
        }
        LogService.LogInfo("Sync War Data");

        foreach (ModLayerData pModLayerData in saveData.pModLayerDatas)
        {
            ModLayer p = new ModLayer();
            p.loadData(pModLayerData);
            ModClass.ModLayer_MANAGER.addObject(p);
        }
        ModClass.ModLayer_MANAGER.update(-1L);
    }
    public static void SaveAll(string saveRootPath)
    {
        string savePath = Path.Combine(saveRootPath, "EmpireCraftModData.json");
        SaveData saveData = new SaveData();
        saveData.actorsExtraData = World.world.units.Select(a=>a.GetExtraData<Actor, ActorExtraData>(true)).Where(ed=>ed!=null).ToList();
        saveData.cityExtraData = World.world.cities.Select(a => a.GetExtraData<City, CityExtraData>(true)).Where(ed => ed != null).ToList(); ;
        saveData.kingdomExtraData = World.world.kingdoms.Select(a => a.GetExtraData<Kingdom, KingdomExtraData>(true)).Where(ed => ed != null).ToList(); ;
        saveData.warExtraData = World.world.wars.Select(a => a.GetExtraData<War, WarExtraData>(true)).Where(ed => ed != null).ToList(); ;
        saveData.clanExtraData = World.world.clans.Select(a => a.GetExtraData<Clan, ClanExtraData>(true)).Where(ed => ed != null).ToList(); ;
        
        foreach(ModLayer pModLayer in ModClass.ModLayer_MANAGER)
        {
            try
            {
                pModLayer.save();
                saveData.pModLayerDatas.Add(pModLayer.data);
            } catch
            {
                LogService.LogInfo("存在模组层级数据出错，跳过该存档");
            }

        }
        string json = JsonConvert.SerializeObject(saveData, Formatting.Indented);
        LogService.LogInfo("" + saveData.actorsExtraData.Count());
        LogService.LogInfo("" + saveData.warExtraData.Count());
        LogService.LogInfo("" + saveData.kingdomExtraData.Count());
        LogService.LogInfo("" + saveData.cityExtraData.Count());
        File.WriteAllText(savePath, json);
        LogService.LogInfo("Save Finished");
    }

}
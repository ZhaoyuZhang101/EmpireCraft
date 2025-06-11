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
using ExampleMod.UI;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Enums;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;

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
        foreach (ExtraActordata entry in saveData.actorsExtraData)
        {
            Actor[] actors = World.world.units.Where(a => a.isActor() && a.getID() == entry.actorId).ToArray();
            if (actors != null && actors.Length > 0)
            {
                actors[0].SetPeeragesLevel(entry.peerage);
                restored++;
            }
        }
    }
    public static void SaveAll(string saveRootPath)
    {
        string savePath = Path.Combine(saveRootPath, "modData.json");
        SaveData saveData = new SaveData();
        foreach (Actor actor in World.world.units.Where(a => a.isActor()))  // 或者你自行的遍历方式
        {
        var lvl = actor.GetPeeragesLevel();
            LogService.LogInfo($"Saving actor {actor.getID()} with Peerage {lvl}");
            ExtraActordata extraData = new ExtraActordata()
            {
                actorId = actor.getID(),
                peerage = lvl
            };
            LogService.LogInfo($"Actor {actor.getID()} Peerage: {extraData.peerage}");
            saveData.actorsExtraData.Add(extraData);
            
        }
        LogService.LogInfo($"准备保存数据到 {savePath}，包含 {saveData.actorsExtraData.Count} 个角色的额外数据。");
        foreach (ExtraActordata entry in saveData.actorsExtraData)
        {
            LogService.LogInfo($"Actor ID: {entry.actorId}, Peerage: {entry.peerage}");
        }
        string json = JsonConvert.SerializeObject(saveData, Formatting.Indented);
        LogService.LogInfo($"保存数据到 {savePath}，内容：{json}");
        File.WriteAllText(savePath, json);
        LogService.LogInfo($"保存数据到 {savePath} 成功。");
    }
}
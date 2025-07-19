using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Layer;
using db;
using System.Collections;
using UnityEngine;
using Newtonsoft.Json;
using System.IO;

namespace EmpireCraft.Scripts.GamePatches;
public class SaveManagerPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(save_mod_data)).Patch(
            AccessTools.Method(typeof(SaveManager), nameof(SaveManager.saveMapData)),
            prefix: new HarmonyMethod(GetType(), nameof(save_mod_data))
        );       
        new Harmony(nameof(loadActors)).Patch(
            AccessTools.Method(typeof(SaveManager), nameof(SaveManager.loadActors)),
            postfix: new HarmonyMethod(GetType(), nameof(loadActors))
        );        
        new Harmony(nameof(load_mod_data)).Patch(
            AccessTools.Method(typeof(SaveManager), nameof(SaveManager.loadData)),
            postfix: new HarmonyMethod(GetType(), nameof(load_mod_data))
        );        
        new Harmony(nameof(clear_data)).Patch(
            AccessTools.Method(typeof(MapBox), nameof(MapBox.startTheGame)),
            prefix: new HarmonyMethod(GetType(), nameof(clear_data))
        );         
        new Harmony(nameof(last_gc)).Patch(
            AccessTools.Method(typeof(MapBox), nameof(MapBox.lastGC)),
            postfix: new HarmonyMethod(GetType(), nameof(last_gc))
        );        
    }
    public static void loadActors(SaveManager __instance)
    {
        ActorPatch.startSessionMonth = Date.getMonthsSince(World.world.getCurSessionTime());
        ActorPatch.isReadyToSet = true;
    }

    public static void last_gc(MapBox __instance)
    {
        ModClass.IS_CLEAR = false;
    }
    public static void clear_data(MapBox __instance, bool pForceGenerate)
    {
        ModClass.IS_CLEAR = true;
        DBManagerPatch.AllClear();
    }

    public static bool save_mod_data(SaveManager __instance, string pFolder, bool pCompress)
    {
        if (ModClass.SAVE_FREEZE)
        {
            LogService.LogInfo("Mod数据保存被冻结，无法保存");
            return false;
        }
        DataManager.SaveAll(pFolder);
        LogService.LogInfo("保存mod数据到 " + pFolder);

        if (string.IsNullOrEmpty(pFolder))
        {
            LogService.LogError("保存路径为空，无法保存mod数据");
            return true;
        }
        return true;

    }    
    public static void load_mod_data(SaveManager __instance, SavedMap pData, string pPath)
    {
        ModClass.IS_CLEAR = true;
        ActorPatch.startSessionMonth = Date.getMonthsSince(World.world.getCurSessionTime());
        ActorPatch.isReadyToSet = false;
        ModClass.EMPIRE_MANAGER = new EmpireManager();
        ModClass.KINGDOM_TITLE_MANAGER = new KingdomTitleManager();
        ModClass.PROVINCE_MANAGER = new ProvinceManager();

        LogService.LogInfo("加载mod数据从 " + pPath);
        if (pData == null)
        {
            LogService.LogError("数据为空，无法加载mod数据");
            return;
        }
        SmoothLoader.add(delegate
        {
            try
            {
                DataManager.LoadAll(pPath);
                LogService.LogInfo("mod数据加载成功");
            }
            catch (Exception ex)
            {
                LogService.LogError("加载mod数据失败: " + ex.ToString());
            }
        }, "LOADING EMPIRE MOD DATA", false, 0.001f);
        ModClass.IS_CLEAR = false;
    }
}

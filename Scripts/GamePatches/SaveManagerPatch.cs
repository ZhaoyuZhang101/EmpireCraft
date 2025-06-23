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

namespace EmpireCraft.Scripts.GamePatches;
public class SaveManagerPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(save_mod_data)).Patch(
            AccessTools.Method(typeof(SaveManager), nameof(SaveManager.saveMapData)),
            postfix: new HarmonyMethod(GetType(), nameof(save_mod_data))
        );        
        new Harmony(nameof(load_mod_data)).Patch(
            AccessTools.Method(typeof(SaveManager), nameof(SaveManager.loadData)),
            postfix: new HarmonyMethod(GetType(), nameof(load_mod_data))
        );        
    }

    public static void save_mod_data(SaveManager __instance, string pFolder, bool pCompress)
    {
        LogService.LogInfo("保存mod数据到 " + pFolder);
        if (string.IsNullOrEmpty(pFolder))
        {
            LogService.LogError("保存路径为空，无法保存mod数据");
            return;
        }
        DataManager.SaveAll(pFolder);

    }    
    public static void load_mod_data(SaveManager __instance, SavedMap pData, string pPath)
    {
        ModClass.EMPIRE_MANAGER = new EmpireManager();
        ModClass.KINGDOM_TITLE_MANAGER = new KingdomTitleManager();
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
                LogService.LogError("加载mod数据失败: " + ex.Message);
            }
        }, "LOADING EMPIRE MOD DATA", false, 0.001f);
    }
}

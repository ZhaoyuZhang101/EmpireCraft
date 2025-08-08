using NeoModLoader.api;
using UnityEngine;
using NeoModLoader.services;
using System;
using System.Reflection;
using EmpireCraft.Scripts.GamePatches;
using NeoModLoader.General;
using System.IO;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.Data;
using System.Collections.Generic;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.GameLibrary;
using System.Linq;
using EmpireCraft.Scripts.AI;
using EmpireCraft.Scripts.GodPowers;
using Newtonsoft.Json;

namespace EmpireCraft.Scripts;
public class ModClass : MonoBehaviour, IMod, IReloadable, ILocalizable, IConfigurable
{
    public static Transform prefab_library;
    public static bool IS_CLEAR = true;
    public static ModLayerManager ModLayer_MANAGER;
    public static ModMapMode CURRENT_MAP_MOD;
    public static ModDeclare _declare;
    private GameObject _ModObject;
    public static ModConfig modConfig;
    public ModDeclare GetDeclaration()
    {
        return _declare;
    }

    void Start ()
    {

        IS_CLEAR = false;
    }

    public GameObject GetGameObject()
    {
        return _ModObject;
    }
    public string GetUrl()
    {
        return "https://github.com/ZhaoyuZhang101/EmpireCraft";
    }

    public void OnLoad(ModDeclare modDeclare, GameObject gameObject)
    {
        _declare = modDeclare;
        _ModObject = gameObject;
        Config.isEditor = true; // Set this to true if you want to enable editor mode for your mod
        LogService.LogInfo("SampleMod Load Finished！！");
        //加载文化名称模板
        LM.ApplyLocale(); // Apply the loaded locales to the game
        Type[] types = Assembly.GetExecutingAssembly().GetTypes();
        foreach (Type type in types)
        {
            if (type.GetInterface(nameof(GamePatch)) != null)
            {
                try
                {
                    GamePatch patch = (GamePatch)type.GetConstructor(new Type[] { }).Invoke(new object[] { });
                    patch.declare = _declare;
                    patch.Initialize();
                }
                catch (Exception e)
                {
                    LogService.LogWarning("Failed to initialize patch: " + type.Name);
                    LogService.LogWarning(e.ToString());
                }
            }
        }

        prefab_library = new GameObject("PrefabLibrary").transform;
        prefab_library.SetParent(transform);
        LoadUI();
        modConfig = new ModConfig(_declare.FolderPath + "/default_config.json", true);
        LogService.LogInfo("加载模组更多世界提示");
        EmpireCraftWorldLogLibrary.init();
        EmpireCraftNamePlateLibrary.init();
        EmpireCraftMetaTypeLibrary.init();
        EmpireCraftBehaviourTaskKingdomLibrary.init();
        EmpireCraftActorTraitGroupLibrary.init();
        EmpireCraftTooltipLibrary.init();
        ModOpinionAddition.init();
        ModPlotsAddition.init();
        EmpireCraftQuantumSpriteLibrary.init();
        World.world._list_meta_main_managers.Add(ModLayer_MANAGER = new ModLayerManager());
        World.world.list_all_sim_managers.Add(ModLayer_MANAGER);
        CURRENT_MAP_MOD = ModMapMode.None;
    }

    public void LoadUI()
    {
        MainTab.Init();
        LogService.LogInfo("ModTemplateUI Load Finish！！");
    }


    public void Reload()
    {
        LogService.LogInfo("SampleMod Reload Finish！！");
        
        LM.ApplyLocale();
        // You can reload your mod here, such as reloading configs, reloading UI, etc.
    }

    public string GetLocaleFilesDirectory(ModDeclare pModDeclare)
    {
        return pModDeclare.FolderPath + "/Locales/"; // Return the directory where your mod's locale files are located
    }

    public ModConfig GetConfig()
    {
        return modConfig;
    }
}
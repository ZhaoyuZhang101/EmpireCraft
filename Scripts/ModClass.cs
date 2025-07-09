using NeoModLoader.api;
using NeoModLoader.api.attributes;
using NeoModLoader.General.UI.Tab;
using UnityEngine;
using NeoModLoader.services;
using System;
using System.Reflection;
using EmpireCraft.Scripts.GamePatches;
using NeoModLoader.General;
using System.IO;
using EmpireCraft.Scripts.Layer;
using static UnityEngine.Random;
using EmpireCraft.Scripts.UI;
using UnityEngine.PlayerLoop;
using EmpireCraft.Scripts.AI;
using db;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GodPowers;
using EmpireCraft.Scripts.Data;
using System.Collections.Generic;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.GameLibrary;
using System.Linq;

namespace EmpireCraft.Scripts;
public class ModClass : MonoBehaviour, IMod, IReloadable, ILocalizable, IConfigurable
{
    public static bool SAVE_FREEZE = false;
    public static Transform prefab_library;
    public static bool IS_CLEAR = true;
    public static EmpireManager EMPIRE_MANAGER;
    public static KingdomTitleManager KINGDOM_TITLE_MANAGER;
    public static ProvinceManager PROVINCE_MANAGER;
    public static bool KINGDOM_TITLE_FREEZE = false;
    public static Empire selected_empire = null;
    public static MetaTypeAsset EMPIRE_METATYPE_ASSET;
    public static EmpireCraftMapMode CURRENT_MAP_MOD;
    public static int TITLE_BEEN_DESTROY_TIME = 50;
    public static ModDeclare _declare;
    private GameObject _modObject;
    public static ModConfig modConfig;
    public static Dictionary<long, List<EmpireCraftHistory>> ALL_HISTORY_DATA = new Dictionary<long, List<EmpireCraftHistory>>();
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
        return _modObject;
    }
    public string GetUrl()
    {
        return "https://github.com/ZhaoyuZhang101/EmpireCraft";
    }

    public void loadCultureNameTemplate()
    {
        foreach (string cultureName in ConfigData.speciesCulturePair.Values)
        {
            string culturesPath = Path.Combine(_declare.FolderPath, "Locales", "Cultures", $"Culture_{cultureName}");
            if (!Directory.Exists(culturesPath))
            {
                return;
            }
            var dirs = Directory.EnumerateFiles(culturesPath, "*.csv", SearchOption.AllDirectories)
            .ToList();
            foreach (var dir in dirs) 
            {
                LogService.LogInfo(dir);
                LM.LoadLocales(dir);
            }
            LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "Cultures", "ProvinceLevel", cultureName + "ProvinceLevel.csv"));
            LogService.LogInfo("Add culture template: " + cultureName);
        }
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "Cultures", "CountryLevelNames.csv"));
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "Cultures", "YearName1.csv"));
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "Cultures", "YearName2.csv"));
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "Cultures", "MiaoHaoPrefixes.csv"));
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "Cultures", "MiaoHaoSuffixes.csv"));
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "Cultures", "ShiHao.csv"));
        LogService.LogInfo("add year name template");
        LogService.LogInfo("加载谥号模板");
        LogService.LogInfo("加载庙号模板");
    }

    public void OnLoad(ModDeclare modDeclare, GameObject gameObject)
    {
        _declare = modDeclare;
        _modObject = gameObject;
        Config.isEditor = true; // Set this to true if you want to enable editor mode for your mod
        LogService.LogInfo("EmpireCraft Load Finished！！");
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "PeeragesLevelNames.csv"));
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "HonoraryOfficial.csv"));
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "MeritLevel.csv"));
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "OfficialType.csv"));
        //加载文化名称模板
        loadCultureNameTemplate();
        LM.ApplyLocale(); // Apply the loaded locales to the game
        Type[] patchTypes = Assembly.GetExecutingAssembly().GetTypes();
        foreach (Type type in patchTypes)
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

        LogService.LogInfo("加载帝国模组更多看法");
        EmpireCraftOpinionAddition.init();
        LogService.LogInfo("加载帝国模组更多政策行为");
        EmpireCraftPlotsAddition.init();
        LogService.LogInfo("加载帝国模组更多世界提示");
        EmpireCraftWorldLogLibrary.init();
        LogService.LogInfo("加载帝国模组名牌");
        EmpireCraftNamePlateLibrary.init();
        LogService.LogInfo("加载帝国模组UI渲染");
        EmpireCraftQuantumSpriteLibrary.init();
        LogService.LogInfo("加载帝国模组地图层级渲染");
        EmpireCraftMetaTypeLibrary.init();
        LogService.LogInfo("加载帝国模组更多提示");
        EmpireCraftTooltipLibrary.init();
        EmpireCraftHistoryDataLibrary.init();
        ActorTraitLibraryExtension.init();
        ActorTraitGroupLibraryExtension.init();
        World.world._list_meta_main_managers.Add(EMPIRE_MANAGER = new EmpireManager());
        World.world._list_meta_main_managers.Add(KINGDOM_TITLE_MANAGER = new KingdomTitleManager());
        World.world._list_meta_main_managers.Add(PROVINCE_MANAGER = new ProvinceManager());
        World.world.list_all_sim_managers.Add(EMPIRE_MANAGER);
        World.world.list_all_sim_managers.Add(KINGDOM_TITLE_MANAGER);
        World.world.list_all_sim_managers.Add(PROVINCE_MANAGER);
        CURRENT_MAP_MOD = EmpireCraftMapMode.None;
        //PlayerConfig.dict["map_province_layer"].boolVal = false;
        PlayerConfig.dict["map_kingdom_layer"].boolVal = false;
        PlayerConfig.dict["map_title_layer"].boolVal = false;
        OnomasticsRule.ReadSetting();
    }

    public void LoadUI()
    {
        BeaurauSystem.init();
        MainTab.Init();
        LogService.LogInfo("EmpireCraftUI Load Finish！！");
    }


    public void Reload()
    {
        LogService.LogInfo("EmpireCraft Reload Finish！！");
        
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "PeeragesLevelNames.csv"));
        loadCultureNameTemplate();
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
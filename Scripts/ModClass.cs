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
using EmpireCraft.Scripts.TipAndLog;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GodPowers;
using EmpireCraft.Scripts.Data;
using System.Collections.Generic;

namespace EmpireCraft.Scripts;
public class ModClass : MonoBehaviour, IMod, IReloadable, ILocalizable, IConfigurable
{
    public static bool SAVE_FREEZE = false;
    public static Transform prefab_library;
    public static string ORC_CULTURE = "Youmu";
    public static string HUMAN_CULTURE = "Huaxia";
    public static string ELF_CULTURE = "Western";
    public static string DWARF_CULTURE = "Youmu";
    public static string OTHER_CULTURE = "Other";
    public static bool IS_CLEAR = true;
    public static EmpireManager EMPIRE_MANAGER;
    public static KingdomTitleManager KINGDOM_TITLE_MANAGER;
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
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales","Cultures","CountryLevelNames.csv"));
        foreach (string cultureName in ConfigData.speciesCulturePair.Values)
        {
            LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "Cultures", "Culture_" + cultureName, cultureName + "FamilyNames.csv"));
            LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "Cultures", "Culture_" + cultureName, cultureName + "CountryNames.csv"));
            LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "Cultures", "Culture_" + cultureName, cultureName + "CityNames1.csv"));
            LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "Cultures", "Culture_" + cultureName, cultureName + "CityNames2.csv"));
            LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "Cultures", "Culture_" + cultureName, cultureName + "UnitNamesMale1.csv"));
            LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "Cultures", "Culture_" + cultureName, cultureName + "UnitNamesMale2.csv"));
            LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "Cultures", "Culture_" + cultureName, cultureName + "UnitNamesFemale1.csv"));
            LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "Cultures", "Culture_" + cultureName, cultureName + "UnitNamesFemale2.csv"));
            LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "Cultures", "Culture_" + cultureName, cultureName + "ReligionNames.csv"));
            LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "Cultures", "ProvinceLevel", cultureName + "ProvinceLevel.csv"));
            LogService.LogInfo("加载文化名称模板: " + cultureName);
        }
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "Cultures", "YearName1.csv"));
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "Cultures", "YearName2.csv"));
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "Cultures", "MiaoHaoPrefixes.csv"));
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "Cultures", "MiaoHaoSuffixes.csv"));
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "Cultures", "ShiHao.csv"));
        LogService.LogInfo("加载年号模板");
        LogService.LogInfo("加载谥号模板");
        LogService.LogInfo("加载庙号模板");
    }

    public void OnLoad(ModDeclare modDeclare, GameObject gameObject)
    {
        _declare = modDeclare;
        _modObject = gameObject;
        Config.isEditor = true; // Set this to true if you want to enable editor mode for your mod
        LogService.LogInfo("帝国模组加载成功！！");
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "PeeragesLevelNames.csv"));
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
        World.world._list_meta_main_managers.Add(EMPIRE_MANAGER = new EmpireManager());
        World.world._list_meta_main_managers.Add(KINGDOM_TITLE_MANAGER = new KingdomTitleManager());
        World.world.list_all_sim_managers.Add(EMPIRE_MANAGER);
        World.world.list_all_sim_managers.Add(KINGDOM_TITLE_MANAGER);
    }

    public void LoadUI()
    {
        MainTab.Init();
        LogService.LogInfo("帝国模组UI加载成功！！");
    }


    public void Reload()
    {
        LogService.LogInfo("帝国模组重新加载成功！！");
        
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
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

namespace EmpireCraft.Scripts;
public class ModClass : MonoBehaviour, IMod, IReloadable, ILocalizable
{
    public static string ORC_CULTURE = "Youmu";
    public static string HUMAN_CULTURE = "Huaxia";
    public static string ELF_CULTURE = "Western";
    public static string DWARF_CULTURE = "Youmu";
    public static string OTHER_CULTURE = "Other";
    public static bool IS_CLEAR = false;
    public static EmpireManager EMPIRE_MANAGER;
    public static KingdomTitleManager KINGDOM_TITLE_MANAGER;
    public static Empire selected_empire = null;
    public static MetaTypeAsset EMPIRE_METATYPE_ASSET;
    public static ModDeclare _declare;
    private GameObject _modObject;
    public ModDeclare GetDeclaration()
    {
        return _declare;
    }

    void Update ()
    {

    }

    void OnDestroy() 
    {
    }

    void Start ()
    {

    }

    public GameObject GetGameObject()
    {
        return _modObject;
    }
    public string GetUrl()
    {
        return "https://github.com/ZhaoyuZhang101/EmpireCraft";
    }

    public void loadCultureNameTemplate(string cultureName)
    {
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", cultureName, cultureName + "FamilyNames.csv"));
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", cultureName, cultureName + "CountryNames.csv"));
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", cultureName, cultureName + "CityNames1.csv"));
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", cultureName, cultureName + "CityNames2.csv"));
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", cultureName, cultureName + "UnitNamesMale1.csv"));
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", cultureName, cultureName + "UnitNamesMale2.csv"));
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", cultureName, cultureName + "UnitNamesFemale1.csv"));
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", cultureName, cultureName + "UnitNamesFemale2.csv"));
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", cultureName, cultureName + "ReligionNames.csv"));

        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "ProvinceLevel", cultureName + "ProvinceLevel.csv"));

        LogService.LogInfo("加载文化名称模板: " + cultureName);
    }

    public void OnLoad(ModDeclare modDeclare, GameObject gameObject)
    {
        _declare = modDeclare;
        _modObject = gameObject;
        Config.isEditor = true; // Set this to true if you want to enable editor mode for your mod
        LogService.LogInfo("帝国模组加载成功！！");
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "PeeragesLevelNames.csv"));
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "CountryLevelNames.csv"));
        //加载文化名称模板
        loadCultureNameTemplate("Huaxia");
        loadCultureNameTemplate("Western");
        loadCultureNameTemplate("Youmu");
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
        if (ModClass.EMPIRE_MANAGER==null)
        {
            ModClass.EMPIRE_MANAGER = new EmpireManager();
        }
        LoadUI();
        LogService.LogInfo("加载帝国模组更多看法");
        EmpireCraftOpinionAddition.init();
        LogService.LogInfo("加载帝国模组更多政策行为");
        EmpireCraftPlotsAddition.init();
        LogService.LogInfo("加载帝国模组更多世界提示");
        EmpireCraftWorldLogLibrary.init();
    }

    public void LoadUI()
    {
        MainTab.Init();
        LogService.LogInfo("帝国模组UI加载成功！！");
    }


    [Hotfixable]
    public void Reload()
    {
        LogService.LogInfo("帝国模组重新加载成功！！");
        
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "PeeragesLevelNames.csv"));
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "CountryLevelNames.csv"));
        loadCultureNameTemplate("Huaxia");
        loadCultureNameTemplate("Western");
        loadCultureNameTemplate("Youmu");
        LM.ApplyLocale();
        // You can reload your mod here, such as reloading configs, reloading UI, etc.
    }

    public string GetLocaleFilesDirectory(ModDeclare pModDeclare)
    {
        return pModDeclare.FolderPath + "/Locales/"; // Return the directory where your mod's locale files are located
    }
}
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
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GodPowers;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NCMS.Extensions;
using NeoModLoader.General.Game.extensions;
using Newtonsoft.Json;

namespace EmpireCraft.Scripts;
public class ModClass : MonoBehaviour, IMod, IReloadable, ILocalizable, IConfigurable
{
    public static bool SAVE_FREEZE = false;
    public static int WAR_END_YEAR = 30;
    public static Transform prefab_library;
    public static bool IS_CLEAR = true;
    public static EmpireManager EMPIRE_MANAGER;
    public static KingdomTitleManager KINGDOM_TITLE_MANAGER;
    public static bool REAL_NUM_SWITCH = false;
    public static bool KINGDOM_TITLE_FREEZE = false;
    public static int TITLE_BEEN_DESTROY_TIME = 50;
    public static ModDeclare _declare;
    private GameObject _modObject;
    public static ModConfig modConfig;
    public static int MOD_DATA_VERSION = 1;
    public static Dictionary<long, List<EmpireCraftHistory>> ALL_HISTORY_DATA = new Dictionary<long, List<EmpireCraftHistory>>();
    public ModDeclare GetDeclaration()
    {
        return _declare;
    }

    void Start ()
    {
        IS_CLEAR = false;
        
    }

    private void FixedUpdate()
    {

        KINGDOM_TITLE_MANAGER.update(-1L);
        World.world.kingdoms.ForEach(pKingdom =>
        {
            pKingdom.CheckEmpire();
            EmpireCraftKingdomBehCheckTemporaryFaction.CheckTf(pKingdom);
            if (pKingdom.isRekt()) return;
            if (!pKingdom.IsEmpire())  return;
            Regime regime = pKingdom.GetRegime();
            if (regime==null)  return;
            var ff = regime.GetDominateFaction();
            if (ff==null)  return;
            foreach (var tf in ff.TemporaryFactions)
            {
                tf.SetEmpire(pKingdom.GetEmpire());
                if (tf.IsNeedToCountDown())
                {
                    if (tf.CountDown > 0)
                    {
                        tf.CountDown -= 1;
                    }
                }
                if (tf.IsStarted()&&!tf.ShowAsPlot)
                {
                    tf.CheckNeedToUpdate();
                }
            }
        });
        
    }

    public GameObject GetGameObject()
    {
        return _modObject;
    }
    public string GetUrl()
    {
        return "https://github.com/ZhaoyuZhang101/EmpireCraft";
    }

    public void LoadCultureNameTemplate()
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
            LogService.LogInfo("Add culture template: " + cultureName);
        }
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
        //加载文化名称模板
        LoadCultureNameTemplate();
        LM.ApplyLocale(); // Apply the loaded locales to the game
        Type[] types = Assembly.GetExecutingAssembly().GetTypes();
        foreach (Type type in types)
        {
            if (type.GetInterface(nameof(GamePatch)) != null)
            {
                try
                {
                    GamePatch patch = (GamePatch)type.GetConstructor(new Type[] { })?.Invoke(new object[] { });
                    if (patch != null)
                    {
                        patch.declare = _declare;
                        patch.Initialize();
                    }
                }
                catch (Exception e)
                {
                    LogService.LogWarning("Failed to initialize patch: " + type.Name);
                    LogService.LogWarning(e.ToString());
                }
            }
        }
        LoadUI();
        prefab_library = new GameObject("PrefabLibrary").transform;
        prefab_library.SetParent(transform);
        modConfig = new ModConfig(_declare.FolderPath + "/default_config.json", true);
        LogService.LogInfo("加载帝国模组更多世界提示");
        EmpireCraftWorldLogLibrary.init();
        EmpireCraftNamePlateLibrary.init();
        EmpireCraftActorTraitLibrary.init();
        EmpireCraftMetaTypeLibrary.init();
        EmpireCraftHistoryDataLibrary.init();
        EmpireCraftActorTraitGroupLibrary.init();
        EmpireCraftTooltipLibrary.init();
        EmpireCraftOpinionAddition.init();
        EmpireCraftPlotsAddition.init();
        EmpireCraftQuantumSpriteLibrary.init();
        EmpireCraftBehaviourTaskLibrary.init();
        EmpireCraftWorldLawGroupLibrary.init();
        EmpireCraftWorldLawLibrary.init();
        EmpireCraftHotKeyLibrary.init();
        RegimeManager.init();
        FactionManager.init();
        EMPIRE_MANAGER = new EmpireManager();
        KINGDOM_TITLE_MANAGER = new KingdomTitleManager();
        OnomasticsRule.ReadSetting();
        string parentFolder = Directory.GetParent(_declare.FolderPath)?.FullName;
        if (parentFolder != null)
        {
            string path = Path.Combine(parentFolder, "CultureSpeciesPairPlayerConfig.json");
            if (File.Exists(path))
            {
                string content = File.ReadAllText(path);
                ConfigData.speciesCulturePair = JsonConvert.DeserializeObject<Dictionary<string, string>>(content);
            }
            else
            {
                LogService.LogInfo("用户文化配置不存在，启用默认配置");
            }
        }
    }

    public void LoadUI()
    {
        MainTab.Init();
        LogService.LogInfo("EmpireCraftUI Load Finish！！");
    }


    public void Reload()
    {
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "PeeragesLevelNames.csv"));
        LoadCultureNameTemplate();
        LM.ApplyLocale();
        FactionManager.ConvertToObjectFromFactionType();
        LogService.LogInfo("EmpireCraft Reload Finish！！");
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
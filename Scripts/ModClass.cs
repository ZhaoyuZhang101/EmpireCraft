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
    public static string NARROW_SPACE = "\u200A";
    public static bool SAVE_FREEZE = false;
    public static int WAR_END_YEAR = 30;
    public static float AI_DETECT_INTERVAL = 0.2f;
    public static float KINGDOM_AI_FULL_SCAN_DURATION = 1f;
    public static float CITY_AI_FULL_SCAN_DURATION = 2f;
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
    public static int MOD_DATA_VERSION = 3;
    public static Dictionary<long, List<EmpireCraftHistory>> ALL_HISTORY_DATA = new Dictionary<long, List<EmpireCraftHistory>>();
    private static readonly List<Kingdom> _fixedUpdateKingdomBuffer = new();
    private readonly List<Kingdom> _detectKingdomBuffer = new();
    private readonly List<City> _detectCityBuffer = new();
    private float _aiDetectElapsed = 0f;
    private int _detectKingdomIndex = 0;
    private int _detectCityIndex = 0;

    public ModDeclare GetDeclaration()
    {
        return _declare;
    }

    void Start()
    {
        IS_CLEAR = false;
    }

    void Update()
    {
        _aiDetectElapsed += Time.deltaTime;
        float detectInterval = AI_DETECT_INTERVAL;
        if (detectInterval <= 0f)
        {
            detectInterval = 0.01f;
        }

        if (_aiDetectElapsed < detectInterval)
        {
            return;
        }

        _aiDetectElapsed = 0f;
        ProcessKingdomDetectBatch(detectInterval);
        ProcessCityDetectBatch(detectInterval);
    }

    private int CalcDetectBatchSize(int totalCount, float detectInterval, float fullScanDuration)
    {
        if (totalCount <= 0)
        {
            return 0;
        }

        float interval = detectInterval <= 0f ? 0.01f : detectInterval;
        float duration = fullScanDuration <= 0f ? 0.01f : fullScanDuration;
        int batchSize = Mathf.CeilToInt(totalCount * interval / duration);
        return Mathf.Clamp(batchSize, 1, totalCount);
    }

    private void RebuildKingdomDetectBuffer()
    {
        _detectKingdomBuffer.Clear();
        if (World.world?.kingdoms == null)
        {
            _detectKingdomIndex = 0;
            return;
        }

        foreach (Kingdom kingdom in World.world.kingdoms)
        {
            if (kingdom != null)
            {
                _detectKingdomBuffer.Add(kingdom);
            }
        }

        _detectKingdomIndex = 0;
    }

    private void RebuildCityDetectBuffer()
    {
        _detectCityBuffer.Clear();
        if (World.world?.cities == null)
        {
            _detectCityIndex = 0;
            return;
        }

        foreach (City city in World.world.cities)
        {
            if (city != null)
            {
                _detectCityBuffer.Add(city);
            }
        }

        _detectCityIndex = 0;
    }

    private void ProcessKingdomDetectBatch(float detectInterval)
    {
        bool hasKingdomAI = GameAIMain.KingdomAis.Count > 0 || GameAIMain.KingdomMindAis.Count > 0 || GameAIMain.EmpireAis.Count > 0;
        bool hasActorAI = GameAIMain.ActorAis.Count > 0;
        if (!hasKingdomAI && !hasActorAI)
        {
            return;
        }

        if (_detectKingdomBuffer.Count == 0 || _detectKingdomIndex >= _detectKingdomBuffer.Count)
        {
            RebuildKingdomDetectBuffer();
        }

        int count = _detectKingdomBuffer.Count;
        if (count <= 0)
        {
            return;
        }

        int endIndex = Math.Min(count, _detectKingdomIndex + CalcDetectBatchSize(count, detectInterval, KINGDOM_AI_FULL_SCAN_DURATION));
        for (int i = _detectKingdomIndex; i < endIndex; i++)
        {
            Kingdom kingdom = _detectKingdomBuffer[i];
            if (kingdom == null || kingdom.isRekt())
            {
                continue;
            }

            for (int j = 0; j < GameAIMain.KingdomAis.Count; j++)
            {
                GameAIMain.KingdomAis[j]?.Detect(kingdom);
            }

            for (int j = 0; j < GameAIMain.KingdomMindAis.Count; j++)
            {
                GameAIMain.KingdomMindAis[j]?.Detect(kingdom);
            }

            for (int j = 0; j < GameAIMain.EmpireAis.Count; j++)
            {
                GameAIMain.EmpireAis[j]?.Detect(kingdom);
            }

            ProcessActorsForKingdom(kingdom);
        }

        _detectKingdomIndex = endIndex;
        if (_detectKingdomIndex >= count)
        {
            _detectKingdomBuffer.Clear();
            _detectKingdomIndex = 0;
        }
    }

    private void ProcessCityDetectBatch(float detectInterval)
    {
        if (GameAIMain.CityAis.Count == 0)
        {
            return;
        }

        if (_detectCityBuffer.Count == 0 || _detectCityIndex >= _detectCityBuffer.Count)
        {
            RebuildCityDetectBuffer();
        }

        int count = _detectCityBuffer.Count;
        if (count <= 0)
        {
            return;
        }

        int endIndex = Math.Min(count, _detectCityIndex + CalcDetectBatchSize(count, detectInterval, CITY_AI_FULL_SCAN_DURATION));
        for (int i = _detectCityIndex; i < endIndex; i++)
        {
            City city = _detectCityBuffer[i];
            if (city == null)
            {
                continue;
            }

            for (int j = 0; j < GameAIMain.CityAis.Count; j++)
            {
                GameAIMain.CityAis[j]?.Detect(city);
            }
        }

        _detectCityIndex = endIndex;
        if (_detectCityIndex >= count)
        {
            _detectCityBuffer.Clear();
            _detectCityIndex = 0;
        }
    }

    private void ProcessActorsForKingdom(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt() || GameAIMain.ActorAis.Count == 0 || kingdom.units == null)
        {
            return;
        }

        bool freezeNonWarriors = kingdom.hasEnemies();
        for (int i = 0; i < kingdom.units.Count; i++)
        {
            Actor actor = kingdom.units[i];
            if (actor == null || actor.isRekt() || !actor.isKingdomCiv())
            {
                continue;
            }

            bool keepLeaderAI = actor.isKing() || actor.isCityLeader();
            if (freezeNonWarriors && !actor.isWarrior() && !keepLeaderAI)
            {
                actor.CloseAI();
                continue;
            }

            actor.OpenAI();
            for (int j = 0; j < GameAIMain.ActorAis.Count; j++)
            {
                GameAIMain.ActorAis[j]?.Detect(actor);
            }
        }
    }

    private void FixedUpdate()
    {
        KINGDOM_TITLE_MANAGER.update(-1L);
        _fixedUpdateKingdomBuffer.Clear();
        foreach (Kingdom kingdom in World.world.kingdoms)
        {
            _fixedUpdateKingdomBuffer.Add(kingdom);
        }

        for (int i = 0; i < _fixedUpdateKingdomBuffer.Count; i++)
        {
            Kingdom pKingdom = _fixedUpdateKingdomBuffer[i];
            if (pKingdom == null) continue;
            pKingdom.CheckEmpire();
            EmpireCraftKingdomBehCheckTemporaryFaction.CheckTf(pKingdom);
            if (pKingdom.isRekt()) continue;
            if (!pKingdom.IsEmpire()) continue;
            Regime regime = pKingdom.GetRegime();
            if (regime == null) continue;
            var ff = regime.GetDominateFaction();
            if (ff == null) continue;
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
                if (tf.IsStarted() && !tf.ShowAsPlot)
                {
                    tf.CheckNeedToUpdate();
                }
            }
        }
        _fixedUpdateKingdomBuffer.Clear();
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
            var dirs = Directory.EnumerateFiles(culturesPath, "*.csv", SearchOption.AllDirectories).ToList();
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
        Config.isEditor = true;
        LogService.LogInfo("EmpireCraft Load Finished！！");
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "PeeragesLevelNames.csv"));
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "HonoraryOfficial.csv"));
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "MeritLevel.csv"));
        LoadCultureNameTemplate();
        LM.ApplyLocale();
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
        EmpireCraftLoyaltyLibrary.init();
        EmpireCraftBuildingLibrary.init();
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
    }

    public string GetLocaleFilesDirectory(ModDeclare pModDeclare)
    {
        return pModDeclare.FolderPath + "/Locales/";
    }

    public ModConfig GetConfig()
    {
        return modConfig;
    }
}

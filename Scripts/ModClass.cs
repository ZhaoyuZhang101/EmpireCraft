using NeoModLoader.api;
using UnityEngine;
using EmpireCraft.Scripts.Compatibility;
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
using EmpireCraft.Scripts.Diagnostics;
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
    // 现代政体统一过的帝国核心再分裂后，军阀时期持续多少年才承认分治；0 = 永远不分家
    public static int WARLORD_ERA_YEARS = 200;
    public static Transform prefab_library;
    public static bool IS_CLEAR = true;
    public static EmpireManager EMPIRE_MANAGER;
    public static KingdomTitleManager KINGDOM_TITLE_MANAGER;
    public static bool REAL_NUM_SWITCH = false;
    public static bool SIMPLE_NAMEPLATE_SWITCH = false;
    public static bool EMPIRE_SHOW_ALLIANCE_SWITCH = false;
    public static bool KINGDOM_TITLE_FREEZE = false;
    public static int TITLE_BEEN_DESTROY_TIME = 50;
    // 法理扩张(未冻结法理)时单个法理最多容纳的城市数，0 表示不限
    public static int TITLE_MAX_CITIES = 5;
    // 和平扩张时单座城市的区域数上限；0 = 不限，不限制战争占领或玩家改地。
    public static int CITY_MAX_ZONES = 50;
    // 游牧营地、斯拉夫文化的城领纯沙漠、纯冻土时地块上限与远近放宽几倍(见 BarrenLandSystem)
    public static int BARREN_ZONE_MULTIPLIER = 2;
    public static int SETTLEMENT_BASE_GOLD = 200;
    public static int SETTLEMENT_PER_CITY_GOLD = 25;
    public static int SETTLEMENT_RESERVE_GOLD = 200;
    public static int SETTLEMENT_WOOD = 20;
    public static int SETTLEMENT_STONE = 10;
    public static int SETTLEMENT_MIN_HOUSEHOLDS = 10;
    public static int SETTLEMENT_TARGET_HOUSEHOLDS = 20;
    public static int SETTLEMENT_RETAIN_HOUSEHOLDS = 30;
    public static int SETTLEMENT_MAX_MIGRATION_PERCENT = 25;
    public static int SETTLEMENT_FOOD_YEARS = 2;
    public static int SETTLEMENT_PREPARATION_MONTHS = 6;
    public static int SETTLEMENT_COOLDOWN_MONTHS = 36;
    public static int FEUDAL_UNION_REALM_LIMIT = 3;
    // 同一文化最多几个帝国并立(僭越称帝争正统，见 ImperialLegitimacyChallengeService)；玩家要求做成设置，默认 2
    public static int SAME_CULTURE_EMPIRE_LIMIT = 2;
    public static bool PERFORMANCE_HIGH_POPULATION_MODE = true;
    public static bool PERFORMANCE_ADAPTIVE_THROUGHPUT_MODE = true;
    public static bool PERFORMANCE_SKIP_HIDDEN_VISUALS = true;
    // 纯净地图模式(模组设置，下次启动生效，见 PureMapMode)
    public static bool PURE_MAP_MODE;
    public static bool PERFORMANCE_SKIP_NAMEPLATE_OVERLAP = true;
    // 独立王国、部落以简化朝堂推制度改革(见 InstitutionSystem.Realm.cs)；关掉就只有帝国推——原始政体的部落除外，
    // 部落不再是帝国，原始改革只能由各部推，关了照样推(见 InstitutionSystem.KingdomReformsEnabledFor)。
    // 设置里没有这一项(旧的 default_config.json)时按默认开
    public static bool KINGDOM_INSTITUTION_REFORMS = true;
    public static ModDeclare _declare;
    private GameObject _modObject;
    public static ModConfig modConfig;
    public static int MOD_DATA_VERSION = 3;
    public static Dictionary<long, List<EmpireCraftHistory>> ALL_HISTORY_DATA = new Dictionary<long, List<EmpireCraftHistory>>();
    // 文化 → 该文化从何时起没有帝国(用于"兜底称帝"的五十年时限)
    public static Dictionary<string, double> CULTURE_NO_EMPIRE_SINCE = new Dictionary<string, double>();
    // 帝国名 → 用这个名字建立过的西方封建帝国 id(用于"第二帝国""第三帝国")
    public static Dictionary<string, List<long>> FEUDAL_EMPIRE_LINEAGE = new Dictionary<string, List<long>>();
    public static HashSet<long> MOD_ALLIANCE_IDS = new HashSet<long>();
    public ModDeclare GetDeclaration()
    {
        return _declare;
    }
    void Start ()
    {
        IS_CLEAR = false;
        // 所有模组都加载完了，补上兼容模组的译文(盖掉它们初始化时写的英文)
        Compatibility.CompatLocalization.Apply();
        Compatibility.WarBoxCompatibility.FillMissingTraitIcons();
        // 所有模组都加载完了，重新接上矿场伐木场的升级链(盖掉其他模组对原版矿场升级的设置)
        EmpireCraft.Scripts.GeneralSystems.IndustryBuildingSystem.Link();
    }

    private int _backgroundCursor;
    private void Update()
    {
        FrameProfiler.FrameTick();
        EmpireCraft.Scripts.GeneralSystems.StableFrameRate.Tick();
        EmpireCraft.Scripts.GeneralSystems.SmoothBorders.FrameTick();
        using (FrameProfiler.Measure("后台·原生植被")) EmpireCraft.Scripts.GeneralSystems.NativeVegetationScheduler.Tick();
        using (FrameProfiler.Measure("后台·纯净地图")) EmpireCraft.Scripts.GeneralSystems.PureMapMode.Tick();
        EmpireCraft.Scripts.GeneralSystems.MapWallSystem.Tick();
        EmpireCraft.Scripts.GeneralSystems.LayerDiagnostics.Tick();
        EmpireCraft.Scripts.GeneralSystems.EnclaveFill.Tick();
        using (FrameProfiler.Measure("后台·官职清理")) EmpireCraft.Scripts.Regimes.OfficeManager.Tick();
        using (FrameProfiler.Measure("后台·和约割城")) EmpireCraft.Scripts.GeneralSystems.WarGoalSystem.Tick();
        using (FrameProfiler.Measure("后台·民族统一")) EmpireCraft.Scripts.GeneralSystems.NationalUnificationSystem.Tick();
        EmpireCraft.Scripts.GeneralSystems.PopulationRuntimeDiagnostics.Tick();
        if (!EmpireCraft.Scripts.GeneralSystems.CityPopulationSystem.AbstractPopulationEnabled)
        {
            EmpireCraft.Scripts.HelperFunc.SimulationFrameBudget.Reset();
            EmpireCraftStrategicScheduler.Tick();
            EmpireCraft.Scripts.GeneralSystems.CityPopulationSystem.Tick();
            EmpireCraft.Scripts.GeneralSystems.AnimalHusbandrySystem.Tick();
            EmpireCraft.Scripts.GamePatches.NoCommonersPatch.CullExcessWild();
            return;
        }
        if (World.world == null || !Config.game_loaded || Config.paused || SmoothLoader.isLoading()) return;
        EmpireCraft.Scripts.HelperFunc.SimulationFrameBudget.BeginFrame();
        using var frameWork = EmpireCraft.Scripts.HelperFunc.SimulationFrameBudget.Measure();
        // 每帧轮换先运行的系统，避免月度人口队列长期挤掉外交。
        for (int i = 0; i < 5 && EmpireCraft.Scripts.HelperFunc.SimulationFrameBudget.HasTime; i++)
        {
            switch ((_backgroundCursor + i) % 5)
            {
                case 0: using (FrameProfiler.Measure("后台·战略调度")) EmpireCraftStrategicScheduler.Tick(); break;
                case 1: using (FrameProfiler.Measure("后台·人口")) EmpireCraft.Scripts.GeneralSystems.CityPopulationSystem.Tick(); break;
                case 2: using (FrameProfiler.Measure("后台·牧业")) EmpireCraft.Scripts.GeneralSystems.AnimalHusbandrySystem.Tick(); break;
                case 3: using (FrameProfiler.Measure("后台·野生清理")) EmpireCraft.Scripts.GamePatches.NoCommonersPatch.CullExcessWild(); break;
                case 4: using (FrameProfiler.Measure("后台·理念接触")) EmpireCraft.Scripts.GeneralSystems.IdeologyPopulationSystem.TickContact(); break;
            }
        }
        _backgroundCursor = (_backgroundCursor + 1) % 5;
        using (FrameProfiler.Measure("后台·军团战线")) EmpireCraft.Scripts.GeneralSystems.CorpsSystem.Update();
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
        // 所有 Culture_* 文件夹都加载(不只是物种映射里用到的)，新加的文化文件夹放进来就生效
        string culturesRoot = Path.Combine(_declare.FolderPath, "Locales", "Cultures");
        IEnumerable<string> cultureNames = Directory.Exists(culturesRoot)
            ? Directory.EnumerateDirectories(culturesRoot, "Culture_*").Select(dir => Path.GetFileName(dir).Substring("Culture_".Length))
            : ConfigData.speciesCulturePair.Values.Distinct();
        foreach (string cultureName in cultureNames)
        {
            string culturesPath = Path.Combine(_declare.FolderPath, "Locales", "Cultures", $"Culture_{cultureName}");
            if (!Directory.Exists(culturesPath))
            {
                continue; // 这个文化没有文件夹，别影响后面的文化
            }
            var dirs = Directory.EnumerateFiles(culturesPath, "*.csv", SearchOption.AllDirectories)
            .ToList();
            foreach (var dir in dirs) 
            {
                string header = File.ReadLines(dir).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(header) || !header.Contains(',')) continue;
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
        // 通用政党命名词库(文化没有单独配置某个理念的党名时使用)
        string partyNamesPath = Path.Combine(_declare.FolderPath, "Locales", "Cultures", "PartyNames");
        if (Directory.Exists(partyNamesPath))
            foreach (string partyNames in Directory.EnumerateFiles(partyNamesPath, "*.csv")) LM.LoadLocales(partyNames);
        // 通用书名词库(时代名著、普通书的固定书名/模板/词语；文化没单独配置时使用)
        string bookNamesPath = Path.Combine(_declare.FolderPath, "Locales", "Cultures", "Books");
        if (Directory.Exists(bookNamesPath))
            foreach (string bookNames in Directory.EnumerateFiles(bookNamesPath, "*.csv")) LM.LoadLocales(bookNames);
        LogService.LogInfo("add year name template");
        LogService.LogInfo("加载谥号模板");
        LogService.LogInfo("加载庙号模板");
    }

    public void OnLoad(ModDeclare modDeclare, GameObject gameObject)
    {
        AncientWarfareIsolation.CaptureOriginalCallbacks();
        _declare = modDeclare;
        PerformanceTraceFile.Configure(modDeclare.FolderPath);
        _modObject = gameObject;
        EmpireCraftDebugProbe.Initialize();
        OnomasticsHelper.PreloadCultureFilesAsync();
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
        // 易俗按钮逐项读取文化配置；必须先读文化，不能只生成范围开关、漏掉全部文化笔刷。
        OnomasticsRule.ReadSetting();
        LoadUI();
        prefab_library = new GameObject("PrefabLibrary").transform;
        prefab_library.SetParent(transform);
        modConfig = LoadSettings();
        // 新版加载器读配置只还原值、不调用回调；IConfigurable 自行管理的配置必须主动应用。
        // 否则界面显示开启，PURE_MAP_MODE 等运行标记仍是默认值，建筑和人物继续显示。
        // 旧版与手机版加载器没有 ApplyCallbacks(读配置时已自己调回调)，按反射找，找不到就跳过，免得编不过
        typeof(ModConfig).GetMethod("ApplyCallbacks", Type.EmptyTypes)?.Invoke(modConfig, null);
        LogService.LogInfo($"[EmpireCraft][配置] 启动设置已应用：纯净地图={PURE_MAP_MODE}，高人口优化={PERFORMANCE_HIGH_POPULATION_MODE}");
        LogService.LogInfo("加载帝国模组更多世界提示");
        EmpireCraftWorldLogLibrary.init();
        EmpireCraftWorldLawGroupLibrary.init();
        EmpireCraftWorldLawLibrary.init();
        EmpireCraftNamePlateLibrary.init();
        EmpireCraftActorTraitLibrary.init();
        EmpireCraft.Scripts.UI.Components.HiResIconFilter.Apply();
        EmpireCraftMetaTypeLibrary.init();
        EmpireCraftHistoryDataLibrary.init();
        EmpireCraftActorTraitGroupLibrary.init();
        EmpireCraftTooltipLibrary.init();
        EmpireCraftOpinionAddition.init();
        EmpireCraftPlotsAddition.init();
        EmpireCraftQuantumSpriteLibrary.init();
        EmpireCraftBehaviourTaskLibrary.init();
        EmpireCraftHotKeyLibrary.init();
        EmpireCraftLoyaltyLibrary.init();
        EmpireCraftBuildingLibrary.init();
        EmpireCraftWarshipLibrary.init();
        RegimeManager.init();
        // 政体的国家类别、官名文本(各政体目录下的 OfficialType.csv，如"侯国""公国""国")在 RegimeManager.init 里才读，
        // 上面那次 ApplyLocale 之后读的文本要再应用一次，否则国名后缀显示成 ZhouFeudalism_hou 这样的键名
        LM.ApplyLocale();
        FactionManager.init();
        EMPIRE_MANAGER = new EmpireManager();
        KINGDOM_TITLE_MANAGER = new KingdomTitleManager();
        string parentFolder = Directory.GetParent(_declare.FolderPath)?.FullName;
        if (parentFolder != null)
        {
            string path = Path.Combine(parentFolder, "CultureSpeciesPairPlayerConfig.json");
            if (File.Exists(path))
            {
                // 玩家配置只覆盖它写到的物种；没写到的(比如新版本加的物种)沿用默认映射，
                // 否则会一律落到 GetCultureFromSpecies 的兜底文化，凭空冒出玩家没放过的文化。
                string content = File.ReadAllText(path);
                var playerPairs = JsonConvert.DeserializeObject<Dictionary<string, string>>(content);
                if (playerPairs != null)
                {
                    // 旧版本把酸液绅士的物种 id 拼错成了 civ_acid_sentleman，迁移到真实 id
                    if (playerPairs.TryGetValue("civ_acid_sentleman", out string acidCulture))
                    {
                        playerPairs.Remove("civ_acid_sentleman");
                        if (!playerPairs.ContainsKey("civ_acid_gentleman"))
                            playerPairs["civ_acid_gentleman"] = acidCulture;
                    }
                    foreach (KeyValuePair<string, string> pair in playerPairs)
                        ConfigData.speciesCulturePair[pair.Key] = pair.Value;
                }
            }
            else
            {
                LogService.LogInfo("用户文化配置不存在，启用默认配置");
            }
        }
        AncientWarfareIsolation.EnableWhenAvailable();
        // 在线更新只是附带功能：初始化出错(手机版网络库不全之类)不能拖垮整个模组加载
        try
        {
            EmpireCraftUpdateService.Initialize();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 在线更新初始化失败，跳过: {exception.Message}");
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
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "HonoraryOfficial.csv"));
        LM.LoadLocales(Path.Combine(_declare.FolderPath, "Locales", "MeritLevel.csv"));
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

    // 玩家的设置(用户 2026-10-27 转玩家反馈"每次模组更新都会导致设置重置")：以前直接读写模组目录里的 default_config.json，
    // 玩家改的设置存进了模组自带的默认文件，一更新(在线更新、创意工坊、手动覆盖)就被新版的默认值盖掉。现在照加载器的规矩：
    // 玩家的设置存在游戏存档目录的 mods_config/<模组 UID>.config，不随模组更新；default_config.json 只当设置项与默认值，
    // 读的时候合并(新加的设置项用默认值，删掉的去掉)。
    // 头一回(还没有 mods_config 文件)从旧设置迁过来：在线更新刚换下的旧版备份里的 default_config.json(更新时整个换掉了，
    // 备份要等这次启动稍后才清掉，见 EmpireCraftUpdateService.ReplacedConfigPath)；没有就用模组目录里的(没换过的、从 git 拉的，
    // 里面就是玩家的设置)。加载器没有合并接口(旧版、手机版)的照旧读写 default_config.json
    private static ModConfig LoadSettings()
    {
        string defaults = Path.Combine(_declare.FolderPath, "default_config.json");
        MethodInfo merge = typeof(ModConfig).GetMethod("MergeWith", new[] { typeof(ModConfig) });
        string folder = ModsConfigFolder();
        if (merge == null || string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(_declare.UID))
            return new ModConfig(defaults, true);
        try
        {
            string user = Path.Combine(folder, _declare.UID + ".config");
            if (!File.Exists(user))
            {
                string legacy = EmpireCraftUpdateService.ReplacedConfigPath() ?? defaults;
                Directory.CreateDirectory(folder);
                if (File.Exists(legacy)) File.Copy(legacy, user);
                LogService.LogInfo($"[EmpireCraft][配置] 设置改存到 {user}，从 {legacy} 迁入");
            }
            var config = new ModConfig(user, true);
            if (File.Exists(defaults)) merge.Invoke(config, new object[] { new ModConfig(defaults, true) });
            config.Save();
            return config;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][配置] 读取玩家设置失败，改用模组目录里的: {exception.Message}");
            return new ModConfig(defaults, true);
        }
    }

    // 加载器存各模组设置的目录(NeoModLoader.constants.Paths.ModsConfigPath；按名字找，手机版没有就用存档目录下的 mods_config)
    private static string ModsConfigFolder()
    {
        try
        {
            object path = Type.GetType("NeoModLoader.constants.Paths, NeoModLoader")?.GetField("ModsConfigPath")?.GetValue(null);
            if (path is string found && !string.IsNullOrEmpty(found)) return found;
        }
        catch
        {
            // 按名字找不到就用下面的
        }
        return Path.Combine(Application.persistentDataPath, "mods_config");
    }

    public ModConfig GetConfig()
    {
        return modConfig;
    }
}

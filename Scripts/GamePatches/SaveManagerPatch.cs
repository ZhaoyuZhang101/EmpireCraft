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
using EmpireCraft.Scripts.AI.ActorAI;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.System;
using EmpireCraft.Scripts.GeneralSystems;

namespace EmpireCraft.Scripts.GamePatches;
public class SaveManagerPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(save_mod_data)).Patch(
            AccessTools.Method(typeof(SaveManager), nameof(SaveManager.saveMapData)),
            prefix: new HarmonyMethod(GetType(), nameof(save_mod_data)),
            postfix: new HarmonyMethod(GetType(), nameof(ClearCachesAfterSave))
        );
        new Harmony(nameof(loadActors)).Patch(
            AccessTools.Method(typeof(SaveManager), nameof(SaveManager.loadActors)),
            prefix: new HarmonyMethod(GetType(), nameof(BeforeLoadActors)),
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
        // 游戏内重新生成地图直接走 clearWorld，不会重新调用 startTheGame。
        new Harmony(nameof(BeforeClearWorld)).Patch(
            AccessTools.Method(typeof(MapBox), nameof(MapBox.clearWorld)),
            prefix: new HarmonyMethod(GetType(), nameof(BeforeClearWorld))
        );
        new Harmony(nameof(last_gc)).Patch(
            AccessTools.Method(typeof(MapBox), nameof(MapBox.lastGC)),
            postfix: new HarmonyMethod(GetType(), nameof(last_gc))
        );        
    }
    public static void BeforeLoadActors()
    {
        try
        {
            CityPatch.RepairLiveCityKingdoms();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 读档修复城市王国失败: {exception.Message}");
        }
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
        BeforeClearWorld();
    }

    public static void BeforeClearWorld()
    {
        ModClass.IS_CLEAR = true;
        DBManagerPatch.AllClear();
        ClearPersistentWorldState();
        ClearRuntimeState();
    }

    public static bool save_mod_data(SaveManager __instance, string pFolder, bool pCompress)
    {
        if (string.IsNullOrEmpty(pFolder))
        {
            LogService.LogError("保存路径为空，无法保存mod数据");
            return true;
        }
        if (ModClass.SAVE_FREEZE)
        {
            return false;
        }
        try
        {
            EmpireCraft.Scripts.GameClassExtensions.ActorExtension.TrimAllPersonalHistories();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 存档前精简个人经历失败: {exception.Message}");
        }
        EmpireCraft.Scripts.GeneralSystems.CityPopulationSystem.FlushPendingGrowth();
        EmpireCraft.Scripts.GeneralSystems.IdeologyPopulationSystem.FlushParallelContact();
        DataManager.SaveAll(pFolder);
        return true;

    }    
    public static void load_mod_data(SaveManager __instance, SavedMap pData, string pPath)
    {
        ModClass.IS_CLEAR = true;
        ActorPatch.startSessionMonth = Date.getMonthsSince(World.world.getCurSessionTime());
        ActorPatch.isReadyToSet = false;
        ClearPersistentWorldState();
        ClearRuntimeState();

        if (pData == null)
        {
            LogService.LogError("数据为空，无法加载mod数据");
            ModClass.IS_CLEAR = false;
            return;
        }
        SmoothLoader.add(delegate
        {
            try
            {
                DataManager.LoadAll(pPath);
            }
            catch (Exception ex)
            {
                LogService.LogError("加载mod数据失败: " + ex.ToString());
            }
            finally
            {
                ModClass.IS_CLEAR = false;
            }
        }, "LOADING EMPIRE MOD DATA", false, 0.001f);
        // 科技首扫(补齐旧档科技、给兼容模组挂补丁)读档后第一次跑要两三秒；
        // 放进加载流程一起等，而不是进游戏后第一个年度轮转突然卡一大帧
        SmoothLoader.add(delegate
        {
            try
            {
                EmpireCraft.Scripts.GeneralSystems.TechnologySystem.TryYearlyScan();
            }
            catch (Exception ex)
            {
                LogService.LogError("读档科技首扫失败: " + ex.ToString());
            }
        }, "LOADING EMPIRE TECH STATE", false, 0.001f);
    }

    // 存档(含自动存档)写完之后清缓存：只清可按需重建的派生缓存、剔除已消亡对象的残留，
    // 不动征兵/调兵队列和单位行军状态这类进行中的运行状态，存档内容也不受影响
    public static void ClearCachesAfterSave()
    {
        try
        {
            KingdomFrontLineHelper.ClearCache();
            EmpireCraftActorCheckWarriorMoveAdvanced.ClearRuntimeState();
            IdeologyPopulationSystem.ClearCaches();
            CityPatch.PruneDeadCityCaches();
            // 不再显式做阻塞式完整 GC：它每次自动存档都固定添几百毫秒停顿；
            // 清掉引用后由运行时自行回收即可
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 存档后清理缓存失败: {exception.Message}");
        }
    }

    private static void ClearRuntimeState()
    {
        // MapBox 实例在重新生成地图时会复用，不能只依赖 World.world 引用变化来取消旧任务。
        CityPopulationSystem.ResetWorldState();
        NativeWorldOptimizationPatch.Reset();
        SimulationFrameBudget.Reset();
        EmpireCraftStrategicScheduler.Reset();
        KingdomFrontLineHelper.ClearCache();
        EmpireCraftActorCheckWarrior.ClearRuntimeState();
        EmpireCraftActorCheckWarriorMove.ClearRuntimeState();
        EmpireCraftActorCheckWarriorMoveAdvanced.ClearRuntimeState();
        EmpireCraft.Scripts.GeneralSystems.WarGoalSystem.ResetWorldState();
        EmpireCraft.Scripts.GeneralSystems.AdministrativeControlSystem.ResetWorldState();
    }

    private static void ClearPersistentWorldState()
    {
        DataManager.ResetCurrentSaveDataPath();
        CulturePatch.ResetWorldState();
        EmpireCraft.Scripts.GeneralSystems.ZonePlanSystem.ResetWorldState();
        EmpireCraft.Scripts.GeneralSystems.AnimalHusbandrySystem.ResetWorldState();
        ModClass.EMPIRE_MANAGER = new EmpireManager();
        ModClass.KINGDOM_TITLE_MANAGER = new KingdomTitleManager();
        ModClass.ALL_HISTORY_DATA = new Dictionary<long, List<EmpireCraftHistory>>();
        ModClass.CULTURE_NO_EMPIRE_SINCE = new Dictionary<string, double>();
        ModClass.FEUDAL_EMPIRE_LINEAGE = new Dictionary<string, List<long>>();
        ModClass.MOD_ALLIANCE_IDS = new HashSet<long>();
        SpecificClanManager._specificClans = new List<SpecificClan>();
        SpecificClanManager.RebuildCache();
        EmpireCoreManager.EmpireCores = new Dictionary<long, EmpireCore>();
        EmpireCraftMetaTypeLibrary.selected_empire = null;
        EmpireCraftMetaTypeLibrary.selected_empireCore = null;
        EmpireCraftMetaTypeLibrary.selected_kingdomTitle = null;
        EmpireCraftNamePlateLibrary.ResetWorldState();
        ZoneMetaDataVisualizer.clearAll();
    }
}

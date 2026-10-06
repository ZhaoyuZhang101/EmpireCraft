using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ai.behaviours;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GamePatches;

// 科技树的卡口。原则是"没研究出来之前，任何途径都拿不到"，所以不只拦 AI 的正常流程，
// 还拦所有最终落地的地方：
//   · 打造装备：工匠只能从本文化已研究的装备里挑(省得白花钱)；
//   · 装备上身(ActorEquipmentSlot.setItem)：继承、购买、发放、拾取……任何来源的未解锁装备都穿不上；
//   · 建筑：建造订单、实际开工(tryToBuild，别的模组会在这里把房子换成别的建筑)、原版升级；
//   · 别的模组直接换建筑(building_swap_guards，比如 WarBox 的民居升级器)；
//   · 生成单位(ActorManager.createNewUnit)：没研究的载具造不出来；
//   · 别的模组的布尔方法(mod_gates，比如 WarBox 的核打击)。
//
// 后两类针对别的模组的补丁都按类型名反射查找，那个模组没装就直接跳过，本模组照常运行。
// 玩家用上帝之力手动生成的东西不拦(WarBox 的手动生成有自己的标记)。
public class TechnologyPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    private static Harmony _harmony;
    private static bool _modPatchesApplied;
    private static readonly Dictionary<MethodBase, string> GateTechs = new();
    private static readonly Dictionary<MethodBase, string> SwapDeferBuildings = new();
    [ThreadStatic] private static int _swapDepth;
    [ThreadStatic] private static string _swapDeferBuilding;
    private static PropertyInfo _warBoxPlayerSpawn;
    private static bool _warBoxPlayerSpawnResolved;

    public void Initialize()
    {
        _harmony = new Harmony("EmpireCraft.Technology");
        // 优先级最高：先把候选清单换成过滤后的副本，再交给后面的补丁(比如 ModernBox 按时代筛枪)。
        // ModernBox 的补丁会直接 RemoveAt 传进来的清单——那本来是全局的装备分类表，被删掉就再也造不出来了；
        // 我们先换成副本，它删的就只是副本。
        _harmony.Patch(AccessTools.Method(typeof(ItemCrafting), nameof(ItemCrafting.getItemAssetToCraft)),
            prefix: new HarmonyMethod(typeof(TechnologyPatch), nameof(FilterCraftList)) { priority = Priority.First });
        _harmony.Patch(AccessTools.Method(typeof(Building), "canBeUpgraded"),
            postfix: new HarmonyMethod(typeof(TechnologyPatch), nameof(CanBeUpgraded)));
        _harmony.Patch(AccessTools.Method(typeof(ActorEquipmentSlot), "setItem"),
            prefix: new HarmonyMethod(typeof(TechnologyPatch), nameof(BeforeSetItem)));
        // 最后执行：别的模组的前置补丁可能把要建的建筑换掉了，拦的是最终要建的那个
        _harmony.Patch(AccessTools.Method(typeof(CityBehBuild), nameof(CityBehBuild.tryToBuild)),
            prefix: new HarmonyMethod(typeof(TechnologyPatch), nameof(BeforeTryToBuild)) { priority = Priority.Last });
        _harmony.Patch(AccessTools.Method(typeof(ActorManager), nameof(ActorManager.createNewUnit)),
            prefix: new HarmonyMethod(typeof(TechnologyPatch), nameof(BeforeCreateUnit)) { priority = Priority.First });
        _harmony.Patch(AccessTools.Method(typeof(Building), "setBuilding"),
            prefix: new HarmonyMethod(typeof(TechnologyPatch), nameof(BeforeSetBuilding)));
        // 著书立说：写书、读书都给本文化攒科技点
        _harmony.Patch(AccessTools.Method(typeof(BookManager), nameof(BookManager.generateNewBook)),
            postfix: new HarmonyMethod(typeof(TechnologyPatch), nameof(AfterBookWritten)));
        _harmony.Patch(AccessTools.Method(typeof(Book), nameof(Book.increaseReadTimes)),
            postfix: new HarmonyMethod(typeof(TechnologyPatch), nameof(AfterBookRead)));
    }

    public static void AfterBookWritten(Actor pActor, Book __result)
    {
        if (__result == null || pActor == null) return;
        try
        {
            // 记下这本书出自哪个文化(文化藏书窗口按这个归类)
            string authorCulture = CultureService.GetActorCulture(pActor);
            if (CultureService.IsValidCulture(authorCulture))
            {
                var bookData = __result.GetOrCreate();
                bookData.id = __result.getID();
                bookData.culture = authorCulture;
            }
            // 普通书换成本文化风格的书名(时代名著随后由 LandmarkBookSystem 再改成名著书名)
            BookNamingSystem.Rename(__result, pActor);
            TechnologySystem.OnBookWritten(TechnologySystem.GetCultureOf(pActor));
        }
        catch
        {
            // 统计失败不影响写书本身
        }
    }

    // 读书算在书所在的城市(图书馆)；找不到就算作者所在的城市
    public static void AfterBookRead(Book __instance)
    {
        if (__instance?.data == null || World.world == null) return;
        try
        {
            City city = World.world.buildings.get(__instance.data.building_id)?.city ??
                        World.world.cities.get(__instance.data.author_city_id);
            string culture = TechnologySystem.GetCultureOf(city);
            TechnologySystem.OnBookRead(culture);
            // 名著被别的文化读到，那个文化也受启发
            LandmarkBookSystem.OnRead(__instance, culture);
        }
        catch
        {
            // 统计失败不影响读书本身
        }
    }

    // 别的模组的程序集可能比本模组晚加载，所以等进了世界、第一次结算时再打这些补丁
    public static void EnsureModPatches()
    {
        if (_modPatchesApplied || _harmony == null) return;
        _modPatchesApplied = true;
        WarBoxCompatibility.EnsureApplied(_harmony);
        CompatLocalization.Apply();
        // modernmod 本想每 0.5 秒重设一次原版建筑到它高级建筑的升级链，但它启动协程时自身物体还没激活，
        // 日志里报"Coroutine couldn't be started"，这个保险就没了。进世界时替它补做一次。
        try
        {
            AccessTools.TypeByName("ModernMod.Code.Buildings")?
                .GetMethod("SetBaseGameBuildingUpgrades", BindingFlags.Public | BindingFlags.Static)?
                .Invoke(null, null);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] modernmod 升级链补设失败: {exception.Message}");
        }
        TechTreeConfig config = TechnologySystem.Config;
        foreach (TechModGateConfig gate in config.mod_gates)
        {
            MethodBase method = FindMethod(gate.type, gate.method);
            if (method == null) continue;
            if (method is not MethodInfo info || info.ReturnType != typeof(bool))
            {
                LogService.LogWarning($"[EmpireCraft] 科技门槛只能挂在返回 bool 的方法上: {gate.type}.{gate.method}");
                continue;
            }
            try
            {
                GateTechs[method] = gate.tech;
                _harmony.Patch(method, prefix: new HarmonyMethod(typeof(TechnologyPatch), nameof(ModGate)));
                LogService.LogInfo($"[EmpireCraft] 科技门槛已接入 {gate.type}.{gate.method} → {gate.tech}");
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft] 科技门槛接入失败 {gate.type}.{gate.method}: {exception.Message}");
            }
        }
        foreach (TechSwapGuardConfig guard in config.building_swap_guards)
        {
            MethodBase method = FindMethod(guard.type, guard.method);
            if (method == null) continue;
            try
            {
                SwapDeferBuildings[method] = guard.defer_to_mod_building ?? "";
                _harmony.Patch(method, prefix: new HarmonyMethod(typeof(TechnologyPatch), nameof(EnterSwapGuard)),
                    finalizer: new HarmonyMethod(typeof(TechnologyPatch), nameof(ExitSwapGuard)));
                LogService.LogInfo($"[EmpireCraft] 建筑替换守卫已接入 {guard.type}.{guard.method}");
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft] 建筑替换守卫接入失败 {guard.type}.{guard.method}: {exception.Message}");
            }
        }
    }

    private static MethodBase FindMethod(string typeName, string methodName)
    {
        if (string.IsNullOrWhiteSpace(typeName) || string.IsNullOrWhiteSpace(methodName)) return null;
        Type type = AccessTools.TypeByName(typeName);
        if (type == null) return null; // 没装对应模组
        return AccessTools.GetDeclaredMethods(type).FirstOrDefault(method => method.Name == methodName);
    }

    private static bool IsLoading() => !Config.game_loaded || SmoothLoader.isLoading();

    #region 装备

    public static void FilterCraftList(Actor pActor, ref List<EquipmentAsset> pItemList, City pCity)
    {
        if (pItemList == null || pItemList.Count == 0 || !TechnologySystem.GatesActive) return;
        try
        {
            if (AncientWarfareCompatibility.OwnsObject(pCity)) return;
            string culture = TechnologySystem.GetCultureOf(pActor);
            if (!CultureService.IsValidCulture(culture)) culture = TechnologySystem.GetCultureOf(pCity);
            // 没有模组文化的(野外、原版文化)只受"禁止近代化"约束
            if (!CultureService.IsValidCulture(culture) && !TechnologySystem.PremodernLocked) return;
            var filtered = new List<EquipmentAsset>(pItemList.Count);
            foreach (EquipmentAsset asset in pItemList)
                if (asset != null && TechnologySystem.CanCraft(culture, asset)) filtered.Add(asset);
            pItemList = filtered;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 科技装备筛选失败: {exception.Message}");
        }
    }

    // 任何来源的装备上身前都过一遍：没研究的穿不上，身上原来的装备也不会被换掉。
    // 没穿上的新物品没有主人，原版的物品清理会把它回收。
    public static bool BeforeSetItem(Item pItem, Actor pActor)
    {
        if (pItem == null || pActor == null || !TechnologySystem.GatesActive || IsLoading()) return true;
        try
        {
            if (AncientWarfareCompatibility.OwnsObject(pActor)) return true;
            return TechnologySystem.CanEquip(pActor, pItem.asset);
        }
        catch
        {
            return true;
        }
    }

    #endregion

    #region 建筑

    public static void CanBeUpgraded(Building __instance, ref bool __result)
    {
        if (!__result || __instance?.asset == null || !TechnologySystem.GatesActive) return;
        City city = __instance.current_tile?.zone?.city;
        if (city == null || AncientWarfareCompatibility.OwnsObject(city)) return;
        if (!TechnologySystem.CanBuild(city, __instance.asset.upgrade_to)) __result = false;
    }

    // 建造订单：升级订单看升级后的建筑，新建订单看建筑本身
    public static bool CanUseBuildOrder(BuildOrder order, City city)
    {
        if (order == null || city == null || !TechnologySystem.GatesActive) return true;
        BuildingAsset asset = order.getBuildingAsset(city);
        if (asset == null) return true;
        return TechnologySystem.CanBuild(city, order.upgrade ? asset.upgrade_to : asset.id);
    }

    public static bool BeforeTryToBuild(City pCity, BuildingAsset pBuildingAsset, ref Building __result)
    {
        if (pCity == null || pBuildingAsset == null || !TechnologySystem.GatesActive) return true;
        if (AncientWarfareCompatibility.OwnsObject(pCity)) return true;
        if (TechnologySystem.CanBuild(pCity, pBuildingAsset.id)) return true;
        __result = null;
        return false;
    }

    public static void EnterSwapGuard(MethodBase __originalMethod)
    {
        _swapDepth++;
        SwapDeferBuildings.TryGetValue(__originalMethod, out _swapDeferBuilding);
    }

    public static Exception ExitSwapGuard(Exception __exception)
    {
        if (_swapDepth > 0) _swapDepth--;
        if (_swapDepth == 0) _swapDeferBuilding = null;
        return __exception;
    }

    // 只在守卫方法执行期间生效(新建建筑也走 setBuilding，平时绝不能拦)
    public static bool BeforeSetBuilding(Building __instance, BuildingAsset pAsset)
    {
        if (_swapDepth <= 0 || pAsset == null || !TechnologySystem.GatesActive) return true;
        if (!string.IsNullOrEmpty(_swapDeferBuilding) && AssetManager.buildings.get(_swapDeferBuilding) != null)
            return false;
        City city = __instance?.current_tile?.zone?.city;
        return city == null || TechnologySystem.CanBuild(city, pAsset.id);
    }

    #endregion

    #region 单位与别的模组的行为

    public static bool BeforeCreateUnit(string pStatsID, WorldTile pTile, ref Actor __result)
    {
        if (!TechnologySystem.GatesActive || IsLoading() || IsPlayerVehicleSpawn()) return true;
        City city = pTile?.zone?.city;
        if (city == null || AncientWarfareCompatibility.OwnsObject(city)) return true;
        if (TechnologySystem.CanSpawnUnit(pStatsID, city)) return true;
        __result = null;
        return false;
    }

    // WarBox 的上帝之力手动生成载具时会打一个标记，玩家的手动操作不拦
    private static bool IsPlayerVehicleSpawn()
    {
        if (!_warBoxPlayerSpawnResolved)
        {
            _warBoxPlayerSpawnResolved = true;
            // 不用 AccessTools.TypeByName：它会枚举所有程序集的所有类型，第一次生成单位(放置种族)时会卡一下；
            // 按全名逐个程序集查找只是字典查询
            Type guard = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("WarBox.Content.VehicleSpawnGuard", false))
                .FirstOrDefault(type => type != null);
            _warBoxPlayerSpawn = guard?.GetProperty("IsPlayerSpawn",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        }
        try
        {
            return _warBoxPlayerSpawn != null && (bool)_warBoxPlayerSpawn.GetValue(null);
        }
        catch
        {
            return false;
        }
    }

    public static bool ModGate(MethodBase __originalMethod, object[] __args, ref bool __result)
    {
        if (!TechnologySystem.GatesActive || !GateTechs.TryGetValue(__originalMethod, out string tech)) return true;
        string culture = "";
        foreach (object arg in __args ?? Array.Empty<object>())
        {
            culture = arg switch
            {
                Actor actor => TechnologySystem.GetCultureOf(actor),
                Kingdom kingdom => TechnologySystem.GetCultureOf(kingdom),
                City city => TechnologySystem.GetCultureOf(city),
                _ => ""
            };
            if (!string.IsNullOrEmpty(culture)) break;
        }
        if (TechnologySystem.CanUse(culture, tech) &&
            !EmpireCraft.Scripts.Compatibility.NuclearDoctrineSystem.BlocksNuclearUse(tech, __args)) return true;
        __result = false;
        // 带 out string 的方法(比如玩家手动下令核打击)把原因带回去，免得对方界面拿到空串
        ParameterInfo[] parameters = __originalMethod.GetParameters();
        for (int i = 0; i < parameters.Length && __args != null && i < __args.Length; i++)
        {
            if (parameters[i].IsOut && parameters[i].ParameterType == typeof(string).MakeByRefType())
                __args[i] = string.Format(NeoModLoader.General.LM.Get("tech_locked_action"),
                    TechnologySystem.GetTechName(tech));
        }
        return false;
    }

    #endregion
}

using System;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Data;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

// 无小人模式：城里的人不睡觉(见 BeforeAddStatusEffect)；兵额按军制(只有军镇有兵)；原版每次刷新城市状态时，把没有实体单位的背景人口也算进城市人口和已占用住房。
// 这样住房被背景人口占着，原版不会因为"空房很多"而无限生育；住满后又会照常盖新民居，
// 城市能容纳的人数随建筑增长(容量本身仍由 getPopulationMaximum 按住房计算)。
public class NoCommonersPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        // 无小人模式下留在地图上的人(名人、劳动者、将领、征召兵)不睡觉：拦下原版的"睡眠"状态
        try
        {
            var harmony = new Harmony(nameof(NoCommonersPatch) + ".Sleep");
            foreach (global::System.Reflection.MethodInfo method in AccessTools.GetDeclaredMethods(typeof(Actor)))
            {
                if (method.Name != "addStatusEffect") continue;
                global::System.Reflection.ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 0 || parameters[0].ParameterType != typeof(string)) continue;
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(NoCommonersPatch), nameof(BeforeAddStatusEffect)));
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 无小人模式免睡补丁未生效: {exception.Message}");
        }
        try
        {
            new Harmony(nameof(NoCommonersPatch)).Patch(AccessTools.Method(typeof(City), "updateCityStatus"),
                postfix: new HarmonyMethod(typeof(NoCommonersPatch), nameof(AfterUpdateCityStatus))
                    { priority = Priority.Last });
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 无小人模式住房补丁未生效: {exception.Message}");
        }
        PatchPopulationQueries();
        PatchVanillaScale();
        try
        {
            // 城破：伤亡、逃难、抢粮(见 ScorchedEarthSystem.OnCityCaptured)
            new Harmony(nameof(NoCommonersPatch) + ".CityFall").Patch(
                AccessTools.Method(typeof(City), nameof(City.joinAnotherKingdom)),
                prefix: new HarmonyMethod(typeof(NoCommonersPatch), nameof(BeforeJoinAnotherKingdom)));
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 无小人模式城破补丁未生效: {exception.Message}");
        }
        try
        {
            // 耕地红线：规划农田区里不盖建筑
            new Harmony(nameof(NoCommonersPatch) + ".Farmland").Patch(
                AccessTools.Method(typeof(City), nameof(City.planAllowsToPlaceBuildingInZone)),
                postfix: new HarmonyMethod(typeof(NoCommonersPatch), nameof(AfterPlanAllowsBuilding)));
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 耕地红线补丁未生效: {exception.Message}");
        }
        try
        {
            // 越富庶的城民居上限越高、房屋越密集
            new Harmony(nameof(NoCommonersPatch) + ".Houses").Patch(AccessTools.Method(typeof(City), "recalculateMaxHouses"),
                postfix: new HarmonyMethod(typeof(NoCommonersPatch), nameof(AfterRecalculateMaxHouses)));
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 无小人模式民居密度补丁未生效: {exception.Message}");
        }
        try
        {
            var harmony = new Harmony(nameof(NoCommonersPatch) + ".Soldiers");
            // 征召兵每次受伤当场记减员(军团人数按生命值折算)，免得两次月度结算之间挨打又回满血时漏算伤亡
            harmony.Patch(AccessTools.Method(typeof(Actor), "getHit"),
                postfix: new HarmonyMethod(typeof(NoCommonersPatch), nameof(AfterGetHit)));
            // 士兵只由军制征召(见 CityPopulationSystem.RaiseLevies)：原版不再把居民、城主拉去当兵
            harmony.Patch(AccessTools.Method(typeof(City), "tryToMakeWarrior"),
                prefix: new HarmonyMethod(typeof(NoCommonersPatch), nameof(BeforeTryToMakeWarrior)));
            // 城主不由士兵兼任：选中士兵时改为从人口中生成一名文官出任
            harmony.Patch(AccessTools.Method(typeof(City), nameof(City.setLeader)),
                prefix: new HarmonyMethod(typeof(NoCommonersPatch), nameof(BeforeSetLeader)));
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 无小人模式士兵/城主补丁未生效: {exception.Message}");
        }
        try
        {
            new Harmony(nameof(NoCommonersPatch) + ".Neutral").Patch(AccessTools.Method(typeof(City), "turnCityToNeutral"),
                prefix: new HarmonyMethod(typeof(NoCommonersPatch), nameof(BeforeTurnCityToNeutral)));
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 无小人模式空城保护补丁未生效: {exception.Message}");
        }
    }

    // 原版：城里一个实体单位都没有就变成无主之地。无小人模式下城里往往只有城主一人，
    // 城主一死城就丢了——其实城里还住着背景人口。这时从背景人口里当场生成一人接任城主，城照常归属本国
    public static bool BeforeTurnCityToNeutral(City __instance)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || __instance?.kingdom == null || __instance.kingdom.wild ||
            EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        // 城里永远有人：背景人口不足时先补足保底，再从中生成一人接任城主
        CityPopulationSystem.EnsureFloor(__instance);
        if (CityPopulationSystem.GetBackgroundTotal(__instance) < 1f) return true;
        Actor leader = CityPopulationSystem.SpawnCivilian(__instance);
        if (leader == null) return true;
        if (__instance.leader == null || __instance.leader.isRekt() || !__instance.leader.isAlive())
            __instance.setLeader(leader, true);
        return false;
    }

    #region 原版逻辑按原尺度读人口

    // 这些原版逻辑拿城市/王国人口做判断，用的是"单位个数"的量级。真实人数放大上百倍后判断会全部走偏：
    //   Actor.isPlacePrivateForBreeding：实体单位数 < 人口上限×2+10 才繁殖(上限放大后原版单位会无限生育)；
    //   City.needSettlers：不足 22 人时招移民；WorldBehaviourActions.updateMigrants：不足 100 人的城才刷原版移民；
    //   DisasterLibrary.spawnMadThought：至少 50 人才发生；
    //   决策 find_city_job(≤30 人时士兵也去干活)、谋划 clan_ascension(王国至少 500 人)。
    // 在这些方法里进入"原尺度"，读到的人口与上限按户计，其余(界面、统计、模组)照常读真实人数
    private static void PatchVanillaScale()
    {
        var harmony = new Harmony(nameof(NoCommonersPatch) + ".VanillaScale");
        var enter = new HarmonyMethod(typeof(NoCommonersPatch), nameof(EnterScale));
        var exit = new HarmonyMethod(typeof(NoCommonersPatch), nameof(ExitScale));
        try
        {
            harmony.Patch(AccessTools.Method(typeof(Actor), "isPlacePrivateForBreeding"),
                postfix: new HarmonyMethod(typeof(NoCommonersPatch), nameof(AfterIsPlacePrivateForBreeding)));
            // 摄魂繁殖(火元素等)、分裂等不走 isPlacePrivateForBreeding，只看 isMetaLimitsReached：一并受上限约束
            harmony.Patch(AccessTools.Method(typeof(BabyHelper), nameof(BabyHelper.isMetaLimitsReached)),
                postfix: new HarmonyMethod(typeof(NoCommonersPatch), nameof(AfterIsMetaLimitsReached)));
            // 宗族图层(按城显示)：城市铭牌后面列出本城的地方豪强
            harmony.Patch(AccessTools.Method(typeof(NameplateText), nameof(NameplateText.showTextClanCity)),
                postfix: new HarmonyMethod(typeof(NoCommonersPatch), nameof(AfterShowTextClanCity)));
            // 生物群系随机刷动物(苍蝇、蚱蜢、甲虫等)同样受上限约束
            harmony.Patch(AccessTools.Method(typeof(WorldBehaviourActions), "updateUnitSpawn"),
                prefix: new HarmonyMethod(typeof(NoCommonersPatch), nameof(BeforeUpdateUnitSpawn)));
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 无小人模式野生动物繁殖上限补丁未生效: {exception.Message}");
        }
        foreach ((Type type, string name) in new[]
                 {
                     (typeof(Actor), "isPlacePrivateForBreeding"),
                     (typeof(City), "needSettlers"),
                     (typeof(WorldBehaviourActions), "updateMigrants"),
                     (typeof(DisasterLibrary), "spawnMadThought"),
                     // 原版 AI 开战：本国士兵要超过 10 人、并比较双方士兵数——按战时能动员的兵力算(见 AfterCountTotalWarriors)
                     (typeof(DiplomacyHelpers), "isWarNeeded"),
                     (typeof(DiplomacyHelpers), "getWarTarget")
                 })
        {
            try
            {
                global::System.Reflection.MethodInfo method = AccessTools.Method(type, name);
                if (method != null) harmony.Patch(method, prefix: enter, finalizer: exit);
                else LogService.LogWarning($"[EmpireCraft] 无小人模式原尺度：找不到 {type.Name}.{name}");
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft] 无小人模式原尺度补丁未生效({type.Name}.{name}): {exception.Message}");
            }
        }
        // 扩张(国王建新城)：原版先看本国有没有不足 30 个单位的城(有就先不扩张)，再从老城挑最多 6 人搬去新城。
        // 无小人模式下城里实体单位很少，前者会让所有国家都不再扩张，后者搬不出人。改为按户数判断，
        // 建城后把老城一成的背景人口迁到新城
        try
        {
            Type foundation = AccessTools.TypeByName("ai.behaviours.BehKingCheckNewCityFoundation") ??
                              AccessTools.TypeByName("BehKingCheckNewCityFoundation");
            if (foundation != null)
            {
                harmony.Patch(AccessTools.Method(foundation, "hasCitiesWithoutPopulation"),
                    prefix: new HarmonyMethod(typeof(NoCommonersPatch), nameof(BeforeHasCitiesWithoutPopulation)));
                harmony.Patch(AccessTools.Method(foundation, "moveSomeUnitsToNewCity"),
                    postfix: new HarmonyMethod(typeof(NoCommonersPatch), nameof(AfterMoveSomeUnitsToNewCity)));
            }
            else LogService.LogWarning("[EmpireCraft] 无小人模式扩张补丁：找不到 BehKingCheckNewCityFoundation");
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 无小人模式扩张补丁未生效: {exception.Message}");
        }
        try
        {
            harmony.Patch(AccessTools.Method(typeof(Kingdom), nameof(Kingdom.countTotalWarriors)),
                postfix: new HarmonyMethod(typeof(NoCommonersPatch), nameof(AfterCountTotalWarriors)));
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 无小人模式开战兵力补丁未生效: {exception.Message}");
        }
        try
        {
            DecisionAsset decision = AssetManager.decisions_library?.get("find_city_job");
            if (decision?.action_check_launch != null)
            {
                DecisionAction original = decision.action_check_launch;
                // 生产已经自动化：留在地图上的名人(族长、官员、士人等)不再去种地、砍树、采矿，只有士兵照原版找差事
                decision.action_check_launch = actor =>
                    (!CityPopulationSystem.AbstractPopulationEnabled || CityPopulationSystem.IsSoldier(actor)) &&
                    InVanillaScale(() => original(actor));
            }
            PlotAsset plot = AssetManager.plots_library?.get("clan_ascension");
            if (plot?.check_is_possible != null)
            {
                PlotCheckerDelegate original = plot.check_is_possible;
                plot.check_is_possible = actor => InVanillaScale(() => original(actor));
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 无小人模式原尺度(决策/谋划)未生效: {exception.Message}");
        }
    }

    private const int ExpansionMinimumHouseholds = 30;
    private const float SettlerShare = 0.1f;

    // 民居上限 × (1 + 富庶度)：最富的城可以盖到两倍的民居
    public static void AfterRecalculateMaxHouses(City __instance)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || __instance?.status == null ||
            EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return;
        __instance.status.houses_max = Mathf.RoundToInt(__instance.status.houses_max *
                                                        (1f + CityConstructionSystem.Wealth(__instance) +
                                                         CityConstructionSystem.SeatBonus(__instance)));
    }

    public static void AfterPlanAllowsBuilding(City __instance, TileZone pZone, ref bool __result)
    {
        if (!__result || pZone == null || __instance == null) return;
        if (FarmlandSystem.IsProtected(__instance) && FarmlandSystem.IsPlanned(__instance, pZone) &&
            FarmlandSystem.HasFarmArea(__instance, pZone)) __result = false;
    }

    public static void BeforeJoinAnotherKingdom(City __instance, Kingdom pNewSetKingdom, bool pCaptured)
    {
        if (!pCaptured || __instance?.kingdom == null || pNewSetKingdom == null || __instance.kingdom == pNewSetKingdom ||
            EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return;
        ScorchedEarthSystem.OnCityCaptured(__instance, __instance.kingdom, pNewSetKingdom);
    }

    public static void AfterGetHit(Actor __instance)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || __instance?.data == null) return;
        if (__instance.GetOrCreate().legion_size > 1f) CityPopulationSystem.LegionAlive(__instance);
    }

    public static bool BeforeTryToMakeWarrior(City __instance, ref bool __result)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled ||
            EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        __result = false;
        return false;
    }

    public static void BeforeSetLeader(City __instance, ref Actor pActor)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || pActor == null ||
            !CityPopulationSystem.IsSoldier(pActor) || __instance?.kingdom == null ||
            EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return;
        Actor civilian = CityPopulationSystem.SpawnCivilian(__instance);
        if (civilian != null) pActor = civilian;
    }

    public static bool BeforeHasCitiesWithoutPopulation(Kingdom pKingdom, ref bool __result)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || pKingdom?.capital == null) return true;
        WorldTile capital = pKingdom.capital.getTile();
        __result = false;
        if (capital == null) return false;
        foreach (City city in pKingdom.getCities())
        {
            if (CityPopulationSystem.Households(city) > ExpansionMinimumHouseholds) continue;
            WorldTile tile = city.getTile();
            if (tile != null && tile.reachableFrom(capital))
            {
                __result = true;
                break;
            }
        }
        return false;
    }

    public static void AfterMoveSomeUnitsToNewCity(City pNewCity, City pFromCity)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled) return;
        CityPopulationSystem.TransferBackground(pFromCity, pNewCity, SettlerShare);
    }

    public static void AfterCountTotalWarriors(Kingdom __instance, ref int __result)
    {
        if (!CityPopulationSystem.VanillaScale || !CityPopulationSystem.AbstractPopulationEnabled) return;
        __result = Math.Max(__result, CityPopulationSystem.WarPotential(__instance));
    }

    private static bool InVanillaScale(Func<bool> check)
    {
        CityPopulationSystem.EnterVanillaScale();
        try
        {
            return check();
        }
        finally
        {
            CityPopulationSystem.ExitVanillaScale();
        }
    }

    public static void EnterScale() => CityPopulationSystem.EnterVanillaScale();

    // 野生动物繁殖上限：无小人模式下地图上几乎没有平民打猎、开荒，野生动物会一路繁殖到几万只，
    // 每帧逐个更新拖垮帧率(暂停时原版仍逐个处理可见性和死亡检查)。全图非文明生物超过上限就不再繁殖
    private static int _wildCount;
    private static float _wildCountedAt = -100f;
    private const float WildCountInterval = 5f;

    // 全图野生生物上限：每座城 WildPerCity 只(畜牧、狩猎的产出已经按地形自动结算，地图上的动物只是点缀)
    private const int WildPerCity = 2;
    public static int WildCap => (World.world?.cities?.Count ?? 0) * WildPerCity;

    public static int WildCount()
    {
        if (Time.unscaledTime - _wildCountedAt < WildCountInterval) return _wildCount;
        _wildCountedAt = Time.unscaledTime;
        int count = 0;
        foreach (Actor actor in World.world.units)
            if (actor?.asset != null && !actor.asset.civ && !actor.asset.is_boat) count++;
        return _wildCount = count;
    }

    public static void AfterIsMetaLimitsReached(Actor pActor, ref bool __result)
    {
        if (__result || !CityPopulationSystem.AbstractPopulationEnabled || pActor?.asset == null || pActor.asset.civ) return;
        if (WildCount() >= WildCap) __result = true;
    }

    // 超出上限的野生生物分帧清掉(每帧最多 CullPerFrame 只，静默移除、不计死亡)，先清火元素等怪物，再清普通动物；
    // 收藏和镜头跟随的不动
    private const int CullPerFrame = 400;
    private static readonly global::System.Collections.Generic.List<Actor> _cullBuffer = new();
    private static object _cullWorld;
    private static int _cullCursor;
    private static bool _cullGame;

    public static void CullExcessWild()
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || World.world?.units == null || Config.paused ||
            !Config.game_loaded) return;
        if (!ReferenceEquals(_cullWorld, World.world))
        {
            _cullWorld = World.world;
            _cullCursor = 0;
            _cullGame = false;
            _wildCountedAt = -100f;
        }
        int excess = WildCount() - WildCap;
        if (excess <= 0) return;
        _cullBuffer.Clear();
        var actors = World.world.units.getSimpleList();
        int count = actors.Count;
        if (count == 0) return;
        if (_cullCursor >= count) { _cullCursor = 0; _cullGame = !_cullGame; }
        bool gamePass = _cullGame;
        for (int scanned = 0; scanned < count && _cullCursor < count && _cullBuffer.Count < CullPerFrame &&
             (scanned == 0 || EmpireCraft.Scripts.HelperFunc.SimulationFrameBudget.HasTime); scanned++)
        {
            Actor actor = actors[_cullCursor++];
            if (actor?.asset == null || actor.asset.civ || actor.asset.is_boat || !actor.isAlive()) continue;
            if (actor.isFavorite() || actor.isCameraFollowingUnit() || AnimalHusbandrySystem.IsGame(actor.asset) != gamePass) continue;
            _cullBuffer.Add(actor);
        }
        if (_cullCursor >= count) { _cullCursor = 0; _cullGame = !_cullGame; }
        int removed = 0;
        foreach (Actor actor in _cullBuffer)
        {
            if (removed >= excess || removed > 0 && !EmpireCraft.Scripts.HelperFunc.SimulationFrameBudget.HasTime) break;
            try
            {
                actor.die(true, AttackType.Other, false, false);
                removed++;
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft] 清理野生生物失败: {exception.Message}");
                break;
            }
        }
        _wildCount = Mathf.Max(0, _wildCount - removed);
        _cullBuffer.Clear();
    }

    public static void AfterShowTextClanCity(NameplateText __instance, Clan pMetaObject, City pCity)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || pCity == null || __instance == null) return;
        var magnates = VirtualGenealogySystem.MagnatesOf(pCity);
        if (magnates.Count == 0) return;
        var names = new global::System.Collections.Generic.List<string>();
        foreach (EmpireCraft.Scripts.System.SpecificClan clan in magnates) names.Add(clan.GetDisplayName());
        string text = __instance._text_name.text + "  " + string.Format(NeoModLoader.General.LM.Get("clan_layer_magnates"),
            string.Join("、", names));
        __instance.setText(text, pCity.city_center);
    }

    public static bool BeforeUpdateUnitSpawn() =>
        !CityPopulationSystem.AbstractPopulationEnabled || WildCount() < WildCap;

    public static void AfterIsPlacePrivateForBreeding(Actor __instance, ref bool __result)
    {
        if (!__result || !CityPopulationSystem.AbstractPopulationEnabled || __instance?.asset == null ||
            __instance.asset.civ || __instance.hasCity()) return;
        if (WildCount() >= WildCap) __result = false;
    }

    public static Exception ExitScale(Exception __exception)
    {
        CityPopulationSystem.ExitVanillaScale();
        return __exception;
    }

    #endregion

    #region 人口统计一律计入背景人口

    // 世界统计(智慧生物人口、总人口)，以及文化、宗教、语言、亚种的人口：原版只数实体单位，
    // 这里补上各城的背景人口与在外军团(城市、王国、联盟在别处已经补上)。文化按人口组和军团来源归类；
    // 宗教、语言、主要亚种尚无人口组对应的原版对象 id，仍按所在城市归类。每帧最多汇总一次。
    private static void PatchPopulationQueries()
    {
        try
        {
            var harmony = new Harmony(nameof(NoCommonersPatch) + ".Population");
            harmony.Patch(AccessTools.Method(typeof(MapBox), nameof(MapBox.getCivWorldPopulation)),
                postfix: new HarmonyMethod(typeof(NoCommonersPatch), nameof(AfterCivWorldPopulation)));
            // MetaObject<T>.getPopulationPeople 是泛型基类的方法：引用类型的各个实例化可能共用同一份代码，
            // 每个都打一次，最终生效的都是同一个按类型分派的后置方法，不会重复相加
            foreach (Type type in new[] { typeof(Culture), typeof(Religion), typeof(Language), typeof(Subspecies) })
            {
                global::System.Reflection.MethodInfo method = AccessTools.Method(type, "getPopulationPeople");
                if (method != null)
                    harmony.Patch(method, postfix: new HarmonyMethod(typeof(NoCommonersPatch), nameof(AfterMetaPopulation)));
            }
            // 原版：文化、宗教、语言、亚种没有实体单位(也没有藏书)就整个删掉。无小人模式下城里实体单位很少，
            // 还有成千上万背景人口信着的文化也会被删，城市随之失去文化。还有背景人口属于它时不删
            foreach (Type type in new[] { typeof(Culture), typeof(Religion), typeof(Language), typeof(Subspecies) })
            {
                global::System.Reflection.MethodInfo method = AccessTools.Method(type, "isReadyForRemoval");
                if (method != null)
                    harmony.Patch(method, postfix: new HarmonyMethod(typeof(NoCommonersPatch), nameof(AfterReadyForRemoval)));
            }
            StatisticsAsset total = AssetManager.statistics_library?.get("world_statistics_population_total");
            if (total?.long_action != null)
            {
                StatisticsLongAction original = total.long_action;
                total.long_action = asset => original(asset) + (CityPopulationSystem.AbstractPopulationEnabled
                    ? WorldBackground() : 0L);
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 无小人模式人口统计补丁未生效: {exception.Message}");
        }
    }

    private static int _indexFrame = -1;
    private static int _cultureIndexFrame = -1;
    private static object _indexWorld;
    private static long _worldBackground;
    private static readonly global::System.Collections.Generic.Dictionary<object, int> MetaBackground = new();
    private static readonly global::System.Collections.Generic.Dictionary<string, float> CultureBackground = new();
    private static readonly global::System.Collections.Generic.Dictionary<Culture, int> CultureMetaBackground = new();

    public static void ResetPopulationIndex()
    {
        _indexWorld = World.world;
        _indexFrame = -1;
        _cultureIndexFrame = -1;
        MetaBackground.Clear();
        CultureBackground.Clear();
        CultureMetaBackground.Clear();
        _worldBackground = 0L;
        LegionStatisticsReadCache.Reset();
    }

    private static void AddCulture(string key, float amount, Culture fallback)
    {
        if (amount <= 0f) return;
        if (string.IsNullOrEmpty(key)) { AddCultureObject(fallback, Mathf.RoundToInt(amount)); return; }
        CultureBackground.TryGetValue(key, out float current);
        CultureBackground[key] = current + amount;
    }

    private static void RebuildIndex()
    {
        if (!ReferenceEquals(_indexWorld, World.world)) ResetPopulationIndex();
        int frame = UnityEngine.Time.frameCount;
        if (frame == _indexFrame) return;
        using var timing = FrameProfiler.Measure("虚拟人口·全图统计索引");
        _indexFrame = frame;
        _worldBackground = 0L;
        MetaBackground.Clear();
        if (World.world?.cities == null) return;
        foreach (City city in World.world.cities)
        {
            if (city?.data == null || city.isRekt()) continue;
            int background = CityPopulationSystem.BackgroundCount(city);
            _worldBackground += background;
            Add(city.religion, background);
            Add(city.language, background);
            Subspecies subspecies = null;
            try
            {
                subspecies = city.getMainSubspecies();
            }
            catch
            {
                // 没有主要亚种就不归类
            }
            Add(subspecies, background);
        }
    }

    // 世界总人口、宗教和语言查询不需要扫描军团。只有文化查询才读取来源。
    private static void RebuildCultureIndex()
    {
        if (!ReferenceEquals(_indexWorld, World.world)) ResetPopulationIndex();
        int frame = Time.frameCount;
        if (frame == _cultureIndexFrame) return;
        using var timing = FrameProfiler.Measure("虚拟人口·文化统计索引");
        _cultureIndexFrame = frame;
        CultureBackground.Clear();
        CultureMetaBackground.Clear();
        if (World.world?.cities == null) return;
        foreach (City city in World.world.cities)
        {
            if (city?.data == null || city.isRekt()) continue;
            foreach (PopGroup group in CityPopulationSystem.Get(city).groups)
                if (group != null) AddCulture(group.culture, group.Background, city.culture);
            if (city.units == null) continue;
            foreach (Actor actor in city.units)
                foreach (var origin in LegionStatisticsReadCache.Read(actor))
                    AddCulture(origin.Culture, origin.Size, city.culture);
        }
        // 一个模板可以对应多个原版 Culture；人数只归一个对象，避免汇总翻倍。
        var assigned = new global::System.Collections.Generic.HashSet<string>();
        if (World.world.cultures != null)
            foreach (Culture culture in World.world.cultures)
            {
                string key = CulturePatch.GetInjectedCultureName(culture);
                if (!string.IsNullOrEmpty(key) && assigned.Add(key) &&
                    CultureBackground.TryGetValue(key, out float amount)) AddCultureObject(culture, Mathf.RoundToInt(amount));
            }
    }

    private static void AddCultureObject(Culture culture, int amount)
    {
        if (culture == null) return;
        CultureMetaBackground.TryGetValue(culture, out int current);
        CultureMetaBackground[culture] = (int)Math.Min(int.MaxValue, (long)current + amount);
    }

    private static void Add(object meta, int amount)
    {
        if (meta == null) return;
        MetaBackground.TryGetValue(meta, out int current);
        MetaBackground[meta] = (int)Math.Min(int.MaxValue, (long)current + amount);
    }

    private static long WorldBackground()
    {
        RebuildIndex();
        return _worldBackground;
    }

    public static void AfterReadyForRemoval(object __instance, ref bool __result)
    {
        if (!__result || !CityPopulationSystem.AbstractPopulationEnabled ||
            __instance is not (Culture or Religion or Language or Subspecies)) return;
        if (__instance is Culture culture)
        {
            RebuildCultureIndex();
            if (CultureBackground.TryGetValue(CulturePatch.GetInjectedCultureName(culture) ?? "", out float people) && people > 0f ||
                CultureMetaBackground.TryGetValue(culture, out int count) && count > 0) __result = false;
        }
        else
        {
            RebuildIndex();
            if (MetaBackground.TryGetValue(__instance, out int background) && background > 0) __result = false;
        }
    }

    public static void AfterCivWorldPopulation(ref int __result)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled) return;
        __result = (int)Math.Min(int.MaxValue, __result + WorldBackground());
    }

    public static void AfterMetaPopulation(object __instance, ref int __result)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled ||
            __instance is not (Culture or Religion or Language or Subspecies)) return;
        if (__instance is Culture culture)
        {
            RebuildCultureIndex();
            if (CultureMetaBackground.TryGetValue(culture, out int background))
                __result = (int)Math.Min(int.MaxValue, (long)__result + background);
        }
        else
        {
            RebuildIndex();
            if (MetaBackground.TryGetValue(__instance, out int background))
                __result = (int)Math.Min(int.MaxValue, (long)__result + background);
        }
    }

    #endregion

    // 返回 false 表示不加这个状态
    public static bool BeforeAddStatusEffect(Actor __instance, string __0)
    {
        if (__0 != "sleeping" || !CityPopulationSystem.AbstractPopulationEnabled) return true;
        return __instance?.city == null ||
               EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance);
    }

    public static void AfterUpdateCityStatus(City __instance)
    {
        if (__instance?.status == null || !CityPopulationSystem.AbstractPopulationEnabled ||
            EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return;
        // 太平时只驻扎一名实体将领：原版兵额压到 1，免得把留下的劳动者拉去当兵；交战时兵额照常，由征召兵补足
        // 兵额一律按军制(见 CityPopulationSystem.LevyTarget)：不是军镇的城为 0，军镇按平时/战时兵额
        __instance.status.warrior_slots = CityPopulationSystem.LevyTarget(__instance);
        // 背景人口和在外军团都没有时，原版的统计就是实体单位数，不用改
        if (CityPopulationSystem.BackgroundCount(__instance) <= 0) return;
        int background = CityPopulationSystem.BackgroundHouseholds(__instance);
        // 原版的人口统计经过 getPopulationPeople(真实人数，可达几百万)。原版机制(岗位分配要按人头循环、
        // 兵额按成年人数算、住房按位数算)改按"户"：实体单位 + 背景人口折成的户数
        int units = 0;
        if (__instance.units != null)
            foreach (Actor actor in __instance.units)
                if (actor != null && !actor.isRekt() && actor.isAlive()) units++;
        __instance.status.population = units + background;
        __instance.status.population_adults = Math.Max(0, __instance.status.population - __instance.status.population_children);
        __instance.status.housing_occupied = Math.Min(__instance.status.housing_total, __instance.status.population);
        __instance.status.housing_free = Math.Max(0, __instance.status.housing_total - __instance.status.population);
    }
}

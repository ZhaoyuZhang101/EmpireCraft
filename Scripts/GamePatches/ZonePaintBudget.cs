using System.Collections.Generic;
using System.Diagnostics;
using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

// 地块上色分帧(纯净地图、平滑国界下一块地上色要几十到一百多微秒)：
//   · 原版"全图重画"(dirtyAndClear：国家换色、帝国变动、分封等都会触发)把五千多块地挤在同一帧上色，卡半秒多；
//   · 这里每帧上色到预算用完为止，余下的地块留到下一轮(原版每帧巡一遍要重画的地块，没画的下一轮还会画)；
//     屏幕里的地块预算宽一些，先画看得见的；
//   · 空白地块(不在"已画"名单里：刚建的城、读档后头一遍)预算宽得多(BlankBudgetMs)，尽快画上，但也不再不限量；
//   · 全图重画、清空已画地块(原版 clearCurrentDrawnZones：切换图层选项、选中对象变了、缩放跨过远景门槛)时不先把色块清成透明
//     (原版先清空再重画，分帧画会露出一块块空白)，旧颜色留到新颜色画上为止，不再归任何对象的地块照原版在下一轮清掉；
//   · 平滑国界全图换色(换图层、初次进图，见 SmoothBorders.RepaintAll)：先算齐再一起翻——算色期间要一起翻的范围里的
//     按 LayerPrecomputeBudgetMs 画、范围外的不画，每帧都巡检(原版跟着模拟步走、不一定每帧一轮，没巡检的那一帧补跑一轮，
//     见 ForceRound)；算齐后镜头外按 CatchUpBudgetMs 补画；
//   · 原版每轮都从头按国、按城巡检，预算总被排在前面的地块用掉：镜头外自身归属或颜色变了的、排了 AgedSeconds 秒还没画的
//     预算放宽到 CatchUpBudgetMs，不让排在后面的地块几十秒轮不到(图层诊断"没刷新的色块")。
// 只在纯净地图或平滑国界开着时生效；每帧上色耗时记在帧率体检里(纯净地图·地块上色)。
public class ZonePaintBudget : GamePatch
{
    public ModDeclare declare { get; set; }
    // 镜头里正在换色、等着画新样子的地块(见 SmoothBorders.Awaiting)与悬停、刚移开的省(SmoothBorders.HoverUrgent)预算放宽到
    // UrgentBudgetMs，整片换色尽快画齐、一起翻，悬停跟手。以前镜头外的也算急、预算 30 毫秒，
    // 大片换色时"纯净地图·地块上色"常顶到 30 毫秒；镜头外的翻牌看不见，按普通预算画
    private const double BudgetMs = 6d, VisibleBudgetMs = 14d, UrgentBudgetMs = 16d;
    // 空白地块：以前当帧不限量全部画上，读档、缩放跨过远景门槛(原版清空整图)时五千多块挤在一帧，
    // 性能日志"原版·updateMapLayers"一帧 500~570 毫秒；现在每帧画到 40 毫秒为止，余下的下一帧接着按这个预算画
    private const double BlankBudgetMs = 40d;
    // 全图换色算色期间范围里每帧画多少：每帧都画、不一次画完(用户 2026-10-22 "一次性算完颜色会造成卡顿")；
    // 以前每轮 24 毫秒，原版又隔一帧才巡检一轮。补画镜头外、镜头外自身变了色的、排得太久的：低于镜头里的预算，
    // 但不再只有 6 毫秒、排在后面的总轮不到
    private const double LayerPrecomputeBudgetMs = 16d, CatchUpBudgetMs = 12d;
    private const float AgedSeconds = 2f;
    private const int MinZonesPerFrame = 6;
    private static readonly double TicksPerMs = Stopwatch.Frequency / 1000d;

    private static int _frame = -1, _painted, _deferredFrame = -10;
    private static long _spent, _started;
    // 因预算延后的空白地块：原版照常调 drawEnd 记进"已画"名单(不会被当成多余的块清掉)，但它还是空的(或旧的)，
    // 下一帧仍按空白地块的预算画，不落到普通预算里慢慢排
    private static readonly HashSet<int> Blank = new();
    // 空白地块分帧的统计：这一阵延后了多少帧、多少块次(画完后记一行日志，看分帧是否常被触发)
    private static int _blankFrames, _blankDeferred, _blankLastFrame = -10000, _blankCountedFrame = -1;
    // 正在缩放跨过远景门槛(QualityChanger.setZoomOrthographic 里原版清空整图)
    private static bool _zoomSwitch;
    // 上次清空已画地块的时刻：之后一秒镜头里的地块按"急着画"的预算上色(同平滑国界换图层)，新样子尽快盖住旧的
    private static float _clearedAt = -10f;
    private const float ClearUrgentSeconds = 1f;

    private static bool Active => PureMapMode.Enabled || SmoothBorders.Enabled;

    // 这一两帧里有地块因为预算没画、留到下一轮(换色渐变等这批画完再统一起扫描，见 SmoothBorders)
    public static bool Backlogged => Time.frameCount - _deferredFrame <= 1;

    // 原版巡检的轮次(redrawZones 每调一次加一，见 BeforeRedraw)；这一轮巡检了几块、要一起翻的范围里因预算延后了几块
    public static int Round { get; private set; }
    private static int _roundChecks, _roundDeferredView, _lastRoundFrame = -1;
    // 原版 redrawZones 画完一轮把计时设成这个数(隔一帧才画下一轮)
    private const float RedrawInterval = 0.01f;

    // 每块地从哪一刻起因预算排着等上色(0 没在等)；换图层算齐期间延后过、还没画上新样子的块
    private static float[] _waitSince = global::System.Array.Empty<float>();
    private static bool[] _layerPending = global::System.Array.Empty<bool>();
    private static int _layerPendingCount, _lastPendingPainted = -1;
    private static object _waitWorld;

    public void Initialize()
    {
        var harmony = new Harmony(nameof(ZonePaintBudget));
        harmony.Patch(AccessTools.Method(typeof(TileZone), nameof(TileZone.checkShouldReRender)),
            prefix: new HarmonyMethod(typeof(ZonePaintBudget), nameof(BeforeCheck)),
            postfix: new HarmonyMethod(typeof(ZonePaintBudget), nameof(AfterCheck)));
        harmony.Patch(AccessTools.Method(typeof(ZoneCalculator), "applyMetaColorsToZone"),
            prefix: new HarmonyMethod(typeof(ZonePaintBudget), nameof(BeginPaint)) { priority = Priority.First },
            postfix: new HarmonyMethod(typeof(ZonePaintBudget), nameof(EndPaint)) { priority = Priority.Last });
        harmony.Patch(AccessTools.Method(typeof(ZoneCalculator), nameof(ZoneCalculator.dirtyAndClear)),
            prefix: new HarmonyMethod(typeof(ZonePaintBudget), nameof(BeforeDirtyAndClear)));
        harmony.Patch(AccessTools.Method(typeof(ZoneCalculator), "checkFadeAndSelectionColors"),
            prefix: new HarmonyMethod(typeof(ZonePaintBudget), nameof(BeforeSelectionColors)));
        harmony.Patch(AccessTools.Method(typeof(ZoneCalculator), "checkClearAllZonesToRedraw"),
            prefix: new HarmonyMethod(typeof(ZonePaintBudget), nameof(BeforeClearAll)));
        harmony.Patch(AccessTools.Method(typeof(ZoneCalculator), "drawZoneClear"),
            postfix: new HarmonyMethod(typeof(ZonePaintBudget), nameof(AfterZoneClear)));
        harmony.Patch(AccessTools.Method(typeof(ZoneCalculator), "clearCurrentDrawnZones"),
            prefix: new HarmonyMethod(typeof(ZonePaintBudget), nameof(BeforeClearDrawn)));
        harmony.Patch(AccessTools.Method(typeof(QualityChanger), "setZoomOrthographic"),
            prefix: new HarmonyMethod(typeof(ZonePaintBudget), nameof(BeforeZoom)),
            finalizer: new HarmonyMethod(typeof(ZonePaintBudget), nameof(AfterZoom)));
        harmony.Patch(AccessTools.Method(typeof(ZoneCalculator), "redrawZones"),
            prefix: new HarmonyMethod(typeof(ZonePaintBudget), nameof(BeforeRedraw)),
            postfix: new HarmonyMethod(typeof(ZonePaintBudget), nameof(AfterRedraw)));
    }

    // 原版清空了一块地：平滑国界忘掉它记下的底色、渐变(见 SmoothBorders.Cleared)；它也不再排队等上色
    private static void AfterZoneClear(TileZone pZone)
    {
        Painting(pZone);
        SmoothBorders.Cleared(pZone);
    }

    // 原版巡检一轮之前：换图层、换主的地块等着画新样子好一起翻时，每帧都巡检
    // (原版画完一轮计时设 0.01 秒、下一帧减掉帧时长就跳过，实际隔一帧才画一轮；一帧里模拟跑几次的也只多补这一次)
    private static void BeforeRedraw(ZoneCalculator __instance)
    {
        Round++;
        _roundChecks = 0;
        _roundDeferredView = 0;
        if (!Active) return;
        // 这一帧已经巡检过一轮(全图换色补跑的，或模拟加速时一帧跑两步)：这一轮跳过，一帧至多一轮
        if (_lastRoundFrame == Time.frameCount)
        {
            __instance._redraw_timer = Mathf.Max(__instance._redraw_timer, RedrawInterval);
            return;
        }
        if (__instance._redraw_timer <= 0f) return;
        if (SmoothBorders.WantsEveryRound) __instance._redraw_timer = 0f;
    }

    // 原版巡检完一轮：告诉平滑国界这一轮要一起翻的范围里还差几块(算齐了就起翻)
    private static void AfterRedraw(ZoneCalculator __instance)
    {
        if (__instance._redraw_timer != RedrawInterval && _roundChecks == 0) return;
        _lastRoundFrame = Time.frameCount;
        if (!Active) return;
        SmoothBorders.LayerRoundEnd(Round, ReferenceEquals(__instance._mode_asset, World.world?.getCachedMapMetaAsset()),
            _roundDeferredView, _layerPendingCount);
    }

    // 全图换色算色期间这一帧原版还没巡检(原版巡检跟着模拟步走，不一定每帧一轮)：补跑一轮，每帧都有进展
    public static void ForceRound(ZoneCalculator calc)
    {
        if (!Active || calc == null || _lastRoundFrame == Time.frameCount || calc._mode_asset == null ||
            !ReferenceEquals(calc._mode_asset, World.world?.getCachedMapMetaAsset())) return;
        calc._redraw_timer = 0f;
        calc.redrawZones();
    }

    // 换图层：之前记下的"还没画上新样子"作废，从这一刻重新记
    public static void ResetLayerPending()
    {
        if (_layerPending.Length > 0) global::System.Array.Clear(_layerPending, 0, _layerPending.Length);
        _layerPendingCount = 0;
        _lastPendingPainted = -1;
    }

    private static void EnsureWait(int count)
    {
        if (!ReferenceEquals(_waitWorld, World.world))
        {
            _waitWorld = World.world;
            _waitSince = global::System.Array.Empty<float>();
            _layerPending = global::System.Array.Empty<bool>();
            _layerPendingCount = 0;
        }
        if (_waitSince.Length >= count) return;
        global::System.Array.Resize(ref _waitSince, count);
        global::System.Array.Resize(ref _layerPending, count);
    }

    // 这块地要画了(或被清掉了)：不再排队
    private static void Painting(TileZone zone)
    {
        int id = zone?.id ?? -1;
        if (id < 0 || id >= _waitSince.Length) return;
        _waitSince[id] = 0f;
        if (!_layerPending[id]) return;
        _layerPending[id] = false;
        _layerPendingCount--;
        _lastPendingPainted = id;
    }

    // 正要画的这块地是换图层以来头一次画上新图层(算齐期间延后过；见 SmoothBorders.CaptureLayerFlip)
    public static bool FirstLayerPaint(TileZone zone) => zone != null && zone.id == _lastPendingPainted;

    // 这块地排着等上色多久了(秒)
    private static float Waited(TileZone zone)
    {
        int id = zone.id;
        return id >= 0 && id < _waitSince.Length && _waitSince[id] > 0f ? Time.unscaledTime - _waitSince[id] : 0f;
    }

    // 图层诊断用：这块没刷新的色块是排着队没轮到，还是原版根本没来重画(重画指纹漏了的变动)
    internal static string WaitNote(TileZone zone)
    {
        if (zone == null) return "";
        float waited = Waited(zone);
        string where = zone.visible ? "镜头里" : "镜头外";
        return waited > 0f ? $"({where}，排着等上色 {waited:0.0} 秒)" : $"({where}，没在排队：原版没来重画)";
    }

    // 一格的样子变了(设地面格、顶层格前后 Type 不同，见 MapWallPatch：水桶抽水抽岩浆、镐铲斧、画地形、岩浆冷却、海水回灌、开田退耕、
    // 结霜化冻……)。图层色块只是盖在地形上的一层半透明颜色，地形由原版自己画在底下(纯净地图也是，见 PureMapPatch.FillAlpha；
    // 以前纯净地图把地形明暗画进不透明的色块，结一次霜就要整块重画，和换色抢上色预算，用户 2026-10-26 "不然每次地形变更都要同步格子，
    // 那就会经常出现国家颜色不刷新的问题")，所以地面样子变了色块不必重画。只有水陆变了要重画：纯净地图、平滑国界下水面不涂国色、
    // 国界贴着海岸走(抽干的湖不重画就一直是空的)，这一格四邻落在别的块上的，那块的边框也跟着重画；平滑国界的上色缓存不含地形，一并丢掉。
    // 城里的地块水陆、山地、阻挡变了，城市平面图跟着重排(桥、城墙、街巷避开水和山)，只重画城市平面图层的那块地(见 CityPlanOverlay)
    public static void TerrainChanged(WorldTile tile, TileTypeBase before)
    {
        TileTypeBase after = tile?.Type;
        if (ReferenceEquals(before, after) || tile.zone == null || !Config.game_loaded || SmoothLoader.isLoading() ||
            World.world?.zone_calculator == null) return;
        bool water = Water(before) != Water(after);
        if (tile.zone.city != null && (water || PlanTerrain(before) != PlanTerrain(after)))
            EmpireCraft.Scripts.UI.Components.CityPlanOverlay.MarkZone(tile.zone);
        if (!water || !PureMapMode.Enabled && !SmoothBorders.Enabled) return;
        Repaint(tile.zone);
        Repaint(tile.tile_up?.zone, tile.zone);
        Repaint(tile.tile_down?.zone, tile.zone);
        Repaint(tile.tile_left?.zone, tile.zone);
        Repaint(tile.tile_right?.zone, tile.zone);
    }

    // 水面(同 SmoothBorders.IsWater、PureMapPatch.FinishZone：岩浆不算水，照常归地块的主人)
    private static bool Water(TileTypeBase type) => type != null && (type.ocean || type.liquid && !type.lava);

    // 城市平面图看的地形：液体、山地、阻挡格
    private static int PlanTerrain(TileTypeBase type) =>
        type == null ? 0 : (type.liquid ? 1 : 0) | (type.mountains ? 2 : 0) | (type.block ? 4 : 0);

    private static void Repaint(TileZone zone, TileZone skip = null)
    {
        if (zone == null || ReferenceEquals(zone, skip)) return;
        zone.resetRenderHelpers();
        SmoothBorders.ForgetSnapshot(zone);
    }

    private static void BeforeZoom() => _zoomSwitch = true;
    private static void AfterZoom() => _zoomSwitch = false;

    // 原版清空所有已画地块(切换图层选项、选中对象变了、清色设置变了、缩放跨过远景门槛)：原版逐块清成透明、移出"已画"名单，
    // 下一轮这五千多块都成了空白地块、当帧全部重画，平滑国界的上色缓存也随清空一起丢掉(见 SmoothBorders.Cleared)。
    // 这里只让它们下一轮重画(同 BeforeDirtyAndClear)：旧颜色和上色缓存留着，分帧画上新样子；不再归谁的地块照原版下一轮清掉。
    // 纯净地图下缩放跨过远景门槛时连重画都不必：清除色恒为不透明(见 PureMapPatch.BeforeShouldBeClearColor)，
    // 色块与远近无关，悬停、选中随远近变了另由 checkClearAllZonesToRedraw 只重新提亮(见 BeforeClearAll)。
    // 没开纯净地图(只开平滑国界)时近看内部要清成透明，照样重画，只是分帧
    private static bool BeforeClearDrawn(ZoneCalculator __instance, bool pCleanTimer)
    {
        if (!Active) return true;
        if (_zoomSwitch && PureMapMode.Enabled) return false;
        foreach (TileZone zone in __instance._current_drawn_zones) zone.resetRenderHelpers();
        if (pCleanTimer) __instance.clearAllRedrawTimers();
        _clearedAt = Time.unscaledTime;
        return false;
    }

    private static void NewFrame()
    {
        int frame = Time.frameCount;
        if (_frame == frame) return;
        _frame = frame;
        _painted = 0;
        _spent = 0L;
        // 隔一阵没有延后的空白地块才算画完(原版隔一帧才巡检一轮，隔两帧就记，一阵分帧被拆成一帧一行)
        if (_blankFrames > 0 && frame - _blankLastFrame > 30) ReportBlank();
        // 一阵子没有延后的空白地块：清掉残留的记号(延后后又不归谁、不再画的块)
        if (Blank.Count > 0 && frame - _blankLastFrame > 600) Blank.Clear();
    }

    // 空白地块分帧画完：记一行日志(一阵一行)
    private static void ReportBlank()
    {
        LogService.LogInfo($"[EmpireCraft][地块上色] 空白地块超过每帧 {BlankBudgetMs:0} 毫秒的预算，分 {_blankFrames} 帧画完，" +
                           $"延后 {_blankDeferred} 块次");
        _blankFrames = 0;
        _blankDeferred = 0;
    }

    // 原版比对的指纹补上实际控制方、实际颜色、所属帝国(见 SmoothBorders.Fingerprint)：这些变了下一轮巡检就重画
    private static void BeforeCheck(TileZone __instance, ref int pColorAssetID)
    {
        if (!Active) return;
        _roundChecks++;
        int extra = SmoothBorders.Fingerprint(__instance);
        if (extra != 0) pColorAssetID = unchecked(pColorAssetID * 397 ^ extra);
    }

    // 这一帧的预算用完了：这块地先不画，下一轮再画
    private static void AfterCheck(TileZone __instance, ref bool __result)
    {
        if (!__result || !Active) return;
        NewFrame();
        ZoneCalculator calc = World.world?.zone_calculator;
        if (calc == null) return;
        EnsureWait(calc.zones?.Count ?? 0);
        // 换图层那一帧原版还按旧图层画一轮(setMode 在这之后)，平滑国界却已按新图层取归属，画出来是新图层的形状配旧图层的颜色：
        // 平滑国界下这一轮不画，下一轮按新图层画
        if (SmoothBorders.Enabled && !ReferenceEquals(calc._mode_asset, World.world.getCachedMapMetaAsset()))
        {
            Defer(__instance, ref __result);
            return;
        }
        double spent = _spent / TicksPerMs;
        // 全图换色还在算色：要一起翻的范围里的按算色预算画，范围外的一概留到算齐之后(算力全给镜头里)
        if (SmoothBorders.LayerPrecomputing)
        {
            bool view = SmoothBorders.InLayerView(__instance);
            if (view && (_painted < MinZonesPerFrame || spent < LayerPrecomputeBudgetMs))
            {
                Painting(__instance);
                return;
            }
            if (view) _roundDeferredView++;
            int id = __instance.id;
            if (id >= 0 && id < _layerPending.Length && !_layerPending[id])
            {
                _layerPending[id] = true;
                _layerPendingCount++;
            }
            Defer(__instance, ref __result);
            return;
        }
        if (_painted < MinZonesPerFrame)
        {
            if (Blank.Count > 0) Blank.Remove(__instance.id);
            Painting(__instance);
            return;
        }
        bool visible = __instance.visible;
        // 换图层起翻后补画镜头外、镜头外自身的归属或颜色变了(不画就一直是错色)、排了太久的(原版每轮都从头按国、按城巡检，
        // 预算总被排在前面的用掉，排在后面的地块几十秒轮不到：图层诊断"没刷新的色块"多是这种)：镜头外的预算放宽到 CatchUpBudgetMs
        bool catchUp = SmoothBorders.LayerBackfilling || SmoothBorders.OwnChanged(__instance) || Waited(__instance) >= AgedSeconds;
        // 空白地块(不在"已画"名单里，或上一帧因预算延后的空白地块)：按宽得多的预算尽快画上；
        // 换图层补画期间镜头外的按补画预算(起翻那几帧不再一下子顶到 40 毫秒)
        if (!calc._current_drawn_zones.Contains(__instance) || Blank.Count > 0 && Blank.Contains(__instance.id))
        {
            bool backfill = !visible && SmoothBorders.LayerBackfilling;
            if (spent < (backfill ? CatchUpBudgetMs : BlankBudgetMs))
            {
                if (Blank.Count > 0) Blank.Remove(__instance.id);
                Painting(__instance);
                return;
            }
            if (!backfill)
            {
                Blank.Add(__instance.id);
                int frame = Time.frameCount;
                if (_blankCountedFrame != frame)
                {
                    _blankCountedFrame = frame;
                    _blankFrames++;
                }
                _blankDeferred++;
                _blankLastFrame = frame;
            }
            Defer(__instance, ref __result);
            return;
        }
        if (spent < BudgetMs)
        {
            Painting(__instance);
            return;
        }
        // 镜头外的：普通预算用完就留到下一轮，只有上面几种放宽(TileZone.visible 由原版 ZoneCamera 每帧维护，
        // 与 getVisibleZones 同一份，不再每帧另建一个 HashSet)
        if (!visible)
        {
            if (catchUp && spent < CatchUpBudgetMs) Painting(__instance);
            else Defer(__instance, ref __result);
            return;
        }
        if (spent < VisibleBudgetMs ||
            spent < UrgentBudgetMs && (catchUp || Time.unscaledTime - _clearedAt < ClearUrgentSeconds ||
                                       SmoothBorders.Awaiting(__instance) || SmoothBorders.HoverUrgent(__instance)))
        {
            Painting(__instance);
            return;
        }
        Defer(__instance, ref __result);
    }

    // 这块地这一帧不画：原版之后照常调 drawEnd(记进"已画"名单、不会被当成多余的块清掉)，下一轮再画
    private static void Defer(TileZone zone, ref bool result)
    {
        result = false;
        _deferredFrame = Time.frameCount;
        zone.resetRenderHelpers();
        int id = zone.id;
        if (id >= 0 && id < _waitSince.Length && _waitSince[id] <= 0f) _waitSince[id] = Mathf.Max(Time.unscaledTime, 0.001f);
    }

    private static void BeginPaint()
    {
        if (!Active) return;
        NewFrame();
        _started = Stopwatch.GetTimestamp();
    }

    private static void EndPaint()
    {
        if (!Active || _started == 0L) return;
        long elapsed = Stopwatch.GetTimestamp() - _started;
        _spent += elapsed;
        _started = 0L;
        _painted++;
        FrameProfiler.Record("纯净地图·地块上色", elapsed);
    }

    // 选中的对象：原版让它的地块每帧重画、边框一闪一闪(呼吸动画)，这里上色贵，每帧重画整个国家拖得一帧一帧的。
    // 照原版定颜色(没选中的淡一些)，但不做呼吸动画、不每帧重画；选中、悬停的提亮见 SmoothBorders 的高亮
    private static bool BeforeSelectionColors(ZoneCalculator __instance, ref Color32 pColorBorderInside, ref Color32 pColorBorderOut,
        IMetaObject pMetaObjectToDraw, MetaTypeAsset pMetaTypeAsset, bool pFavorite, ref bool __result)
    {
        if (!Active) return true;
        bool selected = false;
        if (__instance._cached_check_animation)
        {
            if (__instance.shouldShowSelectionFor(pMetaObjectToDraw)) selected = true;
            else if (!__instance._selected_nano_object.isRekt())
            {
                pColorBorderInside.a = (byte)(pColorBorderInside.a * 0.6f);
                pColorBorderOut.a = (byte)(pColorBorderOut.a * 0.6f);
            }
        }
        bool shown = selected || (!SelectedUnit.isSet()
            ? __instance.checkShouldDrawObject(pFavorite)
            : SelectedUnit.unit.getMetaObjectOfType(pMetaTypeAsset.map_mode) == pMetaObjectToDraw);
        if (!shown)
        {
            pColorBorderInside.a = (byte)(pColorBorderInside.a * 0.5f);
            pColorBorderOut.a = (byte)(pColorBorderOut.a * 0.5f);
            if (__instance._cached_should_be_clear_color) pColorBorderInside = __instance._color_clear;
        }
        __result = false;
        return false;
    }

    // 鼠标移到另一个国家(城市、帝国……)上、选中的东西变了：原版把整张图清空重画——原版只是平涂，重画不费事，
    // 它要让没悬停的对象变淡、悬停的边框呼吸；纯净地图下色块不透明，变淡本来就看不出来，呼吸也改成了常亮，
    // 整图重画(这里一次几百毫秒)白白卡一下。纯净地图下这种情况只把新旧悬停、选中对象的格子重新提亮或还原(约一毫秒)，
    // 平滑国界下只是鼠标移到别的对象上也这样；平滑国界下切换图层选项不清空，分帧重画、翻牌；清色设置变了照原版重画
    // (原版的清空只让各块下一轮重画，见 BeforeClearDrawn)
    private static NanoObject _lastCursor, _lastSelected;

    private static bool BeforeClearAll(ZoneCalculator __instance)
    {
        bool layerChanged = SmoothBorders.PrepareLayerSwitch(__instance);
        NanoObject cursor = __instance._cursor_nano_object, selected = __instance._selected_nano_object;
        NanoObject oldCursor = _lastCursor, oldSelected = _lastSelected;
        _lastCursor = cursor;
        _lastSelected = selected;
        bool smooth = SmoothBorders.Enabled;
        if (!PureMapMode.Enabled && !smooth) return true;
        int state = World.world?.getCachedMapMetaAsset()?.getZoneOptionState() ?? -1;
        bool option = state != -1 && state != __instance._last_zone_state;
        bool clearColor = __instance._last_should_be_clear_color != __instance._cached_should_be_clear_color;
        // 切图层或改选项都保留旧图，目标画面分帧生成后，进入 SmoothBorders 的同一组全图翻转。
        if (smooth && (layerChanged || option && !clearColor))
        {
            __instance._selection_changed_dirty = false;
            __instance.setDrawnZonesDirty();
            foreach (TileZone zone in __instance._current_drawn_zones) zone.resetRenderHelpers();
            __instance.clearAllRedrawTimers();
            return false;
        }
        if (option || clearColor) return true;
        if (!__instance._selection_changed_dirty) return false;
        // 没开纯净地图时选中的对象变了：原版要让别的对象变淡，照原版重画；只是鼠标移到别的对象上，只重新提亮
        if (!PureMapMode.Enabled && selected != oldSelected) return true;
        __instance._selection_changed_dirty = false;
        SmoothBorders.Retint(oldCursor, oldSelected, cursor, selected);
        return false;
    }

    // 全图重画：只让各地块下一轮重画，不先清成透明
    private static bool BeforeDirtyAndClear(ZoneCalculator __instance)
    {
        if (!Active) return true;
        __instance.setDrawnZonesDirty();
        foreach (TileZone zone in __instance._current_drawn_zones) zone.resetRenderHelpers();
        __instance.clearAllRedrawTimers();
        return false;
    }
}

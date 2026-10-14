using System.Collections.Generic;
using System.Diagnostics;
using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

// 地块上色分帧(纯净地图、平滑国界下一块地上色要几十到一百多微秒)：
//   · 原版"全图重画"(dirtyAndClear：国家换色、帝国变动、分封等都会触发)把五千多块地挤在同一帧上色，卡半秒多；
//   · 这里每帧上色到预算用完为止，余下的地块留到下一轮(原版每帧巡一遍要重画的地块，没画的下一轮还会画)；
//     屏幕里的地块预算宽一些，先画看得见的；
//   · 只有还留着旧颜色的地块才分帧；切换图层、选中东西时原版先把色块清空了，空着的地块当帧全部画上，
//     不会一块一块地冒出来；
//   · 全图重画时不先把色块清成透明(原版先清空再重画，分帧画会露出一块块空白)，旧颜色留到新颜色画上为止，
//     不再归任何对象的地块照原版在下一轮清掉。
// 只在纯净地图或平滑国界开着时生效；每帧上色耗时记在帧率体检里(纯净地图·地块上色)。
public class ZonePaintBudget : GamePatch
{
    public ModDeclare declare { get; set; }
    // 正在换色、等着画新样子的地块(见 SmoothBorders.Awaiting)预算放宽到 UrgentBudgetMs，整片换色尽快画齐、一起扫
    private const double BudgetMs = 6d, VisibleBudgetMs = 14d, UrgentBudgetMs = 30d;
    private const int MinZonesPerFrame = 6;
    private static readonly double TicksPerMs = Stopwatch.Frequency / 1000d;

    private static int _frame = -1, _painted, _visibleFrame = -1, _deferredFrame = -10;
    private static long _spent, _started;
    private static readonly HashSet<int> Visible = new();

    private static bool Active => PureMapMode.Enabled || SmoothBorders.Enabled;

    // 这一两帧里有地块因为预算没画、留到下一轮(换色渐变等这批画完再统一起扫描，见 SmoothBorders)
    public static bool Backlogged => Time.frameCount - _deferredFrame <= 1;

    public void Initialize()
    {
        var harmony = new Harmony(nameof(ZonePaintBudget));
        harmony.Patch(AccessTools.Method(typeof(TileZone), nameof(TileZone.checkShouldReRender)),
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
    }

    // 原版清空了一块地：平滑国界忘掉它记下的底色、渐变(见 SmoothBorders.Cleared)
    private static void AfterZoneClear(TileZone pZone) => SmoothBorders.Cleared(pZone);

    private static void NewFrame()
    {
        int frame = Time.frameCount;
        if (_frame == frame) return;
        _frame = frame;
        _painted = 0;
        _spent = 0L;
    }

    // 这一帧的预算用完了：这块地先不画，下一轮再画
    private static void AfterCheck(TileZone __instance, ref bool __result)
    {
        if (!__result || !Active) return;
        NewFrame();
        if (_painted < MinZonesPerFrame) return;
        // 已经清空的地块(不在"已画"名单里)：当帧画上
        if (World.world?.zone_calculator == null || !World.world.zone_calculator._current_drawn_zones.Contains(__instance)) return;
        double spent = _spent / TicksPerMs;
        if (spent < BudgetMs || spent < VisibleBudgetMs && IsVisible(__instance) ||
            spent < UrgentBudgetMs && SmoothBorders.Awaiting(__instance)) return;
        __result = false;
        _deferredFrame = Time.frameCount;
        __instance.resetRenderHelpers();
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

    private static bool IsVisible(TileZone zone)
    {
        int frame = Time.frameCount;
        if (_visibleFrame != frame)
        {
            _visibleFrame = frame;
            Visible.Clear();
            List<TileZone> zones = World.world?.zone_camera?.getVisibleZones();
            if (zones != null)
                foreach (TileZone visible in zones)
                    if (visible != null) Visible.Add(visible.id);
        }
        return Visible.Contains(zone.id);
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
    // 整图重画(这里一次几百毫秒)白白卡一下。纯净地图下这种情况只把新旧悬停、选中对象的格子重新提亮或还原(约一毫秒)；
    // 切换图层选项、清色设置变了照原版重画
    private static NanoObject _lastCursor, _lastSelected;

    private static bool BeforeClearAll(ZoneCalculator __instance)
    {
        NanoObject cursor = __instance._cursor_nano_object, selected = __instance._selected_nano_object;
        NanoObject oldCursor = _lastCursor, oldSelected = _lastSelected;
        _lastCursor = cursor;
        _lastSelected = selected;
        if (!PureMapMode.Enabled) return true;
        int state = World.world?.getCachedMapMetaAsset()?.getZoneOptionState() ?? -1;
        if (state != -1 && state != __instance._last_zone_state ||
            __instance._last_should_be_clear_color != __instance._cached_should_be_clear_color) return true;
        if (!__instance._selection_changed_dirty) return false;
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

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
    private const double BudgetMs = 6d, VisibleBudgetMs = 14d;
    private const int MinZonesPerFrame = 6;
    private static readonly double TicksPerMs = Stopwatch.Frequency / 1000d;

    private static int _frame = -1, _painted, _visibleFrame = -1;
    private static long _spent, _started;
    private static readonly HashSet<int> Visible = new();

    private static bool Active => PureMapMode.Enabled || SmoothBorders.Enabled;

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
    }

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
        if (spent < BudgetMs || spent < VisibleBudgetMs && IsVisible(__instance)) return;
        __result = false;
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

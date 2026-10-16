using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;

namespace EmpireCraft.Scripts.GeneralSystems;

// 重大事件的统一记录：写入帝国史书，同时在事件日志里显示。
// (党禁、军阀时期、朝贡废除、革命滚雪球、WarBox 对接等以前各自写一份私有 Record)
public static class EventRecorder
{
    // empire 为空时只显示日志不入史书；logKingdom 为空时用帝国核心王国；actor 为空时记在元首名下
    public static void Record(Empire empire, string text, Actor actor = null, Kingdom logKingdom = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Kingdom kingdom = logKingdom ?? empire?.CoreKingdom;
        if (empire != null && !empire.isRekt())
            empire.RecordHistory(directContent: text, actorId: actor?.id ?? empire.Emperor?.id ?? -1L,
                kingdomId: kingdom?.id ?? -1L);
        if (kingdom != null) TranslateHelper.LogEventMessage(text, kingdom);
    }

    // 例行政事(选举、组阁、议程、党派分合)：照常写进史书；屏幕日志只给举足轻重的国家(城数不少于 8、
    // 且不少于最大国的四分之一，或玩家正在看的国家)，并全局限流(每 2 秒最多一条)。
    // 玩家反馈：进入现代后小国林立，每国都在选举，日志一直跳、卡爆
    public static void RecordRoutine(Empire empire, string text, Actor actor = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Kingdom kingdom = empire?.CoreKingdom;
        if (empire != null && !empire.isRekt())
            empire.RecordHistory(directContent: text, actorId: actor?.id ?? empire.Emperor?.id ?? -1L,
                kingdomId: kingdom?.id ?? -1L);
        if (kingdom != null && ShowRoutine(empire)) TranslateHelper.LogEventMessage(text, kingdom);
    }

    private const float RoutineInterval = 2f, LargestCacheSeconds = 10f;
    private const int NotableMinCities = 8;
    private static float _lastRoutine = -100f, _largestAt = -100f;
    private static int _largestCities;

    public static bool ShowRoutine(Empire empire)
    {
        if (empire?.data == null || empire.isRekt()) return false;
        bool watched = EmpireCraft.Scripts.GameLibrary.EmpireCraftMetaTypeLibrary.selected_empire == empire;
        if (!watched)
        {
            int cities = CityCount(empire);
            if (cities < NotableMinCities || cities * 4 < LargestCities()) return false;
        }
        float now = UnityEngine.Time.unscaledTime;
        if (now - _lastRoutine < RoutineInterval && now >= _lastRoutine) return false;
        _lastRoutine = now;
        return true;
    }

    private static int CityCount(Empire empire) =>
        empire.kingdoms_list?.Where(kingdom => kingdom?.cities != null && !kingdom.isRekt()).Sum(kingdom => kingdom.cities.Count) ?? 0;

    private static int LargestCities()
    {
        float now = UnityEngine.Time.unscaledTime;
        if (now - _largestAt < LargestCacheSeconds && now >= _largestAt) return _largestCities;
        _largestAt = now;
        _largestCities = ModClass.EMPIRE_MANAGER?.ToList()
            .Where(empire => empire?.data != null && !empire.isRekt() && !empire.IsArchived())
            .Select(CityCount).DefaultIfEmpty(0).Max() ?? 0;
        return _largestCities;
    }

    // 以某个政权的名义记录：它组建了政府(帝国层)就入该帝国史书，否则只显示日志
    public static void Record(Kingdom kingdom, string text, Actor actor = null) =>
        Record(kingdom != null && kingdom.IsEmpire() ? kingdom.GetEmpire() : null, text, actor ?? kingdom?.king, kingdom);
}

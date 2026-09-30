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

    // 以某个政权的名义记录：它组建了政府(帝国层)就入该帝国史书，否则只显示日志
    public static void Record(Kingdom kingdom, string text, Actor actor = null) =>
        Record(kingdom != null && kingdom.IsEmpire() ? kingdom.GetEmpire() : null, text, actor ?? kingdom?.king, kingdom);
}

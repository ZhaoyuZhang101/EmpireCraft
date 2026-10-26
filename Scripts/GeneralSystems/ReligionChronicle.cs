using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;

namespace EmpireCraft.Scripts.GeneralSystems;

// 宗教大事记(用户 2026-10-27 选"记"：宗教窗口里可翻)：创立、并入、君主皈依、迫害驱逐、分裂、圣战、加冕、教宗选立、
// 绝罚、教廷之囚与对立教宗都记进相关宗教自己的大事记；每教留最近 MaxEvents 条。
// 文字在记的时候拼好(各系统写世界日志、史书用的同一句)，读档后照原样显示。
public static class ReligionChronicle
{
    public const int MaxEvents = 100;
    public const int KindFounding = 0, KindPapacy = 1, KindHolyWar = 2, KindSchism = 3, KindConversion = 4;

    public static void Record(Religion religion, int kind, string text)
    {
        if (religion?.data == null || religion.isRekt() || string.IsNullOrWhiteSpace(text) || World.world == null) return;
        ReligionExtension.ReligionExtraData data = religion.GetOrCreate();
        data.Chronicle ??= new List<ReligionExtension.ReligionEventData>();
        data.Chronicle.Add(new ReligionExtension.ReligionEventData { at = World.world.getCurWorldTime(), kind = kind, text = text });
        if (data.Chronicle.Count > MaxEvents) data.Chronicle.RemoveRange(0, data.Chronicle.Count - MaxEvents);
    }

    // 同一件事记进两个宗教(分裂、改宗、并入)
    public static void Record(Religion first, Religion second, int kind, string text)
    {
        Record(first, kind, text);
        if (second != first) Record(second, kind, text);
    }

    // 分出来的教会：创教时(ReligionPatch)记下的"某某创立"用的是改名前的名字，改完名清掉，由分裂那条起头
    public static void Clear(Religion religion)
    {
        if (religion?.data != null) religion.GetOrCreate().Chronicle?.Clear();
    }

    public static List<ReligionExtension.ReligionEventData> Events(Religion religion) =>
        religion?.data == null ? new List<ReligionExtension.ReligionEventData>() : religion.GetOrCreate().Chronicle ?? new List<ReligionExtension.ReligionEventData>();
}

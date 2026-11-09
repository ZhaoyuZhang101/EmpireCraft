using System.Collections.Generic;
using System.Linq;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// [宗教诊断] 按改宗渠道计数(用户 2026-11-06 问答定的验收口径：古典时代全图基督要能看出是哪条渠道改过去的)。
// 各系统改宗时记一笔，宗教年度扫描最后(ReligiousTension.TryYearlyScan)写一行日志并清零；有无小人都记。
public static class ReligionDiagnostics
{
    public const string Ruler = "君主皈依(国)", AdoptSubjects = "入主从俗(国)", NativeRevert = "复归本土(国)", OtherRealm = "率国改奉(国)",
        Persecuted = "迫害改宗(城)", Expelled = "驱逐改宗(城)", HolyWar = "圣战改宗(城)", HolyWarSpared = "圣战未改(城)",
        Assimilated = "同化诉求(城)", AssimilationSpared = "同化未改(城)", Adopted = "背景改信(城)", Converted = "背景改宗(城)",
        Returned = "本土回流(城)", Manual = "逐城改宗(城)", Revelation = "启示立教", OldGods = "古神立教", Syncretism = "融合(条)";

    private static readonly Dictionary<string, int> Counts = new();

    public static void Reset() => Counts.Clear();

    public static void Add(string channel, int count = 1)
    {
        if (string.IsNullOrEmpty(channel) || count <= 0) return;
        Counts.TryGetValue(channel, out int current);
        Counts[channel] = current + count;
    }

    public static void Flush()
    {
        if (Counts.Count == 0) return;
        LogService.LogInfo("[EmpireCraft][宗教诊断] 本年改宗渠道：" +
                           string.Join("、", Counts.OrderByDescending(pair => pair.Value).Select(pair => $"{pair.Key} {pair.Value}")));
        Counts.Clear();
    }
}

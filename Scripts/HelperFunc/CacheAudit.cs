using System.Collections.Generic;

namespace EmpireCraft.Scripts.HelperFunc;

// 排查用(独立核心的 --verify-caches)：几处"一帧里问过就给现成的"缓存(制度特性、执政宗族、宗族索引、城内村镇表)
// 开着时每回给现成的之前都照老办法现算一遍来比，对不上的记数、头几回写日志。平时关着，一行判断的开销
public static class CacheAudit
{
    public static bool Enabled;
    public static long Checked, Mismatched;
    private static readonly Dictionary<string, int> ByLabel = new();

    // 老办法太慢的(把几千个宗族从头找一遍)隔几回核一回，不然开着核对世界走不动
    private static int _tick;

    public static bool Sampled(int every) => Enabled && ++_tick % every == 0;

    public static void Compare(string label, bool same)
    {
        Checked++;
        if (same) return;
        Mismatched++;
        lock (ByLabel)
        {
            ByLabel.TryGetValue(label, out int count);
            ByLabel[label] = count + 1;
            if (count < 5) NeoModLoader.services.LogService.LogWarning("[EmpireCraft][缓存核对] 对不上: " + label);
        }
    }

    public static string Report()
    {
        string detail = "";
        lock (ByLabel)
            foreach (KeyValuePair<string, int> pair in ByLabel) detail += " " + pair.Key + "×" + pair.Value;
        return "缓存核对 " + Checked + " 回，对不上 " + Mismatched + " 回" + detail;
    }
}

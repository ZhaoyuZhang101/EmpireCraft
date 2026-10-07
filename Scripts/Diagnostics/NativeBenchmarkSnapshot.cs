using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;

namespace EmpireCraft.Scripts.Diagnostics;

// 只读已经开启的原版计时器，使用毫秒，不依赖窗口的预算百分比和历史峰值显示模式。
public static class NativeBenchmarkSnapshot
{
    public static void Record()
    {
        if (!Bench.bench_enabled) return;
        PerformanceTraceFile.Record("[EmpireCraft][原版计时·补丁] u4_deadCheck 前缀=" + ActorPrefixOwners());
        foreach (string group in new[] { "game_total", "actors", "buildings", "quantum_sprites" })
        {
            var entries = new List<ToolBenchmarkData>(Bench.getGroup(group).dict_data.Values);
            entries.Sort((a, b) => b.latest_result.CompareTo(a.latest_result));
            var line = new StringBuilder("[EmpireCraft][原版计时·" + group + "] 最近一次/原版滚动均值 ms：");
            int shown = 0;
            foreach (ToolBenchmarkData entry in entries)
            {
                if (entry.latest_result <= 0d || shown++ >= 10) break;
                line.Append(entry.id).Append(' ')
                    .Append((entry.latest_result * 1000d).ToString("0.000", CultureInfo.InvariantCulture))
                    .Append('/').Append((entry.getAverage() * 1000d).ToString("0.000", CultureInfo.InvariantCulture))
                    .Append(" ms，计数 ").Append(entry.getAverageCount()).Append("；");
            }
            PerformanceTraceFile.Record(line.ToString());
        }
    }

    public static string ActorPrefixOwners()
    {
        var prefixes = Harmony.GetPatchInfo(AccessTools.Method(typeof(BatchActors), "u4_deadCheck"))?.Prefixes;
        if (prefixes == null || prefixes.Count == 0) return "原版，无前缀";
        var names = new List<string>();
        foreach (Patch patch in prefixes)
            names.Add(patch.PatchMethod.DeclaringType?.FullName ?? patch.owner);
        return string.Join(", ", names);
    }
}

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace EmpireCraft.Scripts.GeneralSystems;

// 诊断：无小人模式下是谁在往地图上生成实体(落成族人、补城主、征兵……)。
// 每次生成记下调用方(跳过人口系统与族谱系统内部)，随「实体构成」每 30 秒汇总一次
public static class EntitySpawnStats
{
    private static readonly Dictionary<string, int> Counts = new();
    private static int _total;

    public static void RecordCaller()
    {
        _total++;
        string caller = "?";
        try
        {
            var trace = new StackTrace(2, false);
            for (int i = 0; i < trace.FrameCount && i < 12; i++)
            {
                var method = trace.GetFrame(i)?.GetMethod();
                string type = method?.DeclaringType?.Name ?? "";
                if (type.Length == 0 || type.StartsWith("<") || type == nameof(CityPopulationSystem) ||
                    type == nameof(VirtualGenealogySystem) || type == "PersonalClanIdentity" ||
                    type == nameof(EntitySpawnStats)) continue;
                caller = $"{type}.{method.Name}";
                break;
            }
        }
        catch
        {
            // 取不到调用栈就记为未知
        }
        Counts.TryGetValue(caller, out int count);
        Counts[caller] = count + 1;
    }

    // 汇总并清零："共 N 个：A×n、B×m"
    public static string Flush()
    {
        if (_total == 0) return "0";
        string top = string.Join("、", Counts.OrderByDescending(pair => pair.Value).Take(5)
            .Select(pair => $"{pair.Key}×{pair.Value}"));
        string text = $"{_total}({top})";
        Counts.Clear();
        _total = 0;
        return text;
    }
}

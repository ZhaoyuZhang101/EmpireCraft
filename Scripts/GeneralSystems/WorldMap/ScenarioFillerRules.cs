using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Scripts.GeneralSystems.WorldMap;

// 补空小国怎么收拾(规则部分，不碰世界，见 WorldMapScenario.AbsorbEnclosedFillers、DissolveShatteredFillers)：
//   · 被包围：只挨着一家、那家又是剧本里的国，并进去；并完再看一遍，刚才还挨着两家的可能就只挨一家了；
//     只挨着别的补空小国、一家都不挨的(海岛)不动；
//   · 被切碎：无人区划完，所有地块连成的片都不超过 ShatteredZones 块，不立国。
internal static class ScenarioFillerRules
{
    public const int ShatteredZones = 3;

    // neighbours：补空小国 → 挨着的国家(可以是别的补空小国，也可以是剧本里的国)；返回 补空小国 → 并进哪一国
    public static Dictionary<T, T> Absorptions<T>(IReadOnlyDictionary<T, HashSet<T>> neighbours)
    {
        var result = new Dictionary<T, T>();
        var left = neighbours.ToDictionary(pair => pair.Key, pair => new HashSet<T>(pair.Value));
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (T filler in left.Keys.ToList())
            {
                HashSet<T> around = left[filler];
                around.Remove(filler);
                if (around.Count != 1) continue;
                T only = around.First();
                if (left.ContainsKey(only)) continue;
                result[filler] = only;
                left.Remove(filler);
                // 它的邻居从此挨的是并进去的那一国
                foreach (HashSet<T> other in left.Values)
                    if (other.Remove(filler)) other.Add(only);
                changed = true;
            }
        }
        return result;
    }

    // 地块连成的最大一片(只算上下左右相连)；zonesWide 是一行几块
    public static int LargestPiece(IEnumerable<int> zones, int zonesWide)
    {
        var left = new HashSet<int>(zones);
        int largest = 0;
        var stack = new Stack<int>();
        while (left.Count > 0)
        {
            int start = left.First();
            left.Remove(start);
            stack.Push(start);
            int size = 0;
            while (stack.Count > 0)
            {
                int zone = stack.Pop();
                size++;
                int column = zone % zonesWide;
                if (column > 0 && left.Remove(zone - 1)) stack.Push(zone - 1);
                if (column < zonesWide - 1 && left.Remove(zone + 1)) stack.Push(zone + 1);
                if (left.Remove(zone - zonesWide)) stack.Push(zone - zonesWide);
                if (left.Remove(zone + zonesWide)) stack.Push(zone + zonesWide);
            }
            if (size > largest) largest = size;
        }
        return largest;
    }

    public static bool Shattered(IEnumerable<int> zones, int zonesWide)
    {
        int largest = LargestPiece(zones, zonesWide);
        return largest > 0 && largest <= ShatteredZones;
    }
}

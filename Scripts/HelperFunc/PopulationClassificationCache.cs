using System;
using System.Collections.Generic;

namespace EmpireCraft.Scripts.HelperFunc;

// 每座城市独立轮转。即使前排名人每月都过期，后排居民也一定有机会被检查。
public sealed class PopulationClassificationCache
{
    private readonly Dictionary<long, (double at, bool notable)> Entries = new();
    private int _cursor;
    public bool TryGet(long id, double now, Func<double, int> monthsSince, out bool notable)
    {
        notable = true;
        if (!Entries.TryGetValue(id, out var entry) || now < entry.at || monthsSince(entry.at) >= 3) return false;
        notable = entry.notable;
        return true;
    }
    public void Set(long id, double now, bool notable) => Entries[id] = (now, notable);
    // evaluate 返回 true 才消耗昂贵判断额度；已缓存、死亡和受保护的单位只推进游标。
    public void Warm(int count, int budget, Func<int, bool> evaluate, Func<bool> hasTime)
    {
        if (count <= 0) { _cursor = 0; Entries.Clear(); return; }
        _cursor %= count;
        int used = 0;
        for (int visited = 0; visited < count && used < budget; visited++)
        {
            if (visited > 0 && !hasTime()) break;
            int index = _cursor;
            _cursor = (_cursor + 1) % count;
            if (evaluate(index)) used++;
        }
    }
    public void Prune(ICollection<long> liveIds)
    {
        if (Entries.Count <= Math.Max(64, liveIds.Count * 2)) return;
        var live = new HashSet<long>(liveIds);
        var removed = new List<long>();
        foreach (long id in Entries.Keys) if (!live.Contains(id)) removed.Add(id);
        foreach (long id in removed) Entries.Remove(id);
    }
}

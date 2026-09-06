using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Scripts.Layer
{
    public static class PeerageSuccessionRules
    {
        // Follow the elder branch first, including descendants of a deceased child.
        public static T FindDescendant<T>(T predecessor, Func<T, IEnumerable<T>> orderedChildren,
            Func<T, bool> belongsToLine, Func<T, bool> canInherit, Func<T, long> identityId) where T : class
        {
            if (predecessor == null) return null;
            var visited = new HashSet<long> { identityId(predecessor) };
            var pending = new Stack<T>(orderedChildren(predecessor).Reverse());
            while (pending.Count > 0)
            {
                T child = pending.Pop();
                if (child == null || !visited.Add(identityId(child)) || !belongsToLine(child)) continue;
                if (canInherit(child)) return child;
                foreach (T descendant in orderedChildren(child).Reverse()) pending.Push(descendant);
            }
            return null;
        }

        // A vacant hereditary title remains reserved for its recorded lineage.
        public static bool IsReserved<TKey>(IDictionary<TKey, long> holders, TKey title)
        {
            return holders != null && holders.ContainsKey(title);
        }
    }
}

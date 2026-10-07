using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GeneralSystems;
using UnityEngine;

namespace EmpireCraft.Scripts.HelperFunc;

// 同帧的多个职位共享有序名单；资格、官职、随机评分仍在每次选择时重新检查。
internal static class OfficeCandidateRoster
{
    private sealed class Entry
    {
        internal object World, Source;
        internal int Frame = -1, Count;
        internal long Revision;
        internal Actor[] Actors;
    }
    private static readonly ConditionalWeakTable<Kingdom, Entry> Entries = new();
    private static long _revision;
    public static long Builds { get; private set; }
    public static long Hits { get; private set; }
    public static void Invalidate() { if (CityPopulationSystem.AbstractPopulationEnabled) _revision++; }

    public static IEnumerable<Actor> Empire(IEnumerable<Kingdom> kingdoms)
    {
        if (kingdoms == null) yield break;
        var seen = new HashSet<Kingdom>();
        foreach (Kingdom kingdom in kingdoms.ToArray())
        {
            if (kingdom?.data == null || kingdom.isRekt() || !seen.Add(kingdom) || AncientWarfareCompatibility.Owns(kingdom)) continue;
            var units = kingdom.units;
            if (units == null) continue;
            Entry entry = Entries.GetOrCreateValue(kingdom);
            if (entry.Actors == null || entry.Frame != Time.frameCount || entry.Revision != _revision ||
                !ReferenceEquals(entry.World, World.world) || !ReferenceEquals(entry.Source, units) || entry.Count != units.Count)
            {
                entry.Actors = units.ToArray(); entry.Frame = Time.frameCount; entry.Revision = _revision;
                entry.World = World.world; entry.Source = units; entry.Count = units.Count; Builds++;
            }
            else Hits++;
            foreach (Actor actor in entry.Actors)
            {
                if (kingdom.data == null || kingdom.isRekt()) break;
                if (actor?.data == null || actor.asset == null || !actor.isAlive() || actor.asset.is_boat || actor.kingdom != kingdom ||
                    AncientWarfareCompatibility.Owns(actor)) continue;
                yield return actor;
            }
        }
    }
}

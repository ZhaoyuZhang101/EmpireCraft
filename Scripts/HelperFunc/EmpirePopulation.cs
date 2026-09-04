using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Compatibility;

namespace EmpireCraft.Scripts.HelperFunc
{
    internal static class EmpirePopulation
    {
        public static IEnumerable<Actor> Enumerate(IEnumerable<Kingdom> kingdoms)
        {
            if (kingdoms == null) yield break;
            var visited = new HashSet<Kingdom>();
            foreach (Kingdom kingdom in kingdoms.ToArray())
            {
                if (kingdom?.data == null || kingdom.isRekt() || !visited.Add(kingdom) ||
                    AncientWarfareCompatibility.Owns(kingdom)) continue;
                List<Actor> units = kingdom.units;
                if (units == null) continue;
                // Vanilla getUnits dereferences every slot before yielding. Sparse/stale
                // membership lists must be checked here, without mutating another mod's index.
                // A private snapshot also tolerates relocation/removal by the iterator's caller.
                foreach (Actor actor in units.ToArray())
                {
                    if (kingdom.data == null || kingdom.isRekt()) break;
                    if (actor?.data == null || actor.asset == null || !actor.isAlive() ||
                        actor.asset.is_boat || actor.kingdom != kingdom ||
                        AncientWarfareCompatibility.Owns(actor)) continue;
                    yield return actor;
                }
            }
        }
    }
}

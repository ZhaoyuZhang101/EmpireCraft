using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Scripts.Layer
{
    // Persist the wartime identity before conquest can remove the empire or its ruler.
    public class DefeatedEmpireHouse
    {
        public long empire_id { get; set; }
        public long emperor_id { get; set; }
        public long royal_clan_id { get; set; }

        public DefeatedEmpireHouse()
        {
            empire_id = emperor_id = royal_clan_id = -1L;
        }
    }

    public static class AnlePeerageRules
    {
        public static int CandidatePriority(IEnumerable<DefeatedEmpireHouse> defeatedHouses,
            long grantingEmpireId, long actorId, long clanId, bool isResident, bool isRuler)
        {
            if (actorId <= 0 || !isResident || isRuler || defeatedHouses == null) return 0;
            int priority = 0;
            foreach (var house in defeatedHouses.Where(h => h != null && h.empire_id > 0 && h.empire_id != grantingEmpireId))
            {
                if (house.emperor_id == actorId) return 2;
                if (clanId > 0 && house.royal_clan_id == clanId) priority = 1;
            }
            return priority;
        }
    }
}

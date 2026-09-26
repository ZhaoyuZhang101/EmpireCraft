using System;
using EmpireCraft.Scripts.GameClassExtensions;

namespace EmpireCraft.Scripts.GeneralSystems;

public static class RebellionStartupService
{
    public static void RollbackCitySplit(City seat, Kingdom origin, Kingdom rebel)
    {
        if (seat == null || origin == null || rebel == null || origin.isRekt() || seat.kingdom != rebel) return;
        rebel.EndLocalRebelling();
        seat.joinAnotherKingdom(origin);
        if (seat.kingdom != origin) return;
        World.world.game_stats.data.citiesRebelled = Math.Max(0, World.world.game_stats.data.citiesRebelled - 1);
        World.world.map_stats.citiesRebelled = Math.Max(0, World.world.map_stats.citiesRebelled - 1);
    }
}

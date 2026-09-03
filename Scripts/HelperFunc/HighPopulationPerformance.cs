using UnityEngine;

namespace EmpireCraft.Scripts.HelperFunc;

public static class HighPopulationPerformance
{
    private const int CivilianAiThreshold = 5000;
    private const int ExtremePopulationThreshold = 20000;

    public static int GetFrontLineCacheKey()
    {
        int interval = 1;
        if (ModClass.PERFORMANCE_HIGH_POPULATION_MODE && World.world?.units != null)
        {
            int unitCount = World.world.units.Count;
            interval = unitCount >= ExtremePopulationThreshold ? 4 :
                unitCount >= CivilianAiThreshold ? 2 : 1;
        }

        return Time.frameCount / interval;
    }
}

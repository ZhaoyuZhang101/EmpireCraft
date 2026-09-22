using UnityEngine;

namespace EmpireCraft.Scripts.HelperFunc;

public static class HighPopulationPerformance
{
    private const int CivilianAiThreshold = 5000;
    private const int ExtremePopulationThreshold = 20000;
    private const float HighPopulationFrontLineCacheSeconds = 0.25f;
    private const float ExtremePopulationFrontLineCacheSeconds = 0.5f;

    public static int GetFrontLineCacheKey()
    {
        if (!ModClass.PERFORMANCE_HIGH_POPULATION_MODE || World.world?.units == null)
            return Time.frameCount;

        int unitCount = World.world.units.Count;
        if (unitCount < CivilianAiThreshold) return Time.frameCount;

        float cacheSeconds = unitCount >= ExtremePopulationThreshold
            ? ExtremePopulationFrontLineCacheSeconds
            : HighPopulationFrontLineCacheSeconds;
        return Mathf.FloorToInt(Time.realtimeSinceStartup / cacheSeconds);
    }
}

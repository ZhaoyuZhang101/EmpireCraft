using System;
using System.Linq;
using EmpireCraft.Scripts.AI.CityAI;
using EmpireCraft.Scripts.GameClassExtensions;

namespace EmpireCraft.Scripts.GeneralSystems;

public static class RebellionStartupService
{
    private const float MinMilitiaShare = 0.12f;
    private const float MaxMilitiaShare = 0.35f;
    private const int MinMilitia = 3;

    // 起事城市原先多是郡县，政体不许养兵、兵早被裁撤，起义时城里一个兵都没有；
    // 原版规则下没有守军的城市，任何一个敌兵进城十来秒就被收复，起义等于一冒头就被扑灭。
    // 起事时按民怨烈度(0~1)就地武装一部分成年居民为义军，并立刻组建军队。
    // 规模是守住本城、跟小股官军周旋的量级，真正的正规军压境仍然打不过。
    public static int RaiseUprisingMilitia(Kingdom rebel, float fervor)
    {
        if (rebel == null || rebel.isRekt() || rebel.cities == null) return 0;
        float share = MinMilitiaShare + (MaxMilitiaShare - MinMilitiaShare) * Math.Max(0f, Math.Min(1f, fervor));
        int raised = 0;
        foreach (City city in rebel.cities.ToList())
        {
            if (city == null || city.isRekt() || city.units == null) continue;
            var candidates = city.units.Where(actor => actor != null && actor.isAlive() && actor.isAdult() &&
                                                       actor.kingdom == rebel && !actor.isWarrior() &&
                                                       !actor.isKing() && !actor.isCityLeader() &&
                                                       actor.equipment != null && actor.CanFoundCivKingdom())
                .OrderByDescending(actor => actor.stats["health"]).ToList();
            int adults = city.units.Count(actor => actor != null && actor.isAlive() && actor.isAdult());
            // 只补足到目标人数：已有守军的封国起兵时不会再额外叠加
            int garrison = city.units.Count(actor => actor != null && actor.isAlive() && actor.isWarrior());
            int target = Math.Min(candidates.Count,
                Math.Max(0, Math.Max(MinMilitia, (int)Math.Ceiling(adults * share)) - garrison));
            for (int i = 0; i < target; i++)
            {
                city.makeWarrior(candidates[i]);
                raised++;
            }
            city.checkArmyExistence();
            if (!city.hasArmy()) EmpireCraftCityBehCheckArmy.CreateNewArmy(city);
            // 立刻重新评估军队(不等下一个月)，并清掉起事前官军留下的占领进度
            CityExtension.CityExtraData cityData = city.GetOrCreate();
            if (cityData != null) cityData.last_army_check_ts = -1d;
            city.clearCapture();
        }
        return raised;
    }

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

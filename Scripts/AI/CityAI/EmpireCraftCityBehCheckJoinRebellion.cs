using System;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;

namespace EmpireCraft.Scripts.AI.CityAI;

public class EmpireCraftCityBehCheckJoinRebellion: GameAICityBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(City pCity)
    {
        if (pCity.isRekt()) return BehResult.Stop;
        if (pCity.isNeutral()) return BehResult.Stop;
        if (pCity.isCapitalCity()&&pCity.kingdom.IsEmpire()) return BehResult.Stop;
        if (pCity.getLoyalty()>0) return BehResult.Continue;
        var initKingdom = pCity.kingdom;
        var target = pCity.neighbours_cities.ToList().Find(c =>
        {
            if (c.isRekt()) return false;
            if (c.isNeutral()) return false;
            if (!c.hasKingdom()) return false;
            var targetKingdom = c.kingdom;
            if (initKingdom == targetKingdom) return false;
            var targetWar = targetKingdom.getWars().ToList().Find(w =>
                (w.GetEmpireWarType() == EmpireWarType.地方叛乱 || w.GetEmpireWarType() == EmpireWarType.地方独立)&&w.main_attacker==initKingdom&&w.main_defender==targetKingdom);
            if (targetWar == null) return false;
            return true;
        });
        if (target == null) return BehResult.Continue;
        var targetKingdom = target.kingdom;
        if (targetKingdom.countCities()>=targetKingdom.getMaxCities()*2) return BehResult.Continue;
        pCity.joinAnotherKingdom(target.kingdom);
        return  BehResult.Continue;
    }
}
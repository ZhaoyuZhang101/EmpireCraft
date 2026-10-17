using System;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.GeneralSystems;

namespace EmpireCraft.Scripts.AI.CityAI;

public class EmpireCraftCityBehCheckJoinRebellion: GameAICityBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(City pCity)
    {
        if (pCity.isRekt()) return BehResult.Stop;
        if (pCity.isNeutral()) return BehResult.Stop;
        if (pCity.isCapitalCity()&&pCity.kingdom.IsEmpire()) return BehResult.Stop;
        if (!RebellionSystem.CanAttempt(pCity.kingdom) || !CityStabilitySystem.CanRise(pCity)) return BehResult.Continue;
        int loyalty = pCity.getLoyalty();
        if (loyalty > 38) return BehResult.Continue;
        CityValueSnapshot cityValue = pCity.GetCityStrategicValue();
        if (loyalty > CityValueRules.GetIsolationRebellionLoyaltyThreshold(cityValue)) return BehResult.Continue;
        var initKingdom = pCity.kingdom;
        var initCore = initKingdom.GetEmpire()?.CoreKingdom;
        var target = pCity.neighbours_cities.ToList().Find(c =>
        {
            if (c.isRekt()) return false;
            if (c.isNeutral()) return false;
            if (!c.hasKingdom()) return false;
            var targetKingdom = c.kingdom;
            if (initKingdom == targetKingdom) return false;
            // 宗教叛乱只有同信仰的城加入，民族叛乱只有同文化的城加入
            var targetWar = targetKingdom.getWars().ToList().Find(w =>
                (w.GetEmpireWarType() == EmpireWarType.地方叛乱 || w.GetEmpireWarType() == EmpireWarType.地方独立 ||
                 w.GetEmpireWarType() == EmpireWarType.宗教叛乱 && pCity.religion != null && pCity.religion == c.religion ||
                 w.GetEmpireWarType() == EmpireWarType.民族叛乱 &&
                 CultureService.GetMainCulture(pCity) == CultureService.GetMainCulture(c)) &&
                w.main_attacker == targetKingdom && (w.main_defender == initKingdom || initCore != null && w.main_defender == initCore));
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

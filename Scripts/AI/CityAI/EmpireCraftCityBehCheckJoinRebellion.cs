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

    // 这一步排在模组城市大任务"do_mod_city_beh"里(见 GameAIMain.CityAIs)，返回 Stop 会结束整个任务、后面各步都不跑：
    // 只有已毁、无主的城整座不跑模组城市 AI 才用 Stop。帝国都城只是不投叛军，后面的律令、城主、官职、收税、法理首都、
    // 合并民居照常(以前这里也返回 Stop，帝国都城这些步一直轮不到)
    public override BehResult execute(City pCity)
    {
        if (pCity.isRekt()) return BehResult.Stop;
        if (pCity.isNeutral()) return BehResult.Stop;
        if (pCity.isCapitalCity()&&pCity.kingdom.IsEmpire()) return BehResult.Continue;
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
            var targetWar = targetKingdom.getWars().ToList().Find(w =>
                (w.GetEmpireWarType() == EmpireWarType.地方叛乱 || w.GetEmpireWarType() == EmpireWarType.地方独立) &&
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

using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions;

public class TempFac_转天朝制度 : TemporaryFaction
{

    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        Empire empire = GetEmpire();
        foreach (Kingdom kingdom in empire.kingdoms_list)
        {
            kingdom.SetRegimeType(RegimeType.LvLing);
            kingdom.LoadRegime();
            Regime regime = kingdom.GetRegime();
            if (!kingdom.IsEmpire())
            {
                regime.SetAllowDiplomacy(false);
            }
            regime.SetLeaderSelectMethod(LeaderSelectMethod.Exam);
        }
        empire.data.centerOffice.Init(empire.CoreKingdom);
        empire.AutoEnfeoff();
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire.kingdoms_list.FindAll(k => !k.IsEmpire()).Sum(k => k.countTotalWarriors()) <
            empire.CoreKingdom.countTotalWarriors())
        {
            return true;
        }
        foreach (var k in empire.kingdoms_list)
        {
            if (k.IsEmpire()) continue;
            Regime regime = k.GetRegime();
            if (regime.IsAllowDiplomacy())
            {
                return false;
            }
        }
        return true;
    }
}

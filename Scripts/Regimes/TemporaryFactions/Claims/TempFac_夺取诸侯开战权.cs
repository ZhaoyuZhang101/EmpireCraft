using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_夺取诸侯开战权 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_夺取诸侯开战权();
        res.Init(faction);
        return res;
    }
    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        Kingdom kingdom = GetKingdomTarget();
        if (kingdom != null)
        {
            if (!CheckRebelling(kingdom))
            {
                kingdom.GetRegime().SetAllowDiplomacy(false);
            }
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire == null) return false;
        foreach (Kingdom kingdom in empire.kingdoms_list)
        {
            if (kingdom.IsEmpire()) continue;
            Regime regime = kingdom.GetRegime();
            if (regime.IsAllowDiplomacy())
            {
                if (kingdom.countTotalWarriors() * 3 > empire.countWarriors())
                {
                    SetKingdomTarget(kingdom);
                    return true;
                }
            }
        }
        return false;
    }
}

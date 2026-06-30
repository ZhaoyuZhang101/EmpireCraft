using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_夺取诸侯开战权 : TemporaryFaction
{
    public override bool RequireCrimeTarget => true;

    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_夺取诸侯开战权();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        return res;
    }

    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        Kingdom kingdom = GetKingdomTarget();
        if (kingdom != null && !CheckRebelling(kingdom))
        {
            if (!TryEnforceCrimeForCurrentTarget())
            {
                End();
                return;
            }
            kingdom.GetRegime().SetAllowDiplomacy(false);
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
            if (regime.IsAllowDiplomacy() && kingdom.countTotalWarriors() * 3 > empire.countWarriors())
            {
                return TrySetTarget(kingdom);
            }
        }

        return false;
    }
}

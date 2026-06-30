using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_提高赋税 : TemporaryFaction
{
    public override bool RequireCrimeTarget => true;

    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_提高赋税();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        return res;
    }

    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        Empire empire = GetEmpire();
        Kingdom kingdom = GetKingdomTarget();
        if (empire != null && kingdom != null && !CheckRebelling(kingdom))
        {
            if (!TryEnforceCrimeForCurrentTarget())
            {
                End();
                return;
            }
            empire.AddTaxRate();
        }

        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire == null) return false;
        if (empire.data == null || empire.data.TaxRate >= 1f) return false;

        foreach (Kingdom kingdom in empire.kingdoms_list)
        {
            if (kingdom == null || kingdom.isRekt() || kingdom.IsEmpire()) continue;
            if (kingdom.IsFactionRebelling()) continue;
            if (kingdom.getWars().Any()) continue;

            return TrySetTarget(kingdom);
        }

        return false;
    }
}

using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.GeneralSystems;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_降低赋税 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_降低赋税();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        Empire empire = GetEmpire();
        if (empire != null && ConstitutionalEconomySystem.CanChangeTax(empire))
        {
            empire.SubTaxRate();
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire == null) return false;
        return empire.data != null && empire.data.TaxRate > 0f &&
               ConstitutionalEconomySystem.CanChangeTax(empire);
    }
}

using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_划地给教廷 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_划地给教廷();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        return res;
    }
    public override void Init(FixedFaction faction)
    {
        base.Init(faction);
        base.Hide = true;
    }

    public override void Execute()
    {
        End();
    }
    
    public override bool CheckCondition()
    {
        return false;
    }
}

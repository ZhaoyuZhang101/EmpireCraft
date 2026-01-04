using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;
using EmpireCraft.Scripts.Layer;
namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_游牧化 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_游牧化();
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
        var target = GetKingdomTarget();
        if (target != null)
        {
            target.SetRegimeType(RegimeType.YouMu);
            target.LoadRegime();
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        foreach (var kingdom in empire.kingdoms_list)
        {
            if (kingdom.GetRegime().type != RegimeType.YouMu)
            {
                SetKingdomTarget(kingdom);
                return true;
            }
        }
        return false;
    }
}

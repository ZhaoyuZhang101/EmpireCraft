using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_允许诸侯自由开战 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_允许诸侯自由开战();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override void Execute()
    {
        Kingdom kingdom = GetKingdomTarget();
        if (kingdom != null)
        {
            Regime regime = kingdom.GetRegime();
            regime.SetAllowDiplomacy(true);
            regime.SetAllowSupportCenterArmy(false);
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire.HasEmperor())
        {
            if (!empire.Emperor.hasTrait("ambitious")) return false;
        }
        foreach (Kingdom kingdom in empire.kingdoms_list)
        {
            Regime regime = kingdom.GetRegime();
            if (!regime.IsAllowDiplomacy())
            {
                SetKingdomTarget(kingdom);
                return true;
            }
        }
        return false;
    }
}

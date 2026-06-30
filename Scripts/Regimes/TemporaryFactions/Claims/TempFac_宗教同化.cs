using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_宗教同化 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_宗教同化();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        return res;
    }

    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        var kingdom = GetKingdomTarget();
        if (!CheckRebelling(kingdom))
        {
            kingdom.setReligion(GetEmpire().Religion);
            kingdom.units.ForEach(u=>u.setReligion(GetEmpire().Religion));
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire.Religion.isRekt()) return false;
        foreach (var kingdom in empire.kingdoms_list)
        {
            if (kingdom.religion != empire.Religion)
            {
                SetKingdomTarget(kingdom);
                return true;
            }
        }
        return false;
    }
}

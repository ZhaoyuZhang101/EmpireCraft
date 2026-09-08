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
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        var kingdom = GetKingdomTarget();
        Empire empire = GetEmpire();
        if (kingdom?.data != null && !kingdom.isRekt() && empire?.Religion != null &&
            !empire.Religion.isRekt() && !CheckRebelling(kingdom))
        {
            kingdom.setReligion(empire.Religion);
            kingdom.units?.ForEach(u =>
            {
                if (u?.data != null && !u.isRekt()) u.setReligion(empire.Religion);
            });
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire == null || empire.IsArchived() || empire.isRekt() ||
            empire.Religion == null || empire.Religion.isRekt() || empire.kingdoms_list == null) return false;
        foreach (var kingdom in empire.kingdoms_list)
        {
            if (kingdom?.data == null || kingdom.isRekt()) continue;
            if (kingdom.religion != empire.Religion)
            {
                SetKingdomTarget(kingdom);
                return true;
            }
        }
        return false;
    }
}

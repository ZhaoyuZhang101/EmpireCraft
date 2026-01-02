using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_加强神权 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_加强神权();
        res.Init(faction);
        return res;
    }

    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        Empire empire = GetEmpire();
        if (empire != null)
        {
            if (empire.CoreKingdom.GetRegime().GetReligionLevel() < (ReligionLevel)2)
            {
                var level = (int) empire.CoreKingdom.GetRegime().GetReligionLevel();
                empire.CoreKingdom.GetRegime().SetReligionLevel((ReligionLevel)(level + 1));
            }
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire == null) return false;
        if (empire.CoreKingdom.GetRegime().GetReligionLevel() < (ReligionLevel)2)
        {
            Acc = 30;
            return true;
        }
    return false;
}
}

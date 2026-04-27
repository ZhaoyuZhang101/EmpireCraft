using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_削藩 : TemporaryFaction
{
    public override bool RequireCrimeTarget => true;
    public override bool RequireRenown => true;

    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_削藩();
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
        if (kingdom != null && !CheckRebelling(kingdom))
        {
            if (!TryEnforceCrimeForCurrentTarget())
            {
                End();
                return;
            }

            foreach (var city in kingdom.cities)
            {
                city.joinAnotherKingdom(GetEmpire().CoreKingdom);
            }
            LogService.LogInfo("执行成功");
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
            if (kingdom.IsFactionRebelling()) continue;
            if (kingdom.getWars().Any()) continue;
            if ((empire.Emperor?.renown??0)==0) continue;
            if ((kingdom.king?.renown??0)>(empire.Emperor?.renown??0)) continue;
            Regime regime = kingdom.GetRegime();
            if (regime.GetReligionLevel() == ReligionLevel.High) continue;
            if (regime.GetLeaderSelectMethod() == LeaderSelectMethod.Succession)
            {
                if (kingdom.countTotalWarriors() * 5 <= empire.countWarriors() - kingdom.countTotalWarriors())
                {
                    return TrySetTarget(kingdom);
                }
            }
        }

        return false;
    }
}

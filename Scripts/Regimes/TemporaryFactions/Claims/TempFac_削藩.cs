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
            GetEmpire().AddMandate(20);
            LogService.LogInfo("执行成功");
        }
        End();
    }

    public override bool CheckLocalCondition(Kingdom actor)
    {
        if (!base.CheckLocalCondition(actor)) return false;
        Empire empire = GetEmpire();
        var target = empire.kingdoms_list.ToList().Find(k =>
        {
            if (!k.hasKing()) return false;
            if (k.king.GetFaction()==GetFaction()) return false;
            if (k.king.renown>actor.king.renown) return false;
            if (k.GetRegime().GetLeaderSelectMethod() != LeaderSelectMethod.Succession) return false;
            return true;
        });
        SetKingdomTarget(target);
        return true;
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
            if (!OwnEnoughRenown(kingdom)) continue;
            if ((kingdom?.king?.GetViolateValue()??0)<90) continue;
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

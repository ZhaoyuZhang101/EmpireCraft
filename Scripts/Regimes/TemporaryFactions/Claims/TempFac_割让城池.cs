using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_割让城池 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_割让城池();
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
        if (kingdom != null)
        {
            City city = kingdom.cities.ToList().Select(c => c.neighbours_cities.ToList().Find(nc =>
                nc.kingdom.IsInSameEmpire(GetEmpire().CoreKingdom) && (!c.kingdom.IsEmpire() || !c.isCapitalCity()))).First();
            city?.joinAnotherKingdom(kingdom);
            if (kingdom.isInWarWith(GetEmpire().CoreKingdom))
            {
                kingdom.EndWarWith(GetEmpire().CoreKingdom);
            }
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        var coreKingdom = empire.CoreKingdom;
        if (coreKingdom.hasEnemies())
        {
            if (empire.countCities() > 1)
            {
                foreach (var enemy in coreKingdom.getEnemiesKingdoms())
                {
                    if (!empire.IsNeighbourWith(enemy)) continue;
                    City city = enemy.cities.ToList().Select(c => c.neighbours_cities.ToList().Find(nc =>
                        nc.kingdom.IsInSameEmpire(GetEmpire().CoreKingdom) && (!c.kingdom.IsEmpire() || !c.isCapitalCity()))).First();
                    if (city == null) continue;
                    if (!(enemy.countTotalWarriors() * 1.5 >= empire.countWarriors())) continue;
                    SetKingdomTarget(enemy);
                    return true;
                }
            }
        }
        return false;
    }
}

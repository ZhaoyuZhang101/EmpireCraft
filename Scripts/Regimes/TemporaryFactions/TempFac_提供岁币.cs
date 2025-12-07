using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions;

public class TempFac_提供岁币 : TemporaryFaction
{

    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        Kingdom kingdom = GetKingdomTarget();
        if (kingdom != null)
        {
            Empire empire = GetEmpire();
            kingdom.JoinGivenAlliance(empire);
            kingdom.EndWarWith(empire.CoreKingdom);
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire.CoreKingdom.HasGivenAlliance()) return false;
        var enemies = GetEmpire().CoreKingdom.getEnemiesKingdoms();
        if (enemies.Any())
        {
            foreach (var w in enemies)
            {
                if (empire.given_Kingdoms.Contains(w)) continue;
                if (w.IsInSameEmpire(empire.CoreKingdom)) continue;
                if (w.HasGivenAlliance()) continue;
                if (w.IsInEmpire())
                {
                    Empire enemyEmpire = w.GetEmpire();
                    if (enemyEmpire.countWarriors() >= empire.countWarriors())
                    {
                        SetKingdomTarget(enemyEmpire.CoreKingdom);
                        return true;
                    }
                }
                else
                {
                    if (w.countTotalWarriors() > empire.countWarriors())
                    {
                        SetKingdomTarget(w);
                        return true;
                    }
                }
            }
        }
        return false;
    }
}

using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;
using EmpireCraft.Scripts.Layer;
namespace EmpireCraft.Scripts.Regimes.TemporaryFactions;

public class TempFac_游牧扩张 : TemporaryFaction
{
    
    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        Empire empire = GetEmpire();
        var target = GetKingdomTarget();
        if (target != null)
        {
            var war = DiplomacyHelpers.wars.newWar(empire.CoreKingdom, target, WarTypeLibrary.normal);
            war.SetEmpireWarType(EmpireWarType.游牧扩张);
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire.CoreKingdom.hasEnemies()) return false;
        var neighbours = empire.GetKingdomNeighbours();
        if (neighbours.Any())
        {
            var target = neighbours.Find(k=>!empire.CoreKingdom.isOpinionTowardsKingdomGood(k));
            if (target != null)
            {
                if (target.countTotalWarriors() < empire.countWarriors())
                {
                    SetKingdomTarget(target);
                    return true;
                }
            }
        }
        return false;
    }
}

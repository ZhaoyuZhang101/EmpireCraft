using EmpireCraft.Scripts.AI;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;
using EmpireCraft.Scripts.Layer;
namespace EmpireCraft.Scripts.Regimes.TemporaryFactions;

public class TempFac_劫掠 : TemporaryFaction
{
    
    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        Empire empire = GetEmpire();
        Kingdom target = GetKingdomTarget();
        if (target != null)
        {
            var war = DiplomacyHelpers.wars.newWar(empire.CoreKingdom, target, WarTypeLibrary.clash);
            war.SetEmpireWarType(EmpireWarType.劫掠);
            var op = World.world.diplomacy.getOpinion(target, empire.CoreKingdom);
            if (!op.results.ContainsKey(EmpireCraftOpinionAddition.OpinionKingdomBeenPlunder))
            {
                op.results.Add(EmpireCraftOpinionAddition.OpinionKingdomBeenPlunder, -20);
            }
            else
            {
                op.results[EmpireCraftOpinionAddition.OpinionKingdomBeenPlunder] -= 20;
            }
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        var neighbours = empire.GetKingdomNeighbours();
        if (!neighbours.Any()) return false;
        var target = neighbours.Find(k => k.GetRegime().type != empire.CoreKingdom.GetRegime().type);
        if (target!= null)
        {
            SetKingdomTarget(target);
            return true;
        }
        return false;
    }
}

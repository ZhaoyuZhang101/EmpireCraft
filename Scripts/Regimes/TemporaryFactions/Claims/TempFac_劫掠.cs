using EmpireCraft.Scripts.AI;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;
using EmpireCraft.Scripts.Layer;
namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_劫掠 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_劫掠();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        return res;
    }
    
    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        Empire empire = GetEmpire();
        Kingdom target = GetKingdomTarget();
        if (target != null)
        {
            var war = DiplomacyHelpers.wars.newWar(empire.CoreKingdom, target, WarTypeLibrary.normal);
            war.SetEmpireWarType(EmpireWarType.劫掠);
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire == null) return false;
        if (empire.CoreKingdom.hasEnemies()) return false;
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

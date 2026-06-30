using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;
using EmpireCraft.Scripts.Layer;
namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_迫使朝贡 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_迫使朝贡();
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
        Kingdom kingdom = GetKingdomTarget();
        if (kingdom != null)
        {
            var war = DiplomacyHelpers.wars.newWar(empire.CoreKingdom, kingdom, WarTypeLibrary.normal);
            war.SetEmpireWarType(EmpireWarType.迫使朝贡);
        }
        CountDown = 5;
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire?.CoreKingdom?.hasEnemies()??true) return false;
        foreach (var kingdom in World.world.kingdoms)
        {
            if (kingdom.IsInEmpire()) continue;
            if (empire.taken_Kingdoms.Contains(kingdom)) continue;
            if (kingdom.countTotalWarriors()>=empire.countWarriors()) continue;
            if (!empire.IsNeighbourWith(kingdom)) continue;
            if (kingdom.cities.Count<=1) continue;
            SetKingdomTarget(kingdom);
            return true;
        }
        return false;
    }
}

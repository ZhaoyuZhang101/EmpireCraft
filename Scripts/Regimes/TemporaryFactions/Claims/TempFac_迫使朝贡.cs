using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.GeneralSystems;
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
        res.canBePushByLocal = canBePushByLocal;
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
        if (!FeudalConquestService.HasTributeInstitution(empire)) return false;
        foreach (var kingdom in World.world.kingdoms)
        {
            if (kingdom.IsInEmpire()) continue;
            // 战国的周诸侯仍奉王室，不能迫其朝贡
            if (ZhouEraSystem.IsDetachedVassal(kingdom)) continue;
            if (empire.taken_Kingdoms.Contains(kingdom)) continue;
            // 比战力(军团、无小人模式下原版小人数是 0，以前比小人数永远不开战)
            if (WarSituation.Power(kingdom) >= WarSituation.Power(empire) || !WarGate.CanDeclare(empire.CoreKingdom, kingdom)) continue;
            if (!empire.IsNeighbourWith(kingdom) && !StraitReach.Across(empire.CoreKingdom, kingdom)) continue;
            if (kingdom.cities.Count<=1) continue;
            SetKingdomTarget(kingdom);
            return true;
        }
        return false;
    }
}

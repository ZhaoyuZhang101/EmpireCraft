using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions;

public class TempFac_扩张地盘 : TemporaryFaction
{
    public override bool Hide => true;
    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        var kingdom = GetKingdomTarget();
        if (kingdom != null)
        {
            War war = DiplomacyHelpers.wars.newWar(GetEmpire().CoreKingdom, kingdom, WarTypeLibrary.normal);
            war.SetEmpireWarType(EmpireWarType.帝国扩张);
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire == null) return false;
        foreach (var kingdom in World.world.kingdoms)
        {
            if (kingdom.IsInSameEmpire(empire.CoreKingdom)) continue;
            if (empire.IsNeighbourWith(kingdom))
            {
                if (empire.given_Kingdoms.Contains(kingdom)) continue;
                if (empire.taken_Kingdoms.Contains(kingdom)) continue;
                if (kingdom.isInWarWith(empire.CoreKingdom)) continue;
                if (empire.CoreKingdom.isOpinionTowardsKingdomGood(kingdom)) continue;
                SetKingdomTarget(kingdom);
                Acc = 30;
                return true;
            }
        }
        return false;
    }
}

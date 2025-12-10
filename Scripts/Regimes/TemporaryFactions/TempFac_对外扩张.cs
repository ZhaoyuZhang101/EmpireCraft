using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions;

public class TempFac_对外扩张 : TemporaryFaction
{

    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        Kingdom kingdom = GetKingdomTarget();
        if (kingdom != null)
        {
            var war = World.world.diplomacy.startWar(GetEmpire().CoreKingdom, kingdom, WarTypeLibrary.normal);
            war?.SetEmpireWarType(EmpireWarType.攘夷);
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire == null) return false;
        foreach (var kingdom in World.world.kingdoms)
        {
            if (empire.given_Kingdoms.Contains(kingdom)) continue;
            if (empire.taken_Kingdoms.Contains(kingdom)) continue;
            if (kingdom.IsInEmpire()) continue;
            if (empire.IsNeighbourWith(kingdom))
            {
                if (kingdom.species_id != empire.CoreKingdom.species_id)
                {
                    if (!kingdom.isInWarWith(empire.CoreKingdom))
                    {
                        if (empire.countWarriors() > kingdom.countTotalWarriors())
                        {
                            SetKingdomTarget(kingdom);
                            return true;
                        }
                    }
                }
            }
        }
        return false;
    }
}

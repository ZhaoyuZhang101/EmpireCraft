using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_谋求统一 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_谋求统一();
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
        Kingdom kingdom = GetKingdomTarget();
        if (kingdom != null)
        {
            var war = World.world.diplomacy.startWar(GetEmpire().CoreKingdom, kingdom, WarTypeLibrary.normal);
            if (war != null)
            {
                war.SetEmpireWarType(EmpireWarType.统一);
            }
        }
        CountDown = 5;
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire?.CoreKingdom?.hasEnemies()??true) return false;
        EmpireCore selfCore = EmpireCoreManager.Get(empire);
        foreach (var kingdom in World.world.kingdoms)
        {
            if (empire.given_Kingdoms.Contains(kingdom)) continue;
            if (empire.taken_Kingdoms.Contains(kingdom)) continue;
            if (kingdom.IsLocalRebelling()) continue;
            if (kingdom.IsFactionRebelling()) continue;
            if (kingdom.IsEmpire() && !kingdom.IsInSameEmpire(empire.CoreKingdom))
            {
                EmpireCore targetCore = EmpireCoreManager.GetRiseCandidateCore(kingdom);
                if (selfCore != null && targetCore != null && selfCore.id == targetCore.id)
                {
                    Empire targetEmpire = kingdom.GetEmpire();
                    if (targetEmpire != null && empire.countWarriors() >= targetEmpire.countWarriors())
                    {
                        SetKingdomTarget(targetEmpire.CoreKingdom);
                        return true;
                    }
                }
            }
            if (empire.GetKingdomNeighbours().Any(k => k.IsInEmpire() && k == kingdom && k.getSpecies()==empire.CoreKingdom.getSpecies()))
            {
                var targetEmpire = kingdom.GetEmpire();
                if (empire.countWarriors() > targetEmpire.countWarriors())
                {
                    SetKingdomTarget(targetEmpire.CoreKingdom);
                    return true;
                }
            };
            if (kingdom.IsInEmpire()) continue;
            if (empire.IsNeighbourWith(kingdom))
            {
                if (kingdom.species_id == empire.CoreKingdom.species_id)
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

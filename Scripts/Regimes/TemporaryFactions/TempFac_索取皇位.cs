using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions;

public class TempFac_索取皇位 : TemporaryFaction
{
    public override bool Hide => true;
    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        var target = GetActorTarget();
        if (target != null)
        {
            var empire = GetEmpire();
            empire.CoreKingdom.setKing(target);
            target.setKingdom(empire.CoreKingdom);
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire != null)
        {
            if (!empire.CoreKingdom.GetRegime().has_cabinet) return false;
            if (!empire.HasEmperor() || !empire.Emperor.isAdult())
            {
                var target = empire.GetCabinetLeader();
                if (target == null) return false;
                if (empire.GetCabinetMembers()
                    .All(c => c?.GetFaction() == empire.CoreKingdom?.GetRegime()?.GetDominateFaction()))
                {
                    if (!empire.HasEmperor()) Acc = 40;
                    else if (!empire.Emperor.isAdult()) Acc = 0;
                    SetActorTarget(target);
                    return true;
                }
            }
        }
        return false;
    }
}

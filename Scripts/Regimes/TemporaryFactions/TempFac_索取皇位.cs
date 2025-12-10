using System.Linq;
using EmpireCraft.Scripts.Enums;
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
            if (empire.CoreKingdom.GetMoney() <= 0)
            {
                War war = null;
                foreach (var kingdom in empire.kingdoms_hashset)
                {
                    if (kingdom.IsEmpire()) continue;
                    if (kingdom?.king?.GetFaction()==target.GetFaction() && war == null) continue;
                    if (war == null)
                    {
                        war = DiplomacyHelpers.wars.newWar(kingdom, empire.CoreKingdom, WarTypeLibrary.normal);
                        war.SetEmpireWarType(EmpireWarType.清君侧);
                    }
                    else
                    {
                        if (kingdom?.king?.GetFaction() == target.GetFaction())
                        {
                            war.joinDefenders(kingdom);
                        }
                        else
                        {
                            war.joinAttackers(kingdom);
                        }
                    }
                }
            }
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire != null)
        {
            if (!empire.CoreKingdom.GetRegime().has_cabinet)
            {
                if (!empire.HasEmperor() || !empire.Emperor.isAdult())
                {
                    var leader = empire.CoreKingdom?.GetRegime()?.GetDominateFaction()?.GetLeader();
                    if (leader != null)
                    {
                        if (!empire.HasEmperor()) Acc = 40;
                        else if (!empire.Emperor.isAdult()) Acc = 0;
                        SetActorTarget(leader);
                        return true;
                    }
                }
            }
            else
            {
                if (!empire.HasEmperor() || !empire.Emperor.isAdult())
                {
                    var target = empire.GetCabinetLeader();
                    if (target == null) return false;
                    if (target.GetFaction() == empire.CoreKingdom?.GetRegime()?.GetDominateFaction())
                    {
                        if (!empire.HasEmperor()) Acc = 40;
                        else if (!empire.Emperor.isAdult()) Acc = 0;
                        SetActorTarget(target);
                        return true;
                    }
                }
            }

        }
        return false;
    }
}

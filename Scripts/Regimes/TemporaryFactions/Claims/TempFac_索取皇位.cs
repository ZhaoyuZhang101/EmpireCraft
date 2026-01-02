using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_索取皇位 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_索取皇位();
        res.Init(faction);
        return res;
    }
    public override void Init(FixedFaction faction)
    {
        base.Init(faction);
        base.Hide = true;
    }

    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        var target = GetActorTarget();
        if (target != null)
        {
            var empire = GetEmpire();
            empire.CoreKingdom.GetOffice().meta_object = empire.CoreKingdom;
            empire.CoreKingdom.GetOffice().SetActor(target);
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
    public override bool CheckContinue()
    {
        Empire empire = GetEmpire();
        return empire?.Emperor == null || !empire.Emperor.isAdult();
    }
    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire != null)
        {
            if (!empire.CoreKingdom.GetRegime().has_cabinet)
            {
                if (!empire.HasEmperor())
                {
                    var leader = empire.CoreKingdom?.GetRegime()?.GetDominateFaction()?.GetLeader();
                    if (empire.CoreKingdom.GetRegime().type == RegimeType.Feudalism)
                    {
                        var id = empire.CoreKingdom?.GetRegime()?.GetDominateFaction()?.Members?.Take(3)
                            .FirstOrDefault()??-1L;
                        leader = World.world.units.get(id);
                    }
                    
                    if (leader != null)
                    {
                        SetActorTarget(leader);
                        return true;
                    }
                }
            }
            else
            {
                if (!empire.HasEmperor())
                {
                    var target = empire.CoreKingdom?.GetRegime()?.type== RegimeType.Feudalism?empire.CoreKingdom?.GetRegime()?.GetDominateFaction()?.GetLeader():empire.GetCabinetLeader();
                    if (target == null) return false;
                    if (target.GetFaction() == empire.CoreKingdom?.GetRegime()?.GetDominateFaction())
                    {
                        SetActorTarget(target);
                        return true;
                    }
                }
            }
        }
        return false;
    }
}

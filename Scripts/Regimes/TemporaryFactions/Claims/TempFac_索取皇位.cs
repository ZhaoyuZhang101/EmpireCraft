using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NCMS.Extensions;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_索取皇位 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_索取皇位();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
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
        Actor newEmperor = null;
        if (target != null)
        {
            var empire = GetEmpire();
            if (empire.Mandate >= 70)
            {
                if (empire.EmpireSpecificClan != null)
                {
                    var kingCandidate = empire.EmpireSpecificClan.all_valid_members.FindAll(v=>v._actor.isKing());
                    var normalCandidate = empire.EmpireSpecificClan.all_valid_members.FindAll(v=>!v._actor.isKing());
                    if (kingCandidate.Count > 0)
                    {
                        newEmperor = kingCandidate.OrderByDescending(k=>k._actor.kingdom.countTotalWarriors()).FirstOrDefault()?._actor;
                        if (newEmperor != null) TranslateHelper.LogMinisterSelectEmpire(empire, newEmperor.GetOffice(), newEmperor.kingdom, newEmperor);
                        
                    }
                    if (newEmperor == null)
                    {
                        if (normalCandidate.Count > 0)
                        {
                            newEmperor = kingCandidate.OrderBy(k => k._actor.GetIdentity()?.honoraryOfficial??999)
                                .FirstOrDefault()
                                ?._actor;
                            if (newEmperor != null) TranslateHelper.LogMinisterSelectEmpire(empire, newEmperor.GetOffice(), null, newEmperor);
                        }
                    }

                    if (newEmperor != null)
                    {
                        empire.CoreKingdom.GetOffice().meta_object = empire.CoreKingdom;
                        empire.CoreKingdom.GetOffice().SetActor(newEmperor);
                        End();
                        return;
                    }
                }
            } else if (empire.Mandate >= 30 && (empire.CoreKingdom.GetRegime().type == RegimeType.LvLing || empire.CoreKingdom.GetRegime().type == RegimeType.ZhouFeudalism))
            {
                War war = null;
                foreach (var kingdom in empire.kingdoms_list)
                {
                    if (!kingdom.StartLocalRebelling(EmpireWarType.藩王索取皇位)) continue;
                    if (war == null)
                    {
                        war = DiplomacyHelpers.diplomacy.startWar(kingdom, empire.CoreKingdom, WarTypeLibrary.normal);
                        war.SetEmpireWarType(EmpireWarType.藩王索取皇位);
                    }
                    else
                    {
                        war.joinAttackers(kingdom);
                    }
                }

                if (war != null)
                {
                    End();
                    return;
                }
            }
            empire.CoreKingdom.GetOffice().meta_object = empire.CoreKingdom;
            empire.CoreKingdom.GetOffice().SetActor(target);
            if (empire.Mandate<30)
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
        return empire?.Emperor == null;
    }
    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire != null)
        {
            if (empire.getWars().Any(w=>w.GetEmpireWarType()== EmpireWarType.藩王索取皇位)) return false;
            if (!empire.CoreKingdom.GetRegime().has_cabinet)
            {
                if (!empire.HasEmperor()&&!empire.CoreKingdom.HasHeir())
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
                if (!empire.HasEmperor()&&!empire.CoreKingdom.HasHeir())
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

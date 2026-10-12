using System;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.ActorAI;

public class EmpireCraftActorCheckOffice:GameAIActorBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Actor pActor)
    {
        if (pActor.isRekt()) return BehResult.Continue;
        if (!pActor.IsOnOffice()) return BehResult.Continue;
        var office = pActor.GetOffice();
        if (office == null)
        {
            pActor.EndOffice();
            return BehResult.Continue;
        }

        // 妃嫔、在位君主的配偶不担任后宫以外的官职(已在任的卸任，另选他人)
        bool rulerOffice = office.meta_object is Kingdom ruled && ruled.GetOffice() == office;
        if (!pActor.CanServeOffice(pActor.kingdom) ||
            office.leader_select_method != EmpireCraft.Scripts.Regimes.LeaderSelectMethod.Harem && !rulerOffice &&
            pActor.IsHaremOrConsort())
        {
            office.RemoveActor();
            pActor.EndOffice();
            return BehResult.Continue;
        }

        if (pActor.isKing())
        {
            if (pActor.kingdom.HasMainTitle())
            {
                var mainTitle = pActor.kingdom.GetMainTitle();
                if (mainTitle.owner.isRekt())
                {
                    pActor.AddOwnedTitle(mainTitle);
                    (pActor.kingdom.GetEmpire() ?? pActor.kingdom.GetTakenAllianceEmpire())?
                        .SynchronizeLandedLegalTitles(pActor.kingdom);
                }
            }
        }
        if (office.actor_id == pActor.id) return BehResult.Continue;
        pActor.EndOffice();
        return BehResult.Continue;
    }
}

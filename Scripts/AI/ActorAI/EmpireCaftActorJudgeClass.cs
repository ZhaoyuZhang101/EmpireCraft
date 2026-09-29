using System;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.AI.ActorAI;

public class EmpireCaftActorJudgeClass: GameAIActorBase
{
    public override Type OriginalBeh => GetType();
    public override BehResult execute(Actor pActor)
    {
        pActor.SetSocialClass(JudgeClass(pActor));
        return BehResult.Continue;
    }
    public static SocialClass JudgeClass( Actor pActor)
    {
        if (world.kingdoms.ToList().FindAll(k => k.GetRegime() != null).Any(k =>
                k.GetRegime().GetLeaderSelectMethod() == LeaderSelectMethod.Succession &&
                k.getKingClan() == pActor.clan))
        {
            return SocialClass.Noble;
        }
        if (pActor.IsOnOffice())
        {
            return SocialClass.Officer;
        }
        if (pActor.isWarrior())
        {
            return SocialClass.Army;
        }
        if (IsManualWorkerJob(pActor)) return SocialClass.Labour;
        if (LandEconomySystem.IsLandlord(pActor)) return SocialClass.Landlord;
        if (pActor.GetOrCreate().is_economic_merchant) return SocialClass.Merchant;
        if (UrbanEmploymentSystem.IsEmployed(pActor)) return SocialClass.Labour;
        return SocialClass.Peasant;
    }

    public static bool IsManualWorkerJob(Actor actor) => actor?.citizen_job?.id switch
    {
        "woodcutter" or "miner" or "miner_deposit" or "road_builder" or "cleaner" or
            "manure_cleaner" or "gatherer_herbs" or "gatherer_bushes" or "gatherer_honey" => true,
        _ => false
    };
}

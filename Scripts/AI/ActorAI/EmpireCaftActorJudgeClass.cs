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
        if (pActor.citizen_job != null)
        {
            switch (pActor.citizen_job.id)
            {
                case "woodcutter":
                case "miner":
                case "miner_deposit":
                case "road_builder":
                case "cleaner":
                case "manure_cleaner":
                case "gatherer_herbs":
                case "gatherer_bushes":
                case "gatherer_honey":
                    return SocialClass.Labour;
            }
        }
        if (LandEconomySystem.IsLandlord(pActor)) return SocialClass.Landlord;
        if (pActor.GetOrCreate().is_economic_merchant) return SocialClass.Merchant;
        return SocialClass.Peasant;
    }
}

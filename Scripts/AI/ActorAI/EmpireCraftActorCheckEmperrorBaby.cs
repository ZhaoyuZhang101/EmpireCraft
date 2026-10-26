using System;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.System;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.ActorAI;

public class EmpireCraftActorCheckEmperorBaby: GameAIActorBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Actor pActor)
    {
        if (!pActor.IsEmperor()) return BehResult.Continue;
        if (!pActor.hasLover()) return BehResult.Continue;
        if (!pActor.isAdult()) return BehResult.Continue;
        if (!pActor.HasSpecificClan()) return BehResult.Continue;
        var sc = pActor.GetSpecificClan();
        var pc = pActor.GetPersonalIdentity();
        if (sc == null || pc==null) return BehResult.Continue;
        pc.is_main = true;
        if (sc.GetChildren(pc).Any(c => c.Item2.CanHeir(pc))) return BehResult.Continue;
        // 一年半至多一个、不育的生不了(见 RoyalHouseSystem)：不再保证代代有子
        if (!EmpireCraft.Scripts.GeneralSystems.RoyalHouseSystem.CanBirth(pActor)) return BehResult.Continue;
        var lover = pActor.lover;
        // 原版几岁就算成年：君主与配偶都到生养的年纪才强制生育(见 RealisticAgeSystem.MinParentAge)
        if (!EmpireCraft.Scripts.GeneralSystems.RealisticAgeSystem.OldEnoughToParent(pActor) ||
            !EmpireCraft.Scripts.GeneralSystems.RealisticAgeSystem.OldEnoughToParent(lover)) return BehResult.Continue;
        var lsc = lover.GetPersonalIdentity();
        if (lsc == null) return BehResult.Continue;
        lsc.is_main = false;
        //无子嗣强制生育
        var baby = BabyMaker.makeBaby(pActor, lover,
            sc.clan_sex_priority == SpecificClanType.FemalePriority ? ActorSex.Female : ActorSex.Male);
        return BehResult.Continue;
    }
}
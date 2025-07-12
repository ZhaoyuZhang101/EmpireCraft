using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GameLibrary;
public static class EmpireCraftActorTraitLibrary
{
    public static void init()
    {
        ActorTraitLibrary lib = AssetManager.traits;
        lib.add(new ActorTrait
        {
            id = "jingshi",
            path_icon = "ui/icons/actor_traits/iconJingshi",
            group_id = "EmpireExam",
            action_on_add = pass_empire_exam
        });
        lib.add(new ActorTrait
        {
            id = "gongshi",
            path_icon = "ui/icons/actor_traits/iconGongshi",
            group_id = "EmpireExam",
            action_on_add = pass_province_exam
        });
        lib.add(new ActorTrait
        {
            id = "juren",
            path_icon = "ui/icons/actor_traits/iconJuren",
            group_id = "EmpireExam",
            action_on_add = pass_city_exam
        });
        lib.add(new ActorTrait
        {
            id = "officer",
            path_icon = "ui/icons/actor_traits/officer",
            group_id = "EmpireOffice",
            action_on_add = become_officer
        });
        lib.add(new ActorTrait
        {
            id = "officerLeave",
            path_icon = "ui/icons/actor_traits/officerLeave",
            group_id = "EmpireOffice",
            action_on_add = office_leave
        });
    }
    private static bool office_leave(NanoObject pTarget, BaseAugmentationAsset pTrait)
    {
        Actor actor = (Actor)pTarget;
        if(actor.hasTrait("officer"))
        {
            actor.removeTrait("officer");
        }
        LogService.LogInfo("告老还乡");
        return true;
    }
    private static bool become_officer(NanoObject pTarget, BaseAugmentationAsset pTrait)
    {
        Actor actor = (Actor)pTarget;
        if (actor.hasTrait("officerLeave"))
        {
            actor.removeTrait("officerLeave");
        }
        LogService.LogInfo("当官");
        return true;
    }

    private static bool pass_city_exam(NanoObject pTarget, BaseAugmentationAsset pTrait)
    {
        Actor actor = (Actor)pTarget;
        return true;
    }

    private static bool pass_province_exam(NanoObject pTarget, BaseAugmentationAsset pTrait)
    {
        Actor actor = (Actor)pTarget;
        if(actor.hasTrait("juren"))
        {
            actor.removeTrait("juren");
        }
        return true;
    }

    private static bool pass_empire_exam(NanoObject pTarget, BaseAugmentationAsset pTrait)
    {
        Actor actor = (Actor)pTarget;
        if (actor.hasTrait("gongshi"))
        {
            actor.removeTrait("gongshi");
        }
        if (actor.kingdom.GetEmpire()==null) return true;
        TranslateHelper.LogNewJingShi(actor.kingdom.GetEmpire(), actor);
        return true;
    }
}

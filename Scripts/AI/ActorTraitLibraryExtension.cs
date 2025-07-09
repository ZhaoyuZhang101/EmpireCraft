using EmpireCraft.Scripts.GameClassExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.AI;
public static class ActorTraitLibraryExtension
{
    public static void init ()
    {
        ActorTraitLibrary lib = AssetManager.traits;
        lib.add(new ActorTrait
        {
            id = "jingshi",
            path_icon = "ui/Icons/actor_traits/trait_jingshi",
            group_id = "EmpireExam",
            action_on_add = pass_empire_exam
        });
        lib.add(new ActorTrait
        {
            id = "gongshi",
            path_icon = "ui/Icons/actor_traits/trait_gongshi",
            group_id = "EmpireExam",
            action_on_add = pass_province_exam
        });
        lib.add(new ActorTrait
        {
            id = "juren",
            path_icon = "ui/Icons/actor_traits/trait_juren",
            group_id = "EmpireExam",
            action_on_add = pass_city_exam
        });
    }

    private static bool pass_city_exam(NanoObject pTarget, BaseAugmentationAsset pTrait)
    {
        Actor actor = (Actor)pTarget;
        actor.city.AddExamPassPerson(actor);
        return true;
    }

    private static bool pass_province_exam(NanoObject pTarget, BaseAugmentationAsset pTrait)
    {
        Actor actor = (Actor)pTarget;
        actor.city.GetProvince().AddExamPassPerson(actor);
        return true;
    }

    private static bool pass_empire_exam(NanoObject pTarget, BaseAugmentationAsset pTrait)
    {
        Actor actor = (Actor)pTarget;
        actor.city.kingdom.GetEmpire().AddExamPassPerson(actor);
        return true;
    }
}

using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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
            action_on_augmentation_add = pass_empire_exam
        });
        lib.add(new ActorTrait
        {
            id = "gongshi",
            path_icon = "ui/icons/actor_traits/iconGongshi",
            group_id = "EmpireExam",
            action_on_augmentation_add = pass_province_exam
        });
        lib.add(new ActorTrait
        {
            id = "juren",
            path_icon = "ui/icons/actor_traits/iconJuren",
            group_id = "EmpireExam",
            action_on_augmentation_add = pass_city_exam
        });
        lib.add(new ActorTrait
        {
            id = "officer",
            path_icon = "ui/icons/actor_traits/iconEmpireOfficer",
            group_id = "EmpireOffice",
            action_on_augmentation_add = become_officer
        });
        lib.add(new ActorTrait
        {
            id = "officerLeave",
            path_icon = "ui/icons/actor_traits/iconOfficerLeave",
            group_id = "EmpireOffice",
            action_on_augmentation_add = office_leave
        });
        lib.add(new ActorTrait
        {
            id = "cleric",
            path_icon = "ui/icons/actor_traits/iconOfficerLeave",
            group_id = "EmpireOffice"
        });
        lib.add(new ActorTrait
        {
            id = "revolutionary",
            path_icon = "ui/icons/actor_traits/iconEmpireArmy",
            group_id = "EmpireOffice"
        });
        lib.add(new ActorTrait
        {
            id = "empireSoldier",
            path_icon = "ui/icons/actor_traits/iconEmpireArmy",
            group_id = "EmpireArmy",
            action_on_augmentation_add = been_soldier
        });
        lib.t.base_stats["damage"] = 20f;
        lib.t.base_stats["speed"] = 20f;
        lib.t.base_stats["armor"] = 40f;
        lib.t.base_stats["critical_damage_multiplier"] = 0.3f;
        lib.t.base_stats["critical_chance"] = 0.4f;
        lib.add(new ActorTrait
        {
            id = "empireArmedProvinceSoldier",
            path_icon = "ui/icons/actor_traits/iconEmpireEliteArmy",
            group_id = "EmpireArmy",
            action_on_augmentation_add = been_elite_soldier
        });
        lib.t.base_stats["damage"] = 40f;
        lib.t.base_stats["speed"] = 40f;
        lib.t.base_stats["armor"] = 50f;
        lib.t.base_stats["critical_chance"] = 0.8f;
        lib.t.base_stats["critical_damage_multiplier"] = 0.5f;

        // 文化科技树"战术研究"类节点解锁后发放的特质：不是靠身份(入伍/晋升)获得，
        // 而是靠制度研究获得，所以数值比 empireSoldier/empireArmedProvinceSoldier
        // (那两个是"身份"特质)小一些，算是在身份特质之上的额外加成，不是替代关系。
        // 图标复用游戏自带的 ui/icons/iconWar(本模组其它地方已经在用这个图标，确认加载没问题)，
        // 跟 empireSoldier/empireArmedProvinceSoldier 已经占用的两张兵种图标区分开。
        lib.add(new ActorTrait
        {
            id = "tacticalDrilling",
            path_icon = "ui/icons/iconWar",
            group_id = "EmpireArmy"
        });
        lib.t.base_stats["damage"] = 10f;
        lib.t.base_stats["speed"] = 10f;
    }
    public static bool been_soldier(NanoObject pTarget, BaseAugmentationAsset pTrait)
    {
        Actor actor = (Actor)pTarget;
        if (actor.hasTrait("empireArmedProvinceSoldier"))
        {
            actor.removeTrait("empireArmedProvinceSoldier");
        }
        return true;
    }
    public static bool been_elite_soldier(NanoObject pTarget, BaseAugmentationAsset pTrait)
    {
        Actor actor = (Actor)pTarget;
        if (actor.hasTrait("empireSoldier"))
        {
            actor.removeTrait("empireSoldier");
        }
        return true;
    }
    private static bool office_leave(NanoObject pTarget, BaseAugmentationAsset pTrait)
    {
        Actor actor = (Actor)pTarget;
        if(actor.hasTrait("officer"))
        {
            actor.removeTrait("officer");
        }
        if(actor.city == null) return false;
        if(actor.city.kingdom == null) return false;
        if (actor.city.kingdom.IsInEmpire())
        {
            actor.GetIdentity()?.ChangeOfficialLevel(-1);
        }
        return true;
    }
    private static bool become_officer(NanoObject pTarget, BaseAugmentationAsset pTrait)
    {
        Actor actor = (Actor)pTarget;
        if (actor.hasTrait("officerLeave"))
        {
            actor.removeTrait("officerLeave");
        }
        return true;
    }

    private static bool pass_city_exam(NanoObject pTarget, BaseAugmentationAsset pTrait)
    {
        Actor actor = (Actor)pTarget;
        actor.data.renown += 5;
        return true;
    }

    private static bool pass_province_exam(NanoObject pTarget, BaseAugmentationAsset pTrait)
    {
        Actor actor = (Actor)pTarget;
        if(actor.hasTrait("juren"))
        {
            actor.removeTrait("juren");
        }
        actor.data.renown += 5;
        return true;
    }

    private static bool pass_empire_exam(NanoObject pTarget, BaseAugmentationAsset pTrait)
    {
        Actor actor = (Actor)pTarget;
        if (actor.hasTrait("gongshi"))
        {
            actor.removeTrait("gongshi");
        }
        actor.data.renown += 5;
        if (actor.kingdom.GetEmpire()==null) return true;
        TranslateHelper.LogNewJingShi(actor.kingdom.GetEmpire(), actor);
        return true;
    }
}

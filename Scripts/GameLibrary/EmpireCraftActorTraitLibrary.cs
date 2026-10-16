using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.GeneralSystems;
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
        IdeologyTraitIcons.Register();
        // 理念特质的说明用理念图鉴里的说明(institution_ideology_<理念>_desc；用户 2026-10-17 "理念都没用本地化还有说明")
        foreach (PartyIdeology ideology in Enum.GetValues(typeof(PartyIdeology)))
            lib.add(new ActorTrait
            {
                id = IdeologyPopulationSystem.TraitId(ideology),
                path_icon = IdeologyTraitIcons.Path(ideology),
                group_id = "EmpireIdeology",
                special_locale_description = "institution_ideology_" + string.Concat(ideology.ToString().Select((ch, index) =>
                    index > 0 && char.IsUpper(ch) ? "_" + char.ToLowerInvariant(ch) : char.ToLowerInvariant(ch).ToString())) + "_desc",
                action_on_augmentation_add = IdeologyPopulationSystem.OnTraitAdded
            });
        IdeologyPopulationSystem.SetTraitsReady();
        // 宦官(阉党，见 CliqueSystem)
        lib.add(new ActorTrait
        {
            id = CliqueSystem.EunuchTrait,
            path_icon = IdeologyTraitIcons.Path(PartyIdeology.Eunuchs),
            group_id = "EmpireIdeology"
        });
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

        // 开国雄主 / 中兴之主：授予条件与忠诚、腐败效果见 RulerTraitSystem
        lib.add(new ActorTrait
        {
            id = RulerTraitSystem.Founder,
            path_icon = "ui/icons/actor_traits/iconFounderRuler",
            group_id = "EmpireOffice"
        });
        lib.t.base_stats["lifespan"] = 10f;
        lib.t.base_stats["multiplier_lifespan"] = 0.3f;
        lib.add(new ActorTrait
        {
            id = RulerTraitSystem.Restorer,
            path_icon = "ui/icons/actor_traits/iconRestorerRuler",
            group_id = "EmpireOffice"
        });
        // 中兴雄主(极品中兴之主)：与开国雄主同样长寿，其余特性见 RulerTraitSystem
        lib.add(new ActorTrait
        {
            id = RulerTraitSystem.GreatRestorer,
            path_icon = "ui/icons/actor_traits/iconGreatRestorerRuler",
            group_id = "EmpireOffice"
        });
        lib.t.base_stats["lifespan"] = 10f;
        lib.t.base_stats["multiplier_lifespan"] = 0.3f;

        // 君主资质(即位时定性，效果见 RulerQualitySystem)：明君为正面特质，其余为负面(计入个人品行)
        lib.add(new ActorTrait
        {
            id = RulerQualitySystem.Wise,
            path_icon = "ui/icons/actor_traits/iconWiseRuler",
            group_id = "EmpireOffice",
            type = TraitType.Positive
        });
        lib.add(new ActorTrait
        {
            id = RulerQualitySystem.Weak,
            path_icon = "ui/icons/actor_traits/iconWeakRuler",
            group_id = "EmpireOffice",
            type = TraitType.Negative
        });
        lib.add(new ActorTrait
        {
            id = RulerQualitySystem.Fatuous,
            path_icon = "ui/icons/actor_traits/iconFatuousRuler",
            group_id = "EmpireOffice",
            type = TraitType.Negative
        });
        lib.t.base_stats["multiplier_lifespan"] = -0.1f;
        lib.add(new ActorTrait
        {
            id = RulerQualitySystem.Dissolute,
            path_icon = "ui/icons/actor_traits/iconDissoluteRuler",
            group_id = "EmpireOffice",
            type = TraitType.Negative
        });
        lib.t.base_stats["lifespan"] = -10f;
        lib.t.base_stats["multiplier_lifespan"] = -0.25f;
        lib.add(new ActorTrait
        {
            id = RulerQualitySystem.Tyrant,
            path_icon = "ui/icons/actor_traits/iconTyrantRuler",
            group_id = "EmpireOffice",
            type = TraitType.Negative
        });

        // 名将名臣(成名时按档位发一条，晋档换掉旧的，见 FamousTalentSystem)：名将重攻防兵略，名臣重治事智力外交，
        // 都带寿数加成；兵略会直接抬高战时统帅给全军的加成(见 WarCommandSystem.Factor)
        AddTalentTrait(lib, FamousTalentSystem.GeneralFine, "iconTalentGeneralFine",
            ("warfare", 4f), ("damage", 25f), ("health", 150f), ("armor", 20f), ("speed", 10f),
            ("critical_chance", 0.1f), ("multiplier_lifespan", 0.1f));
        AddTalentTrait(lib, FamousTalentSystem.GeneralFamous, "iconTalentGeneralFamous",
            ("warfare", 8f), ("damage", 50f), ("health", 400f), ("armor", 35f), ("speed", 20f),
            ("critical_chance", 0.2f), ("critical_damage_multiplier", 0.3f), ("multiplier_lifespan", 0.15f));
        AddTalentTrait(lib, FamousTalentSystem.GeneralPillar, "iconTalentGeneralPillar",
            ("warfare", 14f), ("damage", 90f), ("health", 900f), ("armor", 50f), ("speed", 30f),
            ("critical_chance", 0.3f), ("critical_damage_multiplier", 0.5f), ("multiplier_lifespan", 0.25f));
        AddTalentTrait(lib, FamousTalentSystem.MinisterFine, "iconTalentMinisterFine",
            ("stewardship", 4f), ("intelligence", 2f), ("diplomacy", 2f), ("lifespan", 10f), ("multiplier_lifespan", 0.1f));
        AddTalentTrait(lib, FamousTalentSystem.MinisterFamous, "iconTalentMinisterFamous",
            ("stewardship", 8f), ("intelligence", 4f), ("diplomacy", 4f), ("lifespan", 15f), ("multiplier_lifespan", 0.15f));
        AddTalentTrait(lib, FamousTalentSystem.MinisterPillar, "iconTalentMinisterPillar",
            ("stewardship", 14f), ("intelligence", 8f), ("diplomacy", 8f), ("lifespan", 25f), ("multiplier_lifespan", 0.2f));
    }

    private static void AddTalentTrait(ActorTraitLibrary lib, string id, string icon,
        params (string stat, float value)[] stats)
    {
        lib.add(new ActorTrait
        {
            id = id,
            path_icon = "ui/icons/actor_traits/" + icon,
            group_id = "EmpireOffice",
            type = TraitType.Positive
        });
        foreach ((string stat, float value) in stats) lib.t.base_stats[stat] = value;
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

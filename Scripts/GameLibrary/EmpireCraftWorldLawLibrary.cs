using NeoModLoader.services;
using EmpireCraft.Scripts;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.GeneralSystems;
using NCMS.Extensions;
using System.Linq;

namespace EmpireCraft.Scripts.GameLibrary;

public static class EmpireCraftWorldLawLibrary
{
    public static WorldLawAsset world_law_civ_limit_population_50;
    public static WorldLawAsset world_law_civ_limit_population_20;
    public static WorldLawAsset empirecraft_law_realistic_age;
    public static WorldLawAsset empirecraft_law_prevent_city_destroy;
    public static WorldLawAsset empirecraft_law_ban_empire;
    public static WorldLawAsset empirecraft_law_simplify_nameplates;
    public static WorldLawAsset empirecraft_law_prevent_building_destroy;
    public static WorldLawAsset empirecraft_law_allow_harem;
    public static WorldLawAsset empirecraft_law_allow_skeleton;
    public static WorldLawAsset empirecraft_law_switch_occupy_mode;
    public static WorldLawAsset empirecraft_law_allow_social;
    public static WorldLawAsset empirecraft_law_fixed_de_jure_culture;
    public static WorldLawAsset empirecraft_law_ban_vanilla_alliance;
    public static void init()
    {
        LogService.LogInfo("加载帝国世界规则");
        AssetManager.world_laws_library.list.RemoveAll(w => w.id == "world_law_civ_limit_population_100");
        //限制人口100
        AssetManager.world_laws_library.add(WorldLawLibrary.world_law_civ_limit_population_100  = new WorldLawAsset()
        {
            id = "world_law_civ_limit_population_100",
            group_id = "harmony",
            icon_path = "ui/Icons/iconPopulation100",
            on_state_enabled = Population100On,
            default_state = false
        });
        //限制人口50
        AssetManager.world_laws_library.add(world_law_civ_limit_population_50 = new WorldLawAsset()
        {
            id = "world_law_civ_limit_population_50",
            group_id = "harmony",
            icon_path = "ui/Icons/iconPopulation50",
            on_state_enabled = Population50On,
            default_state = false
        });
        //限制人口20
        AssetManager.world_laws_library.add(world_law_civ_limit_population_20= new WorldLawAsset()
        {
            id = "world_law_civ_limit_population_20",
            group_id = "harmony",
            icon_path = "ui/Icons/iconPopulation20",
            on_state_enabled = Population20On,
            default_state = false
        });
        //真实年龄
        AssetManager.world_laws_library.add(empirecraft_law_realistic_age= new WorldLawAsset()
        {
            id = "empirecraft_law_realistic_age",
            group_id = "EmpireCraftCommonSetting",
            icon_path = "ui/Icons/actor_traits/iconDeathMark",
            default_state = true
        });
        //阻止城市毁灭
        AssetManager.world_laws_library.add(empirecraft_law_prevent_city_destroy= new WorldLawAsset()
        {
            id = "empirecraft_law_prevent_city_destroy",
            group_id = "EmpireCraftCommonSetting",
            icon_path = "ui/icons/iconCity",
            default_state = true
        });
        //禁止称帝
        AssetManager.world_laws_library.add(empirecraft_law_ban_empire= new WorldLawAsset()
        {
            id = "empirecraft_law_ban_empire",
            group_id = "EmpireCraftCommonSetting",
            icon_path = "ui/icons/iconKingdom",
            default_state = false
        });
        //简化铭牌
        AssetManager.world_laws_library.add(empirecraft_law_simplify_nameplates= new WorldLawAsset()
        {
            id = "empirecraft_law_simplify_nameplates",
            group_id = "EmpireCraftCommonSetting",
            icon_path = "ui/icons/iconSimplifyNamePlate",
            default_state = false
        });
        //阻止建筑毁灭
        AssetManager.world_laws_library.add(empirecraft_law_prevent_building_destroy= new WorldLawAsset()
        {
            id = "empirecraft_law_prevent_building_destroy",
            group_id = "EmpireCraftCommonSetting",
            icon_path = "buildings/civ_main/human/barracks_human/main_0",
            default_state = false
        });
        //后宫开关
        AssetManager.world_laws_library.add(empirecraft_law_allow_harem = new WorldLawAsset()
        {
            id = "empirecraft_law_allow_harem",
            group_id = "EmpireCraftCommonSetting",
            icon_path = "ui/icons/iconKingdom",
            default_state = true
        });
        //开启骷髏
        AssetManager.world_laws_library.add(empirecraft_law_allow_skeleton = new WorldLawAsset()
        {
            id = nameof(empirecraft_law_allow_skeleton),
            group_id = "EmpireCraftCommonSetting",
            icon_path = "ui/icons/iconSkeleton",
            default_state = false
        });
        //帝国占领模式
        AssetManager.world_laws_library.add(empirecraft_law_switch_occupy_mode = new WorldLawAsset()
        {
            id = nameof(empirecraft_law_switch_occupy_mode),
            group_id = "EmpireCraftCommonSetting",
            icon_path = "ui/icons/iconWar",
            default_state = true
        });
        //允许社交
        AssetManager.world_laws_library.add(empirecraft_law_allow_social = new WorldLawAsset()
        {
            id = nameof(empirecraft_law_allow_social),
            group_id = "EmpireCraftCommonSetting",
            icon_path = "ui/icons/iconWar",
            on_state_change = AllowSocialChange,
            default_state = false
        });
        // 固定法理文化及其辖内城市的官方主流文化。居民文化与人口流动继续运行，
        // 因而解锁后城市仍可从当时的人口结构重新开始文化转变流程。
        AssetManager.world_laws_library.add(empirecraft_law_fixed_de_jure_culture = new WorldLawAsset()
        {
            id = nameof(empirecraft_law_fixed_de_jure_culture),
            group_id = "EmpireCraftCommonSetting",
            icon_path = "ui/icons/iconCulture",
            on_state_change = FixedDeJureCultureChange,
            default_state = false
        });
        // 禁止原版结盟(默认开启)：原版的建盟/入盟决议不再发起；模组自己的同盟不受影响
        AssetManager.world_laws_library.add(empirecraft_law_ban_vanilla_alliance = new WorldLawAsset()
        {
            id = nameof(empirecraft_law_ban_vanilla_alliance),
            group_id = "EmpireCraftCommonSetting",
            icon_path = "plots/icons/plot_alliance_create",
            on_state_change = BanVanillaAllianceChange,
            default_state = true
        });
        
    }

    private static void BanVanillaAllianceChange(PlayerOptionData pOption)
    {
        if (pOption?.boolVal == true) ModAllianceService.DissolveVanillaAlliances();
    }

    private static void FixedDeJureCultureChange(PlayerOptionData pOption)
    {
        if (World.world == null) return;
        if (World.world.cities != null)
        {
            foreach (City city in World.world.cities)
                CultureService.UpdateCityCultureShiftCandidate(city);
        }
        if (World.world.kingdoms != null)
        {
            foreach (Kingdom kingdom in World.world.kingdoms)
                CultureService.UpdateCulturalAssimilationDuty(kingdom);
        }
    }

    private static void AllowSocialChange(PlayerOptionData pOption)
    {
        if (World.world?.units == null || pOption == null) return;
        World.world.units.ForEach(a =>
        {
            if (a?.asset?.civ == true && a.decisions != null)
            {
                foreach (var decision in a.decisions.ToList())
                {
                    if (decision == null) continue;
                    if (ActorPatch.BlockDecisions.Contains(decision.id))
                    {
                        a.setDecisionState(decision._index, pOption.boolVal);
                    }
                }
            }
        });
    }
    
    private static void Population20On(PlayerOptionData pOption)
    {
        world_law_civ_limit_population_50.toggle(false);
        WorldLawLibrary.world_law_civ_limit_population_100.toggle(false);
        RefreshLaws();
    }

    private static void Population50On(PlayerOptionData pOption)
    {
        world_law_civ_limit_population_20.toggle(false);
        WorldLawLibrary.world_law_civ_limit_population_100.toggle(false);
        RefreshLaws();
    }

    private static void Population100On(PlayerOptionData pOption)
    {
        world_law_civ_limit_population_20.toggle(false);
        world_law_civ_limit_population_50.toggle(false);
        RefreshLaws();
    }

    public static void RefreshLaws()
    {        
        ScrollWindow window = ScrollWindow.getCurrentWindow();
        WorldLawElement[] law_window = window.transform.parent.GetComponentsInChildren<WorldLawElement>();
        foreach (var law in law_window)
        {
            law.updateStatus();
        }
    }
}

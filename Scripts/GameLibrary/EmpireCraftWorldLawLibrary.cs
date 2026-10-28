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
    public static WorldLawAsset empirecraft_law_transmigrant_arrival;
    public static WorldLawAsset empirecraft_law_prevent_city_destroy;
    public static WorldLawAsset empirecraft_law_ban_empire;
    public static WorldLawAsset empirecraft_law_simplify_nameplates;
    public static WorldLawAsset empirecraft_law_prevent_building_destroy;
    public static WorldLawAsset empirecraft_law_allow_harem;
    public static WorldLawAsset empirecraft_law_allow_skeleton;
    public static WorldLawAsset empirecraft_law_switch_occupy_mode;
    public static WorldLawAsset empirecraft_law_corps_general;
    public static WorldLawAsset empirecraft_law_allow_social;
    public static WorldLawAsset empirecraft_law_fixed_de_jure_culture;
    public static WorldLawAsset empirecraft_law_ban_vanilla_alliance;
    public static WorldLawAsset empirecraft_law_tech_tree;
    public static WorldLawAsset empirecraft_law_combine_houses;
    public static WorldLawAsset empirecraft_law_premodern;
    public static WorldLawAsset empirecraft_law_no_commoners;
    public static WorldLawAsset empirecraft_law_fixed_settlements;
    public static WorldLawAsset empirecraft_law_surname_dynasty;
    public static WorldLawAsset empirecraft_law_dejure_first;
    public static void init()
    {
        LogService.LogInfo("加载帝国世界规则");
        AssetManager.world_laws_library.list.RemoveAll(w => w.id == "world_law_civ_limit_population_100");
        AssetManager.world_laws_library.dict.Remove("world_law_civ_limit_population_100");
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
        //穿越者随机降临(见 TransmigrantSystem.TryRandomArrival)
        AssetManager.world_laws_library.add(empirecraft_law_transmigrant_arrival = new WorldLawAsset()
        {
            id = nameof(empirecraft_law_transmigrant_arrival),
            group_id = "EmpireCraftCommonSetting",
            icon_path = "ui/icons/godpowers/transmigrant_landing",
            default_state = false
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
        //军团统领实体(占领模式下的军团在地图上由一名统领代表；关闭则只有兵牌)
        AssetManager.world_laws_library.add(empirecraft_law_corps_general = new WorldLawAsset()
        {
            id = nameof(empirecraft_law_corps_general),
            group_id = "EmpireCraftCommonSetting",
            icon_path = "ui/icons/iconWar",
            on_state_change = CorpsGeneralChange,
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
        // 文明科技树(默认开启)：装备材质、枪械和高级建筑需要本文化研究对应技术；关闭则全部放开
        AssetManager.world_laws_library.add(empirecraft_law_tech_tree = new WorldLawAsset()
        {
            id = nameof(empirecraft_law_tech_tree),
            group_id = "EmpireCraftCommonSetting",
            icon_path = "ui/icons/iconKnowledge",
            default_state = true
        });
        // 禁止近代化(默认关闭)：打开后世界停在君主立宪与中古技术，不出现政党政治、共和、工业与各模组的现代内容
        AssetManager.world_laws_library.add(empirecraft_law_premodern = new WorldLawAsset()
        {
            id = nameof(empirecraft_law_premodern),
            group_id = "EmpireCraftCommonSetting",
            icon_path = "ui/icons/iconBooks",
            default_state = false
        });
        // 民居合并为文化城池(默认关闭)：开启后城市人口过 80 拆掉民居、建一座文化大城。
        // 装了 modernmod 想看 6~11 级民居的话不要开。
        AssetManager.world_laws_library.add(empirecraft_law_combine_houses = new WorldLawAsset()
        {
            id = nameof(empirecraft_law_combine_houses),
            group_id = "EmpireCraftCommonSetting",
            icon_path = "ui/icons/iconHoused",
            default_state = false
        });
        // 无小人模式(默认开启)：普通人口由城市人口数据层承载，只保留名人作为实体单位(见 CityPopulationSystem)。
        // 默认值只用于新世界和不含此法则的旧存档；已存过此法则的存档按存档里的开关读取
        AssetManager.world_laws_library.add(empirecraft_law_no_commoners = new WorldLawAsset()
        {
            id = nameof(empirecraft_law_no_commoners),
            group_id = "EmpireCraftCommonSetting",
            icon_path = "ui/icons/iconPopulation",
            default_state = true
        });
        // 固定城乡格局(默认关闭)：开了以后 AI 不新建村镇、兵火不烧、废墟不自己重建，只听手动(见 RuralSettlementSystem.Fixed)
        AssetManager.world_laws_library.add(empirecraft_law_fixed_settlements = new WorldLawAsset()
        {
            id = nameof(empirecraft_law_fixed_settlements),
            group_id = "EmpireCraftCommonSetting",
            icon_path = "ui/icons/godpowers/place_village",
            default_state = false
        });
        // 姓氏国号(默认关闭)：新朝再用本天下前朝用过的国号时冠开国之君的姓，同姓用方位，都分不出才加"后"(见 RealmNamingSystem.Founding)
        AssetManager.world_laws_library.add(empirecraft_law_surname_dynasty = new WorldLawAsset()
        {
            id = nameof(empirecraft_law_surname_dynasty),
            group_id = "EmpireCraftCommonSetting",
            icon_path = "ui/icons/iconClan",
            default_state = false
        });
        // 先收法理(默认开启)：挑宣战目标时占着本国法理城的邻国优先，没有才照常扩张(见 DeJureAgenda)
        AssetManager.world_laws_library.add(empirecraft_law_dejure_first = new WorldLawAsset()
        {
            id = nameof(empirecraft_law_dejure_first),
            group_id = "EmpireCraftCommonSetting",
            icon_path = "ui/icons/iconKingdom",
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

    // 军团(以及"不留军人实体")只在无小人模式 + 帝国占领模式下运行：没开时切换这个开关不会有任何变化，提示一下
    private static void CorpsGeneralChange(PlayerOptionData pOption)
    {
        if (World.world == null || GeneralSystems.CorpsSystem.Enabled) return;
        WorldTip.showNow(NeoModLoader.General.LM.Get("empirecraft_law_corps_general_needs_modes"), false, "top", 5f);
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

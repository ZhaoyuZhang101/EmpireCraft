using NeoModLoader.services;
using EmpireCraft.Scripts;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.GeneralSystems;
using NCMS.Extensions;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

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
    public static WorldLawAsset empirecraft_law_tech_calendar;
    public static WorldLawAsset empirecraft_law_no_commoners;
    public static WorldLawAsset empirecraft_law_fixed_settlements;
    public static WorldLawAsset empirecraft_law_fixed_city_borders;
    public static WorldLawAsset empirecraft_law_surname_dynasty;
    public static WorldLawAsset empirecraft_law_dejure_first;
    public static WorldLawAsset empirecraft_law_enforce_laws;
    public static WorldLawAsset empirecraft_law_ban_rebellion;
    public static WorldLawAsset empirecraft_law_ban_diplomacy;
    public static WorldLawAsset empirecraft_law_ban_ai_war;
    public static WorldLawAsset empirecraft_law_ban_ai_alliance;
    public static WorldLawAsset empirecraft_law_ban_ai_vassal;
    public static WorldLawAsset empirecraft_law_ban_ai_tribute;
    public static WorldLawAsset empirecraft_law_ban_ai_marriage;
    public static WorldLawAsset empirecraft_law_ban_ai_pact;
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
        // 科技依年代(默认开启)：只对剧本开局的世界起作用，各时代的科技要到相应的年代才能研究(见 TechCalendarRules)
        AssetManager.world_laws_library.add(empirecraft_law_tech_calendar = new WorldLawAsset()
        {
            id = nameof(empirecraft_law_tech_calendar),
            group_id = "EmpireCraftCommonSetting",
            icon_path = "ui/icons/iconKnowledge",
            default_state = true
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
        // 城市边界固定(默认关闭)：城的地块不再自动增减，只听手动(见 CityTerritoryProtection.BordersLocked)
        AssetManager.world_laws_library.add(empirecraft_law_fixed_city_borders = new WorldLawAsset()
        {
            id = nameof(empirecraft_law_fixed_city_borders),
            group_id = "EmpireCraftCommonSetting",
            icon_path = "ui/icons/iconCityZones",
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
        // 执行律法(默认开)：帝国按政体的律法定罪量刑(见 EmpireLawSystem)；关了不再自动查案，揭发罪行也定不了罪
        AssetManager.world_laws_library.add(empirecraft_law_enforce_laws = new WorldLawAsset()
        {
            id = nameof(empirecraft_law_enforce_laws),
            group_id = "EmpireCraftCommonSetting",
            icon_path = "ui/icons/plots/plot_kingdom_expose_crime",
            default_state = true
        });
        // 禁止叛乱(默认关)：开了以后一场叛乱都不起——苛政民变、民族独立、土崩瓦解、革命滚雪球、朝局逼反、宗教叛乱、
        // 军阀自立、拥兵自立、藩王索位全停(闸在 RebellionSystem.RebellionsBanned；两个起事入口
        // KingdomExtension.StartLocalRebelling / StartFactionRebelling 另各有一道兜底)。
        // 给剧本作者用：地图还没铺开时先冻住内乱，铺完再关掉。已经打起来的叛乱不会因此收场。
        // 2026-11-12 起开着时也不许自立(城自成新国、成员叛离帝国，见 SecessionGate)；「禁止AI外交」「禁止AI宣战」开着、
        // 原版「外交」「叛乱」关着时同样如此
        AssetManager.world_laws_library.add(empirecraft_law_ban_rebellion = new WorldLawAsset()
        {
            id = nameof(empirecraft_law_ban_rebellion),
            group_id = "EmpireCraftCommonSetting",
            icon_path = "ui/icons/iconWar",
            default_state = false
        });
        // 禁止AI外交(总开关，默认关)：开着时下面六项一律停。只拦 AI 自己发起的，上帝视角的外交面板与神力照常(见 DiplomacyLaws)
        AssetManager.world_laws_library.add(empirecraft_law_ban_diplomacy = new WorldLawAsset()
        {
            id = nameof(empirecraft_law_ban_diplomacy),
            group_id = "EmpireCraftCommonSetting",
            icon_path = "ui/icons/iconKingdom",
            default_state = false
        });
        // 禁止AI宣战(默认关)：AI 不再主动宣战(模组经 WarGate.CanDeclare，原版经 WarTargetPatch)；正在打的仗照打完。
        // 开着时也不许自立、不起事(见 SecessionGate)
        AssetManager.world_laws_library.add(empirecraft_law_ban_ai_war = new WorldLawAsset()
        {
            id = nameof(empirecraft_law_ban_ai_war),
            group_id = "EmpireCraftCommonSetting",
            icon_path = "ui/icons/iconWar",
            default_state = false
        });
        // 禁止AI结盟(默认关)：AI 不再建盟、入盟(原版 Alliance.canJoin，见 AlliancePatch)；已结的盟不解散
        AssetManager.world_laws_library.add(empirecraft_law_ban_ai_alliance = new WorldLawAsset()
        {
            id = nameof(empirecraft_law_ban_ai_alliance),
            group_id = "EmpireCraftCommonSetting",
            icon_path = "plots/icons/plot_alliance_create",
            default_state = false
        });
        // 禁止AI归附(默认关)：小国不再望风归附大国(见 SmallStateSubmissionSystem)；已有的附庸关系不动
        AssetManager.world_laws_library.add(empirecraft_law_ban_ai_vassal = new WorldLawAsset()
        {
            id = nameof(empirecraft_law_ban_ai_vassal),
            group_id = "EmpireCraftCommonSetting",
            icon_path = "ui/icons/iconKingdom",
            default_state = false
        });
        // 禁止AI朝贡(默认关)：不再有新的称臣纳贡(见 TributaryRelations.Submit)；已有的朝贡国不动
        AssetManager.world_laws_library.add(empirecraft_law_ban_ai_tribute = new WorldLawAsset()
        {
            id = nameof(empirecraft_law_ban_ai_tribute),
            group_id = "EmpireCraftCommonSetting",
            icon_path = "ui/icons/iconKingdom",
            default_state = false
        });
        // 禁止AI和亲(默认关)：各国不再议婚(见 RoyalMarriageSystem.Monthly)；已结的姻亲不动
        AssetManager.world_laws_library.add(empirecraft_law_ban_ai_marriage = new WorldLawAsset()
        {
            id = nameof(empirecraft_law_ban_ai_marriage),
            group_id = "EmpireCraftCommonSetting",
            icon_path = "ui/icons/iconClan",
            default_state = false
        });
        // 禁止AI通好(默认关)：跨文明大国不再结通好(见 GreatPowerDiplomacy)；已有的通好不动
        AssetManager.world_laws_library.add(empirecraft_law_ban_ai_pact = new WorldLawAsset()
        {
            id = nameof(empirecraft_law_ban_ai_pact),
            group_id = "EmpireCraftCommonSetting",
            icon_path = "ui/icons/iconKingdom",
            default_state = false
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

    // ───────── 世界法则跟着存档走 ─────────
    // 世界法则本身是 PlayerConfig 里的玩家选项(各回调的参数就是 PlayerOptionData)，跨存档共享：
    // 在一局里开了「禁止叛乱」，换一局、开新世界也还开着(用户 2026-11-08"这些设置应该只能跟随存档，而不是模组")。
    // 这里照模组原有的做法(switch_real_num 等三个开关，见 DataManager)把本模组的法则一并存进存档：
    //   · 存档时记下每条法则的开关；
    //   · 读档时按存档里的开关还原；存档里没有的(旧档、后来新加的法则)用各自的 default_state；
    //   · 新建世界时(DBManagerPatch.AllClear)一律回到 default_state，不把上一局的设置带过来。
    // 按反射取本类所有法则字段，以后再加法则不用改这里。
    private static List<WorldLawAsset> OwnLaws() =>
        typeof(EmpireCraftWorldLawLibrary)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(WorldLawAsset))
            .Select(field => field.GetValue(null) as WorldLawAsset)
            .Where(law => law != null && !string.IsNullOrEmpty(law.id))
            .ToList();

    // 直接写选项值，不走 toggle：toggle 会触发 on_state_change(解散同盟、重扫文化……)，读档时不该跑
    private static void Set(WorldLawAsset law, bool on)
    {
        if (law == null || string.IsNullOrEmpty(law.id) || PlayerConfig.dict == null) return;
        if (PlayerConfig.dict.TryGetValue(law.id, out PlayerOptionData option)) option.boolVal = on;
    }

    public static Dictionary<string, bool> Capture()
    {
        var result = new Dictionary<string, bool>();
        try
        {
            foreach (WorldLawAsset law in OwnLaws()) result[law.id] = law.isEnabled();
        }
        catch (global::System.Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][世界法则] 记入存档失败: {exception.Message}");
        }
        return result;
    }

    // saved 为 null(新建世界)时整份回到默认
    public static void Apply(Dictionary<string, bool> saved)
    {
        try
        {
            foreach (WorldLawAsset law in OwnLaws())
                Set(law, saved != null && saved.TryGetValue(law.id, out bool on) ? on : law.default_state);
        }
        catch (global::System.Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][世界法则] 按存档还原失败: {exception.Message}");
        }
    }

    public static void ResetToDefaults() => Apply(null);

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

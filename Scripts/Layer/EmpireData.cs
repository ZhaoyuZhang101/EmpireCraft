using EmpireCraft.Scripts;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using UnityEngine;

// Token: 0x0200023D RID: 573
public class EmpireData : MetaObjectData
{
    public string motto { get; set; }
    public int banner_background_id { get; set; }
    public long empire_specific_clan { get; set; } = -1L;
    public float TaxRate = 0.2f;
    public int banner_icon_id { get; set; }
    public int Mandate { get; set; } = 100; // 原始事件法统；综合正统由 MonarchyLegitimacy 计算
    public List<long> CabinetMembers { get; set; } = new List<long>();
    // 华夏部落联盟的诸部议事会(见 TribalCouncilSystem)：议事的人
    public List<long> tribal_council { get; set; }
    public bool ShouldSerializetribal_council() => tribal_council != null && tribal_council.Count > 0;
    // 朝堂博弈(见 CourtSystem)
    public CourtState court = new();
    // 周制的天下大势：西周、春秋、战国(见 ZhouEraSystem)
    public ZhouEraState zhou = new();
    // 西方：与教廷的关系(加冕、叙任权、绝罚、什一税，见 PapacySystem)
    public PapalRelationState papal = new();
    // 神圣罗马式选帝制：选立储君、皇族届数、选举让步(见 HolyRomanSystem)
    public HolyRomanState holy_roman = new();
    public long Religion { get; set; } = -1L;
    public EmpireHeirLawType heir_type { get; set; }
    public int max_province_city_num { get; set; } = 3;
    public List<int> PreviousYearsMoney = new();
    public double MilitaryExpenditureRate { get; set; } = 0.2;
    public int MilitaryExpenditure = 0;
    public double last_military_expenditure_timestamp = -1d;
    public double last_increase_mandate_timestamp = -1L;
    // 上次岁祭、上次记"岁祭不举"的时间(见 RitesSystem)
    public double last_sacrifice_at = -1d;
    public double last_sacrifice_lapse_at = -1d;
    // 上次国人暴动的时间(见 GuoyeSystem)
    public double last_guoren_riot_at = -1d;
    // 本朝另刻的受命宝(装备，见 ImperialSealSystem)
    public long imperial_seal_lesser_item_id = -1L;
    // 上次修氏族志、抑豪强的时间(见 GentrySystem)
    public double gentry_register_at = -1d;
    public double gentry_curbed_at = -1d;
    // 朝廷与世家的关系(-100~100)、上回世家相争的时间(见 GentrySystem.Court、GentrySystem.Events)
    public float gentry_favor;
    public double gentry_feud_at = -1d;
    // 诉求上回施行的时间(见 TemporaryFaction.CooldownYears)
    public Dictionary<string, double> claim_done_at = new();
    // 羁縻朝贡(见 JimiTributeSystem)：各羁縻政区受册封的首领(负数 = 新首领还没册封)、上回入朝、加恩安抚的时间；
    // 叛去的羁縻政区与叛去的时间
    public Dictionary<long, long> jimi_chiefs = new();
    public Dictionary<long, double> jimi_tribute_at = new();
    public Dictionary<long, double> jimi_favor_at = new();
    public Dictionary<long, double> jimi_rebels = new();
    public bool original_royal_been_changed { get; set; } = false;
    public bool feed_royal = false;
    public double original_royal_been_changed_timestamp { get; set; }
    public string founder_actor_name { get; set; }
    [DefaultValue(-1L)]
    public long founder_actor_id { get; set; } = -1L;
    [DefaultValue(-1L)]
    public long dynasty_founder_actor_id { get; set; } = -1L;
    // 绝嗣裂土时据地称帝、承接了旧朝廷的积弊(见 InterregnumSystem.InheritDecline)：开国之君没有开国气象
    public bool inherited_decline { get; set; }
    public string founder_kingdom_name { get; set; }
    public string year_name = "";
    public List<EmpireCraftHistory> history = new List<EmpireCraftHistory>();
    public EmpireCraftHistory currentHistory {  get; set; }
    public EmpirePeriod empirePeriod {  get; set; }
    public bool is_been_controlled { get; set; } = false;
    //岁币国
    public List<long> given_Kingdoms = new List<long>();
    // 岁赐报价随协定保存，人口变化不改变已签订的年度金额。
    public Dictionary<long, int> given_annual_quotes = new();
    //朝贡国
    public List<long> taken_Kingdoms = new List<long>();
    public EmpireAddition additions = new EmpireAddition();
    [DefaultValue(-1L)]
    public long founder_kingdom_id { get; set; } = -1L;

    public string directPre = "";
    // 失都改称(见 CapitalLossNamingSystem)：核心首都失陷的时间(-1 = 没失陷)、是否已加方位、加方位前原来的前缀
    public double core_capital_lost_since = -1d;
    public bool capital_loss_prefix_applied;
    public string capital_loss_previous_prefix = "";
    public string core_name = "";
    public string empire_type_key = "";
    // 天下之名(见 RealmNamingSystem)：国号来源(RealmNamingSystem.Source*)、以之为号的帝国法理、复国定下的方位前缀、
    // 与旧朝同名时加的方位前缀、改用天下之名(普世之名、唯一合法政府)前的本名
    public int name_source;
    public long named_core_id = -1L;
    public string founding_prefix = "";
    public string rival_name_prefix = "";
    public string pre_tianxia_name = "";
    // 同名相争(见 RealmNamingSystem.Contest)：所争的名、是否已让出(改用自家王国法理名)、让出后用的名、让出前的前缀、
    // 上次因正统易手换名的时间(十年内至多换一次)
    public string title_claim_name = "";
    public bool title_claim_yielded;
    public string title_claim_own_name = "";
    public string title_claim_prev_prefix = "";
    public double title_claim_switched_at = -1d;
    // 多京(见 MultiCapitalSystem)：各京(主京在前)；行省阶段归太子的第二直隶
    public List<JingRecord> jing = new List<JingRecord>();
    public long zhili_kingdom_id = -1L;
    // 幕府(见 BakufuSystem)：开幕时的天皇家(宗族)与当时的天皇；天皇为虚君，明治维新时大政奉还
    public long court_clan_id = -1L;
    public long court_emperor_id = -1L;
    // 武家执政(见 MilitaryRegency)：0 无、1 征夷大将军、2 苏丹；将军/苏丹的家族
    public int military_regency = 0;
    // 宠臣(见 FavouriteSystem)：宠臣与宠信他的皇帝
    public long favourite_id = -1L;
    public long favourite_emperor_id = -1L;
    public long regent_clan_id = -1L;
    // 跨文明通好(见 GreatPowerDiplomacy)：对方帝国编号 → 通好的时间
    public Dictionary<long, double> diplomatic_pacts = new();
    // 西方封建帝国的代数：同名封建帝国反复建立时称"第二帝国""第三帝国"(0/1 表示不加序数)
    public int western_ordinal = 0;
    // 游牧雄主(见 UnificationSystem 游牧难一统)：领着统一天下的那位雄主与他的名字(-1 没有)；
    // 雄主不在位了、还没换过汗的时间(-1 没有；有就是雄主之后第一次汗位更替，必乱)
    public long nomad_hero_id = -1L;
    public string nomad_hero_name = "";
    public double nomad_hero_fell_at = -1d;
    // 大分裂冷却(见 UpheavalGuard，用户 2026-10-21 选)：最近一次大分裂(汗位/哈里发/苏丹更替之乱、雄主身后分裂、应仁之乱、
    // 土崩瓦解、宗室另立)真走掉成员的时间；-1 没有(旧档读进来也是 -1)
    public double last_upheaval_at = -1d;
    // 异族之地(见 ForeignLandSystem)：未同化的异族人口占治下人口几成(隔海的加权，年度快照，扣正统用)；
    // 正统跌破五十时出兵镇压三代未附之地的时间(压住一年)
    public float foreign_land_share;
    public double foreign_suppressed_at = -1d;
    // 土崩瓦解(见 ProvincialCollapseSystem.TryFall)：这一回起义各省(首义 + 响应)离开时带走的城数，收束时只按它算帝国还剩多少；
    // -1 为没记过(旧档瓦解进行到一半读进来，收束时按起义各省眼下的城数估)，首义时清零
    public int collapse_risen_cities = -1;
    // 分裂进程(见 SchismSystem，用户 2026-10-21 选"预警前兆、先割据后独立、争位内战代替集体出走、连锁观望")：
    // 类别(0 无进程；旧档读进来也是 0，旧机制下已出走的成员不追认)、阶段、阶段起始时间、本阶段时长(月)、进程开始时间；
    // 化解门槛(正统、积弊均值，开进程时由各路径给定)；看"新君在位满三年"的那位(皇帝或将军、苏丹)与他起算的时间(-1 不算)；
    // 不稳成员(预警时离心、割据后割据、土崩瓦解首义后观望)；争位者的国、人名、国名与争位之战
    public int schism_kind = 0;
    public int schism_stage = 0;
    public double schism_stage_at = -1d;
    public int schism_stage_months = 0;
    public double schism_started_at = -1d;
    public int schism_gate_legitimacy = 0;
    public float schism_gate_malady = 0f;
    public long schism_ruler_id = -1L;
    public double schism_ruler_at = -1d;
    public List<long> schism_members = new();
    public long schism_claimant_id = -1L;
    public string schism_claimant_name = "";
    public string schism_claimant_realm = "";
    public long schism_war_id = -1L;
    // 争位之战开打时双方的都城(决胜：攻下对方都城即定局，见 SchismSystem.ContestMonthly)；-1 为没记(旧档)
    public long schism_core_capital_id = -1L;
    public long schism_claimant_capital_id = -1L;
    // 中央(核心是主攻方或主守方)上次打赢一仗的时间(化解用，不论有无进程都记，见 SchismSystem.OnWarEnded)；-1 没有
    public double schism_last_victory_at = -1d;
    // 土崩瓦解首义之后的声势(见 SchismSystem.Momentum)：起义各省(首义 + 响应)、各自起事时的城数与都城；
    // 叛省比起事时多打下的城(上次看到的数)与多打下城的时间
    public List<long> schism_risen = new();
    public List<int> schism_risen_cities = new();
    public List<long> schism_risen_capitals = new();
    public int schism_rebel_gain = 0;
    public double schism_rebel_gain_at = -1d;
    public long emperor { get; set; } = -1L;
    public long empire_clan { get; set; } = -1L;
    public long empire_core_id { get; set; } = -1L;

    public List<long> kingdoms;
    public List<long> cities;
    public List<string> history_emperrors = new List<string>();

    public bool is_allow_normal_to_exam = true; 
    public bool has_year_name = false;

    public long empire;
    public long last_core_kingdom_id { get; set; } = -1L;
    public long original_capital;

    public double timestamp_member_joined;
    public double timestamp_established_time;
    public double timestamp_invite_war_cool_down;
    //岁币时间戳
    public double timestamp_given_time;
    public CenterOffice centerOffice { get; set; }
    public double newEmperor_timestamp { get; set; }
    // newEmperor_timestamp 是哪位君主即位登记时记下的(见 Empire.NewEmperor、RepairUnregisteredEmperor)；-1 为旧档
    public long reign_emperor_id { get; set; } = -1L;
    // 旧档里一部自立的部落帝国(以前的 TribalFissionService.FormTribes，10-26 去掉)：读档后、每月撤去
    // (见 TribalFissionService.DissolveSettledTribes)；有了共事的部落改为 false。只留作旧档迁移，新立的帝国不再记
    public bool formed_as_tribe { get; set; }
    // 部落联盟的战功(部落之战每胜一场、每收一部记一分，见 TribalWarSystem.Concede)：换天命，进宗法分封时按资历定王室用
    // (见 TribalFissionService.SettleRoyalHouse)
    public int tribal_war_merit { get; set; }
    // 禅让旧档清理做过了(见 ShanrangSystem.DropInterimReigns)
    public bool shanrang_cleaned { get; set; }
    public double center_loss_timestamp { get; set; } = -1L;
    public bool is_center_lost { get; set; } = false;

    public double last_exam_timestamp { get; set; } = -1L;
    public double last_select_lovers_timestamp { get; set; } = -1L;

    public double last_office_exam_timestamp { get; set; } = -1L;
    
    public double last_educate_timestamp { get; set; } = -1L;
    public double last_honorary_peerage_timestamp { get; set; } = -1L;
    public Dictionary<string, long> honorary_peerage_holders { get; set; } = new Dictionary<string, long>();
    public Dictionary<string, long> honorary_peerage_holder_identities { get; set; } = new Dictionary<string, long>();
    public Dictionary<string, string> honorary_peerage_holder_names { get; set; } = new Dictionary<string, string>();
    public List<DefeatedEmpireHouse> defeated_empire_houses { get; set; } = new List<DefeatedEmpireHouse>();
    // 西方帝国历代皇帝的即位记录(王号序数用)：同一帝国、同一氏族、同名才累计
    public List<RegnalRecord> regnal_records { get; set; } = new List<RegnalRecord>();
    public double last_legal_peerage_timestamp { get; set; } = -1L;
    public Dictionary<long, long> legal_peerage_holders { get; set; } = new Dictionary<long, long>();
    public Dictionary<long, long> legal_peerage_holder_identities { get; set; } = new Dictionary<long, long>();
    public Dictionary<long, string> legal_peerage_types { get; set; } = new Dictionary<long, string>();
    public Dictionary<long, long> legal_peerage_kingdoms { get; set; } = new Dictionary<long, long>();
    [DefaultValue(-1L)]
    public long powerful_minister_id { get; set; } = -1L;
    public int powerful_minister_progress { get; set; } = 0;
    public int powerful_minister_stage { get; set; } = 0;
    public bool powerful_minister_is_regent { get; set; } = false;
    public bool powerful_minister_is_empress_dowager { get; set; } = false;
    public bool powerful_minister_is_former_empress { get; set; } = false;
    [DefaultValue(-1L)]
    public long powerful_minister_title_id { get; set; } = -1L;
    public double last_powerful_minister_timestamp { get; set; } = -1L;
    public double powerful_minister_stage_timestamp { get; set; } = -1L;
    public double powerful_minister_regency_end_timestamp { get; set; } = -1L;
    public long powerful_minister_emperor_id { get; set; } = -1L;
    public long minister_usurper_id { get; set; } = -1L;
    // 当今权臣是外戚(见 ConsortKinSystem)
    public bool powerful_minister_is_consort_kin { get; set; } = false;
    // 已施行"增广尊谥"：此后有庙号的先帝都得长谥(见 PosthumousNameGenerator)
    public bool long_posthumous { get; set; }
    // 谥法已由皇帝定过(增广尊谥或谥法从简)：一个帝国只定一次，改朝换代也不重来
    public bool posthumous_decided { get; set; }
    // 帝国设置"开启长谥号"：打开后本朝有庙号的先帝都显示长谥，关着(默认)显示短谥"国号某帝"
    public bool show_long_posthumous { get; set; }
    // 帝国设置的国号前缀：false 为自动(迁都、复国、分裂时系统加的方位词)，true 为手选——prefix_choice 是方位词键
    // (Eastern/Western/Southern/Northern/Former/Later/great)、自己写的字，空为不加
    public bool prefix_manual { get; set; }
    // 自定义地图色(#RRGGBB，空为原版颜色，见 CustomColorSystem)
    public string custom_color { get; set; } = "";
    public string prefix_choice { get; set; } = "";
    // 全帝国连荒两年以上的城占几成(见 FamineUnrestSystem，推高王朝积弊)
    public float famine_share { get; set; }
    // 帝国分裂(见 EmpirePartitionSystem)：另一朝的帝国 id、分裂方式(will 遗诏分治 / minister 权臣分立 /
    // restoration 宗室另立)、时间；amity 兄弟之国(互不征伐)；claimant 为分出来的一朝(日后互争正统时是争正统者)
    public long partition_twin_id { get; set; } = -1L;
    public string partition_kind { get; set; } = "";
    public double partition_time { get; set; } = -1d;
    public bool partition_amity { get; set; }
    public bool partition_claimant { get; set; }
    // 皇帝出行(纯净地图的皇帝标记，见 EmperorTourSystem)：出行方式(""=在宫里，tour/visit/campaign)、所在地块、
    // 目的城、随驾的军、出发与到达的时间、是否回銮、出行的皇帝、上次回宫的时间
    public string tour_kind { get; set; } = "";
    public int tour_zone { get; set; } = -1;
    public long tour_city { get; set; } = -1L;
    public long tour_corps { get; set; } = -1L;
    public double tour_since { get; set; } = -1d;
    public double tour_arrived { get; set; } = -1d;
    public bool tour_returning { get; set; }
    public long tour_emperor { get; set; } = -1L;
    public double tour_last { get; set; } = -1d;
    // 朝政积弊：六条弊病各 0~100、上次结算(见 MaladySystem)
    public Dictionary<string, float> maladies { get; set; } = new();
    public double last_malady_update { get; set; } = -1d;
    // 派系失衡(见 FactionBalanceSystem)：失衡度 0~100、独大的派系、是否已记过"一家独大"、上次结算
    public float faction_imbalance { get; set; }
    public string faction_imbalance_id { get; set; } = "";
    public bool faction_imbalance_warned { get; set; }
    public double last_faction_balance_update { get; set; } = -1d;
    // 税基流失的账目(见 TaxErosionSystem)：本年应征、隐田逃户、胥吏舞弊、城中贪墨、地方截留，满一年结转为上一年
    public long fiscal_assessed { get; set; }
    public long fiscal_hidden { get; set; }
    public long fiscal_fraud { get; set; }
    public long fiscal_stolen { get; set; }
    public long fiscal_withheld { get; set; }
    public long fiscal_last_assessed { get; set; }
    public long fiscal_last_hidden { get; set; }
    public long fiscal_last_fraud { get; set; }
    public long fiscal_last_stolen { get; set; }
    public long fiscal_last_withheld { get; set; }
    public double fiscal_year_start { get; set; } = -1d;

    public bool archived { get; set; } = false;
    
    public int cached_population { get; set; } = 0;
    public int cached_warriors { get; set; } = 0;
    public int cached_warriors_max { get; set; } = 0;
    public double last_cached_timestamp { get; set; } = -1L;
    public int annual_power_population { get; set; } = 0;
    public int annual_power_military { get; set; } = 0;
    public int annual_power_economy { get; set; } = 0;
    public double annual_power_index { get; set; } = 0d;
    public double last_national_power_timestamp { get; set; } = -1L;
    [DefaultValue(-1L)]
    public long legitimacy_rival_empire_id { get; set; } = -1L;
    public bool legitimacy_rivalry_recognized { get; set; } = false;
    public bool legitimacy_challenger { get; set; } = false;
    public bool auto_enfeoff_tracking_initialized { get; set; } = false;
    public bool territorial_enfeoff_pending { get; set; }
    public bool succession_enfeoff_pending { get; set; }
    public List<long> pending_conquest_enfeoff_kingdom_ids { get; set; } = new();
    public bool administrative_reorganization_pending { get; set; }
    public double last_administrative_reorganization_timestamp { get; set; } = -1d;
    public List<long> auto_enfeoff_observed_title_ids { get; set; } = new List<long>();
    public List<long> auto_enfeoff_observed_city_ids { get; set; } = new List<long>();
    public double last_auto_enfeoff_check_timestamp { get; set; } = -1L;
    public string conquest_founder_culture { get; set; } = "";
    public string ruling_culture { get; set; } = "";
    public string institutional_culture { get; set; } = "";
    public string external_identity_culture { get; set; } = "";
    public string military_tradition { get; set; } = "";
    public CompositeEmpireIntegrationStage composite_integration_stage { get; set; } = CompositeEmpireIntegrationStage.None;
    public int composite_integration { get; set; } = 0;
    public int central_plains_legitimacy { get; set; } = 0;
    public int ruling_tradition_legitimacy { get; set; } = 0;
    public double composite_identity_adopted_timestamp { get; set; } = -1L;
    public double last_composite_identity_update_timestamp { get; set; } = -1L;
    public bool composite_cultural_name_adopted { get; set; } = false;
    public string composite_cultural_name { get; set; } = "";
    public string composite_cultural_name_culture { get; set; } = "";
    public string composite_cultural_name_source { get; set; } = "";
    public double composite_cultural_name_adopted_timestamp { get; set; } = -1L;
    // 征服王朝的治理路线(见 CompositeEmpireService.GetRoute)：0 未定、1 分治、2 兼用、3 从俗；定下或改换的时间
    public int composite_route { get; set; } = 0;
    public double composite_route_since { get; set; } = -1d;
    // 本俗反扑(国语骑射，见 CompositeEmpireService.ProclaimNativism)：重申的时间，二十年内不从俗
    public double nativist_revival_at { get; set; } = -1d;
    public InstitutionEmpireState institution_state { get; set; } = new InstitutionEmpireState();
    // 帝国对本文化科技的采纳(帝国成员共用，见 TechnologySystem.RealmHasTech)
    public EmpireCraft.Scripts.Data.RealmTechState realm_tech { get; set; }
    public ConstitutionalEconomyState constitutional_economy { get; set; } = new ConstitutionalEconomyState();
}

public enum RoyalFeedLevel
{
    None,
    Low,
    Middle,
    High,
    Extreme
}
public class EmpireAddition
{
    public Dictionary<OfficerPowerType, int> addition =  OfficeManager.AllPower.ToDictionary(t => t, t => 0);
    //内阁加成
    public float cabinet_acc = 0;

    public void Add(EmpireAddition other)
    {
        foreach (var kv in other.addition)
            this.addition[kv.Key] += kv.Value;

        this.cabinet_acc += other.cabinet_acc;
    }

    public string toString()
    {
        var content = "";
        foreach (var kv in addition)
        {
            content += $"{kv.Key}: {kv.Value.ToString().ColorString(pColor:kv.Value>=0? Color.green : Color.red)}\n";
        }
        return content;
    }
}
public class EmpireCraftHistory
{
    public long id { get; set; }
    public string empire_name { get; set; }
    public string dynasty_name { get; set; }
    public string empire_full_name { get; set; }
    public string royal_surname { get; set; }
    public bool is_first { get; set; } = false;
    // 明式长谥(本朝施行"增广尊谥"后，见 PosthumousNameGenerator.LongName)：尊谥各字的本地化键，最后一个是本谥
    public List<string> shihao_long { get; set; }
    public string year_name { get; set; }
    public string emperor { get; set; }
    public string miaohao_name { get; set; }
    public string miaohao_suffix { get; set; }
    public string shihao_name { get; set; }
    public int total_time { get; set; }
    // 共和国元首的任期(与君主的在位记录分开显示，不按姓氏分朝代)；office_title 为当时的元首称号(总统、主席……)
    public bool is_republic { get; set; }
    public string office_title { get; set; } = "";

    // 在位生平统计(谥法依据，见 ReignRecordSystem / PosthumousNameGenerator)；-1 表示旧档没有记录
    public int start_cities { get; set; } = -1;
    public int max_cities { get; set; } = -1;
    public int end_cities { get; set; } = -1;
    public int start_mandate { get; set; } = -1;
    public int end_mandate { get; set; } = -1;
    public int wars_won { get; set; }
    public int wars_lost { get; set; }
    public int rebellions { get; set; }
    public int rebellions_lost { get; set; }
    public int age_at_end { get; set; } = -1;
    public bool ended_alive { get; set; }
    public bool ended_dynasty { get; set; }
    public bool restorer { get; set; }
    // 君主资质(RulerQualitySystem.Grade 的名字)与在位期间是否被权臣、外戚挟制，谥号用
    public string ruler_grade { get; set; } = "";
    public bool was_controlled { get; set; }
    // 怎么继的位：废立(deposed)、兵变夺位(coup)、诸王争位(strife)、宫廷政变(palace_coup)；空为照常继位(世系图标出，见 DynastyLineageWindow)
    public string succession_note { get; set; } = "";

    public List<string> initial_cities = new List<string>();
    public List<HistoryDescription> descriptions =  new List<HistoryDescription>();
}

public class RegnalRecord
{
    public long actor_id = -1L;
    public long clan_id = -1L;
    public string given_name = "";
}

public class HistoryDescription
{
    public string time;
    public List<string> cities;
    public string description;
    public double timestamp = -1L;
    public long actor_id = -1L;
    public long kingdom_id = -1L;
}

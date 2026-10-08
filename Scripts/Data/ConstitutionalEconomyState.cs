using System.Collections.Generic;

namespace EmpireCraft.Scripts.Data;

public sealed class CompletedTradeVoyage
{
    public double timestamp;
    public long origin_city_id;
    public long destination_city_id;
    public bool foreign_kingdom;
    public int delivered_gold;
}

// 议会的一个议席：属于哪个派系、由谁出任。actor_id < 0 表示空缺（该派系没有合适人选）。
public sealed class ParliamentSeat
{
    public string faction_id = "";
    public long actor_id = -1L;
    // 普选后的选区(行政区所在国 id)；-1 表示全国统一分配
    public long district_kingdom_id = -1L;
}

// 一个行政区的地方政治(见 ProvincialPoliticsSystem)
public sealed class ProvincePolitics
{
    // 地方选举选出的执政党(省长所属政党)；空 = 还没办过地方选举
    public string governing_party_id = "";
    // 各党在本省的支持率(0~1)：地方选举时记一次，玩家资助/打压后即时重算
    public Dictionary<string, float> vote_shares = new();
    public double last_election = -1d;
    // 玩家资助(+)/打压(-)对各党本省得票的影响(-0.5~0.5)，逐年回落
    public Dictionary<string, float> influence = new();
}

// 执政党的跨届施政议程：修改一条宪法条款，需要在议会里攒够票数(见 ParliamentSystem.UpdateAgenda)
public sealed class GovernmentAgenda
{
    public string clause = "";
    public string target = "";
    // 推动议程的政党；换届后新政府主张一致就接手，否则废止
    public string party_id = "";
    public float progress;
    public double started_at = -1d;
    // 上一次结算时的支持议席占比与修宪门槛(界面显示用)
    public float support;
    public float threshold;
    public bool stalled;
    // 开始卡在二读(票数不够)的时间；-1 表示没有卡住
    public double stalled_since = -1d;
}

// 一个外国对本国施加的理念压力(民意系统每年重算，只用于显示和转化)
public sealed class IdeologyPressureSource
{
    public long empire_id = -1L;
    public string empire_name = "";
    public EmpireCraft.Scripts.GeneralSystems.PartyIdeology ideology;
    public float amount;
}

public sealed class ConstitutionalEconomyState
{
    // —— 民意与理念压力(见 PublicOpinionSystem) ——
    // 0 满意 / 1 异见 / 2 抵制 / 3 革命浪潮
    public int opinion_level;
    public int opinion_wave_years;
    // 革命浪潮中已经为顺应民意举行过几次提前大选；选举仍未让民意所向的政党上台时转为现代革命
    public int opinion_wave_elections;
    // 去年是否处于全国总罢工(罢工城市 ≥30%)；用于只在总罢工开始时记一次史书
    public bool general_strike;
    // 今年 WarBox 示威/罢工城市占全国城市的比例(年度结算时统计，民意计算读取)
    public float strike_share;
    public float demonstration_share;
    // 意识形态演变(见 IdeologyDynamicsSystem)：
    // 疲劳度 0~100(宣传越久越空洞)；经济趋势(近十年繁荣度变化，金融危机记为衰退)；
    // 上次检查时的元首(换人即路线之争)、现行路线；每年的全国繁荣度(最多 11 年)；看到的宪法序号(换宪法即重置疲劳)
    public float ideology_fatigue;
    public float economic_trend;
    public bool economic_crisis;
    public long ideology_line_head_id = -1L;
    public string ideology_line = "";
    public List<float> prosperity_history = new();
    public int ideology_constitution_number;
    // 言论压力 -100~100：正 = 要求放开言论，负 = 要求收紧；越过 ±50 言论自由改一档后归零(见 IdeologyDynamicsSystem)
    public float speech_pressure;
    // 思想解放期开始时间(-1 = 没有)；被压抑的思想 0~100(高压下不信立国理念、又不敢说的人积累，遇事爆发)；
    // 上一年的言论自由(改为宽松即开启解放期)
    public double liberation_started = -1d;
    public bool liberation_announced_end = true;
    public float suppressed_thought;
    public string last_speech = "";
    // 复辟帝制(见 RestorationSystem)：0 = 无，1 = 筹备(筹安会、劝进)，2 = 已称帝；
    // 阶段开始时间、发起复辟的元首、现任元首上台时间、上次复辟失败的时间(冷却用)
    public int restoration_stage;
    public double restoration_started = -1d;
    public long restoration_head_id = -1L;
    public long tenure_head_id = -1L;
    public double tenure_since = -1d;
    public double restoration_failed_at = -1d;
    public float opinion_support;
    public float opinion_dissent;
    public string opinion_preferred = "";
    public List<IdeologyPressureSource> ideology_pressure = new();
    public string stable_culture = "";
    public double stable_culture_since = -1d;
    public double last_economy_update = -1d;
    public double last_ai_constitution_attempt = -1d;
    public double capitalist_candidate_since = -1d;
    public double capitalist_decline_since = -1d;
    public bool capitalist_budding;
    // 上帝模式强制催生的萌芽：条件不足时也不会消退
    public bool capitalist_budding_forced;
    public List<CompletedTradeVoyage> recent_trade = new();
    // —— 政党(开放党禁后，见 PartySystem) ——
    public bool parties_reorganized;
    public double last_party_update = -1d;
    // Annual social spending requires a researched party program and a funded treasury.
    public int welfare_level;
    public bool welfare_funded;
    // —— 共和(见 RepublicSystem) ——
    public bool is_republic;
    public EmpireCraft.Scripts.GeneralSystems.PartyIdeology republic_ideology;
    public double republic_since = -1d;
    // 0 = legacy/unset, 1 = common era, 2 = republic era.
    public int republic_calendar_mode;
    // 0 = stable, 1 = provisional government, 2 = constitution drafting.
    public int republic_transition_stage;
    public double republic_transition_started = -1d;
    public bool republic_first_election_pending;
    // 一党制时的执政党 id；空表示多党制
    public string one_party_id = "";
    // —— 党禁与选举制度(见 PartyBanSystem) ——
    // 统一战线：党禁关闭时仍合法存在、参加选举的友党(执政党除外)
    public List<string> allied_party_ids = new();
    // true = 民主集中制(代表大会逐级推选)，false = 普选
    public bool democratic_centralism;
    // 党禁关闭期间积累的重开压力(0~100)，满了重新开放党禁
    public float party_ban_reopen_pressure;
    public double party_ban_since = -1d;
    public double last_party_ban_check = -1d;
    // 独裁度计算用：当前执政党连续执政的起点
    public string ruling_streak_party_id = "";
    public double ruling_streak_since = -1d;
    // 已按哪个帝国核心改用核心名作国号(-1 = 未改)
    public long named_after_core_id = -1L;
    // 废君压力(见 RepublicSystem.UpdateAbolitionPressure)：已能废除君主制却迟迟不废、非保守派占优时逐年加剧
    public float abolition_pressure;
    public double abolition_pressure_since = -1d;
    // 国库亏空(帝国核心国库 <0)开始的时间(-1 = 没有亏空)，见 EmpireBankruptcySystem
    public double bankrupt_since = -1d;
    // 革命建立的政府(地方互保同盟打赢后建国等)：军阀时期里即使不是中央也保留正常国家地位
    public bool revolutionary_government;
    // —— 宪法(见 ConstitutionSystem)——现代国家的现行宪法/临时约法；非立宪君主制为 null
    public ConstitutionData constitution;
    // 本国颁布过几部正式宪法(临时约法不计)，新宪法按此编号
    public int constitution_count;
    public long deposed_royal_clan_id = -1L;
    public int previous_regime = -1;
    // 当前中央机构是按哪个理念建的(空 = 政体默认机构)；与执政理念不符时重建
    public string bureau_ideology = "";
    public bool constitutional_reform_active;
    public double constitutional_reform_started = -1d;
    public float constitutional_reform_progress;
    public int constitutional_reform_stage;
    public bool constitutional_monarchy;
    public double last_deadlock_notice = -1d;

    // —— 议会（制宪进入议会阶段后出现，取代原有内阁）——
    public List<ParliamentSeat> parliament_seats = new();
    // 第几届议会；每次全面改选 +1
    public int parliament_term;
    public double last_parliament_election = -1d;
    // 上一次实际举行大选的时间(last_parliament_election 会被置 -1 表示"请求改选"，这里不会)，用于提前大选的冷却
    public double last_general_election_held = -1d;
    // 共和国：每人以执政党领袖身份赢得大选、出任总理的届数(宪法"最多 N 届"条款据此限任)
    public Dictionary<long, int> head_terms = new();
    public double last_parliament_by_election = -1d;
    // 议会选出的总理大臣
    public long prime_minister_id = -1L;
    public string prime_minister_faction_id = "";
    // majority 单一派系过半 / coalition 宪政联盟过半 / minority 少数派政府
    public string government_type = "";
    // 执政一方（单一派系或联盟）掌握的议席数
    public int government_seats;
    // 执政联盟的全部成员(含总理所属派系)；多数/少数派政府只有一个
    public List<string> coalition_faction_ids = new();
    // 本届与上届各派系议席、得票占比(议席图的涨跌箭头)
    public Dictionary<string, int> previous_seat_counts = new();
    public Dictionary<string, float> vote_shares = new();
    public Dictionary<string, float> previous_vote_shares = new();
    // 本届政府组成的时间、上次不信任投票的时间、上次年度政局结算的时间
    public double government_formed_at = -1d;
    public double last_no_confidence = -1d;
    public double last_government_review = -1d;
    // 政党分肥制下换了执政党的时间：之后两年官员大换血，宪政合法性下降(见 ModernLegitimacy)
    public double spoils_turnover_at = -1d;
    public GovernmentAgenda government_agenda;
    // —— 地方政治(见 ProvincialPoliticsSystem)——键为行政区(帝国内的国家)id
    public Dictionary<long, ProvincePolitics> provinces = new();
    // 地方选举在哪一届议会的中期办过(每届一次)
    public int last_local_election_term = -1;
    public double last_local_review = -1d;
    // 连续几次地方选举在野党控制了六成以上的行政区(满两次推动改行联邦制)
    public int local_opposition_streak;
    // —— 苛政与民怨(见 HarshRuleSystem)——
    // 王朝积弊：前现代按年累积/整顿，现代与过渡政体不适用。保存年标记防止读档重复结算。
    public double dynastic_since = -1d;
    public double last_dynastic_update = -1d;
    public double last_dynastic_uprising_attempt = -1d;
    public float dynastic_strain;
    public bool dynastic_crisis_announced;
    // 封国/城市腐败按户加权的年度快照；中央腐败仍实时读取原值。-1 兼容旧档。
    public double local_corruption_snapshot = -1d;
    public double last_corruption_snapshot = -1d;
    public double last_anticorruption_campaign = -1d;
    public double last_anticorruption_check = -1d;
    public int harsh_corruption_burden = -1;
    public float harsh_other_burden = -1f;
    public float harsh_burden;
    public string harsh_main_cause = "";
    // 现代国家的街头抗争：0 平静 / 1 游行示威 / 2 冲突 / 3 大规模暴乱；暴乱持续年数、连续镇压年数
    public int street_unrest_stage;
    public int street_unrest_years;
    public int repression_years;
    public double last_repression = -1d;
    // 上一次回应街头抗争的时间与方式(repress / concede)、是否玩家亲自拍板
    public double last_street_response = -1d;
    public string last_street_response_kind = "";
    public bool last_street_response_by_player;
}

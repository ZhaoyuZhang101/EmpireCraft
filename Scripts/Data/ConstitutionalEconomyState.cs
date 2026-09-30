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
    public double last_parliament_by_election = -1d;
    // 议会选出的总理大臣
    public long prime_minister_id = -1L;
    public string prime_minister_faction_id = "";
    // majority 单一派系过半 / coalition 宪政联盟过半 / minority 少数派政府
    public string government_type = "";
    // 执政一方（单一派系或联盟）掌握的议席数
    public int government_seats;
}

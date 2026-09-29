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

public sealed class ConstitutionalEconomyState
{
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

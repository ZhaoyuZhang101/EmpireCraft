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
    public List<CompletedTradeVoyage> recent_trade = new();
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

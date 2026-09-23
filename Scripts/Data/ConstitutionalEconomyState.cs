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
}

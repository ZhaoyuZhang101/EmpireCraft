using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 党际与街头(用户 2026-11-10 "政党总感觉是空壳"，问答选「党际与街头」)：
//   · 好感 = 理念远近(相距 0 为 +50，每远 10 少 6) + 记忆(FixedFaction.PartyMemory，每年回落一成)；
//     同在执政联盟每年 +3，一边执政一边在野每年 -1；议案上对方投反对 -3(被反对的一方)/-1，在野却投赞成 +2/+1；
//   · 组阁：好感低于 RefuseCoalition 的不肯入阁(ParliamentSystem.TryCoalition)；表决时在野党好感 ≥40 的弃权改赞成、
//     ≤-40 的改反对；倒阁表决时好感为负的友党更易倒戈；
//   · 街头(每年)：民意至少"有异见"且执政表现低于 45 时，民意所向(没有就与政府理念相距最远)的在野大党组织示威，
//     给街头抗争加压(HarshRuleSystem.UpdateStreetUnrest)；社会主义各党得工人支持过四成、又不在执政的发动罢工(执政表现 -4)；
//     执政表现低于 35 时最大在野党在议会质询(执政表现 -3、它的势力 +1)。
public static class PartyRelations
{
    public const float RefuseCoalition = -30f;
    private const float MemoryCap = 60f, MemoryDecay = 0.1f;
    private const float RallyPressure = 12f;

    public static float Relation(FixedFaction a, FixedFaction b)
    {
        if (a == null || b == null) return 0f;
        if (a == b) return 100f;
        float baseline = 50f - PartySystem.IdeologyDistance(a.Ideology, b.Ideology) * 0.6f;
        float memory = a.PartyMemory != null && a.PartyMemory.TryGetValue(b.GetID(), out float value) ? value : 0f;
        return Mathf.Clamp(baseline + memory, -100f, 100f);
    }

    private static void Remember(FixedFaction a, FixedFaction b, float delta)
    {
        if (a == null || b == null || a == b) return;
        a.PartyMemory ??= new Dictionary<string, float>();
        string id = b.GetID();
        a.PartyMemory[id] = Mathf.Clamp((a.PartyMemory.TryGetValue(id, out float value) ? value : 0f) + delta, -MemoryCap, MemoryCap);
    }

    // 不在同一执政联盟的两党：好感高的弃权改赞成，好感低的弃权改反对
    public static int AdjustVote(Empire empire, FixedFaction party, FixedFaction sponsor, int vote, float stance)
    {
        if (vote != 0 || party == null || sponsor == null || party == sponsor || !party.IsParty || !sponsor.IsParty) return vote;
        if (ParliamentSystem.IsGoverningFaction(empire, party.GetID()) && ParliamentSystem.IsGoverningFaction(empire, sponsor.GetID()))
            return vote;
        float relation = Relation(party, sponsor);
        return relation >= 40f ? 1 : relation <= -40f ? -1 : 0;
    }

    public static void AfterBill(Empire empire, FixedFaction sponsor, ParliamentBillSystem.Tally tally)
    {
        if (sponsor == null || tally == null) return;
        foreach ((FixedFaction party, int _, int vote, float _) in tally.Parties)
        {
            if (party == sponsor) continue;
            if (vote < 0)
            {
                Remember(sponsor, party, -3f);
                Remember(party, sponsor, -1f);
            }
            else if (vote > 0 && !ParliamentSystem.IsGoverningFaction(empire, party.GetID()))
            {
                Remember(sponsor, party, 2f);
                Remember(party, sponsor, 1f);
            }
        }
    }

    public static bool RefusesCoalition(FixedFaction formateur, FixedFaction partner) =>
        formateur != null && partner != null && formateur.IsParty && partner.IsParty && Relation(partner, formateur) < RefuseCoalition;

    // 倒阁表决：联盟里对首相党好感为负的友党多一成倒戈
    public static float NoConfidenceBias(FixedFaction partner, FixedFaction leader) =>
        partner?.IsParty == true && leader?.IsParty == true && Relation(partner, leader) < 0f ? 0.1f : 0f;

    #region 每年

    public static void Yearly(Empire empire)
    {
        ConstitutionalEconomyState state = empire?.data?.constitutional_economy;
        if (state == null || !PartySystem.IsActive(empire)) return;
        List<FixedFaction> parties = PartySystem.GetParties(empire).Where(party => !party.Ban).ToList();
        foreach (FixedFaction party in parties)
        {
            if (party.PartyMemory == null) continue;
            foreach (string id in party.PartyMemory.Keys.ToList())
            {
                party.PartyMemory[id] *= 1f - MemoryDecay;
                if (Mathf.Abs(party.PartyMemory[id]) < 0.5f || parties.All(other => other.GetID() != id)) party.PartyMemory.Remove(id);
            }
        }
        if (ParliamentSystem.HasParliament(empire))
        {
            foreach (FixedFaction a in parties)
            foreach (FixedFaction b in parties)
            {
                if (a == b) continue;
                bool aGov = ParliamentSystem.IsGoverningFaction(empire, a.GetID());
                bool bGov = ParliamentSystem.IsGoverningFaction(empire, b.GetID());
                if (aGov && bGov) Remember(a, b, 3f);
                else if (aGov != bGov) Remember(a, b, -1f);
            }
        }
        Street(empire, state, parties);
    }

    // 街头：在野党示威、罢工、质询
    private static void Street(Empire empire, ConstitutionalEconomyState state, List<FixedFaction> parties)
    {
        state.party_street_pressure = 0f;
        state.last_party_street = World.world?.getCurWorldTime() ?? 0d;
        if (!ParliamentSystem.HasParliament(empire) || RepublicSystem.IsOneParty(empire)) return;
        List<FixedFaction> opposition = parties.Where(party => !ParliamentSystem.IsGoverningFaction(empire, party.GetID())).ToList();
        if (opposition.Count == 0) return;
        float approval = PartyFinanceSystem.Approval(empire);
        int opinion = PublicOpinionSystem.GetLevel(empire);
        FixedFaction governing = PartySystem.GetGovernmentParty(empire);
        string country = empire.GetEmpireFullName();

        if (opinion >= PublicOpinionSystem.Dissidents && approval < 45f)
        {
            bool hasPreferred = PublicOpinionSystem.TryGetPreferred(empire, out PartyIdeology preferred);
            FixedFaction organiser = opposition.Where(party => hasPreferred && party.Ideology == preferred)
                .OrderByDescending(party => party.CentralRatio).FirstOrDefault() ??
                opposition.OrderByDescending(party => governing == null ? 0f : PartySystem.IdeologyDistance(party.Ideology, governing.Ideology))
                    .ThenByDescending(party => party.CentralRatio).FirstOrDefault();
            if (organiser != null && organiser.CentralRatio >= 10)
            {
                state.party_street_pressure += RallyPressure;
                EventRecorder.RecordRoutine(empire, string.Format(LM.Get("party_rally_history"), country, organiser.Name), organiser.GetLeader());
            }
        }
        foreach (FixedFaction party in opposition.Where(party => IdeologyFamilies.IsSocialist(party.Ideology)))
        {
            float labour = party.ClassSupport != null && party.ClassSupport.TryGetValue(SocialClass.Labour, out float value) ? value : 0f;
            if (labour < 40f || opinion < PublicOpinionSystem.Dissidents) continue;
            PartyFinanceSystem.Adjust(empire, "approval_strike", -4f);
            state.party_street_pressure += RallyPressure * 0.5f;
            EventRecorder.RecordRoutine(empire, string.Format(LM.Get("party_strike_history"), country, party.Name), party.GetLeader());
            break;
        }
        if (approval < 35f)
        {
            FixedFaction questioner = opposition.OrderByDescending(party => party.CentralRatio).First();
            PartyFinanceSystem.Adjust(empire, "approval_interpellation", -3f);
            empire.CoreKingdom?.TryIncreaseFactionRatio(questioner, 1);
            EventRecorder.RecordRoutine(empire, string.Format(LM.Get("party_interpellation_history"), country, questioner.Name,
                Mathf.RoundToInt(approval)), questioner.GetLeader());
        }
    }

    // 街头抗争的加压(HarshRuleSystem.UpdateStreetUnrest)：去年在野党组织的示威、罢工，两年后作废
    public static float StreetPressure(Empire empire)
    {
        ConstitutionalEconomyState state = empire?.data?.constitutional_economy;
        if (state == null || state.last_party_street < 0d || Date.getYearsSince(state.last_party_street) >= 2) return 0f;
        return state.party_street_pressure;
    }

    #endregion
}

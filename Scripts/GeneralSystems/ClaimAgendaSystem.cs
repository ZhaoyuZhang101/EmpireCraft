using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

public sealed class ClaimAgendaContext
{
    public Empire Empire;
    public Dictionary<SocialClass, float> ClassShares;
    public IReadOnlyDictionary<SocialClass, float> Grievances;
    public Dictionary<PartyIdeology, int> IdeologyCounts;
    public int Population;
    public int ParliamentSeats;
}

public sealed class ClaimAgendaView
{
    public float PopulationShare;
    public float LegislativeShare;
    public float Support;
    public float Opposition;
    public float Urgency;
    public float Score;
    public float ProgressMultiplier;
    public int IdeologyStage;
    public string TechnologyName = "";
    public string Blocker = "";
    public bool CanPropose => string.IsNullOrEmpty(Blocker);
}

public static class ClaimAgendaSystem
{
    public static ClaimAgendaContext BuildContext(Empire empire)
    {
        if (empire?.CoreKingdom == null) return null;
        var counts = IdeologyPopulationSystem.GetEmpireCounts(empire);
        return new ClaimAgendaContext
        {
            Empire = empire,
            ClassShares = InstitutionSystem.BuildClassShares(empire),
            Grievances = InstitutionSystem.GetClassGrievances(empire),
            IdeologyCounts = counts,
            Population = counts.Values.Sum(),
            ParliamentSeats = empire.data?.constitutional_economy?.parliament_seats?.Count ?? 0
        };
    }

    public static ClaimAgendaView Evaluate(ClaimAgendaContext context, FixedFaction faction,
        TemporaryFaction claim)
    {
        var view = new ClaimAgendaView();
        if (context == null || faction == null || claim == null)
        {
            view.Blocker = "institution_reform_invalid";
            return view;
        }

        Empire empire = context.Empire;
        if (faction.IsParty && context.Population > 0)
            view.PopulationShare = context.IdeologyCounts.TryGetValue(faction.Ideology, out int adherents)
                ? 100f * adherents / context.Population : 0f;

        float classSupport = 0f;
        float classOpposition = 0f;
        foreach (KeyValuePair<SocialClass, float> pair in context.ClassShares)
        {
            float effect = FactionClassSystem.GetClaimClassEffect(claim.type, pair.Key);
            if (effect > 0f) classSupport += pair.Value * effect * 4f;
            else classOpposition -= pair.Value * effect * 4f;
        }
        if (context.ParliamentSeats > 0)
        {
            int seats = empire.data.constitutional_economy.parliament_seats.Count(seat =>
                seat.faction_id == faction.GetID());
            view.LegislativeShare = 100f * seats / context.ParliamentSeats;
        }
        view.Support = Mathf.Clamp(12f + classSupport + view.PopulationShare * 0.32f +
            faction.CentralRatio * 0.24f + view.LegislativeShare * 0.18f, 0f, 100f);
        view.Opposition = Mathf.Clamp(8f + classOpposition +
            (context.ParliamentSeats > 0 ? (100f - view.LegislativeShare) * 0.12f : 0f), 0f, 100f);
        view.Urgency = GetUrgency(context, claim.type);

        string culture = InstitutionSystem.GetPrimaryCulture(empire);
        if (faction.IsParty)
        {
            for (int stage = 1; stage <= 3; stage++)
                if (InstitutionSystem.GetFeature(culture,
                        IdeologyInstitutionPaths.StageFeature(faction.Ideology, stage)) > 0f)
                    view.IdeologyStage = stage;
        }

        var reformNodes = InstitutionSystem.GetLineNodesWithFeature(empire,
                InstitutionFeatures.ClaimReformPrefix + claim.type).ToList();
        InstitutionNodeConfig target = reformNodes
            .FirstOrDefault(node => !InstitutionSystem.IsEnacted(culture, node.id));
        if (target != null)
        {
            view.TechnologyName = InstitutionSystem.GetNodeName(target);
            if (!InstitutionSystem.CanStartReform(empire, target, out string reason, false) &&
                !InstitutionSystem.CanStartReform(empire, target, out _, true))
                view.Blocker = reason;
        }
        else if (reformNodes.Count > 0)
        {
            view.TechnologyName = InstitutionSystem.GetNodeName(reformNodes.Last());
            view.Blocker = "institution_reform_already_enacted";
        }
        else if (claim.type == TemporaryFactionType.提高福利)
        {
            view.TechnologyName = $"{PartySystem.GetIdeologyName(faction.Ideology)} · " +
                                  LM.Get($"ideology_{faction.Ideology}_stage_2");
            if (!faction.IsParty || view.IdeologyStage < 2)
                view.Blocker = "agenda_requires_policy_program";
            else if (empire.data.constitutional_economy?.welfare_level >= 3)
                view.Blocker = "agenda_welfare_maximum";
            else if (empire.CoreKingdom.GetMoney() < ConstitutionalEconomySystem.GetWelfareAnnualCost(
                         empire, empire.data.constitutional_economy.welfare_level + 1))
                view.Blocker = "agenda_welfare_treasury";
        }

        // 与文化制度树、科技时代挂钩的前提(见 ClaimRules)；推动类决议显示要推动的制度
        if (view.CanPropose && !ClaimRules.IsAllowed(empire, claim.type, out string ruleBlocker))
            view.Blocker = ruleBlocker;
        if (string.IsNullOrEmpty(view.TechnologyName))
        {
            InstitutionNodeConfig pushTarget = ClaimRules.FindPushTarget(empire, claim.type);
            if (pushTarget != null) view.TechnologyName = InstitutionSystem.GetNodeName(pushTarget);
        }

        view.Score = view.Support - view.Opposition + view.Urgency +
                     (claim.IsStarted() ? 20f : 0f);
        view.ProgressMultiplier = Mathf.Clamp(0.65f +
            (view.Support - view.Opposition) / 100f +
            (view.IdeologyStage >= 1 ? 0.1f : 0f) +
            (view.IdeologyStage >= 2 ? 0.1f : 0f), 0.35f, 1.65f);
        return view;
    }

    private static float GetUrgency(ClaimAgendaContext context, TemporaryFactionType type)
    {
        Empire empire = context.Empire;
        float Grievance(SocialClass socialClass) => context.Grievances != null &&
            context.Grievances.TryGetValue(socialClass, out float value) ? value : 0f;
        float Share(SocialClass socialClass) => context.ClassShares != null &&
            context.ClassShares.TryGetValue(socialClass, out float value) ? value : 0f;
        return type switch
        {
            TemporaryFactionType.提高福利 =>
                (Grievance(SocialClass.Labour) + Grievance(SocialClass.Peasant)) * 0.18f,
            TemporaryFactionType.土地改革 => GetLandlessUrgency(empire),
            TemporaryFactionType.降低赋税 => (empire.data?.TaxRate ?? 0f) * 25f,
            TemporaryFactionType.建立共和 => (100f - empire.Legitimacy) * 0.18f +
                Mathf.Max(0f, (Share(SocialClass.Labour) + Share(SocialClass.Peasant) - 0.45f) * 80f),
            TemporaryFactionType.推行普选 => PartySystem.HasUniversalSuffrage(empire) ? 0f : 15f +
                Mathf.Max(0f, (Share(SocialClass.Labour) + Share(SocialClass.Peasant) - 0.40f) * 100f),
            TemporaryFactionType.开放党禁 => PartySystem.IsActive(empire) ? 0f : 15f,
            _ => 0f
        };
    }

    private static float GetLandlessUrgency(Empire empire)
    {
        var cities = empire.CoreKingdom?.cities?.Where(city => city != null && !city.isRekt()).ToList();
        return cities == null || cities.Count == 0 ? 0f :
            Mathf.Clamp((float)cities.Average(city => LandEconomySystem.GetReport(city).LandlessPopulationRatio) * 100f,
                0f, 30f);
    }
}

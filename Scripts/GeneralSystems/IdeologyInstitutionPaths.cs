using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;

namespace EmpireCraft.Scripts.GeneralSystems;

// Each culture line supplies its own prerequisite for party politics; ideological
// knowledge is shared by that culture, while a government's program is not.
public static class IdeologyInstitutionPaths
{
    public sealed class Profile
    {
        public readonly SocialClass Support;
        public readonly SocialClass Opposition;
        public readonly float MovementMultiplier;
        public readonly float VoteMultiplier;
        public readonly float DefectionBonus;
        public readonly float MilitiaMultiplier;
        public readonly float PolicyRelief;
        public readonly float PolicyBacklash;

        public Profile(SocialClass support, SocialClass opposition, float movement, float vote,
            float defection, float militia, float relief, float backlash)
        {
            Support = support;
            Opposition = opposition;
            MovementMultiplier = movement;
            VoteMultiplier = vote;
            DefectionBonus = defection;
            MilitiaMultiplier = militia;
            PolicyRelief = relief;
            PolicyBacklash = backlash;
        }
    }

    private static readonly Dictionary<PartyIdeology, Profile> Profiles = new()
    {
        [PartyIdeology.Anarchism] = new(SocialClass.Labour, SocialClass.Noble, 1.35f, 1.08f, 0.06f, 1.20f, 2.0f, 2.2f),
        [PartyIdeology.SocialDemocracy] = new(SocialClass.Labour, SocialClass.Landlord, 1.22f, 1.20f, 0.02f, 1.05f, 2.8f, 1.4f),
        [PartyIdeology.Libertarianism] = new(SocialClass.Merchant, SocialClass.Officer, 1.24f, 1.12f, 0.02f, 0.95f, 2.0f, 1.6f),
        [PartyIdeology.SocialLiberalism] = new(SocialClass.Merchant, SocialClass.Noble, 1.17f, 1.18f, 0.02f, 1.00f, 2.4f, 1.3f),
        [PartyIdeology.Capitalism] = new(SocialClass.Merchant, SocialClass.Labour, 1.16f, 1.17f, 0.01f, 1.00f, 2.4f, 2.0f),
        [PartyIdeology.ConservativeLiberalism] = new(SocialClass.Landlord, SocialClass.Labour, 1.10f, 1.16f, 0.03f, 1.00f, 2.0f, 1.4f),
        [PartyIdeology.Centrism] = new(SocialClass.Officer, SocialClass.Labour, 1.08f, 1.24f, 0.03f, 1.00f, 1.8f, 0.8f),
        [PartyIdeology.Socialism] = new(SocialClass.Peasant, SocialClass.Landlord, 1.26f, 1.15f, 0.06f, 1.15f, 2.7f, 1.8f),
        [PartyIdeology.Communism] = new(SocialClass.Labour, SocialClass.Noble, 1.32f, 1.06f, 0.11f, 1.30f, 3.0f, 2.5f),
        [PartyIdeology.ReligiousDemocracy] = new(SocialClass.Peasant, SocialClass.Merchant, 1.20f, 1.13f, 0.04f, 1.05f, 2.3f, 1.4f),
        [PartyIdeology.Authoritarianism] = new(SocialClass.Officer, SocialClass.Merchant, 1.12f, 1.05f, 0.10f, 1.20f, 2.3f, 2.0f),
        [PartyIdeology.Conservatism] = new(SocialClass.Noble, SocialClass.Labour, 1.08f, 1.14f, 0.08f, 1.10f, 2.0f, 1.4f),
        [PartyIdeology.Fascism] = new(SocialClass.Army, SocialClass.Merchant, 1.28f, 1.04f, 0.12f, 1.30f, 2.5f, 2.8f)
    };

    public static Profile GetProfile(PartyIdeology ideology) => Profiles[ideology];

    public static string Branch(PartyIdeology ideology) => $"ideology_{ideology}";

    public static string StageFeature(PartyIdeology ideology, int stage) =>
        $"ideology_stage:{ideology}:{stage}";

    public static bool TryGetIdeology(string branch, out PartyIdeology ideology)
    {
        ideology = PartyIdeology.Conservatism;
        return branch != null && branch.StartsWith("ideology_", StringComparison.Ordinal) &&
               Enum.TryParse(branch.Substring("ideology_".Length), out ideology) &&
               Enum.IsDefined(typeof(PartyIdeology), ideology);
    }

    public static IReadOnlyList<InstitutionNodeView> Visible(IReadOnlyList<InstitutionNodeView> views,
        PartyIdeology selected)
    {
        var included = new HashSet<string>(views.Where(view =>
                !TryGetIdeology(view.Node.branch, out _) ||
                TryGetIdeology(view.Node.branch, out PartyIdeology ideology) && ideology == selected)
            .Select(view => view.Node.id), StringComparer.Ordinal);
        var byId = views.ToDictionary(view => view.Node.id, StringComparer.Ordinal);
        var pending = new Queue<string>(included);
        while (pending.Count > 0)
        {
            if (!byId.TryGetValue(pending.Dequeue(), out InstitutionNodeView view)) continue;
            foreach (string required in InstitutionDefinitionRegistry.GetAllPrerequisites(view.Node))
                if (included.Add(required)) pending.Enqueue(required);
        }
        return views.Where(view => included.Contains(view.Node.id)).ToList();
    }

    public static void AddTo(InstitutionTreeConfig tree)
    {
        if (tree?.nodes == null) return;
        InstitutionNodeConfig partyBan = tree.nodes.FirstOrDefault(node =>
            node?.features?.ContainsKey(PartySystem.FeaturePartyPolitics) == true);
        if (partyBan == null) return;
        foreach (PartyIdeology ideology in Enum.GetValues(typeof(PartyIdeology)))
        {
            string branch = Branch(ideology);
            InstitutionNodeConfig foundation = tree.nodes.FirstOrDefault(node =>
                node?.features?.ContainsKey(IdeologySpreadSystem.FeatureKey(ideology)) == true);
            if (foundation == null)
            {
                foundation = new InstitutionNodeConfig
                {
                    id = $"{tree.line}_ideology_{ideology}_foundation",
                    name_key = $"party_ideology_{ideology}",
                    description_key = "ideology_stage_foundation_desc",
                    branch = branch,
                    advancement = partyBan.advancement + 1,
                    requires = new List<string> { partyBan.id },
                    features = new Dictionary<string, float>
                    {
                        [IdeologySpreadSystem.FeatureKey(ideology)] = 1f
                    }
                };
                tree.nodes.Add(foundation);
            }
            else
            {
                foundation.branch = branch;
                foundation.requires.RemoveAll(required => tree.nodes.Any(candidate =>
                    candidate?.id == required && candidate.features?.Keys.Any(key =>
                        key.StartsWith("ideology:", StringComparison.Ordinal)) == true));
                if (foundation.requires.Count == 0) foundation.requires.Add(partyBan.id);
            }

            InstitutionNodeConfig previous = foundation;
            for (int stage = 1; stage <= 3; stage++)
            {
                string id = $"{tree.line}_ideology_{ideology}_stage_{stage}";
                InstitutionNodeConfig configured = tree.nodes.FirstOrDefault(candidate => candidate?.id == id);
                if (configured != null)
                {
                    configured.branch = branch;
                    if (configured.name_key == $"ideology_stage_{stage}")
                        configured.name_key = $"ideology_{ideology}_stage_{stage}";
                    previous = configured;
                    continue;
                }
                Profile profile = GetProfile(ideology);
                var node = new InstitutionNodeConfig
                {
                    id = id,
                    name_key = $"ideology_{ideology}_stage_{stage}",
                    description_key = $"ideology_stage_{stage}_desc",
                    branch = branch,
                    advancement = previous.advancement + 1,
                    requires = new List<string> { previous.id },
                    research = new InstitutionResearchConfig { mandate_required = 25 },
                    reform = new InstitutionReformConfig
                    {
                        base_progress_per_year = 8f,
                        minimum_years = 5 + stage * 2
                    },
                    politics = new InstitutionPoliticsConfig
                    {
                        support_classes = new Dictionary<SocialClass, float> { [profile.Support] = 0.6f + stage * 0.1f },
                        oppose_classes = new Dictionary<SocialClass, float> { [profile.Opposition] = 0.4f + stage * 0.1f }
                    },
                    features = new Dictionary<string, float> { [StageFeature(ideology, stage)] = 1f },
                    absorb = new InstitutionAbsorbConfig { enabled = false }
                };
                tree.nodes.Add(node);
                previous = node;
            }
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;

namespace EmpireCraft.Scripts.GeneralSystems;

public static class ProductionInstitutionPaths
{
    public static void AddTo(InstitutionTreeConfig tree)
    {
        if (tree?.nodes == null || string.IsNullOrWhiteSpace(tree.line)) return;
        string root = tree.root_nodes?.FirstOrDefault(id => tree.nodes.Any(node => node?.id == id));
        if (string.IsNullOrEmpty(root)) return;

        string prerequisite = root;
        for (int stage = 1; stage <= 3; stage++)
        {
            string id = $"{tree.line}_urban_production_{stage}";
            if (tree.nodes.Any(node => node?.id == id))
            {
                prerequisite = id;
                continue;
            }

            tree.nodes.Add(new InstitutionNodeConfig
            {
                id = id,
                name_key = $"urban_production_stage_{stage}",
                description_key = $"urban_production_stage_{stage}_desc",
                branch = "production",
                advancement = stage switch { 1 => 5, 2 => 9, _ => 14 },
                requires = new List<string> { prerequisite },
                research = new InstitutionResearchConfig { mandate_required = 20 + stage * 5 },
                reform = new InstitutionReformConfig
                {
                    base_progress_per_year = 8f,
                    minimum_years = 5 + stage * 2
                },
                politics = new InstitutionPoliticsConfig
                {
                    support_classes = new Dictionary<SocialClass, float>
                    {
                        [SocialClass.Peasant] = 0.35f,
                        [SocialClass.Merchant] = 0.8f,
                        [SocialClass.Labour] = 0.7f,
                        [SocialClass.Citizen] = 0.55f
                    },
                    oppose_classes = new Dictionary<SocialClass, float>
                    {
                        [SocialClass.Landlord] = 0.15f
                    }
                },
                features = new Dictionary<string, float>
                {
                    [InstitutionFeatures.UrbanProductionStage] = stage
                },
                absorb = new InstitutionAbsorbConfig { enabled = false }
            });
            prerequisite = id;
        }
    }
}

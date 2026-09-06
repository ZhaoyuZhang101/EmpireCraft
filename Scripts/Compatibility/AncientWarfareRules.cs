using System;

namespace EmpireCraft.Scripts.Compatibility
{
    public static class AncientWarfareRules
    {
        public const string XiaSpecies = "Xia";
        public const string XiaizationLevelKey = "aw_xiaization_level";
        public const string SuzerainIdKey = "aw_vassal_suzerain_id";

        public static bool OwnsKingdom(bool loaded, string originalSpecies, string kingdomAsset,
            string resolvedSpecies, string banner, int xiaizationLevel)
        {
            return loaded && (xiaizationLevel > 0 || IsXia(originalSpecies) || IsXia(kingdomAsset) ||
                IsXia(resolvedSpecies) || IsXia(banner));
        }

        public static bool IsXia(string species)
        {
            return string.Equals(species, XiaSpecies, StringComparison.Ordinal);
        }
    }
}

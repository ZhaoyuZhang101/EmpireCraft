namespace EmpireCraft.Scripts.Layer
{
    public static class DeJureTitleBindingRules
    {
        public const int WeakMandateThreshold = 60;

        public static bool IsAdministrativeAcquisitionBlocked(bool isLvLing, bool isProvinceOrMilitary,
            int mandate)
        {
            return isLvLing && isProvinceOrMilitary && mandate > WeakMandateThreshold;
        }

        public static string GetLandedPeerageKey(bool isImperialClan)
        {
            return isImperialClan ? "default_peerages_2" : "tang_peerage_guogong";
        }
    }
}

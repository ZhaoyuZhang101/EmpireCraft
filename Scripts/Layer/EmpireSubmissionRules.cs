using System.Collections.Generic;

namespace EmpireCraft.Scripts.Layer;

public static class EmpireSubmissionRules
{
    public const int MinimumMandate = 80;
    public const int RebellionOpinionPenalty = -100;

    public static bool CanAcceptVoluntarySubmission(int mandate, int treasury)
    {
        return mandate >= MinimumMandate && treasury >= 0;
    }

    public static bool CanVoluntarilyBecomeTributary(bool hasMainTitle,
        bool mainTitleBelongsToOverlordCore)
    {
        return !hasMainTitle || !mainTitleBelongsToOverlordCore;
    }

    public static bool IsOriginalRebellionEmpire(IEnumerable<long> originEmpireIds, long empireId)
    {
        if (originEmpireIds == null || empireId < 0) return false;
        foreach (long originEmpireId in originEmpireIds)
        {
            if (originEmpireId == empireId) return true;
        }
        return false;
    }
}

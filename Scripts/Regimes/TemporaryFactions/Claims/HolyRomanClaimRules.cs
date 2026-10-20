using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

internal static class HolyRomanClaimRules
{
    public static bool IsEligible(Empire empire)
    {
        return empire != null && !empire.isRekt() && empire.CoreKingdom != null &&
               !empire.CoreKingdom.isRekt() &&
               empire.CoreKingdom.GetRegime()?.type == RegimeType.Feudalism;
    }

    public static IEnumerable<Kingdom> GetPrinces(Empire empire)
    {
        if (!IsEligible(empire)) return Enumerable.Empty<Kingdom>();
        return empire.kingdoms_list.Where(kingdom => kingdom != null && !kingdom.isRekt() &&
                                                     kingdom != empire.CoreKingdom && kingdom.hasKing());
    }

    // 世俗诸侯：自由城市、主教领本就不开战、不给中央出兵，治安令与诸侯特权都不涉及它们
    public static IEnumerable<Kingdom> GetSecularPrinces(Empire empire) =>
        GetPrinces(empire).Where(HolyRomanSystem.SecularPrince);

    public static bool HasElectoralCollege(Empire empire)
    {
        if (!IsEligible(empire)) return false;
        int validElectors = empire.GetCabinetMembers().Count(actor => actor != null && !actor.isRekt());
        return validElectors >= 2 && GetPrinces(empire).Count() >= 2;
    }
}

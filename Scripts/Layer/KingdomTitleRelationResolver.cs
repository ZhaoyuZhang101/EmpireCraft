using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;

namespace EmpireCraft.Scripts.Layer;

public sealed class KingdomTitleHolderRelation
{
    public Empire Empire;
    public Actor Holder;
    public bool Unlanded;
}

public static class KingdomTitleRelationResolver
{
    public static List<KingdomTitleHolderRelation> ResolveCurrentHolders(KingdomTitle title)
    {
        var result = new List<KingdomTitleHolderRelation>();
        if (title == null || title.isRekt()) return result;

        EmpireCore core = title.title_capital?.GetEmpireCore();
        if (core != null)
        {
            foreach (Empire empire in EmpireCoreManager.GetEmpires(core)
                         .Where(empire => empire != null && !empire.IsArchived())
                         .OrderBy(empire => empire.GetEmpireName()))
            {
                Actor holder = empire.GetLegalPeerageHolder(title);
                if (holder == null || holder.isRekt()) continue;
                Kingdom landedKingdom = empire.GetLandedLegalTitleKingdom(title);
                result.Add(new KingdomTitleHolderRelation
                {
                    Empire = empire,
                    Holder = holder,
                    Unlanded = landedKingdom == null || landedKingdom.king != holder
                });
            }
        }

        Actor directHolder = title.main_kingdom != null && !title.main_kingdom.isRekt()
            ? title.main_kingdom.king
            : title.owner != null && !title.owner.isRekt() ? title.owner : null;
        if (directHolder != null && !result.Any(view => view.Holder?.id == directHolder.id))
        {
            Empire directEmpire = directHolder.kingdom?.GetEmpire() ??
                                   directHolder.kingdom?.GetTakenAllianceEmpire();
            result.Add(new KingdomTitleHolderRelation
            {
                Empire = directEmpire,
                Holder = directHolder,
                Unlanded = title.main_kingdom == null || title.main_kingdom.isRekt() ||
                           title.main_kingdom.king != directHolder
            });
        }

        return result;
    }

    public static List<Kingdom> FindCurrentAdministrations(KingdomTitle title)
    {
        if (title == null || title.isRekt()) return new List<Kingdom>();
        return (title.city_list ?? new List<City>())
            .Where(city => city != null && !city.isRekt() && city.kingdom != null)
            .Select(city => city.kingdom)
            .Distinct()
            .Where(kingdom => !kingdom.isRekt() && kingdom.GetAdministrativeTitle() == title)
            .OrderBy(kingdom => kingdom.id)
            .ToList();
    }
}

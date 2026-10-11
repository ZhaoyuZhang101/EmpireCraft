using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GodPowers;

// 春秋战国的神力(见 ZhouEraSystem)：
//   召集会盟——点周室任一领土，立即会盟改立霸主(春秋)；
//   赐胙——点一个诸侯，天子赐胙立其为霸主(春秋)；
//   推进时代——点周室任一领土：西周→春秋→战国→统一
public static class ZhouEraPowers
{
    public static void init()
    {
        PowerLibrary powers = AssetManager.powers;
        powers.add(new GodPower { id = "zhou_conference", name = "zhou_conference", click_action = Conference });
        powers.add(new GodPower { id = "zhou_bestow", name = "zhou_bestow", click_action = Bestow });
        powers.add(new GodPower { id = "zhou_advance", name = "zhou_advance", click_action = Advance });
    }

    private static Kingdom KingdomAt(WorldTile tile) => tile?.zone_city?.kingdom is Kingdom kingdom &&
                                                       kingdom.data != null && !kingdom.isRekt() ? kingdom : null;

    private static Empire ZhouAt(WorldTile tile, out Kingdom kingdom)
    {
        kingdom = KingdomAt(tile);
        Empire empire = kingdom?.GetEmpire();
        if (ZhouEraSystem.Applies(empire)) return empire;
        Tip(LM.Get("zhou_power_not_zhou"));
        return null;
    }

    private static void Tip(string text) => WorldTip.showNow(text, false, "top", 3f);

    private static bool Conference(WorldTile tile, string power)
    {
        Empire empire = ZhouAt(tile, out _);
        if (empire == null) return false;
        var state = ZhouEraSystem.State(empire);
        if (state.era != ZhouEraSystem.Spring)
        {
            Tip(LM.Get("zhou_power_spring_only"));
            return false;
        }
        Kingdom hegemon = ZhouEraSystem.HoldConference(empire, state, null);
        Tip(hegemon == null ? LM.Get("zhou_power_no_candidate")
            : string.Format(LM.Get("zhou_power_hegemon"), hegemon.GetKingdomName()));
        return hegemon != null;
    }

    private static bool Bestow(WorldTile tile, string power)
    {
        Empire empire = ZhouAt(tile, out Kingdom kingdom);
        if (empire == null) return false;
        var state = ZhouEraSystem.State(empire);
        if (state.era != ZhouEraSystem.Spring)
        {
            Tip(LM.Get("zhou_power_spring_only"));
            return false;
        }
        if (kingdom == empire.CoreKingdom)
        {
            Tip(LM.Get("zhou_power_pick_vassal"));
            return false;
        }
        ZhouEraSystem.HoldConference(empire, state, kingdom);
        Tip(string.Format(LM.Get("zhou_power_hegemon"), kingdom.GetKingdomName()));
        return true;
    }

    private static bool Advance(WorldTile tile, string power)
    {
        Empire empire = ZhouAt(tile, out _);
        if (empire == null) return false;
        if (ZhouEraSystem.State(empire).era >= ZhouEraSystem.Unified) return false;
        ZhouEraSystem.GodAdvance(empire);
        Empire now = tile.zone_city?.kingdom?.GetEmpire();
        int era = now?.data?.zhou?.era ?? ZhouEraSystem.Unified;
        Tip(string.Format(LM.Get("zhou_power_advanced"), LM.Get("zhou_era_" + era)));
        return true;
    }
}

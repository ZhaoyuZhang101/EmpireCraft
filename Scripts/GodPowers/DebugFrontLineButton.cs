using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GodPowers;

public static class DebugFrontLineButton
{
    public static void init()
    {
        PowerLibrary powerLib = AssetManager.powers;
        powerLib.add(new GodPower
        {
            id = "debug_frontline",
            name = "debug_frontline",
            click_action = clickDebugFrontline,
            select_button_action = selectDebugFrontline,
            can_drag_map = true
        });
    }

    private static bool selectDebugFrontline(string pPowerID)
    {
        WorldTip.showNow("debug_frontline", true, "top", 3f, "#F3961F");
        return false;
    }

    private static bool clickDebugFrontline(WorldTile pTile, string pPowerID)
    {
        Kingdom kingdom = pTile?.zone_city?.kingdom;
        if (kingdom == null || kingdom.isRekt() || kingdom.isNeutral())
        {
            ActionLibrary.showWhisperTip("debug_frontline_invalid_target");
            return false;
        }

        KingdomFrontLineHelper.DebugFrontZones(kingdom);
        ActionLibrary.showWhisperTip("debug_frontline_triggered");
        return true;
    }
}

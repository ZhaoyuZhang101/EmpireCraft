using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;

namespace EmpireCraft.Scripts.GodPowers;

public static class RemoveTitleFromEmpireCoreButton
{
    public static void init()
    {
        AssetManager.powers.add(new GodPower
        {
            id = "remove_title_from_empire_core",
            name = "remove_title_from_empire_core",
            click_action = remove_title_from_empire_core_action
        });
    }

    private static bool remove_title_from_empire_core_action(WorldTile pTile, string pPower)
    {
        City city = pTile?.zone?.city;
        KingdomTitle title = city?.GetTitle();
        EmpireCore core = city?.GetEmpireCore();
        if (city == null || title == null || core == null)
        {
            ActionLibrary.showWhisperTip("empire_core_invalid_target");
            return false;
        }

        if (!EmpireCoreManager.RemoveTitleFromCore(core, title))
        {
            ActionLibrary.showWhisperTip("empire_core_invalid_target");
            return false;
        }

        ActionLibrary.showWhisperTip("remove_title_from_empire_core_success");
        World.world.zone_calculator?.dirtyAndClear();
        World.world.zone_calculator?.setDrawnZonesDirty();
        return true;
    }
}

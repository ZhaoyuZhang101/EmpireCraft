using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;

namespace EmpireCraft.Scripts.GodPowers;

public static class CreateEmpireCoreButton
{
    public static void init()
    {
        AssetManager.powers.add(new GodPower
        {
            id = "create_empire_core",
            name = "create_empire_core",
            click_action = create_empire_core_action
        });
    }

    private static bool create_empire_core_action(WorldTile pTile, string pPower)
    {
        City city = pTile?.zone?.city;
        KingdomTitle title = city?.GetTitle();
        if (city == null || title == null)
        {
            ActionLibrary.showWhisperTip("empire_core_invalid_target");
            return false;
        }

        EmpireCore core = EmpireCoreManager.CreateCoreFromTitle(title);
        if (core == null)
        {
            ActionLibrary.showWhisperTip("empire_core_invalid_target");
            return false;
        }

        ActionLibrary.showWhisperTip("create_empire_core_success");
        World.world.zone_calculator?.dirtyAndClear();
        World.world.zone_calculator?.setDrawnZonesDirty();
        return true;
    }
}

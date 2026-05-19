using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;

namespace EmpireCraft.Scripts.GodPowers;

public static class DestroyEmpireCoreButton
{
    public static void init()
    {
        AssetManager.powers.add(new GodPower
        {
            id = "destroy_empire_core",
            name = "destroy_empire_core",
            click_action = destroy_empire_core_action
        });
    }

    private static bool destroy_empire_core_action(WorldTile pTile, string pPower)
    {
        City city = pTile?.zone?.city;
        EmpireCore core = city?.GetEmpireCore();
        if (city == null || core == null)
        {
            ActionLibrary.showWhisperTip("empire_core_invalid_target");
            return false;
        }

        if (!EmpireCoreManager.DestroyEmpireCore(core))
        {
            ActionLibrary.showWhisperTip("empire_core_invalid_target");
            return false;
        }

        ActionLibrary.showWhisperTip("destroy_empire_core_success");
        World.world.zone_calculator?.dirtyAndClear();
        World.world.zone_calculator?.setDrawnZonesDirty();
        return true;
    }
}

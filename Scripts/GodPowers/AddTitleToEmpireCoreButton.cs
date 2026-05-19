using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;

namespace EmpireCraft.Scripts.GodPowers;

public static class AddTitleToEmpireCoreButton
{
    public static void init()
    {
        AssetManager.powers.add(new GodPower
        {
            id = "add_title_to_empire_core",
            name = "add_title_to_empire_core",
            click_action = add_title_to_empire_core_action
        });
    }

    private static bool add_title_to_empire_core_action(WorldTile pTile, string pPower)
    {
        City city = pTile?.zone?.city;
        if (city == null || city.isRekt() || city.isNeutral())
        {
            ActionLibrary.showWhisperTip("empire_core_invalid_target");
            return false;
        }

        if (ConfigData.selected_cityA == null)
        {
            if (!city.hasTitle())
            {
                ActionLibrary.showWhisperTip("empire_core_select_title_first");
                return false;
            }

            ConfigData.selected_cityA = city;
            ActionLibrary.showWhisperTip("empire_core_title_selected");
            return false;
        }

        if (ConfigData.selected_cityA == city)
        {
            ConfigData.selected_cityA = null;
            ActionLibrary.showWhisperTip("city_cancelled");
            return false;
        }

        KingdomTitle title = ConfigData.selected_cityA.GetTitle();
        EmpireCore core = city.GetEmpireCore();
        ConfigData.selected_cityA = null;
        if (title == null || core == null)
        {
            ActionLibrary.showWhisperTip("empire_core_invalid_target");
            return false;
        }

        if (!EmpireCoreManager.AddTitleToCore(core, title))
        {
            ActionLibrary.showWhisperTip("empire_core_invalid_target");
            return false;
        }

        ActionLibrary.showWhisperTip("add_title_to_empire_core_success");
        World.world.zone_calculator?.dirtyAndClear();
        World.world.zone_calculator?.setDrawnZonesDirty();
        return true;
    }
}

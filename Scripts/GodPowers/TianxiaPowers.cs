using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;

namespace EmpireCraft.Scripts.GodPowers;

// 天下(见 TianxiaSystem)：先点一个帝国核心的城，再点另一个帝国核心的城，前者所在的天下并入后者的天下；
// 退出：点联合天下里某个帝国核心的城，它自成一个天下
public static class TianxiaPowers
{
    public static void init()
    {
        AssetManager.powers.add(new GodPower
        {
            id = "tianxia_join",
            name = "tianxia_join",
            click_action = JoinAction
        });
        AssetManager.powers.add(new GodPower
        {
            id = "tianxia_leave",
            name = "tianxia_leave",
            click_action = LeaveAction
        });
    }

    private static bool JoinAction(WorldTile pTile, string pPower)
    {
        City city = pTile?.zone?.city;
        EmpireCore core = city?.GetEmpireCore();
        if (city == null || city.isRekt() || core == null)
        {
            ActionLibrary.showWhisperTip("tianxia_invalid_target");
            return false;
        }
        if (ConfigData.selected_cityA == null || ConfigData.selected_cityA.isRekt())
        {
            ConfigData.selected_cityA = city;
            ActionLibrary.showWhisperTip("tianxia_core_selected");
            return false;
        }
        EmpireCore from = ConfigData.selected_cityA.GetEmpireCore();
        ConfigData.selected_cityA = null;
        if (from == null || TianxiaSystem.Lead(from) == TianxiaSystem.Lead(core))
        {
            ActionLibrary.showWhisperTip("city_cancelled");
            return false;
        }
        if (!TianxiaSystem.Join(from, core))
        {
            ActionLibrary.showWhisperTip("tianxia_invalid_target");
            return false;
        }
        ActionLibrary.showWhisperTip("tianxia_join_success");
        return true;
    }

    private static bool LeaveAction(WorldTile pTile, string pPower)
    {
        EmpireCore core = pTile?.zone?.city?.GetEmpireCore();
        if (core == null || !TianxiaSystem.Leave(core))
        {
            ActionLibrary.showWhisperTip("tianxia_not_joint");
            return false;
        }
        ActionLibrary.showWhisperTip("tianxia_leave_success");
        return true;
    }
}

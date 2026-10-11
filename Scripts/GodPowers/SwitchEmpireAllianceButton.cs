namespace EmpireCraft.Scripts.GodPowers;

// 帝国视图下是否叠加显示同盟
public static class SwitchEmpireAllianceButton
{
    public static void init()
    {
        PowerLibrary powerLib = AssetManager.powers;
        powerLib.add(new GodPower
        {
            id = "empire_show_alliance",
            name = "empire_show_alliance",
            toggle_name = "switch_empire_show_alliance",
            toggle_action = toggleAction
        });
    }

    private static void toggleAction(string pPower)
    {
        GodPower godPower = AssetManager.powers.get(pPower);
        PlayerOptionData playerOptionData = PlayerConfig.dict[godPower.toggle_name];
        ModClass.EMPIRE_SHOW_ALLIANCE_SWITCH = !playerOptionData.boolVal;
        // 立即重绘地图区块，让同盟成员换成同盟颜色/换回本国颜色
        World.world?.zone_calculator?.dirtyAndClear();
    }
}

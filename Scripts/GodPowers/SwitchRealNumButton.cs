namespace EmpireCraft.Scripts.GodPowers;

public static class SwitchRealNumButton
{

    public static void init()
    {
        PowerLibrary powerLib = AssetManager.powers;
        powerLib.add(new GodPower
        {
            id = "real_num",
            name = "real_num",
            toggle_name = "switch_real_num",
            toggle_action = toggleAction
        });
    }

    private static void toggleAction(string pPower)
    {
        GodPower godPower = AssetManager.powers.get(pPower);
        PlayerOptionData playerOptionData = PlayerConfig.dict[godPower.toggle_name];
        ModClass.REAL_NUM_SWITCH = !playerOptionData.boolVal;
    }
}
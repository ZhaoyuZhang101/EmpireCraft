namespace EmpireCraft.Scripts.GodPowers;

public static class SwitchSimpleNameplateButton
{
    public static void init()
    {
        PowerLibrary powerLib = AssetManager.powers;
        powerLib.add(new GodPower
        {
            id = "simple_nameplate",
            name = "simple_nameplate",
            toggle_name = "switch_simple_nameplate",
            toggle_action = toggleAction
        });
    }

    private static void toggleAction(string pPower)
    {
        GodPower godPower = AssetManager.powers.get(pPower);
        PlayerOptionData playerOptionData = PlayerConfig.dict[godPower.toggle_name];
        ModClass.SIMPLE_NAMEPLATE_SWITCH = !playerOptionData.boolVal;
    }
}

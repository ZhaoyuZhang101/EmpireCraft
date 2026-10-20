namespace EmpireCraft.Scripts.GodPowers;

// 战声开关：镜头拉近到交战处时听得到厮杀声(见 GeneralSystems.BattleSound)。
// 与地形浮雕一样随玩家设置保存，默认开；音量跟原版的音效音量走
public static class SwitchBattleSoundButton
{
    public const string Option = "switch_battle_sound";

    public static bool On => !PlayerConfig.dict.TryGetValue(Option, out PlayerOptionData option) || option.boolVal;

    public static void init()
    {
        // OptionAsset 要自己登记(理由见 SwitchTerrainReliefButton)
        if (!AssetManager.options_library.has(Option))
            AssetManager.options_library.add(new OptionAsset { id = Option, type = OptionType.Bool, default_bool = true });
        if (!PlayerConfig.dict.ContainsKey(Option))
            PlayerConfig.instance.data.add(new PlayerOptionData(Option) { boolVal = true });
        AssetManager.powers.add(new GodPower
        {
            id = "battle_sound",
            name = "battle_sound",
            toggle_name = Option
        });
        GeneralSystems.BattleSound.EnsureHost();
    }
}

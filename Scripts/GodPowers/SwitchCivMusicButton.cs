namespace EmpireCraft.Scripts.GodPowers;

// 文明乐开关：镜头拉近到某一系文明的地盘时放那一系的曲子、原版背景音乐淡出(见 GeneralSystems.CivMusic)。
// 与战声一样随玩家设置保存，默认开；音量跟原版的音乐音量走
public static class SwitchCivMusicButton
{
    public const string Option = "switch_civ_music";

    public static bool On => !PlayerConfig.dict.TryGetValue(Option, out PlayerOptionData option) || option.boolVal;

    public static void init()
    {
        // OptionAsset 要自己登记(理由见 SwitchBattleSoundButton)
        if (!AssetManager.options_library.has(Option))
            AssetManager.options_library.add(new OptionAsset { id = Option, type = OptionType.Bool, default_bool = true });
        if (!PlayerConfig.dict.ContainsKey(Option))
            PlayerConfig.instance.data.add(new PlayerOptionData(Option) { boolVal = true });
        AssetManager.powers.add(new GodPower
        {
            id = "civ_music",
            name = "civ_music",
            toggle_name = Option
        });
        GeneralSystems.CivMusic.EnsureHost();
    }
}

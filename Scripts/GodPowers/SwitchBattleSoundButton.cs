namespace EmpireCraft.Scripts.GodPowers;

// 战声开关：镜头拉近到交战处时听得到厮杀声(见 GeneralSystems.BattleSound)。
// 随玩家设置保存，换存档不用重开，默认开；音量跟原版的音效音量走
public static class SwitchBattleSoundButton
{
    public const string Option = "switch_battle_sound";

    public static bool On => !PlayerConfig.dict.TryGetValue(Option, out PlayerOptionData option) || option.boolVal;

    public static void init()
    {
        // 开关的 OptionAsset 要自己登记：GodPower.option_asset 按 toggle_name 去 options_library 里取，
        // NML 的 CreateToggleButton 只在玩家设置里还没有这一项时才替我们登记，而这里先放了默认开的设置项，
        // 它就跳过了——没有 OptionAsset，换图、读档时原版刷新开关图标(PowerButton.checkToggleIcon)就空引用
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

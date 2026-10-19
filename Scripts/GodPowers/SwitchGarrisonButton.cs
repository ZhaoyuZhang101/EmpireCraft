namespace EmpireCraft.Scripts.GodPowers;

// 玩家显示偏好，默认关闭；与地形浮雕一样随玩家设置保存。
public static class SwitchGarrisonButton
{
    public const string Option = "switch_show_garrison";

    public static bool Shown => PlayerConfig.dict.TryGetValue(Option, out PlayerOptionData option) && option.boolVal;

    public static void init()
    {
        if (!AssetManager.options_library.has(Option))
            AssetManager.options_library.add(new OptionAsset { id = Option, type = OptionType.Bool, default_bool = false });
        if (!PlayerConfig.dict.ContainsKey(Option))
            PlayerConfig.instance.data.add(new PlayerOptionData(Option) { boolVal = false });
        AssetManager.powers.add(new GodPower
        {
            id = "show_garrison",
            name = "show_garrison",
            toggle_name = Option
        });
    }
}

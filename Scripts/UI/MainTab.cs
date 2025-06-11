using System.Collections.Generic;
using EmpireCraft.Scripts.GodPowers;
using NeoModLoader.General;
using NeoModLoader.General.UI.Tab;

namespace ExampleMod.UI;

internal static class MainTab
{
    public const string SET_EMPIRE_POWER = "set_empire_power";
    public const string PROVINCE_LAYER_TOGGLE = "province_layer_toggle";
    public static PowersTab tab;

    public static void Init()
    {
        // Create a tab with id "Example", title key "tab_example", description key "hotkey_tip_tab_other", and icon "ui/icons/iconSteam".
        // 创建一个id为"Example", 标题key为"set_empire_power", 描述key为"hotkey_tip_tab_other", 图标为"ui/icons/iconSteam"的标签页.
        tab = TabManager.CreateTab("EmpireTab", "empire_tab_name", "empire_tab_description",
            SpriteTextureLoader.getSprite("ui/icons/iconSteam"));
        // Set the layout of the tab. The layout is a list of strings, each string is a category. Names of each category are not important.
        // 设置标签页的布局. 布局是一个字符串列表, 每个字符串是一个分类. 每个分类的名字不重要.
        tab.SetLayout(new List<string>()
        {
            SET_EMPIRE_POWER,
            PROVINCE_LAYER_TOGGLE
        });
        // Add buttons to the tab.
        // 向标签页添加按钮.
        _addButtons();
        // Update the layout of the tab.
        // 更新标签页的布局.
        tab.UpdateLayout();
    }

    private static void _addButtons()
    {
        SetEmpirePower.init();
        tab.AddPowerButton(SET_EMPIRE_POWER,
            PowerButtonCreator.CreateGodPowerButton("SetEmpire",
                SpriteTextureLoader.getSprite("ui/icons/iconAttack")));
        ProvinceLayerToggle.init();
        tab.AddPowerButton(PROVINCE_LAYER_TOGGLE,
            PowerButtonCreator.CreateToggleButton("ProvinceLayer",
                SpriteTextureLoader.getSprite("ui/icons/iconAttack")));
        
    }
}
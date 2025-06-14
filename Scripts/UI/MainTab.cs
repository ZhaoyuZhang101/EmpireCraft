using System.Collections.Generic;
using EmpireCraft.Scripts.GodPowers;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.General.UI.Tab;
using NeoModLoader.utils;

namespace EmpireCraft.Scripts.UI;

internal static class MainTab
{
    public const string CREATE_EMPIRE_BUTTON = "create_empire_button";
    public const string EMPIRE_LAYER_TOGGLE = "empire_layer_toggle";
    public const string EMPIRE_LIST = "empire_list_window";
    public static PowersTab tab;

    public static void Init()
    {
        // Create a tab with id "Example", title key "tab_example", description key "hotkey_tip_tab_other", and icon "ui/icons/iconSteam".
        // 创建一个id为"Example", 标题key为"set_empire_power", 描述key为"hotkey_tip_tab_other", 图标为"ui/icons/iconSteam"的标签页.
        tab = TabManager.CreateTab("EmpireTab", "empire_tab_name", "empire_tab_description",
            SpriteLoadUtils.LoadSingleSprite(ModClass._declare.FolderPath+"/Resources/TabEmpire.png"));
        // Set the layout of the tab. The layout is a list of strings, each string is a category. Names of each category are not important.
        // 设置标签页的布局. 布局是一个字符串列表, 每个字符串是一个分类. 每个分类的名字不重要.
        tab.SetLayout(new List<string>()
        {
            CREATE_EMPIRE_BUTTON,
            EMPIRE_LAYER_TOGGLE,
            EMPIRE_LIST,
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
        CreateEmpireButton.init();
        tab.AddPowerButton(CREATE_EMPIRE_BUTTON,
            PowerButtonCreator.CreateGodPowerButton("create_empire",
                SpriteTextureLoader.getSprite("ui/icons/iconKingdom")));
        EmpireLayerToggle.init();
        tab.AddPowerButton(EMPIRE_LAYER_TOGGLE, FixFunctions.CreateToggleButton("empire_layer",
                SpriteTextureLoader.getSprite("ui/icons/iconAlliance")));

        //EmpireListButton.init();
        //tab.AddPowerButton(EMPIRE_LIST,
        //    PowerButtonCreator.CreateGodPowerButton("empire_list_window",
        //        SpriteTextureLoader.getSprite("ui/icons/iconAttack")));
    }
}
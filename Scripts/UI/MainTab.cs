using System.Collections.Generic;
using EmpireCraft.Scripts.GodPowers;
using EmpireCraft.Scripts.UI.Windows;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.General.UI.Tab;
using NeoModLoader.utils;

namespace EmpireCraft.Scripts.UI;

internal static class MainTab
{
    public const string CREATE_EMPIRE_BUTTON = "create_empire_button";
    public const string EMPIRE_LAYER_TOGGLE = "empire_layer_toggle";
    public const string TITLE_LAYER_TOGGLE = "title_layer_toggle";
    public const string EMPIRE_FORM_BUTTON = "empire_form_button";
    public const string EMPIRE_ENFEOFF_BUTTON = "empire_enfeoff_button";
    public const string EMPIRE_LIST_BUTTON = "empire_list_button";
    public static PowersTab tab;

    public static void Init()
    {
        // Create a tab with id "Example", title key "tab_example", description key "hotkey_tip_tab_other", and icon "ui/icons/iconSteam".
        // 创建一个id为"Example", 标题key为"set_empire_power", 描述key为"hotkey_tip_tab_other", 图标为"ui/icons/iconSteam"的标签页.
        tab = TabManager.CreateTab("EmpireTab", "empire_tab_name", "empire_tab_description",
            SpriteLoadUtils.LoadSingleSprite(ModClass._declare.FolderPath+"/GameResources/TabEmpire.png"));
        // Set the layout of the tab. The layout is a list of strings, each string is a category. Names of each category are not important.
        // 设置标签页的布局. 布局是一个字符串列表, 每个字符串是一个分类. 每个分类的名字不重要.
        tab.SetLayout(new List<string>()
        {
            CREATE_EMPIRE_BUTTON,
            EMPIRE_LAYER_TOGGLE,
            TITLE_LAYER_TOGGLE,
            EMPIRE_ENFEOFF_BUTTON,
            EMPIRE_FORM_BUTTON,
            EMPIRE_LIST_BUTTON
        });
        // Add buttons to the tab.
        // 向标签页添加按钮.
        _addButtons();
        _createWindows();
        // Update the layout of the tab.
        // 更新标签页的布局.
        tab.UpdateLayout();
    }

    private static void _createWindows()
    {
        EmpireListWindow.CreateWindow(nameof(EmpireListWindow),
            nameof(EmpireListWindow) + "Title");
        EmpireWindow.CreateWindow(nameof(EmpireWindow),
            nameof(EmpireWindow) + "Title");
    }

    private static void _addButtons()
    {
        CreateEmpireButton.init();
        tab.AddPowerButton(CREATE_EMPIRE_BUTTON,
            PowerButtonCreator.CreateGodPowerButton("create_empire",
                SpriteTextureLoader.getSprite("ui/icons/iconAlliance")));

        EmpireLayerToggle.init();
        PowerButton pb = FixFunctions.CreateToggleButton("empire_layer",
                 SpriteTextureLoader.getSprite("ui/icons/iconKingdom"));
        pb.name = "empire_layer";
        tab.AddPowerButton(EMPIRE_LAYER_TOGGLE, pb);

        TitleLayerToggle.init();
        pb = FixFunctions.CreateToggleButton("title_layer",
                 SpriteTextureLoader.getSprite("ui/icons/iconCity"));
        tab.AddPowerButton(TITLE_LAYER_TOGGLE, pb);

        EmpireEnfeoffButton.init();
        tab.AddPowerButton(EMPIRE_ENFEOFF_BUTTON, PowerButtonCreator.CreateGodPowerButton("empire_enfeoff",
                SpriteLoadUtils.LoadSingleSprite(ModClass._declare.FolderPath + "/GameResources/SplitAllUnderHeaven.png")));

        EmpireFormButton.init();
        tab.AddPowerButton(EMPIRE_FORM_BUTTON,
            PowerButtonCreator.CreateGodPowerButton("empire_form",
                SpriteLoadUtils.LoadSingleSprite(ModClass._declare.FolderPath + "/GameResources/ChineseCrown.png")));

        tab.AddPowerButton(EMPIRE_LIST_BUTTON, PowerButtonCreator.CreateWindowButton("empire_list", nameof(EmpireListWindow),
            SpriteLoadUtils.LoadSingleSprite(ModClass._declare.FolderPath + "/icon.png")));

    }
}
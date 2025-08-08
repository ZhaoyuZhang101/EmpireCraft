using System.Collections.Generic;
using EmpireCraft.Scripts.GodPowers;
using EmpireCraft.Scripts.UI.Windows;
using NCMS.Utils;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.General.UI.Tab;
using NeoModLoader.utils;

namespace EmpireCraft.Scripts.UI;

internal static class MainTab
{
    public const string KINGDOM_TITLE_GROUP = "kingdom_title_group";
    public const string EMPIRE_GROUP = "empire_layer_group";
    public const string EMPIRE_FUNCTIONS = "empire_function_group";
    public const string PROVINCE_GROUP = "province_group";
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
            EMPIRE_GROUP,
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
        ModLayerWindow.CreateWindow(nameof(ModLayerWindow),
            nameof(ModLayerWindow) + "Title");
    }

    private static void _addButtons()
    {
        ModLayerToggle.init();
        PowerButton pb3 = FixFunctions.CreateToggleButton("ModLayer_layer",
                 SpriteTextureLoader.getSprite("ui/icons/iconCity"));
        tab.AddPowerButton(EMPIRE_GROUP, pb3);

        CreateModLayerButton.init();
        tab.AddPowerButton(EMPIRE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("create_ModLayer",
                SpriteLoadUtils.LoadSingleSprite(ModClass._declare.FolderPath + "/GameResources/TitleCreate.png")));

        AddModLayerButton.init();
        tab.AddPowerButton(EMPIRE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("add_ModLayer",
                SpriteLoadUtils.LoadSingleSprite(ModClass._declare.FolderPath + "/GameResources/TitleAdd.png")));

        RemoveModLayerButton.init();
        tab.AddPowerButton(EMPIRE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("remove_ModLayer",
                SpriteLoadUtils.LoadSingleSprite(ModClass._declare.FolderPath + "/GameResources/TitleRemove.png")));

    }
}
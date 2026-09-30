using System.Collections.Generic;
using System;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.GodPowers;
using EmpireCraft.Scripts.UI.Windows;
using NCMS.Utils;
using NeoModLoader.api;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using NeoModLoader.General.UI.Tab;
using NeoModLoader.services;
using NeoModLoader.utils;
using UnityEngine;

namespace EmpireCraft.Scripts.UI;

internal static class MainTab
{
    public const string KINGDOM_TITLE_GROUP = "kingdom_title_group";
    // 标签页按类别分组(同原版标签页，组与组之间有分隔线)：法理 | 帝国核心 | 帝国 | 文化与理念 | 理念种子 | 显示与工具
    public const string TITLE_GROUP = "title_group";
    public const string EMPIRE_CORE_GROUP = "empire_core_group";
    public const string EMPIRE_GROUP = "empire_layer_group";
    public const string CULTURE_GROUP = "culture_ideology_group";
    public const string IDEOLOGY_SEED_GROUP = "ideology_seed_group";
    public const string DISPLAY_GROUP = "display_tool_group";
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
            TITLE_GROUP,
            EMPIRE_CORE_GROUP,
            EMPIRE_GROUP,
            CULTURE_GROUP,
            IDEOLOGY_SEED_GROUP,
            DISPLAY_GROUP,
        });
        // Add buttons to the tab.
        // 向标签页添加按钮.
        _addButtons();
        _createWindows();
        // Update the layout of the tab.
        // 更新标签页的布局.
        tab.UpdateLayout();
        // 当前加载器版本的 UpdateLayout 把所有按钮排成一整列，不分组也不画分隔线：
        // 在它排好的基础上按组重新排，组与组之间留空并画分隔线(同原版标签页)
        ApplyGroupedLayout();
    }

    private static void _createWindows()
    {
        EmpireListWindow.CreateWindow(nameof(EmpireListWindow),
            nameof(EmpireListWindow) + "Title");
        CultureSpeciesPairWindow.CreateWindow(nameof(CultureSpeciesPairWindow),
            nameof(CultureSpeciesPairWindow) + "Title");
        EmpireWindow.CreateWindow(nameof(EmpireWindow),
            nameof(EmpireWindow) + "Title");
        KingdomTitleWindow.CreateWindow(nameof(KingdomTitleWindow),
            nameof(KingdomTitleWindow) + "Title");
        // Phase 16 加的 CityNameHistoryWindow 当时漏掉了这一步——每一个自定义
        // AutoLayoutWindow<T> 窗口都得在这里调一次 CreateWindow 完成注册（标题文本、
        // WindowLibrary/WindowToolbar 等原版系统需要用到的 WindowAsset 记录都是这一步
        // 建立的），跟有没有单独的顶栏按钮无关。没注册就直接 ScrollWindow.showWindow(...)
        // 打开，原版那些"窗口切换时通知所有监听者"的代码（HoveringBgIconManager.animate、
        // WindowToolbar.toggleShow 等）在自己内部查不到这个窗口 ID 对应的记录，就会各自
        // 抛空引用异常，把窗口内容还没来得及构建的那一步给打断——表现出来就是点了按钮以后
        // 窗口只有个空壳，内容一直是原版兜底的"未找到/开发中"占位页。
        CityNameHistoryWindow.CreateWindow(nameof(CityNameHistoryWindow),
            nameof(CityNameHistoryWindow) + "Title");
        EmpireBeaurauWindow.CreateWindow(nameof(EmpireBeaurauWindow),
            nameof(EmpireBeaurauWindow) + "Title");
        ChangeUnitWindow.CreateWindow(nameof(ChangeUnitWindow),
            nameof(ChangeUnitWindow) + "Title");
        SpecificClanWindow.CreateWindow(nameof(SpecificClanWindow),
            nameof(SpecificClanWindow) + "Title");
        EmpireSettingWindow.CreateWindow(nameof(EmpireSettingWindow),
            nameof(EmpireSettingWindow) + "Title");
        RegimeWindow.CreateWindow(nameof(RegimeWindow),
            "");
        SpecificClanListWindow.CreateWindow(nameof(SpecificClanListWindow),
            nameof(SpecificClanListWindow) + "Title");
        FactionDetailWindow.CreateWindow(nameof(FactionDetailWindow),
            "");
        TraitsSelectWindow.CreateWindow(nameof(TraitsSelectWindow),
            nameof(TraitsSelectWindow) + "Title");
        AddFactionMemberWindow.CreateWindow(nameof(AddFactionMemberWindow),
            nameof(AddFactionMemberWindow) + "Title");
        AddFactionWindow.CreateWindow(nameof(AddFactionWindow),
            nameof(AddFactionWindow) + "Title");
        EmpireHistoryWindow.CreateWindow(nameof(EmpireHistoryWindow),
            "empire_history");
        EmpireCoreWindow.CreateWindow(nameof(EmpireCoreWindow),
            "EmpireCoreWindowTitle");
        BugReportWindow.CreateWindow(nameof(BugReportWindow),
            "bug_report_window_title");
        OnlineUpdateWindow.CreateWindow(nameof(OnlineUpdateWindow),
            "online_update_window_title");
        // InstitutionWindow 换成了 AbstractWideWindow(真宽窗口)，注册方式跟其它
        // AutoLayoutWindow<T> 窗口不一样：CreateAndInit 只认 (窗口id, 尺寸)，标题固定用
        // "<窗口id> Title"(带空格)这个约定 key，不能像 CreateWindow 那样自己传 titleKey。
        InstitutionWindow.CreateAndInit(nameof(InstitutionWindow), new Vector2(480f, 380f));
        // 族谱树是单独的宽窗口，标题固定用 "<窗口id> Title" 约定 key(见上面 InstitutionWindow)。
        SpecificClanTreeWindow.CreateAndInit(nameof(SpecificClanTreeWindow), new Vector2(500f, 400f));
        // 模组文化窗口（点击文化图层/原版文化链接时打开，见 CultureWindowRedirectPatch）
        CultureInfoWindow.CreateAndInit(nameof(CultureInfoWindow), new Vector2(480f, 380f));
        IdeologyInfoWindow.CreateAndInit(nameof(IdeologyInfoWindow), new Vector2(480f, 380f));
        TechWindow.CreateAndInit(nameof(TechWindow), new Vector2(480f, 380f));
        CultureBooksWindow.CreateAndInit(nameof(CultureBooksWindow), new Vector2(480f, 380f));
    }
    [Hotfixable]
    private static void _addButtons()
    {
        // ---- 法理：图层与法理、省份的增删 ----
        PowerButton pb0 = FixFunctions.CreateLayerButton(MetaTypeExtension.KingdomTitle,
                 SpriteTextureLoader.getSprite("ui/icons/iconToolTitleLayer.png"));
        AddButton(TITLE_GROUP, pb0);

        CreateTitleButton.init();
        AddButton(TITLE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("create_title",
                  SpriteTextureLoader.getSprite("ui/icons/iconToolTitleCreate.png")));

        AddTitleButton.init();
        AddButton(TITLE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("add_title",
                SpriteTextureLoader.getSprite("ui/icons/iconToolTitleAdd.png")));

        RemoveTitleButton.init();
        AddButton(TITLE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("remove_title",
                SpriteTextureLoader.getSprite("ui/icons/iconToolTitleRemove.png")));

        CreateProvinceButton.init();
        AddButton(TITLE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("create_province",
                SpriteTextureLoader.getSprite("ui/icons/iconToolProvinceCreate.png")));

        AddProvinceButton.init();
        AddButton(TITLE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("add_province",
                SpriteTextureLoader.getSprite("ui/icons/iconToolProvinceAdd.png")));

        RemoveProvinceButton.init();
        AddButton(TITLE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("remove_province",
                SpriteTextureLoader.getSprite("ui/icons/iconToolProvinceRemove.png")));

        // ---- 帝国核心 ----
        CreateEmpireCoreButton.init();
        AddButton(EMPIRE_CORE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("create_empire_core",
                SpriteTextureLoader.getSprite("ui/icons/iconToolCoreCreate.png")));

        AddTitleToEmpireCoreButton.init();
        AddButton(EMPIRE_CORE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("add_title_to_empire_core",
                SpriteTextureLoader.getSprite("ui/icons/iconToolCoreAddTitle.png")));

        RemoveTitleFromEmpireCoreButton.init();
        AddButton(EMPIRE_CORE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("remove_title_from_empire_core",
                SpriteTextureLoader.getSprite("ui/icons/iconToolCoreRemoveTitle.png")));

        DestroyEmpireCoreButton.init();
        AddButton(EMPIRE_CORE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("destroy_empire_core",
                SpriteTextureLoader.getSprite("ui/icons/iconToolCoreDestroy.png")));
        
        // ---- 帝国：图层、称帝与解散、分封、列表、人物建国 ----
        PowerButton pb = FixFunctions.CreateLayerButton(MetaTypeExtension.Empire,
                 SpriteTextureLoader.getSprite("ui/icons/iconKingdom"));
        AddButton(EMPIRE_GROUP, pb);

        CreateEmpireButton.init();
        AddButton(EMPIRE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("create_empire",
                SpriteTextureLoader.getSprite("ui/icons/iconAlliance")));

        EmpireFormButton.init();
        AddButton(EMPIRE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("empire_form",
                SpriteLoadUtils.LoadSingleSprite(ModClass._declare.FolderPath + "/GameResources/ChineseCrown.png")));


        RemoveEmpireButton.init();
        AddButton(EMPIRE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("remove_empire",
                SpriteLoadUtils.LoadSingleSprite(ModClass._declare.FolderPath + "/GameResources/ChineseCrown_remove.png")));

        EmpireEnfeoffButton.init();
        AddButton(EMPIRE_GROUP, PowerButtonCreator.CreateGodPowerButton("empire_enfeoff",
                SpriteLoadUtils.LoadSingleSprite(ModClass._declare.FolderPath + "/GameResources/SplitAllUnderHeaven.png")));

        //帝国势力列表
        var empireListButon = PowerButtonCreator.CreateWindowButton("empire_list", nameof(EmpireListWindow),
            SpriteLoadUtils.LoadSingleSprite(ModClass._declare.FolderPath + "/icon.png"));
        AddButton(EMPIRE_GROUP, empireListButon);
        empireListButon._button.OnHover(() =>
        {
            Tooltip.show(empireListButon,"normal", new TooltipData()
            {
                tip_name = "show_empire_list",
                tip_description = "show_empire_list_description"
            });
        });
        empireListButon._button.OnHoverOut(Tooltip.hideTooltip);
        
        //宗族列表
        var specificClanListButton = PowerButtonCreator.CreateWindowButton("specific_clan_list",
            nameof(SpecificClanListWindow),
            SpriteLoadUtils.LoadSingleSprite(ModClass._declare.FolderPath + "/GameResources/ui/specificClanIcon.png"));
        specificClanListButton._button.OnHover(() =>
            {
                Tooltip.show(specificClanListButton,"normal", new TooltipData()
                {
                    tip_name = "show_specific_clan_list",
                    tip_description = "show_specific_clan_list_description"
                });
            });
        specificClanListButton._button.OnHoverOut(Tooltip.hideTooltip);
        AddButton(EMPIRE_GROUP, specificClanListButton);
        
        ActorCreateKingdom.init();
        AddButton(EMPIRE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("actor_create_kingdom",
               SpriteTextureLoader.getSprite("ui/icons/iconKingdom")));
        
        // ---- 文化与理念：图层与文化配置 ----
        // 原版文化图层的第二个入口：复用同一个 GodPower/OptionAsset，因此这里与
        // 原版按钮的区域模式、边框和名称开关始终同步。
        PowerButton cultureLayer = FixFunctions.CloneExistingLayerButton(MetaType.Culture,
                 SpriteTextureLoader.getSprite("ui/icons/iconCulture"));
        if (cultureLayer != null) AddButton(CULTURE_GROUP, cultureLayer);

        // 理念图层(独立于原版宗教图层)
        PowerButton ideologyLayer = FixFunctions.CreateLayerButton(MetaTypeExtension.Ideology,
                 SpriteTextureLoader.getSprite("ui/icons/iconBooks"));
        AddButton(CULTURE_GROUP, ideologyLayer);

        var cultureConfigButton = PowerButtonCreator.CreateWindowButton("culture_list", nameof(CultureSpeciesPairWindow),
            SpriteTextureLoader.getSprite("ui/icons/iconCulture"));
        AddButton(CULTURE_GROUP, cultureConfigButton);
        cultureConfigButton._button.OnHover(() =>
        {
            Tooltip.show(cultureConfigButton,"normal", new TooltipData()
            {
                tip_name = "show_culture_config",
                tip_description = "show_culture_config_description"
            });
        });
        cultureConfigButton._button.OnHoverOut(Tooltip.hideTooltip);

        // ---- 理念种子 ----
        IdeologyTraitIcons.Register();
        IdeologySeedPower.Init();
        foreach (GeneralSystems.PartyIdeology ideology in Enum.GetValues(typeof(GeneralSystems.PartyIdeology)))
        {
            string powerId = IdeologySeedPower.Id(ideology);
            PowerButton seedButton = PowerButtonCreator.CreateGodPowerButton(powerId,
                SpriteTextureLoader.getSprite(IdeologyTraitIcons.Path(ideology)));
            // NML 从原版 inspect 按钮克隆 GodPower 按钮。当前游戏版本的预制体自带
            // TipButton，若不覆盖就会一直显示它继承来的 "Normal Tooltip"。
            TipButton tip = seedButton?.GetComponent<TipButton>();
            if (tip != null)
            {
                tip.textOnClick = powerId;
                tip.textOnClickDescription = powerId + "_description";
                tip.text_description_2 = "";
                tip.type = "normal";
            }
            AddButton(IDEOLOGY_SEED_GROUP, seedButton);
        }

        // ---- 显示与工具：显示开关、调试、反馈与更新 ----
        SwitchRealNumButton.init();
        PowerButton pb4 = PowerButtonCreator.CreateToggleButton("real_num",
            SpriteTextureLoader.getSprite("ui/realNumToggle"));
        AddButton(DISPLAY_GROUP, pb4);

        SwitchSimpleNameplateButton.init();
        PowerButton simpleNameplateButton = PowerButtonCreator.CreateToggleButton("simple_nameplate",
            SpriteTextureLoader.getSprite("ui/icons/iconHideUI"));
        AddButton(DISPLAY_GROUP, simpleNameplateButton);

        // 帝国视图下是否叠加显示同盟
        SwitchEmpireAllianceButton.init();
        PowerButton empireAllianceButton = PowerButtonCreator.CreateToggleButton("empire_show_alliance",
            SpriteTextureLoader.getSprite("plots/icons/plot_alliance_create"));
        AddButton(DISPLAY_GROUP, empireAllianceButton);

        DebugFrontLineButton.init();
        AddButton(DISPLAY_GROUP,
            PowerButtonCreator.CreateGodPowerButton("debug_frontline",
                SpriteTextureLoader.getSprite("ui/icons/iconWar")));

        var bugReportButton = PowerButtonCreator.CreateWindowButton("bug_report_button", nameof(BugReportWindow),
            GetOriginalBugIcon());
        AddButton(DISPLAY_GROUP, bugReportButton);
        bugReportButton._button.OnHover(() =>
        {
            Tooltip.show(bugReportButton, "normal", new TooltipData
            {
                tip_name = "bug_report_button",
                tip_description = "bug_report_button_description"
            });
        });
        bugReportButton._button.OnHoverOut(Tooltip.hideTooltip);

        Sprite updateIcon = SpriteTextureLoader.getSprite("ui/icons/iconDownload") ??
                            SpriteTextureLoader.getSprite("ui/icons/iconSteam");
        var updateButton = PowerButtonCreator.CreateWindowButton(
            "online_update_button",
            nameof(OnlineUpdateWindow),
            updateIcon);
        AddButton(DISPLAY_GROUP, updateButton);
        updateButton._button.OnHover(() =>
        {
            Tooltip.show(updateButton, "normal", new TooltipData
            {
                tip_name = "online_update_button",
                tip_description = "online_update_button_description"
            });
        });
        updateButton._button.OnHoverOut(Tooltip.hideTooltip);
    }

    // 各组的按钮(按加入顺序)，用于自行分组排版
    private static readonly List<KeyValuePair<string, PowerButton>> _buttonsInOrder = new();

    private static void AddButton(string group, PowerButton button)
    {
        tab.AddPowerButton(group, button);
        if (button != null) _buttonsInOrder.Add(new KeyValuePair<string, PowerButton>(group, button));
    }

    // 原版 PowersTab.sortButtons 按子物体顺序排版(子物体数量变化时重排)，会覆盖任何手动坐标；
    // 但它认得名字以 "_line" 开头的子物体：遇到就收尾当前列、画一条分隔线再继续。
    // 所以这里按组调整子物体顺序，并在组与组之间插入一条 "_line" 分隔线，交给原版排版
    private static void ApplyGroupedLayout()
    {
        try
        {
            Transform parent = tab.transform;
            int index = int.MaxValue;
            foreach (var pair in _buttonsInOrder)
                if (pair.Value != null && pair.Value.transform.parent == parent)
                    index = Mathf.Min(index, pair.Value.transform.GetSiblingIndex());
            if (index == int.MaxValue) return;
            GameObject template = FindVanillaLine();
            string current = null;
            int lineCount = 0;
            foreach (var pair in _buttonsInOrder)
            {
                if (pair.Value == null || pair.Value.transform.parent != parent) continue;
                if (current != null && pair.Key != current)
                {
                    GameObject line = CreateLine(parent, template, lineCount++);
                    line.transform.SetSiblingIndex(index++);
                }
                current = pair.Key;
                pair.Value.transform.SetSiblingIndex(index++);
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 标签页分组排版失败: {exception.Message}");
        }
    }

    // 从原版标签页里找一条现成的分隔线做模板，外观与原版一致
    private static GameObject FindVanillaLine()
    {
        foreach (PowersTab other in Resources.FindObjectsOfTypeAll<PowersTab>())
        {
            if (other == null || other == tab) continue;
            foreach (Transform child in other.transform)
                if (child.name.StartsWith("_line")) return child.gameObject;
        }
        return null;
    }

    private static GameObject CreateLine(Transform parent, GameObject template, int number)
    {
        GameObject line;
        if (template != null)
        {
            line = UnityEngine.Object.Instantiate(template, parent);
        }
        else
        {
            line = new GameObject("_line", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
            line.transform.SetParent(parent, false);
            var image = line.GetComponent<UnityEngine.UI.Image>();
            image.color = new Color(0.16f, 0.14f, 0.12f, 0.9f);
            image.raycastTarget = false;
            line.GetComponent<RectTransform>().sizeDelta = new Vector2(2f, 48f);
        }
        line.name = $"_line_empirecraft_{number}";
        var lineImage = line.GetComponent<UnityEngine.UI.Image>();
        if (lineImage != null) lineImage.enabled = true;
        line.SetActive(true);
        return line;
    }

    private static Sprite GetOriginalBugIcon()
    {
        Sprite originalIcon = SpriteTextureLoader.getSprite("ui/icons/iconDebug");
        if (originalIcon != null) return originalIcon;

        string[] exactNames = { "iconDebug", "iconBug", "icon_bug", "bug" };
        Sprite[] sprites = Resources.FindObjectsOfTypeAll<Sprite>();
        foreach (string name in exactNames)
        {
            foreach (Sprite sprite in sprites)
            {
                if (string.Equals(sprite.name, name, StringComparison.OrdinalIgnoreCase)) return sprite;
            }
        }
        return SpriteTextureLoader.getSprite("ui/icons/iconInfo");
    }
}

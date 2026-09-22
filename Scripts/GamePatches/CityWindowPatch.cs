using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.UI.Components;
using EmpireCraft.Scripts.UI.Windows;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Data;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.GamePatches;
public class CityWindowPatch : GamePatch
{
    public ModDeclare declare { get; set; }
    public static CityWindow _window { get; set; }
    public static SimpleButton limitToggle { get; set; }
    public static TextInput limitInput { get; set; }


    public void Initialize()
    {
        new Harmony(nameof(startShowingWindow)).Patch(
            AccessTools.Method(typeof(CityWindow), nameof(CityWindow.startShowingWindow)),
            postfix: new HarmonyMethod(GetType(), nameof(startShowingWindow))
        );
        new Harmony(nameof(showStatsRows)).Patch(
            AccessTools.Method(typeof(CityWindow), nameof(CityWindow.showStatsRows)),
            prefix: new HarmonyMethod(GetType(), nameof(showStatsRows))
        );
    }
    public static bool showStatsRows(CityWindow __instance)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        City metaObject = __instance.meta_object;
        if (metaObject == null)
            return false;
        if (metaObject.kingdom.isNeutral())
            __instance.village_title.setKeyAndUpdate("village_dying");
        else
            __instance.village_title.setKeyAndUpdate("village");
        __instance.tryShowPastNames();
        __instance.showStatRow("founded", (object) metaObject.getFoundedDate(), MetaType.None, -1L, "iconAge", (string) null, (TooltipDataGetter) null);
        __instance.tryToShowActor("founder", metaObject.data.founder_id, metaObject.data.founder_name, pIconPath: "actor_traits/iconStupid");
        __instance.tryShowPastRulers();
        __instance.tryToShowActor("village_statistics_leader", pObject: metaObject.leader, pIconPath: "iconLeaders");
        if (metaObject.hasLeader())
            __instance.showStatRow("ruler_money", (object) metaObject.GetMoney(), "#43FF43", pIconPath: "iconMoney");
        CityValueSnapshot cityValue = metaObject.GetCityStrategicValue();
        string cityValueText = $"{cityValue.Total}/100" +
                               (cityValue.IsIsolated ? $" ({LM.Get("city_value_isolated")})" : "");
        __instance.showStatRow("city_value", cityValueText, "#FFD34E", pIconPath: "iconKings");
        __instance.showStatRow("tax", (object) metaObject.kingdom.GetTaxRate().ToString("0%"), "#43FF43", pIconPath: "kingdom_traits/kingdom_trait_tax_rate_local_low");
        __instance.showStatRow("tribute", (object) metaObject.kingdom.GetTaxRate().ToString("0%"), "#43FF43", pIconPath: "kingdom_traits/kingdom_trait_tax_rate_tribute_high");
        __instance.tryToShowActor("king", pObject: metaObject.kingdom.king, pIconPath: "iconKings");
        __instance.tryToShowMetaSpecies("founder_species", metaObject.getFounderSpecies()?.id);
        return false;
    }
    public static void startShowingWindow(CityWindow __instance)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return;
        _window = __instance;
        AddSettingTab();
    }

    public static void AddSettingTab()
    {
        City city = SelectedMetas.selected_city;
        Transform space = _window.tabs.transform.Find("space (1)");
        if (space != null) 
        {
            GameObject.Destroy(space.gameObject);
        }
        if (!_window.tabs._tabs.Any(p => p.name == "city_setting"))
        {
            SimpleWindowTab simpleWindowTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
            simpleWindowTab.Setup("city_setting", _window.scroll_window, CreateCitySettingContent());
            if (city.GetMaxPopulationLimitStats())
            {
                limitToggle.Text.text = LM.Get("On");
                limitInput.input.text = city.GetMaxPopulation().ToString();
            }
            else
            {
                limitToggle.Text.text = LM.Get("Off");
                limitInput.input.text = LM.Get("population_limit_text"); 
            }
        }
        else
        {
            if (city.GetMaxPopulationLimitStats())
            {
                limitToggle.Text.text = LM.Get("On");
                limitInput.input.text = city.GetMaxPopulation().ToString();
            }
            else
            {
                limitToggle.Text.text = LM.Get("Off");
                limitInput.input.text = LM.Get("population_limit_text");
            }
        }

    }

    [Hotfixable]
    public static List<Transform> CreateCitySettingContent()
    {
        City city = SelectedMetas.selected_city;
        List<Transform> city_setting_contents = new List<Transform>();

        // 创建标题
        SimpleText title = GameObject.Instantiate(SimpleText.Prefab);
        title.Setup(LM.Get("population_limit_title"), TextAnchor.MiddleCenter, new Vector2(180, 30));
        title.background.enabled = false;
        // 强制标题大小
        var titleLayout = title.gameObject.AddComponent<LayoutElement>();
        titleLayout.minWidth = titleLayout.preferredWidth = 180;
        titleLayout.minHeight = titleLayout.preferredHeight = 30;
        title.transform.localScale = Vector3.one;


        RectTransform hori = UIHelper.CreateHorizontalLayoutContainer("hori", new Vector2(300, 40));
        hori.transform.localScale = Vector3.one;
        var horiLayout = hori.gameObject.AddComponent<LayoutElement>();
        horiLayout.minWidth = horiLayout.preferredWidth = 300;
        horiLayout.minHeight = horiLayout.preferredHeight = 40;
        // 创建开关
        limitToggle = GameObject.Instantiate(SimpleButton.Prefab);

        // 强制开关大小
        var limitToggleLayout = limitToggle.gameObject.AddComponent<LayoutElement>();
        limitToggleLayout.minWidth = limitToggleLayout.preferredWidth = 70;
        limitToggleLayout.minHeight = limitToggleLayout.preferredHeight = 30;
        limitToggle.transform.SetParent(hori.transform);

        // 创建输入框
        limitInput = GameObject.Instantiate(TextInput.Prefab);
        limitInput.Setup(city.GetMaxPopulationLimitStats()? city.GetMaxPopulation().ToString(): LM.Get("population_limit_text"), newValue => InputCityPopLimit(newValue, limitInput));
        limitInput.SetSize(new Vector2(210, 30));
        limitInput.transform.localScale = Vector3.one;
        limitToggle.Setup(() => LimitationToggleSwitch(limitToggle, limitInput), SpriteTextureLoader.getSprite("ui/buttonToggleIndicator_1"), city.GetMaxPopulationLimitStats() ? LM.Get("On") : LM.Get("Off"), pSize: new Vector2(65, 30));
        // 强制输入框大小
        var inputLayout = limitInput.gameObject.AddComponent<LayoutElement>();
        inputLayout.minWidth = inputLayout.preferredWidth = 210;
        inputLayout.minHeight = inputLayout.preferredHeight = 30;
        limitInput.transform.SetParent(hori.transform);

        city_setting_contents.Add(title.transform);
        city_setting_contents.Add(hori.transform);

        // 城市改名 + 文化地名历史不再直接摊在设置面板里了（之前那版每次点开设置都要
        // 把整份可编辑列表都画出来，用户明确要求挪到单独的窗口）。这里只留一个入口
        // 按钮，点了以后按跟法理窗口"文化地名历史"标签页同一套规范打开的独立窗口
        // （CityNameHistoryWindow：文化标签 + 可编辑输入框 + 删除按钮，一行一条）。
        SimpleText nameHistoryTitle = GameObject.Instantiate(SimpleText.Prefab);
        nameHistoryTitle.Setup(LM.Get("city_setting_name_history_title"), TextAnchor.MiddleCenter, new Vector2(180, 30));
        nameHistoryTitle.background.enabled = false;
        var nameHistoryTitleLayout = nameHistoryTitle.gameObject.AddComponent<LayoutElement>();
        nameHistoryTitleLayout.minWidth = nameHistoryTitleLayout.preferredWidth = 180;
        nameHistoryTitleLayout.minHeight = nameHistoryTitleLayout.preferredHeight = 30;
        nameHistoryTitle.transform.localScale = Vector3.one;

        SimpleButton openNameHistoryButton = GameObject.Instantiate(SimpleButton.Prefab);
        openNameHistoryButton.Setup(OpenCityNameHistoryWindow, SpriteTextureLoader.getSprite("ui/icons/iconCulture"),
            LM.Get("city_setting_open_name_history"), pSize: new Vector2(200, 30));
        openNameHistoryButton.transform.localScale = Vector3.one;
        var openNameHistoryButtonLayout = openNameHistoryButton.gameObject.AddComponent<LayoutElement>();
        openNameHistoryButtonLayout.minWidth = openNameHistoryButtonLayout.preferredWidth = 200;
        openNameHistoryButtonLayout.minHeight = openNameHistoryButtonLayout.preferredHeight = 30;

        city_setting_contents.Add(nameHistoryTitle.transform);
        city_setting_contents.Add(openNameHistoryButton.transform);

        return city_setting_contents;
    }

    public static void OpenCityNameHistoryWindow()
    {
        City city = SelectedMetas.selected_city;
        if (city == null) return;
        ScrollWindow.showWindow(nameof(CityNameHistoryWindow));
    }

    public static void InputCityPopLimit(string pName, TextInput textInput)
    {
        City city = SelectedMetas.selected_city;
        int limitNum = int.TryParse(pName, out int num) ? num : -1;
        if (city == null) return;
        bool is_open = city.GetMaxPopulationLimitStats();
        if (is_open && limitNum > 0)
        {
            city.SetMaxPopulation(limitNum);
            textInput.input.text = limitNum.ToString();
        }
        else
        {
            textInput.input.text = city.GetMaxPopulation().ToString();
        }
    }

    public static void LimitationToggleSwitch(SimpleButton button, TextInput textInput)
    {
        City city = SelectedMetas.selected_city;
        bool limitationStats = city.GetMaxPopulationLimitStats();
        if (limitationStats) 
        {
            button.Text.text = LM.Get("Off");
            city.CloseMaxPopulationLimit();
            textInput.input.text = LM.Get("population_limit_text");
        } else
        {
            button.Text.text = LM.Get("On");
            city.OpenMaxPopulationLimit();
            textInput.input.text = city.GetMaxPopulation().ToString();
        }
    }
}

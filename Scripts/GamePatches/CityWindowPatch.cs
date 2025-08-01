using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.UI.Components;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
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
    }

    public static void startShowingWindow(CityWindow __instance)
    {
        _window = __instance;
        AddSettingTab();
    }

    public static void AddSettingTab()
    {
        City city = Config.selected_city;
        Transform space = _window.tabs.transform.Find("space (1)");
        if (space != null) 
        {
            GameObject.Destroy(space.gameObject);
        }
        if (!_window.tabs._tabs.Any(p => p.name == "city_setting"))
        {
            SimpleWindowTab simpleWindowTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
            simpleWindowTab.Setup("city_setting", _window, CreateCitySettingContent());
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
        City city = Config.selected_city;
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

        return city_setting_contents;
    }                                                                                   
    public static void InputCityPopLimit(string pName, TextInput textInput)
    {
        City city = Config.selected_city;
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
        City city = Config.selected_city;
        bool limitationStats = city.GetMaxPopulationLimitStats();
        if (limitationStats) 
        {
            button.Text.text = LM.Get("Off");
            city.CloseMaxPopulationLimit();
            LogService.LogInfo("关闭人口限制");
            textInput.input.text = LM.Get("population_limit_text");
        } else
        {
            button.Text.text = LM.Get("On");
            city.OpenMaxPopulationLimit();
            LogService.LogInfo("开启人口限制");
            textInput.input.text = city.GetMaxPopulation().ToString();
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows;

// 模组设置(「卷册」宽窗口)：加载器模组列表里点本模组的「设置」打开这里(见 ModSettingsPatch)，代替加载器的统一清单。
//   · 左栏：模组名与版本 → 五个分类(画面与性能、帝国与天下、法理、战争与乱世、城市与营建，各带项数) → 说明、恢复本类默认、在线更新；
//   · 右栏：页头(分类名 · 项数 · 已开) → 主指标(本类最要紧的几项当前值) → 分组卡片，一行一项：开关行点整行切换，数值行加减或直接输入；
//   · 改动当场调设置回调并存盘(与加载器窗口关上时的效果一样)；纯净地图这类标"重启生效"。
// 设置项仍是 default_config.json 的 Default 组，分类只在这里排：不改组名，免得已存的设置读不回来。
// 新加设置项：default_config.json 加一项，再在下面 Categories 里挂到某张卡上(没挂上的不显示)。
public class ModSettingsWindow : WideScrollWindow<ModSettingsWindow>
{
    private const string ConfigGroup = "Default";

    private enum Kind { Switch, Number }

    private sealed class Setting
    {
        public string Id;
        public Kind Kind;
        public bool DefaultOn;
        public int DefaultValue, Min, Max, Step;
        // 单位格式键("{0} 年")，空 = 只写数字；Zero = 0 的说法键(不限、永不)
        public string Unit, Zero;
        public bool Restart;
        // 归进"其他"的项不知道默认值，恢复默认时跳过
        public bool HasDefault = true;
    }

    private sealed class Card
    {
        public string Key, Icon;
        public Setting[] Items;
    }

    private sealed class Category
    {
        public string Key, Icon;
        public string[] Headline;
        public Card[] Cards;
    }

    private static Setting Switch(string id, bool on, bool restart = false) =>
        new() { Id = id, Kind = Kind.Switch, DefaultOn = on, Restart = restart };

    private static Setting Number(string id, int value, int min, int max, int step, string unit = null, string zero = null) =>
        new() { Id = id, Kind = Kind.Number, DefaultValue = value, Min = min, Max = max, Step = step, Unit = unit, Zero = zero };

    // 取值范围与 ConfigActions 里各回调的夹取一致
    private static readonly Category[] Categories =
    {
        new()
        {
            Key = "mod_settings_cat_display", Icon = "ui/icons/iconSpeed",
            Headline = new[] { "pure_map_toggle", "smooth_borders_toggle", "performance_stable_framerate_toggle" },
            Cards = new[]
            {
                new Card
                {
                    Key = "mod_settings_card_map", Icon = "ui/icons/iconKingdomZones", Items = new[]
                    {
                        Switch("pure_map_toggle", true, restart: true),
                        Switch("smooth_borders_toggle", true),
                        Switch("performance_stable_framerate_toggle", false),
                    }
                },
                new Card
                {
                    Key = "mod_settings_card_speed", Icon = "ui/icons/iconSpeed", Items = new[]
                    {
                        Switch("performance_high_population_toggle", true),
                        Switch("performance_adaptive_throughput_toggle", true),
                        Switch("performance_hidden_visuals_toggle", true),
                        Switch("performance_nameplate_overlap_toggle", true),
                        Switch("performance_diagnostics_toggle", false),
                    }
                },
                new Card
                {
                    Key = "mod_settings_card_save", Icon = "ui/icons/iconSaveLocal", Items = new[]
                    {
                        Switch("save_freeze_toggle_Text", false),
                    }
                },
            }
        },
        new()
        {
            Key = "mod_settings_cat_empire", Icon = "ui/icons/iconCrown",
            Headline = new[] { "Feudal_Union_Realm_Limit_Text", "Same_Culture_Empire_Limit_Text", "realm_name_rules_toggle" },
            Cards = new[]
            {
                new Card
                {
                    Key = "mod_settings_card_names", Icon = "ui/icons/iconCrown", Items = new[]
                    {
                        Switch("realm_name_rules_toggle", true),
                        Switch("multi_capital_toggle", true),
                    }
                },
                new Card
                {
                    Key = "mod_settings_card_rise", Icon = "ui/icons/iconKnowledge", Items = new[]
                    {
                        Switch("kingdom_institution_reforms_toggle", true),
                        Switch("hre_dissolution_toggle", true),
                    }
                },
                new Card
                {
                    Key = "mod_settings_card_limits", Icon = "ui/icons/iconKingdom", Items = new[]
                    {
                        Number("Feudal_Union_Realm_Limit_Text", 3, 1, 99, 1, "mod_settings_unit_realms"),
                        Number("Same_Culture_Empire_Limit_Text", 2, 1, 99, 1, "mod_settings_unit_empires"),
                    }
                },
            }
        },
        new()
        {
            Key = "mod_settings_cat_title", Icon = "ui/icons/iconTitleLayer",
            Headline = new[] { "Title_Freeze_Toggle", "Title_Been_Destroy_Text", "Title_Max_Cities_Text" },
            Cards = new[]
            {
                new Card
                {
                    Key = "mod_settings_card_title", Icon = "ui/icons/iconTitleLayer", Items = new[]
                    {
                        Switch("Title_Freeze_Toggle", true),
                        Number("Title_Been_Destroy_Text", 50, 1, 9999, 10, "mod_settings_unit_years"),
                        Number("Title_Max_Cities_Text", 3, 0, 999, 1, "mod_settings_unit_cities", "mod_settings_unlimited"),
                        Number("Title_Share_Limit_Text", 2, 1, 99, 1, "mod_settings_unit_shares"),
                        Number("Core_Share_Limit_Text", 5, 1, 99, 1, "mod_settings_unit_shares"),
                        Number("Tianxia_Share_Limit_Text", 2, 1, 99, 1, "mod_settings_unit_shares"),
                    }
                },
            }
        },
        new()
        {
            Key = "mod_settings_cat_war", Icon = "ui/icons/iconWar",
            Headline = new[] { "war_end_year_Text", "Warlord_Era_Years_Text" },
            Cards = new[]
            {
                new Card
                {
                    Key = "mod_settings_card_war", Icon = "ui/icons/iconWar", Items = new[]
                    {
                        Number("war_end_year_Text", 10, 1, 9999, 5, "mod_settings_unit_years"),
                        Number("Warlord_Era_Years_Text", 200, 0, 9999, 10, "mod_settings_unit_years", "mod_settings_never"),
                    }
                },
            }
        },
        new()
        {
            Key = "mod_settings_cat_city", Icon = "ui/icons/iconCity",
            Headline = new[] { "Settlement_Base_Gold_Text", "Settlement_Target_Households_Text", "Settlement_Cooldown_Months_Text" },
            Cards = new[]
            {
                new Card
                {
                    Key = "mod_settings_card_city", Icon = "ui/icons/iconCityZones", Items = new[]
                    {
                        Number("City_Max_Zones_Text", 100, 0, 99999, 10, "mod_settings_unit_zones", "mod_settings_unlimited"),
                        Number("Barren_Zone_Multiplier_Text", 2, 1, 10, 1, "mod_settings_unit_times"),
                    }
                },
                new Card
                {
                    Key = "mod_settings_card_cost", Icon = "ui/icons/iconMoney", Items = new[]
                    {
                        Number("Settlement_Base_Gold_Text", 200, 50, 100000, 50, "mod_settings_unit_gold"),
                        Number("Settlement_Per_City_Gold_Text", 25, 0, 100000, 5, "mod_settings_unit_gold"),
                        Number("Settlement_Reserve_Gold_Text", 200, 0, 100000, 50, "mod_settings_unit_gold"),
                        Number("Settlement_Wood_Text", 20, 1, 100000, 5),
                        Number("Settlement_Stone_Text", 10, 1, 100000, 5),
                    }
                },
                new Card
                {
                    Key = "mod_settings_card_migrants", Icon = "ui/icons/iconFamily", Items = new[]
                    {
                        Number("Settlement_Min_Households_Text", 10, 10, 1000, 5, "mod_settings_unit_households"),
                        Number("Settlement_Target_Households_Text", 20, 10, 1000, 5, "mod_settings_unit_households"),
                        Number("Settlement_Retain_Households_Text", 30, 30, 1000, 5, "mod_settings_unit_households"),
                        Number("Settlement_Migration_Percent_Text", 25, 1, 25, 1, "mod_settings_unit_percent"),
                    }
                },
                new Card
                {
                    Key = "mod_settings_card_timing", Icon = "ui/icons/iconClock", Items = new[]
                    {
                        Number("Settlement_Food_Years_Text", 2, 1, 20, 1, "mod_settings_unit_years"),
                        Number("Settlement_Preparation_Months_Text", 6, 1, 120, 1, "mod_settings_unit_months"),
                        Number("Settlement_Cooldown_Months_Text", 36, 1, 1200, 6, "mod_settings_unit_months"),
                    }
                },
            }
        },
    };

    private int _category;
    // 恢复默认要点两下：第一下按钮变成"再点一次确认"
    private bool _confirmReset;
    // 这次画的分类：固定的五类，加上没排进分类的设置项归成的"其他"(有才加)
    private readonly List<Category> _categories = new();

    // 加载器的「设置」按钮走这里(见 ModSettingsPatch)；窗口没建好时返回 false，交回加载器自己的窗口
    public static bool Open()
    {
        if (Instance == null || ModClass.modConfig == null) return false;
        ScrollWindow.showWindow(WindowId);
        return true;
    }

    protected override void OnOpen() => _confirmReset = false;

    // 以后 default_config.json 新加了设置项却没排进上面的分类，也不能看不到：开关、整数两类归进"其他"，没有默认值可恢复
    protected override void Prepare()
    {
        _categories.Clear();
        _categories.AddRange(Categories);
        var known = new HashSet<string>(Categories.SelectMany(category => category.Cards)
            .SelectMany(card => card.Items).Select(setting => setting.Id));
        var others = new List<Setting>();
        foreach (ModConfigItem item in ConfigItems())
        {
            if (item == null || known.Contains(item.Id)) continue;
            if (item.Type == ConfigItemType.SWITCH)
                others.Add(new Setting { Id = item.Id, Kind = Kind.Switch, HasDefault = false });
            else if (item.Type == ConfigItemType.TEXT && int.TryParse(item.TextVal, out _))
                others.Add(new Setting { Id = item.Id, Kind = Kind.Number, Min = 0, Max = 999999, Step = 1, HasDefault = false });
        }
        if (others.Count > 0)
            _categories.Add(new Category
            {
                Key = "mod_settings_cat_other", Icon = "ui/icons/iconOptions", Headline = Array.Empty<string>(),
                Cards = new[] { new Card { Key = "mod_settings_card_other", Icon = "ui/icons/iconOptions", Items = others.ToArray() } }
            });
        _category = Mathf.Clamp(_category, 0, _categories.Count - 1);
    }

    private Category Current => _categories[_category];

    // ───────── 左栏 ─────────
    protected override void DrawRail(AutoVertLayoutGroup rail)
    {
        rail.AddRailTitle(LM.Get("mod_settings_rail_title"),
            string.Format(LM.Get("mod_settings_rail_version"), ModClass._declare?.Version ?? ""));

        var menu = rail.BeginRailMenu(LM.Get("mod_settings_categories"));
        for (int i = 0; i < _categories.Count; i++)
        {
            int index = i;
            menu.AddRailOption(SpriteTextureLoader.getSprite(_categories[i].Icon), LM.Get(_categories[i].Key),
                Present(_categories[i]).Count().ToString(), i == _category, () => Select(index), i);
        }

        var notes = rail.BeginRailCard();
        string hint = LM.Get("mod_settings_hint");
        notes.AddRailText(hint.ColorString(WideKit.Muted), 6,
            Mathf.Ceil((UIHelper.EstimateTextWidth(hint, 6) * 1.1f + 6f) / (UIHelper.RailWidth - 8f)) * 8f + 2f, wrap: true);
        if (Present(Current).Any(setting => setting.HasDefault))
            rail.AddRailButton("mod_settings_reset", LM.Get(_confirmReset ? "mod_settings_reset_confirm" : "mod_settings_reset"),
                ResetCategory);
        rail.AddRailButton("online_update_button", LM.Get("online_update_button"),
            () => ScrollWindow.showWindow(nameof(OnlineUpdateWindow)));
    }

    private void Select(int index)
    {
        if (index == _category) return;
        _category = index;
        _confirmReset = false;
        Refresh(resetScroll: true);
    }

    // ───────── 右栏 ─────────
    protected override void DrawPage(AutoVertLayoutGroup page)
    {
        Category category = Current;
        List<Setting> present = Present(category).ToList();
        if (present.Count == 0)
        {
            page.AddPageHeader(PageWidth, LM.Get(category.Key));
            page.AddEmptyCard(PageWidth, LM.Get("mod_settings_empty"));
            return;
        }

        page.AddPageHeader(PageWidth, LM.Get(category.Key), string.Format(LM.Get("mod_settings_count"), present.Count),
            EnabledChip(present));

        var headline = category.Headline.Select(id => present.FirstOrDefault(setting => setting.Id == id))
            .Where(setting => setting != null)
            .Select(setting => (LM.Get(setting.Id), Display(setting), Hex(setting))).ToArray();
        if (headline.Length > 0) page.AddKpiRow(PageWidth, headline);

        foreach (Card card in category.Cards)
        {
            List<Setting> items = card.Items.Where(setting => Item(setting.Id) != null).ToList();
            if (items.Count == 0) continue;
            var block = page.BeginCard(PageWidth, card.Key, WideKit.Gold,
                EnabledChip(items) ?? string.Format(LM.Get("mod_settings_count"), items.Count), card.Icon);
            for (int i = 0; i < items.Count; i++) DrawSetting(block, items[i], i);
        }
    }

    private void DrawSetting(AutoVertLayoutGroup card, Setting setting, int index)
    {
        string name = LM.Get(setting.Id);
        string detailKey = setting.Id + " Description";
        string detail = LM.Has(detailKey) ? LM.Get(detailKey) : null;
        string badge = setting.Restart ? LM.Get("mod_settings_restart") : null;
        if (setting.Kind == Kind.Switch)
        {
            bool on = Item(setting.Id).BoolVal;
            card.AddToggleRow(PageWidth, index, name, detail, on, LM.Get(on ? "mod_settings_on" : "mod_settings_off"),
                () => SetSwitch(setting, !on), badge);
            return;
        }
        int value = ValueOf(setting);
        card.AddStepperRow(PageWidth, index, name, detail, value.ToString(), UnitWord(setting),
            text =>
            {
                // 输入框回车或失焦时：当场存值(随后关窗也不丢)，重画放到下一帧，不在输入框自己的事件里拆掉它
                if (int.TryParse(text, out int typed)) Apply(setting, typed);
                if (gameObject.activeInHierarchy) StartCoroutine(RefreshNextFrame());
            },
            () => SetNumber(setting, value - setting.Step), () => SetNumber(setting, value + setting.Step), badge);
    }

    // ───────── 取值、显示 ─────────
    private static IEnumerable<Setting> Present(Category category) =>
        category.Cards.SelectMany(card => card.Items).Where(setting => Item(setting.Id) != null);

    private static Dictionary<string, ModConfigItem> ConfigGroupItems()
    {
        ModConfig config = ModClass.modConfig;
        if (config == null) return null;
        try
        {
            return config[ConfigGroup];
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    private static IEnumerable<ModConfigItem> ConfigItems() =>
        ConfigGroupItems()?.Values ?? Enumerable.Empty<ModConfigItem>();

    private static ModConfigItem Item(string id) =>
        ConfigGroupItems() is { } items && items.TryGetValue(id, out ModConfigItem item) ? item : null;

    private static int ValueOf(Setting setting) =>
        int.TryParse(Item(setting.Id)?.TextVal, out int value) ? value : setting.DefaultValue;

    // "已开 3/5"；没有开关项时为空
    private static string EnabledChip(IEnumerable<Setting> settings)
    {
        List<Setting> switches = settings.Where(setting => setting.Kind == Kind.Switch).ToList();
        if (switches.Count == 0) return null;
        return string.Format(LM.Get("mod_settings_enabled"), switches.Count(setting => Item(setting.Id).BoolVal), switches.Count);
    }

    private static string Display(Setting setting)
    {
        if (setting.Kind == Kind.Switch) return LM.Get(Item(setting.Id).BoolVal ? "mod_settings_on" : "mod_settings_off");
        int value = ValueOf(setting);
        if (value == 0 && !string.IsNullOrEmpty(setting.Zero)) return LM.Get(setting.Zero);
        return string.IsNullOrEmpty(setting.Unit) ? value.ToString() : string.Format(LM.Get(setting.Unit), value);
    }

    private static string Hex(Setting setting)
    {
        if (setting.Kind == Kind.Switch) return Item(setting.Id).BoolVal ? WideKit.Green : WideKit.Muted;
        return ValueOf(setting) == 0 && !string.IsNullOrEmpty(setting.Zero) ? WideKit.Cyan : WideKit.Gold;
    }

    // 输入框旁的单位字(格式里去掉数字)
    private static string UnitWord(Setting setting) =>
        string.IsNullOrEmpty(setting.Unit) ? "" : string.Format(LM.Get(setting.Unit), "").Trim();

    // ───────── 改值(当场调回调并存盘) ─────────
    private void SetSwitch(Setting setting, bool on)
    {
        ModConfigItem item = Item(setting.Id);
        if (item == null) return;
        item.SetValue(on);
        Save();
        Refresh();
    }

    private void SetNumber(Setting setting, int value)
    {
        Apply(setting, value);
        Refresh();
    }

    private static void Apply(Setting setting, int value)
    {
        ModConfigItem item = Item(setting.Id);
        if (item == null) return;
        value = Mathf.Clamp(value, setting.Min, setting.Max);
        if (item.TextVal == value.ToString()) return;
        item.SetValue(value.ToString());
        Save();
    }

    private IEnumerator RefreshNextFrame()
    {
        yield return null;
        Refresh();
    }

    private void ResetCategory()
    {
        if (!_confirmReset)
        {
            _confirmReset = true;
            Refresh();
            return;
        }
        _confirmReset = false;
        foreach (Setting setting in Present(Current).Where(setting => setting.HasDefault))
        {
            ModConfigItem item = Item(setting.Id);
            if (setting.Kind == Kind.Switch)
            {
                if (item.BoolVal != setting.DefaultOn) item.SetValue(setting.DefaultOn);
            }
            else if (item.TextVal != setting.DefaultValue.ToString())
                item.SetValue(setting.DefaultValue.ToString());
        }
        Save();
        Refresh();
    }

    private static void Save()
    {
        try
        {
            ModClass.modConfig?.Save();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][设置] 存设置失败: {exception.Message}");
        }
    }
}

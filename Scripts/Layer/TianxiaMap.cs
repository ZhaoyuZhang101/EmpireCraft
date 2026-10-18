using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.UI.Windows;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.Layer;

// 天下图层(MetaTypeExtension.Tianxia，见 TianxiaSystem)：
//   0 天下：各天下一色，天下之间描边，铭牌写天下名与天命；
//   1 诸家：天下里按各家(宗主)着色——一眼看出谁据有多少、天下共主占了几成；天下以外不上色。
// 点地块打开天下窗口；悬停看天下名、天命、各家占比。
// 还没有帝国核心、按文化连片的天下(见 UnificationSystem.UncoredRealmOf)用素色画出、描边，铭牌写"某某(未立核心)"，
// 悬停看各家占比——以前这些地方在图层上一片空白。
public sealed class TianxiaMapColor : MetaObject<IdeologyMapColorData>
{
    private ColorAsset _color;

    public override MetaType meta_type => MetaTypeExtension.Tianxia;
    public override ColorLibrary getColorLibrary() => AssetManager.culture_colors_library;
    public override ColorAsset getColor() => _color;

    public static TianxiaMapColor Create(int index, string hex)
    {
        // 原版 tryMakeNewColorAsset 新建的颜色不 initColor() 就是透明色
        ColorAsset asset = ColorAsset.tryMakeNewColorAsset(hex);
        asset.initColor();
        var color = new TianxiaMapColor { _color = asset };
        color.loadData(new IdeologyMapColorData { name = "tianxia_" + index });
        return color;
    }
}

public static class TianxiaMap
{
    public const string TooltipType = "empirecraft_tianxia";

    // 古旧的绢本色：赭、石青、石绿、朱砂、藤黄、黛、赤金……相邻的天下按主核心编号轮着取
    private static readonly string[] Palette =
    {
        "#b5653c", "#3f7f9e", "#5d9a5a", "#b8423a", "#c9a23a", "#5b5f8f", "#9a6a9a", "#4d8f86", "#a8743f", "#7a8f3a",
        "#8f4f62", "#4f6fae"
    };

    private static TianxiaMapColor[] _colors;
    // 未立核心的天下：素绢色
    private const string UncoredHex = "#8a8170";
    private static TianxiaMapColor _uncoredColor;

    private static TianxiaMapColor UncoredColor() => _uncoredColor ??= TianxiaMapColor.Create(Palette.Length, UncoredHex);

    private static TianxiaMapColor ColorOf(EmpireCore lead)
    {
        _colors ??= Palette.Select((hex, i) => TianxiaMapColor.Create(i, hex)).ToArray();
        return _colors[(int)((lead.id % Palette.Length + Palette.Length) % Palette.Length)];
    }

    public static string HexOf(EmpireCore lead) => Palette[(int)((lead.id % Palette.Length + Palette.Length) % Palette.Length)];

    private static bool Shown(City city) =>
        city != null && !city.isRekt() && !AncientWarfareCompatibility.OwnsObject(city) &&
        (TianxiaSystem.LeadOf(city) != null || UnificationSystem.UncoredRealmOf(city) != null);

    public static void Configure(MetaTypeAsset asset)
    {
        asset.draw_zones = (MetaZoneDrawAction)(meta =>
        {
            int mode = meta.getZoneOptionState();
            foreach (City city in World.world.cities)
            {
                if (!Shown(city)) continue;
                EmpireCore lead = TianxiaSystem.LeadOf(city);
                Kingdom holder = EmpireCoreControl.Sovereign(city.kingdom);
                if (lead == null)
                {
                    DrawUncored(city, mode, holder, meta);
                    continue;
                }
                foreach (TileZone zone in city.zones)
                {
                    EmpireCraftMetaTypeLibrary.zone_manager.drawBegin();
                    if (mode == 1 && holder != null)
                        EmpireCraftMetaTypeLibrary.zone_manager.drawZoneMeta(holder, zone,
                            HolderBorder(zone.zone_up, lead, holder), HolderBorder(zone.zone_down, lead, holder),
                            HolderBorder(zone.zone_left, lead, holder), HolderBorder(zone.zone_right, lead, holder),
                            holder.data, meta);
                    else
                    {
                        TianxiaMapColor color = ColorOf(lead);
                        EmpireCraftMetaTypeLibrary.zone_manager.drawZoneMeta(color, zone,
                            TianxiaBorder(zone.zone_up, lead), TianxiaBorder(zone.zone_down, lead),
                            TianxiaBorder(zone.zone_left, lead), TianxiaBorder(zone.zone_right, lead), color.data, meta);
                    }
                    EmpireCraftMetaTypeLibrary.zone_manager.drawEnd(zone);
                }
            }
        });
        asset.check_cursor_highlight = (MetaZoneHighlightAction)((meta, tile, highlight) =>
        {
            EmpireCore lead = TianxiaSystem.LeadOf(tile?.zone?.city);
            List<City> cities = lead != null ? TianxiaSystem.Cities(lead) : UnificationSystem.UncoredRealmOf(tile?.zone?.city);
            if (cities == null) return;
            QuantumSpriteLibrary.colorZones(highlight, cities.Where(city => city != null && !city.isRekt())
                .SelectMany(city => city.zones).ToList(), highlight.color);
        });
        // 原版每帧无条件调用这两个委托，不设会每帧抛空引用
        asset.check_tile_has_meta = (MetaZoneTooltipAction)((zone, meta, option) => Shown(zone?.city));
        asset.check_cursor_tooltip = (zone, meta, option) =>
        {
            City city = zone?.city;
            if (!Shown(city)) return false;
            Tooltip.hideTooltip(city, true, TooltipType);
            Tooltip.show(city, TooltipType, new TooltipData
            {
                city = city, kingdom = city.kingdom, tooltip_scale = 0.7f, is_sim_tooltip = true
            });
            return true;
        };
        asset.click_action_zone = new MetaZoneClickAction((tile, power) =>
        {
            EmpireCore lead = TianxiaSystem.LeadOf(tile?.zone?.city);
            if (lead == null) return false;
            TianxiaWindow.Open(lead);
            return true;
        });
    }

    private static bool TianxiaBorder(TileZone neighbour, EmpireCore lead) => TianxiaSystem.LeadOf(neighbour?.city) != lead;

    private static void DrawUncored(City city, int mode, Kingdom holder, MetaTypeAsset meta)
    {
        List<City> realm = UnificationSystem.UncoredRealmOf(city);
        bool Border(TileZone neighbour) => TianxiaSystem.LeadOf(neighbour?.city) != null ||
                                          UnificationSystem.UncoredRealmOf(neighbour?.city) != realm ||
                                          mode == 1 && EmpireCoreControl.Sovereign(neighbour.city.kingdom) != holder;
        foreach (TileZone zone in city.zones)
        {
            EmpireCraftMetaTypeLibrary.zone_manager.drawBegin();
            if (mode == 1 && holder != null)
                EmpireCraftMetaTypeLibrary.zone_manager.drawZoneMeta(holder, zone, Border(zone.zone_up), Border(zone.zone_down),
                    Border(zone.zone_left), Border(zone.zone_right), holder.data, meta);
            else
            {
                TianxiaMapColor color = UncoredColor();
                EmpireCraftMetaTypeLibrary.zone_manager.drawZoneMeta(color, zone, Border(zone.zone_up), Border(zone.zone_down),
                    Border(zone.zone_left), Border(zone.zone_right), color.data, meta);
            }
            EmpireCraftMetaTypeLibrary.zone_manager.drawEnd(zone);
        }
    }

    // 未立核心的天下的称呼：按这片里最多的主流文化(铭牌每帧要用，每年重算后才重取)
    private static readonly Dictionary<List<City>, string> UncoredNames = new();
    private static int _namesVersion = -1;

    private static string UncoredName(List<City> cities)
    {
        if (_namesVersion != UnificationSystem.UncoredVersion)
        {
            UncoredNames.Clear();
            _namesVersion = UnificationSystem.UncoredVersion;
        }
        if (UncoredNames.TryGetValue(cities, out string cached)) return cached;
        return UncoredNames[cities] = ComputeUncoredName(cities);
    }

    private static string ComputeUncoredName(List<City> cities)
    {
        string culture = cities.Where(city => city?.data != null && !city.isRekt())
            .Select(city => CultureService.GetMainCulture(city, false))
            .Where(CultureService.IsValidCulture).GroupBy(name => name).OrderByDescending(group => group.Count())
            .Select(group => group.Key).FirstOrDefault();
        return string.Format(LM.Get("tianxia_uncored_name"), culture?.GetCultureTranslate() ?? "");
    }

    private static bool HolderBorder(TileZone neighbour, EmpireCore lead, Kingdom holder)
    {
        City other = neighbour?.city;
        return TianxiaSystem.LeadOf(other) != lead || EmpireCoreControl.Sovereign(other.kingdom) != holder;
    }

    // 铭牌：每个天下一枚，沿用帝国的简易纯文字样式，只写天下名称。
    public static void SubmitLabels()
    {
        foreach (EmpireCore lead in TianxiaSystem.Leads())
        {
            List<City> cities = TianxiaSystem.Cities(lead);
            if (cities.Count == 0) continue;
            UI.Components.TerritoryLabelRenderer.SubmitCities($"tianxia:{lead.id}", TianxiaSystem.Name(lead), cities,
                UI.Components.TerritoryLabelRenderer.EmpireStyle);
        }
        int index = 0;
        foreach (List<City> realm in UnificationSystem.UncoredRealms())
        {
            List<City> alive = realm.Where(city => city?.data != null && !city.isRekt()).ToList();
            if (alive.Count > 0)
                UI.Components.TerritoryLabelRenderer.SubmitCities($"tianxia:uncored:{index}", UncoredName(realm), alive,
                    UI.Components.TerritoryLabelRenderer.EmpireStyle);
            index++;
        }
    }

    // 悬停提示：天下名、天命、各家占比(前五)、本城归谁
    public static void ShowTooltip(Tooltip tooltip, string type, TooltipData data)
    {
        City city = data?.city;
        EmpireCore lead = TianxiaSystem.LeadOf(city);
        if (lead == null)
        {
            ShowUncoredTooltip(tooltip, city);
            return;
        }
        tooltip.clear();
        tooltip.setTitle(TianxiaSystem.Name(lead), "tianxia_label", HexOf(lead));
        tooltip.addLineText(LM.Get("tianxia_mandate_short"), TianxiaSystem.MandateText(lead), "#FF8A7A", pPercent: false,
            pLocalize: false);
        (Dictionary<Kingdom, int> held, int total) = TianxiaSystem.Holdings(lead);
        foreach (KeyValuePair<Kingdom, int> pair in held.OrderByDescending(pair => pair.Value).Take(5))
            tooltip.addLineText(pair.Key.GetKingdomName(), $"{pair.Value / (float)total:P0}", pair.Key.getColor()?.color_text ?? "#FFFFFF",
                pPercent: false, pLocalize: false);
        Kingdom owner = EmpireCoreControl.Sovereign(city.kingdom);
        tooltip.addBottomDescription(string.Format(LM.Get("tianxia_tooltip_city"), city.GetCityName(), owner?.GetKingdomName() ?? ""));
    }

    private static void ShowUncoredTooltip(Tooltip tooltip, City city)
    {
        List<City> whole = UnificationSystem.UncoredRealmOf(city);
        List<City> realm = whole?.Where(item => item?.data != null && !item.isRekt()).ToList();
        if (realm == null || realm.Count == 0) return;
        tooltip.clear();
        tooltip.setTitle(UncoredName(whole), "tianxia_label", UncoredHex);
        tooltip.addLineText(LM.Get("tianxia_mandate_short"), LM.Get("tianxia_uncored_hint"), "#8FA0A8", pPercent: false,
            pLocalize: false);
        Dictionary<Kingdom, int> held = EmpireCoreControl.CountHolders(realm);
        foreach (KeyValuePair<Kingdom, int> pair in held.OrderByDescending(pair => pair.Value).Take(5))
            tooltip.addLineText(pair.Key.GetKingdomName(), $"{pair.Value / (float)realm.Count:P0}",
                pair.Key.getColor()?.color_text ?? "#FFFFFF", pPercent: false, pLocalize: false);
        Kingdom owner = EmpireCoreControl.Sovereign(city.kingdom);
        tooltip.addBottomDescription(string.Format(LM.Get("tianxia_tooltip_city"), city.GetCityName(), owner?.GetKingdomName() ?? ""));
    }
}

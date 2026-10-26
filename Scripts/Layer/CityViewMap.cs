using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using UnityEngine;

namespace EmpireCraft.Scripts.Layer;

public sealed class CityViewColor : MetaObject<IdeologyMapColorData>
{
    private ColorAsset _color;

    public override MetaType meta_type => MetaTypeExtension.CityView;
    public override ColorLibrary getColorLibrary() => AssetManager.culture_colors_library;
    public override ColorAsset getColor() => _color;

    public static CityViewColor Create()
    {
        ColorAsset asset = ColorAsset.tryMakeNewColorAsset("#8a8a80");
        asset.initColor();
        var color = new CityViewColor { _color = asset };
        color.loadData(new IdeologyMapColorData { name = "city_view" });
        return color;
    }
}

// 城市图层(MetaTypeExtension.CityView)：只显示模拟的城市——不涂国家颜色，露出原本的地形；
// 城(城墙、街巷、建筑、新城区)、出城的路、关厢、镇、村、田块、林子照各自的颜色画在地形上(城市平面图层，见 CityPlanOverlay；色块全透明)，
// 国界、城界都不画，也不画铭牌。悬停高亮整座城
public static class CityViewMap
{
    private static CityViewColor _color;
    private static CityViewColor Color => _color ??= CityViewColor.Create();

    public static bool Active => World.world?.getCachedMapMetaAsset()?.map_mode == MetaTypeExtension.CityView;

    private static bool Shown(City city) =>
        city != null && !city.isRekt() && city.kingdom != null && !AncientWarfareCompatibility.OwnsObject(city);

    public static void Configure(MetaTypeAsset asset)
    {
        asset.draw_zones = (MetaZoneDrawAction)(meta =>
        {
            foreach (City city in World.world.cities)
            {
                if (!Shown(city)) continue;
                foreach (TileZone zone in city.zones)
                {
                    EmpireCraftMetaTypeLibrary.zone_manager.drawBegin();
                    EmpireCraftMetaTypeLibrary.zone_manager.drawZoneMeta(Color, zone, false, false, false, false, Color.data, meta);
                    EmpireCraftMetaTypeLibrary.zone_manager.drawEnd(zone);
                }
            }
        });
        asset.check_cursor_highlight = (MetaZoneHighlightAction)((meta, tile, highlight) =>
        {
            City hovered = tile?.zone?.city;
            if (!Shown(hovered)) return;
            QuantumSpriteLibrary.colorZones(highlight, hovered.zones, highlight.color);
        });
        asset.check_tile_has_meta = (MetaZoneTooltipAction)((zone, meta, option) => Shown(zone?.city));
        asset.check_cursor_tooltip = (zone, meta, option) => false;
        asset.click_action_zone = new MetaZoneClickAction((tile, power) => false);
    }
}

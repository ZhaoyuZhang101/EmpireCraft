using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Windows;
using NeoModLoader.services;
using System.Drawing;
using System.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.GameLibrary;
public static class EmpireCraftMetaTypeLibrary
{
    public static ZoneCalculator zone_manager;
    public static void init()
    {
        zone_manager = World.world.zone_calculator;
        MetaTypeLibrary ml = AssetManager.meta_type_library;
        ml.kingdom.drawn_zones = delegate
        {
            if (ModClass.IS_CLEAR) return;
            // todo: 当有更多地图模式的时候都可添加于此
            switch (ModClass.CURRENT_MAP_MOD)
            {
                case ModMapMode.ModLayer:
                    foreach (ModLayer pModLayer in ModClass.ModLayer_MANAGER.ToList())
                    {
                        if (pModLayer != null)
                        {
                            drawnForModLayer(pModLayer);
                        }
                    }
                    foreach (var c in World.world.cities)
                    {
                        if (c == null) continue;
                        if (!c.IsInModLayer())
                        {
                            drawnForCity(c);
                        }
                    }
                    return;
                case ModMapMode.None:
                    break;
            }
            foreach (var k in World.world.kingdoms)
                zone_manager.drawForKingdom(k);
            zone_manager.drawForKingdom(WildKingdomsManager.neutral);
        };
        ml.kingdom.click_action_zone = delegate (WorldTile pTile, string pPower)
        {
            if (pTile == null)
            {
                return false;
            }
            City city = pTile.zone.city;
            if (city.isRekt())
            {
                return false;
            }
            Kingdom kingdom = city.kingdom;
            if (kingdom.isRekt())
            {
                return false;
            }
            if (kingdom.isNeutral())
            {
                return false;
            }
            
            // todo: 当有更多地图模式的时候都可添加于此
            switch (ModClass.CURRENT_MAP_MOD)
            {
                case ModMapMode.ModLayer:
                    if (pTile.hasCity())
                    {
                        if (pTile.zone_city.IsInModLayer())
                        {
                            ConfigData.CurrentSelectedModLayer = pTile.zone_city.GetModLayer();
                            ScrollWindow.showWindow(nameof(ModLayerWindow));
                            LogService.LogInfo("open ModLayer window");
                            return true;
                        }
                    }
                    break;
                case ModMapMode.None:
                    break;
            }
            MetaType.Kingdom.getAsset().selectAndInspect(kingdom);
            return true;
        };
        ml.kingdom.check_cursor_tooltip = delegate (WorldTile pTile)
        {
            City city = pTile.zone.city;
            if (city.isRekt())
            {
                return false;
            }
            
            // todo: 当有更多地图模式的时候都可添加于此
            switch (ModClass.CURRENT_MAP_MOD)
            {
                case ModMapMode.ModLayer:
                    if (city.IsInModLayer())
                    {
                        tooltip_ModLayer_action(city.GetModLayer());
                        return true;
                    }
                    break;
                case ModMapMode.None:
                    break;
            }

            city.meta_type_asset.cursor_tooltip_action(city);
            return true;
        };
        ml.kingdom.check_cursor_highlight = delegate (WorldTile pTile, QuantumSpriteAsset pAsset)
        {
            bool flag = PlayerConfig.optionBoolEnabled("highlight_kingdom_enemies");
            UnityEngine.Color color = pAsset.color;
            City city = pTile.zone.city;
            if (city.isRekt()) return;
            
            // todo: 当有更多地图模式的时候都可添加于此
            switch (ModClass.CURRENT_MAP_MOD)
            {
                case ModMapMode.ModLayer:
                    if (city.IsInModLayer())
                    {
                        ModLayer pModLayer = city.GetModLayer();
                        if (pModLayer != null)
                        {
                            foreach (City c in pModLayer.city_list)
                            {
                                QuantumSpriteLibrary.colorZones(pAsset, c.zones, color);
                            }
                            return;
                        }
                    }
                    break;
                case ModMapMode.None:
                    break;
            }
            foreach (var city2 in city.kingdom.cities)
            {
                QuantumSpriteLibrary.colorZones(pAsset, city2.zones, color);
            }
            if (flag)
            {
                QuantumSpriteLibrary.colorEnemies(pAsset, city.kingdom);
            }
        };
    }

    public static void drawnForModLayer(ModLayer p)
    {
        foreach (TileZone tz in p.allZones())
        {
            zone_manager.drawBegin();
            drawZoneModLayer(tz);
            zone_manager.drawEnd(tz);
        }
    }

    public static void drawnForCity(City city)
    {
        for (int i = 0; i < city.zones.Count; i++)
        {
            TileZone pZone = city.zones[i];
            zone_manager.drawBegin();
            zone_manager.drawZoneCity(pZone);
            zone_manager.drawEnd(pZone);
        }
    }
    public static void drawZoneModLayer(TileZone pZone)
    {
        ModLayer pModLayer = pZone.city.GetModLayer();
        if (pModLayer == null) return;
        bool pUp =    isBorderColor_ModLayer(pZone.zone_up, pModLayer, true);
        bool pDown =  isBorderColor_ModLayer(pZone.zone_down, pModLayer, false);
        bool pLeft =  isBorderColor_ModLayer(pZone.zone_left, pModLayer, false);
        bool pRight = isBorderColor_ModLayer(pZone.zone_right, pModLayer, true);
        int num = -1;
        num = pModLayer.GetHashCode();
        int num2 = zone_manager.generateIdForDraw(zone_manager._mode_asset, num, pUp, pDown, pLeft, pRight);
        if (pZone.last_drawn_id == num2 && pZone.last_drawn_hashcode == num)
        {
            return;
        }
        pZone.last_drawn_id = num2;
        pZone.last_drawn_hashcode = num;
        Color32 colorBorderInsideAlpha = Toolbox.color_clear;
        Color32 colorMain = Toolbox.color_clear;
        ColorAsset color = pModLayer.getColor();
        colorBorderInsideAlpha = color.getColorBorderInsideAlpha();
        colorMain = color.getColorMain2();
        if (zone_manager.shouldBeClearColor())
        {
            colorBorderInsideAlpha = zone_manager.color_clear;
        }
        zone_manager.applyMetaColorsToZone(pZone, ref colorBorderInsideAlpha, ref colorMain, pUp, pDown, pLeft, pRight);
    }

    public static bool isBorderColor_ModLayer(TileZone pZone, ModLayer pModlayer, bool pCheckFriendly = false)
    {
        if (pZone == null)
        {
            return true;
        }
        if (pZone.city == null) return true;
        if (!pZone.city.IsInModLayer()) return true;
        ModLayer ModLayerOnZone = pZone.city.GetModLayer();
        return ModLayerOnZone == null || ModLayerOnZone != pModlayer;
    }
    

    public static void tooltip_ModLayer_action(ModLayer pModLayer)
    {
        if (!pModLayer.isRekt())
        {
            Tooltip.hideTooltip(pModLayer, pOnlySimObjects: true, "ModLayer");
            Tooltip.show(pModLayer, "ModLayer", new TooltipData
            {
                city = pModLayer.CoreCity,
                tooltip_scale = 0.7f,
                is_sim_tooltip = true
            });
        }
    }
}

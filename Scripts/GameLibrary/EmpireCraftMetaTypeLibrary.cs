using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Windows;
using NeoModLoader.services;
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
            if (ModClass.CURRENT_MAP_MOD == EmpireCraftMapMode.Empire)
            {
                foreach (Empire empire in ModClass.EMPIRE_MANAGER)
                {
                    if (empire != null)
                    {
                        drawForEmpire(empire);
                    }
                }
                foreach (var k in World.world.kingdoms)
                {
                    if (k == null) continue;
                    if (!k.isInEmpire())
                        zone_manager.drawForKingdom(k);
                }
            }
            else if (ModClass.CURRENT_MAP_MOD == EmpireCraftMapMode.Province)
            {
                foreach (Province province in ModClass.PROVINCE_MANAGER)
                {
                    if (province != null)
                    {
                        drawnForProvince(province);
                    }
                }
                foreach (var k in World.world.kingdoms)
                {
                    if (k == null) continue;
                    if (!k.isEmpire())
                        zone_manager.drawForKingdom(k);
                }
            }
            else
            {
                foreach (var k in World.world.kingdoms)
                    zone_manager.drawForKingdom(k);
            }
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
            if (ModClass.CURRENT_MAP_MOD == EmpireCraftMapMode.Empire)
            {
                if (kingdom.isInEmpire())
                {
                    ConfigData.CURRENT_SELECTED_EMPIRE = kingdom.GetEmpire();
                    ScrollWindow.showWindow(nameof(EmpireWindow));
                    return true;
                }
            }
            if (ModClass.CURRENT_MAP_MOD == EmpireCraftMapMode.Province)
            {
                if (pTile.hasCity())
                {
                    if (pTile.zone_city.hasProvince())
                    {
                        ConfigData.CURRENT_SELECTED_PROVINCE = pTile.zone_city.GetProvince();
                        LogService.LogInfo("open province window");
                        return true;
                    }

                }
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
            Kingdom kingdom = city.kingdom;
            if (kingdom.isRekt())
            {
                return false;
            }
            if (kingdom.isNeutral())
            {
                return false;
            }
            if (ModClass.CURRENT_MAP_MOD == EmpireCraftMapMode.Empire)
            {
                if (kingdom.isInEmpire())
                {
                    tooltip_empire_action(kingdom);
                    return true;
                }
            }
            if (ModClass.CURRENT_MAP_MOD == EmpireCraftMapMode.Province)
            {
                if (city.hasProvince())
                {
                    tooltip_province_action(city.GetProvince());
                    return true;
                }
            }
            kingdom.meta_type_asset.cursor_tooltip_action(kingdom);
            return true;
        };
        ml.kingdom.check_cursor_highlight = delegate (WorldTile pTile, QuantumSpriteAsset pAsset)
        {
            bool flag = PlayerConfig.optionBoolEnabled("highlight_kingdom_enemies");
            Color color = pAsset.color;
            City city = pTile.zone.city;
            if (!city.isRekt())
            {
                if (ModClass.CURRENT_MAP_MOD == EmpireCraftMapMode.Province)
                {
                    if (city.hasProvince())
                    {
                        Province province = city.GetProvince();
                        if (province != null)
                        {
                            foreach (City c in province.city_list)
                            {
                                QuantumSpriteLibrary.colorZones(pAsset, c.zones, color);
                            }
                            return;
                        }
                    }
                }
                for (int i = 0; i < city.kingdom.cities.Count; i++)
                {
                    City city2 = city.kingdom.cities[i];
                    QuantumSpriteLibrary.colorZones(pAsset, city2.zones, color);
                }
                if (flag)
                {
                    QuantumSpriteLibrary.colorEnemies(pAsset, city.kingdom);
                }
            }
        };
        ml.city.drawn_zones = delegate
        {
            if (ModClass.IS_CLEAR) return;
            if (ModClass.CURRENT_MAP_MOD == EmpireCraftMapMode.Title)
            {
                foreach (KingdomTitle kingdomTitle in ModClass.KINGDOM_TITLE_MANAGER)
                {
                    if (kingdomTitle != null)
                    {
                        drawnForTitle(kingdomTitle);
                    }
                }
                foreach (var k in World.world.cities)
                {
                    if (!k.hasTitle())
                        drawnForCity(k);
                }
            }
            else
            {
                foreach (var k in World.world.cities)
                    drawnForCity(k);
            }
        };
        ml.city.click_action_zone = delegate (WorldTile pTile, string pPower)
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
            if (ModClass.CURRENT_MAP_MOD == EmpireCraftMapMode.Title)
            {
                if (city.hasTitle())
                {
                    ConfigData.CURRENT_SELECTED_TITLE = city.GetTitle();
                    ScrollWindow.showWindow(nameof(KingdomTitleWindow));
                    return true;
                }
            }
            Config.selected_city = city;
            ScrollWindow.showWindow("city");
            return true;
        };
        ml.city.check_cursor_tooltip = delegate (WorldTile pTile)
        {
            City city = pTile.zone.city;
            if (city.isRekt())
            {
                return false;
            }
            if (ModClass.CURRENT_MAP_MOD == EmpireCraftMapMode.Title)
            {
                if (city.hasTitle())
                {
                    tooltip_title_action(city.GetTitle());
                    return true;
                }
            }
            city.meta_type_asset.cursor_tooltip_action(city);
            return true;
        };
    }

    public static void drawnForTitle(KingdomTitle kt)
    {
        foreach (TileZone tz in kt.allZones())
        {
            zone_manager.drawBegin();
            drawZoneKingdomTitle(tz);
            zone_manager.drawEnd(tz);
        }
    }

    public static void drawnForProvince(Province p)
    {
        foreach (TileZone tz in p.allZones())
        {
            zone_manager.drawBegin();
            drawZoneProvince(tz);
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
    public static void drawForEmpire(Empire empire)
    {
        foreach (TileZone tz in empire.allZones())
        {
            zone_manager.drawBegin();
            drawZoneEmpire(tz);
            zone_manager.drawEnd(tz);
        }
    }

    public static void drawZoneEmpire(TileZone pZone)
    {
        if (pZone == null) return;
        if (pZone.city == null) return;
        Empire empire = pZone.city.kingdom.GetEmpire();
        if (empire == null) return;

        bool pUp = isBorderColor_Empire(pZone.zone_up, empire, true);
        bool pDown = isBorderColor_Empire(pZone.zone_down, empire, false);
        bool pLeft = isBorderColor_Empire(pZone.zone_left, empire, false);
        bool pRight = isBorderColor_Empire(pZone.zone_right, empire, true);
        int num = -1;
        if (empire != null)
        {
            num = empire.GetHashCode();
        }
        int num2 = zone_manager.generateIdForDraw(zone_manager._mode_asset, num, pUp, pDown, pLeft, pRight);
        if (pZone.last_drawn_id == num2 && pZone.last_drawn_hashcode == num)
        {
            return;
        }
        pZone.last_drawn_id = num2;
        pZone.last_drawn_hashcode = num;
        Color32 colorBorderInsideAlpha = Toolbox.color_clear;
        Color32 colorMain = Toolbox.color_clear;
        if (empire != null)
        {
            if (empire.empire.isAlive())
            {
                ColorAsset color = empire.empire.getColor();
                colorBorderInsideAlpha = color.getColorBorderInsideAlpha();
                colorMain = color.getColorMain2();
                if (zone_manager.shouldBeClearColor())
                {
                    colorBorderInsideAlpha = zone_manager.color_clear;
                }
            }
        }
        zone_manager.applyMetaColorsToZone(pZone, ref colorBorderInsideAlpha, ref colorMain, pUp, pDown, pLeft, pRight);
    }

    public static void drawZoneProvince(TileZone pZone)
    {
        Province p = pZone.city.GetProvince();
        if (p == null) return;

        bool pUp = isBorderColor_Province(pZone.zone_up, p, true);
        bool pDown = isBorderColor_Province(pZone.zone_down, p, false);
        bool pLeft = isBorderColor_Province(pZone.zone_left, p, false);
        bool pRight = isBorderColor_Province(pZone.zone_right, p, true);
        int num = -1;
        if (p != null)
        {
            num = p.GetHashCode();
        }
        int num2 = zone_manager.generateIdForDraw(zone_manager._mode_asset, num, pUp, pDown, pLeft, pRight);
        if (pZone.last_drawn_id == num2 && pZone.last_drawn_hashcode == num)
        {
            return;
        }
        pZone.last_drawn_id = num2;
        pZone.last_drawn_hashcode = num;
        Color32 colorBorderInsideAlpha = Toolbox.color_clear;
        Color32 colorMain = Toolbox.color_clear;
        if (p != null)
        {
            ColorAsset color = p.getColor();
            colorBorderInsideAlpha = color.getColorBorderInsideAlpha();
            colorMain = color.getColorMain2();
            if (zone_manager.shouldBeClearColor())
            {
                colorBorderInsideAlpha = zone_manager.color_clear;
            }
        }
        zone_manager.applyMetaColorsToZone(pZone, ref colorBorderInsideAlpha, ref colorMain, pUp, pDown, pLeft, pRight);
    }

    public static void drawZoneKingdomTitle(TileZone pZone)
    {
        KingdomTitle kt = pZone.city.GetTitle();
        if (kt == null) return;

        bool pUp = isBorderColor_KingdomTitle(pZone.zone_up, kt, true);
        bool pDown = isBorderColor_KingdomTitle(pZone.zone_down, kt, false);
        bool pLeft = isBorderColor_KingdomTitle(pZone.zone_left, kt, false);
        bool pRight = isBorderColor_KingdomTitle(pZone.zone_right, kt, true);
        int num = -1;
        if (kt != null)
        {
            num = kt.GetHashCode();
        }
        int num2 = zone_manager.generateIdForDraw(zone_manager._mode_asset, num, pUp, pDown, pLeft, pRight);
        if (pZone.last_drawn_id == num2 && pZone.last_drawn_hashcode == num)
        {
            return;
        }
        pZone.last_drawn_id = num2;
        pZone.last_drawn_hashcode = num;
        Color32 colorBorderInsideAlpha = Toolbox.color_clear;
        Color32 colorMain = Toolbox.color_clear;
        if (kt != null)
        {
            ColorAsset color = kt.getColor();
            colorBorderInsideAlpha = color.getColorBorderInsideAlpha();
            colorMain = color.getColorMain2();
            if (zone_manager.shouldBeClearColor())
            {
                colorBorderInsideAlpha = zone_manager.color_clear;
            }
        }
        zone_manager.applyMetaColorsToZone(pZone, ref colorBorderInsideAlpha, ref colorMain, pUp, pDown, pLeft, pRight);
    }

    public static bool isBorderColor_Empire(TileZone pZone, Empire pEmpire, bool pCheckFriendly = false)
    {
        if (pZone == null)
        {
            return true;
        }
        if (pZone.city == null) return true;
        if (pZone.city.kingdom == null) return true;
        Empire empireOnZone = pZone.city.kingdom.GetEmpire();
        return empireOnZone == null || empireOnZone != pEmpire;
    }

    public static bool isBorderColor_KingdomTitle(TileZone pZone, KingdomTitle pKingdomTitle, bool pCheckFriendly = false)
    {
        if (pZone == null)
        {
            return true;
        }
        if (pZone.city == null) return true;
        if (!pZone.city.hasTitle()) return true;
        KingdomTitle titleOnZone = pZone.city.GetTitle();
        return titleOnZone == null || titleOnZone != pKingdomTitle;
    }

    public static bool isBorderColor_Province(TileZone pZone, Province province, bool pCheckFriendly = false)
    {
        if (pZone == null)
        {
            return true;
        }
        if (pZone.city == null) return true;
        if (!pZone.city.hasProvince()) return true;
        Province titleOnZone = pZone.city.GetProvince();
        return titleOnZone == null || titleOnZone != province;
    }

    public static void tooltip_empire_action(Kingdom kingdom)
    {
        if (!kingdom.isRekt())
        {
            Tooltip.hideTooltip(kingdom, pOnlySimObjects: true, "empire");
            Tooltip.show(kingdom, "empire", new TooltipData
            {
                kingdom = kingdom,
                tooltip_scale = 0.7f,
                is_sim_tooltip = true
            });
        }
    }

    public static void tooltip_province_action(Province province)
    {
        if (!province.isRekt())
        {
            Tooltip.hideTooltip(province, pOnlySimObjects: true, "province");
            Tooltip.show(province, "province", new TooltipData
            {
                city = province.province_capital,
                tooltip_scale = 0.7f,
                is_sim_tooltip = true
            });
        }
    }

    public static void tooltip_title_action(KingdomTitle kingdomTitle)
    {
        if (!kingdomTitle.isRekt())
        {
            Tooltip.hideTooltip(kingdomTitle, pOnlySimObjects: true, "kingdom_title");
            Tooltip.show(kingdomTitle, "kingdom_title", new TooltipData
            {
                city = kingdomTitle.title_capital,
                tooltip_scale = 0.7f,
                is_sim_tooltip = true
            });
        }
    }

}

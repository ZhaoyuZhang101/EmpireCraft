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
        if (ModClass.CURRENT_MAP_MOD == ModMapMode.ModObject)
            {
                foreach (ModObject pModObject in ModClass.ModObjectManager.ToList())
                {
                    if (pModObject != null)
                    {
                        drawnForModObject(pModObject);
                    }
                }
                foreach (var k in World.world.kingdoms)
                {
                    if (k == null) continue;
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
            if (ModClass.CURRENT_MAP_MOD == ModMapMode.ModObject)
            {
                if (pTile.hasCity())
                {
                    if (pTile.zone_city.hasProvince())
                    {
                        ConfigData.CurrentSelectedModObject = pTile.zone_city.GetProvince();
                        ScrollWindow.showWindow(nameof(ProvinceWindow));
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
            UnityEngine.Color color = pAsset.color;
            City city = pTile.zone.city;
            if (city.isRekt()) return;
            if (ModClass.CURRENT_MAP_MOD == EmpireCraftMapMode.Province)
            {
                if (city.hasProvince())
                {
                    ModObject modObject = city.GetProvince();
                    if (modObject != null)
                    {
                        foreach (City c in modObject.city_list)
                        {
                            QuantumSpriteLibrary.colorZones(pAsset, c.zones, color);
                        }
                        return;
                    }
                }
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

    public static void drawnForModObject(ModObject p)
    {
        foreach (TileZone tz in p.allZones())
        {
            zone_manager.drawBegin();
            drawZoneModObject(tz);
            zone_manager.drawEnd(tz);
        }
    }
    public static void drawZoneModObject(TileZone pZone)
    {
        ModObject p = pZone.city.GetProvince();
        if (p == null) return;
        Empire empire = p.empire;
        Kingdom mainKingdom = empire.empire;
        if (!mainKingdom.isAlive())
        {
            ModClass.ModObjectManager.dissolveProvince(p);
            ModClass.EMPIRE_MANAGER.dissolveEmpire(empire);
            return;
        }
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
            ColorAsset color = mainKingdom.getColor();
            colorBorderInsideAlpha = color.getColorBorderInsideAlpha();
            colorMain = color.getColorMain2();
            if(p.empire.empire!=pZone.city.kingdom)
            {
                colorMain.r += 5;
                colorMain.a -= 5;
            }
            if (zone_manager.shouldBeClearColor())
            {
                colorBorderInsideAlpha = zone_manager.color_clear;
            }
        }
        zone_manager.applyMetaColorsToZone(pZone, ref colorBorderInsideAlpha, ref colorMain, pUp, pDown, pLeft, pRight);
    }

    public static bool isBorderColor_Province(TileZone pZone, ModObject province, bool pCheckFriendly = false)
    {
        if (pZone == null)
        {
            return true;
        }
        if (pZone.city == null) return true;
        if (!pZone.city.hasProvince()) return true;
        ModObject titleOnZone = pZone.city.GetProvince();
        return titleOnZone == null || titleOnZone != province;
    }
    

    public static void tooltip_province_action(ModObject province)
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
}

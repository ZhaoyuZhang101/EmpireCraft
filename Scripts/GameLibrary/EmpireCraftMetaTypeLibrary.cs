using System.Collections.Generic;
using System.Linq;
using System.Security.Policy;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Windows;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GameLibrary;
public static class EmpireCraftMetaTypeLibrary
{
    public static Empire selected_empire; 
    public static KingdomTitle selected_kingdomTitle; 
    public static MetaTypeAsset empire; 
    public static MetaTypeAsset kingdomTitle; 
    public static ZoneCalculator zone_manager => World.world.zone_calculator;

    public static void init()
    {
      AddEmpireMeta();
      AddKingdomTitleMeta();
      AssetManager.meta_type_library.linkAssets();
    }

    public static void AddEmpireMeta()
    {
        MetaTypeAsset pAsset13 = new MetaTypeAsset();
        pAsset13.id = "empire";
        pAsset13.ranks = MetaTypeLibrary.generateExponentialRanks(100.0, 1.5);
        pAsset13.window_name = "EmpireWindow";
        pAsset13.power_tab_id = "selected_empire";
        pAsset13.force_zone_when_selected = true;
        pAsset13.set_icon_for_cancel_button = true;
        pAsset13.icon_list = "iconKingdomList";
        pAsset13.icon_single_path = "ui/icons/iconKingdom";
        pAsset13.window_action_clear = (MetaTypeAction) (() => selected_empire = (Empire) null);
        pAsset13.window_history_action_update = (MetaTypeHistoryAction) ((ref WindowHistoryData pHistoryData) =>
        {
	        pHistoryData.kingdom = selected_empire?.CoreKingdom; 
        });
        pAsset13.window_history_action_restore = (MetaTypeHistoryAction) ((ref WindowHistoryData pHistoryData) => SelectedMetas.selected_kingdom = pHistoryData.kingdom);
        pAsset13.has_dynamic_zones = true;
        pAsset13.dynamic_zone_option = 2;
        pAsset13.reports = new string[4]
        {
          "happy",
          "unhappy",
          "many_children",
          "many_homeless"
        };
        pAsset13.get_list = (MetaTypeListAction) (() => (IEnumerable<NanoObject>) ModClass.EMPIRE_MANAGER.ToList().Where(e => !e.IsArchived()));
        pAsset13.has_any = (MetaTypeListHasAction) (() => ModClass.EMPIRE_MANAGER.ToList().Any(e => !e.IsArchived()));
        pAsset13.get_selected = (MetaSelectedGetter) (() => (NanoObject) selected_empire);
        pAsset13.set_selected = (MetaSelectedSetter) (pElement => selected_empire = pElement as Empire);
        pAsset13.get = (MetaGetter) (pId => (NanoObject) ModClass.EMPIRE_MANAGER.get(pId));
        pAsset13.map_mode = MetaTypeExtension.Empire;
        pAsset13.option_id = "map_Empire_layer";
      
        pAsset13.power_option_zone_id = "Empire_layer";
        pAsset13.click_action_zone = new MetaZoneClickAction(inspectEmpire);
        pAsset13.selected_tab_action_meta = new MetaTypeActionAsset(defaultClickActionZone);
        pAsset13.check_unit_has_meta = (MetaCheckUnitWindowAction) (pActor => pActor.isKingdomCiv());
        pAsset13.set_unit_set_meta_for_meta_for_window = (MetaUnitSetMetaForWindow) (pActor => selected_empire = pActor.kingdom.GetEmpire());
        pAsset13.draw_zones = (MetaZoneDrawAction) (pMetaTypeAsset =>
        {
	        switch (pMetaTypeAsset.getZoneOptionState())
	        {
		        case 0:
              foreach (var kingdom in World.world.kingdoms)
              { 
                if (kingdom.IsInEmpire()) continue;
                if (kingdom.HasTakenAlliance())
                {
                  var takenEmpire = kingdom.GetTakenAllianceEmpire();
                  if (takenEmpire != null && !takenEmpire.isRekt() && !takenEmpire.IsArchived())
                  {
                    foreach (City city in kingdom.cities)
                    {
                      foreach (TileZone zone in city.zones)
                      {
                        zone_manager.drawBegin();
                        drawZoneEmpireWithKingdomBorder(zone, takenEmpire);
                        zone_manager.drawEnd(zone);
                      }
                    }
                    continue;
                  }
                }
                drawDefaultMeta(kingdom.meta_type_asset);
              }
              WorldTile mouseTilePosCachedFrame = World.world.getMouseTilePosCachedFrame();
              var kingdomSelect = mouseTilePosCachedFrame?.zone_city?.kingdom;
              foreach (var pEmpire in ModClass.EMPIRE_MANAGER.ToList().Where(e => !e.IsArchived()))
              {
                  foreach (City city in pEmpire.getCities().ToList())
                  {
                    if (kingdomSelect == null)
                    {
                      foreach (TileZone zone in city.zones)
                      {
                        zone_manager.drawBegin();
                        drawZoneEmpireWithKingdomBorder(zone, pEmpire);
                        zone_manager.drawEnd(zone);
                      }
                    }
                    else
                    {
                      if (!kingdomSelect.cities.Contains(city))
                      {
                        foreach (TileZone zone in city.zones)
                        {
                          zone_manager.drawBegin();
                          drawZoneEmpireWithKingdomBorder(zone, pEmpire);
                          zone_manager.drawEnd(zone);
                        }
                      }
                      else
                      {
                        foreach (TileZone zone in city.zones)
                        {
                          zone_manager.drawBegin();
                          drawZoneSelectedEmpireWithKingdomBorder(zone, kingdomSelect);
                          zone_manager.drawEnd(zone);
                        }
                      }
                    }
                  }
              }
			        break;
		        case 1:

			        foreach (var pEmpire in ModClass.EMPIRE_MANAGER.ToList().Where(e => !e.IsArchived()))
			        {
                if (pEmpire.CoreKingdom.HasTakenAlliance()) continue;
                var cities = pEmpire.AllCities();
                foreach (var kingdom in pEmpire.taken_Kingdoms)
                {
                  if (kingdom.isRekt()) continue;
                  cities = cities.Union(kingdom.cities).ToList();
                }
                foreach (City city in cities)
                {
                  foreach (TileZone zone in city.zones)
                  {
                    zone_manager.drawBegin();
                    drawZoneEmpireWithKingdomBorder(zone, pEmpire);
                    zone_manager.drawEnd(zone);
                  }
                }
			        }
			        break;
		        case 2:
              foreach (var pEmpire in ModClass.EMPIRE_MANAGER.ToList().Where(e => !e.IsArchived()))
              {
                if (pEmpire.CoreKingdom.HasGivenAlliance()) continue;
                var cities = pEmpire.AllCities();
                foreach (var kingdom in pEmpire.given_Kingdoms)
                {
                  if (kingdom.isRekt()) continue;
                  cities = cities.Union(kingdom.cities).ToList();
                }
                foreach (City city in cities)
                {
                  foreach (TileZone zone in city.zones)
                  {
                    zone_manager.drawBegin();
                    drawZoneEmpireWithKingdomBorder(zone, pEmpire);
                    zone_manager.drawEnd(zone);
                  }
                }
              }
			        break;
	        }
        });
        double _last_dynamic_zones_ts = -1L;
        pAsset13.dynamic_zones = (MetaZoneDynamicAction) (() =>
        {
          if (_last_dynamic_zones_ts > 0 && Date.getMonthsSince(_last_dynamic_zones_ts) < 1) return;
          List<Actor> simpleList = World.world.units.getSimpleList();
          double curWorldTime = World.world.getCurWorldTime();
          int index = 0;
          for (int count = simpleList.Count; index < count; ++index)
          {
            Actor actor = simpleList[index];
            if (actor.asset.show_on_meta_layer)
            {
              TileZone zone = actor.current_tile.zone;
              if (actor.hasCity())
                if (actor.city?.kingdom?.GetEmpire()!=null)
                  ZoneMetaDataVisualizer.countMetaZone(zone, (IMetaObject) actor.city.kingdom.GetEmpire(), curWorldTime);
            }
          }
          _last_dynamic_zones_ts = World.world.getCurWorldTime();
        });
        pAsset13.check_cursor_highlight = (MetaZoneHighlightAction) ((pMetaTypeAsset, pTile, pQAsset) =>
        {
          Color color = pQAsset.color;
          City city11 = pTile.zone.city;
          if (city11.isRekt())
            return;
          Kingdom kingdom11 = city11.kingdom;
          if (kingdom11.isRekt()) return;
          switch (pMetaTypeAsset.getZoneOptionState())
          {
            case 0:
              if (!kingdom11.IsInEmpire())
                return;
              foreach (City city12 in kingdom11.GetEmpire().AllCities())
                QuantumSpriteLibrary.colorZones(pQAsset, city12.zones, color);
              break;
            case 1:
              Empire empire1 = null;
              if (kingdom11.HasTakenAlliance())
              {
                empire1 = kingdom11.GetTakenAllianceEmpire();
              } else if (kingdom11.IsInEmpire())
              {
                empire1 = kingdom11.GetEmpire();
              }

              List<City> cities = new List<City>();
              if (empire1 != null)
              {
                cities = empire1.AllCities();
                foreach (var kingdom in empire1.taken_Kingdoms)
                {
                  if (kingdom.isRekt()) continue;
                  cities = cities.Union(kingdom.cities).ToList();
                }
              }
              foreach (City city12 in cities)
                QuantumSpriteLibrary.colorZones(pQAsset, city12.zones, color);
              break;
            case 2:
              Empire empire2 = null;
              if (kingdom11.HasGivenAlliance())
              {
                empire2 = kingdom11.GetGivenAllianceEmpire();
              } else if (kingdom11.IsInEmpire())
              {
                if (kingdom11.GetEmpire().CoreKingdom.HasGivenAlliance())
                {
                  empire2 = kingdom11.GetEmpire().CoreKingdom.GetGivenAllianceEmpire();
                }
                else
                {
                  empire2 = kingdom11.GetEmpire();
                }
              }

              List<City> cities2 = new List<City>();
              if (empire2 != null)
              {
                cities2 = empire2.AllCities();
                foreach (var kingdom in empire2.given_Kingdoms)
                {
                  cities2 = kingdom.IsInEmpire() ? cities2.Union(kingdom.GetEmpire().getCities()).ToList() : cities2.Union(kingdom.cities).ToList();
                }
              }
              foreach (City city12 in cities2)
                QuantumSpriteLibrary.colorZones(pQAsset, city12.zones, color);
              break;
          }

        });
        pAsset13.tile_get_metaobject = (MetaZoneGetMeta) ((pZone, pZoneOption) =>
        {
          Kingdom kingdomOnZone = pZone.city?.kingdom;
          if (kingdomOnZone==null)
            return null;
          if (kingdomOnZone.IsInEmpire()) return kingdomOnZone.GetEmpire();
          if (kingdomOnZone.HasTakenAlliance()) return kingdomOnZone.GetTakenAllianceEmpire();
          return null;
        });
        pAsset13.tile_get_metaobject_0 = (MetaZoneGetMetaSimple) (pZone =>
        {
          Kingdom kingdom = pZone?.city?.kingdom;
          if (kingdom == null) return null;
          if (kingdom.IsInEmpire()) return kingdom.GetEmpire();
          if (kingdom.HasTakenAlliance()) return kingdom.GetTakenAllianceEmpire();
          return null;
        });
        pAsset13.tile_get_metaobject_1 = (MetaZoneGetMetaSimple) (pZone => ZoneMetaDataVisualizer.getZoneMetaData(pZone).meta_object);
        pAsset13.tile_get_metaobject_2 = (MetaZoneGetMetaSimple) (pZone => ZoneMetaDataVisualizer.getZoneMetaData(pZone).meta_object);
        pAsset13.check_tile_has_meta = (MetaZoneTooltipAction) ((pZone, pAsset, pZoneOption) =>
        {
          IMetaObject metaObject = pAsset.tile_get_metaobject(pZone, pZoneOption);
          Empire m = metaObject as Empire;
          if (m == null) return false;
          return m.isRekt();
        });
        pAsset13.check_cursor_tooltip = new MetaZoneTooltipAction(checkCursorTooltipDefault);
        pAsset13.cursor_tooltip_action = (MetaTooltipShowAction) (pMeta =>
        {
          Empire pEmpire = pMeta as Empire;
          if (pEmpire.isRekt() || pEmpire.IsArchived())
            return;
          string str = "empire";
          Tooltip.hideTooltip((object) pEmpire, true, str);
          Tooltip.show((object) pEmpire, str, new TooltipData()
          {
            kingdom = pEmpire.CoreKingdom,
            tooltip_scale = 0.7f,
            is_sim_tooltip = true
          });
        });
        pAsset13.stat_hover = (MetaStatAction) ((pMetaId, pField) =>
        {
          Empire pObject = ModClass.EMPIRE_MANAGER.get(pMetaId);
          if (pObject.isRekt() || pObject.IsArchived())
            return;
          Tooltip.show((object) pField, "empire", new TooltipData()
          {
            kingdom = pObject.CoreKingdom
          });
        });
        pAsset13.stat_click = (MetaStatAction) ((pMetaId, _) =>
        {
          Empire pObject = ModClass.EMPIRE_MANAGER.get(pMetaId);
          if (pObject.isRekt() || pObject.IsArchived())
            return;
          selected_empire = pObject;
          SelectedMetas.selected_kingdom = selected_empire.CoreKingdom;
          ScrollWindow.showWindow(nameof(EmpireWindow));
        });
        empire = AssetManager.meta_type_library.add(pAsset13);
    }    
    public static void AddKingdomTitleMeta()
    {
      MetaTypeAsset pAsset13 = new MetaTypeAsset();
        pAsset13.id = "kingdomTitle";
        pAsset13.ranks = MetaTypeLibrary.generateExponentialRanks(100.0, 1.5);
        pAsset13.window_name = "KingdomTitleWindow";
        pAsset13.power_tab_id = "selected_kingdomTitle";
        pAsset13.force_zone_when_selected = true;
        pAsset13.set_icon_for_cancel_button = true;
        pAsset13.icon_list = "iconKingdomList";
        pAsset13.icon_single_path = "ui/icons/iconKingdom";
        pAsset13.window_action_clear = (MetaTypeAction) (() => selected_kingdomTitle = (KingdomTitle) null);
        pAsset13.window_history_action_update = (MetaTypeHistoryAction) ((ref WindowHistoryData pHistoryData) =>
        {
	        pHistoryData.city = selected_kingdomTitle?.title_capital; 
        });
        pAsset13.window_history_action_restore = (MetaTypeHistoryAction) ((ref WindowHistoryData pHistoryData) => SelectedMetas.selected_city = pHistoryData.city);
        pAsset13.has_dynamic_zones = true;
        pAsset13.dynamic_zone_option = 2;
        pAsset13.reports = new string[4]
        {
          "happy",
          "unhappy",
          "many_children",
          "many_homeless"
        };
        pAsset13.get_list = (MetaTypeListAction) (() => (IEnumerable<NanoObject>) ModClass.KINGDOM_TITLE_MANAGER);
        pAsset13.has_any = (MetaTypeListHasAction) (() => ModClass.KINGDOM_TITLE_MANAGER.hasAny());
        pAsset13.get_selected = (MetaSelectedGetter) (() => (NanoObject) selected_kingdomTitle);
        pAsset13.set_selected = (MetaSelectedSetter) (pElement => selected_kingdomTitle = pElement as KingdomTitle);
        pAsset13.get = (MetaGetter) (pId => (NanoObject) ModClass.KINGDOM_TITLE_MANAGER.get(pId));
        pAsset13.map_mode = MetaTypeExtension.KingdomTitle;
        pAsset13.option_id = "map_KingdomTitle_layer";
        pAsset13.power_option_zone_id = "KingdomTitle_layer";
        pAsset13.click_action_zone = new MetaZoneClickAction(inspectKingdomTitle);
        pAsset13.selected_tab_action_meta = new MetaTypeActionAsset(defaultClickActionZone);
        pAsset13.check_unit_has_meta = (MetaCheckUnitWindowAction) (pActor => pActor.isKingdomCiv());
        pAsset13.set_unit_set_meta_for_meta_for_window = (MetaUnitSetMetaForWindow) (pActor => selected_kingdomTitle = pActor.city.GetTitle());
        pAsset13.draw_zones = (MetaZoneDrawAction) (pMetaTypeAsset =>
        {
	        switch (pMetaTypeAsset.getZoneOptionState())
	        {
		        case 0:
              foreach (var city in World.world.cities)
              { 
                if (city.hasTitle()) continue;
                drawDefaultMeta(city.meta_type_asset);
              }
			        drawDefaultMeta(pMetaTypeAsset);
              drawForCities(pMetaTypeAsset, WildKingdomsManager.neutral.getCities(), getZoneDelegate(pMetaTypeAsset));
			        break;
		        case 1:
			        foreach (var kt in ModClass.KINGDOM_TITLE_MANAGER)
			        {
				        foreach (City city in kt.getCities())
				        {
					        foreach (TileZone zone in city.zones)
					        {
						        zone_manager.drawBegin();
						        drawZoneKingdomTitleWithCityBorder(zone, 1);
						        zone_manager.drawEnd(zone);
					        }
				        }
			        }
			        break;
		        case 2:
			        drawDefaultFluid(pMetaTypeAsset);
			        break;
	        }
        });
        pAsset13.dynamic_zones = (MetaZoneDynamicAction) (() =>
        {
          List<Actor> simpleList = World.world.units.getSimpleList();
          double curWorldTime = World.world.getCurWorldTime();
          int index = 0;
          for (int count = simpleList.Count; index < count; ++index)
          {
            Actor actor = simpleList[index];
            if (actor.asset.show_on_meta_layer)
            {
              TileZone zone = actor.current_tile.zone;
              if (actor.hasCity())
                if (actor.city.hasTitle())
                  ZoneMetaDataVisualizer.countMetaZone(zone, (IMetaObject) actor.city.GetTitle(), curWorldTime);
            }
          }
        });
        pAsset13.check_cursor_highlight = (MetaZoneHighlightAction) ((pMetaTypeAsset, pTile, pQAsset) =>
        {
          Color color = pQAsset.color;
          if (pMetaTypeAsset.getZoneOptionState() is 0 or 1)
          {
            City city11 = pTile.zone.city;
            if (city11.isRekt())
              return;
            if (!city11.hasTitle())
              return;
            foreach (City city12 in city11.GetTitle()?.getCities()?? new List<City>())
              QuantumSpriteLibrary.colorZones(pQAsset, city12.zones, color);
          }
          else
            highlightDefault(pTile, pQAsset, color);
        });
        pAsset13.tile_get_metaobject = (MetaZoneGetMeta) ((pZone, pZoneOption) =>
        {
          City cityOnZone = pZone.city;
          if (cityOnZone==null)
            return null;
          return cityOnZone.hasTitle() ? cityOnZone.GetTitle(): null;
        });
        pAsset13.tile_get_metaobject_0 = (MetaZoneGetMetaSimple) (pZone =>
        {
          City city = pZone.city;
          return city?.GetTitle();
        });
        pAsset13.tile_get_metaobject_1 = (MetaZoneGetMetaSimple) (pZone => ZoneMetaDataVisualizer.getZoneMetaData(pZone).meta_object);
        pAsset13.tile_get_metaobject_2 = (MetaZoneGetMetaSimple) (pZone => ZoneMetaDataVisualizer.getZoneMetaData(pZone).meta_object);
        pAsset13.check_tile_has_meta = (MetaZoneTooltipAction) ((pZone, pAsset, pZoneOption) =>
        {
          IMetaObject metaObject = pAsset.tile_get_metaobject(pZone, pZoneOption);
          KingdomTitle kt = metaObject as KingdomTitle;
          if (kt == null) return false;
          return kt.isRekt();
        });
        pAsset13.check_cursor_tooltip = new MetaZoneTooltipAction(checkCursorTooltipDefault);
        pAsset13.cursor_tooltip_action = (MetaTooltipShowAction) (pMeta =>
        {
          KingdomTitle kingdomTitle = pMeta as KingdomTitle;
          if (kingdomTitle.isRekt())
            return;
          string str = "kingdomTitle";
          Tooltip.hideTooltip((object) kingdomTitle, true, str);
          Tooltip.show((object) kingdomTitle, str, new TooltipData()
          {
            city = kingdomTitle.title_capital,
            tooltip_scale = 0.7f,
            is_sim_tooltip = true
          });
        });
        pAsset13.stat_hover = (MetaStatAction) ((pMetaId, pField) =>
        {
          KingdomTitle pObject = ModClass.KINGDOM_TITLE_MANAGER.get(pMetaId);
          if (pObject.isRekt())
            return;
          Tooltip.show((object) pField, "kingdomTitle", new TooltipData()
          {
            city = pObject.title_capital
          });
        });
        pAsset13.stat_click = (MetaStatAction) ((pMetaId, _) =>
        {
          KingdomTitle pObject = ModClass.KINGDOM_TITLE_MANAGER.get(pMetaId);
          if (pObject.isRekt())
            return;
          selected_kingdomTitle = pObject;
          ScrollWindow.showWindow(nameof(KingdomTitleWindow));
        });
        kingdomTitle = AssetManager.meta_type_library.add(pAsset13);
    }
    public static void defaultClickActionZone(MetaTypeAsset pMetaTypeAsset)
    {
	    switch (pMetaTypeAsset.map_mode)
	    {
		    case MetaTypeExtension.Empire:
          SelectedMetas.selected_kingdom = selected_empire.CoreKingdom;
			    ScrollWindow.showWindow(nameof(EmpireWindow));
			    break;
		    case MetaTypeExtension.KingdomTitle:
			    ScrollWindow.showWindow(nameof(KingdomTitleWindow));
			    break;
	    }
    }  
    public static void drawDefaultFluid(MetaTypeAsset pMetaTypeAsset)
    {
        foreach (ZoneMetaData pData in ZoneMetaDataVisualizer.zone_data_dict.Values)
        {
            if (pData.meta_object != null && pData.meta_object.isAlive())
            {
                zone_manager.drawBegin();
                zone_manager.drawGenericFluid(pData, pMetaTypeAsset);
                zone_manager.drawEnd(pData.zone);
            }
        }
    }
    public static bool inspectEmpire(WorldTile pTile = null, string pPower = null)
    {
      if (pTile == null)
        return false;
      if (!pTile.hasCity()) return false;
      if (!pTile.zone_city.hasKingdom()) return false;
      var kingdom = pTile.zone_city.kingdom;
      Empire pEmpire = kingdom.IsInEmpire() ? kingdom.GetEmpire() : kingdom.GetTakenAllianceEmpire();
      if (pEmpire == null || pEmpire.isRekt() || pEmpire.IsArchived()) return false;
      selected_empire = pEmpire;
      SelectedMetas.selected_kingdom = selected_empire.CoreKingdom;
      ScrollWindow.showWindow(nameof(EmpireWindow));
      return true;
    }
    public static bool inspectKingdomTitle(WorldTile pTile = null, string pPower = null)
    {
      if (pTile == null)
        return false;
      if (!pTile.hasCity()) return false;
      if (!pTile.zone_city.hasTitle()) return false;
      if (pTile.zone_city.GetTitle().isRekt()) return false;
      KingdomTitle kt = pTile.zone_city.GetTitle();
      selected_kingdomTitle = kt;
      ScrollWindow.showWindow(nameof(KingdomTitleWindow));
      return true;
    }
    public static void highlightDefault(WorldTile pTile, QuantumSpriteAsset pQAsset, Color pColorAnimated)
    {
      ZoneMetaData zoneMetaData = ZoneMetaDataVisualizer.getZoneMetaData(pTile.zone);
      if (zoneMetaData.meta_object == null || !zoneMetaData.meta_object.isAlive())
        return;
      using (ListPool<TileZone> zonesWithMeta = ZoneMetaDataVisualizer.getZonesWithMeta(zoneMetaData.meta_object))
        QuantumSpriteLibrary.colorZones(pQAsset, zonesWithMeta, pColorAnimated);
    }
    public static void drawDefaultMeta(MetaTypeAsset pMetaTypeAsset)
    {
      MetaZoneGetMetaSimple zoneDelegate = getZoneDelegate(pMetaTypeAsset);
      foreach (Kingdom kingdom in (CoreSystemManager<Kingdom, KingdomData>) World.world.kingdoms)
        drawForCities(pMetaTypeAsset, kingdom.getCities(), zoneDelegate);
    }

    public static void drawForCities(
      MetaTypeAsset pMetaTypeAsset,
      IEnumerable<City> pListCities,
      MetaZoneGetMetaSimple pZoneGetDelegate)
    {
      foreach (City pListCity in pListCities)
        drawZonesForMeta(pListCity.meta_type_asset, pListCity.zones, pZoneGetDelegate);
    }  

    public static void drawForKingdoms(
      MetaTypeAsset pMetaTypeAsset,
      IEnumerable<Kingdom> pListKingdoms,
      MetaZoneGetMetaSimple pZoneGetDelegate)
    {
      foreach (Kingdom pListKingdom in pListKingdoms)
        foreach (City pListCity in pListKingdom.cities.ToList())
          drawZonesForMeta(pListKingdom.meta_type_asset, pListCity.zones, pZoneGetDelegate);
    }  
    public static void drawZonesForMeta(
      MetaTypeAsset pMetaTypeAsset,
      List<TileZone> pZones,
      MetaZoneGetMetaSimple pZoneGetDelegate)
    {
      foreach (TileZone pZone in pZones)
      {
        zone_manager.drawBegin();
        zone_manager.drawZoneMeta(pZone, pMetaTypeAsset, pZoneGetDelegate);
        zone_manager.drawEnd(pZone);
      }
    }
    public static MetaZoneGetMetaSimple getZoneDelegate(MetaTypeAsset pMetaTypeAsset)
    {
      switch (pMetaTypeAsset.getZoneOptionState())
      {
        case 0:
          return pMetaTypeAsset.tile_get_metaobject_0;
        case 1:
          return pMetaTypeAsset.tile_get_metaobject_1;
        case 2:
          return pMetaTypeAsset.tile_get_metaobject_2;
        default:
          return pMetaTypeAsset.tile_get_metaobject_2;
      }
    }
    public static bool checkCursorTooltipDefault(TileZone pTile, MetaTypeAsset pAsset, int pZoneOption)
    {
      IMetaObject pType = pAsset.tile_get_metaobject(pTile, pZoneOption);
      if (pType == null)
        return false;
      pAsset.cursor_tooltip_action(pType as NanoObject);
      return true;
    }
    
    public static void drawZoneEmpire(TileZone pZone, int pZoneOption = 0)
    {
      Empire empireOnZone = getEmpireOnZone(pZone);
      if (empireOnZone == null)
      {
        ((Kingdom) pZone.getKingdomOnZone(0)).EmpireLeave();
        return;
      };
      bool pUp = isBorderColor_empire(pZone.zone_up, empireOnZone, pZoneOption);
      bool pDown = isBorderColor_empire(pZone.zone_down, empireOnZone, pZoneOption);
      bool pLeft = isBorderColor_empire(pZone.zone_left, empireOnZone, pZoneOption);
      bool pRight = isBorderColor_empire(pZone.zone_right, empireOnZone, pZoneOption);
      zone_manager.drawZoneMeta(empireOnZone, pZone, pUp, pDown, pLeft, pRight, empireOnZone.data, empire);
    }
    public static Empire getEmpireOnZone(TileZone pZone)
    {
      Kingdom kingdom = pZone.city?.kingdom;
      if (kingdom == null) return null;
      if (kingdom.IsInEmpire()) return kingdom.GetEmpire();
      if (kingdom.HasTakenAlliance()) return kingdom.GetTakenAllianceEmpire();
	    return null;
    } 
    public static bool isBorderColor_empire(
        TileZone pZone,
        Empire pEmpire,
        int pZoneOption,
        bool pCheckFriendly = false)
    {
        if (pZone == null)
          return true;
        NanoObject empireOnZone = (NanoObject) getEmpireOnZone(pZone);
        return empireOnZone == null || empireOnZone != pEmpire;
    }
    
    public static void drawZoneKingdomTitleWithCityBorder(TileZone pZone, int pZoneOption = 0)
    { 
	      City cityOnZone = pZone.city;
        bool pUp = zone_manager.isBorderColor_cities(pZone.zone_up, cityOnZone);
        bool pDown = zone_manager.isBorderColor_cities(pZone.zone_down, cityOnZone);
        bool pLeft = zone_manager.isBorderColor_cities(pZone.zone_left, cityOnZone);
        bool pRight = zone_manager.isBorderColor_cities(pZone.zone_right, cityOnZone);
        KingdomTitle kt = cityOnZone.GetTitle();
        zone_manager.drawZoneMeta(kt, pZone, pUp, pDown, pLeft, pRight, kt.data, kingdomTitle);
    }
    
    public static void drawZoneEmpireWithKingdomBorder(TileZone pZone, Empire pEmpire)
    {
        Kingdom kingdomOnZone = pZone?.city?.kingdom;
        if (kingdomOnZone == null) return;
        bool pUp = isBorderColor_empire_kingdoms(pZone.zone_up, kingdomOnZone);
        bool pDown = isBorderColor_empire_kingdoms(pZone.zone_down, kingdomOnZone);
        bool pLeft = isBorderColor_empire_kingdoms(pZone.zone_left, kingdomOnZone);
        bool pRight = isBorderColor_empire_kingdoms(pZone.zone_right, kingdomOnZone);
        zone_manager.drawZoneMeta(pEmpire, pZone, pUp, pDown, pLeft, pRight, pEmpire.data, empire);
    }
    
    public static void drawZoneSelectedEmpireWithKingdomBorder(TileZone pZone, Kingdom pKingdom)
    {
        Kingdom kingdomOnZone = pZone?.city?.kingdom;
        if (kingdomOnZone == null) return;
        bool pUp = isBorderColor_empire_kingdoms(pZone.zone_up, kingdomOnZone);
        bool pDown = isBorderColor_empire_kingdoms(pZone.zone_down, kingdomOnZone);
        bool pLeft = isBorderColor_empire_kingdoms(pZone.zone_left, kingdomOnZone);
        bool pRight = isBorderColor_empire_kingdoms(pZone.zone_right, kingdomOnZone);
        zone_manager.drawZoneMeta(pKingdom, pZone, pUp, pDown, pLeft, pRight, pKingdom.data, empire);
    }
    
    
    public static bool isBorderColor_empire_kingdoms(
      TileZone pZone,
      Kingdom pKingdom)
    {
      if (pZone == null)
        return true;
      Kingdom kingdomOnZone = pZone.city?.kingdom;
      return kingdomOnZone == null || kingdomOnZone != pKingdom;
    }
}

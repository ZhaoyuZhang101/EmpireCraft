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
    public static EmpireCore selected_empireCore;
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
          int zoneOption = pMetaTypeAsset.getZoneOptionState();
          IMetaObject target = getEmpireLayerMetaObject(pTile?.zone, zoneOption);
          if (target is Kingdom kingdom)
          {
            highlightKingdomZones(kingdom, pQAsset, color);
            return;
          }
          if (target is Empire targetEmpire)
            highlightEmpireZones(targetEmpire, zoneOption, pQAsset, color);
        });
        pAsset13.tile_get_metaobject = (MetaZoneGetMeta) ((pZone, pZoneOption) =>
        {
          return getEmpireLayerMetaObject(pZone, pZoneOption);
        });
        pAsset13.tile_get_metaobject_0 = (MetaZoneGetMetaSimple) (pZone => getEmpireLayerMetaObject(pZone, 0));
        pAsset13.tile_get_metaobject_1 = (MetaZoneGetMetaSimple) (pZone => getEmpireLayerMetaObject(pZone, 1));
        pAsset13.tile_get_metaobject_2 = (MetaZoneGetMetaSimple) (pZone => getEmpireLayerMetaObject(pZone, 2));
        pAsset13.check_tile_has_meta = (MetaZoneTooltipAction) ((pZone, pAsset, pZoneOption) =>
        {
          IMetaObject metaObject = pAsset.tile_get_metaobject(pZone, pZoneOption);
          if (metaObject is Empire m) return !m.isRekt() && !m.IsArchived();
          if (metaObject is Kingdom k) return !k.isRekt() && !k.isNeutral();
          return false;
        });
        pAsset13.check_cursor_tooltip = new MetaZoneTooltipAction(checkCursorTooltipDefault);
        pAsset13.cursor_tooltip_action = (MetaTooltipShowAction) (pMeta =>
        {
          if (pMeta is Kingdom pKingdom)
          {
            if (pKingdom.isRekt() || pKingdom.isNeutral()) return;
            MetaType.Kingdom.getAsset().cursor_tooltip_action(pKingdom);
            return;
          }
          if (!(pMeta is Empire pEmpire) || pEmpire.isRekt() || pEmpire.IsArchived()) return;
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
        pAsset13.click_action_zone = new MetaZoneClickAction(inspectEmpireCoreOrKingdomTitle);
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
            if (pMetaTypeAsset.getZoneOptionState() == 0)
            {
              EmpireCore core = getTitleLayerEmpireCore(pTile.zone);
              if (core != null)
              {
                foreach (City city12 in EmpireCoreManager.GetCities(core))
                  QuantumSpriteLibrary.colorZones(pQAsset, city12.zones, color);
                return;
              }
            }
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
          return getKingdomTitleLayerMeta0(pZone);
        });
        pAsset13.tile_get_metaobject_1 = (MetaZoneGetMetaSimple) (pZone => ZoneMetaDataVisualizer.getZoneMetaData(pZone).meta_object);
        pAsset13.tile_get_metaobject_2 = (MetaZoneGetMetaSimple) (pZone => ZoneMetaDataVisualizer.getZoneMetaData(pZone).meta_object);
        pAsset13.check_tile_has_meta = (MetaZoneTooltipAction) ((pZone, pAsset, pZoneOption) =>
        {
          IMetaObject metaObject = pAsset.tile_get_metaobject(pZone, pZoneOption);
          Empire empireMeta = metaObject as Empire;
          if (empireMeta != null) return !empireMeta.isRekt() && !empireMeta.IsArchived();
          KingdomTitle kt = metaObject as KingdomTitle;
          if (kt == null) return false;
          return !kt.isRekt();
        });
        pAsset13.check_cursor_tooltip = (pZone, pAsset, pZoneOption) =>
        {
          if (pZoneOption == 0)
          {
            EmpireCore core = getTitleLayerEmpireCore(pZone);
            if (core != null)
            {
              Tooltip.hideTooltip((object) pZone.city, true, "empireCore");
              Tooltip.show((object) pZone.city, "empireCore", new TooltipData()
              {
                city = pZone?.city,
                tooltip_scale = 0.7f,
                is_sim_tooltip = true
              });
              return true;
            }
          }
          return checkCursorTooltipDefault(pZone, pAsset, pZoneOption);
        };
        pAsset13.cursor_tooltip_action = (MetaTooltipShowAction) (pMeta =>
        {
          if (pMeta is Empire empireMeta)
          {
            if (empireMeta.isRekt() || empireMeta.IsArchived())
              return;
            string empireStr = "empire";
            Tooltip.hideTooltip((object) empireMeta, true, empireStr);
            Tooltip.show((object) empireMeta, empireStr, new TooltipData()
            {
              kingdom = empireMeta.CoreKingdom,
              tooltip_scale = 0.7f,
              is_sim_tooltip = true
            });
            return;
          }
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
          Empire empireMeta = ModClass.EMPIRE_MANAGER.get(pMetaId);
          if (empireMeta != null && !empireMeta.isRekt() && !empireMeta.IsArchived())
          {
            Tooltip.show((object) pField, "empire", new TooltipData()
            {
              kingdom = empireMeta.CoreKingdom
            });
            return;
          }
          KingdomTitle pObject = ModClass.KINGDOM_TITLE_MANAGER.get(pMetaId);
          if (pObject == null || pObject.isRekt())
            return;
          Tooltip.show((object) pField, "kingdomTitle", new TooltipData()
          {
            city = pObject.title_capital
          });
        });
        pAsset13.stat_click = (MetaStatAction) ((pMetaId, _) =>
        {
          Empire empireMeta = ModClass.EMPIRE_MANAGER.get(pMetaId);
          if (empireMeta != null && !empireMeta.isRekt() && !empireMeta.IsArchived())
          {
            selected_empire = empireMeta;
            ScrollWindow.showWindow(nameof(EmpireWindow));
            return;
          }
          KingdomTitle pObject = ModClass.KINGDOM_TITLE_MANAGER.get(pMetaId);
          if (pObject == null || pObject.isRekt())
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
      if (pTile?.zone == null) return false;
      IMetaObject target = getEmpireLayerMetaObject(pTile.zone, empire.getZoneOptionState());
      if (target is Kingdom kingdom)
      {
        if (kingdom.isRekt() || kingdom.isNeutral()) return false;
        MetaType.Kingdom.getAsset().selectAndInspect(kingdom);
        return true;
      }
      if (!(target is Empire pEmpire) || pEmpire.isRekt() || pEmpire.IsArchived()) return false;
      pEmpire.SelectAndInspect();
      return true;
    }

    private static IMetaObject getEmpireLayerMetaObject(TileZone pZone, int pZoneOption)
    {
      Kingdom kingdom = pZone?.city?.kingdom;
      if (kingdom == null || kingdom.isRekt() || kingdom.isNeutral()) return null;

      if (pZoneOption == 0)
      {
        Empire memberEmpire = kingdom.IsInEmpire() ? kingdom.GetEmpire() : null;
        return memberEmpire != null && !memberEmpire.isRekt() && !memberEmpire.IsArchived()
          ? memberEmpire
          : kingdom;
      }

      Empire relatedEmpire = null;
      if (pZoneOption == 1)
      {
        relatedEmpire = kingdom.HasTakenAlliance()
          ? kingdom.GetTakenAllianceEmpire()
          : kingdom.IsInEmpire() ? kingdom.GetEmpire() : null;
      }
      else if (pZoneOption == 2)
      {
        if (kingdom.HasGivenAlliance())
        {
          relatedEmpire = kingdom.GetGivenAllianceEmpire();
        }
        else if (kingdom.IsInEmpire())
        {
          Empire ownEmpire = kingdom.GetEmpire();
          relatedEmpire = ownEmpire?.CoreKingdom?.HasGivenAlliance() == true
            ? ownEmpire.CoreKingdom.GetGivenAllianceEmpire()
            : ownEmpire;
        }
      }

      return relatedEmpire != null && !relatedEmpire.isRekt() && !relatedEmpire.IsArchived()
        ? relatedEmpire
        : kingdom;
    }

    private static void highlightKingdomZones(Kingdom kingdom, QuantumSpriteAsset pQAsset, Color color)
    {
      if (kingdom?.cities == null) return;
      foreach (City city in kingdom.cities)
      {
        if (city == null || city.isRekt()) continue;
        QuantumSpriteLibrary.colorZones(pQAsset, city.zones, color);
      }
    }

    private static void highlightEmpireZones(Empire empire, int pZoneOption, QuantumSpriteAsset pQAsset, Color color)
    {
      if (empire == null || empire.isRekt() || empire.IsArchived()) return;
      foreach (City city in empire.AllCities())
      {
        if (city == null || city.isRekt()) continue;
        QuantumSpriteLibrary.colorZones(pQAsset, city.zones, color);
      }

      List<Kingdom> associatedKingdoms = pZoneOption == 1
        ? empire.taken_Kingdoms
        : pZoneOption == 2 ? empire.given_Kingdoms : null;
      if (associatedKingdoms == null) return;
      foreach (Kingdom kingdom in associatedKingdoms)
        highlightKingdomZones(kingdom, pQAsset, color);
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
    public static bool inspectEmpireCoreOrKingdomTitle(WorldTile pTile = null, string pPower = null)
    {
      if (pTile?.zone == null) return false;
      if (kingdomTitle.getZoneOptionState() != 0)
      {
        return inspectKingdomTitle(pTile, pPower);
      }
      EmpireCore core = getTitleLayerEmpireCore(pTile.zone);
      if (core != null)
      {
        return inspectEmpireCore(pTile, pPower);
      }
      return inspectKingdomTitle(pTile, pPower);
    }

    public static bool inspectEmpireCore(WorldTile pTile = null, string pPower = null)
    {
      if (pTile?.zone == null) return false;
      EmpireCore core = getTitleLayerEmpireCore(pTile.zone);
      if (core == null) return false;
      selected_empireCore = core;
      ScrollWindow.showWindow(nameof(EmpireCoreWindow));
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

    private static IMetaObject getKingdomTitleLayerMeta0(TileZone pZone)
    {
      City city = pZone?.city;
      if (city == null) return null;
      KingdomTitle title = city.GetTitle();
      EmpireCore core = city.GetEmpireCore();
      if (core != null && title != null && EmpireCoreManager.ContainsTitle(core, title))
      {
        Empire empireMeta = ModClass.EMPIRE_MANAGER.get(core.empire_id);
        if (empireMeta != null && !empireMeta.isRekt() && !empireMeta.IsArchived())
        {
          return empireMeta;
        }
        // A de jure empire exists before (and after) its political empire. Use one shared
        // representative meta object so both fill colors and internal borders stay unified.
        return EmpireCoreManager.GetColorTitle(core) ?? title;
      }
      return title;
    }

    private static EmpireCore getTitleLayerEmpireCore(TileZone pZone)
    {
      City city = pZone?.city;
      if (city == null) return null;
      KingdomTitle title = city.GetTitle();
      EmpireCore core = city.GetEmpireCore();
      if (core == null || title == null) return null;
      return EmpireCoreManager.ContainsTitle(core, title) ? core : null;
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

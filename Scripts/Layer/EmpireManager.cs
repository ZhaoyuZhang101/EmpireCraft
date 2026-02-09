using db;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.HelperFunc;
using HarmonyLib;
using NeoModLoader.services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.Layer;

public class EmpireManager : MetaSystemManager<Empire, EmpireData>
{
    public EmpireManager() 
    {
        this.type_id = "empire";
    }
    public bool _dirty_cities = false;
    public override void updateDirtyUnits()
    {
    }

    public override void startCollectHistoryData()
    {
    }

    public override void update(float pElapsed)
    {
        base.update(pElapsed);
        // 创建集合副本进行遍历
        List<Empire> empiresToProcess = new List<Empire>(this);
        if (_lastStatsCacheTimestamp <= 0 || Date.getMonthsSince(_lastStatsCacheTimestamp) >= 1)
        {
            foreach (Empire e in empiresToProcess)
            {
                if (e.IsArchived()) continue;
                int pop = 0;
                int warriors = 0;
                int warriorsMax = 0;
                var tKingdoms = e.kingdoms_list;
                for (int i = 0; i < tKingdoms.Count; i++)
                {
                    var k = tKingdoms[i];
                    var ked0 = KingdomExtension.GetOrCreate(k);
                    ked0.last_cached_timestamp = -1L;
                    int kp = k.getPopulationPeople();
                    int kw = k.countTotalWarriors();
                    int km = k.countWarriorsMax();
                    pop += kp;
                    warriors += kw;
                    warriorsMax += km;
                    var ked = KingdomExtension.GetOrCreate(k);
                    ked.cached_population = kp;
                    ked.cached_warriors = kw;
                    ked.last_cached_timestamp = World.world.getCurWorldTime();
                    var cities = k.cities;
                    for (int j = 0; j < cities.Count; j++)
                    {
                        var c = cities[j];
                        var ced0 = CityExtension.GetOrCreate(c);
                        ced0.last_cached_timestamp = -1L;
                        var ced = CityExtension.GetOrCreate(c);
                        ced.cached_population = c.getPopulationPeople();
                        ced.cached_warriors = c.countWarriors();
                        ced.last_cached_timestamp = World.world.getCurWorldTime();
                    }
                }
                e.data.cached_population = pop;
                e.data.cached_warriors = warriors;
                e.data.cached_warriors_max = warriorsMax;
                e.data.last_cached_timestamp = World.world.getCurWorldTime();
            }
            _lastStatsCacheTimestamp = World.world.getCurWorldTime();
        }

        foreach (Empire current in empiresToProcess)
        {
            if (current.IsArchived()) continue;
            current.clearCursorOver();

            if (!current.checkActive())
            {
                _to_dissolve.Add(current);
            }
            else
            {
                current.update();
            }
        }
        // 处理需要解散的帝国
        foreach (Empire item in _to_dissolve)
        {
            dissolveEmpire(item);
        }
        _to_dissolve.Clear();
    }

    public void dissolveEmpire(Empire pEmpire)
    {
        if (pEmpire == null) return;
        pEmpire.dissolve();
        pEmpire.Dispose();
        pEmpire.Archive();
    }

    private List<Empire> _to_dissolve = new List<Empire>();

    public override void clear()
    {
        base.clear();
    }

    public List<TileZone> GetAllZones()
    {
        List<TileZone> zones = new();
        foreach (Empire e in this)
        {
            foreach(Kingdom k in e.kingdoms_list)
            {
                foreach(City c in k.cities)
                {
                    zones.AddRange(c.zones);
                }
            }
        }
        return zones;
    }

    public override void addObject(Empire pObject)
    {
        base.addObject(pObject);
        World.world.zone_calculator?.setDrawnZonesDirty();
    }
    public override void removeObject(Empire pKingdom)
    {
        base.removeObject(pKingdom);
        World.world.zone_calculator?.setDrawnZonesDirty();
    }
    
    public void RemoveArchivedEmpire(Empire pEmpire)
    {
        if (pEmpire == null) return;
        if (!pEmpire.IsArchived()) return;
        removeObject(pEmpire);
    }
    
    public void PurgeArchivedOlderThanYears(int years)
    {
        var list = this.ToList();
        foreach (var e in list)
        {
            if (!e.IsArchived()) continue;
            if (Date.getYearsSince(e.data.timestamp_established_time) > years)
            {
                removeObject(e);
            }
        }
    }

    // Token: 0x0400230C RID: 8972
    public Sprite[] _cached_banner_backgrounds;

    // Token: 0x0400230D RID: 8973
    public Sprite[] _cached_banner_icons;


    public Empire NewEmpire(Kingdom pKingdom, bool isSplit = false)
    {
        long id = OverallHelperFunc.IdGenerator.NextId();
        var empire = newObjectFromID(id);
        empire.CreateNewEmpire(pKingdom, isSplit);
        empire.addFounder(pKingdom);
        empire.updateColor(pKingdom.getColor());
        empire.data.timestamp_given_time = World.world.getCurWorldTime();
        pKingdom.GetOrCreate().isEmpire = true;
        pKingdom.GetOrCreate().EmpireID = empire.id;
        if (empire.data.has_year_name)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.become_new_empire_log, pKingdom.king.name, empire.GetEmpireName())
            {
                location = pKingdom.location,
                color_special1 = pKingdom.getColor().getColorText()
            }.add();
        }
        else
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.become_new_empire_west_log, pKingdom.king.name, empire.GetEmpireName())
            {
                location = pKingdom.location,
                color_special1 = pKingdom.getColor().getColorText()
            }.add();
        }
        
        return empire;
    }

    public bool forceEmpire(Kingdom pKingdom1, Kingdom pKingdom2)
    {
        Empire empire = ModClass.EMPIRE_MANAGER.get(pKingdom1.GetEmpireID());
        if (empire == null)
        {
            empire = ModClass.EMPIRE_MANAGER.get(pKingdom2.GetEmpireID());
        }
        bool result = false;
        if (empire == null)
        {
            empire = this.NewEmpire(pKingdom1);
            empire.join(pKingdom2);
            result = true;
        }
        else
        {
            empire.join(pKingdom1, true, true);
            empire.join(pKingdom2, true, true);
        }
        return result;
    }
    public Sprite[] getBackgroundsList()
    {
        if (_cached_banner_backgrounds == null)
        {
            _cached_banner_backgrounds = SpriteTextureLoader.getSpriteList("alliances/backgrounds/");
        }
        return _cached_banner_backgrounds;
    }

    public Sprite[] getIconsList()
    {
        if (_cached_banner_icons == null)
        {
            _cached_banner_icons = SpriteTextureLoader.getSpriteList("alliances/icons/");
        }
        return _cached_banner_icons;
    }
    
    private double _lastStatsCacheTimestamp = -1L;
}

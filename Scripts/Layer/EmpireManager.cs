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

        foreach (Empire current in empiresToProcess)
        {
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
        pEmpire.dissolve();
        pEmpire.Dispose();
        base.removeObject(pEmpire);
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
}
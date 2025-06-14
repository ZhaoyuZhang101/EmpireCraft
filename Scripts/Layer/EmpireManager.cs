using EmpireCraft.Scripts.GameClassExtensions;
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

    public override void updateDirtyUnits()
    {
    }

    public override void loadFromSave(List<EmpireData> pList)
    {
        LogService.LogInfo("禁止游戏的读取功能影响mod数据");
    }

    public override List<EmpireData> save(List<Empire> pList = null)
    {
        LogService.LogInfo("禁止游戏的存档功能影响mod数据");
        return null;
    }


    public override void update(float pElapsed)
    {
        base.update(pElapsed);
        foreach (Empire tAlliance in this)
        {
            if (!tAlliance.checkActive())
            {
                this._to_dissolve.Add(tAlliance);
            }
            else
            {
                tAlliance.update();
            }
        }
        foreach (Empire tAlliance2 in this._to_dissolve)
        {
            this.dissolveAlliance(tAlliance2);
        }
        this._to_dissolve.Clear();
    }

    public void dissolveAlliance(Empire pEmpire)
    {
        pEmpire.dissolve();
        this.removeObject(pEmpire);
    }

    private List<Empire> _to_dissolve = new List<Empire>();

    public override void clear()
    {
        base.clear();
    }

    public Sprite[] getIconsList()
    {
        if (this._cached_banner_icons == null)
        {
            this._cached_banner_icons = SpriteTextureLoader.getSpriteList("kingdoms/icons/");
        }
        return this._cached_banner_icons;
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

    public Sprite[] getBackgroundsList()
    {
        if (this._cached_banner_backgrounds == null)
        {
            this._cached_banner_backgrounds = SpriteTextureLoader.getSpriteList("kingdoms/backgrounds/");
        }
        return this._cached_banner_backgrounds;
    }

    // Token: 0x0400230C RID: 8972
    public Sprite[] _cached_banner_backgrounds;

    // Token: 0x0400230D RID: 8973
    public Sprite[] _cached_banner_icons;


    public Empire newEmpire(Kingdom pKingdom)
    {
        Empire empire = base.newObjectFromID(pKingdom.id);
        empire.createNewEmpire(pKingdom);
        empire.addFounder(pKingdom);
        empire.updateColor(pKingdom.getColor());
        LogService.LogInfo("创建帝国成功");
        return empire;
    }

    public void disvolveEmpire(Empire empire)
    {
        empire.dissolve();
        this.removeObject(empire);
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
            empire = this.newEmpire(pKingdom1);
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
}
using db;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.TipAndLog;
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
        foreach (Empire empire in this)
        {
            if (!empire.checkActive())
            {
                this._to_dissolve.Add(empire);
            }
            else
            {
                empire.update();
            }
        }
        foreach (Empire tAlliance2 in this._to_dissolve)
        {
            this.dissolveEmpire(tAlliance2);
        }
        this._to_dissolve.Clear();
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
        long id = OverallHelperFunc.IdGenerator.NextId();
        Empire empire;
        empire = base.newObjectFromID(id);
        empire.createNewEmpire(pKingdom);
        empire.addFounder(pKingdom);
        empire.updateColor(pKingdom.getColor());
        new WorldLogMessage(EmpireCraftWorldLogLibrary.become_new_empire_log, pKingdom.king.name, empire.GetEmpireName())
        {
            location = pKingdom.location,
            color_special1 = pKingdom.kingdomColor.getColorText()
        }.add();
        LogService.LogInfo("创建帝国成功");
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
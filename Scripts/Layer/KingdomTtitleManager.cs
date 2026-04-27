using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace EmpireCraft.Scripts.Layer;
public class KingdomTitleManager : MetaSystemManager<KingdomTitle, KingdomTitleData>
{
    public Sprite[] _cached_banner_backgrounds;

    public Sprite[] _cached_banner_icons;
    public KingdomTitleManager()
    {
        this.type_id = "kingdomTitle";
    }

    public override void updateDirtyUnits()
    {
    }
    public Sprite[] getBackgroundsList()
    {
        if (_cached_banner_backgrounds == null)
        {
            _cached_banner_backgrounds = SpriteTextureLoader.getSpriteList("kingdoms/backgrounds/");
        }
        return _cached_banner_backgrounds;
    }

    public Sprite[] getIconsList()
    {
        if (_cached_banner_icons == null)
        {
            _cached_banner_icons = SpriteTextureLoader.getSpriteList("kingdoms/icons/");
        }
        return _cached_banner_icons;
    }
    public override void startCollectHistoryData()
    {
    }
    public KingdomTitle newKingdomTitle(City pCity)
    {
        long id = OverallHelperFunc.IdGenerator.NextId();
        KingdomTitle title = base.newObjectFromID(id);
        title.newKingdomTitle(pCity);

        return title;
    }

    public bool checkTitleExist(long t) 
    {
        update(-1L);
        return get(t) != null;
    }

    public void AddCityToTitle(KingdomTitle pTitle, City pCity)
    {
        if (pTitle != null && pCity != null)
        {
            pTitle.addCity(pCity);
        }
    }
    public bool forceTitle(City pCity1, City pCity2)
    {
        KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER.get(pCity1.GetTitleID());
        if (title == null)
        {
            title = ModClass.KINGDOM_TITLE_MANAGER.get(pCity2.GetTitleID());
        }
        bool result = false;
        if (title == null)
        {
            title = ModClass.KINGDOM_TITLE_MANAGER.newKingdomTitle(pCity1);
            title.addCity(pCity2);
            result = true;
        }
        else
        {
            title.addCity(pCity1);
            title.addCity(pCity2);
        }
        return result;
    }


    public override void update(float pElapsed)
    {
        base.update(pElapsed);
        if (this.Count <= 0) return;
        foreach (KingdomTitle kt in this)
        {
            if (!kt.checkActive())
            {
                this._to_dissolve.Add(kt);
            }
        }
        foreach (KingdomTitle kt in this._to_dissolve)
        {
            this.dissolveTitle(kt);
        }
        this._to_dissolve.Clear();
    }

    public void dissolveTitle(KingdomTitle pkt)
    {
        pkt.Dissolve();
        pkt.Dispose();
        this.removeObject(pkt);
    }

    public override void removeObject(KingdomTitle pObject)
    {
        base.removeObject(pObject);
    }

    private List<KingdomTitle> _to_dissolve = new List<KingdomTitle>();
}

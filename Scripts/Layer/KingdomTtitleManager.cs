using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.Layer;
public class KingdomTitleManager : MetaSystemManager<KingdomTitle, KingdomTitleData>
{
    public KingdomTitleManager()
    {
        this.type_id = "empire";
    }

    public override void updateDirtyUnits()
    {
    }

    public KingdomTitle newKingdomTitle(City city)
    {
        long id = OverallHelperFunc.IdGenerator.NextId();
        KingdomTitle title = base.newObjectFromID(id);
        title.newKingdomTitle(city);
        return title;
    }

    public bool checkTitleExist(long t) 
    {
        update(-1L);
        return get(t) == null;
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
            title = this.newKingdomTitle(pCity1);
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
        pkt.disolve();
        pkt.Dispose();
        this.removeObject(pkt);
    }

    private List<KingdomTitle> _to_dissolve = new List<KingdomTitle>();
}
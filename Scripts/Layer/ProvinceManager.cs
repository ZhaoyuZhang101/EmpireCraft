using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.Layer;
public class ProvinceManager : MetaSystemManager<Province, ProvinceData>
{
    public ProvinceManager()
    {
        this.type_id = "province";
    }

    public override void updateDirtyUnits()
    {
    }

    public override void startCollectHistoryData()
    {
    }
    public Province newProvince(City city, string name="")
    {
        long id = OverallHelperFunc.IdGenerator.NextId();
        Province province = base.newObjectFromID(id);
        province.newProvince(city, name: name);
        province.updateColor(province.empire.getColor());
        if(city.isCapitalCity())
        {
            province.SetDirectRule();
        }
        LogService.LogInfo($"new province{province.data.name}");
        return province;
    }

    public bool checkProvinceExist(long t)
    {
        update(-1L);
        return get(t) != null;
    }

    public void AddCityToProvince(Province pProvince, City pCity)
    {
        if (pProvince != null && pCity != null)
        {
            pProvince.addCity(pCity);
        }
    }
    public bool forceProvince(City pCity1, City pCity2)
    {
        Province province = ModClass.PROVINCE_MANAGER.get(pCity1.GetProvinceID());
        if (province == null)
        {
            province = ModClass.PROVINCE_MANAGER.get(pCity2.GetProvinceID());
        }
        bool result = false;
        if (province == null)
        {
            province = this.newProvince(pCity1);
            province.addCity(pCity2);
            result = true;
        }
        else
        {
            province.addCity(pCity1);
            province.addCity(pCity2);
        }
        return result;
    }


    public override void update(float pElapsed)
    {
        base.update(pElapsed);
        foreach (Province p in this)
        {
            if (!p.checkActive())
            {
                this._to_dissolve.Add(p);
            }
        }
        foreach (Province p in this._to_dissolve)
        {
            this.dissolveProvince(p);
        }
        this._to_dissolve.Clear();
    }

    public void dissolveProvince(Province p)
    {
        p.disolve();
        p.Dispose();
        this.removeObject(p);
    }

    private List<Province> _to_dissolve = new List<Province>();
}
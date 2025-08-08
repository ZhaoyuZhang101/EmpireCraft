using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.Layer;
public class ModObjectManager : MetaSystemManager<ModObject, ModObjectData>
{
    public ModObjectManager()
    {
        this.type_id = "province";
    }

    public override void updateDirtyUnits()
    {
    }

    public override void startCollectHistoryData()
    {
    }
    public ModObject newModObject(City city)
    {
        long id = OverallHelperFunc.IdGenerator.NextId();
        ModObject modObject = base.newObjectFromID(id);
        modObject.newModObject(city);
        modObject.updateColor(city.getColor());
        LogService.LogInfo($"new object: {modObject.data.name}");
        return modObject;
    }

    public bool checkProvinceExist(long t)
    {
        update(-1L);
        return get(t) != null;
    }

    public void AddCityToModObject(ModObject pModObject, City pCity)
    {
        //todo: 将城市添加进模组实体
        if (pModObject != null && pCity != null)
        {
            pModObject.addCity(pCity);
        }
    }


    public override void update(float pElapsed)
    {
        base.update(pElapsed);
        foreach (ModObject p in this)
        {
            if (!p.checkActive())
            {
                this._to_dissolve.Add(p);
            }
        }
        foreach (ModObject p in this._to_dissolve)
        {
            this.dissolveModObject(p);
        }
        this._to_dissolve.Clear();
    }

    public void dissolveModObject(ModObject p)
    {
        p.dissolve();
        p.Dispose();
        this.removeObject(p);
    }

    private List<ModObject> _to_dissolve = new List<ModObject>();
}
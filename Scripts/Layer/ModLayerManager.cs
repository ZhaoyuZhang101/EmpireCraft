using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.Layer;
public class ModLayerManager : MetaSystemManager<ModLayer, ModLayerData>
{
    public ModLayerManager()
    {
        this.type_id = "ModLayer";
    }

    public override void updateDirtyUnits()
    {
    }

    public override void startCollectHistoryData()
    {
    }
    public ModLayer newModLayer(City city)
    {
        long id = OverallHelperFunc.IdGenerator.NextId();
        ModLayer ModLayer = base.newObjectFromID(id);
        ModLayer.newModLayer(city);
        ModLayer.updateColor(city.getColor());
        LogService.LogInfo($"new object: {ModLayer.data.name}");
        return ModLayer;
    }

    public bool checkProvinceExist(long t)
    {
        update(-1L);
        return get(t) != null;
    }

    public void AddCityToModLayer(ModLayer pModLayer, City pCity)
    {
        //todo: 将城市添加进模组实体
        if (pModLayer != null && pCity != null)
        {
            pModLayer.addCity(pCity);
        }
    }


    public override void update(float pElapsed)
    {
        base.update(pElapsed);
        foreach (ModLayer p in this)
        {
            if (!p.checkActive())
            {
                this._to_dissolve.Add(p);
            }
        }
        foreach (ModLayer p in this._to_dissolve)
        {
            this.DissolveModLayer(p);
        }
        this._to_dissolve.Clear();
    }

    public void DissolveModLayer(ModLayer p)
    {
        p.dissolve();
        p.Dispose();
        this.removeObject(p);
    }

    private List<ModLayer> _to_dissolve = new List<ModLayer>();
}
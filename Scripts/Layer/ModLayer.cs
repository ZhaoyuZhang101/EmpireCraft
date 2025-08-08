using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Numerics;
using System.Threading;
using JetBrains.Annotations;
using UnityEngine;

namespace EmpireCraft.Scripts.Layer;
// 此类为地图层级类的模板，如果需要创建自己的自定义层级，可以参考此方法，并依次建立三个类别，例如这个文件目录下的modlayer， modlayerdata以及modlayermanager
// 同时需要在 ModMapMode的enum类中添加新的地图模式，然后参考此模板中的其他所有和地图层级有关的设置并扩展他们
public class ModLayer : MetaObject<ModLayerData>
{
    public BannerAsset BannerAsset;
    public ModMapMode map_mode = ModMapMode.ModLayer;
    public UnityEngine.Vector3 last_center;
    public UnityEngine.Vector3 province_center;
    public List<City>  city_list = new List<City>();
    public HashSet<City>  city_list_hash = new HashSet<City>();
    private readonly List<TileZone> _zoneScratch = new();
    public KingdomAsset asset;
    //层级的核心城市
    public City CoreCity => World.world.cities.get(data.core_city);
    public override MetaType meta_type
    {
        get
        {
            return MetaType.None;
        }
    }
    
    public void newModLayer(City city)
    {
        // 初始化基础属性
        this.data.created_time = World.world.getCurWorldTime();

        // todo: 设置名称(可自定义命名逻辑)
        this.data.name = string.IsNullOrEmpty(name) ? city.name : name;

        // 设置资产和外观
        var kingdomAsset = city.kingdom.asset;
        this.asset = kingdomAsset ?? throw new InvalidOperationException("Kingdom asset not found");

        // 设置旗帜和颜色
        this.data.banner_icon_id = city.kingdom.data.banner_icon_id;
        this.data.banner_background_id = city.kingdom.data.banner_background_id;

        // 设置创始人信息
        this.data.founder_actor_id = city.kingdom.king.getID();
        this.data.founder_actor_name = city.kingdom.king.getName();
        this.data.original_actor_asset = city.kingdom.king.asset.id;
        this.data.color_id = city.kingdom?.data?.color_id ?? 0; // 默认颜色ID
        this.data.core_city = city.getID();
        this.updateColor(city.getColor());
        
        this.city_list_hash.Add(city);
        // 重新计算
        recalculate();
    }

    public Sprite getElementIcon()
    {
        return AssetManager.kingdom_banners_library.getSpriteIcon(data.banner_icon_id, getActorAsset().banner_id);
    }
    
    public Sprite getElementBackground()
    {
        return AssetManager.kingdom_banners_library.getSpriteBackground(data.banner_background_id, getActorAsset().banner_id);
    }


    public override ActorAsset getActorAsset()
    {
        return getFounderSpecies();
    }

    public ActorAsset getFounderSpecies()
    {
        return AssetManager.actor_library.get(data.original_actor_asset);
    }
    

    public int countZones()
    {
        return allZones().Count;
    }

    public override ColorLibrary getColorLibrary()
    {
        return AssetManager.kingdom_colors_library;
    }
    public UnityEngine.Vector3 GetCenter()
    {
        if (!this._units_dirty)
            return this.last_center;

        if (this.countZones() <= 0)
        {
            this.province_center = Globals.POINT_IN_VOID_2;
            return this.province_center;
        }
        float num = 0f;
        float num2 = 0f;
        float num3 = float.MaxValue;
        TileZone tileZone = null;
        var zones = this.allZones();
        for (int i = 0; i < zones.Count; i++)
        {
            TileZone tileZone2 = zones[i];
            num += tileZone2.centerTile.posV3.x;
            num2 += tileZone2.centerTile.posV3.y;
        }
        this.province_center.x = num / (float)zones.Count;
        this.province_center.y = num2 / (float)zones.Count;
        for (int j = 0; j < zones.Count; j++)
        {
            TileZone tileZone3 = zones[j];
            float num4 = Toolbox.SquaredDist((float)tileZone3.centerTile.x, (float)tileZone3.centerTile.y, this.province_center.x, this.province_center.y);
            if (num4 < num3)
            {
                tileZone = tileZone3;
                num3 = num4;
            }
        }
        this.province_center.x = tileZone.centerTile.posV3.x;
        this.province_center.y = tileZone.centerTile.posV3.y + 2f;
        this.last_center = this.province_center;
        this._units_dirty = false;
        return this.last_center;
    }

    public override void save()
    {
        //todo: 存储实体数据
        foreach (var city in city_list_hash)
        {
            this.data.cities.Add(city.getID());
        }
    }


    public List<TileZone> allZones()
    {
        List<TileZone> zones = new List<TileZone>();
        foreach (City city in city_list_hash)
        {
            foreach (TileZone tz in city.zones)
            {
                zones.Add(tz);
            }
        }
        return zones;
    }

    public void addCity(City city)
    {
        if (this.city_list.Contains(city)) return;
        if (city != null)
        {
            if (!this.city_list_hash.Contains(city))
            {
                this.city_list_hash.Add(city);
            }
            recalculate();
        }
    }

    public void removeCity(City city)
    {
        this.city_list_hash.Remove(city);
        this.recalculate();
        if (this.city_list_hash.Count <= 0)
        {
            ModClass.ModLayer_MANAGER.DissolveModLayer(this);
        }
    }
    
    public bool checkActive()
    {
        //todo: 检测模组实体是否存在
        return true;
    }
    public void recalculate()
    {
        this.city_list.Clear();
        this.city_list.AddRange(city_list_hash);
    }


    public override void Dispose()
    {
        //清空数据
        this.city_list.Clear();
        this.city_list_hash.Clear();
    }

    public void dissolve()
    {
        //todo: 处理实体销毁后的逻辑
    }
    

    public override void loadData(ModLayerData pData)
    {
        //todo: 读档逻辑
        base.loadData(pData);
        this.city_list_hash = new HashSet<City>();
        this.city_list = new List<City>();
        foreach (long city_id in pData.cities)
        {
            this.city_list_hash.Add(World.world.cities.get(city_id));
        }
        recalculate();
    }
}


using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.General.UI.Tab;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Assertions;

namespace EmpireCraft.Scripts.Layer;
public class KingdomTitle : MetaObject<KingdomTitleData>
{
    public BannerAsset BannerAsset;
    public HashSet<City> city_list_hash = new HashSet<City>();
    public List<City> city_list = new List<City>();
    public EmpireCraftMapMode map_mode = EmpireCraftMapMode.Title;
    public Vector3 last_center;
    public Vector3 title_center;
    private readonly List<TileZone> _zoneScratch = new();
    public KingdomAsset asset;
    public City title_capital;
    public Kingdom control_kingdom;
    public Kingdom main_kingdom;

    public Actor owner;
    public ColorAsset kingdomColor => getColor();
    public override MetaType meta_type
    {
        get
        {
            return MetaType.None;
        }
    }

    public int GetTitleBeenControlledYear()
    {
        if (this.data == null) return 0;
        if (this.data.timestamp_been_controled <= 0) return 0;
        return Date.getYearsSince(this.data.timestamp_been_controled);
    }
    public void newKingdomTitle(City city)
    {
        this.addCity(city);
        this.asset = AssetManager.kingdoms.get(city.kingdom.king.asset.kingdom_id_civilization);
        this.updateColor(getColorLibrary().getNextColor());
        this.title_capital = city;
        this.data.founder_actor_id = city.kingdom.king.getID();
        this.data.founder_actor_name = city.kingdom.king.getName();
        this.data.created_time = World.world.getCurWorldTime();
        this.data.banner_icon_id = city.kingdom.data.banner_icon_id;
        this.data.banner_background_id = city.kingdom.data.banner_background_id;
        this.data.original_actor_asset = city.kingdom.king.asset.id;
        this.owner = null;
        string name = city.SelectKingdomName();
        data.province_name = title_capital.GetCityName();
        if (name != null&& name != "") 
        {
            this.data.name = name;
        } else
        {
            this.data.name = city.kingdom.GetKingdomName();
        }
        recalculate();
    }

    public bool HasOwner()
    {
        if (this.owner == null) return false;
        if (!this.owner.isAlive()) return false;
        return true;
    }

    public override ColorAsset getColor()
    {
        if (_cached_color == null)
        {
            _cached_color = getColorLibrary().list[data.color_id];
        }

        return _cached_color;
    }

    public Sprite getElementIcon()
    {
        return AssetManager.kingdom_banners_library.getSpriteIcon(data.banner_icon_id, getActorAsset().banner_id);
    }

    public int countPopulation()
    {
        int res = 0;
        foreach(City city in city_list_hash)
        {
            res += city.getPopulationPeople();
        }
        return res;
    }

    public int countWarriors()
    {
        int res = 0;
        foreach (City city in city_list_hash)
        {
            res += city.getMaxWarriors();
        }
        return res;
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
        int tResult = 0;
        List<City> tCities = this.city_list;
        for (int i = 0; i < tCities.Count; i++)
        {
            City tCity = tCities[i];
            tResult += tCity.countZones();
        }
        return tResult;
    }

    public override ColorLibrary getColorLibrary()
    {
        return AssetManager.kingdom_colors_library;
    }
    public Vector3 GetCenter()
    {
        if (!this._units_dirty)
            return this.last_center;

        if (this.countZones() <= 0)
        {
            this.title_center = Globals.POINT_IN_VOID_2;
            return this.title_center;
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
        this.title_center.x = num / (float)zones.Count;
        this.title_center.y = num2 / (float)zones.Count;
        for (int j = 0; j < zones.Count; j++)
        {
            TileZone tileZone3 = zones[j];
            float num4 = Toolbox.SquaredDist((float)tileZone3.centerTile.x, (float)tileZone3.centerTile.y, this.title_center.x, this.title_center.y);
            if (num4 < num3)
            {
                tileZone = tileZone3;
                num3 = num4;
            }
        }
        this.title_center.x = tileZone.centerTile.posV3.x;
        this.title_center.y = tileZone.centerTile.posV3.y + 2f;
        this.last_center = this.title_center;
        this._units_dirty = false;
        return this.last_center;
    }

    public bool isBeenControlled()
    {
        Kingdom kingdom = null;
        foreach (City c in city_list)
        {
            if (c.isAlive()&&!c.isNeutral())
            {
                if (kingdom == null)
                {
                    kingdom = c.kingdom;
                }else
                {
                    if(c.kingdom != kingdom)
                    {
                        this.data.timestamp_been_controled = -1L;
                        return false;
                    }
                    else
                    {
                        kingdom = c.kingdom;
                    }
                }
            }
        }
        if (control_kingdom!=kingdom)
        {
            data.timestamp_been_controled = World.world.getCurWorldTime();
            control_kingdom = kingdom;
        }
        return true;
    }

    public override void save()
    {
        if (this.data == null) return;
        this.data.cities = new List<long>();
        foreach (City city in city_list_hash)
        {
            this.data.cities.Add(city.data.id);
        }
        this.data.title_capital = this.title_capital.data.id;
        try
        {
            this.data.owner = this.owner == null ? -1L : this.owner.data.id;
        }
        catch
        {
            this.data.owner = -1L;
        }
        if (main_kingdom!=null)
        {
            if (main_kingdom.data == null)
            {
                main_kingdom = null;
                this.data.main_kingdom = -1L;
            } else
            {
                this.data.main_kingdom = main_kingdom.data.id;
            }

        }
        else
        {
            this.data.main_kingdom = -1L;
        }

    }

    public List<TileZone> allZones()
    {
        List<TileZone> zones= new List<TileZone>();
        foreach(City city in  city_list_hash)
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
        if (city != null)
        {
            if (city.hasTitle())
            {
                KingdomTitle oldTitle = city.GetTitle();
                oldTitle.removeCity(city);
            }
            city.SetTitle(this);
            if (!this.city_list_hash.Contains(city))
            {
                this.city_list_hash.Add(city);
            }
            recalculate();
        }
    }

    public void removeCity(City city)
    {

        city.RemoveTitle();
        this.city_list_hash.Remove(city);
        this.recalculate();
        if (this.city_list_hash.Count <= 0)
        {
            ModClass.KINGDOM_TITLE_MANAGER.dissolveTitle(this);
        }
    }
    // Token: 0x06001124 RID: 4388 RVA: 0x000C7748 File Offset: 0x000C5948
    public bool checkActive()
    {
        bool tChanged = false;
        if (this.data == null)
        {
            return false;
        }
        List<City> cities = this.city_list;
        if (cities.Count <= 0)
        {
            return false;
        }
        List<City> needRemove = new List<City>();
        foreach (City city in cities) 
        {
            if (!city.isAlive())
            {
                needRemove.Add(city);
                tChanged = true;
            }
        }
        foreach (City city in needRemove) 
        {
            city.RemoveTitle();
            this.city_list.Remove(city);
        }
        if (this.title_capital==null)
        {
            this.title_capital = city_list.First();
        }
        if (!this.title_capital.isAlive())
        {
            this.title_capital = city_list.First();
        }
        if (tChanged)
        {
            this.recalculate();
        } 
        return this.city_list.Count >= 1;
    }
    public void recalculate()
    {
        this.city_list.Clear();
        this.city_list.AddRange(city_list_hash);
    }


    public override void Dispose()
    {
        this.city_list.Clear();
        this.city_list_hash.Clear();
    }

    public void disolve()
    {
        foreach (City city in city_list_hash)
        {
            city.RemoveTitle();
        }
    }

    public override void loadData(KingdomTitleData pData)
    {
        base.loadData(pData);
        this.city_list_hash.Clear();
        this.city_list.Clear();
        foreach (long city_id in pData.cities)
        {
            this.city_list_hash.Add(World.world.cities.get(city_id));
        }
        this.title_capital = World.world.cities.get(this.data.title_capital);
        this.city_list.AddRange(this.city_list_hash);
        this.owner = this.data.owner == -1L? null:World.world.units.get(this.data.owner);
        this.main_kingdom = this.data.main_kingdom == -1L ? null : World.world.kingdoms.get(this.data.main_kingdom);
        if(pData.province_name==""||pData.province_name==null)
        {
            pData.province_name = title_capital.GetCityName();
        } 
    }
}


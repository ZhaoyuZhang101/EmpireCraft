using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.General;
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
public class Province : MetaObject<ProvinceData>
{
    public BannerAsset BannerAsset;
    public HashSet<City> city_list_hash = new HashSet<City>();
    public List<City> city_list = new List<City>();
    public EmpireCraftMapMode map_mode = EmpireCraftMapMode.Province;
    public Vector3 last_center;
    public Vector3 province_center;
    private readonly List<TileZone> _zoneScratch = new();
    public KingdomAsset asset;
    public City province_capital;
    public Empire empire;
    public Actor officer;
    public ColorAsset kingdomColor => getColor();
    public override MetaType meta_type
    {
        get
        {
            return MetaType.None;
        }
    }

    public void SetOfficer(Actor actor)
    {
        officer = actor;
        actor.SetProvinceID(this.getID());
        this.data.history_officers.Add(actor.getName());
    }
    public void RemoveOfficer(Actor actor)
    {
        actor.RemoveProvinceID();
        officer = null;
    }

    public void SetProvinceLevel(provinceLevel provincelevel)
    {
        string level = provincelevel.ToString().Split('_').Last();
        string province_level_name = "provincelevel";
        string province_level_string = "";
        if (ConfigData.speciesCulturePair.TryGetValue(empire.empire.getSpecies(), out string culture))
        {
            province_level_string = String.Join("_", culture, province_level_name, level);
        }
        else
        {
            province_level_string = String.Join("_", "Western", province_level_name, level);
        }
        this.data.name = this.data.name + ' ' + LM.Get(province_level_string);
        this.data.provinceLevel = provincelevel;
    }

    public void SetDirectRule()
    {
        this.data.provinceLevel = provinceLevel.provincelevel_0;
        this.data.isDirectRule = true;
        string preName;
        string postName;
        if (ConfigData.speciesCulturePair.TryGetValue(empire.empire.getSpecies(),out string culture))
        {
            preName = String.Join("_", culture, "capital");
            postName = String.Join("_", culture, "provincelevel", "0");
        } else
        {
            preName = String.Join("_", "Western", "capital");
            postName = String.Join("_", "Western", "provincelevel", "0");
        }
        this.data.name = LM.Get(preName) + " " + LM.Get(postName);
    }

    public void newProvince(City city, provinceLevel provinceLevel=provinceLevel.provincelevel_4)
    {
        this.empire = city.kingdom.GetEmpire();
        this.data.provinceLevel = provinceLevel;
        this.addCity(city);
        this.asset = AssetManager.kingdoms.get(city.kingdom.king.asset.kingdom_id_civilization);
        this.updateColor(getColorLibrary().getNextColor());
        this.province_capital = city;
        this.data.founder_actor_id = city.kingdom.king.getID();
        this.data.founder_actor_name = city.kingdom.king.getName();
        this.data.created_time = World.world.getCurWorldTime();
        this.data.banner_icon_id = city.kingdom.data.banner_icon_id;
        this.data.banner_background_id = city.kingdom.data.banner_background_id;
        this.data.original_actor_asset = city.kingdom.king.asset.id;
        this.officer = null;
        this.data.name = city.GetCityName();
        this.data.history_officers = new List<string> { };
        SetProvinceLevel(provinceLevel);
        recalculate();
        SetOfficer(city.leader);
        city.removeLeader();
    }

    public bool selectOfficerFromCityClan(City city)
    {
        if (city.getRoyalClan() != null)
        {
            Actor nominate = null;
            foreach (Actor a in city.getRoyalClan().units)
            {
                if (!a.isOfficer())
                {
                    if (nominate == null)
                    {
                        nominate = a;
                    }
                    else
                    {
                        var nominateAbility = nominate.CalculateAbility();
                        var aAbility = a.CalculateAbility();
                        if ((aAbility.wen + aAbility.wu) > (nominateAbility.wen + nominateAbility.wu))
                        {
                            nominate = a;
                        }
                    }
                }
            }
            if (nominate != null)
            {
                this.SetOfficer(nominate);
                return true;
            }
        }
        return false;
    }

    public bool HasOfficer()
    {
        if (this.officer == null) return false;
        if (!this.officer.isAlive()) return false;
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
        foreach (City city in city_list_hash)
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
        if (this.data == null) return;
        this.data.cities = new List<long>();
        foreach (City city in city_list_hash)
        {
            this.data.cities.Add(city.data.id);
        }
        this.data.province_capital = this.province_capital.data.id;
        try
        {
            this.data.officer = this.officer == null ? -1L : this.officer.data.id;
        }
        catch
        {
            this.data.officer = -1L;
        }
        this.data.empire = this.empire.getID();
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
        if (city != null)
        {
            if (city.hasProvince())
            {
                Province oldProvince = city.GetProvince();
                oldProvince.removeCity(city);
            }
            city.SetProvince(this);
            if (!this.city_list_hash.Contains(city))
            {
                this.city_list_hash.Add(city);
            }
            recalculate();
        }
    }

    public void removeCity(City city)
    {
        city.RemoveProvince();
        this.city_list_hash.Remove(city);
        this.recalculate();
        if (this.city_list_hash.Count <= 0)
        {
            ModClass.PROVINCE_MANAGER.dissolveProvince(this);
        } 
        else
        {
            this.province_capital = city_list_hash.GetRandom();
        }
        if (city.isCapitalCity())
        {
            this.data.name = city.GetCityName();
            this.SetProvinceLevel(provinceLevel.provincelevel_4);
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
            city.RemoveProvince();
            this.city_list.Remove(city);
        }
        if (this.province_capital == null)
        {
            this.province_capital = city_list.FirstOrDefault();
        }
        if (!this.province_capital.isAlive())
        {
            this.province_capital = city_list.FirstOrDefault();
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
        if (this.empire == null) return;
        if (this.empire.province_list != null)
        {
            if (this.empire.province_list.Contains(this))
            {
                this.empire.province_list.Remove(this);
            }
        }

    }

    public void disolve()
    {
        foreach (City city in city_list_hash)
        {
            city.RemoveProvince();
        }
    }

    public override void loadData(ProvinceData pData)
    {
        base.loadData(pData);
        this.city_list_hash.Clear();
        this.city_list.Clear();
        foreach (long city_id in pData.cities)
        {
            this.city_list_hash.Add(World.world.cities.get(city_id));
        }
        this.province_capital = World.world.cities.get(this.data.province_capital);
        this.city_list.AddRange(this.city_list_hash);
        this.officer = this.data.officer == -1L ? null : World.world.units.get(this.data.officer);
    }
}


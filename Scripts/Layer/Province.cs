using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Numerics;
using System.Threading;
using UnityEngine;

namespace EmpireCraft.Scripts.Layer;
public class Province : MetaObject<ProvinceData>
{
    public BannerAsset BannerAsset;
    public HashSet<City> city_list_hash = new HashSet<City>();
    public List<City> city_list = new List<City>();
    public EmpireCraftMapMode map_mode = EmpireCraftMapMode.Province;
    public UnityEngine.Vector3 last_center;
    public UnityEngine.Vector3 province_center;
    private readonly List<TileZone> _zoneScratch = new();
    public KingdomAsset asset;
    public City province_capital;
    public Empire empire;
    public Dictionary<City, double> occupied_cities = new Dictionary<City, double>();
    public Actor officer;
    public ColorAsset kingdomColor => getColor();
    public override MetaType meta_type
    {
        get
        {
            return MetaType.None;
        }
    }

    public bool isBorderProvince()
    {
        if (this.data.isDirectRule) return false;
        foreach (City city in city_list)
        {
            if (city.neighbours_kingdoms.Count > 0)
            {
                foreach (Kingdom kingdom in city.neighbours_kingdoms)
                {
                    if (!kingdom.isInEmpire())
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    public bool isNeighbourWith(Province province = null, Kingdom kingdom = null)
    {
        if (province != null){
            foreach (City city in city_list)
            {
                foreach(City city2 in city.neighbours_cities)
                {
                    if (city2.hasProvince())
                    {
                        if (city2.GetProvince().Equals(province))
                        {
                            return true;
                        }
                    }
                }
            }
        } else 
        if (kingdom != null){
            foreach (City city in city_list)
            {
                foreach(City city2 in city.neighbours_cities)
                {
                    if (city2.kingdom!=null)
                    {
                        if (city2.kingdom.Equals(kingdom))
                        {
                            return true;
                        }
                    }
                }
            }
        }
        return false;
    }


    public bool IsTotalVassaled()
    {
        if (occupied_cities==null) return false;
        if (occupied_cities.Count<=0) return false;
        foreach(City city in city_list)
        {
            if (!occupied_cities.ContainsKey(city))
            {
                return false;
            }
        }
        return true;
    }
    public void updateOccupied()
    {
        foreach (City city in city_list)
        {
            if(city.kingdom.isInEmpire())
            {
                Empire empire = city.kingdom.GetEmpire();
                if (empire!=this.empire)
                {
                    AddOccupiedCity(city);
                } else
                {
                    RemoveOccupiedCity(city);
                }
            } else
            {
                AddOccupiedCity(city);
            }
        }
    }

    public void AddOccupiedCity(City city)
    {
        if (this.occupied_cities.ContainsKey(city))
        {
            this.occupied_cities[city] = World.world.getCurSessionTime();
        } else
        {
            this.occupied_cities.Add(city, World.world.getCurSessionTime());
        }
    }
    public void RemoveOccupiedCity(City city)
    {
        if (this.occupied_cities.ContainsKey(city))
        {
            this.occupied_cities.Remove(city);
        }
    }

    public List<Actor> allGongshi()
    {
        List<Actor> list = new List<Actor>();
        foreach (City city in city_list)
        {
            list.AddRange(city.units.FindAll(a => a.hasTrait("gongshi")));
        }
        return list;
    }

    public void SetOfficer(Actor actor)
    {
        if (actor == null) return;

        if (officer != null) 
        {
            officer.RemoveProvinceID();
            officer = null;
        }
        officer = actor;
        if (officer.isCityLeader())
        {
            officer.city.removeLeader();
        }
        if (officer.city!=province_capital)
        {
            if (occupied_cities.ContainsKey(province_capital))
            {
                foreach (City city in city_list)
                {
                    if (occupied_cities.ContainsKey(city)) continue;
                    officer.setCity(city);
                    break;
                }
            } else
            {
                officer.setCity(province_capital);
            }
        }
        OfficeIdentity identity = officer.GetIdentity(empire);
        if (identity == null)
        {
            identity = new OfficeIdentity();
            identity.init(empire, officer);
            officer.SetIdentity(identity, true);
        }
        officer.SetIdentityType();
        officer.ChangeOfficialLevel(OfficialLevel.officiallevel_8);
        officer.UpgradeOfficial(direct: 6);
        officer.addTrait("officer");
        officer.SetProvinceID(this.getID());
        this.data.history_officers.Add(actor.getName());
        this.data.new_officer_timestamp = World.world.getCurWorldTime();
    }

    public void JudgeOfficer()
    {
        ListPool<Actor> gongActors = new ListPool<Actor>();
        ListPool<Actor> jingActors = new ListPool<Actor>();
        ListPool<Actor> royalActors = new ListPool<Actor>();
        foreach (Kingdom kingdom in empire.kingdoms_hashset)
        {
            foreach (Actor actor in kingdom.getUnits())
            {
                if (actor.isUnitFitToRule() && actor.hasTrait("jingshi") && !actor.isKing() && !actor.isOfficer() &&actor.isAdult())
                {
                    jingActors.Add(actor);
                }
                if (actor.isUnitFitToRule() && actor.hasTrait("gongshi") && !actor.isKing() && !actor.isOfficer() && actor.isAdult())
                {
                    gongActors.Add(actor);
                }
                if (actor.isUnitFitToRule() && actor.hasClan() && actor.isAdult())
                {
                    if (actor.clan == empire.empire.getKingClan()&& !actor.isKing()&&!actor.isOfficer())
                    {
                        royalActors.Add(actor);
                    }
                }
            }
        }
        if (GetNewOfficerOnTime() >= 16)
        {
            bool flag = false;
            if(this.officer!=null)
            {
                if(this.officer.isUnitFitToRule())
                {
                    if(this.empire!=null)
                    {
                        if (this.empire.emperor!=null)
                        {
                            if (this.officer.renown > this.empire.emperor.renown)
                            {
                                this.data.new_officer_timestamp = World.world.getCurWorldTime();
                                flag = true;
                                LogService.LogInfo(this.officer.data.name + "连任成功");
                            }
                        }
                    }
                }
            }
            if (!flag)
            {
                RemoveOfficer(true);
            }
        }
        if (officer == null)
        {
            Actor final = null;
            if (jingActors.Count > 0)
            {
                final = jingActors.First();
                jingActors.Remove(final);
                SetOfficer(final);
            }
            else if (gongActors.Count > 0)
            {
                final = gongActors.First();
                gongActors.Remove(final);
                SetOfficer(final);
            }
            else if (royalActors.Count > 0)
            {
                royalActors.Sort(ListSorters.sortUnitByAgeOldFirst);
                final = royalActors.First();
                royalActors.Remove(final);
                SetOfficer(final);
            }
            if (final == null)
            {
                if (this.occupied_cities.ContainsKey(province_capital))
                {
                    foreach(City city in city_list)
                    {
                        if(!this.occupied_cities.ContainsKey(city)) 
                        {
                            SetOfficer(city.leader);
                        }
                    }
                } else
                {
                    if (province_capital.hasLeader())
                    {
                        SetOfficer(province_capital.leader);
                    }
                    else
                    {
                        officer = null;
                    }
                }
            }

        }

    }

    public int GetNewOfficerOnTime()
    {
        if (this.data.new_officer_timestamp==-1L)
        {
            return 0;
        }
        return Date.getYearsSince(this.data.new_officer_timestamp);
    }

    public void RemoveOfficer(bool is_retire = false)
    {
        if (officer != null)
        {
            officer.RemoveProvinceID();
            officer.SetIdentityType();
            officer.ChangeOfficialLevel(OfficialLevel.officiallevel_10);
            if (is_retire)
            {
                officer.addTrait("officerLeave");
            }
        }
        officer = null;
    }

    public string GetProvinceName()
    {
        return this.data.name.Split(' ')[0];
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
        this.data.name = this.GetProvinceName() + ' ' + LM.Get(province_level_string);
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

    public void newProvince(City city, provinceLevel provinceLevel=provinceLevel.provincelevel_3, string name="")
    {
        this.data.isDirectRule = false;
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
        this.data.color_id = empire.empire.data.color_id;
        this.officer = null;
        this.data.is_set_to_country = false;
        if (name == "")
        {
            this.data.name = city.GetCityName();
        } else
        {
            this.data.name = name;
        }
        this.data.history_officers = new List<string> { };
        SetProvinceLevel(provinceLevel);
        recalculate();
        this.empire.province_list.Add(this);
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
        if (this.data == null) return;
        this.data.cities = new List<long>();
        foreach (City city in city_list_hash)
        {
            this.data.cities.Add(city.data.id);
        }
        this.data.province_capital = this.province_capital.getID();
        try
        {
            this.data.officer = this.officer == null ? -1L : this.officer.data.id;
        }
        catch
        {
            this.data.officer = -1L;
        }

        foreach(var pair in this.occupied_cities)
        {
            if (!this.data.occupied_cities.ContainsKey( pair.Key.getID()))
            {
                this.data.occupied_cities.Add(pair.Key.getID(), pair.Value);
            }
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
        if (this.city_list.Contains(city)) return;
        if (city != null)
        {
            if (city.hasProvince())
            {
                Province oldProvince = city.GetProvince();
                oldProvince.removeCity(city);
            }
            if (city.isCapitalCity()) 
            {
                this.SetDirectRule();
                this.province_capital = city;
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
            this.SetProvinceLevel(provinceLevel.provincelevel_3);
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
        if (this.data.is_set_to_country)
        {
            foreach (City city in this.city_list)
            {
                Kingdom kingdom = city.kingdom;
                if (kingdom != null) 
                {
                    if(kingdom.GetProvince() == this)
                    {
                        kingdom.RemoveProvince();
                    }
                }
            }
        }
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

    public bool canStartExam()
    {
        foreach(City city in this.city_list)
        {
            if (city.GetExamPassPersons().Count()<=0)
            {
                return false;
            }
        }
        return true;
    }

    public void disolve()
    {
        foreach (City city in city_list_hash)
        {
            city.RemoveProvince();
        }
    }

    public bool needToBecomeKingdom()
    {
        int officerRenown = 0;
        int emperorRenown = 0;
        if(this.HasOfficer())
        {
            officerRenown = this.officer.renown;
        } 
        if (this.empire.emperor!=null)
        {
            emperorRenown = this.empire.emperor.renown;
        }
        if(this.occupied_cities!=null)
        {
            if(this.occupied_cities.Count>0)
            {
                return false;
            }
        }
        return (!this.data.isDirectRule&& officer.renown>=emperorRenown);
    }

    public Kingdom becomeKingdom()
    {
        SetProvinceLevel(provinceLevel.provincelevel_2);
        Kingdom pKingdom = this.empire.empire;
        Kingdom kingdom = World.world.kingdoms.makeNewCivKingdom(this.officer, pLog: false);
        foreach (City city in city_list_hash)
        {
            city.removeFromCurrentKingdom();
            city.newForceKingdomEvent(base.units, city._boats, kingdom, null);
            city.setKingdom(kingdom);
            city.switchedKingdom();
            kingdom.setCityMetas(city);
            this.officer.joinCity(city);
            if (city.hasProvince())
            {
                Province province = city.GetProvince();
                if (province != null)
                {
                    if (province.occupied_cities != null)
                    {
                        RemoveOccupiedCity(city);
                    }
                }
            }
        }
        kingdom.SetCountryLevel(countryLevel.countrylevel_2);
        kingdom.SetProvince(this);
        this.empire.join(kingdom);
        kingdom.copyMetasFromOtherKingdom(pKingdom);
        TranslateHelper.LogProvinceChangeToKingdom(this, provinceLevel.provincelevel_2);
        this.data.is_set_to_country = true;
        kingdom.data.name = this.data.name;
        return kingdom;
    }

    public override void loadData(ProvinceData pData)
    {
        base.loadData(pData);
        this.city_list_hash = new HashSet<City>();
        this.city_list = new List<City>();
        foreach (long city_id in pData.cities)
        {
            this.city_list_hash.Add(World.world.cities.get(city_id));
        }
        this.province_capital = World.world.cities.get(this.data.province_capital);
        recalculate();
        if (this.data.officer != -1L)
        {
            Actor actor = World.world.units.get(this.data.officer);
            if (actor != null) {
                this.officer = actor;
                LogService.LogInfo($"检测到官员{actor.data.name}");
            } else
            {
                this.data.officer = -1L;
            }
        }
        foreach (var pair in pData.occupied_cities)
        {
            long id = pair.Key;
            City city = World.world.cities.get(id);
            if (city != null)
            {
                this.occupied_cities.Add(city, pair.Value);
            }
        }
        this.empire = ModClass.EMPIRE_MANAGER.get(this.data.empire);
    }
}


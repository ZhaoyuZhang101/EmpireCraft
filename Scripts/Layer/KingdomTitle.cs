using EmpireCraft.Scripts.GameClassExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.Layer;
public class KingdomTitle : MetaObject<KingdomTitleData>
{
    public HashSet<City> city_list_hash = new HashSet<City>();
    public List<City> city_list = new List<City>();

    public void newKingdomTitle(Actor pFounder)
    {
        foreach(City city in pFounder.kingdom.cities)
        {
            city_list_hash.Add(city);
            city.SetTitle(this);
        }
        this.data.founder_actor_id = pFounder.getID();
        this.data.founder_actor_name = pFounder.getName();
        this.data.created_time = World.world.getCurWorldTime();
        this.data.banner_icon_id = pFounder.kingdom.data.banner_icon_id;
        this.data.banner_background_id = pFounder.kingdom.data.banner_background_id;
        this.data.name = pFounder.kingdom.GetKingdomName();
        update();
    }

    public override void save()
    {
        foreach (City city in city_list_hash)
        {
            this.data.cities.Add(city.getID());
        }
    }

    public void addCity(City city)
    {
        if (city != null)
        {
            city.SetTitle(this);
            this.city_list_hash.Add(city);
            update();
        }
    }

    public void removeCity(City city)
    {

        city.RemoveTitle();
    }

    public void update()
    {
        this.city_list.AddRange(city_list_hash);
    }

    public override void loadData(KingdomTitleData pData)
    {
        loadData(pData);
        this.city_list_hash.Clear();
        this.city_list.Clear();
        foreach(long city_id in pData.cities)
        {
            this.city_list_hash.Add(World.world.cities.get(city_id));
        }
        this.city_list.AddRange(this.city_list_hash);
    }
}


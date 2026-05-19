using System.Collections.Generic;
using System.Linq;

public class EmpireCore
{
    public long id { get; set; }
    public long empire_id { get; set; } = -1L;
    public long culture { get; set; }
    public string name { get; set; }
    public double create_timestamp { get; set; }
    public List<(double time, long cityId)> citiesRecord;
    public List<long> empire_history_ids = new List<long>();
    public bool AddCity(City city)
    {
        citiesRecord ??= new List<(double time, long cityId)>();
        if (citiesRecord.Select(a => a.cityId).ToList().Contains(city.id)) return false;
        citiesRecord.Add((World.world.getCurWorldTime(),  city.id));
        return true;
    }
    public bool RemoveCity(City city)
    {
        citiesRecord ??= new List<(double time, long cityId)>();
        if (!citiesRecord.Select(a => a.cityId).ToList().Contains(city.id)) return false;
        citiesRecord.RemoveAll(c=>c.cityId==city.id);
        return true;
    }
}

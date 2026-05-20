using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Layer;

public class EmpireCore
{
    public long id { get; set; }
    public long empire_id { get; set; } = -1L;
    public long culture { get; set; }
    public string name { get; set; }
    public double create_timestamp { get; set; }
    public long CoreCapital { get; set; }
    public List<(double time, long titleId)> titlesRecord;
    public List<long> empire_history_ids = new List<long>();

    public bool SetCoreCapital(City city)
    {
        if (city.isRekt())  return false;
        CoreCapital = city.id;
        return true;
    }
    public City GetCoreCapital()
    {
        return World.world.cities.get(CoreCapital);
    }
    public bool AddTitle(KingdomTitle title)
    {
        if (title == null || title.isRekt()) return false;
        titlesRecord ??= new List<(double time, long titleId)>();
        if (titlesRecord.Any(a => a.titleId == title.id)) return false;
        titlesRecord.Add((World.world.getCurWorldTime(), title.id));
        return true;
    }
    public bool RemoveTitle(KingdomTitle title)
    {
        if (title == null) return false;
        titlesRecord ??= new List<(double time, long titleId)>();
        if (titlesRecord.All(a => a.titleId != title.id)) return false;
        titlesRecord.RemoveAll(c => c.titleId == title.id);
        return true;
    }
}

namespace EmpireCraft.Scripts.GameClassExtensions;

public static class ReligionExtension
{
    public class ReligionExtraData: ExtraDataBase
    {
        //教皇
        public long LeaderID = -1L;
        //圣地
        public long CityID = -1L;
    }
    public static ReligionExtraData GetOrCreate(this Religion a, bool isSave = false)
    {
        var ed = a.GetOrCreate<Religion, ReligionExtraData>(isSave);
        return ed;
    }
    /// <summary>
    /// 获取教皇
    /// </summary>
    /// <param name="r"></param>
    /// <returns></returns>
    public static Actor GetLeader(this Religion r)
    {
        return World.world.units.get(r.GetOrCreate().LeaderID);
    }
    /// <summary>
    /// 更换教皇人选
    /// </summary>
    /// <param name="r"></param>
    /// <param name="actor"></param>
    public static void SetLeader(this Religion r, Actor actor)
    {
        r.GetOrCreate().LeaderID = actor.getID();
    }

    /// <summary>
    /// 设置圣地
    /// </summary>
    /// <param name="r"></param>
    /// <param name="city"></param>
    public static void SetCity(this Religion r, City city)
    {
        r.GetOrCreate().CityID = city.getID();
    }

    /// <summary>
    /// 获取圣地
    /// </summary>
    /// <param name="r"></param>
    /// <returns></returns>
    public static City GetCity(this Religion r)
    {
        return World.world.cities.get(r.GetOrCreate().CityID);
    }
}
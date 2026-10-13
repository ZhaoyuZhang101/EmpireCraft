using System.Collections.Generic;
using EmpireCraft.Scripts.Data;

namespace EmpireCraft.Scripts.GameClassExtensions;

public static class ReligionExtension
{
    public class ReligionExtraData: ExtraDataBase
    {
        //教皇
        public long LeaderID = -1L;
        //圣地
        public long CityID = -1L;
        // 教权(0~100，见 PapacySystem)与上次结算时间
        public float PapalAuthority = 60f;
        public double PapalUpdatedAt = -1d;
        // 圣战(见 PapacySystem.Crusade)：进行中的圣战、冷却到何时、第几次、失利次数
        public long CrusadeWarId = -1L;
        public double CrusadeCooldownUntil = -1d;
        public int CrusadeCount;
        public int CrusadeLosses;
        // 本宗教的骑士团(含已解散的)
        public List<KnightOrderData> Orders = new();
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
        if (city != null)
        {
            r.GetOrCreate().CityID = city.getID();
        }
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
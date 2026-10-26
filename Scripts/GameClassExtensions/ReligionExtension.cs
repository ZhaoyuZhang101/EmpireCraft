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
        // 教廷之囚的起始时间(-1 = 没有)、大分裂(对立教宗)
        public double CaptiveSince = -1d;
        public bool Schism;
        // 宗教改革分出来的国教会：母宗教与分出时间；"教随国定"到何时(母宗教此前不再讨伐异端)
        public long ParentReligionId = -1L;
        public double SplitAt = -1d;
        public double ReligiousPeaceUntil = -1d;
        // 所属天下(创教者文化的天下标识，见 ReligionSphere)；旧档空着，头一次查时补上
        public string Sphere = "";
        // 宗教类型(FaithType 的名字，见 ReligionFaith)；旧档空着，头一次查时补上
        public string Faith = "";
        // 皇帝兼管教会(东西教会大分裂分出的正教会，见 ReligionSchismSystem)：圣地在皇帝手里不算教廷之囚
        public bool Caesaropapist;
        // 本宗教已分出过正教会、什叶派(各一次)
        public bool EasternSplit;
        public bool ShiaSplit;
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
using System.Collections.Generic;
using EmpireCraft.Scripts.GeneralSystems;
using Newtonsoft.Json;

namespace EmpireCraft.Scripts.Data;

// 城市人口数据层(参考维多利亚 3 的 Pop)：把城里的居民按"阶层 × 文化 × 物种 × 理念"分成若干人口组，
// 只记人数，不对应具体单位。这样城市的人口与阶层分布可以脱离实体单位独立存在。
//
// 每个人口组的人数 = 背景人口(没有实体单位的普通人) + 实体单位(上次校准时数到的"名人")。
// 现阶段原版单位照常生成，背景人口恒为 0，数据层就是实体单位的统计镜像；
// 开启无小人模式后，背景人口才会脱离单位自行增减(见 CityPopulationSystem)。
public class PopGroup
{
    public SocialClass social_class = SocialClass.Peasant;
    public string culture = "";
    public string species = "";
    public PartyIdeology ideology = PartyIdeology.Conservatism;
    // 总人数(背景 + 实体)。背景人口按年连续增减，所以用小数累积
    public float size;
    // 上次校准时这一组里数到的实体单位数
    public int named;

    [JsonIgnore] public float Background => size > named ? size - named : 0f;

    public bool SameKind(SocialClass socialClass, string cultureKey, string speciesId, PartyIdeology partyIdeology) =>
        social_class == socialClass && ideology == partyIdeology &&
        string.Equals(culture ?? "", cultureKey ?? "", global::System.StringComparison.Ordinal) &&
        string.Equals(species ?? "", speciesId ?? "", global::System.StringComparison.Ordinal);
}

public class CityPopulationData
{
    public List<PopGroup> groups = new List<PopGroup>();
    // 上次用实体单位校准的世界时间
    public double last_census = -1d;
    // 上次结算背景人口自然增减的世界时间
    public double last_growth = -1d;
    // 上次校准时城里的实体单位数
    public int named_units;
}

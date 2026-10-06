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
    // 军团里的人(无小人模式)：本城征召的每个实体士兵代表一个军团，这里记军团除士兵本人以外的人数，
    // 随士兵的生命值折损(见 CityPopulationSystem.UpdateLegions)，计入城市人口
    public float levied;
    // 背景人口里农民阶层无地的比例(0~1，无小人模式，见 LandEconomySystem.UpdateBackgroundLand)
    public float background_landless;
    // 背景人口的阶层结构是否已经调整过一次(旧存档背景人口几乎全是农民，第一次直接调整到位)
    public bool classes_initialized;
    // 上次结算土地时的背景农民人数(人口减少时空出的地回到幸存者手里)
    public float background_peasants_last = -1f;

    // ---- 人口经济(无小人模式，见 PopulationEconomySystem) ----
    // 上次结算产出与消耗的世界时间
    public double last_economy = -1d;
    // 未满一个整数单位的产出/消耗，留到下次结算累加
    public Dictionary<string, float> output_carry = new Dictionary<string, float>();
    public float food_need_carry;
    public float construction_carry;
    // 上次结算城市建设(施工)的世界时间
    public double last_construction = -1d;
    // 上一次结算的结果(每年折算)，给界面和日志看
    public float last_jobs;
    public float last_workforce;
    public float last_food_output;
    public float last_food_eaten;
    public float last_food_shortage;
    public float last_gold_output;
    // 背景人口纳税(见 PopulationEconomySystem.PayTaxes)：零头与上次结算的年税收
    public float tax_carry;
    public float last_tax_income;
}

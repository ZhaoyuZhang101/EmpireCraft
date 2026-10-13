using System.Collections.Generic;

namespace EmpireCraft.Scripts.Data;

// 军团(借鉴钢铁雄心的师)：兵力只是数字，地图上最多一名统领实体代表(见 CorpsSystem、FrontSystem)。
// 存在所属王国的 KingdomExtraData.corps 里，随存档保存
public sealed class CorpsData
{
    public long id = -1L;
    // 本国第几军(番号)
    public int serial;
    public long kingdom_id = -1L;
    // 驻扎的军镇(征兵、补员、退伍回的城)
    public long home_city_id = -1L;
    // 军镇类别：CityPopulationSystem.ArmyPost
    public int post;
    // 现有兵力 / 满编兵力(人)
    public float strength;
    public float full;
    // 折合旧制多少个军团(原版计兵数、战争 AI 用)
    public int legions;
    // 组织度 0~100：交战下降、休整恢复，归零溃退
    public float org = 100f;
    // 任务：reserve 预备 / march 开赴 / attack 进攻 / defend 防守 / retreat 溃退休整
    public string task = "reserve";
    // 进攻或防守的那座城(战线段)
    public long sector_city_id = -1L;
    // 所在地块(地图兵牌位置)与要开往的地块
    public int zone_id = -1;
    public int target_zone_id = -1;
    // 堑壕：在同一段防守越久越高(0~0.3)
    public float entrench;
    // 统领实体(开关打开时才有)
    public long general_id = -1L;
    public double raised_at = -1d;
    // 兵员来源：退伍时按来源还回人口，阵亡按比例扣减
    public List<LegionPopulationContribution> population = new();
    // 义军：起义、革命时临时武装，仗打完就地解散
    public bool militia;
    // 后勤(见 LogisticsSystem)：补给程度 0~1、上月口粮够了几成
    public float supply = 1f;
    public float rations = 1f;
    // 跨海运兵(见 CorpsSystem.Embark/Sail)：在船上、航线(海区 id)、走到第几段、计步
    public bool embarked;
    public List<int> sea_route;
    public int sea_step;
    public int sea_tick;
}

using System.Collections.Generic;

namespace EmpireCraft.Scripts.Data;

// 科技跟制度一样归属文化：同文化的所有国家共享已发现的材料和已研究的技术。
public sealed class CultureTechState
{
    public List<string> discovered_materials = new();
    public List<string> researched_techs = new();
    // 当前正在研究的技术及进度(研究点)。空串表示还没选，年度结算时自动挑一个。
    public string current_tech = "";
    public float progress;
    // 玩家在科技窗口里指定的目标；为空时由 AI 自动选择
    public string player_target = "";
    // 各项材料/技术的获得时间，只用于窗口显示
    public Dictionary<string, double> timestamps = new();
    // 最近一年的研究点产出，只用于窗口显示(不必每次打开窗口都全图重算)
    public float last_yearly_points;
    // 第一次结算时按本文化已有的装备和建筑补齐对应技术(旧存档/开局前就存在的装备不会凭空"失传")
    public bool bootstrapped;
    // 每项技术各自的已投入研究点(加速事件可以给还没开始研究的技术预存进度)；
    // 上面的 progress 是旧存档字段，读档时并入这里
    public Dictionary<string, float> tech_progress = new();
    // 已触发过加速事件的技术(每项只触发一次)
    public List<string> boosted = new();
    // 上次记录的时代(进入新时代时写日志)；-1 表示还没记录过
    public int last_era_tier = -1;
    // 研究出新武器后，接下来几年士兵陆续换装
    public int modernize_years;
    // 上一年本文化的城市数(文化越大科技越贵)
    public int city_count;
    // 科技点储备：各项来源每年先攒进这里，再投入研究(自动或玩家手动)
    public float research_bank;
    public bool auto_research = true;
    // 最近一年各来源的科技点(窗口里显示明细)
    public Dictionary<string, float> last_income = new();
    // 去年因国库不足没能投入的科技点(窗口里提示"经费不足")
    public float last_unfunded;
    // 本文化已经写出的时代名著(见 LandmarkBookSystem)
    public List<string> landmark_books = new();
    // 本文化已有其思想的名著(自己写出的 + 读到别的文化写的)，决定名著对制度的推动
    public List<string> known_books = new();
}

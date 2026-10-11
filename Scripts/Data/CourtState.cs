using System.Collections.Generic;

namespace EmpireCraft.Scripts.Data;

// 朝堂博弈的存档数据(见 CourtSystem)：各派系在朝中的处境、当前奏议、皇帝方略与朝局纪事
public sealed class CourtState
{
    public Dictionary<string, CourtFactionState> factions = new();
    public CourtMemorial memorial;
    public List<CourtLogEntry> log = new();
    // 皇帝方略：balance 制衡 / favorite 偏宠 / suppress 打压 / personal 亲政 / regency 摄政(幼主、傀儡)
    public string strategy = "";
    public string strategy_target = "";
    public double last_month = -1d;
    // 律令制中央官制所处阶段(han 三公九卿 / tang 三省六部)，变了就重建中央机构
    public string bureau_stage = "";
    public double last_year = -1d;
}

public sealed class CourtFactionState
{
    // 圣眷：皇帝对这一派的好感(0~100)
    public float favor = 50f;
    // 怨望：诉求被驳、在朝中被排挤积下的不满(0~100)；过高先罢朝，再高兵谏
    public float grievance;
    // 政治资本：每年按官职、阁席与阶层支持累积，上奏、弹劾、掣肘等都要花
    public float capital = 10f;
    // 本年掣肘力度(0~0.6)：拖慢正在施行的奏议
    public float obstruction;
    public string ally = "";
    public string last_action = "";
    public string last_action_detail = "";
    // 议题冷却：议题名 → 可再次上奏的时间
    public Dictionary<string, double> cooldowns = new();
}

public sealed class CourtMemorial
{
    public string claim = "";
    public string proposer = "";
    // 0 上奏 1 票拟 2 廷议 3 批红 4 施行
    public int stage;
    public double stage_time = -1d;
    public float support = -1f;
    // 派系 id → 表态(1 支持 / 0 观望 / -1 反对)
    public Dictionary<string, int> stances = new();
    // 廷议各派权重(派系 id → 权重)，画廷议条用
    public Dictionary<string, float> weights = new();
    public string decider = "";
    public string decision = "";
    public bool shelved_once;
    // 三省六部：门下省是否已审核过(封驳只有一次)
    public bool reviewed;
    public bool drafted_by_ally;
}

public sealed class CourtLogEntry
{
    public double time;
    public string text = "";
}

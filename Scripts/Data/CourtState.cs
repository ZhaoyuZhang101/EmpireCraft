using System.Collections.Generic;

namespace EmpireCraft.Scripts.Data;

// 朝堂博弈的存档数据(见 CourtSystem)：各派系在朝中的处境、当前奏议、皇帝方略与朝局纪事
public sealed class CourtState
{
    public Dictionary<string, CourtFactionState> factions = new();
    public CourtMemorial memorial;
    public List<CourtLogEntry> log = new();
    // 往期奏议(新的在前，至多 CourtSystem.HistorySize 条)：诉求天平页据此列出每件奏议走到哪一步、成没成、为什么
    // (用户 2026-11-10 "推动诉求也要加天平页面""这样才能知道成功还是没成功")
    public List<CourtMemorialRecord> history = new();
    // 皇帝方略：balance 制衡 / favorite 偏宠 / suppress 打压 / personal 亲政 / regency 摄政(幼主、傀儡)
    public string strategy = "";
    public string strategy_target = "";
    public double last_month = -1d;
    // 律令制中央官制所处阶段(han 三公九卿 / tang 三省六部)，变了就重建中央机构
    public string bureau_stage = "";
    // 近代政权用近代措辞记纪事(见 ModernWording)；每回取朝局时更新，不存档
    [Newtonsoft.Json.JsonIgnore] public bool modern;
    public double last_year = -1d;
    // 低正统下集体罢朝持续的年数；换君或朝局恢复后消退，读档保留。
    public int crisis_years;
    public long crisis_emperor_id = -1L;
    public double last_escalation = -1d;
    // 联省兵谏拉起来的成员国(中途退出战争的，每年清掉叛乱状态，见 CourtSystem.AdvanceCrisis)
    public global::System.Collections.Generic.List<long> crisis_rebels = new();
    // 议立新君(见 SuccessionCouncilSystem)：皇位空缺、身后无子孙时朝廷议立的进程
    public CourtSuccession succession;
}

public sealed class CourtSuccession
{
    // 先君的族谱身份
    public long predecessor = -1L;
    // -1 待开列 0 已开列人选 1 廷议已毕 2 已定策(待登基)
    public int stage = -1;
    public double stage_time = -1d;
    // 皇族无人可继，改为劝进外姓
    public bool outsider;
    public List<CourtSuccessionCandidate> candidates = new();
    // 派系 id → 拥立的人选序号；派系 id → 廷议权重
    public Dictionary<string, int> backing = new();
    public Dictionary<string, float> weights = new();
    public int chosen = -1;
    public long chosen_actor = -1L;
    public string decider = "";
    public string decider_role = "";
    public int attempts;
    // 屡次迎立不成：作罢，照继承法递补
    public bool gave_up;
}

public sealed class CourtSuccessionCandidate
{
    // 宗室：族谱身份；外姓：单位
    public long identity = -1L;
    public long actor = -1L;
    public string name = "";
    // elder 立长 / kin 立亲 / worthy 立贤 / young 立幼 / cadet 宗室远支 / minister 权臣 / acclaim 重臣 / commoner 布衣
    public string basis = "";
    public int age;
    // 与先君隔几代(各自往上到共同祖先的代数之和)
    public int kinship;
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
    // 学说朋党投靠的阶级派系(见 CliqueSystem.Patron)：廷议时互相声援；patron_locked 为玩家手动指定，不再自动改投
    public string patron = "";
    public bool patron_locked;
    public string last_action = "";
    public string last_action_detail = "";
    // 议题冷却：议题名 → 可再次上奏的时间
    public Dictionary<string, double> cooldowns = new();
    // 人情账(见 CourtSystem.Bargain)：本派欠哪一派几回赞成(许诺回报换来的票，对方日后上奏时还)；背约的次数(信用越差越难换到票)
    public Dictionary<string, int> owed = new();
    public int reneged;
}

// 廷议前的一笔交换(见 CourtSystem.Bargain)：上奏一派给对方什么、对方表态从几换到几
public sealed class CourtDeal
{
    public string partner = "";
    public string partner_name = "";
    public List<CourtTerm> terms = new();
    // 表态(1 赞成 / 0 观望 / -1 反对)
    public int before, after;
    // deal 换票 / divine 神力强令 / repay 还人情 / reneged 背约
    public string kind = "deal";
}

// 交换的一项筹码：capital 政治资本(点) / money 首领私财(两) / promise 人情(日后回投一次) / ally 结盟 / office 让官(官职 id)
public sealed class CourtTerm
{
    public string kind = "";
    public int amount;
    public long office = -1L;
    public string Key => $"{kind}:{amount}:{office}";
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
    // 争奏时上奏一派投入的影响力(政治资本 + 首领的钱折算)，批红时加分(见 CourtSystem.TryPropose)
    public float bid;
    // 上奏的时间；批红时的准奏率、掷出的数与各项砝码、批红者(诉求天平页用，见 CourtSystem.Decide)
    public double proposed = -1d;
    public float approval = -1f;
    public float roll = -1f;
    public string decider_name = "";
    public List<CourtScaleFactor> factors = new();
    // 廷议前谈成的交换(廷议按换来的表态算)
    public List<CourtDeal> deals = new();
    // 神力(见 CourtSystem.Divine)：按神意改的表态(派系 id → 1/0/-1)、批红另加的神意砝码、施行快慢倍数
    public Dictionary<string, int> stance_overrides = new();
    public List<CourtScaleFactor> god_factors = new();
    public float rate_factor = 1f;
}

// 天平上的一项砝码：key 是语言键，value 是这一项加减的值(准奏率按百分点，施行速度按倍数)，arg 填进语言键
public sealed class CourtScaleFactor
{
    public string key = "";
    public float value;
    public string arg = "";
}

// 一件办完的奏议：走到哪一步、怎么收场(logKey)、廷议与批红时的读数
public sealed class CourtMemorialRecord
{
    public string claim = "";
    public string proposer = "";
    public string proposer_name = "";
    public double proposed = -1d;
    public double closed = -1d;
    // 收场时所在的一步(0 上奏 1 票拟 2 廷议 3 批红 4 施行)
    public int stage;
    // 收场的纪事键(court_log_rejected、court_log_completed……)与纪事原文
    public string outcome = "";
    public string text = "";
    public float support = -1f;
    public float approval = -1f;
    public float roll = -1f;
    public string decision = "";
    public string decider_name = "";
    public float bid;
    // 廷议时各派表态与权重，派系名照当时记下(派系日后改名或解散也能看)
    public Dictionary<string, int> stances = new();
    public Dictionary<string, float> weights = new();
    public Dictionary<string, string> names = new();
    public List<CourtScaleFactor> factors = new();
    public List<CourtDeal> deals = new();
}

public sealed class CourtLogEntry
{
    public double time;
    public string text = "";
}

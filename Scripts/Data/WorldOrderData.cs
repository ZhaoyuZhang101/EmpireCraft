using System.Collections.Generic;

namespace EmpireCraft.Scripts.Data;

// 世界格局(见 GeneralSystems.WorldOrderSystem)：阵营、冷战、万国公会、制裁。随存档记，旧档没有，从无到有
public class WorldOrderData
{
    public int next_bloc_id = 1;
    public List<BlocData> blocs = new List<BlocData>();
    public List<ColdWarData> cold_wars = new List<ColdWarData>();
    // 万国公会；null = 还没有(或已解散)
    public LeagueData league;
    public List<SanctionData> sanctions = new List<SanctionData>();
    // 关税战(见 GeneralSystems.TradeWarSystem)；世界性大萧条开始的时间(-1 = 眼下没有)与上一回结束的时间
    public List<TradeWarData> trade_wars = new List<TradeWarData>();
    public double depression_since = -1d;
    public double depression_ended = -1d;
    // 上一场世界大战打完的时间；去殖民浪潮的起止(见 GeneralSystems.DecolonizationSystem)
    public double world_war_ended_at = -1d;
    public double decolonization_since = -1d;
    public double decolonization_ended = -1d;
    // 航天竞赛(见 GeneralSystems.SpaceRaceSystem)：各国走到第几步(帝国编号 → 1 卫星 2 载人 3 登月)、眼下这一步的进度、各步的天下第一
    public Dictionary<long, int> space_levels = new Dictionary<long, int>();
    public Dictionary<long, float> space_progress = new Dictionary<long, float>();
    public List<SpaceFirstData> space_firsts = new List<SpaceFirstData>();
}

// 航天竞赛的一项天下第一：哪一步、谁、何时(名字当时记下，日后亡了国也看得到)
public class SpaceFirstData
{
    public int kind;
    public long empire_id = -1L;
    public double at = -1d;
    public string name = "";
}

// 两国之间的关税战：帝国编号；1 互加关税 / 2 断绝通商；起因("tariff" 关税壁垒 / "slump" 以邻为壑 / "decree" 当局所定)
public class TradeWarData
{
    public long a = -1L, b = -1L;
    public double since = -1d;
    public int level = 1;
    public double level_since = -1d;
    public string cause = "";
}

// 阵营：一个大国以本国理念为旗号纠合理念相近的国家。成员记帝国编号(盟主也在其中)
public class BlocData
{
    public int id;
    public long leader_id = -1L;
    public int ideology;
    public double since = -1d;
    public List<long> members = new List<long>();
    // 战时盟友：理念未必相近，只因有共同的敌人入的盟(见 WorldOrderSystem.JoinWartime)；仗打完理念不合的退出
    public List<long> wartime = new List<long>();
    // 军备竞赛 0~3：平时多养兵、多拨军费
    public int arms_level;
    // 没有对手的起点(-1 = 眼下有对手)：太久没有对手就散
    public double unopposed_since = -1d;
}

// 两个阵营之间的冷战
public class ColdWarData
{
    public int a, b;
    public double since = -1d;
    // 紧张 0~100：满了要么打起来，要么(核均势、够不着)缓和
    public float tension;
    public double detente_until = -1d;
    public double last_crisis = -1d;
    public int crises;
    // 两边盟主真打过(立阵营时就在交战，或冷战转热)：收场时不说"不战而终"
    public bool hot;
}

public class LeagueData
{
    public double since = -1d;
    public long founder_id = -1L;
    public List<long> members = new List<long>();
    // 通过了几回制裁决议、被否决或不了了之几回
    public int resolutions, failures;
}

// 制裁：by_bloc >= 0 是那个阵营的制裁，-1 是万国公会的
public class SanctionData
{
    public long target_id = -1L;
    public double since = -1d;
    public int years;
    public int by_bloc = -1;
    public string reason = "";
}

using System.Collections.Generic;

namespace EmpireCraft.Scripts.Data;

public enum TalentKind
{
    General = 0,   // 名将
    Minister = 1   // 名臣
}

// 档位：-1(None)只是"功业在攒、还没成名"的候选，不进名册窗口
public enum TalentRank
{
    None = -1,
    Fine = 0,      // 良才
    Famous = 1,    // 名
    Pillar = 2     // 国柱
}

public enum TalentFall
{
    None = 0,
    Battle = 1,    // 战死
    Age = 2,       // 老死
    Illness = 3,   // 病逝
    Executed = 4,  // 被诛
    Other = 5
}

// 名将名臣名册里的一条(见 FamousTalentSystem)。在世与已故都存在同一份名册里，已故的留作青史。
// 势力那几项在世时每年刷新，去世时定格成快照——国可能已经灭亡了，不能靠实时去查。
public sealed class TalentRecord
{
    public long actor_id = -1L;
    public string name = "";
    public TalentKind kind;
    public TalentRank rank = TalentRank.None;
    public float fame;
    // 成名时刻(世界时间)；候选为 -1
    public double founded_at = -1d;
    // 称号：本地化键 + 一个参数(城名等)，分开存，换语言也能重新渲染
    public string title_key = "";
    public string title_arg = "";
    // 最近一次立功的由来与地点(成名时据此定称号)
    public string deed = "";
    public string deed_arg = "";
    // 最近一次立功的时刻(一年内没立过功的候选，功业逐年衰减)
    public double credited_at = -1d;
    // 上次年度结算时 TotalPerformance 的读数(名臣的政绩增量)；-1 = 还没记
    public double performance_seen = -1d;
    public long kingdom_id = -1L;
    public long empire_id = -1L;
    public string realm_name = "";
    public string realm_color = "";
    public string realm_text_color = "";
    public string post = "";
    public bool alive = true;
    // 君主的猜忌(功高震主，见 ThroneSuspicion)：满一百有个了断；记着是哪位君主的猜忌，换了君主减半
    public float suspicion;
    public long suspicion_ruler_id = -1L;
    public double died_at = -1d;
    public int died_age;
    public TalentFall fall;
}

public sealed class TalentSaveData
{
    public List<TalentRecord> records = new();
    // 各势力(帝国/独立国)上一次成名的时刻，按种类分：成名要隔几年，不能一窝蜂
    public Dictionary<string, double> last_founded = new();
}

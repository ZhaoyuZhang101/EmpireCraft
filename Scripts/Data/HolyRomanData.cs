using System.Collections.Generic;

namespace EmpireCraft.Scripts.Data;

// 神圣罗马式选帝制帝国(见 HolyRomanSystem)，存在 EmpireData.holy_roman
public sealed class HolyRomanState
{
    // 选立的储君(皇帝在世时预先选出的继承人，-1 = 没有；原型"罗马人民的国王")
    public long king_of_romans_id = -1L;
    // 当今皇族(宗族 id)与连续当选的届数
    public long house_clan_id = -1L;
    public int house_terms;
    public double last_election_at = -1d;
    // 历次选举让步(已本地化的文本)，最新的在后；上次让步的时间
    public List<string> concessions = new();
    public double last_concession_at = -1d;
    // 上次检定储君
    public double last_king_of_romans_check = -1d;

    // —— 帝国议会(见 HolyRomanSystem.Diet) ——
    public double last_diet_at = -1d;
    public List<DietRecord> diets = new();
    // 上次付议的战争(同一场仗只议一次)
    public long last_war_agenda_id = -1L;
    // 帝国战争：议会通过的战争与应征的成员国
    public long imperial_war_id = -1L;
    public List<long> imperial_war_members = new();
    // 抗命(拒缴摊派、拒不应征)：成员国 id → 时间
    public Dictionary<long, double> defiance = new();
    // 帝国禁令：被禁的成员国、时间、国名、执行禁令的战争
    public long banned_kingdom_id = -1L;
    public double banned_at = -1d;
    public string banned_name = "";
    public long ban_war_id = -1L;

    // —— 帝国治安令(见 HolyRomanSystem 的「帝国治安令」一节) ——
    // 颁布时间(-1 = 未施行或已作废)；施行期间以特权、选举让步取回外交开战权的诸侯
    public double imperial_peace_at = -1d;
    public List<long> privileged_ids = new();

    // —— 加冕远征(见 HolyRomanSystem.Romzug) ——
    public double romzug_at = -1d;
    public long romzug_emperor_id = -1L;

    // —— 帝国改革(见 HolyRomanSystem.Reform) ——
    // 帝国大区：成员国 id → 大区序号；大区首领(大区内最强的诸侯)
    public Dictionary<long, int> circles = new();
    public double circles_at = -1d;
    public int court_rulings;

    // —— 诸侯同盟与和约(见 HolyRomanSystem.Leagues) ——
    public long league_leader_id = -1L;
    public List<long> league_member_ids = new();
    public string league_name = "";
    public double league_formed_at = -1d;
    public long league_war_id = -1L;
    // 和约之后诸侯取得完全主权
    public bool sovereign_princes;
    public string peace_name = "";
    public double peace_at = -1d;

    // —— 帝国权威(见 HolyRomanSystem.Authority) ——
    // 0~100；-1 = 还没起算(新帝国第一次年结记 50)，-2 = 旧档读入、第一次年结按现状折算(见 DataManager.NormalizeEmpireData)
    public int authority = -1;
    // 最近 16 笔收支(最新的在后)
    public List<AuthorityEntry> authority_log = new();
    // 已按加冕计过权威的皇帝
    public long authority_crowned_id = -1L;
    // 权威低于 20 起算的时间(回到 25 以上重计；帝国解体用)
    public double authority_low_since = -1d;
    // 本年已扣的抗命、失城(各有封顶)，本年涨跌合计(诊断用)；年结后清零
    public int year_defiance_loss, year_city_loss, year_gain, year_loss;
    // 本年帝国内有过兵戈(禁令战、宗教战……)，年结时不算太平
    public bool year_turmoil;
    // 帝国改革在本帝国颁行的时间(-1 = 未颁行；文化制度节点只是前提，要帝国议会通过)；上次付议
    public double reform_at = -1d;
    public double last_reform_agenda_at = -1d;
    // 护佑之战：中央应求援加入成员守方的战争 id → 被护佑的成员国 id
    public Dictionary<long, long> protect_wars = new();

    // —— 诸侯私战与帝国法院(见 HolyRomanSystem.Feuds) ——
    // 私战：进行中的与十年内打完的(同一对诸侯十年内不再私战)
    public List<PrivateWarRecord> private_wars = new();
    // 待裁的诉讼
    public List<LawsuitRecord> lawsuits = new();
    // 最近 8 起私战结果与裁决(已本地化)
    public List<string> court_log = new();
    // 累计：私战场数、判给原告、驳回(帝国法院判城沿用 court_rulings)
    public int private_war_count, lawsuits_granted, lawsuits_refused;

    // —— 选帝侯席位(见 HolyRomanSystem.Electors) ——
    // 世俗四席所在的成员国(-1 = 出缺)；最早一席出缺的时间；最近 10 条授、夺、出缺
    public List<long> elector_realm_ids = new();
    public double elector_vacant_at = -1d;
    public List<string> elector_log = new();
}

// 一场诸侯私战
public sealed class PrivateWarRecord
{
    public long war_id = -1L, attacker_id = -1L, defender_id = -1L;
    public string attacker_name = "", defender_name = "", city_names = "";
    // 争的城(只有这几座能在两家之间易手)
    public List<long> city_ids = new();
    public double at = -1d, ended_at = -1d;
    // 帝国改革颁行之后的违禁私战
    public bool illegal;
}

// 一桩帝国法院的诉讼(原告告被告占了自己法理的一座城)
public sealed class LawsuitRecord
{
    public long plaintiff_id = -1L, defendant_id = -1L, city_id = -1L;
    public string plaintiff_name = "", defendant_name = "", city_name = "";
    public double filed_at = -1d;
    public bool bribed;
}

// 帝国权威的一笔收支
public sealed class AuthorityEntry
{
    public double at;
    public int delta;
    public string text = "";
}

// 一次帝国议会
public sealed class DietRecord
{
    public double at;
    // levy 摊派 / war 帝国战争 / ban 帝国禁令 / reform 帝国改革
    public string agenda = "";
    public string subject = "";
    public int electors_yes, electors_no, princes_yes, princes_no, cities_yes, cities_no;
    public bool passed;
    public string outcome = "";
}

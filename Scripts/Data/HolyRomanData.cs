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
}

// 一次帝国议会
public sealed class DietRecord
{
    public double at;
    // levy 摊派 / war 帝国战争 / ban 帝国禁令
    public string agenda = "";
    public string subject = "";
    public int electors_yes, electors_no, princes_yes, princes_no, cities_yes, cities_no;
    public bool passed;
    public string outcome = "";
}

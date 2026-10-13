using System.Collections.Generic;

namespace EmpireCraft.Scripts.Data;

// 神圣罗马式选帝制帝国(见 HolyRomanSystem)，存在 EmpireData.holy_roman
public sealed class HolyRomanState
{
    // 罗马人民的国王(皇帝在世时预先选出的继承人，-1 = 没有)
    public long king_of_romans_id = -1L;
    // 当今皇族(宗族 id)与连续当选的届数
    public long house_clan_id = -1L;
    public int house_terms;
    public double last_election_at = -1d;
    // 历次选举让步(本地化键)，最新的在后
    public List<string> concessions = new();
    // 上次检定罗马人民的国王
    public double last_king_of_romans_check = -1d;
}

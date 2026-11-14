// 战争、同盟、国与国的好感。只管名册(谁在哪一边、谁同谁结盟)和好感的加总；
// 仗怎么打、何时议和是模组军团与战争系统的事，它们的补丁挂在这里的开战、停战、退出上。
//# hand War: Dispose getAsset getAttackers getDefenders getDuration getMainAttacker getMainDefender getYearStarted hasEnded hasKingdom isAttacker isDefender isInWarWith isMainAttacker isMainDefender isSameAs isTotalWar joinAttackers joinDefenders leaveWar removeFromWar setWinner
//# hand WarManager: endWar getNeutralKingdoms getWars hasWars newWar warStateChanged
//# hand Alliance: canJoin countWarriors hasWars isSame join leave hasSupremeKingdom hasSharedBordersWithKingdom isForcedType
//# hand AllianceManager: anyAlliances dissolveAlliance forceAlliance newAlliance
//# hand DiplomacyManager: getOpinion getRelation startWar
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using EmpireCraft.Server;

public partial class War
{
    public IEnumerable<Kingdom> getAttackers() => _list_attackers;
    public IEnumerable<Kingdom> getDefenders() => _list_defenders;
    public Kingdom getMainAttacker() => main_attacker;
    public Kingdom getMainDefender() => main_defender;
    public bool hasKingdom(Kingdom pKingdom) => pKingdom != null && (_list_attackers.Contains(pKingdom) || _list_defenders.Contains(pKingdom));
    public bool isAttacker(Kingdom pKingdom) => pKingdom != null && _list_attackers.Contains(pKingdom);
    public bool isDefender(Kingdom pKingdom) => pKingdom != null && _list_defenders.Contains(pKingdom);
    public bool isMainAttacker(Kingdom pKingdom) => pKingdom != null && main_attacker == pKingdom;
    public bool isMainDefender(Kingdom pKingdom) => pKingdom != null && main_defender == pKingdom;
    public bool isSameAs(War pWar) => pWar == this;
    public WarTypeAsset getAsset() => AssetManager.war_types_library.get(data?.war_type ?? "normal");
    public bool isTotalWar() => getAsset()?.total_war == true;
    public bool hasEnded() => !isAlive() || data.winner != WarWinner.Nobody;
    public int getDuration() => getAge();
    public int getYearStarted() => Date.getYear(data.created_time);
    public void setWinner(WarWinner pWinner) => data.winner = pWinner;

    public bool isInWarWith(Kingdom pKingdom, Kingdom pTarget) =>
        _list_attackers.Contains(pKingdom) && _list_defenders.Contains(pTarget) || _list_defenders.Contains(pKingdom) && _list_attackers.Contains(pTarget);

    [MethodImpl(P.N)]
    public void joinAttackers(Kingdom pKingdom)
    {
        if (pKingdom == null || hasKingdom(pKingdom)) return;
        WarManager.ShimMembership++;
        _list_attackers.Add(pKingdom);
        data.list_attackers.Add(pKingdom.id);
    }

    [MethodImpl(P.N)]
    public void joinDefenders(Kingdom pKingdom)
    {
        if (pKingdom == null || hasKingdom(pKingdom)) return;
        WarManager.ShimMembership++;
        _list_defenders.Add(pKingdom);
        data.list_defenders.Add(pKingdom.id);
    }

    [MethodImpl(P.N)]
    public void leaveWar(Kingdom pKingdom) => removeFromWar(pKingdom, true);

    // 一国退出：主攻、主守退了，这场仗就结束
    [MethodImpl(P.N)]
    public void removeFromWar(Kingdom pKingdom, bool pInPeace)
    {
        if (pKingdom == null) return;
        WarManager.ShimMembership++;
        if (_list_attackers.Remove(pKingdom))
        {
            data.list_attackers.Remove(pKingdom.id);
            data.past_attackers.Add(pKingdom.id);
        }
        if (_list_defenders.Remove(pKingdom))
        {
            data.list_defenders.Remove(pKingdom.id);
            data.past_defenders.Add(pKingdom.id);
        }
        if (!isAlive()) return;
        if (pKingdom == main_attacker || pKingdom == main_defender || _list_attackers.Count == 0 || _list_defenders.Count == 0)
            World.world.wars.endWar(this, pInPeace ? WarWinner.Peace : pKingdom == main_attacker ? WarWinner.Defenders : WarWinner.Attackers);
    }

    [MethodImpl(P.N)]
    public override void Dispose()
    {
        setAlive(false);
        WarManager.ShimMembership++;
        _list_attackers.Clear();
        _list_defenders.Clear();
    }
}

public partial class WarManager
{
    [MethodImpl(P.N)]
    public War newWar(Kingdom pAttacker, Kingdom pDefender, WarTypeAsset pType)
    {
        if (pAttacker == null || pDefender == null || pAttacker == pDefender) return null;
        War war = newObject();
        war.data.war_type = pType?.id ?? "normal";
        war.data.name = pAttacker.name + " - " + pDefender.name;
        war.main_attacker = pAttacker;
        war.main_defender = pDefender;
        war.data.main_attacker = pAttacker.id;
        war.data.main_defender = pDefender.id;
        war.joinAttackers(pAttacker);
        war.joinDefenders(pDefender);
        pAttacker.data.timestamp_last_war = pDefender.data.timestamp_last_war = World.world.getCurWorldTime();
        return war;
    }

    [MethodImpl(P.N)]
    public void endWar(War pWar, WarWinner pWinner = WarWinner.Nobody)
    {
        if (pWar == null || !pWar.isAlive()) return;
        pWar.setWinner(pWinner);
        Kingdom attacker = pWar.main_attacker, defender = pWar.main_defender;
        if (attacker != null && defender != null)
        {
            DiplomacyRelation relation = World.world.diplomacy.getRelation(attacker, defender);
            if (relation?.data != null) relation.data.timestamp_last_war_ended = World.world.getCurWorldTime();
        }
        removeObject(pWar);
        pWar.Dispose();
    }

    public IEnumerable<War> getWars(Kingdom pKingdom, bool pRandom = false)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i].hasKingdom(pKingdom)) yield return list[i];
    }

    public bool hasWars(Kingdom pKingdom)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i].hasKingdom(pKingdom)) return true;
        return false;
    }

    public bool hasWars(Alliance pAlliance) => pAlliance != null && pAlliance.kingdoms_list.Any(hasWars);

    // 两国交战(Kingdom.isInWarWith)：模组到处问(敌我、战线、外交)，以前每问一次把各场战争的两边名单挨个查一遍。
    // 现在按国记下各自的敌国，名单一变才重建：增减参战国都经 joinAttackers/joinDefenders/removeFromWar/Dispose(记一笔 ShimMembership)，
    // 读档直接往名单里加，靠名单的人数对不上来发现。重建好的表整张换上，别的线程读到的总是完整的一份
    internal static int ShimMembership;
    private Dictionary<Kingdom, HashSet<Kingdom>> _enemyIndex = new(), _allyIndex = new();
    private long _enemyStamp = long.MinValue;

    // 同在某场仗的一边(Kingdom.isInWarOnSameSide)
    internal bool ShimSameSide(Kingdom a, Kingdom b)
    {
        if (a == null || b == null || a == b) return false;
        EnemyIndex();
        return _allyIndex.TryGetValue(a, out HashSet<Kingdom> allies) && allies.Contains(b);
    }

    internal bool ShimAtWar(Kingdom a, Kingdom b)
    {
        if (a == null || b == null) return false;
        return EnemyIndex().TryGetValue(a, out HashSet<Kingdom> enemies) && enemies.Contains(b);
    }

    private Dictionary<Kingdom, HashSet<Kingdom>> EnemyIndex()
    {
        long stamp = ShimMembership * 1_000_003L + list.Count;
        for (int i = 0; i < list.Count; i++) stamp = stamp * 31 + list[i]._list_attackers.Count * 1009 + list[i]._list_defenders.Count;
        if (stamp == _enemyStamp) return _enemyIndex;
        var index = new Dictionary<Kingdom, HashSet<Kingdom>>();
        var allies = new Dictionary<Kingdom, HashSet<Kingdom>>();
        static void Mark(Dictionary<Kingdom, HashSet<Kingdom>> into, Kingdom from, Kingdom to)
        {
            if (from == null || to == null) return;
            if (!into.TryGetValue(from, out HashSet<Kingdom> set)) into[from] = set = new HashSet<Kingdom>();
            set.Add(to);
        }
        static void Side(Dictionary<Kingdom, HashSet<Kingdom>> into, List<Kingdom> side)
        {
            foreach (Kingdom a in side)
                foreach (Kingdom b in side)
                    if (a != b) Mark(into, a, b);
        }
        for (int i = 0; i < list.Count; i++)
        {
            foreach (Kingdom attacker in list[i]._list_attackers)
                foreach (Kingdom defender in list[i]._list_defenders)
                {
                    Mark(index, attacker, defender);
                    Mark(index, defender, attacker);
                }
            Side(allies, list[i]._list_attackers);
            Side(allies, list[i]._list_defenders);
        }
        _allyIndex = allies;
        _enemyIndex = index;
        _enemyStamp = stamp;
        return index;
    }

    public ListPool<Kingdom> getNeutralKingdoms(Kingdom pKingdom, bool pOnlyWithoutWars = false, bool pOnlyWithoutAlliances = false)
    {
        var neutral = new ListPool<Kingdom>();
        foreach (Kingdom other in World.world.kingdoms.list)
        {
            if (other == pKingdom || !other.isAlive() || pKingdom.isInWarWith(other)) continue;
            if (pOnlyWithoutWars && hasWars(other)) continue;
            if (pOnlyWithoutAlliances && other.hasAlliance()) continue;
            neutral.Add(other);
        }
        return neutral;
    }

    public void warStateChanged()
    {
    }
}

public partial class Alliance
{
    public static bool isSame(Alliance pAlliance1, Alliance pAlliance2) => pAlliance1 != null && pAlliance1 == pAlliance2;

    [MethodImpl(P.N)]
    public bool canJoin(Kingdom pKingdom) =>
        pKingdom != null && pKingdom.isAlive() && !pKingdom.hasAlliance() && !kingdoms_list.Any(member => member.isInWarWith(pKingdom));

    public bool join(Kingdom pKingdom, bool pRecalc = true, bool pForce = false)
    {
        if (pKingdom == null || kingdoms_hashset.Contains(pKingdom)) return false;
        if (!pForce && !canJoin(pKingdom)) return false;
        pKingdom.getAlliance()?.leave(pKingdom);
        kingdoms_hashset.Add(pKingdom);
        kingdoms_list.Add(pKingdom);
        pKingdom.ShimAlliance = this;
        pKingdom.data.timestamp_alliance = World.world.getCurWorldTime();
        return true;
    }

    public void leave(Kingdom pKingdom, bool pRecalc = true)
    {
        if (pKingdom == null || !kingdoms_hashset.Remove(pKingdom)) return;
        kingdoms_list.Remove(pKingdom);
        if (pKingdom.ShimAlliance == this) pKingdom.ShimAlliance = null;
        if (kingdoms_list.Count < 2 && isAlive()) World.world.alliances.dissolveAlliance(this);
    }

    public int countWarriors() => kingdoms_list.Sum(member => member.countTotalWarriors());
    public bool hasWars() => World.world.wars.hasWars(this);
    public bool hasSupremeKingdom() => kingdoms_list.Any(member => member.isSupreme());
    public bool hasSharedBordersWithKingdom(Kingdom pKingdom) =>
        pKingdom != null && pKingdom.cities.Any(city => city.neighbours_kingdoms.Any(near => kingdoms_hashset.Contains(near)));
    public bool isForcedType() => false;
    public override IEnumerable<City> getCities() => kingdoms_list.SelectMany(member => member.cities);
}

public partial class AllianceManager
{
    public bool anyAlliances() => list.Count > 0;

    public Alliance newAlliance(Kingdom pKingdom, Kingdom pKingdom2)
    {
        if (pKingdom == null || pKingdom2 == null || pKingdom == pKingdom2) return null;
        Alliance alliance = newObject();
        alliance.data.name = pKingdom.name + " + " + pKingdom2.name;
        alliance.generateColor();
        alliance.join(pKingdom, true, true);
        alliance.join(pKingdom2, true, true);
        return alliance;
    }

    // 不问愿不愿意，直接结盟(一方已有盟的并进去)
    public bool forceAlliance(Kingdom pKingdom1, Kingdom pKingdom2)
    {
        if (pKingdom1 == null || pKingdom2 == null || pKingdom1 == pKingdom2) return false;
        Alliance existing = pKingdom1.getAlliance() ?? pKingdom2.getAlliance();
        if (existing == null) return newAlliance(pKingdom1, pKingdom2) != null;
        existing.join(pKingdom1, true, true);
        existing.join(pKingdom2, true, true);
        return true;
    }

    public void dissolveAlliance(Alliance pAlliance)
    {
        if (pAlliance == null || !pAlliance.isAlive()) return;
        foreach (Kingdom member in pAlliance.kingdoms_list.ToArray())
            if (member.ShimAlliance == pAlliance) member.ShimAlliance = null;
        pAlliance.kingdoms_list.Clear();
        pAlliance.kingdoms_hashset.Clear();
        removeObject(pAlliance);
    }
}

public partial class DiplomacyManager
{
    private readonly Dictionary<(long, long), DiplomacyRelation> _relations = new();

    public DiplomacyRelation getRelation(Kingdom pK1, Kingdom pK2)
    {
        if (pK1 == null || pK2 == null) return null;
        (long, long) key = pK1.id < pK2.id ? (pK1.id, pK2.id) : (pK2.id, pK1.id);
        if (!_relations.TryGetValue(key, out DiplomacyRelation relation)) _relations[key] = relation = newObject();
        return relation;
    }

    public override void clear()
    {
        _relations.Clear();
        base.clear();
    }

    // 存读档：两国之间记着的那点往来(上回停战是什么时候)
    internal IEnumerable<KeyValuePair<(long, long), DiplomacyRelation>> ShimRelations => _relations;

    internal void ShimRestoreRelation(long first, long second, DiplomacyRelationData data)
    {
        if (data == null) return;
        _relations[first < second ? (first, second) : (second, first)] = ShimRestore(data);
    }

    // 好感：库里登记的各条(模组加的那些都在)逐条算、加总
    public KingdomOpinion getOpinion(Kingdom k1, Kingdom k2)
    {
        var opinion = new KingdomOpinion();
        if (k1 == null || k2 == null) return opinion;
        foreach (OpinionAsset entry in AssetManager.opinion_library.list)
        {
            if (entry?.calc == null) continue;
            int value;
            try
            {
                value = entry.calc(k1, k2);
            }
            catch (Exception)
            {
                continue;
            }
            if (value == 0) continue;
            opinion.results[entry] = value;
            opinion.total += value;
        }
        return opinion;
    }

    [MethodImpl(P.N)]
    public War startWar(Kingdom pAttacker, Kingdom pDefender, WarTypeAsset pAsset, bool pLog = true) => World.world.wars.newWar(pAttacker, pDefender, pAsset);
}

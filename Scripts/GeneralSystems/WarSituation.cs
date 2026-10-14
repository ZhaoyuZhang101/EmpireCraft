using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;

namespace EmpireCraft.Scripts.GeneralSystems;

// 战局判断：给"割地、赔款求和"这类决议用——只有真打输了才谈得上议和让步，
// 而不是一开战、敌人兵力差不多就主动割地。
//   · 参战时记下各方的城市数(WarPatch 开战/参战时调用 RecordParticipant)，之后比较现在的城市数就知道丢了多少城；
//   · 兵力对比：敌方(帝国算整个帝国)战士数 / 我方战士数；
//   · 战争年数。
public static class WarSituation
{
    public static long SideKey(Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        return empire != null && !empire.isRekt() ? empire.id : kingdom?.id ?? -1L;
    }

    public static int SideCities(Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        if (empire != null && !empire.isRekt()) return empire.countCities();
        return kingdom?.cities?.Count ?? 0;
    }

    // 一方的兵力：原版士兵数，军团模式再加各军团兵力(兵在军团里只是数据，原版数不到)；帝国算中央连同各成员国
    public static int SideWarriors(Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        if (empire != null && !empire.isRekt())
            return empire.countWarriors() + (empire.kingdoms_list?.Sum(member => CorpsSoldiers(member)) ?? 0);
        return (kingdom?.countTotalWarriors() ?? 0) + CorpsSoldiers(kingdom);
    }

    private static int CorpsSoldiers(Kingdom kingdom) =>
        !CorpsSystem.Enabled || kingdom?.data == null || kingdom.isRekt() ? 0
            : UnityEngine.Mathf.RoundToInt(CorpsSystem.Of(kingdom).Sum(corps => UnityEngine.Mathf.Max(0f, corps.strength)));

    // 主动开战比的实力：国力(人口、财力、军力综合，见 GetNationalPower)，平时军团只留常备兵、按兵数比不出谁强谁弱；
    // 帝国算中央连同各成员国
    public static double Power(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return 0d;
        Empire empire = kingdom.GetEmpire();
        return empire != null && !empire.isRekt() ? Power(empire) : kingdom.GetNationalPower();
    }

    public static double Power(Empire empire) =>
        empire?.kingdoms_list?.Where(member => member != null && !member.isRekt()).Sum(member => member.GetNationalPower()) ?? 0d;

    // 忙不过来，不再开新仗：手上已有两场仗，或正被全面侵略(以前只要在打任何一场仗——平叛、边境冲突——就不提对外用兵)
    public static bool TooBusyForWar(Kingdom kingdom) =>
        kingdom?.data == null || kingdom.isRekt() ||
        kingdom.getWars().Count(war => war?.data != null && !war.hasEnded()) >= 2 || WarGoalSystem.FightingForSurvival(kingdom);

    public static void RecordParticipant(War war, Kingdom kingdom)
    {
        if (war?.data == null || kingdom?.data == null || kingdom.isRekt()) return;
        Dictionary<long, int> initial = war.GetOrCreate().initial_cities ??= new Dictionary<long, int>();
        long key = SideKey(kingdom);
        if (key >= 0 && !initial.ContainsKey(key)) initial[key] = SideCities(kingdom);
    }

    // 我方(core)与 enemy 交战中的战争(双方分属攻守两边)
    public static War FindWar(Kingdom core, Kingdom enemy)
    {
        if (core == null || enemy == null) return null;
        return core.getWars().FirstOrDefault(war => war != null && !war.hasEnded() &&
            (war.isAttacker(core) && war.isDefender(enemy) || war.isDefender(core) && war.isAttacker(enemy)));
    }

    public static float WarYears(War war) =>
        war?.data == null ? 0f : Date.getYearsSince(war.data.created_time);

    // 这场仗里我方丢了多少城；没记录(旧存档里开打的战争)返回 -1
    public static int CitiesLost(War war, Kingdom core)
    {
        Dictionary<long, int> initial = war?.GetOrCreate().initial_cities;
        if (initial == null || !initial.TryGetValue(SideKey(core), out int before)) return -1;
        return before - SideCities(core);
    }

    // 兵力对比：敌方 / 我方(我方没兵时视为极大)
    public static float StrengthRatio(Kingdom core, Kingdom enemy)
    {
        int ours = SideWarriors(core);
        int theirs = SideWarriors(enemy);
        return ours <= 0 ? (theirs > 0 ? 99f : 1f) : (float)theirs / ours;
    }
}

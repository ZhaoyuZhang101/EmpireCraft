using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;

namespace EmpireCraft.Scripts.GeneralSystems;

// 记录每位皇帝的在位生平(写进 EmpireCraftHistory)，供谥法定庙号谥号(PosthumousNameGenerator)：
//   疆域：即位、极盛、身后的城市数；正统：即位与身后；战绩：对外胜负、平叛与叛乱得手；
//   结局：身后是否在世(失位)、享年、是否中兴之主、是否末代之君(改朝换代时回填)。
public static class ReignRecordSystem
{
    public static void OnReignStart(Empire empire)
    {
        EmpireCraftHistory reign = empire?.data?.currentHistory;
        if (reign == null) return;
        int cities = empire.countCities();
        reign.start_cities = cities;
        reign.max_cities = cities;
        reign.start_mandate = empire.Mandate;
    }

    public static void OnYear(Empire empire)
    {
        EmpireCraftHistory reign = empire?.data?.currentHistory;
        if (reign == null || reign.start_cities < 0) return;
        reign.max_cities = Math.Max(reign.max_cities, empire.countCities());
    }

    public static void OnReignEnd(Empire empire, Actor emperor)
    {
        EmpireCraftHistory reign = empire?.data?.currentHistory;
        if (reign == null || emperor?.data == null) return;
        if (reign.start_cities >= 0) reign.max_cities = Math.Max(reign.max_cities, empire.countCities());
        reign.end_cities = empire.countCities();
        reign.end_mandate = empire.Mandate;
        reign.ended_alive = emperor.isAlive();
        reign.age_at_end = emperor.getAge();
        reign.restorer = emperor.hasTrait(RulerTraitSystem.Restorer);
    }

    public static void OnDynastyChanged(Empire empire, Actor newEmperor)
    {
        DynasticCycleSystem.OnDynastyChanged(empire);
        EmpireCraftHistory last = empire?.data?.history?.LastOrDefault(reign => reign != null && !reign.is_republic);
        if (last != null && last.id != newEmperor?.data?.id) last.ended_dynasty = true;
    }

    public static void OnWarEnded(War war, WarWinner winner)
    {
        if (war == null) return;
        bool rebellion = war.getAsset() == WarTypeLibrary.rebellion;
        var seen = new HashSet<long>();
        foreach (Kingdom kingdom in war._list_attackers.ToList()) Tally(kingdom, true, winner, rebellion, seen);
        foreach (Kingdom kingdom in war._list_defenders.ToList()) Tally(kingdom, false, winner, rebellion, seen);
    }

    private static void Tally(Kingdom kingdom, bool attacker, WarWinner winner, bool rebellion, HashSet<long> seen)
    {
        if (kingdom == null || kingdom.isRekt() || !kingdom.IsEmpire()) return;
        Empire empire = kingdom.GetEmpire();
        EmpireCraftHistory reign = empire?.data?.currentHistory;
        if (reign == null || reign.is_republic || !seen.Add(empire.id)) return;
        bool won = winner == (attacker ? WarWinner.Attackers : WarWinner.Defenders);
        bool lost = winner == (attacker ? WarWinner.Defenders : WarWinner.Attackers);
        if (rebellion && !attacker)
        {
            reign.rebellions++;
            if (lost) reign.rebellions_lost++;
            return;
        }
        if (won) reign.wars_won++;
        else if (lost) reign.wars_lost++;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 古典共和的元老院(施行「元老院」制度后)。元老名单记在帝国的内阁名单里，和封建选帝侯团同一套：
// 执政官出缺时由 OfficeSelector.RankElectiveCandidates 把元老当选举人，按派系计票。
//   · 席位：每个成员城邦按人口分 1~4 席(不到 1 万 1 席，1 万起 2 席，10 万起 3 席，100 万以上 4 席)；
//   · 人选：城邦之主固定居首席，其余席位先留任上届仍属本城邦的元老，再按贵族优先、威望高低补足；
//   · 没有元老院的古典共和不设元老，名单清空(首领按兵力最强者接任)。
// 由帝国的内阁检查行为定期调用。
public static class SenateSystem
{
    public static int SeatsFor(Kingdom state)
    {
        long population = Math.Max(1L, state?.getPopulationPeople() ?? 1);
        return Mathf.Clamp((int)Math.Floor(Math.Log10(population)) - 2, 1, 4);
    }

    public static void Maintain(Empire empire)
    {
        Kingdom core = empire?.CoreKingdom;
        if (core == null || empire.data == null) return;
        empire.data.CabinetMembers ??= new List<long>();
        if (!CityStateService.HasSenate(empire))
        {
            if (empire.data.CabinetMembers.Count > 0) empire.data.CabinetMembers.Clear();
            return;
        }
        List<long> previous = empire.data.CabinetMembers.ToList();
        var used = new HashSet<long>();
        var senators = new List<long>();
        foreach (Kingdom state in empire.kingdoms_list
                     .Where(kingdom => kingdom != null && !kingdom.isRekt() && kingdom != core)
                     .OrderByDescending(kingdom => kingdom.countTotalWarriors()).ToList())
        {
            int seats = SeatsFor(state);
            var chosen = new List<Actor>();
            void Take(Actor actor)
            {
                if (chosen.Count < seats && Eligible(actor, core) && actor.kingdom == state && used.Add(actor.id))
                    chosen.Add(actor);
            }
            if (state.hasKing()) Take(state.king);
            foreach (long id in previous) Take(World.world.units.get(id));
            if (chosen.Count < seats && state.units != null)
                foreach (Actor actor in state.units.ToList()
                             .Where(actor => Eligible(actor, core))
                             .OrderByDescending(actor => actor.GetOrCreate().socialClass == SocialClass.Noble)
                             .ThenByDescending(actor => actor.renown))
                    Take(actor);
            senators.AddRange(chosen.Select(actor => actor.id));
        }
        empire.data.CabinetMembers = senators;
        var before = new HashSet<long>(previous);
        foreach (long id in senators.Where(id => !before.Contains(id)))
            World.world.units.get(id)?.RecordPersonalHistory(LM.Get("personal_history_became_senator"));
    }

    private static bool Eligible(Actor actor, Kingdom core) =>
        actor?.data != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() && !actor.IsWarMachine() &&
        !actor.IsSkeleton() && actor.CanServeOffice(core);
}

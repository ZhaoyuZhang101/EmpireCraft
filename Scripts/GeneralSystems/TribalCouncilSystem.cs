using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 诸部议事会(用户 2026-10-22 "华夏的部落联合参考古典共和制度"；问答选：诸部议事会推举共主、终身任、
// 原「部落联合」节点改为设立议事会、家天下以后保留为贵族议政)。仿古典共和的元老院(见 SenateSystem)：
//   · 设立：华夏原始政体的部落联盟施行「诸部议事」(节点 huaxia_tribal_alliance)以后；部落联合本身开局就能结成(见 TribalWarSystem)；
//   · 席位：共主之部与共事它的各部按人口各出 1~4 席(同元老院)；首领居首席，其余先留任上届仍属本部的，再按贵族、威望补足；
//   · 推举：共主崩或年老让位时由议事会投票(见 ShanrangSystem)——每位议事投本部参选的首领，本部的人选远不如最贤者(分数不到七成)
//     或本部没人参选的投最贤者；得票最多者为共主，票同比禅让的分数。终身任，没有定期改选；
//   · 施行家天下以后不再推举、父子相传，议事会照旧设着，作贵族议政；进到周制分封(换了政体)时解散。
// 名单记在帝国数据 tribal_council(不占内阁名单)。每月随禅让一起维护(见 TribalFissionService.EnforceOneCity)。
public static class TribalCouncilSystem
{
    public const string Node = "huaxia_tribal_alliance";
    private const float LoyaltyShare = 0.7f;

    // 设着议事会：华夏原始政体的部落联盟，施行过「诸部议事」(被家天下取代以后也算)
    public static bool Active(Empire empire)
    {
        if (empire?.data == null || empire.isRekt() || !DynastyNaming.HuaxiaOriginEmpire(empire)) return false;
        string culture = CultureService.GetInstitutionCulture(empire.CoreKingdom);
        return InstitutionSystem.IsEnacted(culture, Node) || InstitutionSystem.IsSuperseded(culture, Node);
    }

    // 出议事的各部：共主之部在前，再是帝国里的、共事它的各部
    public static List<Kingdom> Tribes(Empire empire)
    {
        var result = new List<Kingdom>();
        Kingdom core = empire?.CoreKingdom;
        if (core?.data == null || core.isRekt()) return result;
        result.Add(core);
        foreach (Kingdom tribe in empire.kingdoms_list.Concat(TribalFissionService.VassalsOf(empire)))
            if (tribe?.data != null && !tribe.isRekt() && !result.Contains(tribe)) result.Add(tribe);
        return result;
    }

    public static List<Actor> Members(Empire empire) =>
        (empire?.data?.tribal_council ?? new List<long>()).Select(id => World.world.units.get(id))
        .Where(actor => actor?.data != null && !actor.isRekt() && actor.isAlive()).ToList();

    public static void Monthly()
    {
        if (ModClass.EMPIRE_MANAGER == null) return;
        foreach (Empire empire in ModClass.EMPIRE_MANAGER.ToList())
        {
            if (empire?.data == null || empire.isRekt() || empire.IsArchived()) continue;
            try
            {
                Maintain(empire);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][诸部议事] {empire.GetEmpireName()} 议事会维护失败: {exception.Message}");
            }
        }
    }

    public static void Maintain(Empire empire)
    {
        if (!Active(empire))
        {
            if (empire.data.tribal_council != null) empire.data.tribal_council = null;
            return;
        }
        List<long> previous = empire.data.tribal_council ?? new List<long>();
        var used = new HashSet<long>();
        var council = new List<long>();
        foreach (Kingdom tribe in Tribes(empire))
        {
            int seats = SenateSystem.SeatsFor(tribe);
            int taken = 0;
            void Take(Actor actor)
            {
                if (taken >= seats || !Eligible(actor) || actor.kingdom != tribe || !used.Add(actor.id)) return;
                council.Add(actor.id);
                taken++;
            }
            if (tribe.hasKing()) Take(tribe.king);
            foreach (long id in previous)
            {
                if (taken >= seats) break;
                Take(World.world.units.get(id));
            }
            if (taken < seats && tribe.units != null)
                foreach (Actor actor in tribe.units.ToList().Where(Eligible)
                             .OrderByDescending(actor => actor.GetOrCreate().socialClass == SocialClass.Noble)
                             .ThenByDescending(actor => actor.renown))
                {
                    if (taken >= seats) break;
                    Take(actor);
                }
        }
        empire.data.tribal_council = council;
        var before = new HashSet<long>(previous);
        foreach (long id in council.Where(id => !before.Contains(id)))
            World.world.units.get(id)?.RecordPersonalHistory(LM.Get("personal_history_joined_tribal_council"));
    }

    private static bool Eligible(Actor actor) =>
        actor?.data != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() && !actor.IsWarMachine() &&
        !actor.IsSkeleton();

    // 推举计票：每位议事投本部参选的首领(本部几位参选的投分数最高的)；本部的人选不到最贤者分数七成、或本部没人参选的投最贤者。
    // 得票多的在前，票同比禅让的分数
    public static List<(Actor actor, Kingdom tribe, int votes)> Tally(Empire empire, List<(Actor actor, Kingdom tribe)> candidates)
    {
        var result = new List<(Actor actor, Kingdom tribe, int votes)>();
        if (candidates == null || candidates.Count == 0) return result;
        var scored = candidates.Where(candidate => candidate.actor?.data != null)
            .Select(candidate => (candidate.actor, candidate.tribe, score: ShanrangSystem.Score(candidate.actor, candidate.tribe)))
            .GroupBy(candidate => candidate.actor).Select(group => group.First())
            .OrderByDescending(candidate => candidate.score).ToList();
        if (scored.Count == 0) return result;
        var votes = scored.ToDictionary(candidate => candidate.actor, _ => 0);
        var best = scored[0];
        foreach (Actor member in Members(empire))
        {
            var own = scored.FirstOrDefault(candidate => candidate.tribe == member.kingdom);
            Actor pick = own.actor != null && own.score >= best.score * LoyaltyShare ? own.actor : best.actor;
            votes[pick]++;
        }
        return scored.Select(candidate => (candidate.actor, candidate.tribe, votes[candidate.actor]))
            .OrderByDescending(candidate => candidate.Item3)
            .ThenByDescending(candidate => ShanrangSystem.Score(candidate.actor, candidate.tribe)).ToList();
    }
}

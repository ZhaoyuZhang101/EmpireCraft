using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 禅让推举(用户 2026-10-16 选"共主禅让推举")：华夏原始政体的部落联盟，施行世袭王权以前共主不传子——
//   · 共主崩，或年过七十(每月约百分之二的机会让位)：诸部议事，在共主之部的城主与共事它的各部首领里推一位为共主——
//     威望最高、所在之部最大，正当壮年的加分、年过七十的减分；比原定继位的强出两成以上才换人；
//   · 推出的是别部首领：那一部入部落联盟为共主之部(帝国核心移过去)，原来的共主之部改为共事新共主(见 TribalFissionService)；
//     推出的是本部的城主：本部改立他为首领；
//   · 换了氏族也不算篡位(见 Empire.NewEmperor)，不改国号、不扣篡位的正统；施行世袭王权以后父子相传(家天下)。
//   · 共主崩后原版先按世袭立的那位(原定继位的)，当月就被推举换下的，只是待推举的人，不算一代共主，世系图里不列他
//     (用户 2026-10-17 "为什么这么多在位0年的"：以前他也记了一任，在位 0 年；旧存档读档后清一次，见 DropInterimReigns)。
public static class ShanrangSystem
{
    private const int AbdicateAge = 70, OldAge = 70, PrimeMin = 30, PrimeMax = 60;
    private const float AbdicateChance = 0.02f, Margin = 1.2f, MarginFlat = 10f, PerCity = 15f;
    // 各部落联盟上次见到的共主(人、名字；崩了以后人可能已经不在，史册要写他的名字)
    private static readonly Dictionary<long, (long id, string name)> LastRuler = new();

    public static void ResetWorldState()
    {
        LastRuler.Clear();
    }

    // 共主推举而不是父子相传(华夏原始政体，还没施行世袭王权)
    public static bool Elective(Empire empire) =>
        DynastyNaming.HuaxiaOriginEmpire(empire) &&
        !InstitutionSystem.IsEnacted(CultureService.GetInstitutionCulture(empire.CoreKingdom), "huaxia_hereditary_kingship");

    // 每月(TribalFissionService.EnforceOneCity)
    public static void Monthly()
    {
        if (ModClass.EMPIRE_MANAGER == null) return;
        foreach (Empire empire in ModClass.EMPIRE_MANAGER.ToList())
        {
            if (empire?.data == null || empire.isRekt() || empire.IsArchived() || !Elective(empire)) continue;
            if (!empire.data.shanrang_cleaned)
            {
                empire.data.shanrang_cleaned = true;
                DropInterimReigns(empire);
            }
            Actor ruler = empire.Emperor;
            if (ruler?.data == null || ruler.isRekt() || !ruler.isAlive()) continue;
            bool known = LastRuler.TryGetValue(empire.id, out var last);
            LastRuler[empire.id] = (ruler.data.id, ruler.getName());
            bool succeeded = known && last.id != ruler.data.id;
            bool abdicate = !succeeded && ruler.getAge() >= AbdicateAge && Random.value < AbdicateChance;
            if (!succeeded && !abdicate) continue;
            (Actor best, Kingdom tribe) = Worthiest(empire, ruler);
            if (best == null || best == ruler || tribe == null) continue;
            if (Score(best, tribe) < Score(ruler, empire.CoreKingdom) * Margin + MarginFlat) continue;
            string previous = abdicate ? ruler.getName() : last.name;
            string chosen = tribe == empire.CoreKingdom ? best.getName() : tribe.GetKingdomName() + best.getName();
            try
            {
                if (!Install(empire, best, tribe)) continue;
            }
            catch (global::System.Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][禅让] {empire.GetEmpireName()} 推举共主失败: {exception.Message}");
                continue;
            }
            Actor installed = empire.Emperor ?? best;
            if (succeeded && installed != ruler) DropInterimReign(empire, ruler.data.id, ruler.name);
            LastRuler[empire.id] = (installed.data.id, installed.getName());
            string content = string.Format(LM.Get(abdicate ? "shanrang_abdicate_history" : "shanrang_elect_history"), previous, chosen);
            LogService.LogInfo("[EmpireCraft][禅让] " + content);
            empire.RecordHistory(directContent: content, actorId: best.data.id, kingdomId: tribe.id);
        }
    }

    // 待推举的那一任不入世系：在位不满一年、换下时还活着的那条记录去掉
    private static void DropInterimReign(Empire empire, long id, string name)
    {
        List<EmpireCraftHistory> history = empire.data.history;
        int index = history?.FindLastIndex(reign => reign != null && reign.id == id) ?? -1;
        if (index < 0 || history[index].total_time > 0 || history[index].is_republic) return;
        history.RemoveAt(index);
        int named = empire.data.history_emperrors?.LastIndexOf(name) ?? -1;
        if (named >= 0) empire.data.history_emperrors.RemoveAt(named);
    }

    // 旧存档：推举共主的部落联盟里，在位 0 年、换下时还活着、没有谥号的几任都是待推举的(推举出来的人年过七十要减分，
    // 不会刚推上去不满一年又让位)
    private static void DropInterimReigns(Empire empire)
    {
        List<EmpireCraftHistory> history = empire.data.history;
        if (history == null) return;
        List<EmpireCraftHistory> interim = history.Where(reign => reign != null && !reign.is_republic && reign.total_time <= 0 &&
                                                                  reign.ended_alive && reign.age_at_end >= 0 && reign.age_at_end < 70 &&
                                                                  string.IsNullOrEmpty(reign.shihao_name) && !reign.is_first).ToList();
        foreach (EmpireCraftHistory reign in interim)
        {
            history.Remove(reign);
            int named = empire.data.history_emperrors?.LastIndexOf(reign.emperor) ?? -1;
            if (named >= 0) empire.data.history_emperrors.RemoveAt(named);
        }
        int removed = interim.Count;
        if (removed > 0) LogService.LogInfo($"[EmpireCraft][禅让] {empire.GetEmpireName()} 世系去掉 {removed} 位待推举的");
    }

    // 推举的分数：威望、所在之部的城数，正当壮年加分、年过七十减分
    private static float Score(Actor actor, Kingdom tribe)
    {
        if (actor?.data == null) return 0f;
        int age = actor.getAge();
        return actor.data.renown + PerCity * (tribe?.cities?.Count ?? 0) + (age >= PrimeMin && age <= PrimeMax ? 10f : 0f) -
               (age >= OldAge ? 25f : 0f);
    }

    private static bool Fit(Actor actor) =>
        actor?.data != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() && actor.isUnitFitToRule();

    // 候选：共主之部各城的城主、共事它的各部首领
    private static (Actor actor, Kingdom tribe) Worthiest(Empire empire, Actor ruler)
    {
        Kingdom core = empire.CoreKingdom;
        var candidates = new List<(Actor actor, Kingdom tribe)>();
        foreach (City city in core?.cities ?? new List<City>())
            if (Fit(city?.leader) && city.leader != ruler && !city.leader.isKing()) candidates.Add((city.leader, core));
        foreach (Kingdom tribe in TribalFissionService.VassalsOf(empire))
            if (Fit(tribe.king) && tribe.hasCapital()) candidates.Add((tribe.king, tribe));
        return candidates.OrderByDescending(entry => Score(entry.actor, entry.tribe)).FirstOrDefault();
    }

    private static bool Install(Empire empire, Actor best, Kingdom tribe)
    {
        if (tribe == empire.CoreKingdom) return empire.InstallHeadOfState(best);
        // 别部首领为共主：那一部入部落联盟、成为共主之部；原来的共主之部之后离开帝国、共事新共主
        tribe.GetOrCreate().tribal_overlord_id = -1L;
        if (tribe.hasAlliance()) tribe.getAlliance().leave(tribe);
        if (tribe.GetEmpire() != empire) empire.join(tribe, pForce: true);
        return empire.TransferCoreTo(tribe);
    }
}

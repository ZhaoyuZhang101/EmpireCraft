using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 个人恩怨(用户 2026-10-19 选"个人恩怨")：要人之间的恩主/门生、宿敌，记在族谱身份上(每人至多三条，见 PersonalClanIdentity.ties)。
//   结缘/结仇：被弹劾去职(与弹劾一派的首领)；要人被杀(死者的子与兄弟与凶手，杀亲之仇)；被俘放归、赎回(与胜方统领)；
//   殿试及第(以当朝首辅为座主)。
//   每年结算(只看在世的实体要人，一人一年至多一件事)：
//     · 构陷：同朝为官的宿敌，名望不低于对方的一边有一成机会使对方去职；
//     · 复仇：有杀亲之仇的，有五分机会遣刺客，三成五得手(刺君主减半)；
//     · 投敌：与本国君主有仇的统领、官员，本国正与人交战时有五分机会投奔敌国；
//   朝堂：恩主被弹劾，在朝为官的门生每人使弹劾难成一成(至多两成)；弹劾宿敌易成一成五(见 CourtSystem.TryImpeach)。
//   恩怨写进双方生平(大事)，列传里列出(见 BiographySystem.Compose)。
public static class PersonalTieSystem
{
    public const int Patron = 1, Protege = 2, Rival = 3;
    public const string CauseImpeached = "impeached", CauseKinKilled = "kin_killed", CauseCaptured = "captured",
        CauseExam = "exam", CauseDismissed = "dismissed", CauseRank = "rank", CauseFavourite = "favourite";
    private const int MaxTies = 3, MaxAvengers = 3;
    private const float FrameChance = 0.1f, RevengeChance = 0.05f, RevengeSuccess = 0.35f, DefectChance = 0.05f;

    #region 结缘结仇

    private static void Add(PersonalClanIdentity owner, PersonalClanIdentity other, int kind, string cause)
    {
        if (owner == null || other == null || owner.id == other.id || World.world == null) return;
        owner.ties ??= new List<PersonalTie>();
        owner.ties.RemoveAll(tie => tie == null || tie.other == other.id);
        if (owner.ties.Count >= MaxTies)
        {
            // 满了先挤掉最早的恩主/门生，都是宿敌就挤掉最早的宿敌
            PersonalTie drop = owner.ties.Where(tie => tie.kind != Rival).OrderBy(tie => tie.since).FirstOrDefault() ??
                               owner.ties.OrderBy(tie => tie.since).First();
            owner.ties.Remove(drop);
        }
        owner.ties.Add(new PersonalTie { other = other.id, kind = kind, cause = cause, since = World.world.getCurWorldTime() });
    }

    public static void MakeRivals(Actor a, Actor b, string cause)
    {
        PersonalClanIdentity ia = a?.GetPersonalIdentity(), ib = b?.GetPersonalIdentity();
        if (ia == null || ib == null || ia.id == ib.id || TieOf(ia, ib.id)?.kind == Rival) return;
        Add(ia, ib, Rival, cause);
        Add(ib, ia, Rival, cause);
        string text = LM.Get("tie_cause_" + cause);
        BiographySystem.Note(a, string.Format(LM.Get("bio_tie_rival"), b.getName(), text), b);
        BiographySystem.Note(b, string.Format(LM.Get("bio_tie_rival"), a.getName(), text), a);
    }

    public static void MakePatron(Actor protege, Actor patron, string cause)
    {
        PersonalClanIdentity ip = protege?.GetPersonalIdentity(), ia = patron?.GetPersonalIdentity();
        if (ip == null || ia == null || ip.id == ia.id || TieOf(ip, ia.id) != null) return;
        Add(ip, ia, Patron, cause);
        Add(ia, ip, Protege, cause);
        BiographySystem.Note(protege, string.Format(LM.Get("bio_tie_patron"), patron.getName()), patron);
    }

    // 要人被杀：死者在世的子与兄弟(至多三人)与凶手结下杀亲之仇
    public static void OnKinKilled(Actor victim, Actor killer)
    {
        PersonalClanIdentity dead = victim?.GetPersonalIdentity();
        PersonalClanIdentity murderer = killer?.GetPersonalIdentity();
        if (dead == null || murderer == null || World.world == null) return;
        IEnumerable<PersonalClanIdentity> kin = SpecificClanManager.getChildren(dead).Select(pair => pair.Item2)
            .Concat(SpecificClanManager.GetSiblingsWithRelation(dead).Select(pair => pair.Item2));
        int count = 0;
        foreach (PersonalClanIdentity relative in kin)
        {
            if (count >= MaxAvengers) break;
            if (relative == null || !relative.is_alive || relative.id == murderer.id || relative.age < 12) continue;
            Add(relative, murderer, Rival, CauseKinKilled);
            Add(murderer, relative, Rival, CauseKinKilled);
            relative.RecordPersonalHistory(string.Format(LM.Get("bio_tie_kin_killed"), victim.getName(), killer.getName()),
                eventKey: "tie", relatedActorId: killer.id, major: true);
            count++;
        }
    }

    public static PersonalTie TieOf(PersonalClanIdentity owner, long otherId) =>
        owner?.ties?.FirstOrDefault(tie => tie != null && tie.other == otherId);

    public static bool AreRivals(Actor a, Actor b) =>
        TieOf(a?.GetPersonalIdentity(), b?.GetPersonalIdentity()?.id ?? -1L)?.kind == Rival;

    // 在朝为官、在世的门生人数
    public static int ProtegesInOffice(Actor patron)
    {
        PersonalClanIdentity identity = patron?.GetPersonalIdentity();
        if (identity?.ties == null) return 0;
        int count = 0;
        foreach (PersonalTie tie in identity.ties)
        {
            if (tie?.kind != Protege) continue;
            Actor actor = SpecificClanManager.getPerson(tie.other)?._actor;
            if (actor?.data != null && actor.isAlive() && actor.IsOnOffice() && actor.kingdom?.GetEmpire() == patron.kingdom?.GetEmpire())
                count++;
        }
        return count;
    }

    #endregion

    #region 年度结算

    public static void Yearly()
    {
        if (World.world?.units == null) return;
        int acted = 0;
        foreach (Actor actor in World.world.units.ToList())
        {
            if (acted >= 12) break;
            PersonalClanIdentity identity = actor?.data == null || actor.isRekt() || !actor.isAlive() ? null : actor.GetPersonalIdentity();
            if (identity?.ties == null || identity.ties.Count == 0) continue;
            try
            {
                if (Act(actor, identity)) acted++;
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][恩怨] {actor.getName()} 结算失败: {exception.Message}");
            }
        }
    }

    private static bool Act(Actor actor, PersonalClanIdentity identity)
    {
        foreach (PersonalTie tie in identity.ties.Where(tie => tie?.kind == Rival).ToList())
        {
            Actor rival = SpecificClanManager.getPerson(tie.other)?._actor;
            if (rival?.data == null || rival.isRekt() || !rival.isAlive()) continue;
            if (tie.cause == CauseKinKilled && UnityEngine.Random.value < RevengeChance) return Revenge(actor, rival);
            if (rival.isKing() && rival.kingdom == actor.kingdom && !actor.isKing() &&
                (CorpsSystem.IsGeneral(actor) || actor.IsOnOffice()) &&
                UnityEngine.Random.value < DefectChance * (AmbitionSystem.Wants(actor, AmbitionSystem.Throne) ? 2f : 1f) &&
                Defect(actor, rival)) return true;
            if (actor.IsOnOffice() && rival.IsOnOffice() && actor.kingdom?.GetEmpire() is Empire empire &&
                rival.kingdom?.GetEmpire() == empire && actor.renown >= rival.renown && UnityEngine.Random.value < FrameChance)
                return Frame(actor, rival);
        }
        return false;
    }

    // 构陷：使宿敌去职
    private static bool Frame(Actor actor, Actor rival)
    {
        OfficeObject office = rival.GetOffice();
        if (office == null) return false;
        office.RemoveActor();
        BiographySystem.Note(actor, string.Format(LM.Get("bio_tie_framed_other"), rival.getName()), rival);
        BiographySystem.Note(rival, string.Format(LM.Get("bio_tie_framed_by"), actor.getName()), actor);
        Record(actor.kingdom, string.Format(LM.Get("tie_frame_history"), actor.getName(), rival.getName()), actor);
        return true;
    }

    // 复仇：遣刺客
    private static bool Revenge(Actor actor, Actor rival)
    {
        float chance = RevengeSuccess * (rival.isKing() ? 0.5f : 1f);
        if (UnityEngine.Random.value < chance)
        {
            string text = string.Format(LM.Get("tie_revenge_history"), actor.getName(), rival.getName());
            BiographySystem.Note(actor, string.Format(LM.Get("bio_tie_revenge"), rival.getName()), rival);
            Record(rival.kingdom, text, rival);
            BiographySystem.Kill(rival, string.Format(LM.Get("death_cause_revenge"), actor.getName()), actor);
        }
        else
        {
            BiographySystem.Note(actor, string.Format(LM.Get("bio_tie_revenge_failed"), rival.getName()), rival);
            BiographySystem.Note(rival, string.Format(LM.Get("bio_tie_survived_assassin"), actor.getName()), actor);
        }
        return true;
    }

    // 投敌：与本国君主有仇，投奔正与本国交战的敌国
    private static bool Defect(Actor actor, Actor ruler)
    {
        Kingdom home = actor.kingdom;
        Kingdom enemy = home?.getWars().Where(war => war?.data != null && !war.hasEnded())
            .SelectMany(war => war.isAttacker(home) ? war._list_defenders : war._list_attackers)
            .Where(kingdom => kingdom?.data != null && !kingdom.isRekt() && kingdom.capital?.data != null && !kingdom.capital.isRekt())
            .OrderByDescending(kingdom => kingdom.countCities()).FirstOrDefault();
        if (enemy == null) return false;
        CorpsSystem.LoseGeneral(actor);
        actor.GetOffice()?.RemoveActor();
        string text = string.Format(LM.Get("tie_defect_history"), actor.getName(), ruler.getName(), enemy.GetKingdomName());
        BiographySystem.Note(actor, string.Format(LM.Get("bio_tie_defected"), ruler.getName(), enemy.GetKingdomName()), ruler);
        Record(home, text, actor);
        actor.joinCity(enemy.capital);
        actor.setKingdom(enemy);
        return true;
    }

    private static void Record(Kingdom kingdom, string text, Actor actor)
    {
        if (kingdom == null) return;
        Empire empire = kingdom.GetEmpire();
        if (empire != null && !empire.isRekt()) EventRecorder.Record(empire, text, actor, kingdom);
        else EventRecorder.Record(kingdom, text, actor);
    }

    #endregion

    // 列传里的一句：座主、门生、宿敌
    public static string Describe(PersonalClanIdentity person)
    {
        if (person?.ties == null || person.ties.Count == 0) return "";
        var parts = new List<string>();
        foreach (PersonalTie tie in person.ties)
        {
            string name = SpecificClanManager.getPerson(tie?.other ?? -1L)?.name;
            if (tie == null || string.IsNullOrWhiteSpace(name)) continue;
            parts.Add(tie.kind switch
            {
                Patron => string.Format(LM.Get("biography_tie_patron"), name),
                Protege => string.Format(LM.Get("biography_tie_protege"), name),
                _ => string.Format(LM.Get("biography_tie_rival"), name, LM.Get("tie_cause_" + tie.cause))
            });
        }
        return parts.Count == 0 ? "" : string.Join(LM.Get("biography_tie_separator"), parts) + LM.Get("biography_period");
    }
}

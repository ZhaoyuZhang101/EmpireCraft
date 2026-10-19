using System;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 野心(用户 2026-10-19 选"功高震主与野心")：要人(在任官员、统领、城主、名将名臣、有爵有封地的)各有一个志向，记在族谱身份上：
//   封侯(peerage) · 拜相(chancellor) · 敛财(wealth) · 扬名(fame) · 篡位(throne)；有野心(ambitious)的多想拜相篡位，贪婪(greedy)的多想敛财。
//   每年(RoyalHouseSystem 年度)：
//     · 达成的记一笔"得偿所愿"(封了爵、当上首辅、家资过五百、成了名将名臣、登上君位)，死时没达成的列传里写"抱憾"；
//     · 想拜相的在朝官员，一成机会与当朝首辅结怨(引出构陷，见 PersonalTieSystem)；
//     · 想敛财的在任官员每年侵吞一笔，所在城腐败上升；
//     · 想篡位的：功高震主时自立的分量加倍(见 ThroneSuspicion)，与君主结仇后投敌的机会加倍(见 PersonalTieSystem)。
public static class AmbitionSystem
{
    public const string Peerage = "peerage", Chancellor = "chancellor", Wealth = "wealth", Fame = "fame", Throne = "throne";
    private const int WealthGoal = 500, Skim = 12;
    private const float ChancellorRivalChance = 0.1f, SkimCorruption = 0.01f;

    public static bool Wants(Actor actor, string ambition)
    {
        PersonalClanIdentity identity = actor?.GetPersonalIdentity();
        return identity != null && !identity.ambition_done && identity.ambition == ambition;
    }

    private static bool Notable(Actor actor) =>
        actor.IsOnOffice() || CorpsSystem.IsGeneral(actor) || actor.isCityLeader() || FamousTalentSystem.IsFamous(actor) ||
        (actor.GetOwnedTitle()?.Count ?? 0) > 0;

    public static void Yearly()
    {
        if (World.world?.units == null) return;
        foreach (Actor actor in World.world.units.ToList())
        {
            if (actor?.data == null || actor.isRekt() || !actor.isAlive() || !actor.isAdult() || actor.asset?.civ != true) continue;
            PersonalClanIdentity identity = actor.GetPersonalIdentity();
            if (identity == null) continue;
            try
            {
                if (string.IsNullOrEmpty(identity.ambition))
                {
                    if (Notable(actor) && !actor.isKing()) Assign(actor, identity);
                    continue;
                }
                if (identity.ambition_done) continue;
                if (Fulfilled(actor, identity.ambition))
                {
                    identity.ambition_done = true;
                    BiographySystem.Note(actor, string.Format(LM.Get("bio_ambition_done"), LM.Get("ambition_" + identity.ambition)));
                    continue;
                }
                Pursue(actor, identity.ambition);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][野心] {actor.getName()} 结算失败: {exception.Message}");
            }
        }
    }

    private static void Assign(Actor actor, PersonalClanIdentity identity)
    {
        (string key, float weight)[] options = actor.hasTrait("ambitious")
            ? new[] { (Throne, 0.3f), (Chancellor, 0.4f), (Peerage, 0.3f) }
            : actor.hasTrait("greedy")
                ? new[] { (Wealth, 0.7f), (Peerage, 0.2f), (Chancellor, 0.1f) }
                : new[] { (Peerage, 0.35f), (Chancellor, 0.25f), (Fame, 0.3f), (Wealth, 0.1f) };
        float roll = UnityEngine.Random.value * options.Sum(option => option.weight);
        string chosen = options[options.Length - 1].key;
        foreach ((string key, float weight) in options)
        {
            if (roll < weight)
            {
                chosen = key;
                break;
            }
            roll -= weight;
        }
        identity.ambition = chosen;
        identity.ambition_done = Fulfilled(actor, chosen);
    }

    private static bool Fulfilled(Actor actor, string ambition) => ambition switch
    {
        Peerage => (actor.GetOwnedTitle()?.Count ?? 0) > 0 || !string.IsNullOrWhiteSpace(actor.GetPersonalIdentity()?.PeeragesLevel),
        Chancellor => actor.kingdom?.GetEmpire()?.GetCabinetLeader() == actor,
        Wealth => actor.money >= WealthGoal,
        Fame => FamousTalentSystem.IsFamous(actor),
        Throne => actor.isKing(),
        _ => true
    };

    private static void Pursue(Actor actor, string ambition)
    {
        switch (ambition)
        {
            case Chancellor:
                Actor leader = actor.kingdom?.GetEmpire()?.GetCabinetLeader();
                if (leader?.data != null && leader != actor && leader.isAlive() && actor.IsOnOffice() &&
                    UnityEngine.Random.value < ChancellorRivalChance)
                    PersonalTieSystem.MakeRivals(actor, leader, PersonalTieSystem.CauseRank);
                break;
            case Wealth:
                if (!actor.IsOnOffice() || actor.city?.data == null) break;
                actor.data.money += Skim;
                actor.city.AddCorruptionRate(SkimCorruption);
                break;
        }
    }

    // 列传里的一句：志在什么、得偿所愿或抱憾
    public static string Describe(PersonalClanIdentity person)
    {
        if (person == null || string.IsNullOrEmpty(person.ambition)) return "";
        string goal = LM.Get("ambition_" + person.ambition);
        if (person.ambition_done) return string.Format(LM.Get("biography_ambition_done"), goal);
        return string.Format(LM.Get(person.is_alive ? "biography_ambition" : "biography_ambition_failed"), goal);
    }
}

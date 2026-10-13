using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 夺嫡(皇帝的成年儿子之间争储、争位)：
//   · 实力：名望，加上统兵(是军团统领 +60)、封爵(每块封地 +30)、年纪(每岁 +0.5)；
//   · 废太子(每年)：太子之外最强的皇子比太子强三成以上时，皇帝有机会废太子、改立他(三分，昏聩、荒淫、暴虐之君加倍，
//     明君减半，皇帝年过五十再加五成)；被废的太子四分之一被赐死；
//   · 兵变夺位(每年)：皇帝年过四十五，统兵的皇子(或比太子强五成以上的)有一分五的机会发动兵变——诛杀太子、
//     逼皇帝退位(留作太上皇)，自立为帝；昏聩、暴虐之君加倍；
//   · 诸王争位(骤然驾崩或寿终而没有册立储君，见 RoyalHouseSystem)：一个月后新君已立，兄弟里最强的一位若不弱于新君八成，
//     有机会起兵——按实力比定胜负，胜则弑新君自立，败则被诛；新君年幼时他更敢动手；
//   · 怎么继的位记在纪年上(EmpireCraftHistory.succession_note)，世系图里标出来(见 DynastyLineageWindow)。
public static class PrinceStruggleSystem
{
    private const float DeposeChance = 0.03f, CoupChance = 0.015f, ContestChance = 0.35f;
    private const int PendingMonths = 6;

    public const string NoteDeposed = "deposed", NoteCoup = "coup", NoteStrife = "strife", NotePalaceCoup = "palace_coup",
        NoteUsurp = "usurp", NoteMilitaryCoup = "military_coup", NoteCourtChoice = "court_choice", NoteAcclaimed = "acclaimed";

    private static readonly Dictionary<long, (double at, float severity)> Contests = new();

    public static void QueueContest(Empire empire, float severity)
    {
        if (empire?.data == null) return;
        Contests[empire.data.id] = (World.world.getCurWorldTime(), severity);
    }

    // 记下下一位君主是怎么继的位(新君即位时写进纪年，见 HistoryRecordSystem.RecordNewEmperorHistory)
    public static void Note(Kingdom kingdom, Actor successor, string note)
    {
        if (kingdom?.data == null || successor?.data == null) return;
        var data = kingdom.GetOrCreate();
        data.succession_note = note;
        data.succession_note_actor = successor.id;
    }

    private static float Strength(Actor actor) =>
        actor?.data == null ? 0f
            : actor.renown + (CorpsSystem.IsGeneral(actor) ? 60f : 0f) + (actor.GetOwnedTitle()?.Count ?? 0) * 30f +
              Mathf.Min(actor.getAge(), 60) * 0.5f;

    private static List<Actor> Princes(Empire empire, Actor emperor) =>
        (emperor.getChildren(false) ?? Enumerable.Empty<Actor>())
        .Where(son => son?.data != null && !son.isRekt() && son.isAlive() && son.isSexMale() && son.isAdult() &&
                      !son.isKing() && son.kingdom?.GetEmpire() == empire).ToList();

    private static float Mood(Actor emperor) => RulerQualitySystem.Of(emperor) switch
    {
        RulerQualitySystem.Grade.Wise => 0.5f,
        RulerQualitySystem.Grade.Fatuous or RulerQualitySystem.Grade.Dissolute or RulerQualitySystem.Grade.Tyrant => 2f,
        _ => 1f
    };

    public static void Yearly(Empire empire)
    {
        Actor emperor = empire?.Emperor;
        Kingdom core = empire?.CoreKingdom;
        if (emperor?.data == null || !emperor.isAlive() || core?.data == null ||
            !EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckKing.NeedSuccession(core)) return;
        List<Actor> princes = Princes(empire, emperor);
        if (princes.Count < 2) return;
        Actor heir = core.HasHeir() ? core.GetHeir() : null;
        if (heir == null || !princes.Contains(heir)) return;
        Actor rival = princes.Where(prince => prince != heir).OrderByDescending(Strength).First();
        float ratio = Strength(rival) / Mathf.Max(1f, Strength(heir));
        float mood = Mood(emperor);
        // 兵变夺位
        if (emperor.getAge() >= 45 && (CorpsSystem.IsGeneral(rival) || ratio >= 1.5f) &&
            UnityEngine.Random.value < CoupChance * (mood > 1f ? 2f : 1f))
        {
            Record(empire, string.Format(LM.Get("prince_coup_history"), rival.getName(), heir.getName(), emperor.getName()), rival);
            heir.die(false, AttackType.Other, true, true);
            Install(empire, rival, NoteCoup);
            return;
        }
        // 废太子
        if (ratio >= 1.3f && UnityEngine.Random.value < DeposeChance * mood * (emperor.getAge() >= 50 ? 1.5f : 1f))
        {
            core.GetOrCreate().designated_heir_id = rival.id;
            core.SetHeir(rival);
            Note(core, rival, NoteDeposed);
            Record(empire, string.Format(LM.Get("prince_deposed_history"), emperor.getName(), heir.getName(), rival.getName()), emperor);
            if (UnityEngine.Random.value < 0.25f)
            {
                Record(empire, string.Format(LM.Get("prince_deposed_killed_history"), heir.getName()), heir);
                heir.die(false, AttackType.Other, true, true);
            }
        }
    }

    // 诸王争位：新君即位一个月后结算；半年内一直没有新君就作罢
    public static void Monthly()
    {
        foreach (var pair in Contests.ToList())
        {
            Empire empire = ModClass.EMPIRE_MANAGER?.get(pair.Key);
            if (empire?.data == null || Date.getMonthsSince(pair.Value.at) >= PendingMonths)
            {
                Contests.Remove(pair.Key);
                continue;
            }
            Actor emperor = empire.Emperor;
            if (Date.getMonthsSince(pair.Value.at) < 1 || emperor?.data == null || !emperor.isAlive()) continue;
            Contests.Remove(pair.Key);
            Contest(empire, emperor, pair.Value.severity);
        }
    }

    private static void Contest(Empire empire, Actor emperor, float severity)
    {
        PersonalClanIdentity identity = emperor.GetPersonalIdentity();
        if (identity == null) return;
        Actor rival = SpecificClanManager.GetSiblingsWithRelation(identity).Select(pair => pair.Item2)
            .Where(person => person != null && person.is_alive && person.sex == ActorSex.Male)
            .Select(person => person.Realize())
            .Where(actor => actor?.data != null && actor.isAlive() && actor.isAdult() && actor != emperor && !actor.isKing() &&
                            actor.kingdom?.GetEmpire() == empire)
            .OrderByDescending(Strength).FirstOrDefault();
        if (rival == null) return;
        float ratio = Strength(rival) / Mathf.Max(1f, Strength(emperor)) * (emperor.isAdult() ? 1f : 1.5f);
        if (ratio < 0.8f || UnityEngine.Random.value >= ContestChance * severity) return;
        if (UnityEngine.Random.value < ratio / (ratio + 1f))
        {
            Record(empire, string.Format(LM.Get("prince_strife_win_history"), rival.getName(), emperor.getName()), rival);
            Install(empire, rival, NoteStrife);
            if (emperor.isAlive()) emperor.die(false, AttackType.Other, true, true);
        }
        else
        {
            Record(empire, string.Format(LM.Get("prince_strife_lose_history"), rival.getName()), rival);
            rival.die(false, AttackType.Other, true, true);
        }
        foreach (City city in empire.CoreKingdom?.cities?.ToList() ?? new List<City>())
        {
            CityStabilityData state = city?.data == null ? null : CityStabilitySystem.State(city);
            if (state != null) state.stability = Mathf.Max(0f, state.stability - 6f);
        }
    }

    // 换上新君(照宫廷政变的做法：旧君离位，新君坐上君主官职)
    private static void Install(Empire empire, Actor successor, string note)
    {
        Kingdom core = empire.CoreKingdom;
        Note(core, successor, note);
        core.removeKing();
        OfficeObject office = core.GetOffice();
        if (office != null)
        {
            office.meta_object = core;
            office.SetActor(successor);
        }
        else core.setKing(successor);
    }

    private static void Record(Empire empire, string text, Actor actor)
    {
        empire.RecordHistory(directContent: text, actorId: actor?.id ?? -1L);
        TranslateHelper.LogEventMessage(text, empire.CoreKingdom);
    }
}

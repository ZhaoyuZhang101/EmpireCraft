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

// 后宫争储(华夏：律令制、分封制)：荒淫或昏聩之君膝下有两个以上皇子、且诸子不同母时，
//   宠妃每年有一成二(荒淫)或六分(昏聩)的可能让皇帝废长立幼，改立她的儿子为储君(册立储君，ResolveHeir 优先)；
//   被废的皇子有三成可能"暴毙"(夺嫡之祸)。
public static class HaremSuccessionSystem
{
    private const float DissoluteChance = 0.12f, FatuousChance = 0.06f, MurderChance = 0.3f;

    public static void Yearly(Empire empire)
    {
        try
        {
            Kingdom core = empire?.CoreKingdom;
            Actor emperor = empire?.Emperor;
            if (core?.data == null || emperor?.data == null || !emperor.isAlive() ||
                core.GetRegime()?.type is not (RegimeType.LvLing or RegimeType.ZhouFeudalism)) return;
            float chance = RulerQualitySystem.Of(emperor) switch
            {
                RulerQualitySystem.Grade.Dissolute => DissoluteChance,
                RulerQualitySystem.Grade.Fatuous => FatuousChance,
                _ => 0f
            };
            if (chance <= 0f || UnityEngine.Random.value >= chance) return;
            List<Actor> sons = (emperor.getChildren(false) ?? Enumerable.Empty<Actor>())
                .Where(child => child?.data != null && !child.isRekt() && child.isAlive() && child.isSexMale() &&
                                !child.isKing() && child.kingdom?.GetEmpire() == empire).ToList();
            if (sons.Count < 2) return;
            Actor heir = core.HasHeir() ? core.GetHeir() : null;
            Actor passedOver = heir != null && sons.Contains(heir) ? heir : sons.OrderByDescending(son => son.getAge()).First();
            Actor rivalMother = Mother(passedOver);
            Actor favourite = sons.Where(son => son != passedOver && Mother(son) is Actor mother && mother != rivalMother)
                .OrderBy(son => son.getAge()).FirstOrDefault();
            Actor consort = Mother(favourite);
            if (favourite == null || consort == null) return;
            core.GetOrCreate().designated_heir_id = favourite.id;
            core.SetHeir(favourite);
            PrinceStruggleSystem.Note(core, favourite, PrinceStruggleSystem.NoteDeposed);
            string text = string.Format(LM.Get("harem_heir_history"), consort.getName(), emperor.getName(),
                favourite.getName(), passedOver.getName());
            Record(empire, emperor, text);
            if (UnityEngine.Random.value >= MurderChance) return;
            string name = passedOver.getName();
            passedOver.die(false, AttackType.Other, true, true);
            Record(empire, emperor, string.Format(LM.Get("harem_heir_death_history"), name));
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][后宫] 争储失败: {exception.Message}");
        }
    }

    private static Actor Mother(Actor child) =>
        child?.getParents()?.FirstOrDefault(parent => parent?.data != null && !parent.isRekt() && parent.isSexFemale());

    private static void Record(Empire empire, Actor emperor, string text)
    {
        empire.RecordHistory(directContent: text, actorId: emperor.id, kingdomId: empire.CoreKingdom.id);
        TranslateHelper.LogEventMessage(text, empire.CoreKingdom);
    }
}

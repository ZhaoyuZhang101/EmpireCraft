using System;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 外戚干政(律令制，原型两汉外戚——霍、窦、梁、何，王莽代汉)：
//   · 外戚宗族：太后(先帝皇后或皇帝生母)在世时为太后的娘家，否则为皇后(皇帝的配偶)的娘家；皇族本家不算；
//   · 外戚首领：外戚宗族里在本帝国、成年、名望最高的人(太后、皇后本人除外)，即"大将军"；
//   · 幼主即位：外戚首领名望不低于朝中辅政人选的一半时，以大将军辅政，优先于朝臣与太后本人；
//     皇帝成年后外戚照样专权(不必身居中枢)，靠太后撑腰或皇帝庸弱、昏聩、荒淫；
//   · 成年的庸弱、昏聩、荒淫之君在位：外戚首领名望够了(入场门槛同权臣)也能专权；
//   · 此后照权臣流程推进：控制朝政 → 封公 → 加九锡 → 篡位(王莽代汉)；
//   · 诛外戚：外戚专权时，成年、不昏聩荒淫的皇帝每年有机会联合近臣发难——
//     成：外戚首领去职、永不录用、家财充公；败：事泄，皇帝反被挟制(控制度大涨、正统 -10)。
public static class ConsortKinSystem
{
    private const float RegentShare = 0.5f;
    private const float PurgeBase = 0.06f;

    public static SpecificClan ConsortClan(Empire empire)
    {
        Actor emperor = empire?.Emperor;
        if (emperor?.data == null) return null;
        SpecificClan royal = empire.EmpireSpecificClan;
        foreach (Actor woman in new[] { empire.GetEmpressDowager(), emperor.lover })
        {
            SpecificClan clan = woman?.data == null ? null : woman.GetSpecificClan();
            if (clan != null && (royal == null || !SpecificClanManager.SameLineage(clan, royal))) return clan;
        }
        return null;
    }

    public static bool IsConsortKin(Empire empire, Actor actor)
    {
        SpecificClan clan = ConsortClan(empire);
        SpecificClan own = actor?.data == null ? null : actor.GetSpecificClan();
        return clan != null && own != null && SpecificClanManager.SameLineage(own, clan);
    }

    // 外戚首领(大将军)
    public static Actor Chief(Empire empire)
    {
        SpecificClan clan = ConsortClan(empire);
        if (clan == null) return null;
        Actor dowager = empire.GetEmpressDowager(), emperor = empire.Emperor, empress = emperor?.lover;
        return clan.AllAliveMembers?.Where(actor => actor?.data != null && !actor.isRekt() && actor.isAlive() &&
                                                    actor.isAdult() && actor != dowager && actor != empress &&
                                                    actor != emperor && !actor.isKing() &&
                                                    actor.kingdom?.GetEmpire() == empire &&
                                                    actor.CanServeOffice(empire.CoreKingdom))
            .OrderByDescending(actor => actor.renown).FirstOrDefault();
    }

    // 幼主：外戚首领以大将军辅政(名望不低于朝中辅政人选的一半)
    public static Actor RegentFor(Empire empire, Actor courtCandidate)
    {
        Actor chief = Chief(empire);
        return chief != null && chief.renown >= (courtCandidate?.renown ?? 0) * RegentShare ? chief : null;
    }

    // 成年皇帝庸弱、昏聩、荒淫，或年轻皇帝(不满 25 岁)有太后撑腰：外戚首领名望够了就专权
    public static Actor DominantFor(Empire empire, int entryInfluence)
    {
        if (!Backed(empire)) return null;
        Actor chief = Chief(empire);
        return chief != null && chief.renown >= entryInfluence ? chief : null;
    }

    public static bool Backed(Empire empire)
    {
        Actor emperor = empire?.Emperor;
        if (emperor?.data == null) return false;
        // 宗室外戚一派独大时外戚随时有撑腰(见 FactionBalanceSystem)
        return RulerQualitySystem.IsBad(emperor) || emperor.getAge() < YoungEmperor && empire.GetEmpressDowager() != null ||
               FactionBalanceSystem.BacksConsortKin(empire);
    }

    private const int YoungEmperor = 25;

    #region 诛外戚

    // 每年(ConstitutionalEconomySystem 年度结算)
    public static void Yearly(Empire empire)
    {
        try
        {
            if (empire?.data == null || !empire.data.powerful_minister_is_consort_kin) return;
            Actor emperor = empire.Emperor, kin = empire.GetPowerfulMinister();
            if (emperor?.data == null || !emperor.isAlive() || !emperor.isAdult() || kin?.data == null ||
                RulerQualitySystem.Indulgent(emperor)) return;
            float chance = PurgeBase + Mathf.Clamp((emperor.stats["stewardship"] - 10f) * 0.01f, -0.04f, 0.1f);
            if (RulerQualitySystem.Of(emperor) == RulerQualitySystem.Grade.Weak) chance *= 0.5f;
            if (UnityEngine.Random.value >= chance) return;
            float success = Mathf.Clamp(0.35f + empire.Mandate / 200f - empire.data.powerful_minister_progress / 300f +
                                        (emperor.renown - kin.renown) / 2000f, 0.1f, 0.85f);
            if (UnityEngine.Random.value < success) Purge(empire, emperor, kin);
            else Backfire(empire, emperor, kin);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][外戚] 年度结算失败: {exception.Message}");
        }
    }

    private static void Purge(Empire empire, Actor emperor, Actor kin)
    {
        string kinName = kin.getName();
        Kingdom core = empire.CoreKingdom;
        int fortune = Math.Max(0, kin.money);
        if (fortune > 0)
        {
            kin.data.money = 0;
            core.AddMoney(fortune, TreasuryCategory.RecoveredFunds);
        }
        kin.data.renown = kin.renown / 2;
        kin.BanFromOffice(empire.id);
        empire.ResetPowerfulMinister();
        empire.AddMandate(5);
        string text = string.Format(LM.Get("history_consort_purged"), emperor.getName(), kinName, MoneyDisplay.Format(fortune));
        empire.RecordHistory(directContent: text, actorId: emperor.id);
        emperor.RecordPersonalHistory(text, relatedActorId: kin.id);
        kin.RecordPersonalHistory(text, relatedActorId: emperor.id);
        TranslateHelper.LogEventMessage(text, core);
    }

    private static void Backfire(Empire empire, Actor emperor, Actor kin)
    {
        empire.data.powerful_minister_progress = Math.Min(100, empire.data.powerful_minister_progress + 25);
        empire.AddMandate(-10);
        string text = string.Format(LM.Get("history_consort_purge_failed"), emperor.getName(), kin.getName());
        empire.RecordHistory(directContent: text, actorId: emperor.id);
        emperor.RecordPersonalHistory(text, relatedActorId: kin.id);
        TranslateHelper.LogEventMessage(text, empire.CoreKingdom);
    }

    #endregion
}

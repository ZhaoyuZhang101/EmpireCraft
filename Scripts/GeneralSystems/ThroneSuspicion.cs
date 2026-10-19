using System;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 功高震主(用户 2026-10-19 选"功高震主与野心")：成名(名、国柱)的名将名臣，君主的猜忌每年累积(见 FamousTalentSystem.Assess)——
//   国柱多、名少；名望压过君主越多越快；暴君翻倍、昏君荒淫多三成、贤君只剩四成；君主多疑再乘一倍半；手握军团的名将再多三成。
//   换了君主猜忌减半。满一百时有个了断(之后从零算起)：
//     · 贤君：功成身退六成、罢官四成；
//     · 寻常、庸弱：身退三成、罢官五成、赐死一成、拥兵自立一成；
//     · 昏君、荒淫：罢官五成、赐死三成、自立两成；暴君：赐死六成、自立三成、罢官一成；
//     志在篡位或有野心(ambitious)的，自立的分量加倍；自立只给手握军团、驻地不是都城的名将，做不到改为罢官。
//   赐死的记"伏诛"，子与兄弟与君主结下杀亲之仇(见 PersonalTieSystem)；罢官的与君主结怨；自立的以驻地据城称王，带走所统之军，与旧主开战。
public static class ThroneSuspicion
{
    private const float Full = 100f;

    public static void Yearly(TalentRecord record, Actor actor)
    {
        if (record == null || actor?.data == null || record.rank < TalentRank.Famous || actor.isKing()) return;
        Kingdom kingdom = actor.kingdom;
        Empire empire = kingdom?.GetEmpire();
        Actor ruler = empire?.Emperor ?? kingdom?.king;
        if (ruler?.data == null || !ruler.isAlive() || ruler == actor || kingdom.HasHeir() && kingdom.GetHeir() == actor) return;
        if (record.suspicion_ruler_id != ruler.id)
        {
            record.suspicion *= 0.5f;
            record.suspicion_ruler_id = ruler.id;
        }
        RulerQualitySystem.Grade grade = RulerQualitySystem.Of(ruler);
        float gain = (record.rank == TalentRank.Pillar ? 8f : 4f) *
                     Mathf.Clamp(actor.renown / Mathf.Max(1f, ruler.renown), 0.5f, 3f) *
                     grade switch
                     {
                         RulerQualitySystem.Grade.Wise => 0.4f,
                         RulerQualitySystem.Grade.Weak => 0.8f,
                         RulerQualitySystem.Grade.Fatuous or RulerQualitySystem.Grade.Dissolute => 1.3f,
                         RulerQualitySystem.Grade.Tyrant => 2f,
                         _ => 1f
                     } *
                     (ruler.hasTrait("paranoid") ? 1.5f : 1f) * (CorpsSystem.IsGeneral(actor) ? 1.3f : 1f);
        record.suspicion += gain;
        if (record.suspicion < Full) return;
        record.suspicion = 0f;
        try
        {
            Resolve(actor, ruler, kingdom, empire, grade);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][功高震主] {actor.getName()} 结算失败: {exception.Message}");
        }
    }

    private static void Resolve(Actor actor, Actor ruler, Kingdom kingdom, Empire empire, RulerQualitySystem.Grade grade)
    {
        (float retire, float dismiss, float execute, float rebel) = grade switch
        {
            RulerQualitySystem.Grade.Wise => (0.6f, 0.4f, 0f, 0f),
            RulerQualitySystem.Grade.Fatuous or RulerQualitySystem.Grade.Dissolute => (0f, 0.5f, 0.3f, 0.2f),
            RulerQualitySystem.Grade.Tyrant => (0f, 0.1f, 0.6f, 0.3f),
            _ => (0.3f, 0.5f, 0.1f, 0.1f)
        };
        if (AmbitionSystem.Wants(actor, AmbitionSystem.Throne) || actor.hasTrait("ambitious")) rebel *= 2f;
        float roll = UnityEngine.Random.value * (retire + dismiss + execute + rebel);
        string name = actor.getName(), rulerName = ruler.getName();
        if (roll < retire)
        {
            Step(actor);
            Report(kingdom, empire, string.Format(LM.Get("overshadow_retire_history"), name, rulerName), actor);
            BiographySystem.Note(actor, string.Format(LM.Get("bio_overshadow_retire"), rulerName), ruler);
            return;
        }
        roll -= retire;
        if (roll < dismiss || roll >= dismiss + execute && !Rebel(actor, ruler, kingdom, empire))
        {
            Step(actor);
            Report(kingdom, empire, string.Format(LM.Get("overshadow_dismissed_history"), name, rulerName), actor);
            BiographySystem.Note(actor, string.Format(LM.Get("bio_overshadow_dismissed"), rulerName), ruler);
            PersonalTieSystem.MakeRivals(actor, ruler, PersonalTieSystem.CauseDismissed);
            return;
        }
        if (roll < dismiss + execute)
        {
            Report(kingdom, empire, string.Format(LM.Get("overshadow_executed_history"), name, rulerName), actor);
            BiographySystem.Note(ruler, string.Format(LM.Get("bio_overshadow_ordered_death"), name), actor);
            BiographySystem.Kill(actor, string.Format(LM.Get("death_cause_overshadow"), rulerName), ruler, executed: true);
        }
    }

    // 卸去官职与兵权
    private static void Step(Actor actor)
    {
        CorpsSystem.LoseGeneral(actor);
        actor.GetOffice()?.RemoveActor();
    }

    // 拥兵自立：以所统之军的驻地据城称王，带走这支军，与旧主开战；驻地是都城、不在本国、没有兵权的做不到
    private static bool Rebel(Actor actor, Actor ruler, Kingdom kingdom, Empire empire)
    {
        CorpsData corps = CorpsSystem.CorpsOf(actor);
        City seat = corps == null ? null : CorpsSystem.Home(corps);
        if (seat == null || seat.kingdom != kingdom || seat == kingdom.capital || !actor.CanFoundCivKingdom()) return false;
        Kingdom rebel = seat.makeOwnKingdom(actor);
        if (rebel == null) return false;
        CultureService.ApplyLocalCulture(rebel, seat);
        rebel.ClearAdministrativeTitle();
        rebel.SetKingdomType(KingdomType.default_country_post);
        rebel.GetRegime()?.SetAllowDiplomacy(true);
        rebel.GetRegime()?.SetAllowArmy(true);
        CorpsSystem.Transfer(corps, kingdom, rebel, seat);
        Kingdom target = empire?.CoreKingdom ?? kingdom;
        War war = DiplomacyHelpers.wars.newWar(rebel, target, WarTypeLibrary.normal);
        war?.SetEmpireWarType(EmpireWarType.地方独立);
        Report(kingdom, empire, string.Format(LM.Get("overshadow_rebel_history"), actor.getName(), ruler.getName(), seat.GetCityName()), actor);
        BiographySystem.Note(actor, string.Format(LM.Get("bio_overshadow_rebel"), ruler.getName(), seat.GetCityName()), ruler);
        return true;
    }

    private static void Report(Kingdom kingdom, Empire empire, string text, Actor actor)
    {
        if (empire != null && !empire.isRekt()) EventRecorder.Record(empire, text, actor, kingdom);
        else EventRecorder.Record(kingdom, text, actor);
    }
}

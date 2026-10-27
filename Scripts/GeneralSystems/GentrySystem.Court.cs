using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 世家在朝(用户 2026-10-27 问答选"朝堂话语权")：
//   · 朝廷与世家(帝国的 gentry_favor，-100~100)：每年向选官之制定的底色靠一成——九品中正 30、察举 15、世卿世禄 5、科举 -20；
//     世家子弟补官 +0.5、修氏族志(升的家数×2 − 降的家数×3)、抑豪强 -25、世家女入宫 +10、篡位者出自世家 +10 / 起于寒门 -15、
//     世家相争 -3。40 以上法统每年 +1，-40 以下 -1，且郡望不在京城的强宗据城自守的机会加倍；
//   · 各家立场：朝廷关系 + 族中在任官员每人 10(至多 30)，30 以上亲附、-30 以下离心，其余中立(世家页显示)；
//   · 改革阻力：推改革的政权里世家的门第合计(每 100 算 1，至多 3)——触动世家利益的(科举、均田，或反对贵族、地主的)
//     反对 +8×门第合计(至多 25)；合世家心意的(九品中正、察举，或贵族、地主支持的)支持 +6×门第合计(至多 20)。
public static partial class GentrySystem
{
    public const float FavorHigh = 40f, FavorLow = -40f;
    private const float FavorDrift = 0.1f, FavorNineRank = 30f, FavorRecommendation = 15f, FavorHereditary = 5f, FavorExam = -20f;
    public const float FavorAppoint = 0.5f, FavorCurb = -25f, FavorEmpress = 10f, FavorGentryUsurper = 10f, FavorUpstart = -15f,
        FavorFeud = -3f;
    private const float StanceOffice = 10f, StanceOfficeCap = 30f, StanceLine = 30f;
    private const float LeanOppose = 8f, LeanOpposeCap = 25f, LeanSupport = 6f, LeanSupportCap = 20f, LeanWeightCap = 3f;

    public static float Favor(Empire empire) => empire?.data?.gentry_favor ?? 0f;

    public static void ShiftFavor(Empire empire, float delta)
    {
        if (empire?.data == null || delta == 0f) return;
        empire.data.gentry_favor = Mathf.Clamp(empire.data.gentry_favor + delta, -100f, 100f);
    }

    // 选官之制定的底色(照帝国核心的制度文化)
    private static float FavorBase(Empire empire)
    {
        string culture = CultureService.GetInstitutionCulture(empire.CoreKingdom);
        if (!CultureService.IsValidCulture(culture)) return 0f;
        if (InstitutionSystem.IsEnacted(culture, ExamNode)) return FavorExam;
        if (InstitutionSystem.IsEnacted(culture, NineRankNode)) return FavorNineRank;
        return InstitutionSystem.IsEnacted(culture, RecommendationNode) ? FavorRecommendation : FavorHereditary;
    }

    // 每年(一轮门第结算开始时)：有世家的帝国，朝廷关系向底色靠、按关系增减法统；世家相争(见 GentrySystem.Events)
    private static void YearlyCourt()
    {
        if (ModClass.EMPIRE_MANAGER == null) return;
        foreach (Empire empire in ModClass.EMPIRE_MANAGER.ToList())
        {
            if (empire?.data == null || empire.isRekt() || empire.IsArchived() || empire.CoreKingdom == null) continue;
            List<SpecificClan> clans = Of(empire);
            if (clans.Count == 0) continue;
            float favor = empire.data.gentry_favor;
            empire.data.gentry_favor = Mathf.Clamp(favor + (FavorBase(empire) - favor) * FavorDrift, -100f, 100f);
            if (empire.data.gentry_favor >= FavorHigh) empire.AddMandate(1);
            else if (empire.data.gentry_favor <= FavorLow) empire.AddMandate(-1);
            TryFeud(empire, clans);
            TryConsort(empire, clans);
        }
    }

    // 立场：1 亲附、0 中立、-1 离心
    public static int Stance(SpecificClan clan, Empire empire)
    {
        float score = Favor(empire) + Mathf.Min(StanceOfficeCap, InOffice(clan) * StanceOffice);
        return score >= StanceLine ? 1 : score <= -StanceLine ? -1 : 0;
    }

    private static int InOffice(SpecificClan clan) =>
        clan.LivingPeople.Count(person => !person.is_virtual && person._actor?.data != null && !person._actor.isRekt() &&
                                          (person._actor.IsOnOffice() || person._actor.isCityLeader()));

    // 改革阻力(见 InstitutionSystem.CalculatePoliticalBalance)：返回要加的支持、反对
    public static (float support, float opposition) ReformLean(InstitutionRealm realm, InstitutionNodeConfig node)
    {
        if (realm == null || node?.politics == null) return (0f, 0f);
        IEnumerable<SpecificClan> clans = realm.IsEmpire
            ? Of(realm.Empire)
            : GentryIds.Select(SpecificClanManager.Get).Where(clan => clan != null && clan.gentry && Seat(clan)?.kingdom == realm.Kingdom);
        float weight = Mathf.Min(LeanWeightCap, clans.Sum(clan => clan.gentry_pedigree) / 100f);
        if (weight <= 0f) return (0f, 0f);
        bool hurts = node.id == ExamNode || node.id == EqualFieldNode ||
                     node.politics.oppose_classes.TryGetValue(SocialClass.Noble, out float noble) && noble >= 0.3f ||
                     node.politics.oppose_classes.TryGetValue(SocialClass.Landlord, out float landlord) && landlord >= 0.3f;
        if (hurts) return (0f, Mathf.Min(LeanOpposeCap, weight * LeanOppose));
        bool pleases = node.id == NineRankNode || node.id == RecommendationNode ||
                       node.politics.support_classes.TryGetValue(SocialClass.Noble, out float nobleSupport) && nobleSupport >= 0.5f ||
                       node.politics.support_classes.TryGetValue(SocialClass.Landlord, out float landlordSupport) && landlordSupport >= 0.5f;
        return pleases ? (Mathf.Min(LeanSupportCap, weight * LeanSupport), 0f) : (0f, 0f);
    }
}

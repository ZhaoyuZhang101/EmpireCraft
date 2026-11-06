using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 清代朝制(用户 2026-11-06 选"清代朝制")：华夏线内阁之后的四个节点，清朝的朝廷走华夏线所以用得上，别的华夏王朝也能推行。
//   · 秘密立储(特性 secret_succession)：皇帝在诸皇子里按才具暗定储君(不论长幼)，不昭告天下；
//     废太子、宫门之变、后宫干储、朝臣议立储君都没了由头(见 PrinceStruggleSystem、HaremSuccessionSystem、TempFac_册立储君)；
//   · 军机处(特性 grand_council)：皇帝亲裁，成年皇帝在位时权臣坐大慢到三分之一(摄政、太后临朝照旧，见 Empire.UpdatePowerfulMinister)；
//   · 摊丁入亩(特性 land_poll_merger)：丁银摊入田赋，百姓不再为躲丁役逃亡，户税不因徭役流失(见 TaxErosionSystem)；
//   · 改土归流(特性 tusi_reform)：天命五十以上、不打仗时，五年一处，把与直辖之地接壤的羁縻州改设流官、改为直辖；
//     土司去职，那里的城十年里渐渐服帖。
public static class LateImperialSystem
{
    public const string SecretSuccessionFeature = "secret_succession", GrandCouncilFeature = "grand_council",
        LandPollFeature = "land_poll_merger", TusiFeature = "tusi_reform";

    private const int DesignateYears = 10, TusiIntervalYears = 5, TusiMandate = 50, MinisterSlowdown = 3;
    private const float TusiStabilityMax = 6f, TusiFadeYears = 10f;

    #region 秘密立储

    // 帝国核心、世袭(不是强者继承)、文化施行了秘密立储
    public static bool SecretSuccession(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt() || !kingdom.IsEmpire()) return false;
        Empire empire = kingdom.GetEmpire();
        Regime regime = kingdom.GetRegime();
        return empire != null && empire.CoreKingdom == kingdom && regime != null &&
               regime.GetLeaderSelectMethod() == LeaderSelectMethod.Succession &&
               regime.GetSuccessionLaw() != SuccessionLawType.强者继承法 &&
               InstitutionSystem.HasFeature(empire, SecretSuccessionFeature);
    }

    // 皇子的才具：四项本事，明君加、昏庸减，声望略算
    public static float Ability(Actor actor) =>
        actor?.data == null ? 0f
            : actor.intelligence + actor.stewardship + actor.diplomacy + actor.warfare +
              (actor.hasTrait(RulerQualitySystem.Wise) ? 8f : 0f) - (RulerQualitySystem.IsBad(actor) ? 8f : 0f) +
              Mathf.Min(actor.renown, 500) * 0.01f;

    // 暗定储君(储君复查时，见 EmpireCraftKingdomBehCheckHeir)：十年一议，储君不在了就当场再定
    public static void Designate(Kingdom kingdom)
    {
        if (!SecretSuccession(kingdom)) return;
        var data = kingdom.GetOrCreate();
        bool current = data.designated_heir_id >= 0 && World.world.units.get(data.designated_heir_id) is Actor heir && heir.isAlive();
        if (current && data.secret_heir_at >= 0d && Date.getYearsSince(data.secret_heir_at) < DesignateYears) return;
        List<Actor> candidates = EmpireCraftKingdomBehCheckHeir.HeirCandidates(kingdom);
        if (candidates.Count == 0) return;
        List<Actor> adults = candidates.Where(actor => actor.isAdult()).ToList();
        Actor pick = (adults.Count > 0 ? adults : candidates).OrderByDescending(Ability).ThenByDescending(actor => actor.getAge()).First();
        data.designated_heir_id = pick.getID();
        data.secret_heir_at = World.world.getCurWorldTime();
    }

    #endregion

    #region 军机处

    // 权臣每月坐大多少(见 Empire.UpdatePowerfulMinister)：军机处下成年皇帝亲裁，慢到三分之一
    public static int MinisterGrowth(Empire empire, int monthlyChange, bool regency)
    {
        if (monthlyChange <= 0 || regency || !InstitutionSystem.HasFeature(empire, GrandCouncilFeature)) return monthlyChange;
        return monthlyChange / MinisterSlowdown;
    }

    #endregion

    #region 摊丁入亩

    public static bool LandPollMerged(Empire empire) => empire?.data != null && InstitutionSystem.HasFeature(empire, LandPollFeature);

    #endregion

    #region 改土归流

    public static void Yearly(Empire empire)
    {
        Kingdom core = empire?.CoreKingdom;
        if (core?.data == null || core.isRekt() || !InstitutionSystem.HasFeature(empire, TusiFeature)) return;
        if (empire.Mandate < TusiMandate || core.hasEnemies()) return;
        var extra = core.GetOrCreate();
        if (extra.tusi_reform_at >= 0d && Date.getYearsSince(extra.tusi_reform_at) < TusiIntervalYears) return;
        Kingdom target = (empire.kingdoms_list ?? new List<Kingdom>())
            .Where(member => member?.data != null && !member.isRekt() && member != core &&
                             member.GetKingdomType() == KingdomType.LvLing_jimizhou && BordersDirectRule(empire, member))
            .OrderBy(member => member.cities?.Count ?? 0).ThenBy(member => member.id)
            .FirstOrDefault();
        if (target == null) return;
        Reform(empire, core, extra, target);
    }

    // 羁縻州与直辖之地(本帝国里不是羁縻的成员或核心)接壤
    private static bool BordersDirectRule(Empire empire, Kingdom member)
    {
        foreach (City city in member.cities ?? new List<City>())
        foreach (City other in city?.neighbours_cities ?? new HashSet<City>())
        {
            Kingdom owner = other?.kingdom;
            if (owner == null || owner == member || owner.GetEmpire() != empire) continue;
            if (!JimiSystem.IsJimiAdministration(owner)) return true;
        }
        return false;
    }

    private static void Reform(Empire empire, Kingdom core, KingdomExtension.KingdomExtraData extra, Kingdom member)
    {
        double now = World.world.getCurWorldTime();
        extra.tusi_reform_at = now;
        var data = member.GetOrCreate();
        data.jimi_mode = (int)JimiMode.Direct;
        data.jimi_homeland_checked = true;
        Regime regime = member.GetRegime();
        if (regime != null)
        {
            regime.SetAllowDiplomacy(false);
            // 设流官：科举出身的官员轮任，不再世袭
            regime.SetLeaderSelectMethod(LeaderSelectMethod.Exam);
            TreasurySystem.EnsureCollectsTax(regime);
        }
        foreach (City city in member.cities ?? new List<City>())
            if (city?.data != null) city.GetOrCreate().tusi_reformed_at = now;
        data.last_kingdom_status_ts = -1d;
        EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(member);
        string text = string.Format(LM.Get("tusi_reform_history"), empire.GetEmpireName(), member.GetKingdomName());
        empire.RecordHistory(directContent: text, kingdomId: core.id);
        TranslateHelper.LogEventMessage(text, core);
        LogService.LogInfo($"[EmpireCraft][改土归流] {empire.GetEmpireName()} 把 {member.GetKingdomName()} 改为直辖");
    }

    // 改土归流以后土司旧地不服：当年扣满，十年里渐渐服帖(见 CityStabilitySystem)
    public static float TusiStabilityLoss(City city)
    {
        if (city?.data == null) return 0f;
        double at = city.GetOrCreate().tusi_reformed_at;
        if (at < 0d) return 0f;
        float years = (float)Date.getYearsSince(at);
        return TusiStabilityMax * Mathf.Max(0f, 1f - years / TusiFadeYears);
    }

    #endregion
}

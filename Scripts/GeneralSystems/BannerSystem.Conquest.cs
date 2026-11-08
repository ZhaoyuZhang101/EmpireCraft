using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 入关三政(用户 2026-11-06 选"入关三政自动化")：八旗多元制的王朝入关(都城移进属地文化之城)以后，按时间表自己颁行，
// 不再只靠剧本事件(剧本事件做过的算数，不重复)。时间表见 BannerRules：
//   · 跑马圈地：入关后第 1、3、5 年各圈一次，圈京畿(都城与近旁属地之城)；二十五年后永停圈地，退还一半旗地；
//   · 剃发易服：入关次年颁行——属地之民大怨，属地之城十五年里渐渐服帖；面上归顺，融合 +5，属地正统 -10，本族正统 +5；
//   · 逃人法：入关第二年颁行，四十年后天下无事放宽(撤法，百姓又能迁徙)。
public static partial class BannerSystem
{
    private const float QueuePeasantGrievance = 10f, QueueLandlordGrievance = 6f, QueueOfficerGrievance = 4f;
    private const int QueueIntegration = 5, QueueCentralLegitimacy = 10, QueueRulingLegitimacy = 5;
    private const float StopPeasantRelief = 10f, StopLandlordRelief = 5f, StopAnnexationRelief = 10f, RelaxPeasantRelief = 5f;

    // 入关：都城是属地文化一系的城，又不是旗下旧地。辽东的城多是汉民，以前清开局坐盛京就算入关了，
    // 头几年在辽东跑马圈地、剃发易服、颁逃人法，自家几座城怨声载道
    private static bool Entered(Empire empire)
    {
        City capital = empire.CoreKingdom?.capital;
        if (capital?.data == null || capital.isRekt() || capital.GetOrCreate().banner_home) return false;
        return CultureService.SameKin(CultureService.GetMainCulture(capital, initialize: false),
            CompositeEmpireService.GetInstitutionalCulture(empire));
    }

    // 入关前的禁旅八旗自备鞍马器械、以旗地为生，出征只领行粮，军饷按 BannerRules.PreEntryUpkeep 折算(见 FormationSystem.UpkeepFactor、
    // CorpsSystem 量入为出)；入关以后照禁旅八旗的饷。以前清在关外也按禁旅八旗的饷(一般军的 1.4 倍)养兵，辽东几城的税养不起，
    // 战时军费系数只有两三成，兵额一路缩编
    public static float PayFactor(Kingdom kingdom)
    {
        Kingdom core = CoreOf(kingdom);
        if (core?.data == null || core.isRekt() || !Organized(core)) return 1f;
        return core.GetOrCreate().banner_entered_at < 0d ? BannerRules.PreEntryUpkeep : 1f;
    }

    private static float YearsSince(double at) => at < 0d ? -1f : (float)Date.getYearsSince(at);

    private static void ConquestYearly(Empire empire, Kingdom core, KingdomExtension.KingdomExtraData extra)
    {
        if (extra.banner_entered_at < 0d)
        {
            if (!Entered(empire)) return;
            extra.banner_entered_at = World.world.getCurWorldTime();
            LogService.LogInfo($"[EmpireCraft][八旗] {empire.GetEmpireName()} 入关，都城 {core.capital?.GetCityName()}");
        }
        float years = YearsSince(extra.banner_entered_at);
        if (BannerRules.EncloseDue(extra.banner_enclosures, years, extra.banner_enclosure_stopped))
        {
            int done = Enclose(core, EnclosureTargets(empire, core));
            if (done > 0)
                Announce(empire, core, string.Format(LM.Get("banner_enclosure_history"), empire.GetEmpireName(), done));
            else extra.banner_enclosures++;    // 京畿已圈到头，这一次算过了
        }
        if (BannerRules.QueueDue(years, extra.banner_queue_at >= 0d)) IssueQueue(empire, core, extra);
        if (BannerRules.LawDue(years, extra.banner_fugitive_law, extra.banner_law_relaxed))
        {
            EnactFugitiveLaw(core);
            Announce(empire, core, string.Format(LM.Get("banner_law_history"), empire.GetEmpireName()));
        }
        if (BannerRules.StopDue(years, extra.banner_enclosure_stopped)) StopEnclosure(empire, core, extra);
        if (BannerRules.RelaxDue(years, extra.banner_fugitive_law, extra.banner_law_relaxed, core.hasEnemies()))
            RelaxFugitiveLaw(empire, core, extra);
    }

    private static void Announce(Empire empire, Kingdom core, string text)
    {
        empire.RecordHistory(directContent: text, kingdomId: core.id);
        TranslateHelper.LogEventMessage(text, core);
    }

    // 京畿：都城与近旁的属地文化之城(只挑核心直辖的)
    private static List<City> EnclosureTargets(Empire empire, Kingdom core)
    {
        City capital = core.capital;
        if (capital?.data == null) return new List<City>();
        string institutional = CompositeEmpireService.GetInstitutionalCulture(empire);
        Vector2 at = capital.city_center;
        return (core.cities ?? new List<City>())
            .Where(city => city?.data != null && !city.isRekt() && !city.GetOrCreate().banner_home &&
                           city.GetOrCreate().banner_land < BannerRules.EnclosureCap &&
                           CultureService.SameKin(CultureService.GetMainCulture(city, initialize: false), institutional))
            .OrderBy(city => (city.city_center - at).sqrMagnitude)
            .Take(BannerRules.EnclosureCities)
            .ToList();
    }

    // 剃发易服
    private static void IssueQueue(Empire empire, Kingdom core, KingdomExtension.KingdomExtraData extra)
    {
        extra.banner_queue_at = World.world.getCurWorldTime();
        ClaimPolicySystem.AddGrievance(empire, SocialClass.Peasant, QueuePeasantGrievance);
        ClaimPolicySystem.AddGrievance(empire, SocialClass.Landlord, QueueLandlordGrievance);
        ClaimPolicySystem.AddGrievance(empire, SocialClass.Officer, QueueOfficerGrievance);
        empire.data.composite_integration = Mathf.Clamp(empire.data.composite_integration + QueueIntegration, 1, 100);
        empire.data.central_plains_legitimacy = Mathf.Clamp(empire.data.central_plains_legitimacy - QueueCentralLegitimacy, 0, 100);
        empire.data.ruling_tradition_legitimacy = Mathf.Clamp(empire.data.ruling_tradition_legitimacy + QueueRulingLegitimacy, 0, 100);
        Announce(empire, core, string.Format(LM.Get("banner_queue_history"), empire.GetEmpireName()));
    }

    // 剃发易服以后属地之城不服(见 CityStabilitySystem)
    public static float QueueStabilityLoss(City city)
    {
        Empire empire = city?.kingdom?.GetEmpire();
        if (empire == null || !Practices(empire)) return 0f;
        double at = empire.CoreKingdom.GetOrCreate().banner_queue_at;
        if (at < 0d) return 0f;
        if (!CultureService.SameKin(CultureService.GetMainCulture(city, initialize: false), CompositeEmpireService.GetInstitutionalCulture(empire)))
            return 0f;
        return BannerRules.QueueStability(YearsSince(at));
    }

    // 永停圈地，退还一半旗地
    private static void StopEnclosure(Empire empire, Kingdom core, KingdomExtension.KingdomExtraData extra)
    {
        extra.banner_enclosure_stopped = true;
        int returned = 0;
        foreach (City city in CitiesOf(empire).ToList())
        {
            var cityExtra = city.GetOrCreate();
            if (cityExtra.banner_land <= 0f) continue;
            cityExtra.banner_land *= BannerRules.StopReturnShare;
            returned++;
        }
        ClaimPolicySystem.AddGrievance(empire, SocialClass.Peasant, -StopPeasantRelief);
        ClaimPolicySystem.AddGrievance(empire, SocialClass.Landlord, -StopLandlordRelief);
        empire.data.maladies ??= new Dictionary<string, float>();
        empire.data.maladies.TryGetValue("annexation", out float annexation);
        empire.data.maladies["annexation"] = Mathf.Clamp(annexation - StopAnnexationRelief, 0f, 100f);
        Announce(empire, core, string.Format(LM.Get("banner_enclosure_stopped_history"), empire.GetEmpireName(), returned));
    }

    // 放宽逃人法：撤法，百姓又能迁徙
    private static void RelaxFugitiveLaw(Empire empire, Kingdom core, KingdomExtension.KingdomExtraData extra)
    {
        extra.banner_fugitive_law = false;
        extra.banner_law_relaxed = true;
        foreach (Kingdom kingdom in empire.kingdoms_list?.ToList() ?? new List<Kingdom>())
        {
            if (kingdom?.data == null || kingdom.isRekt()) continue;
            kingdom.GetOrCreate().fugitive_household_law_enacted = false;
            kingdom.GetRegime()?.laws?.Remove(LawType.逃人法);
        }
        ClaimPolicySystem.AddGrievance(empire, SocialClass.Peasant, -RelaxPeasantRelief);
        Announce(empire, core, string.Format(LM.Get("banner_law_relaxed_history"), empire.GetEmpireName()));
    }
}

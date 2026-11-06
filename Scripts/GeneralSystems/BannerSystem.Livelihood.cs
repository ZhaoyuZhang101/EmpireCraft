using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 八旗生计与湘淮军(用户 2026-11-06 选"八旗生计与湘淮军")：
//   · 旗缺：入主以后第一次发饷时按旗人数定下，此后每年只添千分之三(BannerRules.NextQuota)；旗人生齿日繁，多出来的是闲散——
//     闲散不领饷(旗饷按缺发)、旗人添怨、八旗战力败得更快、典卖旗地(旗地流失，兼并添一笔)；闲散过三成汉军出旗：
//     属地文化一系的旗人出旗为民，此后编旗不再收他们；
//   · 团练成勇营：民变未平、八旗战力不足五十时，属地文化的行省里人最多、还没办过的一处办团练，编成勇营(自筹饷械，不再支援中央)，
//     五年一处，至多四处；天命跌破三十，办过勇营的督抚拥兵自重：得以自行外交，成了藩镇(可以拥兵自立)。
public static partial class BannerSystem
{
    private const float MilitiaPeopleShare = 0.03f, MilitiaPeopleMin = 600f;

    private static float Bannermen(Empire empire) =>
        CitiesOf(empire).Sum(city => CityPopulationSystem.BackgroundOfClass(city, SocialClass.Banner));

    public static float IdleShare(Empire empire)
    {
        if (!Practices(empire)) return 0f;
        return BannerRules.IdleShare(Bannermen(empire), empire.CoreKingdom.GetOrCreate().banner_quota);
    }

    // 每年：旗缺、闲散、典卖旗地、汉军出旗(在发饷之前算，旗饷按缺发)
    private static float LivelihoodYearly(Empire empire, Kingdom core, KingdomExtension.KingdomExtraData extra, float bannermen)
    {
        float years = extra.banner_quota_at < 0d ? 0f : YearsSince(extra.banner_quota_at);
        if (extra.banner_quota <= 0f || years >= 1f)
        {
            extra.banner_quota = BannerRules.NextQuota(extra.banner_quota, bannermen, years);
            extra.banner_quota_at = World.world.getCurWorldTime();
        }
        float idle = BannerRules.IdleShare(bannermen, extra.banner_quota);
        if (idle <= 0f) return 0f;
        ClaimPolicySystem.AddGrievance(empire, SocialClass.Banner, BannerRules.IdleGrievance * idle);
        bool sold = false;
        foreach (City city in CitiesOf(empire))
        {
            var cityExtra = city.GetOrCreate();
            if (cityExtra.banner_land <= 0f) continue;
            cityExtra.banner_land = BannerRules.NextLandAfterSale(cityExtra.banner_land, idle);
            sold = true;
        }
        if (sold)
        {
            empire.data.maladies ??= new Dictionary<string, float>();
            empire.data.maladies.TryGetValue("annexation", out float annexation);
            empire.data.maladies["annexation"] = Mathf.Clamp(annexation + BannerRules.IdleAnnexation * idle, 0f, 100f);
        }
        if (BannerRules.ReleaseDue(idle, extra.banner_hanjun_released)) ReleaseHanjun(empire, core, extra);
        return idle;
    }

    // 汉军出旗：属地文化一系的旗人出旗为民
    private static void ReleaseHanjun(Empire empire, Kingdom core, KingdomExtension.KingdomExtraData extra)
    {
        extra.banner_hanjun_released = true;
        string institutional = CompositeEmpireService.GetInstitutionalCulture(empire);
        float released = 0f;
        foreach (City city in CitiesOf(empire).ToList())
        {
            CityPopulationData data = CityPopulationSystem.Get(city);
            if (data?.groups == null) continue;
            SocialClass commoner = GuoyeSystem.CommonerOf(city);
            foreach (PopGroup group in data.groups.Where(group => group.social_class == SocialClass.Banner && group.Background > 0f &&
                                                                  CultureService.SameKin(group.culture, institutional)).ToList())
            {
                float removed = CityPopulationSystem.RemoveBackground(group, group.Background);
                if (removed <= 0f) continue;
                CityPopulationSystem.AddBackground(city, commoner, group.culture, group.species, group.ideology, removed);
                released += removed;
            }
        }
        // 出旗的不再领饷，旗缺跟着减
        extra.banner_quota = Mathf.Max(0f, extra.banner_quota - released);
        Announce(empire, core, string.Format(LM.Get("banner_hanjun_released_history"), empire.GetEmpireName(), (int)released));
    }

    // 汉军出旗以后，编旗只收属地文化一系以外的人
    private static bool MayEnroll(City city, PopGroup group)
    {
        Empire empire = city?.kingdom?.GetEmpire();
        if (empire == null || !Practices(empire) || !empire.CoreKingdom.GetOrCreate().banner_hanjun_released) return true;
        return !CultureService.SameKin(group.culture, CompositeEmpireService.GetInstitutionalCulture(empire));
    }

    // 编旗：把本城庶民按各组人数摊入旗下(汉军出旗以后跳过属地文化一系)
    private static void Enroll(City city, CityPopulationData data, SocialClass commoner, float amount)
    {
        if (amount <= 0f) return;
        List<PopGroup> pool = data.groups.Where(group => group.social_class == commoner && group.Background > 0f && MayEnroll(city, group)).ToList();
        float have = pool.Sum(group => group.Background);
        if (have <= 0f) return;
        float ratio = Mathf.Clamp01(amount / have);
        foreach (PopGroup group in pool)
        {
            float removed = CityPopulationSystem.RemoveBackground(group, group.Background * ratio);
            if (removed > 0f) CityPopulationSystem.AddBackground(city, SocialClass.Banner, group.culture, group.species, group.ideology, removed);
        }
    }

    #region 团练成勇营

    // 每年：民变未平而八旗打不动，属地行省办团练成勇营；天命跌破三十，办过勇营的督抚拥兵自重
    private static void MilitiaYearly(Empire empire, Kingdom core, KingdomExtension.KingdomExtraData extra)
    {
        List<Kingdom> raised = (empire.kingdoms_list ?? new List<Kingdom>())
            .Where(member => member?.data != null && !member.isRekt() && member.GetOrCreate().banner_yongying).ToList();
        if (empire.Mandate < BannerRules.AutonomyMandate)
            foreach (Kingdom member in raised.Where(member => !member.GetOrCreate().banner_autonomous))
                Autonomy(empire, core, member);
        int rebellions = DynasticCycleSystem.PopularRebellions(empire).count;
        if (!BannerRules.MilitiaDue(rebellions, Vigor(core), YearsSince(extra.banner_militia_at), raised.Count)) return;
        string institutional = CompositeEmpireService.GetInstitutionalCulture(empire);
        Kingdom province = empire.kingdoms_list
            .Where(member => member?.data != null && !member.isRekt() && member != core && member.capital != null &&
                             !member.GetOrCreate().banner_yongying && !LeagueBound(member) &&
                             CultureService.SameKin(CultureService.GetRealmCulture(member), institutional))
            .OrderByDescending(member => member.cities.Sum(CityPopulationSystem.GetBackgroundTotal))
            .FirstOrDefault();
        if (province == null) return;
        extra.banner_militia_at = World.world.getCurWorldTime();
        province.GetOrCreate().banner_yongying = true;
        // 自筹饷械，不再支援中央
        province.GetRegime()?.SetAllowSupportCenterArmy(false);
        float people = Mathf.Max(MilitiaPeopleMin, CityPopulationSystem.GetBackgroundTotal(province.capital) * MilitiaPeopleShare);
        CorpsData corps = CorpsSystem.RaiseStanding(province, province.capital, people, "yongying");
        string leader = province.king?.getName() ?? province.GetKingdomName();
        Announce(empire, core, string.Format(LM.Get("banner_yongying_history"), empire.GetEmpireName(), province.GetKingdomName(), leader));
        LogService.LogInfo($"[EmpireCraft][八旗] {province.GetKingdomName()} 团练成勇营，兵 {(int)(corps?.strength ?? 0f)}");
    }

    // 督抚拥兵自重：得以自行外交，成了藩镇
    private static void Autonomy(Empire empire, Kingdom core, Kingdom member)
    {
        Regime regime = member.GetRegime();
        if (regime == null) return;
        member.GetOrCreate().banner_autonomous = true;
        regime.SetAllowDiplomacy(true);
        TreasurySystem.EnsureCollectsTax(regime);
        EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(member);
        Announce(empire, core, string.Format(LM.Get("banner_autonomy_history"), member.GetKingdomName(), empire.GetEmpireName()));
    }

    #endregion
}

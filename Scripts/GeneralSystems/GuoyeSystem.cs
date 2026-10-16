using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 国野之制(用户 2026-10-16："国人和野人制度""还有国人和野人阶级呢")：施行了"国野之制"(华夏宗法分封以后，见 Huaxia.json)的国家——
//   · 各国的国都为"国"，城中庶民是国人；其余城邑为"野"，庶民是野人(背景人口每年改过去，见 Settle；
//     CityPopulationSystem.TargetClassShares 按此把农民那一份记为国人或野人)；
//   · 国人：国中赋税轻(税率八成)，全数可征为兵；对君主有发言权——君主暴虐、昏聩荒淫时国人怨气大涨(见 SocialCrisisSystem.For)，
//     怨气过七十、组织动员过四成，每年约四分之一的机会国人暴动：君主出奔，国人改立储君或宗室(见 Yearly)；国人不走一般的阶层起义；
//   · 野人：赋税重(税率一倍二)，只有四分之一征为兵(徒役)；不满时照常阶层起义；
//   · 不再施行以后(编户齐民)，国人、野人都编为农民。
// 国人、野人只在背景人口里(无小人模式)；实体单位仍记农民。
public static class GuoyeSystem
{
    public const string Feature = "guoye";
    private const float GuorenTax = 0.8f, YerenTax = 1.2f, YerenLevy = 0.25f;
    private const float RiotGrievance = 70f, RiotMobilization = 40f, RiotChance = 0.25f, RiotRelief = 40f;
    private const int RiotCooldownYears = 30, RiotMandateLoss = 10;

    public static bool Practices(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild) return false;
        string culture = CultureService.GetRealmCulture(kingdom);
        return CultureService.IsValidCulture(culture) && InstitutionSystem.GetFeature(culture, Feature) > 0f;
    }

    // 国都为"国"
    public static bool IsGuo(City city) => city?.kingdom != null && city.kingdom.capital == city;

    // 本城的庶民算哪个阶层
    public static SocialClass CommonerOf(City city) =>
        !Practices(city?.kingdom) ? SocialClass.Peasant : IsGuo(city) ? SocialClass.Guoren : SocialClass.Yeren;

    // 赋税轻重(乘在税率上)
    public static float TaxFactor(City city) =>
        !Practices(city?.kingdom) ? 1f : IsGuo(city) ? GuorenTax : YerenTax;

    // 不征为兵的野人
    public static float Exempt(City city) =>
        Practices(city?.kingdom) && !IsGuo(city)
            ? CityPopulationSystem.BackgroundOfClass(city, SocialClass.Yeren) * (1f - YerenLevy) : 0f;

    // 每年(背景人口自然增减时)：庶民改记为本城该有的国人、野人或农民(迁都、城换了主、废除国野之制都跟着变)
    public static void Settle(City city, CityPopulationData data)
    {
        if (data?.groups == null) return;
        SocialClass commoner = CommonerOf(city);
        foreach (SocialClass from in new[] { SocialClass.Peasant, SocialClass.Guoren, SocialClass.Yeren })
            if (from != commoner) SlaverySystem.Convert(city, data, from, commoner, float.MaxValue);
    }

    // 每年(帝国例行检查，见 EmpireCraftEmpireBehCheckMandate)：国人暴动
    public static void Yearly(Empire empire)
    {
        Kingdom core = empire?.CoreKingdom;
        if (core == null || core.isRekt() || !Practices(core) || empire.data?.institution_state == null) return;
        if (!InstitutionSystem.GetClassGrievances(empire).TryGetValue(SocialClass.Guoren, out float grievance) ||
            grievance < RiotGrievance || SocialCrisisSystem.GetMobilization(empire, SocialClass.Guoren) < RiotMobilization) return;
        double now = World.world.getCurWorldTime();
        if (empire.data.last_guoren_riot_at >= 0d && now >= empire.data.last_guoren_riot_at &&
            Date.getYearsSince(empire.data.last_guoren_riot_at) < RiotCooldownYears) return;
        if (Random.value >= RiotChance) return;
        Riot(empire, core, now);
    }

    private static void Riot(Empire empire, Kingdom core, double now)
    {
        Actor former = empire.Emperor;
        if (former?.data == null || !former.isAlive()) return;
        empire.data.last_guoren_riot_at = now;
        empire.AddMandate(-RiotMandateLoss);
        ClaimPolicySystem.AddGrievance(empire, SocialClass.Guoren, -RiotRelief);
        empire.data.institution_state.class_mobilization ??= new Dictionary<SocialClass, float>();
        empire.data.institution_state.class_mobilization[SocialClass.Guoren] = 0f;
        Actor successor = Successor(empire, former);
        City refuge = Refuge(empire, core);
        string formerName = former.getName();
        string content;
        if (successor != null && refuge != null)
        {
            core.removeKing();
            OfficeObject office = core.GetOffice();
            if (office != null)
            {
                office.meta_object = core;
                office.SetActor(successor);
            }
            else core.setKing(successor);
            former.joinCity(refuge);
            former.setKingdom(refuge.kingdom);
            content = string.Format(LM.Get("guoren_riot_exile_history"), formerName, refuge.GetCityName(), successor.getName());
            successor.RecordPersonalHistory(content, relatedActorId: former.id);
        }
        else content = string.Format(LM.Get("guoren_riot_history"), formerName);
        former.RecordPersonalHistory(content);
        LogService.LogInfo("[EmpireCraft][国人暴动] " + content);
        empire.RecordHistory(directContent: content, actorId: former.id, kingdomId: core.id);
        TranslateHelper.LogEventMessage(content, core);
    }

    // 国人改立：储君，否则成年的宗亲(名望最高)
    private static Actor Successor(Empire empire, Actor former)
    {
        Kingdom core = empire.CoreKingdom;
        Actor heir = core.HasHeir() ? core.GetHeir() : null;
        if (heir?.data != null && heir != former && heir.isAlive() && !heir.isRekt()) return heir;
        return empire.EmpireSpecificClan?.AllAliveMembers?
            .Where(actor => actor?.data != null && actor != former && actor.isAlive() && !actor.isRekt() && actor.isAdult() &&
                            !actor.isKing() && actor.kingdom?.GetEmpire() == empire)
            .OrderByDescending(actor => actor.renown).FirstOrDefault();
    }

    // 出奔之地：离国都最近的别国城邑(本帝国诸侯国的都城优先)，没有就本国别的城
    private static City Refuge(Empire empire, Kingdom core)
    {
        City capital = core.capital;
        if (capital == null) return null;
        IEnumerable<City> abroad = empire.kingdoms_list.Where(kingdom => kingdom != null && !kingdom.isRekt() && kingdom != core)
            .Select(kingdom => kingdom.capital).Where(city => city != null && !city.isRekt());
        IEnumerable<City> home = core.cities.Where(city => city != null && !city.isRekt() && city != capital);
        return abroad.OrderBy(city => (city.city_center - capital.city_center).sqrMagnitude).FirstOrDefault() ??
               home.OrderBy(city => (city.city_center - capital.city_center).sqrMagnitude).FirstOrDefault();
    }
}

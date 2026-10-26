using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
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

// 教廷的大事件(每年随教权与帝国结算)：
//   · 教廷之囚：圣地落在世俗强国(帝国中央或成员)手里。挟持者的君主由教宗加冕(教廷在本帝国，见 Coronation)；
//     与挟持者交战的帝国每年按 教权/100 的概率遭绝罚(教宗受挟)；被挟持满 20 年出现对立教宗(大分裂)，大分裂期间教权目标再 -20；
//     圣地脱离挟持即告结束；
//   · 宗教改革脱离罗马：文化施行了宗教改革的西方帝国(教廷所在的、皇帝仍奉旧教的选帝制帝国除外)以皇帝为首另立国教会(新宗教，圣地在帝都)，
//     帝国各国、各城、城里的人改奉国教会；没收教产(两年经常收入)；母宗教教权 -8。国教会记下母宗教，
//     此后 30 年内母宗教的教廷可号召讨伐异端(教权 40 以上)；讨伐异端的圣战打完(不论胜负)即"教随国定"，母宗教 50 年内不再讨伐异端；
//   · 骑士团国世俗化：骑士团国所在文化施行了宗教改革或世俗化，大团长改为世袭公国，骑士团解散；
//   · 圣座帝国：教廷(教皇国)自己做了帝国中央——国号改为"某某圣座"，正统按教权计((教权-50)/2)，不需要加冕。
public static partial class PapacySystem
{
    private const int SchismYears = 20;
    private const float SchismPenalty = 20f;
    private const int HereticWindowYears = 30;
    private const int ReligiousPeaceYears = 50;
    private const float ChurchLandsYears = 2f;

    public static bool IsHolySee(Empire empire)
    {
        Religion religion = ReligionOf(empire);
        Kingdom papacy = Papacy(religion);
        return papacy != null && empire?.CoreKingdom == papacy && papacy.GetKingdomType() == KingdomType.Feudalism_papal_state;
    }

    // 每年随 UpdateReligion：教廷之囚的起止、大分裂(对立教宗)。
    // 对立教宗：教廷之囚满 SchismYears 年，或教宗正统低于 PopeLegitimacy.AntipopeBelow 时每年 AntipopeChance；
    // 教廷脱困、教宗正统(不计对立教宗那一项)回到 PopeLegitimacy.HealedAt 以上才弥合(见 PopeLegitimacy)
    private static void UpdateCaptivity(Religion religion)
    {
        var data = religion.GetOrCreate();
        double now = World.world.getCurWorldTime();
        bool captive = IsCaptive(religion);
        if (!captive && data.CaptiveSince >= 0d) data.CaptiveSince = -1d;
        else if (captive && data.CaptiveSince < 0d)
        {
            data.CaptiveSince = now;
            Kingdom captor = Realm(Papacy(religion));
            string text = string.Format(LM.Get("papal_captivity_log"), religion.name, captor?.GetKingdomName() ?? "");
            TranslateHelper.LogEventMessage(text, captor);
            ReligionChronicle.Record(religion, ReligionChronicle.KindPapacy, text);
            return;
        }
        bool papal = PopeLegitimacy.Applies(religion);
        if (data.Schism)
        {
            if (captive || papal && PopeLegitimacy.Get(religion, countSchism: false) < PopeLegitimacy.HealedAt) return;
            data.Schism = false;
            string ended = string.Format(LM.Get("papal_schism_ended_log"), religion.name);
            TranslateHelper.LogEventMessage(ended, Papacy(religion));
            ReligionChronicle.Record(religion, ReligionChronicle.KindPapacy, ended);
            return;
        }
        bool longCaptive = captive && now >= data.CaptiveSince && Date.getYearsSince(data.CaptiveSince) >= SchismYears;
        bool weakPope = papal && PopeLegitimacy.Get(religion) < PopeLegitimacy.AntipopeBelow &&
                        UnityEngine.Random.value < PopeLegitimacy.AntipopeChance;
        if (!longCaptive && !weakPope) return;
        data.Schism = true;
        string schism = string.Format(LM.Get("papal_schism_log"), religion.name);
        TranslateHelper.LogEventMessage(schism, Papacy(religion));
        ReligionChronicle.Record(religion, ReligionChronicle.KindPapacy, schism);
    }

    // 教宗受挟：与挟持者交战的帝国遭绝罚
    private static bool CaptorBan(Empire empire, PapalRelationState state, Religion religion)
    {
        if (!IsCaptive(religion)) return false;
        Kingdom captor = Realm(Papacy(religion));
        if (captor == null || captor == empire.CoreKingdom || !empire.CoreKingdom.isInWarWith(captor) ||
            UnityEngine.Random.value >= Authority(religion) / 100f) return false;
        Excommunicate(empire, state, "papal_ban_reason_captor");
        return true;
    }

    // 宗教改革脱离罗马：另立国教会
    private static void TryBreakWithRome(Empire empire)
    {
        if (InstitutionSystem.IsEnacted(empire, NodeReformation)) BreakWithRome(empire);
    }

    // 另立国教会：本文化施行宗教改革时(TryBreakWithRome)；教权低落、圣战屡败时也会提早(见 ReligionSchismSystem)
    public static bool BreakWithRome(Empire empire)
    {
        // 选帝制帝国的皇帝不随宗教改革脱离罗马，由诸侯另立新教会(见 HolyRomanSystem.SeedReformation)
        if (!IsWestern(empire) || HolyRomanSystem.KeepsOldFaith(empire)) return false;
        PapalRelationState state = State(empire);
        Religion old = ReligionOf(empire);
        if (state == null || state.national_church_id >= 0 || old == null) return false;
        Kingdom papacy = Papacy(old);
        if (papacy == null || Realm(papacy) == empire.CoreKingdom || papacy.GetEmpire() == empire) return false;
        Actor head = empire.Emperor;
        City capital = empire.CoreKingdom.capital;
        if (head == null || capital == null) return false;
        head.joinCity(capital);
        Religion church = World.world.religions.newReligion(head, false);
        if (church?.data == null) return false;
        church.data.name = string.Format(LM.Get("national_church_name"), empire.GetEmpireName());
        church.SetCity(capital);
        var churchData = church.GetOrCreate();
        churchData.ParentReligionId = old.data.id;
        churchData.SplitAt = World.world.getCurWorldTime();
        state.national_church_id = church.data.id;
        foreach (Kingdom kingdom in empire.kingdoms_list.ToList())
        {
            if (kingdom?.data == null || kingdom.isRekt()) continue;
            kingdom.setReligion(church);
            foreach (City city in kingdom.cities.ToList())
            {
                if (city?.data == null || city.isRekt()) continue;
                city.setReligion(church);
                foreach (Actor actor in city.units.ToList())
                    if (actor?.data != null && !actor.isRekt() && actor.religion == old) actor.setReligion(church);
            }
        }
        empire.Religion = church;
        long lands = (long)(StateDebtSystem.AnnualStableIncome(empire.CoreKingdom) * ChurchLandsYears);
        if (lands > 0) empire.CoreKingdom.AddMoney((int)Math.Min(int.MaxValue, lands), TreasuryCategory.RecoveredFunds);
        AddAuthority(old, -8f);
        string text = string.Format(LM.Get("papal_reformation_history"), empire.GetEmpireFullName(), old.name, church.name,
            MoneyDisplay.Format(lands));
        Record(empire, text);
        ReligionChronicle.Clear(church);
        ReligionChronicle.Record(old, church, ReligionChronicle.KindSchism, text);
        return true;
    }

    // 讨伐异端的目标：从本宗教分出去、30 年内、不在"教随国定"期间
    private static Kingdom HereticTarget(Religion religion)
    {
        var data = religion.GetOrCreate();
        double now = World.world.getCurWorldTime();
        if (now < data.ReligiousPeaceUntil) return null;
        foreach (Religion other in World.world.religions)
        {
            if (other?.data == null || other == religion) continue;
            var split = other.GetOrCreate();
            if (split.ParentReligionId != religion.data.id || split.SplitAt < 0d ||
                Date.getYearsSince(split.SplitAt) > HereticWindowYears) continue;
            // 只讨伐整个政权改宗的(国教会)；帝国诸侯另立的新教会归帝国内的宗教战争(见 HolyRomanSystem.Leagues)，
            // 不然教廷会去讨伐仍奉旧教的皇帝
            Kingdom head = Papacy(other);
            if (head != null && Realm(head).religion == other) return Realm(head);
        }
        return null;
    }

    // 讨伐异端的圣战打完、帝国诸侯订立和约：教随国定
    public static void ReligiousPeace(Religion religion)
    {
        religion.GetOrCreate().ReligiousPeaceUntil = World.world.getCurWorldTime() + ReligiousPeaceYears * SecondsPerYear;
        TranslateHelper.LogEventMessage(string.Format(LM.Get("papal_religious_peace_log"), religion.name), Papacy(religion));
    }

    // 骑士团国世俗化
    private static bool SecularizeOrderState(KnightOrderData order)
    {
        Kingdom state = order.state_kingdom_id >= 0 ? World.world.kingdoms.get(order.state_kingdom_id) : null;
        if (state?.data == null || state.isRekt()) return false;
        string culture = CultureService.GetInstitutionCulture(state);
        if (!InstitutionSystem.IsEnacted(culture, NodeReformation) && !InstitutionSystem.IsEnacted(culture, NodeSecularization))
            return false;
        string name = string.Format(LM.Get("order_duchy_name"), state.capital?.GetCityName() ?? state.GetKingdomName());
        state.data.name = name;
        order.dissolved = true;
        TranslateHelper.LogEventMessage(string.Format(LM.Get("order_secularized_log"), order.name, name), state);
        return true;
    }

    // 圣座帝国：国号改"某某圣座"
    private static void NameHolySee(Empire empire, PapalRelationState state)
    {
        if (state.holy_see_named || !IsHolySee(empire)) return;
        City holy = HolyCity(ReligionOf(empire));
        if (holy == null) return;
        state.holy_see_named = true;
        empire.SetEmpireName(string.Format(LM.Get("holy_see_empire_name"), holy.GetCityName()));
        Record(empire, string.Format(LM.Get("holy_see_history"), empire.GetEmpireFullName()));
    }
}

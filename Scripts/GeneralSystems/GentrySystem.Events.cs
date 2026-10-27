using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 世家事件与名人(用户 2026-10-27 问答选"世家事件与名人")：
//   · 家学渊源：世家子弟立功成名快一半(见 FamousTalentSystem.Credit)；
//   · 世家女入宫：华夏帝国的皇帝(或储君)成年、没有在世的正室，每年按 ConsortChance 从本朝门第最高的几家里挑一位
//     待嫁的女儿(15~30 岁)入宫为后(储君为妃)，那家门第 +3、朝廷与世家关系 +10，史书记、上屏幕；
//   · 灾年赈济：郡望之城闹饥荒，世家每年按 ReliefChance 开仓赈济乡里——门第 +2、国家粮仓 +门第×2、
//     这座城两年里自然稳定度 +ReliefStability(见 StabilityBonus)，三年至多一回；
//   · 世家相争：本朝两家甲乙等世家族中都有人在朝，每年按 FeudChance 争权分党——两家门第各 -3、法统 -1、
//     朝廷与世家关系 -3，每朝 FeudYears 年至多一回。
public static partial class GentrySystem
{
    private const float FameBonus = 1.5f, ConsortChance = 0.5f, HeirConsortChance = 0.3f, ReliefChance = 0.4f,
        ReliefPedigree = 2f, ReliefGranaryPerPedigree = 2f, ConsortPedigree = 3f, FeudChance = 0.06f, FeudPedigree = 3f;
    private const int ConsortClans = 5, BrideMinAge = 15, BrideMaxAge = 30, ReliefYears = 2, ReliefCooldownYears = 3, FeudYears = 15;
    public const float ReliefStability = 6f;

    public static float FameFactor(Actor actor) => IsGentry(actor) ? FameBonus : 1f;

    // 世家女入宫(每年，见 YearlyCourt)：皇帝没有正室为后，储君没有正室为妃
    private static void TryConsort(Empire empire, List<SpecificClan> clans)
    {
        Kingdom core = empire.CoreKingdom;
        if (core?.capital == null || !Huaxia(core.capital)) return;
        Actor emperor = empire.Emperor;
        Actor heir = core.HasHeir() ? core.GetHeir() : null;
        if (Unwed(emperor) && Random.value < ConsortChance) Wed(empire, clans, emperor, "gentry_empress_history");
        else if (heir != emperor && Unwed(heir) && Random.value < HeirConsortChance) Wed(empire, clans, heir, "gentry_heir_consort_history");
    }

    private static bool Unwed(Actor actor)
    {
        if (actor?.data == null || actor.isRekt() || !actor.isAlive() || !actor.isAdult() || !actor.isSexMale()) return false;
        PersonalClanIdentity identity = actor.GetPersonalIdentity();
        return identity != null && identity.IsFreeToMarry(-2L);
    }

    private static void Wed(Empire empire, List<SpecificClan> clans, Actor groom, string key)
    {
        foreach (SpecificClan clan in clans.Take(ConsortClans).OrderByDescending(clan => clan.gentry_pedigree * Random.Range(0.6f, 1f)))
        {
            PersonalClanIdentity candidate = clan.LivingPeople.FirstOrDefault(person => person.sex == ActorSex.Female &&
                person.age >= BrideMinAge && person.age <= BrideMaxAge && !person.is_concubine && person.IsFreeToMarry(-2L));
            if (candidate == null) continue;
            Actor bride = candidate.is_virtual ? candidate.Realize() : candidate._actor;
            if (bride?.data == null || bride.isRekt() || !bride.isAlive() || KinshipRules.AreCloseKin(bride, groom)) continue;
            PersonalClanIdentity head = groom.GetPersonalIdentity();
            if (head == null) return;
            // 鳏居的先清掉已故正室的记录(同 RoyalMarriageSystem)
            if (head.hasLover() && head.IsFreeToMarry(-2L)) head.lover = (-1L, -1L);
            head.setLover(bride);
            if (!head.hasLover()) continue;
            groom.setLover(bride);
            bride.setLover(groom);
            Kingdom core = empire.CoreKingdom;
            if (bride.isCityLeader() && bride.city != null) bride.city.removeLeader();
            if (bride.kingdom != core) bride.joinKingdom(core);
            if (core.capital != null && bride.city != core.capital) bride.joinCity(core.capital);
            clan.gentry_pedigree = Mathf.Min(100f, clan.gentry_pedigree + ConsortPedigree);
            ShiftFavor(empire, FavorEmpress);
            Record(Seat(clan), string.Format(LM.Get(key), JunWang(clan), bride.getName(), groom.getName()), force: true);
            NoteClan(clan, "clan_history_gentry_consort", string.Format(LM.Get(key), JunWang(clan), bride.getName(), groom.getName()));
            return;
        }
    }

    // 灾年赈济(每年，Assess 里)
    private static void TryRelief(SpecificClan clan, City seat)
    {
        if (seat?.kingdom == null || !FamineUnrestSystem.InFamine(seat) || Random.value >= ReliefChance) return;
        double now = World.world.getCurWorldTime();
        if (clan.gentry_relief_at >= 0d && now >= clan.gentry_relief_at && Date.getYearsSince(clan.gentry_relief_at) < ReliefCooldownYears)
            return;
        clan.gentry_relief_at = now;
        clan.gentry_pedigree = Mathf.Min(100f, clan.gentry_pedigree + ReliefPedigree);
        GranarySystem.Donate(seat.kingdom, clan.gentry_pedigree * ReliefGranaryPerPedigree);
        string text = string.Format(LM.Get("gentry_relief_history"), JunWang(clan), seat.GetCityName());
        Record(seat, text, force: true);
        NoteClan(clan, "clan_history_gentry_relief", text);
    }

    // 赈济过的城两年里自然稳定度加成
    private static bool Relieved(City city)
    {
        if (city?.data == null) return false;
        foreach (long id in GentryIds)
        {
            SpecificClan clan = SpecificClanManager.Get(id);
            if (clan == null || clan.gentry_seat_city_id != city.data.id || clan.gentry_relief_at < 0d) continue;
            double now = World.world.getCurWorldTime();
            if (now >= clan.gentry_relief_at && Date.getYearsSince(clan.gentry_relief_at) < ReliefYears) return true;
        }
        return false;
    }

    // 世家相争(每年，见 YearlyCourt)
    private static void TryFeud(Empire empire, List<SpecificClan> clans)
    {
        double now = World.world.getCurWorldTime();
        if (empire.data.gentry_feud_at >= 0d && now >= empire.data.gentry_feud_at && Date.getYearsSince(empire.data.gentry_feud_at) < FeudYears)
            return;
        List<SpecificClan> strong = clans.Where(clan => Grade(clan.gentry_pedigree) <= 2 && InOffice(clan) > 0).Take(4).ToList();
        if (strong.Count < 2 || Random.value >= FeudChance) return;
        SpecificClan a = strong[0], b = strong[1 + Random.Range(0, strong.Count - 1)];
        empire.data.gentry_feud_at = now;
        a.gentry_pedigree = Mathf.Max(0f, a.gentry_pedigree - FeudPedigree);
        b.gentry_pedigree = Mathf.Max(0f, b.gentry_pedigree - FeudPedigree);
        empire.AddMandate(-1);
        ShiftFavor(empire, FavorFeud);
        string text = string.Format(LM.Get("gentry_feud_history"), JunWang(a), JunWang(b));
        Record(Seat(a), text, force: true);
        NoteClan(a, "clan_history_gentry_feud", text);
        NoteClan(b, "clan_history_gentry_feud", text);
    }
}

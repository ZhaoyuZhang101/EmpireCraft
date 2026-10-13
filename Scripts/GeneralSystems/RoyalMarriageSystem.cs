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
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 西方王室联姻与继承(西方封建制的君主国)：
//   ① 联姻：每年各国可能为储君、国王(没有正室时)、成年未婚的王子公主向他国王室议婚——对方的国王、储君或王子公主，
//      异性、成年、未婚、不是近亲；看对方国力、是否接壤、信仰与文化是否相同、两国好感，配国王、储君的更体面。
//      一方是在位君主或储君的，另一方嫁(入赘)过去；两位在位君主联姻则各守其国，子女日后兼承两国；都不是的，
//      女方嫁到男方的国家。两国结为姻亲：二十年内互不开战，一方遭外敌攻打另一方可能出兵相助。
//   ② 宣称：在位君主与他国君主是隔四代以内的血亲(含外嫁女子的后代)，就对那国王位有继承宣称，可以为此开战
//      (见 FeudalVassalService.CanDeclareExternalWar)。
//   ③ 继承(见 EmpireCraftKingdomBehCheckHeir)：封建王国本宗绝嗣，按血缘远近找外嫁宗亲的后代入继；
//      入继者本是别国君主的，两国共主(见 PersonalUnionService)。
public static class RoyalMarriageSystem
{
    private const int PactYears = 20, ClaimDistance = 4, MinAge = 16, MaxAge = 40, KingMaxAge = 60;
    private const float ProposalChance = 0.35f, AidChance = 0.5f;

    public static bool Applies(Kingdom kingdom) =>
        kingdom?.data != null && !kingdom.isRekt() && kingdom.hasKing() && kingdom.GetRegime()?.type == RegimeType.Feudalism;

    #region 议婚(RoyalHouseSystem 每月调用；各国按编号分到十二个月里，每国每年一次机会，不在同一帧里全算)

    private static int _month;

    public static void Monthly()
    {
        if (World.world?.kingdoms == null) return;
        int slot = _month++ % 12;
        using var timing = FrameProfiler.Measure("联姻·议婚");
        foreach (Kingdom kingdom in World.world.kingdoms.list.ToList())
        {
            if (kingdom?.data == null || (int)(kingdom.id % 12) != slot) continue;
            if (!Applies(kingdom) || UnityEngine.Random.value >= ProposalChance) continue;
            try
            {
                TryArrange(kingdom);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][联姻] {kingdom.data.name} 议婚失败: {exception.Message}");
            }
        }
    }

    // 本国的王室：储君、国王、国王的子女(有实体的)
    private static IEnumerable<Actor> Royals(Kingdom kingdom)
    {
        Actor king = kingdom.king;
        if (kingdom.HasHeir()) yield return kingdom.GetHeir();
        yield return king;
        foreach (Actor child in king?.getChildren(false) ?? Enumerable.Empty<Actor>()) yield return child;
    }

    private static bool Marriageable(Actor actor, Kingdom kingdom)
    {
        if (actor?.data == null || actor.isRekt() || !actor.isAlive() || !actor.isAdult() || actor.IsEmperor()) return false;
        int age = actor.getAge();
        if (age < MinAge || age > (actor == kingdom.king ? KingMaxAge : MaxAge)) return false;
        PersonalClanIdentity identity = actor.GetPersonalIdentity();
        return identity != null && !identity.is_concubine && identity.IsFreeToMarry(-2L);
    }

    private static bool Ruling(Actor actor, Kingdom kingdom) =>
        actor == kingdom.king || kingdom.HasHeir() && kingdom.GetHeir() == actor;

    private static void TryArrange(Kingdom kingdom)
    {
        Actor royal = Royals(kingdom).Distinct().FirstOrDefault(actor => Marriageable(actor, kingdom));
        if (royal == null) return;
        (Actor partner, Kingdom other) best = (null, null);
        float bestScore = float.MinValue;
        foreach (Kingdom other in World.world.kingdoms.list)
        {
            if (other == kingdom || other?.data == null || other.isRekt() || !other.hasKing() ||
                !RegimeManager.IsMonarchy(other.GetRegime()?.type) || kingdom.isInWarWith(other)) continue;
            float opinion = World.world.diplomacy?.getOpinion(kingdom, other)?.total ?? 0;
            if (opinion < -30f) continue;
            float realm = (float)Math.Log10(1d + other.GetNationalPower());
            foreach (Actor candidate in Royals(other).Distinct())
            {
                if (!Marriageable(candidate, other) || candidate.data.sex == royal.data.sex ||
                    KinshipRules.AreCloseKin(royal, candidate)) continue;
                float score = realm + opinion / 50f + UnityEngine.Random.Range(0f, 1.5f);
                if (candidate == other.king) score += 3f;
                else if (other.HasHeir() && other.GetHeir() == candidate) score += 2f;
                if (StraitReach.Near(kingdom, other)) score += 1.5f;
                if (kingdom.religion != null && kingdom.religion == other.religion) score += 1f;
                if (CultureService.GetRealmCulture(kingdom) == CultureService.GetRealmCulture(other)) score += 0.5f;
                if (score <= bestScore) continue;
                bestScore = score;
                best = (candidate, other);
            }
        }
        if (best.partner == null) return;
        Marry(kingdom, royal, best.other, best.partner);
    }

    private static void Marry(Kingdom home, Actor royal, Kingdom other, Actor partner)
    {
        PersonalClanIdentity a = royal.GetPersonalIdentity(), b = partner.GetPersonalIdentity();
        if (a == null || b == null) return;
        // 正室：由本宗承嗣的一方(男系宗族的男方、女系宗族的女方)娶(招)；鳏寡的先清掉已故配偶的记录
        bool aHeads = a.IsHeirPriority();
        PersonalClanIdentity head = aHeads ? a : b;
        Actor spouse = aHeads ? partner : royal;
        if (head.hasLover() && head.IsFreeToMarry(-2L)) head.lover = (-1L, -1L);
        head.setLover(spouse);
        if (!head.hasLover()) return;
        royal.setLover(partner);
        partner.setLover(royal);
        // 谁去谁的国家
        bool royalRules = Ruling(royal, home), partnerRules = Ruling(partner, other);
        Actor mover = null;
        Kingdom destination = null;
        if (royalRules && !partnerRules) (mover, destination) = (partner, home);
        else if (partnerRules && !royalRules) (mover, destination) = (royal, other);
        else if (!royalRules && !partnerRules)
            (mover, destination) = royal.isSexFemale() ? (royal, other) : (partner, home);
        if (mover != null && destination?.capital != null) Relocate(mover, destination);
        double now = World.world.getCurWorldTime();
        home.GetOrCreate().marriage_pacts[other.id] = now;
        other.GetOrCreate().marriage_pacts[home.id] = now;
        string text = string.Format(LM.Get("royal_marriage_history"), home.GetKingdomName(), royal.getName(),
            other.GetKingdomName(), partner.getName());
        TranslateHelper.LogEventMessage(text, home);
        Empire otherEmpire = other.GetEmpire();
        if (otherEmpire != null && otherEmpire != home.GetEmpire()) otherEmpire.RecordHistory(directContent: text);
    }

    private static void Relocate(Actor mover, Kingdom destination)
    {
        if (mover.IsOnOffice())
        {
            mover.GetOffice()?.RemoveActor();
            mover.EndOffice();
        }
        if (mover.isCityLeader() && mover.city != null) mover.city.removeLeader();
        mover.removeFromArmy();
        mover.joinKingdom(destination);
        mover.joinCity(destination.capital);
    }

    #endregion

    #region 姻亲之国

    // 二十年内的姻亲：互不开战(EmpireCraftPlotsAddition、WarTargetPatch、FeudalVassalService)
    public static bool Allied(Kingdom a, Kingdom b)
    {
        if (a?.data == null || b?.data == null || a == b) return false;
        return Pact(a, b) || Pact(Core(a), Core(b));
    }

    private static Kingdom Core(Kingdom kingdom) => kingdom.GetEmpire()?.CoreKingdom ?? kingdom;

    private static bool Pact(Kingdom a, Kingdom b)
    {
        if (a?.data == null || b?.data == null || a == b) return false;
        var pacts = a.GetOrCreate().marriage_pacts;
        if (pacts == null || !pacts.TryGetValue(b.id, out double since)) return false;
        if (Date.getYearsSince(since) < PactYears && !b.isRekt()) return true;
        pacts.Remove(b.id);
        return false;
    }

    // 姻亲之国遭外敌攻打：可能出兵相助(WarPatch.new_war)
    public static void OnWarStarted(War war)
    {
        Kingdom defender = war?.main_defender, attacker = war?.main_attacker;
        var pacts = defender?.data == null ? null : defender.GetOrCreate().marriage_pacts;
        if (pacts == null || pacts.Count == 0 || attacker == null) return;
        foreach (long id in pacts.Keys.ToList())
        {
            Kingdom ally = World.world.kingdoms.get(id);
            if (ally?.data == null || ally.isRekt() || !Pact(defender, ally) || ally == attacker || war.hasKingdom(ally) ||
                Allied(ally, attacker) || ally.isInWarWith(attacker) || UnityEngine.Random.value >= AidChance) continue;
            war.joinDefenders(ally);
            TranslateHelper.LogEventMessage(string.Format(LM.Get("royal_marriage_aid_history"), ally.GetKingdomName(),
                defender.GetKingdomName()), ally);
        }
    }

    #endregion

    #region 宣称

    // 两国君主是隔 ClaimDistance 代以内的血亲(沿父母、子女走，含外嫁女子的后代)：claimant 对 target 的王位有宣称。
    // 挑开战目标时各国每隔几秒就要对每个邻国查一次：同一对君主一分钟内直接用上次的结果
    private static readonly Dictionary<(long, long), (bool claim, float at)> ClaimCache = new();
    private const float ClaimCacheSeconds = 60f;

    public static bool HasClaim(Kingdom claimant, Kingdom target)
    {
        Actor a = claimant?.king, b = target?.king;
        if (a?.data == null || b?.data == null || a == b) return false;
        var key = (a.id, b.id);
        float now = Time.unscaledTime;
        if (ClaimCache.TryGetValue(key, out var cached) && now - cached.at < ClaimCacheSeconds && now >= cached.at)
            return cached.claim;
        if (ClaimCache.Count > 4000) ClaimCache.Clear();
        using var timing = FrameProfiler.Measure("联姻·继承宣称");
        int distance = BloodDistance(a.GetPersonalIdentity(), b.GetPersonalIdentity(), ClaimDistance);
        bool claim = distance > 0 && distance <= ClaimDistance;
        ClaimCache[key] = (claim, now);
        return claim;
    }

    // 血缘远近(沿父母、子女走的步数，找不到为 -1)
    public static int BloodDistance(PersonalClanIdentity from, PersonalClanIdentity to, int limit)
    {
        if (from == null || to == null) return -1;
        if (from.id == to.id) return 0;
        var seen = new HashSet<long> { from.id };
        var frontier = new List<PersonalClanIdentity> { from };
        for (int depth = 1; depth <= limit && frontier.Count > 0; depth++)
        {
            var next = new List<PersonalClanIdentity>();
            foreach (PersonalClanIdentity person in frontier)
                foreach (long id in Relatives(person))
                {
                    if (!seen.Add(id)) continue;
                    if (id == to.id) return depth;
                    PersonalClanIdentity relative = SpecificClanManager.getPerson(id);
                    if (relative != null) next.Add(relative);
                }
            if (seen.Count > 2000) break;
            frontier = next;
        }
        return -1;
    }

    // 外家入继：从先君出发沿父母、子女找血亲，近的在前(同样近的成年人在前、年长的在前)
    public static IEnumerable<(PersonalClanIdentity person, int distance)> Cognates(PersonalClanIdentity origin, int limit)
    {
        if (origin == null) yield break;
        var seen = new HashSet<long> { origin.id };
        var frontier = new List<PersonalClanIdentity> { origin };
        for (int depth = 1; depth <= limit && frontier.Count > 0; depth++)
        {
            var next = new List<PersonalClanIdentity>();
            foreach (PersonalClanIdentity person in frontier)
                foreach (long id in Relatives(person))
                {
                    if (!seen.Add(id)) continue;
                    PersonalClanIdentity relative = SpecificClanManager.getPerson(id);
                    if (relative != null) next.Add(relative);
                }
            foreach (PersonalClanIdentity relative in next.Where(person => person.is_alive && !person.is_concubine)
                         .OrderByDescending(person => person.age >= 18).ThenByDescending(person => person.age))
                yield return (relative, depth);
            if (seen.Count > 2000) yield break;
            frontier = next;
        }
    }

    private static IEnumerable<long> Relatives(PersonalClanIdentity person)
    {
        if (person.father > 0) yield return person.father;
        if (person.mother > 0) yield return person.mother;
        foreach (long child in person.children ?? new List<long>()) yield return child;
    }

    #endregion
}

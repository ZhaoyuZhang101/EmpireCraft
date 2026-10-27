using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 御侮(用户 2026-10-27："本天下的本土帝国面对异族入侵更有号召力，更有战斗欲望，除非对方整体国力大于自己")：
//   · 本土帝国：帝国中央的文化与它都城的主流文化同族(CultureService.SameKin：同文化或同一天下标识)，入主异族之地的不算；
//   · 异族入侵：本帝国(中央或成员)在一场仗里是守方，主攻方(按所在帝国的中央)与本帝国不同族；
//   · 对方整体国力(帝国连同各成员，见 WarSituation.Power)大于本帝国的，以下都不生效；
//   · 战斗欲望：本帝国各军在本族的城(城里主流文化同族)上与入侵者交战，攻守战力 ×Ardor，没有军守城时团练抵抗同样加成
//     (见 FrontSystem.ResolveCity)；这场仗不求和，欠饷也困兽犹斗、倾国而战(见 WarGoalSystem.TrySueForPeace、FightingForSurvival)；
//   · 号召力：每场入侵之战每 RallyYears 年号召一次，同族的本土政权(都城文化也同族、不在本帝国里、与本帝国和入侵者都没在交战、
//     不和入侵者同盟、手上不忙、打得起仗、与本帝国或入侵者接壤)按国力从大到小至多 RallyMax 家，各有 RallyChance 举兵来援
//     (受着天命的共主号召 RallyMandateChance)；已和入侵者议和退出这场仗的不再来。
public static class NativeDefenseSystem
{
    public const float Ardor = 1.2f;
    private const int RallyMax = 3;
    private const float RallyChance = 0.5f, RallyMandateChance = 0.8f, RallyYears = 1f;

    private static object _world;
    private static double _lastMonth = -1d;
    // (本帝国中央, 入侵者中央) → 算的时间、结果；原版一个月 5 个世界秒，一个月算一次
    private static readonly Dictionary<(long, long), (double at, bool value)> Cache = new();
    // 号召过的入侵之战 → 号召的时间
    private static readonly Dictionary<long, double> Rallied = new();

    private static Kingdom Realm(Kingdom kingdom) => FrontSystem.Realm(kingdom);

    private static bool Alive(Kingdom kingdom) => kingdom?.data != null && !kingdom.isRekt();

    private static void ResetWorldState()
    {
        Cache.Clear();
        Rallied.Clear();
        _lastMonth = -1d;
    }

    public static void Tick()
    {
        MapBox world = World.world;
        if (world?.wars == null || ModClass.IS_CLEAR) return;
        if (!ReferenceEquals(_world, world))
        {
            ResetWorldState();
            _world = world;
        }
        double now = world.getCurWorldTime();
        if (_lastMonth >= 0d && now >= _lastMonth && Date.getMonthsSince(_lastMonth) < 1) return;
        _lastMonth = now;
        Cache.Clear();
        try
        {
            Rally(now);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][御侮] 号召失败: {exception.Message}");
        }
    }

    // 本土帝国的中央：帝国中央，文化与都城的主流文化同族
    public static bool IsNativeEmpire(Kingdom realm)
    {
        if (!Alive(realm) || realm.capital == null) return false;
        Empire empire = realm.GetEmpire();
        return empire?.data != null && !empire.isRekt() && empire.CoreKingdom == realm && NativeBorn(realm);
    }

    // 本国的文化与都城的主流文化同族
    private static bool NativeBorn(Kingdom kingdom)
    {
        if (kingdom?.capital == null) return false;
        string culture = CultureService.GetRealmCulture(kingdom), home = CultureService.GetMainCulture(kingdom.capital);
        return CultureService.IsValidCulture(culture) && CultureService.IsValidCulture(home) && CultureService.SameKin(culture, home);
    }

    private static bool SameKin(Kingdom a, Kingdom b) =>
        CultureService.SameKin(CultureService.GetRealmCulture(a), CultureService.GetRealmCulture(b));

    // side 所在的本土帝国正遭 foe(按所在帝国的中央)这一异族入侵，对方整体国力又不大于本帝国
    public static bool Resisting(Kingdom side, Kingdom foe)
    {
        Kingdom native = Realm(side), invader = Realm(foe);
        if (!Alive(native) || !Alive(invader) || native == invader || World.world == null) return false;
        double now = World.world.getCurWorldTime();
        var key = (native.id, invader.id);
        if (Cache.TryGetValue(key, out var cached) && now >= cached.at && now - cached.at < 5d) return cached.value;
        bool value = IsNativeEmpire(native) && CultureService.IsValidCulture(CultureService.GetRealmCulture(invader)) &&
                     !SameKin(native, invader) && InvasionWars(native).Any(war => Realm(war.main_attacker) == invader) &&
                     WarSituation.Power(invader) <= WarSituation.Power(native);
        Cache[key] = (now, value);
        return value;
    }

    // 战斗欲望：side 的军在 city 上与 foe 交战(城是本族的城)
    public static float CombatFactor(Kingdom side, Kingdom foe, City city)
    {
        if (side == null || foe == null || city?.data == null || !Resisting(side, foe)) return 1f;
        string culture = CultureService.GetMainCulture(city);
        return CultureService.IsValidCulture(culture) && CultureService.SameKin(culture, CultureService.GetRealmCulture(Realm(side)))
            ? Ardor
            : 1f;
    }

    // 本帝国(中央连同成员)作为守方的仗，主攻方与本帝国不同族
    private static IEnumerable<War> InvasionWars(Kingdom native)
    {
        Empire empire = native.GetEmpire();
        IEnumerable<Kingdom> members = empire?.kingdoms_list != null ? empire.kingdoms_list.Append(native).Distinct() : new[] { native };
        var seen = new HashSet<War>();
        foreach (Kingdom member in members.ToList())
        {
            if (!Alive(member)) continue;
            foreach (War war in member.getWars())
            {
                if (war?.data == null || war.hasEnded() || !war.isDefender(member) || !seen.Add(war)) continue;
                Kingdom attacker = war.main_attacker;
                if (Alive(attacker) && Realm(attacker) != native && CultureService.IsValidCulture(CultureService.GetRealmCulture(attacker)) &&
                    !SameKin(native, Realm(attacker)))
                    yield return war;
            }
        }
    }

    #region 号召

    private static void Rally(double now)
    {
        foreach (Empire empire in ModClass.EMPIRE_MANAGER?.ToList() ?? new List<Empire>())
        {
            Kingdom native = empire?.CoreKingdom;
            if (empire?.data == null || empire.isRekt() || empire.IsArchived() || !IsNativeEmpire(native)) continue;
            foreach (War war in InvasionWars(native).ToList())
            {
                if (Rallied.TryGetValue(war.data.id, out double at) && now >= at && Date.getYearsSince(at) < RallyYears) continue;
                Kingdom invader = Realm(war.main_attacker);
                if (!Resisting(native, invader)) continue;
                Rallied[war.data.id] = now;
                CallKin(empire, native, invader, war);
            }
        }
        foreach (long id in Rallied.Keys.ToList())
            if (World.world.wars.get(id) is not War war || war.data == null || war.hasEnded()) Rallied.Remove(id);
    }

    private static void CallKin(Empire empire, Kingdom native, Kingdom invader, War war)
    {
        float chance = TianxiaSystem.MandateOf(native) != null ? RallyMandateChance : RallyChance;
        var joined = new List<Kingdom>();
        foreach (Kingdom kin in Candidates(native, invader, war).Take(RallyMax).ToList())
        {
            if (UnityEngine.Random.value >= chance) continue;
            try
            {
                war.joinDefenders(kin);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][御侮] {kin.GetKingdomName()} 参战失败: {exception.Message}");
                continue;
            }
            if (war.isDefender(kin)) joined.Add(kin);
        }
        if (joined.Count == 0) return;
        string text = string.Format(LM.Get("native_rally_log"), empire.GetEmpireFullName(), invader.GetKingdomName(),
            string.Join("、", joined.Select(kin => kin.GetKingdomName())));
        EventRecorder.Record(empire, text, empire.Emperor);
        TranslateHelper.LogEventMessage(text, native);
        LogService.LogInfo($"[EmpireCraft][御侮] {empire.GetEmpireName()} 抗 {invader.GetKingdomName()}，来援：" +
                           string.Join("、", joined.Select(kin => kin.GetKingdomName())));
    }

    // 应召的同族本土政权，国力大的在前
    private static IEnumerable<Kingdom> Candidates(Kingdom native, Kingdom invader, War war)
    {
        Alliance alliance = invader.hasAlliance() ? invader.getAlliance() : null;
        return World.world.kingdoms.list
            .Where(kin => Alive(kin) && kin.isCiv() && Realm(kin) == kin && kin != native && kin != invader &&
                          !war.hasKingdom(kin) && NativeBorn(kin) && SameKin(kin, native) &&
                          !Compatibility.AncientWarfareCompatibility.Owns(kin) &&
                          (alliance == null || !kin.hasAlliance() || kin.getAlliance() != alliance) &&
                          !FrontSystem.Hostile(kin, native) && !FrontSystem.Hostile(kin, invader) &&
                          !WarSituation.TooBusyForWar(kin) && !CorpsSystem.CannotFundWar(kin) &&
                          !EmpirePeacePatch.HasLeft(kin, war, joiningAttackers: false) &&
                          (kin.IsNeighbourWith(native) || kin.IsNeighbourWith(invader)))
            .OrderByDescending(WarSituation.Power);
    }

    #endregion
}

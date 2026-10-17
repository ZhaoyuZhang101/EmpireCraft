using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 跨文明大国外交(用户 2026-10-17 选"跨文明大国外交")：
//   · 两个皇帝的好感：以前同物种一律 -999(永世仇敌，与谁都不能往来)。现在同一族类(SameKin：同文化或同一天下标识)
//     仍是"天无二日"(-999)；别的文明的帝国按远近：接壤或隔海相望 -60，不接壤 -20；结了盟约的不再相恶，另加好感；
//   · 盟约：别的文明、接壤或隔海相望的两个帝国，不在打仗、国力相差不到三倍、互相好感不太坏(-80 以上)，每年一成机会
//     结盟约三十年——互认尊号(皇帝、可汗、哈里发各称其号)、互不侵犯(见 FeudalVassalService.CanDeclareExternalWar)，
//     一方遭第三国攻打另一方五成机会出兵相助(见 OnWarStarted)。盟约记在两国中央(帝国核心王国)上。
public static class GreatPowerDiplomacy
{
    private const int SameKinEmperors = -999, NeighbourEmperors = -60, DistantEmperors = -20, PactOpinion = 40;
    private const int PactYears = 30;
    private const float PactChance = 0.1f, PowerRatio = 3f, MinOpinion = -80f, AidChance = 0.5f;
    private static double _lastScan = -1d;
    private static object _world;

    private static Kingdom Core(Kingdom kingdom) => kingdom?.GetEmpire()?.CoreKingdom ?? kingdom;

    // 两国(帝国按中央)结着盟约
    public static bool Pact(Kingdom a, Kingdom b)
    {
        Kingdom ca = Core(a), cb = Core(b);
        if (ca?.data == null || cb?.data == null || ca == cb) return false;
        var pacts = ca.GetOrCreate().imperial_pacts;
        if (pacts == null || !pacts.TryGetValue(cb.id, out double since)) return false;
        if (Date.getYearsSince(since) < PactYears && !cb.isRekt()) return true;
        pacts.Remove(cb.id);
        return false;
    }

    // 两个皇帝(同物种、不在一个帝国)之间的好感(EmpireCraftOpinionAddition)
    public static int EmperorOpinion(Kingdom main, Kingdom target)
    {
        if (main == null || target == null || main.IsInSameEmpire(target) || !main.IsEmpire() || !target.IsEmpire() ||
            main.getSpecies() != target.getSpecies() || main.king == target.king) return 0;
        if (Pact(main, target)) return 0;
        string a = CultureService.GetRealmCulture(main), b = CultureService.GetRealmCulture(target);
        if (!CultureService.IsValidCulture(a) || !CultureService.IsValidCulture(b) || CultureService.SameKin(a, b))
            return SameKinEmperors;
        // 远近要查边界与海路，好感常被问到：同一对缓存几秒
        var key = (main.id, target.id);
        float now = UnityEngine.Time.unscaledTime;
        if (NearCache.TryGetValue(key, out var hit) && now >= hit.at && now - hit.at < NearCacheSeconds)
            return hit.near ? NeighbourEmperors : DistantEmperors;
        bool near = StraitReach.Near(main, target);
        NearCache[key] = (near, now);
        return near ? NeighbourEmperors : DistantEmperors;
    }

    private const float NearCacheSeconds = 10f;
    private static readonly Dictionary<(long, long), (bool near, float at)> NearCache = new();

    public static int PactOpinionOf(Kingdom main, Kingdom target) =>
        main != null && target != null && !main.IsInSameEmpire(target) && Pact(main, target) ? PactOpinion : 0;

    // 每年一次(CityPatch 的世界扫描轮转)
    public static void TryYearlyScan()
    {
        if (World.world == null || ModClass.EMPIRE_MANAGER == null || ModClass.IS_CLEAR) return;
        double now = World.world.getCurWorldTime();
        if (!ReferenceEquals(_world, World.world))
        {
            _world = World.world;
            _lastScan = now;
            NearCache.Clear();
            return;
        }
        if (_lastScan >= 0d && now >= _lastScan && Date.getYearsSince(_lastScan) < 1) return;
        _lastScan = now;
        try
        {
            List<Empire> empires = ModClass.EMPIRE_MANAGER.Where(empire => empire?.data != null && !empire.isRekt() &&
                                                                           !empire.IsArchived() && empire.CoreKingdom?.data != null &&
                                                                           !empire.CoreKingdom.isRekt()).ToList();
            for (int i = 0; i < empires.Count; i++)
            for (int j = i + 1; j < empires.Count; j++)
                Consider(empires[i], empires[j]);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][大国外交] 结算失败: {exception.Message}");
        }
    }

    private static void Consider(Empire first, Empire second)
    {
        Kingdom a = first.CoreKingdom, b = second.CoreKingdom;
        if (Pact(a, b) || a.isInWarWith(b) || UnityEngine.Random.value >= PactChance) return;
        string ca = CultureService.GetRealmCulture(a), cb = CultureService.GetRealmCulture(b);
        if (!CultureService.IsValidCulture(ca) || !CultureService.IsValidCulture(cb) || CultureService.SameKin(ca, cb)) return;
        if (!StraitReach.Near(a, b)) return;
        double pa = WarSituation.Power(first), pb = WarSituation.Power(second);
        if (pa <= 0d || pb <= 0d || pa > pb * PowerRatio || pb > pa * PowerRatio) return;
        float opinion = Math.Min(World.world.diplomacy?.getOpinion(a, b)?.total ?? 0, World.world.diplomacy?.getOpinion(b, a)?.total ?? 0);
        if (opinion < MinOpinion) return;
        double now = World.world.getCurWorldTime();
        a.GetOrCreate().imperial_pacts[b.id] = now;
        b.GetOrCreate().imperial_pacts[a.id] = now;
        string text = string.Format(LM.Get("great_power_pact_history"), first.GetEmpireFullName(), second.GetEmpireFullName(),
            PactYears);
        first.RecordHistory(directContent: text, kingdomId: a.id);
        second.RecordHistory(directContent: text, kingdomId: b.id);
        TranslateHelper.LogEventMessage(text, a);
    }

    // 盟约之国遭第三国攻打：五成机会出兵相助(WarPatch.new_war)
    public static void OnWarStarted(War war)
    {
        // 只在帝国中央自己是主守方时(成员的仗中央都不插手，见 MemberWarAid)
        Kingdom defender = war?.main_defender, attacker = war?.main_attacker;
        if (defender?.data == null || Core(defender) != defender) return;
        var pacts = defender.GetOrCreate().imperial_pacts;
        if (pacts == null || pacts.Count == 0 || attacker == null || war.getAsset() == WarTypeLibrary.rebellion) return;
        foreach (long id in pacts.Keys.ToList())
        {
            Kingdom ally = World.world.kingdoms.get(id);
            if (ally?.data == null || ally.isRekt() || !Pact(defender, ally) || ally.IsInSameEmpire(attacker) || war.hasKingdom(ally) ||
                Pact(ally, attacker) || ally.isInWarWith(attacker) || ally.isInWarWith(defender) ||
                UnityEngine.Random.value >= AidChance) continue;
            war.joinDefenders(ally);
            TranslateHelper.LogEventMessage(string.Format(LM.Get("great_power_aid_history"), ally.GetKingdomName(),
                defender.GetKingdomName()), ally);
        }
    }
}

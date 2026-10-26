using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 兵败如山倒(用户 2026-10-19："不要整个国家一次性把土地判过去，至少做成战线快速推进"，统一战争、开国雄主讨伐都选"要真打出来")：
//   统一者打天下里的别家、开国雄主讨伐割据者，对方的正规军打到剩不到满编一成半(或一兵不剩)，而且这场仗里已丢了都城、
//   或丢了开战时三分之一的城，连着两个月没缓过来——战线崩溃：胜方打它推进快一倍(见 FrontSystem.PushFactor)、
//   各城团练抵抗减半；不开军团时攻城快一倍(见 CaptureSpeedPatch)。城仍要一座座打下来，不再举国并入、举国归附。
//   对方正规军恢复到满编三成以上、或两家不再交战，崩溃即止。
// 久战疲敝(用户 2026-10-19："统一战争不用一开始就触发，可以时间打的久了降国内稳定度，很差才这样")：被统一、被讨伐的一方，
//   仗打满一年后各城自然稳定度每年少 6(至多 36)，丢的城每占开战时的一成再少 3(至多 15)，见 CityStabilitySystem.Natural；
//   游牧汗国不论什么仗，打满两年后每年少 5(至多 20，部众思归，见 NomadCycleSystem)；
//   兵败如山倒还要它国内稳定度很差——各城平均不到 25(不开财政时按久战疲敝折算)。统一者、雄主自己不疲敝(开国气象)。
// 以前统一战争另有"兵力不到统一者一成就算打光"：国库独大的统一者一开战，小国一仗没打，两个月后就举国并入；
// 雄主讨伐时对方丢一块地就每月有几率举国归附。
public static class FrontCollapse
{
    private const int ConfirmMonths = 2;
    private const float BrokenShare = 0.15f, RecoveredShare = 0.3f, LostCitiesShare = 1f / 3f;
    private const int WipedWarriors = 3;
    private const float WearyGraceYears = 1f, WearyPerYear = 6f, WearyYearsCap = 36f, WearyPerLostTenth = 3f, WearyLossCap = 15f;
    private const float CollapseStability = 25f, BaseStability = 55f;
    // 久战疲敝按国(帝国按核心)一个月算一次
    private static readonly Dictionary<long, (float loss, double at)> Weariness = new();
    public const float PushBonus = 2f, MilitiaFactor = 0.5f, CaptureBonus = 2f;

    // 崩溃的一方(帝国按核心) → 打垮它的一方；连着几个月被打垮
    private static readonly Dictionary<long, long> Collapsed = new();
    private static readonly Dictionary<long, int> Pending = new();
    // 一家一个月只查一次(统一战争与雄主讨伐可能是同一场仗，各查一遍就会一个月算两个月)；原版一个月 5 个世界秒
    private static readonly Dictionary<long, double> CheckedAt = new();
    private const double HalfMonth = 2.5d;

    public static void ResetWorldState()
    {
        Collapsed.Clear();
        Pending.Clear();
        CheckedAt.Clear();
        Weariness.Clear();
    }

    private static Kingdom Realm(Kingdom kingdom) => FrontSystem.Realm(kingdom);

    private static bool Alive(Kingdom kingdom) => kingdom?.data != null && !kingdom.isRekt();

    public static bool Collapsing(Kingdom foe, Kingdom victor)
    {
        Kingdom a = Realm(foe), b = Realm(victor);
        return Alive(a) && Alive(b) && Collapsed.TryGetValue(a.id, out long by) && by == b.id && FrontSystem.Hostile(a, b);
    }

    public static float PushFactor(Kingdom side, Kingdom opponent) => Collapsed.Count > 0 && Collapsing(opponent, side) ? PushBonus : 1f;

    public static float Militia(Kingdom owner, Kingdom attacker) => Collapsed.Count > 0 && Collapsing(owner, attacker) ? MilitiaFactor : 1f;

    public static float Capture(Kingdom owner, Kingdom attacker) => Collapsed.Count > 0 && Collapsing(owner, attacker) ? CaptureBonus : 1f;

    // 每月对一场仗查一次：victor 是不是把 foe 打垮了
    public static void Check(Kingdom victor, Kingdom foe, War war)
    {
        Kingdom a = Realm(foe), b = Realm(victor);
        if (!Alive(a) || !Alive(b) || a == b || war?.data == null || World.world == null) return;
        double now = World.world.getCurWorldTime();
        if (CheckedAt.TryGetValue(a.id, out double at) && now >= at && now - at < HalfMonth) return;
        CheckedAt[a.id] = now;
        if (Collapsed.TryGetValue(a.id, out long by))
        {
            if (by == b.id && FrontSystem.Hostile(a, b) && !Recovered(a)) return;
            Collapsed.Remove(a.id);
            if (by == b.id && FrontSystem.Hostile(a, b))
                Record(b, string.Format(LM.Get("front_collapse_recovered_history"), a.GetKingdomName()));
            return;
        }
        string reason = Lost(a, b, war);
        if (reason == null || !Broken(a) || AverageStability(a) >= CollapseStability)
        {
            Pending.Remove(a.id);
            return;
        }
        Pending.TryGetValue(a.id, out int months);
        Pending[a.id] = ++months;
        if (months < ConfirmMonths) return;
        Pending.Remove(a.id);
        Collapsed[a.id] = b.id;
        string collapse = string.Format(LM.Get("front_collapse_history"), a.GetKingdomName(), reason, b.GetKingdomName());
        Record(b, collapse);
        WarChronicle.Record(a, b, WarChronicle.Collapse, collapse);
        LogService.LogInfo($"[EmpireCraft][兵败如山倒] {a.GetKingdomName()} 被 {b.GetKingdomName()} 打垮({reason})");
    }

    // 两家不再交战、灭了国的从表里清掉(每月一次)
    public static void Prune()
    {
        if (CheckedAt.Count > 256 && World.world != null)
        {
            double now = World.world.getCurWorldTime();
            foreach (long id in CheckedAt.Where(pair => now < pair.Value || now - pair.Value > 60d).Select(pair => pair.Key).ToList())
                CheckedAt.Remove(id);
        }
        if (Collapsed.Count == 0) return;
        foreach (long id in Collapsed.Keys.ToList())
        {
            Kingdom foe = World.world?.kingdoms?.get(id), victor = World.world?.kingdoms?.get(Collapsed[id]);
            if (!Alive(foe) || !Alive(victor) || !FrontSystem.Hostile(foe, victor)) Collapsed.Remove(id);
        }
    }

    // 游牧统一者失了雄主、统一中辍(见 UnificationSystem 游牧难一统)：它打出来的兵败如山倒随之作罢——
    // 不再是统一战争，每月不再查，留着就一直崩到停战
    public static void Release(Kingdom victor)
    {
        Kingdom b = Realm(victor);
        if (!Alive(b) || Collapsed.Count == 0) return;
        foreach (long id in Collapsed.Where(pair => pair.Value == b.id).Select(pair => pair.Key).ToList())
            Collapsed.Remove(id);
    }

    private static IEnumerable<Kingdom> Members(Kingdom realm) =>
        realm.GetEmpire()?.kingdoms_list?.Where(Alive) ?? new[] { realm };

    // 正规军(帝国连同各成员国)现有兵力与满编；不开军团时按士兵(连同可征之兵)与历来最多的士兵
    private static (float strength, float full) Army(Kingdom realm)
    {
        if (CorpsSystem.Enabled)
        {
            var regular = Members(realm).SelectMany(CorpsSystem.Of).Where(corps => !corps.militia && corps.order_id < 0).ToList();
            return (regular.Sum(corps => Mathf.Max(0f, corps.strength)), regular.Sum(corps => Mathf.Max(0f, corps.full)));
        }
        return (Members(realm).Sum(member => member.countTotalWarriors() + CityPopulationSystem.MobilizableReserve(member)),
            Members(realm).Sum(member => member.GetOrCreate().peak_warriors));
    }

    private static bool Broken(Kingdom realm)
    {
        (float strength, float full) = Army(realm);
        if (!CorpsSystem.Enabled && strength <= WipedWarriors) return true;
        return strength < 1f || full > 0f && strength < full * BrokenShare;
    }

    private static bool Recovered(Kingdom realm)
    {
        (float strength, float full) = Army(realm);
        return full > 0f && strength >= full * RecoveredShare;
    }

    // 这场仗里丢了都城(开战时的都城归了对方，或现在都城的城池被对方占着)、或丢了三分之一的城：返回说明，没有返回 null
    private static string Lost(Kingdom foe, Kingdom victor, War war)
    {
        City seat = World.world?.cities?.get(war.GetOrCreate().war_goal_capital_id);
        bool capital = seat?.data != null && !seat.isRekt() && seat.kingdom != null && Realm(seat.kingdom) == victor;
        if (!capital && foe.capital?.data != null && foe.capital.getTile()?.zone is TileZone center &&
            foe.capital.GetTileZoneOccupier(center) is Kingdom occupier && Realm(occupier) == victor)
            capital = true;
        if (capital) return LM.Get("front_collapse_capital");
        int lost = WarSituation.CitiesLost(war, foe);
        if (lost <= 0) return null;
        int before = lost + WarSituation.SideCities(foe);
        return lost >= before * LostCitiesShare ? string.Format(LM.Get("front_collapse_cities"), lost) : null;
    }

    // 久战疲敝：这座城所属的一方正被统一、被雄主讨伐，按打得最久的那场仗算
    public static float WarWeariness(City city)
    {
        Kingdom realm = Realm(city?.kingdom);
        if (!Alive(realm) || World.world == null) return 0f;
        double now = World.world.getCurWorldTime();
        if (Weariness.TryGetValue(realm.id, out var hit) && now >= hit.at && now - hit.at < HalfMonth * 2d) return hit.loss;
        float loss = 0f;
        if (!UnificationSystem.Unifying(realm) && !RulerTraitSystem.FounderReigns(realm))
            foreach (War war in realm.getWars())
            {
                if (war?.data == null || war.hasEnded() || !RulerTraitSystem.IsFounderUnificationWar(war)) continue;
                float years = WarSituation.WarYears(war);
                if (years < WearyGraceYears) continue;
                float wear = Mathf.Min(WearyYearsCap, (years - WearyGraceYears) * WearyPerYear);
                int lost = WarSituation.CitiesLost(war, realm);
                int before = lost + WarSituation.SideCities(realm);
                if (lost > 0 && before > 0) wear += Mathf.Min(WearyLossCap, lost * 10f / before * WearyPerLostTenth);
                loss = Mathf.Max(loss, wear);
            }
        // 部众思归：游牧汗国一场仗打满两年，各城自然稳定度每年少 5(至多 20，见 NomadCycleSystem)
        loss = Mathf.Max(loss, NomadCycleSystem.StabilityLoss(realm));
        if (Weariness.Count > 512) Weariness.Clear();
        Weariness[realm.id] = (loss, now);
        return loss;
    }

    public static string DescribeWeariness(float loss) => string.Format(LM.Get("stability_war_weariness_detail"), loss);

    // 国内稳定度：各城稳定度的平均(帝国连同各成员国)；不开财政(没有稳定度)的城按久战疲敝折算
    private static float AverageStability(Kingdom realm)
    {
        float sum = 0f;
        int count = 0;
        foreach (Kingdom member in Members(realm))
        {
            if (member.cities == null) continue;
            foreach (City city in member.cities)
            {
                if (city?.data == null || city.isRekt()) continue;
                var state = city.GetOrCreate().stability;
                sum += state != null && state.owner_id == member.id && TreasurySystem.Enabled(city)
                    ? state.stability
                    : Mathf.Max(0f, BaseStability - WarWeariness(city));
                count++;
            }
        }
        return count == 0 ? 100f : sum / count;
    }

    private static void Record(Kingdom victor, string text)
    {
        Empire empire = victor.GetEmpire();
        if (empire != null && !empire.isRekt()) EventRecorder.Record(empire, text, null, victor);
        else EventRecorder.Record(victor, text);
    }
}

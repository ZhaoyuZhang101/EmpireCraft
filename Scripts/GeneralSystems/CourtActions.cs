using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems.Decisions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;

namespace EmpireCraft.Scripts.GeneralSystems;

// 君主平时对朝中各派、对权臣出手(用户 2026-11-15 手机上"看不到派系和权臣""也没法与他们互动""毕竟朝堂博弈也得有")。
// 不经奏议，君主一句话的事(手机上照政令一个月后办，见 TerminalHost.Court)；规则是模组自己的：
//   · 恩赏一派：从国库可支配的钱里出(至少 RewardFloor，或可支配的 RewardShare)，圣眷 +12、怨望 −8；
//   · 申饬一派：圣眷 −10、资本 −12、怨望 +8；
//   · 罢其首领：首领去掉中枢官职(没有中枢官职的不能罢)，资本 −20、怨望 +20、圣眷 −6；
//   · 安抚权臣：从国库出(至少 AppeaseFloor，或可支配的 AppeaseShare)，坐大进度 −12，他那一派圣眷 +6；
//   · 削权臣之权：走天平(见 CurbScale)——推力：天命(正统)、君主资望、君主才干、亲近之派(圣眷 ≥60 的各派占比合计)；
//     阻力：权臣资望、权臣一派的占比、已到的阶段(专权/封公/封王)、手握兵权(统军)。读数 ≥1 成：进度 −45，读数 ≥1.6 当场罢黜；
//     不成：进度 +15，他那一派怨望 +15、资本 +10。
// 每样对同一派(或同一位权臣)一年只能来一回(Cooldown，不存档)。
public static class CourtActions
{
    public const int RewardFloor = 30, AppeaseFloor = 60;
    public const float RewardShare = 0.04f, AppeaseShare = 0.08f, CurbHigh = 1.6f;
    private const int CooldownMonths = 12;

    private static readonly Dictionary<string, double> Cooldown = new();

    public static void ResetWorldState() => Cooldown.Clear();

    private static string Key(Empire empire, string target, string kind) => empire.data.id + "|" + target + "|" + kind;

    // 还要等几个月(0 = 现在就能)
    public static int Wait(Empire empire, string target, string kind)
    {
        if (empire?.data == null || !Cooldown.TryGetValue(Key(empire, target, kind), out double at)) return 0;
        return Math.Max(0, CooldownMonths - Date.getMonthsSince(at));
    }

    private static void Mark(Empire empire, string target, string kind) =>
        Cooldown[Key(empire, target, kind)] = World.world.getCurWorldTime();

    private static int Funds(Empire empire) => StateSettlementSystem.DiscretionaryFunds(empire.CoreKingdom);

    public static int RewardCost(Empire empire) => Math.Max(RewardFloor, (int)(Funds(empire) * RewardShare));
    public static int AppeaseCost(Empire empire) => Math.Max(AppeaseFloor, (int)(Funds(empire) * AppeaseShare));

    // ───────── 对一派 ─────────
    // 成了返回 null，否则返回缘由的文字键
    public static string Reward(Empire empire, FixedFaction faction)
    {
        if (!Usable(empire, faction)) return "court_act_fail";
        if (Wait(empire, faction.GetID(), "reward") > 0) return "court_act_cooldown";
        if (!TreasurySystem.TrySpend(empire.CoreKingdom, RewardCost(empire), TreasuryCategory.Gift)) return "court_act_poor";
        CourtSystem.AdjustFaction(empire, faction, "favor", 12f);
        CourtSystem.AdjustFaction(empire, faction, "grievance", -8f);
        Mark(empire, faction.GetID(), "reward");
        return null;
    }

    public static string Rebuke(Empire empire, FixedFaction faction)
    {
        if (!Usable(empire, faction)) return "court_act_fail";
        if (Wait(empire, faction.GetID(), "rebuke") > 0) return "court_act_cooldown";
        CourtSystem.AdjustFaction(empire, faction, "favor", -10f);
        CourtSystem.AdjustFaction(empire, faction, "capital", -12f);
        CourtSystem.AdjustFaction(empire, faction, "grievance", 8f);
        Mark(empire, faction.GetID(), "rebuke");
        return null;
    }

    // 首领在中枢担着的官职
    public static List<OfficeObject> LeaderOffices(Empire empire, FixedFaction faction)
    {
        Actor leader = faction?.GetLeader();
        List<OfficeObject> all = empire?.data?.centerOffice?.GetAllOffices(empire);
        if (leader == null || all == null) return new List<OfficeObject>();
        return all.Where(office => office != null && office.GetActor() == leader).ToList();
    }

    public static string DismissLeader(Empire empire, FixedFaction faction)
    {
        if (!Usable(empire, faction)) return "court_act_fail";
        if (Wait(empire, faction.GetID(), "dismiss") > 0) return "court_act_cooldown";
        List<OfficeObject> offices = LeaderOffices(empire, faction);
        if (offices.Count == 0) return "court_act_no_office";
        foreach (OfficeObject office in offices) office.RemoveActor();
        CourtSystem.AdjustFaction(empire, faction, "capital", -20f);
        CourtSystem.AdjustFaction(empire, faction, "grievance", 20f);
        CourtSystem.AdjustFaction(empire, faction, "favor", -6f);
        Mark(empire, faction.GetID(), "dismiss");
        return null;
    }

    private static bool Usable(Empire empire, FixedFaction faction) =>
        empire?.data != null && !empire.isRekt() && empire.CoreKingdom?.data != null && faction != null &&
        CourtSystem.Factions(empire).Contains(faction);

    // ───────── 对权臣 ─────────
    public static FixedFaction MinisterFaction(Empire empire, Actor minister) =>
        minister == null ? null : CourtSystem.Factions(empire).FirstOrDefault(faction => faction.GetLeader() == minister) ??
                                  CourtSystem.Factions(empire).FirstOrDefault(faction => minister.GetFaction() == faction);

    public static string Appease(Empire empire)
    {
        Actor minister = empire?.GetPowerfulMinister();
        if (minister == null) return "court_act_no_minister";
        if (Wait(empire, "minister", "appease") > 0) return "court_act_cooldown";
        if (!TreasurySystem.TrySpend(empire.CoreKingdom, AppeaseCost(empire), TreasuryCategory.Gift)) return "court_act_poor";
        empire.data.powerful_minister_progress = Math.Max(0, empire.data.powerful_minister_progress - 12);
        FixedFaction side = MinisterFaction(empire, minister);
        if (side != null) CourtSystem.AdjustFaction(empire, side, "favor", 6f);
        Mark(empire, "minister", "appease");
        return null;
    }

    // 削权的天平：推力÷阻力
    public static float CurbScale(Empire empire, List<ScaleWeight> weights)
    {
        Actor minister = empire?.GetPowerfulMinister();
        Actor emperor = empire?.Emperor;
        if (minister == null || emperor == null) return 0f;
        float push = 1f, pull = 1f;
        void Push(string key, float factor)
        {
            if (factor <= 1.001f) return;
            push *= factor;
            weights?.Add(new ScaleWeight(key, factor, true));
        }
        void Pull(string key, float factor)
        {
            if (factor <= 1.001f) return;
            pull *= factor;
            weights?.Add(new ScaleWeight(key, factor, false));
        }
        Push("court_w_mandate", 1f + Math.Max(0, empire.Legitimacy) / 100f);
        Push("court_w_ruler_standing", 1f + Math.Min(1f, Math.Max(0, emperor.renown) / 800f));
        Push("court_w_ruler_talent", 1f + Math.Max(0, Math.Min(30, Math.Max(emperor.intelligence, emperor.stewardship))) / 60f);
        FixedFaction side = MinisterFaction(empire, minister);
        int loyal = CourtSystem.Factions(empire).Where(faction => faction != side && CourtSystem.FactionState(empire, faction).favor >= 60f)
            .Sum(faction => faction.CentralRatio);
        Push("court_w_loyal", 1f + Math.Min(60, loyal) / 60f);
        Pull("court_w_minister_standing", 1f + Math.Min(1.5f, Math.Max(0, minister.renown) / 600f));
        if (side != null) Pull("court_w_minister_faction", 1f + Math.Min(60, side.CentralRatio) / 50f);
        int stage = empire.data.powerful_minister_stage;
        if (stage > 0) Pull("court_w_minister_stage", 1f + 0.35f * stage);
        if (empire.data.is_been_controlled) Pull("court_w_controlled", 1.6f);
        if (minister.hasArmy() || CorpsSystem.IsGeneral(minister)) Pull("court_w_minister_army", 1.5f);
        return push / pull;
    }

    // 削权：成了返回 null(罢黜了 dismissed = true)，否则返回缘由的文字键
    public static string Curb(Empire empire, out bool dismissed)
    {
        dismissed = false;
        Actor minister = empire?.GetPowerfulMinister();
        if (minister == null) return "court_act_no_minister";
        if (Wait(empire, "minister", "curb") > 0) return "court_act_cooldown";
        Mark(empire, "minister", "curb");
        float ratio = CurbScale(empire, null);
        FixedFaction side = MinisterFaction(empire, minister);
        if (ratio < 1f)
        {
            empire.data.powerful_minister_progress = Math.Min(100, empire.data.powerful_minister_progress + 15);
            if (side != null)
            {
                CourtSystem.AdjustFaction(empire, side, "grievance", 15f);
                CourtSystem.AdjustFaction(empire, side, "capital", 10f);
            }
            return "court_act_curb_failed";
        }
        if (ratio >= CurbHigh)
        {
            empire.ResetPowerfulMinister();
            dismissed = true;
        }
        else empire.data.powerful_minister_progress = Math.Max(0, empire.data.powerful_minister_progress - 45);
        if (side != null) CourtSystem.AdjustFaction(empire, side, "grievance", 10f);
        return null;
    }
}

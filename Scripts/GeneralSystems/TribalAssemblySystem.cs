using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 年度会盟纳贡(用户 2026-10-26 嫌部落时代"缺少对应的玩法"，选定"共主每年会盟，共事部落纳贡；不来朝的可讨伐，屡次不朝的离心加重")：
//   · 会盟：有共事部落的部落联盟(共主仍是原始政体，见 TribalFissionService.Confederations)每年在共主都城会诸部一次，
//     按帝国编号错开月份(见 Due)；共主改了政体(宗法分封、汗国……)就不再会盟，归各制度自己的朝贡、臣属管(见 ZhouEraSystem 等)；
//   · 来不来：各部按对共主的好感、相对国力、离共主都城多远、上回来没来朝算一个概率(见 AttendChance)，与共主交战的不来；
//   · 来朝：纳贡入共主国库(部落国库一成，至少 TributeFloor、至多按户数，见 Tribute)，此后一年彼此好感 +AttendOpinion，
//     连续不朝清零；当年脱离的机会减半(见 BreakawayFactor)；
//   · 不来：连续不朝 +1，连续 DefiantStreak 次以上记为"不臣"——共主对它好感 -DefiantOpinion、脱离的机会加倍，
//     共主可以讨伐它(TribalFissionService.Bound 对"共主打不臣的共事部落"放开，部落打共主、部落之间照旧)：
//     每月按连续不朝的次数有机会兴师(过开战闸门，见 Punish)，共主的开战谋划也可能挑中它；
//     破城后照部落之战了结(赔款、败者重新奉共主，见 TribalWarSystem.Concede)，随即不臣清零；不臣的部落反败共主的就此自立(见 OnConcede)；
//   · 史书：每次会盟记一条(几部来朝、贡多少、谁没来)，有部落新记为不臣、共主兴师讨伐时各记一条。
// 记录在 KingdomExtraData：共主记上次会盟的时间、会过几次，共事部落记所奉的共主、上次来朝的时间、连续不朝的次数(换了共主、不再共事就作废)。
// 每月由 RoyalHouseSystem 调用(紧跟 TribalFissionService.EnforceOneCity)，各共主到期才开。
public static class TribalAssemblySystem
{
    public const int DefiantStreak = 2;

    // 来朝的概率：基数，好感每点(封顶 ±100)，国力超过共主三成以后每多一倍共主的国力，离都城的路程占共主管辖范围的比例(封顶 1.5)，
    // 上回来朝的加成，连续不朝每次的减成(封顶三次)
    private const float AttendBase = 0.75f, OpinionWeight = 0.004f, PowerFree = 0.3f, PowerWeight = 0.6f, DistanceWeight = 0.2f,
        CameBonus = 0.1f, MissedPenalty = 0.1f, MinChance = 0.05f, MaxChance = 0.95f;

    // 贡金：部落国库的一成，至少 TributeFloor(国库不够就倾其所有)，至多 TributeCap 或户数的一半(取大的)
    private const int TributeFloor = 10, TributeCap = 40;

    // 会盟之后一年彼此好感；共主对不臣之部的恶感
    private const int AttendOpinion = 10, DefiantOpinion = 25;

    // 讨伐：每月的机会按连续不朝的次数(两次 3%，每多一次再加 3%，封顶 12%)
    private const float PunishChancePerMiss = 0.03f, PunishChanceMax = 0.12f;

    public static void Monthly()
    {
        if (World.world?.kingdoms == null || ModClass.EMPIRE_MANAGER == null) return;
        try
        {
            double now = World.world.getCurWorldTime();
            foreach (Empire overlord in TribalFissionService.Confederations())
            {
                Kingdom core = overlord.CoreKingdom;
                if (core?.data == null || core.capital == null || core.king == null ||
                    EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(core)) continue;
                if (!Due(overlord, core.GetOrCreate(), now)) continue;
                // 没有共事的部落(一部自立的部落)不会盟
                List<Kingdom> tribes = TribalFissionService.VassalsOf(overlord);
                if (tribes.Count > 0) Assemble(overlord, core, tribes, now);
            }
            Punish(Tidy());
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][会盟] 结算失败: {exception.Message}");
        }
    }

    // 到期：上次会盟满 11 个月、又到了本部落联盟的会盟之月(按帝国编号错开)；错过了(一帧跨过几个月)的满 13 个月补开
    private static bool Due(Empire overlord, KingdomExtension.KingdomExtraData data, double now)
    {
        // 读了更早的档：从头算
        if (data.tribal_assembly_held_at > now) data.tribal_assembly_held_at = -1d;
        double last = data.tribal_assembly_held_at;
        int since = last < 0d ? int.MaxValue : Date.getMonthsSince(last);
        if (since < 11) return false;
        int slot = (int)(Math.Abs(overlord.id) % 12L);
        return Date.getMonth(now) % 12 == slot || last >= 0d && since >= 13;
    }

    private static void Assemble(Empire overlord, Kingdom core, List<Kingdom> tribes, double now)
    {
        KingdomExtension.KingdomExtraData record = core.GetOrCreate();
        record.tribal_assembly_held_at = now;
        var came = new List<Kingdom>();
        var absent = new List<Kingdom>();
        var defiant = new List<Kingdom>();
        int total = 0;
        foreach (Kingdom tribe in tribes)
        {
            if (tribe?.data == null || tribe.isRekt()) continue;
            KingdomExtension.KingdomExtraData data = tribe.GetOrCreate();
            if (data.tribal_assembly_lord_id != overlord.id) Forget(data, overlord.id);
            // 无君、无都的部落这回不计
            if (tribe.king == null || tribe.capital == null) continue;
            if (UnityEngine.Random.value < AttendChance(overlord, tribe))
            {
                int tribute = Tribute(tribe);
                if (tribute > 0)
                {
                    tribe.SubMoney(tribute, TreasuryCategory.Tribute);
                    total += tribute;
                }
                data.tribal_missed_assemblies = 0;
                data.tribal_attended_at = now;
                came.Add(tribe);
                continue;
            }
            data.tribal_missed_assemblies++;
            absent.Add(tribe);
            if (data.tribal_missed_assemblies == DefiantStreak) defiant.Add(tribe);
        }
        if (total > 0) core.AddMoney(total, TreasuryCategory.Tribute);
        if (came.Count == 0 && absent.Count == 0) return;
        // 记第几次会盟(史书同一句近几条里只写一次，年年来朝的部数、贡金一样时也不至于漏记)
        int ordinal = ++record.tribal_assembly_count;
        string lord = TribalFissionService.OverlordName(overlord), place = core.capital.GetCityName();
        string content;
        if (came.Count == 0) content = string.Format(LM.Get("tribal_assembly_none_history"), lord, place, ordinal);
        else
        {
            string gifts = total > 0 ? string.Format(LM.Get("tribal_assembly_tribute"), total) : LM.Get("tribal_assembly_tribute_none");
            content = absent.Count == 0
                ? string.Format(LM.Get("tribal_assembly_history"), lord, place, ordinal, came.Count, gifts)
                : string.Format(LM.Get("tribal_assembly_absent_history"), lord, place, ordinal, came.Count, gifts,
                    string.Join(LM.Get("tribal_assembly_separator"), absent.Select(tribe => tribe.GetKingdomName())));
        }
        LogService.LogInfo($"[EmpireCraft][会盟] {content}(共事 {tribes.Count} 部，来朝 {came.Count}，不至 {absent.Count})");
        EventRecorder.RecordRoutine(overlord, content, core.king);
        foreach (Kingdom tribe in defiant)
            EventRecorder.Record(overlord, string.Format(LM.Get("tribal_defiant_history"), tribe.GetKingdomName(), lord),
                logKingdom: tribe);
    }

    // 来朝的概率(公式见类头与常量)；与共主交战的不来
    public static float AttendChance(Empire overlord, Kingdom tribe)
    {
        Kingdom core = overlord?.CoreKingdom;
        if (core?.data == null || tribe?.data == null || tribe.isInWarWith(core)) return 0f;
        KingdomExtension.KingdomExtraData data = tribe.GetOrCreate();
        int opinion = World.world.diplomacy?.getOpinion(tribe, core)?.total ?? 0;
        double corePower = core.GetNationalPower();
        float ratio = corePower > 0d ? (float)(tribe.GetNationalPower() / corePower) : 1f;
        float reach = TribalFissionService.Reach(overlord);
        float share = reach > 0f ? Distance(core, tribe) / reach : 0f;
        bool same = data.tribal_assembly_lord_id == overlord.id;
        int missed = same ? data.tribal_missed_assemblies : 0;
        bool cameLast = same && missed == 0 && data.tribal_attended_at >= 0d;
        float chance = AttendBase + Math.Max(-100, Math.Min(100, opinion)) * OpinionWeight -
                       Math.Max(0f, ratio - PowerFree) * PowerWeight - Math.Min(share, 1.5f) * DistanceWeight +
                       (cameLast ? CameBonus : 0f) - Math.Min(missed, 3) * MissedPenalty;
        return Math.Max(MinChance, Math.Min(MaxChance, chance));
    }

    // 贡金(见 TributeFloor、TributeCap)：户数无小人模式按户，否则按人头
    public static int Tribute(Kingdom tribe)
    {
        int money = Math.Max(0, tribe.GetMoney());
        if (money <= 0) return 0;
        int households = CityPopulationSystem.AbstractPopulationEnabled ? CityPopulationSystem.Households(tribe) : tribe.units?.Count ?? 0;
        int cap = Math.Max(TributeCap, households / 2);
        return Math.Min(money, Math.Min(cap, Math.Max(money / 10, TributeFloor)));
    }

    private static float Distance(Kingdom core, Kingdom tribe)
    {
        WorldTile seat = core?.capital?.getTile(), there = tribe?.capital?.getTile();
        return seat == null || there == null ? 0f : UnityEngine.Mathf.Sqrt(Toolbox.SquaredDistTile(seat, there));
    }

    // 换了共主：以前的来朝、不朝记录作废
    private static void Forget(KingdomExtension.KingdomExtraData data, long lord)
    {
        data.tribal_assembly_lord_id = lord;
        data.tribal_missed_assemblies = 0;
        data.tribal_attended_at = -1d;
    }

    // 不再共事、换了共主的记录作废；返回眼下不臣的部落与其共主
    private static List<(Kingdom tribe, Empire overlord)> Tidy()
    {
        var result = new List<(Kingdom, Empire)>();
        foreach (Kingdom kingdom in World.world.kingdoms.list)
        {
            if (kingdom?.data == null || kingdom.isRekt()) continue;
            KingdomExtension.KingdomExtraData data = kingdom.GetOrCreate();
            if (data.tribal_assembly_lord_id < 0) continue;
            Empire overlord = TribalFissionService.OverlordOf(kingdom);
            if (overlord == null || overlord.id != data.tribal_assembly_lord_id)
            {
                Forget(data, -1L);
                continue;
            }
            if (data.tribal_missed_assemblies >= DefiantStreak) result.Add((kingdom, overlord));
        }
        return result;
    }

    // 共主讨伐不臣：养得起兵、手上不到两场仗、过得了开战闸门(停战年限、强弱，见 WarGate)、两边够得着；每个共主每月至多一场
    private static void Punish(List<(Kingdom tribe, Empire overlord)> defiant)
    {
        var busy = new HashSet<long>();
        foreach ((Kingdom tribe, Empire overlord) in defiant)
        {
            Kingdom core = overlord.CoreKingdom;
            if (core?.data == null || core.isRekt() || busy.Contains(overlord.id) || core.isInWarWith(tribe)) continue;
            int missed = tribe.GetOrCreate().tribal_missed_assemblies;
            float chance = Math.Min(PunishChanceMax, PunishChancePerMiss * (missed - DefiantStreak + 1));
            if (UnityEngine.Random.value >= chance) continue;
            if (core.king == null || EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(core) ||
                EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(tribe) ||
                WarSituation.TooBusyForWar(core) || CorpsSystem.CannotFundWar(core) || !WarGate.CanDeclare(core, tribe) ||
                !WarReach.Reachable(core, tribe)) continue;
            War war = World.world.diplomacy?.startWar(core, tribe, WarTypeLibrary.normal);
            if (war?.data == null) continue;
            busy.Add(overlord.id);
            string lord = TribalFissionService.OverlordName(overlord);
            war.SetEmpireWarType(EmpireWarType.伐不臣);
            war.data.name = string.Format(LM.Get("tribal_punish_war_name"), lord, tribe.GetKingdomName());
            EventRecorder.Record(overlord, string.Format(LM.Get("tribal_punish_history"), lord, tribe.GetKingdomName()),
                logKingdom: core);
        }
    }

    // 部落之战了结(TribalWarSystem.Concede 之后调用)：共主打赢的，败者重新奉共主(仍记着它，见 TribalFissionService.Incorporate)、
    // 不臣清零；共事的部落反败共主的，就此不再共事、自立一方
    public static void OnConcede(Kingdom loser, Kingdom winner)
    {
        if (loser?.data == null || winner?.data == null) return;
        Empire overlord = TribalFissionService.OverlordOf(loser);
        if (overlord != null && overlord.CoreKingdom == winner)
        {
            KingdomExtension.KingdomExtraData data = loser.GetOrCreate();
            if (data.tribal_assembly_lord_id != overlord.id) Forget(data, overlord.id);
            if (data.tribal_missed_assemblies > 0)
                LogService.LogInfo($"[EmpireCraft][会盟] {loser.GetKingdomName()} 兵败服罪，不朝 {data.tribal_missed_assemblies} 次清零");
            data.tribal_missed_assemblies = 0;
            return;
        }
        overlord = TribalFissionService.OverlordOf(winner);
        if (overlord == null || overlord.CoreKingdom != loser) return;
        KingdomExtension.KingdomExtraData free = winner.GetOrCreate();
        free.tribal_overlord_id = -1L;
        Forget(free, -1L);
        string content = string.Format(LM.Get("tribal_defiant_free_history"), winner.GetKingdomName(),
            TribalFissionService.OverlordName(overlord));
        EventRecorder.Record(overlord, content, logKingdom: winner);
        World.world?.zone_calculator?.dirtyAndClear();
    }

    // 记为不臣(连续不朝 DefiantStreak 次以上)：只认眼下所奉的共主
    public static bool DefiantTo(Kingdom tribe, Empire overlord)
    {
        if (tribe?.data == null || overlord == null) return false;
        KingdomExtension.KingdomExtraData data = tribe.GetOrCreate();
        return data.tribal_assembly_lord_id == overlord.id && data.tribal_missed_assemblies >= DefiantStreak;
    }

    // 当年(一年内)来朝过
    private static bool Attended(KingdomExtension.KingdomExtraData data)
    {
        double at = data.tribal_attended_at;
        return at >= 0d && World.world.getCurWorldTime() >= at && Date.getYearsSince(at) < 1f;
    }

    // 脱离的机会(见 TribalFissionService.Breakaway)：不臣的加倍，当年来朝的减半
    public static float BreakawayFactor(Kingdom tribe, Empire overlord)
    {
        if (tribe?.data == null || overlord == null) return 1f;
        KingdomExtension.KingdomExtraData data = tribe.GetOrCreate();
        if (data.tribal_assembly_lord_id != overlord.id) return 1f;
        if (data.tribal_missed_assemblies >= DefiantStreak) return 2f;
        return Attended(data) ? 0.5f : 1f;
    }

    // 好感(见 EmpireCraftOpinionAddition)：当年来朝的部落与共主彼此 +AttendOpinion；共主对不臣之部 -DefiantOpinion
    public static int OpinionOf(Kingdom main, Kingdom target)
    {
        if (main?.data == null || target?.data == null || main == target) return 0;
        Kingdom tribe;
        Empire overlord = TribalFissionService.OverlordOf(main);
        if (overlord != null && overlord.CoreKingdom == target) tribe = main;
        else if ((overlord = TribalFissionService.OverlordOf(target)) != null && overlord.CoreKingdom == main) tribe = target;
        else return 0;
        KingdomExtension.KingdomExtraData data = tribe.GetOrCreate();
        if (data.tribal_assembly_lord_id != overlord.id) return 0;
        if (data.tribal_missed_assemblies >= DefiantStreak) return tribe == target ? -DefiantOpinion : 0;
        return Attended(data) ? AttendOpinion : 0;
    }
}

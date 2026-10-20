using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 去留冷却(用户 2026-10-20 转群友"蒙古、阿拉伯这边把诸侯国独立了马上又加入；拜占庭这边加入了又分裂")：
// 上帝说了算——神力做下的去留要稳住一段时间；AI 自己的并入、离开照旧，但不许来回横跳。
//   · 神力放出(移除帝国；解除附庸；加冕称帝时离开原帝国；加入帝国时离开原帝国)：ReleaseYears 年内原帝国(连同它的核心)、
//     原宗主(连同宗主所在的帝国)不得收回——自愿归附、收朝贡、收附庸、各种强制并入一概拦下(见 Empire.join、
//     KingdomExtension.JoinTakenAlliance、FeudalVassalService.BindCore)；打仗打下来的照旧(两家正在交战，
//     或停战不到 WarResultYears 年——战后称臣、立附庸在停战后几帧里结算)。它自己也不主动回去，除非危急(见 Desperate)。
//     头 TruceYears 年原帝国、原宗主不对它开战(用户 2026-10-20 定，见 TruceForbids、WarGate.Forbidden)，它去打原主不拦；
//   · 神力拉入(加入帝国、创建省份、普天之下、强制附庸)：JoinYears 年内不因离心自行离开——拥兵自立、诸侯同盟、
//     教皇党倒戈、改朝离心、亏空自立、羁縻叛去、附庸独立都不算它(RebellionSystem.CanAttempt 与 Settled)；
//     战败失城、亡国、帝国瓦解另说；
//   · 自己离开的(不论何因)：RejoinYears 年内不自愿回归同一帝国、不自愿向它称臣纳贡，危急时除外；
//   · 新并入的成员(不论怎么并入)：RebellionRules.LocalGraceYears 年内不被诸侯同盟拉走、不倒戈、不因改朝或亏空离去
//     (以前只有起事这一路看这十年，见 Settled)。
// 诊断日志：[EmpireCraft][归附] 记并入与拦下，[EmpireCraft][离心] 记离开(谁、哪个帝国、由哪一路调用、刚入/刚离几年)，
// 每分钟至多 LogPerMinute 行，同一国被拦下的同一帝国一年只记一次。
public static class MembershipCooldown
{
    public const int ReleaseYears = 15;
    public const int JoinYears = 15;
    public const int RejoinYears = 10;
    public const int WarResultYears = 1;
    // 神力放出后原帝国、原宗主不对它开战的年数(见 TruceForbids)
    public const int TruceYears = 10;
    // 危急：正被别国攻打，这场仗丢了三成以上的城，或敌方兵力是自己的三倍以上
    public const float DesperateLostShare = 0.3f, DesperateStrength = 3f;
    private const int LogPerMinute = 30;

    #region 神力

    private static int _godDepth;

    // 神力按钮里改去留时包一层：标记"上帝说了算"，不受保护期拦阻，并记下放出、拉入
    public static bool GodActing => _godDepth > 0;

    public static T ByGod<T>(Func<T> action)
    {
        _godDepth++;
        try { return action(); }
        finally { _godDepth--; }
    }

    public static void ByGod(Action action)
    {
        _godDepth++;
        try { action(); }
        finally { _godDepth--; }
    }

    // 神力放出：从 empire(或 lord 名下)放出
    public static void MarkRelease(Kingdom kingdom, Empire empire, Kingdom lord)
    {
        if (kingdom?.data == null || kingdom.isRekt() || World.world == null || empire == null && lord == null) return;
        var data = kingdom.GetOrCreate();
        data.manual_release_empire_id = empire?.id ?? -1L;
        data.manual_release_lord_id = lord?.id ?? -1L;
        data.manual_release_at = World.world.getCurWorldTime();
        // 神力把它放出来：之前神力拉入的那一段不再算
        if (empire != null && data.manual_join_empire_id == empire.id || lord != null && data.manual_join_lord_id == lord.id)
            data.manual_join_at = -1d;
        Log("离心", $"神力放出 {kingdom.GetKingdomName()}，离开{Name(empire, lord)}；{ReleaseYears} 年内原主不得收回");
    }

    // 神力拉入：并入 empire(或归 lord 为附庸)
    public static void MarkJoin(Kingdom kingdom, Empire empire, Kingdom lord)
    {
        if (kingdom?.data == null || kingdom.isRekt() || World.world == null || empire == null && lord == null) return;
        var data = kingdom.GetOrCreate();
        data.manual_join_empire_id = empire?.id ?? -1L;
        data.manual_join_lord_id = lord?.id ?? -1L;
        data.manual_join_at = World.world.getCurWorldTime();
        // 神力又把它放回原主：放出的保护期作废
        if (ReleaseYearsLeft(kingdom, empire, lord) > 0) data.manual_release_at = -1d;
        Log("归附", $"神力拉入 {kingdom.GetKingdomName()}，归{Name(empire, lord)}；{JoinYears} 年内不因离心自行离开");
    }

    #endregion

    #region 判定

    private static double Now => World.world?.getCurWorldTime() ?? -1d;

    // 神力放出的保护期还剩几年(对 empire 或 lord 而言)。从帝国放出的，帝国核心也收不得；从某国名下放出的，那国所在的帝国也收不得
    public static int ReleaseYearsLeft(Kingdom kingdom, Empire empire, Kingdom lord)
    {
        if (kingdom?.data == null || World.world == null) return 0;
        var data = kingdom.GetOrCreate();
        if (data.manual_release_at < 0d || Now < data.manual_release_at) return 0;
        long empireId = empire?.id ?? -1L;
        long coreId = empire?.CoreKingdom?.id ?? -1L;
        long lordId = lord?.id ?? -1L;
        long lordEmpireId = lord?.GetEmpireID() ?? -1L;
        bool match = data.manual_release_empire_id >= 0 &&
                     (data.manual_release_empire_id == empireId || data.manual_release_empire_id == lordEmpireId) ||
                     data.manual_release_lord_id >= 0 &&
                     (data.manual_release_lord_id == lordId || data.manual_release_lord_id == coreId);
        return match ? Math.Max(0, ReleaseYears - Date.getYearsSince(data.manual_release_at)) : 0;
    }

    // 神力放出后 TruceYears 年内，原帝国(核心与各成员)、原宗主(连同宗主所在的帝国)不对它开战(见 WarGate.Forbidden)；
    // 它自己去打原主不拦。保护期因战事、危急并回或神力放回而作废的，这一条随之作废
    public static bool TruceForbids(Kingdom attacker, Kingdom target)
    {
        if (attacker?.data == null || target?.data == null || attacker == target || World.world == null) return false;
        var data = target.GetOrCreate();
        if (data.manual_release_at < 0d || Now < data.manual_release_at || Date.getYearsSince(data.manual_release_at) >= TruceYears)
            return false;
        long empireId = attacker.GetEmpireID();
        if (data.manual_release_empire_id >= 0 && empireId == data.manual_release_empire_id) return true;
        if (data.manual_release_lord_id < 0) return false;
        if (attacker.id == data.manual_release_lord_id) return true;
        Kingdom lord = World.world.kingdoms?.get(data.manual_release_lord_id);
        return empireId >= 0 && lord?.data != null && !lord.isRekt() && lord.GetEmpireID() == empireId;
    }

    // 神力拉入、仍在原处、未满 JoinYears 年(帝国核心不算)
    public static bool ManualJoinActive(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt() || World.world == null) return false;
        var data = kingdom.GetOrCreate();
        if (data.manual_join_at < 0d || Now < data.manual_join_at || Date.getYearsSince(data.manual_join_at) >= JoinYears)
            return false;
        Empire empire = kingdom.GetEmpire();
        if (empire != null && empire.CoreKingdom != kingdom && data.manual_join_empire_id == empire.id) return true;
        Kingdom lord = FeudalVassalService.GetOverlord(kingdom);
        return lord != null && data.manual_join_lord_id == lord.id;
    }

    // 已在帝国里待稳：入帝国满 RebellionRules.LocalGraceYears 年，且不在神力拉入的稳定期里
    public static bool Settled(Kingdom kingdom) =>
        kingdom?.data != null && RebellionSystem.GraceYearsRemaining(kingdom) == 0 && !ManualJoinActive(kingdom);

    // 离开 empire 才几年(没离开过、离开的不是它为 -1)
    public static int YearsSinceLeft(Kingdom kingdom, Empire empire)
    {
        if (kingdom?.data == null || empire == null || World.world == null) return -1;
        var data = kingdom.GetOrCreate();
        if (data.left_empire_id != empire.id || data.left_empire_at < 0d || Now < data.left_empire_at) return -1;
        return Date.getYearsSince(data.left_empire_at);
    }

    // 两家正在交战，或停战不到 WarResultYears 年：打下来的、战后称臣立附庸的，照旧
    private static bool WarResult(Kingdom kingdom, Empire empire, Kingdom lord)
    {
        if (empire != null && !empire.isRekt() && !empire.IsArchived() && empire.hasWarsWith(kingdom)) return true;
        if (lord != null && !lord.isRekt() && kingdom.isInWarWith(lord)) return true;
        Kingdom other = lord ?? empire?.CoreKingdom;
        if (other?.data == null || other.isRekt()) return false;
        double ended = World.world?.diplomacy?.getRelation(kingdom, other)?.data?.timestamp_last_war_ended ?? -1d;
        return ended >= 0d && Now >= ended && Date.getYearsSince(ended) < WarResultYears;
    }

    // 危急：正被别国(不是 former 帝国的)攻打，这场仗已丢了 DesperateLostShare 以上的城，或敌方兵力是自己的 DesperateStrength 倍以上
    public static bool Desperate(Kingdom kingdom, Empire former)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return false;
        foreach (War war in kingdom.getWars())
        {
            if (war?.data == null || war.hasEnded() || !war.isDefender(kingdom)) continue;
            Kingdom attacker = war.getMainAttacker();
            if (attacker?.data == null || attacker.isRekt() || former != null && attacker.GetEmpire() == former) continue;
            int lost = WarSituation.CitiesLost(war, kingdom);
            int held = WarSituation.SideCities(kingdom);
            if (lost > 0 && lost >= (lost + held) * DesperateLostShare) return true;
            if (WarSituation.StrengthRatio(kingdom, attacker) >= DesperateStrength) return true;
        }
        return false;
    }

    // 自愿归附、自愿称臣纳贡(由它自己发起)：神力放出的保护期内、自己离开不到 RejoinYears 年，都不回去，危急时除外
    public static bool BlocksVoluntary(Kingdom kingdom, Empire empire)
    {
        if (GodActing || kingdom?.data == null || empire == null) return false;
        bool released = ReleaseYearsLeft(kingdom, empire, null) > 0;
        int left = YearsSinceLeft(kingdom, empire);
        bool recent = left >= 0 && left < RejoinYears;
        return (released || recent) && !Desperate(kingdom, empire);
    }

    // 并入帝国(不论自愿还是强制)：神力放出的保护期内拦下，打仗打下来的、自己危急来投的除外。会记日志
    public static bool BlocksJoin(Kingdom kingdom, Empire empire)
    {
        if (GodActing || kingdom?.data == null || empire == null) return false;
        int years = ReleaseYearsLeft(kingdom, empire, null);
        if (years <= 0 || WarResult(kingdom, empire, null) || Desperate(kingdom, empire)) return false;
        LogBlocked(kingdom, empire, null, "并入", years);
        return true;
    }

    // 收为朝贡国：神力放出的保护期内拦下(战后称臣、危急除外)；自愿的另看自己离开几年
    public static bool BlocksTributary(Kingdom kingdom, Empire empire, bool forced)
    {
        if (GodActing || kingdom?.data == null || empire == null) return false;
        if (!forced && BlocksVoluntary(kingdom, empire)) return true;
        int years = ReleaseYearsLeft(kingdom, empire, null);
        if (years <= 0 || WarResult(kingdom, empire, null) || Desperate(kingdom, empire)) return false;
        LogBlocked(kingdom, empire, null, "称臣纳贡", years);
        return true;
    }

    // 收为附庸：神力放出的保护期内原宗主(连同原帝国的核心)收不得，打下来的、危急的除外
    public static bool BlocksVassal(Kingdom subject, Kingdom lord, bool log = true)
    {
        if (GodActing || subject?.data == null || lord?.data == null || subject.GetOrCreate().manual_release_at < 0d) return false;
        Empire lordEmpire = lord.GetEmpire();
        int years = ReleaseYearsLeft(subject, lordEmpire, lord);
        if (years <= 0 || WarResult(subject, lordEmpire, lord) || Desperate(subject, lordEmpire)) return false;
        if (log) LogBlocked(subject, lordEmpire, lord, "为附庸", years);
        return true;
    }

    #endregion

    #region 并入与离开(由 Empire.join、Empire.leave 调用)

    public static void OnJoined(Kingdom kingdom, Empire empire, bool forced, bool legitimacyTransfer)
    {
        if (kingdom?.data == null || empire?.data == null || kingdom == empire.CoreKingdom) return;
        if (GodActing)
        {
            MarkJoin(kingdom, empire, null);
            return;
        }
        // 保护期里打下来的、危急来投的：已经回来了，保护期作废(免得日后进出又被它拦住)
        int protectedYears = ReleaseYearsLeft(kingdom, empire, null);
        if (protectedYears > 0) kingdom.GetOrCreate().manual_release_at = -1d;
        if (!LogAllowed()) return;
        try
        {
            int left = YearsSinceLeft(kingdom, empire);
            string how = (legitimacyTransfer ? "正统转移" : forced ? "强制" : "自愿") +
                         (protectedYears > 0 ? $"，神力放出的保护期还剩 {protectedYears} 年，因战事或危急放行" : "");
            string back = left >= 0 ? $"；离开该帝国才 {left} 年{(left < RejoinYears ? "(来回)" : "")}" : "";
            LogService.LogInfo($"[EmpireCraft][归附] {kingdom.GetKingdomName()} 并入 {empire.GetEmpireName()}({how}，{Caller()}){back}");
        }
        catch (Exception)
        {
            // 诊断日志出错不影响并入
        }
    }

    public static void OnLeft(Kingdom kingdom, Empire empire, bool rebel)
    {
        if (kingdom?.data == null || empire?.data == null || World.world == null) return;
        var data = kingdom.GetOrCreate();
        // 入帝国几年(EmpireJoin 记的时间；先取，离开后不再改)
        double joinedAt = data.TimestampEmpire;
        data.left_empire_id = empire.id;
        data.left_empire_at = Now;
        if (GodActing)
        {
            MarkRelease(kingdom, empire, null);
            return;
        }
        if (!LogAllowed()) return;
        try
        {
            string stay = joinedAt >= 0d && Now >= joinedAt ? $"；入帝国 {Date.getYearsSince(joinedAt)} 年" : "";
            string manual = data.manual_join_empire_id == empire.id && data.manual_join_at >= 0d && Now >= data.manual_join_at &&
                            Date.getYearsSince(data.manual_join_at) < JoinYears ? "(神力拉入未满期)" : "";
            LogService.LogInfo($"[EmpireCraft][离心] {kingdom.GetKingdomName()} 离开 {empire.GetEmpireName()}" +
                               $"({(rebel ? "叛离" : "离开")}，{Caller()}){stay}{manual}");
        }
        catch (Exception)
        {
            // 诊断日志出错不影响离开
        }
    }

    #endregion

    #region 日志

    private static float _logWindow = -1f;
    private static int _logCount;
    private static readonly Dictionary<string, double> LastBlocked = new();

    private static bool LogAllowed()
    {
        float now = UnityEngine.Time.realtimeSinceStartup;
        if (_logWindow < 0f || now < _logWindow || now - _logWindow >= 60f)
        {
            _logWindow = now;
            _logCount = 0;
        }
        return _logCount++ < LogPerMinute;
    }

    private static void Log(string tag, string text)
    {
        try
        {
            if (LogAllowed()) LogService.LogInfo($"[EmpireCraft][{tag}] {text}");
        }
        catch (Exception)
        {
            // 诊断日志出错不影响结算
        }
    }

    // 同一国被同一帝国(宗主)拦下的同一类，一年只记一次
    private static void LogBlocked(Kingdom kingdom, Empire empire, Kingdom lord, string what, int years)
    {
        string key = $"{kingdom.id}:{empire?.id ?? -1L}:{lord?.id ?? -1L}:{what}";
        double now = Now;
        if (LastBlocked.TryGetValue(key, out double last) && now >= last && Date.getYearsSince(last) < 1) return;
        if (LastBlocked.Count > 2000) LastBlocked.Clear();
        LastBlocked[key] = now;
        Log("归附", $"拦下 {kingdom.GetKingdomName()} {what}{Name(empire, lord)}：神力放出的保护期还剩 {years} 年({Caller()})");
    }

    private static string Name(Empire empire, Kingdom lord) =>
        lord != null ? lord.GetKingdomName() : empire?.GetEmpireName() ?? "";

    // 由哪一路调用(跳过 Empire 与本类自身)，诊断用
    private static string Caller()
    {
        try
        {
            var frames = new global::System.Diagnostics.StackTrace(2, false).GetFrames();
            if (frames == null) return "";
            foreach (var frame in frames)
            {
                var method = frame?.GetMethod();
                Type type = method?.DeclaringType;
                if (type == null || type == typeof(MembershipCooldown) || type == typeof(Empire) ||
                    type == typeof(KingdomExtension) || type == typeof(FeudalVassalService)) continue;
                // 匿名方法、lambda 归到外层类
                while (type.DeclaringType != null && type.Name.StartsWith("<", StringComparison.Ordinal)) type = type.DeclaringType;
                return type.Name + "." + method.Name;
            }
        }
        catch (Exception)
        {
            // 取不到调用栈不影响结算
        }
        return "";
    }

    #endregion
}

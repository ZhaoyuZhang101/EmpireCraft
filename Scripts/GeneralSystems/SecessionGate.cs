using System;
using System.Collections.Generic;
using System.Diagnostics;
using EmpireCraft.Scripts.GameLibrary;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 不许自立(群友 2026-11-12 开着「禁止AI宣战」后"不宣战了 但是莫名其妙独立"，用户定"关掉外交或者关掉战争之后，独立也一并关掉"，
// 问答全选推荐：模组三项+原版两项都算、剧本事件照常、立新国与离开帝国都拦)：
//   · 开关：模组「禁止AI外交」(总)「禁止AI宣战」「禁止叛乱」任一开着，或原版「外交」「叛乱」任一关着；
//   · 拦三道公共的门：
//       城自立成新国——一切经 City.makeOwnKingdom 的(见 CityPatch.makeOwnKingdom：部落分迁、世家据城、三家分晋、诸子分帐、
//         管不到放出、神职据圣城、拥兵自立、各种民变起事……)；
//       成员国叛离帝国——Empire.leave 记作叛离(isLeave)的(投保护者、附从散去……)；
//       各种起事——RebellionSystem.RebellionsBanned(五十来处的 CanAttempt 与两个起事入口都看它)；
//     另有几处不记叛离就离开帝国、或进门前就动了东西的自立(东西分立、绝嗣裂土、汗国崩解与离心、埃米尔坐大、异族之地、
//     亏空自立、羁縻叛去、改朝离心、护国、北归、诸侯投保护者、教皇党倒戈、新教起兵、权臣逼反……)在各自决策处问 Forbids；
//   · 放行：神力(MembershipCooldown.GodActing、原版激励掉落)、神力推满的诉求(DiplomacyLaws.Forced)、剧本开局与剧本事件、
//     受命立国与战后安置(Sanctioned 作用域：分封、推恩、设省、营建新城立封国、神罗成员、城邦与兼领战果、傀儡、退位、骑士团国、
//     终端分省，以及争位之战收场、立为保护国)；
//   · 只拦新的自立，已经在打的叛乱、争位照原规矩收尾；已经分出去的不收回。
// 附庸、朝贡国、羁縻之外的宗藩关系(部落共事、战国诸侯离开周室)不在此列。
public static class SecessionGate
{
    private const string LogTag = "[EmpireCraft][不许自立]";

    // 纯判定(回归测试抠这一段)：五个开关里任一个是"关"的意思就不许，放行的情形除外
    public static bool Decide(bool banDiplomacy, bool banWar, bool banRebellion, bool vanillaDiplomacy,
        bool vanillaRebellions, bool exempt) =>
        !exempt && (banDiplomacy || banWar || banRebellion || !vanillaDiplomacy || !vanillaRebellions);

    private static int _sanctioned, _scenario, _god;
    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);

    // 开关开着(不论放不放行)：法则说明、日志用
    public static bool Banned() => Decide(On(EmpireCraftWorldLawLibrary.empirecraft_law_ban_diplomacy),
        On(EmpireCraftWorldLawLibrary.empirecraft_law_ban_ai_war), On(EmpireCraftWorldLawLibrary.empirecraft_law_ban_rebellion),
        !Off(WorldLawLibrary.world_law_diplomacy), !Off(WorldLawLibrary.world_law_rebellions), exempt: false);

    public static bool Exempt => _sanctioned > 0 || _scenario > 0 || _god > 0 || MembershipCooldown.GodActing ||
                                 DiplomacyLaws.Forced || WarReach.ScenarioDeclaring || WorldMap.WorldMapScenario.Building;

    // 这一刻不许自立
    public static bool Forbids() => !Exempt && Banned();

    // 拦下时记一笔(同一来处同一对象只记一次)
    public static bool Forbids(string what, string subject)
    {
        if (!Forbids()) return false;
        string caller = Caller();
        if (Reported.Count < 512 && Reported.Add($"{what}|{caller}|{subject}"))
            LogService.LogInfo($"{LogTag} 拦下{what}：{subject}(来自 {caller})");
        return true;
    }

    // 受命立国、战后安置：这一步里的立国、离开帝国照常
    public static T Sanctioned<T>(Func<T> action)
    {
        _sanctioned++;
        try { return action(); }
        finally { _sanctioned--; }
    }

    public static void Sanctioned(Action action)
    {
        _sanctioned++;
        try { action(); }
        finally { _sanctioned--; }
    }

    // 剧本事件(见 ScenarioEventSystem.Fire)
    public static T Scenario<T>(Func<T> action)
    {
        _scenario++;
        try { return action(); }
        finally { _scenario--; }
    }

    // 原版神力：激励掉落让城当场自立(City.useInspire，见 CityPatch)
    public static void EnterGod() => _god++;

    public static void ExitGod()
    {
        if (_god > 0) _god--;
    }

    // 模组法则：世界还没建好时取不到法则对象，按没开算
    private static bool On(WorldLawAsset law)
    {
        try
        {
            return law?.isEnabled() == true;
        }
        catch
        {
            return false;
        }
    }

    // 原版法则：取不到时按开着(默认)算
    private static bool Off(WorldLawAsset law)
    {
        try
        {
            return law != null && !law.isEnabled();
        }
        catch
        {
            return false;
        }
    }

    private static string Caller()
    {
        try
        {
            foreach (StackFrame frame in new StackTrace(2, false).GetFrames() ?? Array.Empty<StackFrame>())
            {
                Type type = frame.GetMethod()?.DeclaringType;
                if (type?.Namespace == null || !type.Namespace.StartsWith("EmpireCraft", StringComparison.Ordinal)) continue;
                string method = frame.GetMethod().Name;
                // 跳过闸本身与两道门(立国补丁、Empire.leave)，记真正的来处
                if (type == typeof(SecessionGate) || method is "makeOwnKingdom" or "leave") continue;
                return $"{type.Name}.{method}";
            }
        }
        catch (Exception)
        {
            // 只是日志
        }
        return "原版";
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GamePatches;

// 帝国中央议和，听命于中央的成员一起停战：
//   原版主攻(守)国退出战争时会从同侧另挑一国接任，战争照打——中央单方面议和后仗由行政区接着打，
//   中央又因"成员在打仗"被拉回去(见 EmpireCraftKingdomBehCheckPlots.CheckJoinWar)。
//   现在中央退出某场战争(议和或战败)、或整场战争结束时，帝国里没有自主外交权的成员(行政区、军府等)
//   一并退出与中央这些对手的战争；中央与对手记下停战时间，此前就在打的旧仗中央不再因成员回去参战。
public class EmpirePeacePatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public sealed class Snapshot
    {
        public Empire Empire;
        public List<Kingdom> Foes;
    }

    private static bool _cascading;

    public void Initialize()
    {
        var harmony = new Harmony(nameof(EmpirePeacePatch));
        harmony.Patch(AccessTools.Method(typeof(War), nameof(War.removeFromWar)),
            prefix: new HarmonyMethod(typeof(EmpirePeacePatch), nameof(BeforeRemove)),
            postfix: new HarmonyMethod(typeof(EmpirePeacePatch), nameof(AfterRemove)));
        harmony.Patch(AccessTools.Method(typeof(WarManager), nameof(WarManager.endWar)),
            prefix: new HarmonyMethod(typeof(EmpirePeacePatch), nameof(BeforeEnd)) { priority = Priority.First },
            postfix: new HarmonyMethod(typeof(EmpirePeacePatch), nameof(AfterEnd)));
    }

    private static Snapshot Capture(War war, Kingdom kingdom)
    {
        if (war?.data == null || kingdom == null || kingdom.isRekt() || !kingdom.IsEmpire()) return null;
        Empire empire = kingdom.GetEmpire();
        if (empire == null || empire.isRekt()) return null;
        bool attacker = war.isAttacker(kingdom);
        if (!attacker && !war.isDefender(kingdom)) return null;
        List<Kingdom> foes = (attacker ? war._list_defenders : war._list_attackers)
            .Where(foe => foe != null && !foe.isRekt() && foe.GetEmpire() != empire).ToList();
        return foes.Count == 0 ? null : new Snapshot { Empire = empire, Foes = foes };
    }

    public static void BeforeRemove(War __instance, Kingdom pKingdom, out Snapshot __state) =>
        __state = __instance != null && __instance.isAlive() && !__instance.hasEnded() ? Capture(__instance, pKingdom) : null;

    public static void AfterRemove(Snapshot __state) => Cascade(__state);

    public static void BeforeEnd(War pWar, out List<Snapshot> __state)
    {
        __state = null;
        if (pWar?.data == null || !pWar.isAlive() || pWar.hasEnded()) return;
        foreach (Kingdom kingdom in pWar._list_attackers.Concat(pWar._list_defenders).ToList())
        {
            Snapshot snapshot = Capture(pWar, kingdom);
            if (snapshot != null) (__state ??= new List<Snapshot>()).Add(snapshot);
        }
    }

    // 战争补丁可能拦下"结束"(如索取法理未达成)：真结束了才连带停战
    public static void AfterEnd(War pWar, List<Snapshot> __state)
    {
        if (__state == null || pWar == null || !pWar.hasEnded()) return;
        foreach (Snapshot snapshot in __state) Cascade(snapshot);
    }

    // 听命于中央的成员：行政区、军府等，或政体没有自主外交权的成员；正在叛乱的不算
    public static bool Obeys(Empire empire, Kingdom member) =>
        member?.data != null && !member.isRekt() && member != empire.CoreKingdom && member.GetEmpire() == empire &&
        !member.IsFactionRebelling() && !member.IsLocalRebelling() &&
        (member.IsAdministrativeKingdomType() || member.GetRegime()?.IsAllowDiplomacy() == false);

    private static void Cascade(Snapshot snapshot)
    {
        if (snapshot == null || _cascading || World.world == null) return;
        _cascading = true;
        try
        {
            double now = World.world.getCurWorldTime();
            Kingdom core = snapshot.Empire.CoreKingdom;
            var foes = new HashSet<Kingdom>(snapshot.Foes);
            foreach (Kingdom foe in foes) SetTruce(core, foe, now);
            foreach (Kingdom member in snapshot.Empire.kingdoms_list.ToList())
            {
                if (!Obeys(snapshot.Empire, member)) continue;
                foreach (War war in member.getWars().ToList())
                {
                    if (war?.data == null || !war.isAlive() || war.hasEnded()) continue;
                    var opponents = war.isAttacker(member) ? war._list_defenders : war._list_attackers;
                    if (!opponents.Any(foes.Contains)) continue;
                    war.leaveWar(member);
                }
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 中央议和连带成员停战失败: {exception.Message}");
        }
        finally
        {
            _cascading = false;
        }
    }

    private static void SetTruce(Kingdom a, Kingdom b, double now)
    {
        if (a == null || b == null || a == b || a.isRekt() || b.isRekt()) return;
        World.world.diplomacy.getRelation(a, b).data.timestamp_last_war_ended = now;
    }

    // 这场仗是不是中央已经议和退出过的旧仗(开战早于中央与对方停战)
    public static bool IsWarAlreadyLeft(Kingdom core, War war, Kingdom member)
    {
        if (core == null || war?.data == null || World.world?.diplomacy == null) return false;
        var opponents = war.isAttacker(member) ? war._list_defenders : war._list_attackers;
        foreach (Kingdom foe in opponents)
        {
            if (foe == null || foe.isRekt() || foe == core) continue;
            double ended = World.world.diplomacy.getRelation(core, foe).data.timestamp_last_war_ended;
            if (ended > 0d && war.data.created_time <= ended) return true;
        }
        return false;
    }
}

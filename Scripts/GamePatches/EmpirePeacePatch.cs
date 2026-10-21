using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
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

    public static void BeforeEnd(War pWar, WarWinner pWinner, out List<Snapshot> __state)
    {
        __state = null;
        // 同盟把两场战争并成一场(Merged)不是议和：参战各方转到合并后的战争里接着打
        if (pWinner == WarWinner.Merged) return;
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

    // 听命于中央的成员：没有自主外交权的成员，或朝廷任命(不世袭)的行政区、军府；
    // 世袭的藩镇、封国保留自主外交与兵权，正在叛乱的也不算
    public static bool Obeys(Empire empire, Kingdom member)
    {
        if (member?.data == null || member.isRekt() || member == empire.CoreKingdom || member.GetEmpire() != empire ||
            member.IsFactionRebelling() || member.IsLocalRebelling()) return false;
        Regime regime = member.GetRegime();
        if (regime == null) return false;
        // 选帝制下皇帝家族领地薄弱：比皇帝自家领国城多的诸侯不听调遣
        if (EmpireCraft.Scripts.GeneralSystems.HolyRomanSystem.Defies(empire, member)) return false;
        if (!regime.IsAllowDiplomacy()) return true;
        return member.IsAdministrativeKingdomType() && regime.GetLeaderSelectMethod() != LeaderSelectMethod.Succession;
    }

    // 随中央参战(只用于"随不随征"这一处判定：开战、有人加入时当场拉人的 FeudalVassalService.ExpandSide)：
    // 听命的成员之外，封建帝国(中央行西方封建)的诸侯在中央守土的仗里——别国打上门来、中央是主守方——也随中央参战
    // (用户 2026-10-21 选"称帝后动员不变差")。称帝前附庸网打仗是一个整体，宗主挨打附庸一齐上阵；称帝后诸侯有自主外交、
    // 不算听命，只能等王国 AI 每月逐个随征(见 CheckJoinWar)，帝国反倒比附庸网难动员。进攻战照旧(听命的当场随征，诸侯走 CheckJoinWar)。
    // 不改 Obeys 本身：议和连带停战、中央代付军饷、奉诏出征、求援与驰援都看 Obeys，改了诸侯守土时这些也会跟着变
    // (中央替诸侯付饷、调走诸侯的兵)，诸侯在这些事上的自主照旧
    public static bool FollowsIntoWar(Empire empire, Kingdom member, War war)
    {
        if (empire?.CoreKingdom == null) return false;
        if (Obeys(empire, member)) return true;
        Kingdom core = empire.CoreKingdom;
        if (war?.data == null || war.hasEnded() || member?.data == null || member.isRekt() || member == core ||
            member.GetEmpire() != empire || member.IsFactionRebelling() || member.IsLocalRebelling() ||
            member.isInWarWith(core)) return false;
        if (core.GetRegime()?.type != RegimeType.Feudalism) return false;
        // 选帝制下不听调遣的大诸侯(见 HolyRomanSystem.Defies)守土也不随征
        if (EmpireCraft.Scripts.GeneralSystems.HolyRomanSystem.Defies(empire, member)) return false;
        Kingdom attacker = war.getMainAttacker();
        return war.getMainDefender() == core && attacker?.data != null && !attacker.isRekt() && attacker.GetEmpire() != empire &&
               war.getAsset() != WarTypeLibrary.rebellion && !NotDefence.Contains(war.GetEmpireWarType());
    }

    // 中央是主守方但不算"别国打上门"的仗：劫掠来去一阵风(王国 AI 随征本来就不进劫掠之战，见 CheckJoinWar)；
    // 叛乱、独立、争位这些家里的事诸侯不必一齐上阵
    private static readonly HashSet<EmpireWarType> NotDefence = new()
    {
        EmpireWarType.劫掠, EmpireWarType.地方独立, EmpireWarType.派系叛乱, EmpireWarType.地方叛乱, EmpireWarType.民族叛乱,
        EmpireWarType.宗教叛乱, EmpireWarType.清君侧, EmpireWarType.藩王索取皇位, EmpireWarType.附庸独立, EmpireWarType.不奉诏,
        EmpireWarType.对立国王, EmpireWarType.私战
    };

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
            ClearMutualOccupation(core, foes);
            foreach (Kingdom member in snapshot.Empire.kingdoms_list.ToList())
            {
                // 听命的成员，以及应议会征召参加帝国战争的诸侯(见 HolyRomanSystem.Diet)
                if (!Obeys(snapshot.Empire, member) &&
                    !EmpireCraft.Scripts.GeneralSystems.HolyRomanSystem.InImperialWar(snapshot.Empire, member)) continue;
                bool left = false;
                foreach (War war in member.getWars().ToList())
                {
                    if (war?.data == null || !war.isAlive() || war.hasEnded()) continue;
                    var opponents = war.isAttacker(member) ? war._list_defenders : war._list_attackers;
                    if (!opponents.Any(foes.Contains)) continue;
                    war.leaveWar(member);
                    left = true;
                }
                if (left) ClearMutualOccupation(member, foes);
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

    // 停战后双方互相留下的占领(地块与攻城进度)清掉：整场战争结束时战争补丁会清，单方面退出时不会
    private static void ClearMutualOccupation(Kingdom kingdom, HashSet<Kingdom> foes)
    {
        if (kingdom == null || kingdom.isRekt()) return;
        foreach (City city in kingdom.cities.ToList())
            city?.ClearOccupationBy(occupier => foes.Contains(occupier) && !occupier.isInWarWith(kingdom));
        foreach (Kingdom foe in foes)
        {
            if (foe == null || foe.isRekt() || foe.isInWarWith(kingdom)) continue;
            foreach (City city in foe.cities.ToList())
                city?.ClearOccupationBy(occupier => occupier == kingdom);
        }
    }

    private static void SetTruce(Kingdom a, Kingdom b, double now)
    {
        if (a == null || b == null || a == b || a.isRekt() || b.isRekt()) return;
        World.world.diplomacy.getRelation(a, b).data.timestamp_last_war_ended = now;
    }

    // 这场仗是不是中央已经议和退出过的旧仗(开战早于中央与对方停战)
    public static bool IsWarAlreadyLeft(Kingdom core, War war, Kingdom member) =>
        member != null && war?.data != null && HasLeft(core, war, joiningAttackers: war.isAttacker(member));

    // 要加入某一方的国家，是否已经和另一方议和、退出过这场仗(开战早于双方停战)：
    // 自动参战(中央援助成员、周室攘夷/尊王/合纵)都不再把它拉回同一场旧仗
    public static bool HasLeft(Kingdom kingdom, War war, bool joiningAttackers)
    {
        if (kingdom == null || war?.data == null || World.world?.diplomacy == null) return false;
        var opponents = joiningAttackers ? war._list_defenders : war._list_attackers;
        foreach (Kingdom foe in opponents)
        {
            if (foe == null || foe.isRekt() || foe == kingdom) continue;
            double ended = World.world.diplomacy.getRelation(kingdom, foe).data.timestamp_last_war_ended;
            if (ended > 0d && war.data.created_time <= ended) return true;
        }
        return false;
    }
}

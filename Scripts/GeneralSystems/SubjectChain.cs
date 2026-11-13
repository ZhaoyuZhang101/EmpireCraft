using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 宗主链(用户 2026-11-13 "而且附庸还能反过来附庸")：从属关系有好几种——封建附庸(FeudalVassalService)、向帝国称臣纳贡
// (KingdomExtension.JoinTakenAlliance)、属地(ColonySystem)、傀儡(PuppetStateSystem)，帝国的成员上面还有帝国中央。
// 以前各管各的：封建附庸只查封建这一条链上有没有绕回来，朝贡、属地、傀儡不查，所以甲向乙称臣纳贡、乙又成了甲的附庸或保护国，
// 两国互为上下家。这里把几种关系当成一条链看：
//   · 和平的路子(称臣、投靠、挑附庸)：对方正(直接或隔着几层)从属于自己的，不向它称臣；
//   · 强来的(打下来的、神力、剧本、战果)：下家打赢了翻身做主，先把自己对它的那一层解开再立(见 Overturn)；
//     隔着帝国成员那一层的解不开(成员归帝国管)，就不立；
//   · 旧档里已经绕成一圈的，每年核一遍(见 Untangle)：国力强的翻身，弱的照旧在下。
public static class SubjectChain
{
    // 直接的上家：封建宗主、受贡的帝国中央、属地的宗主国、傀儡的主国、所在帝国的中央
    public static IEnumerable<Kingdom> Lords(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) yield break;
        if (FeudalVassalService.GetOverlord(kingdom) is Kingdom feudal) yield return feudal;
        if (Suzerain(kingdom) is Kingdom suzerain) yield return suzerain;
        if (ColonySystem.MetropoleOf(kingdom) is Kingdom metropole) yield return metropole;
        if (PuppetStateSystem.Master(kingdom) is Kingdom master) yield return master;
        if (Centre(kingdom) is Kingdom centre) yield return centre;
    }

    // 受它朝贡的帝国的中央
    private static Kingdom Suzerain(Kingdom kingdom)
    {
        if (!kingdom.HasTakenAlliance()) return null;
        Kingdom core = kingdom.GetTakenAllianceEmpire()?.CoreKingdom;
        return core?.data != null && core != kingdom && !core.isRekt() ? core : null;
    }

    // 帝国的成员：上面是帝国中央
    private static Kingdom Centre(Kingdom kingdom)
    {
        if (!kingdom.IsInEmpire() || kingdom.IsEmpire()) return null;
        Kingdom core = kingdom.GetEmpire()?.CoreKingdom;
        return core?.data != null && core != kingdom && !core.isRekt() ? core : null;
    }

    // kingdom 是不是(直接或隔着几层)从属于 lord
    public static bool Under(Kingdom kingdom, Kingdom lord)
    {
        if (kingdom?.data == null || lord?.data == null || kingdom == lord) return false;
        var seen = new HashSet<long> { kingdom.id };
        var queue = new Queue<Kingdom>();
        queue.Enqueue(kingdom);
        while (queue.Count > 0)
            foreach (Kingdom up in Lords(queue.Dequeue()))
            {
                if (up == lord) return true;
                if (seen.Add(up.id)) queue.Enqueue(up);
            }
        return false;
    }

    // 要立 lord 作 subject 的上家，而 lord 眼下正从属于 subject：把 lord 通到 subject 的那一层解开(翻身做主)。
    // 解完不再从属返回真；隔着帝国成员那一层的解不开，返回假(这层关系就不立了)
    public static bool Overturn(Kingdom lord, Kingdom subject)
    {
        if (lord?.data == null || subject?.data == null || lord == subject) return false;
        if (!Under(lord, subject)) return true;
        foreach (Kingdom up in Lords(lord).ToList())
        {
            if (up != subject && !Under(up, subject)) continue;
            string how = null;
            if (FeudalVassalService.GetOverlord(lord) == up)
            {
                FeudalVassalService.Break(lord);
                how = "附庸";
            }
            else if (Suzerain(lord) == up)
            {
                lord.RemoveTakenAlliance();
                how = "朝贡";
            }
            else if (ColonySystem.MetropoleOf(lord) == up)
            {
                ColonySystem.Release(lord, null);
                how = "属地";
            }
            else if (PuppetStateSystem.Master(lord) == up)
            {
                PuppetStateSystem.Release(lord, null);
                how = "傀儡";
            }
            if (how != null)
                LogService.LogInfo($"[EmpireCraft][宗主链] {lord.GetKingdomName()} 要作 {subject.GetKingdomName()} 的上家，" +
                                   $"先解开它对 {up.GetKingdomName()} 的{how}");
        }
        return !Under(lord, subject);
    }

    // 旧档里已经互为上下家的(每年核一遍，见 ColonySystem.Tick)：国力强的翻身，弱的照旧在下；强的那层解不开就解弱的
    public static void Untangle(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return;
        foreach (Kingdom up in Lords(kingdom).ToList())
        {
            if (!Under(up, kingdom)) continue;
            bool stronger = kingdom.GetNationalPower() >= up.GetNationalPower();
            Kingdom strong = stronger ? kingdom : up, weak = stronger ? up : kingdom;
            bool done = Overturn(strong, weak) || Overturn(weak, strong);
            LogService.LogInfo($"[EmpireCraft][宗主链] {kingdom.GetKingdomName()} 与 {up.GetKingdomName()} 互为上下家，" +
                               (done ? $"{strong.GetKingdomName()} 在上" : "隔着帝国成员，解不开"));
            return;
        }
    }
}

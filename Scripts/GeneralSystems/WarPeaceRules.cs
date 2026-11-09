using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;

namespace EmpireCraft.Scripts.GeneralSystems;

// 最短战期(玩家 2026-11-08"想要和平和不成"；华章 11-09"天下统一战争一开打就收岁币强制和平，然后代码限制接着开打，
// 又接着白和了"、"春秋的战争谈判系统限定开战几年后才能谈判和平，这个我觉得挺好"；用户 11-09 选"最短 3 年·保护 5 年")：
// 开战不满 MinWarYears 年不得以"议和"收场。
//   · 只拦议和(WarWinner.Peace)这一种结局。分出胜负、一方亡国、同盟并战(Merged)照旧立刻结束——
//     这条闸不会把任何一场该结束的仗卡死，最多拖到三年；
//   · 玩家强制停战(外交面板 DiplomacyActions、各神力，都在 MembershipCooldown.ByGod 作用域里)不受此限：
//     硬限制只约束 AI；
//   · 劫掠之战本就是抢够就退，剧本按史实定下的仗也照旧；
//   · 一方已经没有城了，再拖没有意义，放行。
// 闸设在 WarPatch.end_war(模组本来就在那儿拦过"索取法理未达成""去帝号未达门槛")，一处覆盖所有议和路径。
public static class WarPeaceRules
{
    public const int MinWarYears = 3;

    public static bool MayNegotiate(War war)
    {
        if (war?.data == null || war.hasEnded()) return true;
        // 玩家强制的一律放行
        if (MembershipCooldown.GodActing) return true;
        if (war.GetOrCreate().scripted_war) return true;
        if (war.GetEmpireWarType() == EmpireWarType.劫掠) return true;
        Kingdom attacker = war.main_attacker, defender = war.main_defender;
        if (attacker?.data == null || defender?.data == null || attacker.isRekt() || defender.isRekt()) return true;
        // 一方已经没城了：不是议和该管的事，放给胜负结算
        if (WarSituation.SideCities(attacker) <= 0 || WarSituation.SideCities(defender) <= 0) return true;
        return WarSituation.WarYears(war) >= MinWarYears;
    }
}

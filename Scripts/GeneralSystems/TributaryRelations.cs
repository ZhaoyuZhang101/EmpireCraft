using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 朝贡双向(用户 2026-10-19 选"朝贡双向与称臣和约"；以前朝贡国每年纳贡，宗主既不保护也不回报)：
//   · 宗主保护：朝贡国遭外敌攻打(打它的不是宗主一方的人，也不是劫掠)，宗主没在打仗、出得起军费、
//     兵力压过攻方、过得了开战闸门(停战、同盟……见 WarGate)时出兵加入守方；
//   · 厚往薄来：每次纳贡宗主回赐三成，朝贡国离心减一分；宗主得一点正统(万邦来朝)；
//   · 朝贡国与宗主算一家，科技互通(见 TechnologySystem.RebuildContacts)；
//   · 称臣和约：不行封建附庸的胜者，迫使附庸改为对方向本帝国称臣纳贡(见 WarGoalSystem.Tick)。
public static class TributaryRelations
{
    public const float ReturnGiftShare = 0.3f;
    private const float GiftLoyalty = 0.03f;

    // 王国 AI 每月一查(朝贡国自己查)
    public static void Protect(Kingdom tributary)
    {
        Empire empire = tributary?.GetTakenAllianceEmpire();
        Kingdom core = empire?.CoreKingdom;
        if (core?.data == null || core.isRekt() || empire.IsArchived() || core.hasEnemies()) return;
        War war = tributary.getWars().FirstOrDefault(candidate =>
            candidate?.data != null && candidate.isAlive() && !candidate.hasEnded() && candidate.isDefender(tributary) &&
            candidate.GetEmpireWarType() != EmpireWarType.劫掠 && !candidate.hasKingdom(core) &&
            !candidate._list_attackers.Any(kingdom => kingdom == core || kingdom?.GetEmpire() == empire ||
                                                      kingdom?.GetTakenAllianceEmpire() == empire));
        Kingdom attacker = war?.main_attacker;
        if (attacker?.data == null || attacker.isRekt() || EmpirePeacePatch.HasLeft(core, war, joiningAttackers: false)) return;
        if (WarSituation.TooBusyForWar(core) || CorpsSystem.CannotFundWar(core) ||
            WarSituation.Power(empire) <= WarSituation.Power(attacker) || !WarGate.CanDeclare(core, attacker)) return;
        war.joinDefenders(core);
        if (!war._list_defenders.Contains(core)) return;
        TranslateHelper.LogEventMessage(string.Format(LM.Get("tributary_protected"), core.GetKingdomName(),
            tributary.GetKingdomName(), attacker.GetKingdomName()), core);
    }

    // 纳贡之后(见 KingdomExtension.StartToTaken)：回赐、正统
    public static void AfterTribute(Kingdom tributary, Empire empire, int value)
    {
        Kingdom core = empire?.CoreKingdom;
        if (tributary?.data == null || core?.data == null || value <= 0) return;
        int gift = UnityEngine.Mathf.Min(UnityEngine.Mathf.RoundToInt(value * ReturnGiftShare), UnityEngine.Mathf.Max(0, core.GetMoney()));
        if (gift > 0)
        {
            core.SubMoney(gift, TreasuryCategory.Tribute);
            tributary.AddMoney(gift, TreasuryCategory.Tribute);
        }
        var extra = tributary.GetOrCreate();
        extra.leave_taken_alliance_preference = UnityEngine.Mathf.Max(0f, extra.leave_taken_alliance_preference - GiftLoyalty);
        empire.AddMandate(1);
    }

    // 称臣纳贡和约：胜者所在帝国收得了朝贡就让对方称臣；返回是否成了
    public static bool Submit(Kingdom lord, Kingdom subject)
    {
        Empire empire = lord?.GetEmpire();
        if (empire == null || subject?.data == null || subject.isRekt() || subject.IsEmpire() ||
            !FeudalConquestService.HasTributeInstitution(empire)) return false;
        subject.JoinTakenAlliance(empire, pForce: true);
        return subject.GetTakenAllianceEmpire() == empire;
    }
}

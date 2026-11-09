using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 朝贡双向(用户 2026-10-19 选"朝贡双向与称臣和约"；以前朝贡国每年纳贡，宗主既不保护也不回报)：
//   · 宗主保护：朝贡国遭外敌攻打(打它的不是宗主一方的人，也不是劫掠)，宗主没在打仗、出得起军费、
//     兵力压过攻方、过得了开战闸门(停战、同盟……见 WarGate)时出兵加入守方；
//   · 厚往薄来：每次纳贡宗主回赐三成，朝贡国离心减一分；宗主得一点正统(万邦来朝)；游牧朝贡国向定居宗主纳贡，
//     回赐一到两倍(按宗主正统，末世减半，正统不到 30 断了；见 SteppeOrderSystem 朝贡换赏赐)；
//   · 朝贡国与宗主算一家，科技互通(见 TechnologySystem.RebuildContacts)；
//   · 称臣和约：不行封建附庸的胜者，迫使附庸改为对方向本帝国称臣纳贡(见 WarGoalSystem.Tick)；
//   · 朝贡要够得着(用户 2026-11-02 转群友截图"朝贡这么多""隔这么远朝贡感觉有点夸张")：以前自愿来朝、迫使朝贡、天下来朝、
//     游牧称臣只看"与帝国接壤"，帝国连同远方的封国、附庸一起算，封国挨着的远国也来朝贡，满地图都是朝贡国。现在要宗主的直辖之地
//     (核心王国与它的行政区，不含封国、附庸、别的朝贡国)够得着(InReach：接壤、只隔着走得通的无人区、或隔海在一方水师辐射内；
//     用户 2026-11-05 选"收紧到接壤"，隔着第三国不算，以前同一块陆地上相距不过 80 格也算)；已经在朝贡的够不着满 DriftYears 年就断了(道远不通，朝贡遂绝)。
//     剧本、神力定下的朝贡照样立(不过闸)，够不着的同样过些年断了。
public static class TributaryRelations
{
    public const float ReturnGiftShare = 0.3f;
    private const float GiftLoyalty = 0.03f;
    // 够不着几年断朝贡；够不够得着隔多久重算(按年缓存)
    private const float DriftYears = 10f, ReachRecheckYears = 1f;
    // 朝贡国 → 上回量的时间、够不够得着、最早够不着的时间(不存档：读档后从头数)
    private static readonly Dictionary<long, (double at, long empire, bool reach, double farSince)> Reach = new();

    public static void ResetWorldState() => Reach.Clear();

    // 宗主的直辖之地：核心王国与它的行政区(律令制的道、都护府，行省、总督区……见 IsAdministrativeKingdomType)
    public static List<Kingdom> Direct(Empire empire)
    {
        var direct = new List<Kingdom>();
        Kingdom core = empire?.CoreKingdom;
        if (core?.data == null || core.isRekt()) return direct;
        direct.Add(core);
        if (empire.kingdoms_list != null)
            foreach (Kingdom member in empire.kingdoms_list)
                if (member?.data != null && !member.isRekt() && member != core && member.IsAdministrativeKingdomType()) direct.Add(member);
        return direct;
    }

    // 宗主直辖之地够不够得着这一国(见类头)
    public static bool InReach(Kingdom tributary, Empire empire) =>
        tributary?.data != null && !tributary.isRekt() && empire != null && !empire.isRekt() && !empire.IsArchived() &&
        WarReach.TouchingFrom(Direct(empire), tributary);

    // 已经在朝贡的：够不着满 DriftYears 年(见 KingdomExtension.NeedToRemoveTakenAlliance)。一年量一次
    public static bool DriftedAway(Kingdom tributary, Empire empire)
    {
        if (tributary?.data == null || empire == null) return false;
        double now = World.world.getCurWorldTime();
        bool known = Reach.TryGetValue(tributary.id, out var state) && state.empire == empire.id;
        bool fresh = known && now >= state.at && Date.getYearsSince(state.at) < ReachRecheckYears;
        if (!fresh)
        {
            bool reach = InReach(tributary, empire);
            double farSince = reach ? -1d : known && state.farSince >= 0d && now >= state.farSince ? state.farSince : now;
            state = (now, empire.id, reach, farSince);
            Reach[tributary.id] = state;
        }
        if (state.reach || state.farSince < 0d || Date.getYearsSince(state.farSince) < DriftYears) return false;
        Reach.Remove(tributary.id);
        LogService.LogInfo($"[EmpireCraft][朝贡] {tributary.GetKingdomName()} 与{empire.GetEmpireName()}道远不通已满 {DriftYears} 年，不再朝贡");
        return true;
    }

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
        float share = SteppeOrderSystem.TributeGiftShare(tributary, empire) ?? ReturnGiftShare;
        int gift = UnityEngine.Mathf.Min(UnityEngine.Mathf.RoundToInt(value * share), UnityEngine.Mathf.Max(0, core.GetMoney()));
        if (gift > 0)
        {
            core.SubMoney(gift, TreasuryCategory.Tribute);
            tributary.AddMoney(gift, TreasuryCategory.Tribute);
        }
        SteppeOrderSystem.NoteGift(tributary, empire, gift);
        var extra = tributary.GetOrCreate();
        extra.leave_taken_alliance_preference = UnityEngine.Mathf.Max(0f, extra.leave_taken_alliance_preference - GiftLoyalty);
        empire.AddMandate(1);
    }

    // 西方没有朝贡(用户 2026-10-27 截图乌拉尔斯克"朝贡于神圣罗马帝国"："西方没有朝贡")：行封建附庸的、统治文化或制度文化
    // 属罗马一系的帝国一律不收朝贡国(强制也不行，见 KingdomExtension.JoinTakenAlliance)，已有的在例行检查时解除
    // (见 NeedToRemoveTakenAlliance)。以前只看帝国主文化施行没施行带"迫使朝贡"的制度，维京统治、兼采斯拉夫之制的
    // 复合帝国按主文化取到了游牧一系的制度，照样收朝贡
    public static bool IsWestern(Empire empire)
    {
        Kingdom core = empire?.CoreKingdom;
        if (core?.data == null || core.isRekt()) return false;
        if (FeudalVassalService.UsesFeudalVassals(core)) return true;
        return CultureService.GetRealmCulture(core).GetInstitutionLine() == "Roma" ||
               CultureService.GetInstitutionCulture(core).GetInstitutionLine() == "Roma";
    }

    // 称臣纳贡和约：胜者所在帝国收得了朝贡就让对方称臣；返回是否成了
    public static bool Submit(Kingdom lord, Kingdom subject)
    {
        // 世界法则「禁止AI朝贡」(见 DiplomacyLaws)：玩家在外交面板让其称臣不经这里
        if (DiplomacyLaws.TributeBlocked) return false;
        Empire empire = lord?.GetEmpire();
        if (empire == null || subject?.data == null || subject.isRekt() || subject.IsEmpire() ||
            !FeudalConquestService.HasTributeInstitution(empire)) return false;
        subject.JoinTakenAlliance(empire, pForce: true);
        return subject.GetTakenAllianceEmpire() == empire;
    }
}

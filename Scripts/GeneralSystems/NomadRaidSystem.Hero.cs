using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 出雄主与失败内讧：
//   · 盟主威望(SteppeState.prestige)：率众劫掠得手(抢到过、或被买和)一回 +1；被打退清零，首领本人威望 −RepulsedRenown；
//     换了首领从零算。单国劫掠也记威望，只有联盟得手后才掷成雄主——机会见 SteppeRules.HeroChance(威望 2 起 10%，每回 +10%，至多 60%)；
//   · 成雄主(下个月办，不在战争收尾那一帧里改帝国)：盟主还不是帝国的，称汗建国(EmpireManager.NewEmpire + 称帝收尾，按武力霸权)，
//     首领即开国雄主(RulerTraitSystem.Founder)；已是汗国核心的，大汗得开国雄主之号。这一回出过兵的各部(独立的游牧政权，
//     原始部落、别家帝国里的不算)并入汗国为兀鲁思。之后照游牧难一统的规矩走：只有雄主在位能发动统一战争、可南下入主，
//     雄主一死第一回换汗必乱(见 UnificationSystem、NomadCycleSystem)；部落阶段(原始政体)的盟主只攒威望，不称汗；
//   · 失败内讧：联盟被打退，各部每家 InfightChance 的机会反目——本汗国的兀鲁思离心而去(退出汗国)，独立的部与盟主兵戎相见
//     (一场争草场的小仗，见 SteppeOrderSystem.Rift；同一回至多一场)，同样下个月办。
public static partial class NomadRaidSystem
{
    private const int RepulsedRenown = 30;
    private const float InfightChance = 0.3f;

    static partial void AfterRaid(War war, RaidWarState state, Kingdom leader, List<Kingdom> members, bool success)
    {
        if (leader == null) return;
        SteppeState steppe = State(leader);
        long ruler = leader.king?.id ?? -1L;
        if (steppe.prestige_ruler_id != ruler)
        {
            steppe.prestige = 0;
            steppe.prestige_ruler_id = ruler;
        }
        bool league = state.members.Count > 1;
        if (!success)
        {
            int before = steppe.prestige;
            steppe.prestige = 0;
            leader.king?.editRenown(-RepulsedRenown);
            if (league)
                steppe.rift_members = members.Where(member => member != leader && UnityEngine.Random.value < InfightChance)
                    .Select(member => member.id).ToList();
            LogService.LogInfo($"{LogTag} 盟主{state.leader_name} 劫掠被打退，威望{before}→0" +
                               (steppe.rift_members?.Count > 0 ? $"，{steppe.rift_members.Count}部将反目" : ""));
            return;
        }
        steppe.prestige++;
        float chance = league && HeroEligible(leader) ? SteppeRules.HeroChance(steppe.prestige) : 0f;
        bool hero = chance > 0f && UnityEngine.Random.value < chance;
        if (hero)
        {
            steppe.hero_pending = true;
            steppe.hero_members = members.Where(member => member != leader).Select(member => member.id).ToList();
        }
        LogService.LogInfo($"{LogTag} 盟主{state.leader_name} 劫掠得手，威望{steppe.prestige}，" +
                           (league ? $"雄主机会{chance:0.00}{(hero ? "，成雄主" : "")}" : "单国劫掠不掷雄主"));
    }

    // 能成雄主：游牧、已走出部落阶段、一方之主、君主制，首领成年在位，还不是雄主
    private static bool HeroEligible(Kingdom leader)
    {
        if (!IsRealm(leader) || !IsNomad(leader) || TribalFissionService.IsTribal(leader)) return false;
        Actor khan = leader.king;
        if (khan == null || !khan.isAlive() || !khan.isAdult() || RulerTraitSystem.FounderReigns(leader)) return false;
        if (!RegimeManager.IsMonarchy(leader.GetRegime()?.type)) return false;
        Empire empire = leader.GetEmpire();
        return empire == null || !RepublicSystem.IsRepublic(empire);
    }

    // 每月：待办的成雄主、内讧
    private static void Aftermath()
    {
        foreach (Kingdom kingdom in World.world.kingdoms.list.ToList())
        {
            SteppeState steppe = kingdom?.data == null || kingdom.isRekt() ? null : kingdom.GetOrCreate().steppe;
            if (steppe == null) continue;
            try
            {
                if (steppe.hero_pending) PromoteHero(kingdom, steppe);
                if (steppe.rift_members?.Count > 0) Rift(kingdom, steppe);
            }
            catch (Exception exception)
            {
                steppe.hero_pending = false;
                steppe.rift_members = null;
                LogService.LogWarning($"{LogTag} {kingdom.GetKingdomName()} 劫掠善后失败: {exception.Message}");
            }
        }
    }

    // 盟主成雄主：称汗建国(或大汗得雄主之号)，这回出兵的各部并入为兀鲁思
    private static void PromoteHero(Kingdom leader, SteppeState steppe)
    {
        steppe.hero_pending = false;
        List<long> followers = steppe.hero_members ?? new List<long>();
        steppe.hero_members = null;
        if (!HeroEligible(leader) || ModClass.EMPIRE_MANAGER == null) return;
        Actor khan = leader.king;
        Empire empire = leader.GetEmpire();
        bool founded = empire == null;
        if (founded)
        {
            empire = ModClass.EMPIRE_MANAGER.NewEmpire(leader, allowCultureRival: true);
            if (empire == null)
            {
                LogService.LogInfo($"{LogTag} {leader.GetKingdomName()} 盟主{khan.getName()} 该成雄主，称汗不成(建帝国失败)");
                return;
            }
            EmpireFormationService.CompleteFormation(empire, leader, EmpireFormationRoute.Hegemony);
        }
        else if (empire.CoreKingdom != leader) return;
        if (khan != null && khan.isAlive() && !khan.hasTrait(RulerTraitSystem.Founder)) khan.addTrait(RulerTraitSystem.Founder);
        var joined = new List<string>();
        foreach (long id in followers)
        {
            Kingdom member = LiveKingdom(id);
            if (!Alive(member) || member == leader || member.GetEmpire() == empire || member.IsEmpire() || member.IsInEmpire() ||
                !IsNomad(member) || TribalFissionService.IsTribal(member) || MembershipCooldown.BlocksJoin(member, empire)) continue;
            if (member.HasTakenAlliance()) member.RemoveTakenAlliance();
            if (member.hasAlliance()) member.getAlliance().leave(member);
            empire.join(member, true, pForce: true);
            if (member.GetEmpire() != empire) continue;
            EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(member);
            joined.Add(member.GetKingdomName());
        }
        steppe.prestige = 0;
        string text = founded
            ? string.Format(LM.Get("nomad_raid_hero_khanate_history"), leader.GetKingdomName(), khan?.getName() ?? "",
                empire.GetEmpireName())
            : string.Format(LM.Get("nomad_raid_hero_history"), khan?.getName() ?? "", empire.GetEmpireName());
        if (joined.Count > 0) text += string.Format(LM.Get("nomad_raid_hero_followers"), string.Join("、", joined));
        Chronicle(text, new[] { leader });
        LogService.LogInfo($"{LogTag} 雄主出：{khan?.getName()}({leader.GetKingdomName()}){(founded ? "称汗建国" : "得雄主之号")}" +
                           $"{empire.GetEmpireName()}，并入{joined.Count}部");
    }

    // 失败内讧：兀鲁思离心而去，独立的部与盟主兵戎相见(至多一场)
    private static void Rift(Kingdom leader, SteppeState steppe)
    {
        List<long> rivals = steppe.rift_members;
        steppe.rift_members = null;
        Empire own = leader.GetEmpire();
        bool fought = false;
        foreach (long id in rivals)
        {
            Kingdom member = LiveKingdom(id);
            if (!Alive(member) || member == leader) continue;
            if (own != null && member.GetEmpire() == own && own.CoreKingdom == leader)
            {
                if (member.IsFactionRebelling() || member.IsLocalRebelling() || !RebellionSystem.CanAttempt(member)) continue;
                own.leave(member, true, true);
                if (member.GetEmpire() == own) continue;
                Chronicle(string.Format(LM.Get("nomad_raid_rift_leave_history"), member.GetKingdomName(), own.GetEmpireName()),
                    new[] { member, leader });
                continue;
            }
            if (fought || !SteppeOrderSystem.Rift(member, leader)) continue;
            fought = true;
        }
    }
}

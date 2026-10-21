using System;
using System.Collections.Generic;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.Diagnostics;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NCMS.Extensions;
using NeoModLoader.api.attributes;
using NeoModLoader.General.Game.extensions;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.KingdomAI;

public class EmpireCraftKingdomBehCheckPlots : GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();
    public override BehResult execute(Kingdom pKingdom)
    {
        pKingdom.CheckEmpire();
        var ked = pKingdom.GetOrCreate();
        if (ked != null && ked.last_plots_check_ts > 0)
        {
            if (Date.getMonthsSince(ked.last_plots_check_ts) < 1)
            {
                return BehResult.Continue;
            }
        }
        if (pKingdom.IsInEmpire())
        {
            CheckJoinWar(pKingdom);
        }
        // 朝贡国遭外敌攻打，宗主出兵保护(见 TributaryRelations)
        if (pKingdom.HasTakenAlliance()) TributaryRelations.Protect(pKingdom);
        // 跨文明通好之国遭第三国攻打，依约出兵(见 GreatPowerDiplomacy)
        if (pKingdom.IsEmpire()) GreatPowerDiplomacy.Aid(pKingdom);

        CheckProgress(pKingdom);
        CheckPowerfulMinisterPlot(pKingdom);
        //检测加入圣战
        CheckJoinReligionWar(pKingdom);
        CheckMainTitle(pKingdom);
        CheckRebellionWar(pKingdom);
        if (ked != null) ked.last_plots_check_ts = World.world.getCurWorldTime();
        return BehResult.Continue;
    }

    private static void CheckPowerfulMinisterPlot(Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        if (empire?.CoreKingdom != kingdom) return;
        Actor minister = empire.GetPowerfulMinister();
        if (minister == null || minister.isRekt() || minister.plot?.isActive() == true) return;

        string plotId = empire.CanEmpressDowagerInstallSon(minister)
            ? "empress_dowager_install_son"
            : empire.CanPowerfulMinisterUsurp(minister)
                ? "minister_acquire_empire"
                : empire.CanPowerfulMinisterReceiveNineBestowments(minister)
                    ? "minister_receive_nine_bestowments"
                    : empire.CanPowerfulMinisterSeekDukedom(minister)
                        ? "minister_acquire_title"
                        : null;
        if (plotId == null) return;

        PlotAsset plot = AssetManager.plots_library?.basic_plots?.Find(asset => asset?.id == plotId);
        if (plot?.try_to_start_advanced == null) return;
        if (plot.try_to_start_advanced(minister, plot, true))
        {
            EmpireCraftDebugProbe.Hit("powerful_minister.plot_started", () =>
                $"empire={empire.GetEmpireFullName()}({empire.id}), minister={minister.getName()}" +
                $"({minister.id}), stage={empire.data.powerful_minister_stage}, plot={plotId}");
        }
    }
    public void CheckMainTitle(Kingdom pKingdom)
    {
        pKingdom.ReconcileMainTitle();
    }
    public void CheckProgress(Kingdom pKingdom)
    {
        if (!pKingdom.hasEnemies())
        {
            pKingdom.EndFactionRebelling();
            pKingdom.EndLocalRebelling();
        }
        pKingdom.PushProgress();
    }
    public void CheckRebellionWar(Kingdom pKingdom)
    {
        if (!pKingdom.hasEnemies()) return;
        var war = pKingdom.getWars().ToList().Find(w => w.main_attacker == pKingdom&&w.GetEmpireWarType() == EmpireWarType.地方叛乱);
        var empire = war?.GetEmpireTarget();
        if  (empire == null) return;
        var faction = war.GetEmpireFaction();
        var rebelData = pKingdom.GetOrCreate();
        if (TryEndExclaveRebellionWar(pKingdom, war, rebelData)) return;
        // 分封制下的小乱：别的诸侯不来投(见 PettyRebellion)
        if (EmpireCraft.Scripts.GeneralSystems.PettyRebellion.IsPetty(pKingdom)) return;
        if (rebelData.rebellion_auto_expand_remaining < 0)
        {
            rebelData.rebellion_auto_expand_remaining = (int)Math.Ceiling(pKingdom.getMaxCities() * 1.5d);
        }
        foreach (var k in empire.kingdoms_list.ToList())
        {
            if (k == null || k.isRekt() || !k.hasKing() || k.king == null) continue;
            if (k.IsEmpire()) continue;
            if (!RebellionSystem.CanAttempt(k) || !CityStabilitySystem.CanRise(k.capital)) continue;
            if (k == pKingdom) continue;
            if (!k.king.HasFaction()) continue;
            if (k.GetHighestFactionRatio()!=faction) continue;
            if (k.getRenown()>pKingdom.getRenown()) continue;
            int joiningCityCount = k.cities?.Count ?? 0;
            if (joiningCityCount <= 0 || rebelData.rebellion_auto_expand_remaining < joiningCityCount) continue;
            pKingdom.addRenown(-k.getRenown());
            TranslateHelper.LogJoinRebellionWar(k, pKingdom, empire);
            foreach (var c in k.cities.ToList())
            {
                c.joinAnotherKingdom(pKingdom);
            }
            rebelData.rebellion_auto_expand_remaining -= joiningCityCount;
            if (rebelData.rebellion_auto_expand_remaining <= 0) return;
        }
        
    }

    private static bool TryEndExclaveRebellionWar(Kingdom rebels, War war,
        KingdomExtension.KingdomExtraData rebelData)
    {
        if (rebels == null || war == null || rebelData == null || !rebelData.rebellion_origin_city_isolated ||
            rebelData.rebellion_origin_city_value < 0) return false;
        double now = World.world.getCurWorldTime();
        if (rebelData.last_rebellion_exclave_disengagement_roll >= 0 &&
            Date.getYearsSince(rebelData.last_rebellion_exclave_disengagement_roll) < 1) return false;
        rebelData.last_rebellion_exclave_disengagement_roll = now;

        CityValueSnapshot origin = new CityValueSnapshot
        {
            Total = rebelData.rebellion_origin_city_value,
            IsIsolated = true
        };
        float chance = CityValueRules.GetExclaveDisengagementChance(origin, rebels.countCities());
        if (chance <= 0f || !Randy.randomChance(chance)) return false;
        World.world.wars.endWar(war, WarWinner.Peace);
        return true;
    }
    public void CheckJoinReligionWar(Kingdom pKingdom)
    {
        if (!pKingdom.hasReligion()) return;
        if (pKingdom.hasEnemies()) return;
        if (pKingdom.getWars().Count()>0) return;
        foreach (var war in DiplomacyHelpers.wars)
        {
            if (!war.main_attacker?.hasReligion()??true) continue;
            if (war.main_defender.isRekt()) continue;
            if (war.hasKingdom(pKingdom)) continue;
            if (war.getAttackers().Contains(pKingdom)) continue;
            if (war.GetEmpireWarType() == EmpireWarType.神圣)
            {
                if (pKingdom.religion == war.main_attacker?.religion)
                {
                    // 圣战加上限与距离(见 ReligiousTension.CanJoinHolyWar)
                    if (pKingdom.isOpinionTowardsKingdomGood(war.main_attacker) && ReligiousTension.CanJoinHolyWar(pKingdom, war))
                    {
                        war.joinAttackers(pKingdom);
                        TranslateHelper.LogJoinReligionWar(pKingdom, pKingdom.religion);
                        return;
                    }
                }
            }
        }
    }
    
    public void CheckJoinWar(Kingdom pKingdom)
    {
        Empire empire = pKingdom.GetEmpire();
        if (empire == null || empire.CoreKingdom == null || empire.CoreKingdom.isRekt()) return;
        var regime = pKingdom.GetRegime();
        if (regime == null) return;
        // 成员挨打向中央求援、邻近成员挨打议驰援(中央不再自动援助成员，见 MemberWarAid)
        if (!pKingdom.IsEmpire()) MemberWarAid.TryRaise(pKingdom);
        if (empire.CoreKingdom.getWars().Any(w=>w.GetEmpireWarType()== EmpireWarType.藩王索取皇位)) return;
        // 帝国一级的冷却(timestamp_invite_war_cool_down)照旧看：称帝之初、中央应成员求援出兵后三个月内成员不随征
        // (见 Empire.CreateNewEmpire、MemberWarAid.AnswerRequest)。成员随征不再记在帝国上，改记在各成员自己身上，见 JoinCoolingDown
        if (!empire.CanJoinWar()) return;
        if (JoinCoolingDown(pKingdom)) return;
        // 战国：诸侯已称王自立，天子与诸侯不再自动互相参战(合纵连横见 ZhouEraSystem)
        if (ZhouEraSystem.Era(empire) >= ZhouEraSystem.Warring) return;
        if (empire.isRekt() || pKingdom.IsEmpire()) return;
        var coreKingdom = empire.CoreKingdom;
        if (pKingdom.isInWarWith(coreKingdom)) return;
        if (pKingdom.isInWarOnSameSide(coreKingdom)) return;
        if (pKingdom.getWars().Any()) return;
        if (!coreKingdom.hasEnemies()) return;
        // 听命于中央的成员(行政区、军府等没有自主外交权的)是中央的一部分，中央开战一律随征，不看好感。
        // 有自主外交的诸侯这里本来就放行(进攻、守土都随征，只要自己没在打仗、不在冷却)；封建帝国别国打上门时诸侯开战当时就随征，
        // 不等这里每月一查(用户 2026-10-21 选"称帝后动员不变差"，见 EmpirePeacePatch.FollowsIntoWar、FeudalVassalService.ExpandSide)
        if (!GamePatches.EmpirePeacePatch.Obeys(empire, pKingdom) &&
            !pKingdom.isOpinionTowardsKingdomGood(coreKingdom) && !regime.IsAllowDiplomacy()) return;
        bool joinedAny = false;
        foreach (War w in coreKingdom.GetWarsCached(true).ToArray())
        {
            // 中央援助成员的仗不随征(邻近成员要议驰援，见 MemberWarAid)
            if (MemberWarAid.IsMemberWar(w, empire, w.isAttacker(coreKingdom))) continue;
            joinedAny |= TryJoinWarOnSameSide(w, coreKingdom, pKingdom);
        }
        if (joinedAny)
        {
            // 西欧封建帝国按成员各自记；别的帝国照旧记在帝国上、一家随征后全帝国三个月(用户只给西欧放开，
            // 免得阿拉伯的世袭埃米尔、游牧兀鲁思随征也变快，抵消削弱阿拉伯)
            if (PerMemberJoin(empire)) JoinedWarAt[pKingdom.id] = World.world.getCurWorldTime();
            else empire.data.timestamp_invite_war_cool_down = World.world.getCurWorldTime();
            TranslateHelper.LogJoinEmpireWar(pKingdom, empire);
        }
    }

    // 成员随中央参战的冷却：西欧封建帝国每个成员各自三个月(用户 2026-10-21 选"称帝后动员不变差")。以前记在帝国上
    // (timestamp_invite_war_cool_down)，一家成员随征后全帝国三个月内别家都不随征——称帝前附庸网是一齐参战的，
    // 称帝后反倒每三个月才进来一家。不存档：读档、换世界时清掉(见 ResetWorldState)，读档后各成员可以马上随征
    private static readonly Dictionary<long, double> JoinedWarAt = new();
    private const int JoinWarCooldownMonths = 3;

    public static void ResetWorldState() => JoinedWarAt.Clear();

    private static bool PerMemberJoin(Empire empire) => empire?.CoreKingdom?.GetRegime()?.type == RegimeType.Feudalism;

    private static bool JoinCoolingDown(Kingdom kingdom)
    {
        if (!JoinedWarAt.TryGetValue(kingdom.id, out double at)) return false;
        // 记的时间晚于现在(换了世界没清到)按没有记过算
        if (World.world.getCurWorldTime() >= at && Date.getMonthsSince(at) < JoinWarCooldownMonths) return true;
        JoinedWarAt.Remove(kingdom.id);
        return false;
    }

    private static bool TryJoinWarOnSameSide(War war, Kingdom sideKingdom, Kingdom joiningKingdom)
    {
        if (war == null ||
            sideKingdom == null ||
            joiningKingdom == null ||
            joiningKingdom.isRekt() ||
            !war.isAlive() ||
            war.hasEnded() ||
            war.GetEmpireWarType() == EmpireWarType.劫掠 ||
            IsWarMember(war, joiningKingdom) ||
            // 春秋战国：周室诸侯之间的征伐不拉天子与别的诸侯下场(犯天子的勤王见 ZhouEraSystem)
            ZhouEraSystem.IsInternalWar(war))
        {
            return false;
        }

        if (war.isAttacker(sideKingdom))
        {
            war.joinAttackers(joiningKingdom);
        }
        else if (war.isDefender(sideKingdom))
        {
            war.joinDefenders(joiningKingdom);
        }
        else
        {
            return false;
        }

        // joinAttackers/joinDefenders 可能因战争正在结算而拒绝加入。
        // 只有最终真的出现在阵营名单中，才允许播报和写入历史。
        return IsWarMember(war, joiningKingdom);
    }

    private static bool IsWarMember(War war, Kingdom kingdom)
    {
        if (war == null || kingdom == null)
            return false;

        return (war._list_attackers != null && war._list_attackers.Contains(kingdom)) ||
               (war._list_defenders != null && war._list_defenders.Contains(kingdom));
    }
}

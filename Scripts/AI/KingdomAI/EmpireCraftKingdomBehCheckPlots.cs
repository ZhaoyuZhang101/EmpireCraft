using System;
using System.Collections.Generic;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.Diagnostics;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
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
        if (rebelData.rebellion_auto_expand_remaining < 0)
        {
            rebelData.rebellion_auto_expand_remaining = (int)Math.Ceiling(pKingdom.getMaxCities() * 1.5d);
        }
        foreach (var k in empire.kingdoms_list.ToList())
        {
            if (k == null || k.isRekt() || !k.hasKing() || k.king == null) continue;
            if (k.IsEmpire()) continue;
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
                    if (pKingdom.isOpinionTowardsKingdomGood(war.main_attacker))
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
        if (empire.CoreKingdom.getWars().Any(w=>w.GetEmpireWarType()== EmpireWarType.藩王索取皇位)) return;
        if (!empire.CanJoinWar()) return;
        if (!empire.isRekt())
        {
            if (!pKingdom.IsEmpire())
            {
                var coreKingdom = empire.CoreKingdom;
                if (pKingdom.isInWarWith(coreKingdom)) return;
                if (pKingdom.isInWarOnSameSide(coreKingdom)) return;
                if (pKingdom.getWars().Any()) return;
                if (!coreKingdom.hasEnemies()) return;
                if (pKingdom.isOpinionTowardsKingdomGood(coreKingdom) || regime.IsAllowDiplomacy())
                {
                    var wars = coreKingdom.GetWarsCached(true);
                    var enumerable = wars.ToArray();
                    bool joinedAny = false;
                    for (int i = 0; i < enumerable.Count(); i++)
                    {
                        var w = enumerable[i];
                        joinedAny |= TryJoinWarOnSameSide(w, coreKingdom, pKingdom);
                    }
                    if (joinedAny)
                    {
                        empire.data.timestamp_invite_war_cool_down = World.world.getCurWorldTime();
                        TranslateHelper.LogJoinEmpireWar(pKingdom, empire);
                    }
                } 
            }
            else
            {
                var ks = empire.kingdoms_list;
                for (int x = 0; x < ks.Count; x++)
                {
                    var empireKingdom = ks[x];
                    if (empireKingdom.IsEmpire()) continue;
                    if (pKingdom.isInWarWith(empireKingdom)) continue;
                    if (pKingdom.isInWarOnSameSide(empireKingdom)) continue;
                    if (!empireKingdom.hasEnemies())  continue;
                    var kRegime = empireKingdom.GetRegime();
                    if (kRegime == null) continue;
                    if (!pKingdom.isOpinionTowardsKingdomGood(empireKingdom)&&regime.IsAllowDiplomacy()) continue;
                    var wars2 = empireKingdom.GetWarsCached(true);
                    var enumerable = wars2.ToArray();
                    bool joinedMemberWar = false;
                    for (int i = 0; i < enumerable.Count(); i++)
                    {
                        var w = enumerable[i];
                        joinedMemberWar |= TryJoinWarOnSameSide(w, empireKingdom, pKingdom);
                    }
                    if (joinedMemberWar)
                    {
                        empire.data.timestamp_invite_war_cool_down = World.world.getCurWorldTime();
                        TranslateHelper.LogEmpireJoinWar(empire, empireKingdom);
                    }
                }
            }
        }
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
            IsWarMember(war, joiningKingdom))
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

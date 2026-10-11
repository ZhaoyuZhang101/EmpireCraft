using System;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.KingdomAI;

public class EmpireCraftKingdomBehCheckEmpire:GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Kingdom pKingdom)
    {
        pKingdom.CheckEmpire();
        CheckPossible(pKingdom);
        if (pKingdom.IsEmpire())
        {
            SyncData(pKingdom);
            CalcMilitaryExpenditure(pKingdom);
            CheckCorruption(pKingdom);
        }

        if (pKingdom.IsNeedToTaken())
        {
            pKingdom.StartToTaken();
        }
        
        CheckEmpireAlliance(pKingdom);
        return BehResult.Continue;
    }
    /// <summary>
    /// 帝国破产后十年直接解散
    /// </summary>
    /// <param name="pKingdom"></param>
    public static void CheckCorruption(Kingdom pKingdom)
    {
        if (pKingdom.IsEmpire())
        {
            Empire empire = pKingdom.GetEmpire();
            if (pKingdom.GetMoney() < 0)
            {
                if (!pKingdom.IsStartCorrupting())
                {
                    pKingdom.StartCorrupting();
                }
            }
            else
            {
                pKingdom.EndCorrupting();
            }

            // 亏空的正统影响由治理来源/年度破产结算负责，不能每次 AI 检查都再扣一次。
        }
    }
    /// <summary>
    /// 帝国与核心国家同步数据
    /// </summary>
    /// <param name="pKingdom"></param>
    public void SyncData(Kingdom pKingdom)
    {
        //同步天子
        Empire empire = pKingdom.GetEmpire();
        if (empire == null || empire.isRekt() || empire.IsArchived())  return;
        foreach (var kingdom in empire.kingdoms_list)
        {
            if (kingdom.IsEmpire()) continue;
            kingdom.SetEmpireID(empire.id);
        }
        if (string.IsNullOrEmpty(empire.GetEmpireName()))
        {
            if (pKingdom.hasKing()&&pKingdom.king.hasCulture())
            {
                empire.data.directPre = "";
                var nameEmpire = pKingdom.king.culture.getOnomasticData(MetaType.Kingdom).generateName();
                empire.SetEmpireName(nameEmpire);
                if (pKingdom.king.HasSpecificClan())
                {
                    pKingdom.king.GetSpecificClan().RecordHistoryEmpire(empire, pKingdom.capital);
                }
            }
        }

        if (pKingdom.hasKing())
        {
            if (pKingdom.king.HasSpecificClan())
            {
                var specificClan = pKingdom.king.GetSpecificClan();
                if (!specificClan.HasHistoryEmpire())
                {
                    specificClan.RecordHistoryEmpire(empire, pKingdom.capital);
                }
            }
        }
        if (!pKingdom.IsEmpire())
        {
            pKingdom.GetRegime().GetPlayerFactions().ForEach(f=>f.BanFaction());
        }
        else
        {
            pKingdom.GetRegime().GetPlayerFactions().ForEach(f =>
            {
                f.Ban = false;
                f.Update();
            });
        }
    }
    /// <summary>
    /// 每年计算一次军费，使用最近十年的财政记录
    /// </summary>
    /// <param name="pKingdom"></param>
    public void CalcMilitaryExpenditure(Kingdom pKingdom)
    {
        Empire empire = pKingdom.GetEmpire();
        if (empire == null || empire.isRekt() || empire.IsArchived()) return;
        var core = empire.CoreKingdom;
        if (core == null || World.world == null) return;
        double now = World.world.getCurWorldTime();
        if (empire.data.last_military_expenditure_timestamp >= 0d &&
            now >= empire.data.last_military_expenditure_timestamp &&
            Date.getYearsSince(empire.data.last_military_expenditure_timestamp) < 1) return;
        empire.data.last_military_expenditure_timestamp = now;
        if (TreasurySystem.Enabled(core))
        {
            // 城市月度驻军/野战军账单已实际扣款，年度只汇总，不能再扣旧公式军费。
            var fiscal = TreasurySystem.Consolidated(core);
            fiscal.expense_sources.TryGetValue(nameof(TreasuryCategory.Military), out long fieldArmy);
            fiscal.expense_sources.TryGetValue(nameof(TreasuryCategory.Garrison), out long garrisons);
            empire.data.MilitaryExpenditure = (int)Math.Min(int.MaxValue, fieldArmy + garrisons);
            if (empire.IsNeedToGive()) empire.StartToGive();
            return;
        }
        //计算军费
        // 追加当年财政数据
        empire.data.PreviousYearsMoney.Add(empire.CurrentMoney);

        // 始终只保留最近十年
        while (empire.data.PreviousYearsMoney.Count > 10)
            empire.data.PreviousYearsMoney.RemoveAt(0);
        
        if (empire.data.PreviousYearsMoney.Count==10)
        {
            var years = empire.data.PreviousYearsMoney;   // 最近若干年的财政记录（单位同 CurrentMoney）
            var rate = empire.data.MilitaryExpenditureRate;
            //计算军费
            double avg4 = years.Average();
            double avg3 = years.Take(years.Count() - 1).Average();
            double growthAvg = Math.Max(0, avg4 - avg3);
            int militaryCost = (int)(growthAvg  * rate);
            empire.data.MilitaryExpenditure = militaryCost;
            core.SubMoney(militaryCost, TreasuryCategory.Military);
            if (core.hasEnemies())
            {
                // 战时军费：旧版 兵力/4×战争数 极易打到负债扣正统，现为 兵力/10，最多按两场战争计
                var warExpend = (empire.countWarriors() / 10) * Math.Min(2, core.getWars().Count());
                core.SubMoney(warExpend, TreasuryCategory.Military);
            }

            var jiedushis = empire.kingdoms_list.FindAll(k => k.GetKingdomType() == KingdomType.LvLing_jiedushi);
            if (jiedushis.Any())
            {
                //军府维护金
                var junfuMoney = jiedushis.Sum(k => k.countTotalWarriors());
                core.SubMoney(junfuMoney, TreasuryCategory.Military);
            }
        }
        if (empire.IsNeedToGive())
        {
            empire.StartToGive();
        }
    }
    /// <summary>
    /// 判断是否称帝，同时检测出现错误的帝国并予以消灭
    /// </summary>
    /// <param name="pKingdom"></param>
    /// <returns></returns>
    public void CheckPossible(Kingdom pKingdom)
    {
        if (pKingdom.isRekt()) return;
        Empire empire = pKingdom.GetEmpire();
        if (empire != null)
        {
            var coreKingdom = empire.CoreKingdom;
            if (coreKingdom == null || coreKingdom.isRekt())
            {
                empire.TryRepairState("CheckPossible: core kingdom invalid");
                return;
            }
            if (!empire.kingdoms_list.Contains(coreKingdom))
            {
                empire.TryRepairState("CheckPossible: core kingdom missing from empire list");
                return; 
            }
            if (pKingdom == empire.CoreKingdom && pKingdom.hasKing() &&
                CompositeEmpireService.CanAdoptCentralInstitutions(pKingdom.king))
            {
                var adoptionPlot = AssetManager.plots_library.basic_plots
                    .Find(p => p.id == "adopt_central_plains_institutions");
                if (adoptionPlot?.try_to_start_advanced?.Invoke(pKingdom.king, adoptionPlot, true) == true)
                {
                    TranslateHelper.LogCompositeEmpireAdoptionStarted(empire,
                        CompositeEmpireService.GetAdoptionStatus(empire));
                    return;
                }
            }
        }
        // 互为正统对手(僭越称帝后并立)的帝国：较强一方可发起正统之争
        if (empire != null && pKingdom == empire.CoreKingdom &&
            ImperialLegitimacyChallengeService.TryStartRivalryWar(empire)) return;
        // Modern sovereign states enter the empire layer directly. This is state formation,
        // not a monarch's proclamation plot, and therefore ignores pre-modern rise routes.
        // 本文化已废除君主制：独立的君主国(不在帝国里)改行现代政体，之后按现代国家组建政府或为军阀，不再有称帝
        if (empire == null && !pKingdom.IsInEmpire() && CultureAbolishedMonarchy(pKingdom) &&
            RegimeManager.IsMonarchy(pKingdom.GetRegime()?.type))
            CultureService.ApplyCulturePoliticalSystem(pKingdom, CultureService.GetRealmCulture(pKingdom));
        if (empire == null && ModernStateFormationSystem.TryUpdate(pKingdom)) return;
        if (pKingdom.hasKing() && ImperialLegitimacyChallengeService.TryFindTarget(pKingdom, out _))
        {
            var challengePlot = AssetManager.plots_library.basic_plots
                .Find(p => p.id == "usurp_imperial_legitimacy");
            if (challengePlot?.try_to_start_advanced?.Invoke(pKingdom.king, challengePlot, true) == true)
            {
                pKingdom.GetRegime()?.SetAllowDiplomacy(true);
                return;
            }
        }
        // 已经在筹备(或进行别的剧情)时不要重新开始，否则会反复覆盖筹备开始时的记录
        if (!pKingdom.hasKing() || pKingdom.king.plot?.isActive() == true) return;
        if (!CanStartEmpireFormation(pKingdom, true)) return;

        var plot = AssetManager.plots_library.basic_plots.Find(p => p.id == "become_empire");
        if (plot == null) return;
        EmpireFormationService.BeginPreparation(pKingdom, EmpireFormationService.GetBestRoute(pKingdom));
        if (plot.try_to_start_advanced?.Invoke(pKingdom.king, plot, true) == true)
        {
            pKingdom.GetRegime()?.SetAllowDiplomacy(true);
        }
        else
        {
            pKingdom.GetOrCreate().empire_formation_started_timestamp = -1d;
        }
    }

    private static bool CultureAbolishedMonarchy(Kingdom kingdom) =>
        !TechnologySystem.PremodernLocked &&
        InstitutionSystem.GetFeature(CultureService.GetRealmCulture(kingdom), RepublicSystem.FeatureAbolishMonarchy) > 0f;

    // 称帝资格：基础条件 + 不在失败冷却期 + 至少满足一条称帝路线(见 EmpireFormationService)
    public static bool CanStartEmpireFormation(Kingdom pKingdom, bool repairMainTitle = false)
    {
        if (pKingdom == null || pKingdom.isRekt()) return false;
        // 现代政体没有称帝：政权只能经 ModernStateFormationSystem 组建政府(所有文化；华夏条件更苛刻)
        if (pKingdom.GetRegime()?.type == RegimeType.Modern) return false;
        // 本文化已废除君主制：不再有人称帝
        if (CultureAbolishedMonarchy(pKingdom)) return false;
        if (repairMainTitle && pKingdom.hasKing() && !pKingdom.HasMainTitle() &&
            !pKingdom.IsEmpire() && !pKingdom.IsInEmpire())
        {
            pKingdom.ReconcileMainTitle(pKingdom.GetControlledTitle());
        }
        // 共主兼领的王国(城邦同盟成员等)不单独称帝：称帝剧情由君主发起，筹备记录、失败冷却都记在
        // 君主本国(actor.kingdom)身上；若让兼领国发起，本国没有筹备记录会立刻判定"被超越"失败，
        // 而兼领国自己又不进冷却，于是每轮都重新筹备、反复失败
        if (pKingdom.king == null || pKingdom.king.kingdom != pKingdom) return false;
        if (!EmpireFormationService.MeetsBaseRequirements(pKingdom)) return false;
        if (EmpireFormationService.IsInFailureCooldown(pKingdom)) return false;
        return EmpireFormationService.GetBestRoute(pKingdom) != EmpireFormationRoute.None;
    }

    public void CheckEmpireAlliance(Kingdom pKingdom)
    {
        if (pKingdom.NeedToRemoveTakenAlliance())
        {
            pKingdom.RemoveTakenAlliance();
        }
        else if (pKingdom.HasTakenAlliance())
        {
            // Repair tributaries saved by older versions that were recolored as imperial land.
            pKingdom.RestoreOriginalKingdomColor();
        }
        if (pKingdom.NeedToRemoveGivenAlliance())
        {
            pKingdom.RemoveGivenAlliance();
        }
    }
}

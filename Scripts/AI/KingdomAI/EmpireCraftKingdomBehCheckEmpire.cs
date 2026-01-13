using System;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
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

            if (pKingdom.GetCorruptionTime() > 20)
            {
                ModClass.EMPIRE_MANAGER.dissolveEmpire(empire);
                foreach (var c in pKingdom.cities)
                {
                    if(c.isCapitalCity()) continue;
                    var k = c.makeOwnKingdom(c.leader, pRebellion:true);
                    k.data.name = c.GetCityName();
                }
            }
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
    /// 计算军费，通过4年内的平均增长的数值来计算
    /// </summary>
    /// <param name="pKingdom"></param>
    public void CalcMilitaryExpenditure(Kingdom pKingdom)
    {
        Empire empire = pKingdom.GetEmpire();
        //计算军费
        // 追加当年财政数据
        empire.data.PreviousYearsMoney.Add(empire.CurrentMoney);

        // 始终只保留最近4年
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
            empire.CoreKingdom.SubMoney(militaryCost);
            if (empire.CoreKingdom.hasEnemies())
            {
                var warExpend = (empire.countWarriors() / 4) * empire.CoreKingdom.getWars().Count();
                empire.CoreKingdom.SubMoney(warExpend);
            }

            var jiedushis = empire.kingdoms_list.FindAll(k => k.GetKingdomType() == KingdomType.LvLing_jiedushi);
            if (jiedushis.Any())
            {
                //军府维护金
                var junfuMoney = jiedushis.Sum(k => k.countTotalWarriors());
                empire.CoreKingdom.SubMoney(junfuMoney);
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
        ModClass.EMPIRE_MANAGER.update(-1L);
        if (pKingdom.isRekt()) return;
        Empire empire = pKingdom.GetEmpire();
        if (empire != null)
        {
            var coreKingdom = empire.CoreKingdom;
            if (coreKingdom.isRekt())
            {
                empire.CheckDissolve(null);
                return;
            }
            if (!empire.kingdoms_list.Contains(coreKingdom))
            {
                empire.CheckDissolve(null);
                return; 
            }
        }
        if (EmpireCraftWorldLawLibrary.empirecraft_law_ban_empire.isEnabled()) return;
        if ((pKingdom?.GetMoney()??-1)<0) return;
        if (!pKingdom.hasKing()) return ;
        if (pKingdom.IsEmpire()) return ;
        if (pKingdom.IsInEmpire()) return ;
        if (!pKingdom.HasMainTitle()) return ; //if a kingdom has main title, then it could become an empire
        ModClass.EMPIRE_MANAGER.update(-1L);
        var num = ModClass.EMPIRE_MANAGER.ToList().FindAll(e=>!e.isRekt()&&!e.CoreKingdom.isRekt())
            .FindAll(e => e.CoreKingdom.getSpecies() == pKingdom.getSpecies()).Sum(e => e.getUnits().Count());
        var flag = num > 0 && pKingdom.units.Count > num;
        if (pKingdom.CanBecomeEmpire() || flag)
        {
            var plot = AssetManager.plots_library.basic_plots.Find(p => p.id == "become_empire");
            plot?.try_to_start_advanced(pKingdom.king, plot, true);
            pKingdom.GetRegime().SetAllowDiplomacy(true);
        }
    }

    public void CheckEmpireAlliance(Kingdom pKingdom)
    {
        if (pKingdom.NeedToRemoveTakenAlliance())
        {
            pKingdom.RemoveTakenAlliance();
        }
        if (pKingdom.NeedToRemoveGivenAlliance())
        {
            pKingdom.RemoveGivenAlliance();
        }
    }
}
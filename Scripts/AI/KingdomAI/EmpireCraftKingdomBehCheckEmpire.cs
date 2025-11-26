using System;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.KingdomAI;

public class EmpireCraftKingdomBehCheckEmpire:GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Kingdom pKingdom)
    {
        CheckPossible(pKingdom);

        if (pKingdom.IsEmpire())
        {
            SyncData(pKingdom);
            CalcMilitaryExpenditure(pKingdom);
        }
        return BehResult.Continue;
    }
    /// <summary>
    /// 帝国与核心国家同步数据
    /// </summary>
    /// <param name="pKingdom"></param>
    public void SyncData(Kingdom pKingdom)
    {
        //同步天子
        Empire empire = pKingdom.GetEmpire();
        if (pKingdom != null && empire.Emperor != pKingdom.king)
        {
            empire.Emperor = pKingdom.king;
        }

        if (!pKingdom.IsEmpire())
        {
            pKingdom.GetRegime().Factions.ForEach(f=>f.BanFaction());
        }
        else
        {
            pKingdom.GetRegime().Factions.ForEach(f =>
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
        }
    }
    /// <summary>
    /// 判断是否称帝
    /// </summary>
    /// <param name="pKingdom"></param>
    /// <returns></returns>
    public bool CheckPossible(Kingdom pKingdom)
    {
        if (EmpireCraftWorldLawLibrary.empirecraft_law_ban_empire.isEnabled()) return false;
        if (!pKingdom.hasKing()) return false;
        if (pKingdom.IsEmpire()) return false;
        if (pKingdom.IsInEmpire()) return false;
        if (!pKingdom.HasMainTitle()) return false; //if a kingdom has main title then it could become an empire
        ModClass.EMPIRE_MANAGER.update(-1L);
        if (!pKingdom.CanBecomeEmpire()) return false;
        EmpireCraftPlotsAddition.BecomeEmpireAndStartEnfeoff(pKingdom.king);
        return true;
    }
}
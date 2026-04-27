using System;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;
using EmpireCraft.Scripts.Layer;

namespace EmpireCraft.Scripts.AI.KingdomAI;

public class EmpireCraftKingdomBehCheckTax : GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Kingdom pKingdom)
    {
        pKingdom.CheckEmpire();
        if (!pKingdom.IsInEmpire())
        {
            if (pKingdom.GetMoney() < 0)
            {
                pKingdom.AddMoney(100);
            }
        }
        if (!pKingdom.IsNeedToSubmitTax()) return BehResult.Continue;
        pKingdom.CountingFinishedSelfPlot();
        var pTaxRate = pKingdom.GetTaxRate();
        Kingdom pEmpireKingdom = null;
        if (pKingdom.IsInEmpire())
        {
            Empire empire = pKingdom.GetEmpire();
            if (empire != null && !empire.isRekt() && !empire.IsArchived())
            {
                pEmpireKingdom = empire.CoreKingdom;
            }
        }
        int money = pKingdom.GetMoney();
        int num = (int)((float)money * pTaxRate);
        int corruptedMoney = 0;
        if (pKingdom.hasKing())
        {
            Actor actor = pKingdom.king;
            var corruptionValue = actor.CalcCorruptionValue();
            if (pKingdom.IsInEmpire())
            {
                if (corruptionValue > 0)
                {
                    pKingdom.AddCorruptionRate(corruptionValue / 10f);
                }
            }
            else
            {
                pKingdom.AddCorruptionRate(-0.2f);
            }
            corruptedMoney = (int)(corruptionValue / 2) * num;

            if (corruptedMoney > 0)
            {
                actor.addMoney(corruptedMoney);
                actor.RecordCrime(LawType.贪污);
            }
        }
        pKingdom.SubMoney((int)(num * (1.0f - pKingdom.GetCorruptionRate())));
        if (pEmpireKingdom != null)
        {
            pEmpireKingdom.AddMoney((int)((num - corruptedMoney) * (1.0f - pKingdom.GetCorruptionRate())));
        }
        pKingdom.RecordTaxTime();
        return BehResult.Continue;
    }
}

using System;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;
using EmpireCraft.Scripts.Layer;

namespace EmpireCraft.Scripts.AI.KingdomAI;

public class EmpireCraftKingdomBehCheckTax : GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Kingdom pKingdom)
    {
        pKingdom.CheckEmpire();
        if (!TreasurySystem.Enabled(pKingdom) && !pKingdom.IsInEmpire())
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
        int money = Math.Max(0, pKingdom.GetMoney());
        int num = (int)(money * Math.Max(0d, Math.Min(1d, pTaxRate)));
        int corruptedMoney = 0;
        if (pKingdom.hasKing() && !pKingdom.king.isRekt() && pKingdom.king.isAlive())
        {
            Actor actor = pKingdom.king;
            var corruptionValue = actor.CalcCorruptionValue();
            pKingdom.AddCorruptionRate(CorruptionSystem.AnnualOfficeChange(actor, pKingdom, corruptionValue));
            corruptedMoney = (int)(num * Math.Max(0d, Math.Min(1d, corruptionValue / 2d)));
        }
        if (TreasurySystem.Enabled(pKingdom))
        {
            pKingdom.RecordTaxTime();
            return BehResult.Continue;
        }
        int collected = (int)(num * Math.Max(0d, Math.Min(1d, 1d - pKingdom.GetCorruptionRate())));
        corruptedMoney = Math.Min(collected, corruptedMoney);
        if (corruptedMoney > 0 && pKingdom.hasKing())
        {
            pKingdom.king.addMoney(corruptedMoney);
            pKingdom.king.RecordCrime(LawType.贪污);
        }
        pKingdom.SubMoney(collected);
        if (pEmpireKingdom != null)
        {
            pEmpireKingdom.AddMoney(collected - corruptedMoney);
        }
        pKingdom.RecordTaxTime();
        return BehResult.Continue;
    }

}

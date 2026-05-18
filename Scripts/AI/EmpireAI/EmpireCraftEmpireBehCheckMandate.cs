using System;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;

namespace EmpireCraft.Scripts.AI.EmpireAI;

public class EmpireCraftEmpireBehCheckMandate : GameAIEmpireBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Kingdom pKingdom)
    {
        pKingdom.CheckEmpire();
        if (!pKingdom.IsEmpire()) return BehResult.Continue;
        Empire empire = pKingdom.GetEmpire();
        if (empire?.CoreKingdom == null) return BehResult.Continue;
        if (empire.Emperor != null && empire.IsNeedToIncreaseMandate())
        {
            empire.AddMandate(1);
            empire.data.last_increase_mandate_timestamp = world.getCurWorldTime();
            foreach (var k in empire.kingdoms_list.ToList())
            {
                if (empire.CurrentMoney > 0)
                {
                    int opinionValue = World.world.diplomacy.getOpinion(k, empire.CoreKingdom).total;
                    int maintainCost = Math.Max(0, (99999 - opinionValue) / 5);

                    if (k.IsNeedToMaintainGoodOpinion())
                    {
                        if (opinionValue >= 99999)
                        {
                            k.EndMaintainGoodOpinion();
                        }
                        else
                        {
                            empire.CoreKingdom.SubMoney(maintainCost);
                            k.StartMaintainGoodOpinion();
                        }
                    }
                    else if (!k.isOpinionTowardsKingdomGood(empire.CoreKingdom) &&
                             (empire.CoreKingdom?.isOpinionTowardsKingdomGood(k) ?? false))
                    {
                        empire.CoreKingdom.SubMoney(maintainCost);
                        k.StartMaintainGoodOpinion();
                    }
                }
                else
                {
                    k.EndMaintainGoodOpinion();
                }
            }
        }

        return BehResult.Continue;
    }
}

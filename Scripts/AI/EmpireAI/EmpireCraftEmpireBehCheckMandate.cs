using System;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.EmpireAI;

public class EmpireCraftEmpireBehCheckMandate:GameAIEmpireBase
{
    public override Type OriginalBeh => GetType();
    public override BehResult execute(Kingdom pKingdom)
    {
        pKingdom.CheckEmpire();
        if (!pKingdom.IsEmpire()) return BehResult.Continue;
        Empire empire = pKingdom.GetEmpire();
        if (empire?.CoreKingdom == null) return BehResult.Continue;
        if (empire?.Emperor != null)
        {
            if (empire.IsNeedToIncreaseMandate())
            {
                empire.AddMandate(1);
                empire.data.last_increase_mandate_timestamp = world.getCurWorldTime();
                foreach (var k in empire.kingdoms_list.ToList())
                {
                    if (empire.CurrentMoney>0)
                    {
                        //打开转移支付开关
                        if (k.IsNeedToMaintainGoodOpinion())
                        {
                            var value = World.world.diplomacy.getOpinion(k, empire.CoreKingdom).total;
                            if (value>=99999)
                            {
                                k.EndMaintainGoodOpinion();
                            }
                            else
                            {
                                LogService.LogInfo($"当前国家好感度{99999-value}=>维稳所需资金{((99999-value) / 5)}");
                                empire.CoreKingdom?.AddMoney(-((99999-value) / 5));
                                k.StartMaintainGoodOpinion();
                            }
    
                        }
                        else
                        {
                            if (!k.isOpinionTowardsKingdomGood(empire.CoreKingdom)&&(empire.CoreKingdom?.isOpinionTowardsKingdomGood(k)??false))
                            {
                                var value = World.world.diplomacy.getOpinion(k, empire.CoreKingdom).total;
                                LogService.LogInfo($"当前国家好感度{value}=>维稳所需资金{-(value / 5)}");
                                empire.CoreKingdom?.AddMoney(-(value / 5));
                                k.StartMaintainGoodOpinion();
                            }
                        }
                        
                    }
                    else
                    {
                        k.EndMaintainGoodOpinion();
                    }
                }
            }
        }
        return BehResult.Continue;
    }
}
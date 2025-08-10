using System;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.KingdomAI;
public class EmpireCraftKingdomBehCheckHeir: GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();
    public override BehResult execute(Kingdom pKingdom)
    {
        CheckKingdomHeir(pKingdom);
        return BehResult.Continue;
    }

    private static void CheckKingdomHeir(Kingdom pKingdom)
    {
        if (pKingdom.HasHeir()&&!pKingdom.IsNeedToChooseHeir()) return;
        if (pKingdom.CalcHeirFinished())
        {
            pKingdom.SetCalcHeirTask(pKingdom.CalcHeirAsync());
            return;
        }

        if (!pKingdom.GetCalcHeirTask().IsCompleted)
        {
            return;
        }

        try
        {
            var (actor, relation) = pKingdom.GetCalcHeirTask().Result;
            if (actor == null) return;
            pKingdom.SetHeir(actor);
            LogService.LogInfo("开始播报消息");
            TranslateHelper.LogKingChooseHeir(pKingdom, relation, actor);
            pKingdom.ChooseHeirFinished();
        }
        catch (Exception e)
        {
            LogService.LogInfo($"指定王国继承人异常：{e.Message}");
        }
        finally
        {
            LogService.LogInfo("移除任务");
            pKingdom.RemoveCalcHeirStatus();
            LogService.LogInfo("移除任务结束");
        }
        LogService.LogInfo("检查继任者结束");
    }
}

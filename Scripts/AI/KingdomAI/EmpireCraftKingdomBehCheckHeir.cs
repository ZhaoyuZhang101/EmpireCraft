using System;
using System.Threading.Tasks;
using ai.behaviours;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.services;

public class EmpireCraftKingdomBehCheckHeir : GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Kingdom pKingdom)
    {
        if (pKingdom.HasHeir()&&!pKingdom.IsNeedToChooseHeir())  return BehResult.Continue;
        // 如果还没启动，就启动（并限速）
        if (pKingdom.CalcHeirFinished())
        {
            pKingdom.SetCalcHeirTask(pKingdom.ScheduleCalcHeirAsync());
            return BehResult.Continue;
        }

        // 已启动但没完成，继续等待
        if (!pKingdom.GetCalcHeirTask().IsCompleted)
            return BehResult.Continue;

        // 完成了——取结果、做 UI，再清空，推进行为树
        try
        {
            var (actor, relation) = pKingdom.GetCalcHeirTask().Result;
            if (actor != null)
            {
                pKingdom.ChooseHeirFinished();
                if (pKingdom.GetHeir() == actor)
                {
                    return BehResult.Continue;
                }

                if (pKingdom.king == actor)
                {
                    return BehResult.Continue;
                }
                if (!actor.isUnitFitToRule()) return BehResult.Continue;
                pKingdom.SetHeir(actor);
                // 这时肯定在主线程里，UI 调用安全
                TranslateHelper.LogKingChooseHeir(pKingdom, relation, actor);
            }
        }
        catch (Exception e)
        {
            LogService.LogInfo($"CalcHeir 异常: {e}");
        }
        finally
        {
            pKingdom.RemoveCalcHeirStatus();
        }
        return BehResult.Continue;
    }
}
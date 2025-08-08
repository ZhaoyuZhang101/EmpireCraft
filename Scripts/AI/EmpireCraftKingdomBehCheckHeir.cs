using ai.behaviours;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EmpireCraft.Scripts.HelperFunc;

namespace EmpireCraft.Scripts.AI;
public class EmpireCraftKingdomBehCheckHeir: BehaviourActionKingdom
{
    public override BehResult execute(Kingdom pKingdom)
    {
        if (pKingdom.isEmpire()) CheckEmpire(pKingdom);
        else CheckKingdom(pKingdom);
        return BehResult.Continue;
    }

    private async void CheckKingdom(Kingdom pKingdom)
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
            TranslateHelper.LogKingChooseHeir(pKingdom, relation, actor);
            pKingdom.ChooseHeirFinished();
        }
        catch (Exception e)
        {
            LogService.LogInfo($"指定王国继承人异常：{e.Message}");
        }
        finally
        {
            pKingdom.RemoveCalcHeirStatus();
        }
    }

    private void CheckEmpire(Kingdom pKingdom)
    {
        Empire empire = pKingdom.GetEmpire();
        if (empire.data.is_need_to_check_futher_heir)
        {
            LogService.LogInfo("追加检测");
            var heir = empire.CheckHeir(EmpireHeirLawType.officer);
            if (heir != null)
            {
                empire.SetHeir(heir);
                LogService.LogInfo($"当前帝国继承人:{empire.Heir.name}");
            }
        }
        if (!empire.HasHeir())
        {
            empire.RemoveHeir();
            var heir = empire.CheckHeir(EmpireHeirLawType.none);
            if (heir == null && !empire.HasEmperor())
            {
                empire.data.is_need_to_check_futher_heir = true;
            }
            if (heir == null) return;
            empire.SetHeir(heir);
            LogService.LogInfo($"当前皇室继承人:{empire.Heir.name}");
        } else
        {
            if (!empire.IsNeedToEducateHeir());
            Random rand = new Random();
            double i = rand.NextDouble();
            if (!(i > 0.3)) return;
            empire.Heir.data.renown += 5;
        }
    }
}

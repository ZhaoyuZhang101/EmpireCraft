using System;
using System.Threading;
using System.Threading.Tasks;
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
        var (actor, relation) = pKingdom.CalcHeirCore();
        if (actor == null) return;
        pKingdom.SetHeir(actor);
        TranslateHelper.LogKingChooseHeir(pKingdom, relation, actor);
        pKingdom.ChooseHeirFinished();
    }
}

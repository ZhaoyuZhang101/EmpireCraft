using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 僭主派天然持有这个诉求类型(见 FactionManager.FactionConfig),真正能否发起还要看
// 强者继承法是否已经在文化科技树里解锁 —— 未解锁时 CheckCondition 恒 false,
// 跟玩家在 RegimeWindow 里直接选择这条法受同一道科技树门槛约束。
public class TempFac_强者继承法 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_强者继承法();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override void Execute()
    {
        Empire empire = GetEmpire();
        Kingdom coreKingdom = empire?.CoreKingdom;
        Regime regime = coreKingdom?.GetRegime();
        if (regime != null)
        {
            regime.SetSuccessionLaw(SuccessionLawType.强者继承法);
            // 强推继承法改变了原本的继承顺位预期,正统性受损。
            empire.AddMandate(-15);
            CompletionOutcome = LM.Get("succession_law_outcome_strongest");
            LogService.LogInfo($"执行{type}");
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        Kingdom coreKingdom = empire?.CoreKingdom;
        Regime regime = coreKingdom?.GetRegime();
        if (regime == null || regime.GetLeaderSelectMethod() != LeaderSelectMethod.Succession) return false;
        if (coreKingdom.GetSuccessionLaw() == SuccessionLawType.强者继承法) return false;
        return SuccessionLawSystem.IsSuccessionLawUnlocked(coreKingdom, SuccessionLawType.强者继承法);
    }
}

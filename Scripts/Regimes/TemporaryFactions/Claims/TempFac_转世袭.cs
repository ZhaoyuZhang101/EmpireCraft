using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 血脉派天然持有这个诉求类型(见 FactionManager.FactionConfig)。跟"恢复世袭皇权"
// (只服务 Feudalism/神罗分支,见 HolyRomanClaimRules)是同类逻辑的通用版:任何非世袭
// 继承(科举/推举等)的政体,只要血脉派够强都能把它推回世袭 + 嫡长子继承法。
public class TempFac_转世袭 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_转世袭();
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
            regime.SetLeaderSelectMethod(LeaderSelectMethod.Succession);
            regime.SetSuccessionLaw(SuccessionLawType.嫡长子继承法);
            empire.AddMandate(15);
            CompletionOutcome = LM.Get("succession_law_outcome_hereditary_restored");
            LogService.LogInfo($"执行{type}");
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        Kingdom coreKingdom = empire?.CoreKingdom;
        Regime regime = coreKingdom?.GetRegime();
        return regime != null && regime.GetLeaderSelectMethod() != LeaderSelectMethod.Succession;
    }
}

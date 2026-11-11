using System.Linq;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 撤销军府、改回州郡(绥靖派即文官的诉求)：
//   朝廷任命的军府(流官节度使)撤销不需要罪名；世袭的藩镇要有可追究的罪名才能削。
//   逐个找：以前只看成员列表里第一个军府，它的长官没犯罪整条诉求就作罢，后面的军府都轮不到
public class TempFac_撤销军府 : TemporaryFaction
{
    public override bool RequireCrimeTarget => Hereditary(GetKingdomTarget());

    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_撤销军府();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        var kingdom = GetKingdomTarget();
        if (kingdom != null && !CheckRebelling(kingdom))
        {
            if (!TryEnforceCrimeForCurrentTarget())
            {
                End();
                return;
            }

            Regime regime = kingdom.GetRegime();
            regime.SetAllowDiplomacy(false);
            regime.SetLeaderSelectMethod(LeaderSelectMethod.Exam);
            // 改回州郡：重新供养中央军；转军府时免掉的赋税恢复征收
            regime.SetAllowSupportCenterArmy(true);
            if (regime.GetTaxLevel() == TaxLevel.None) regime.SetTaxLevel(TaxLevel.Low);
            EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(kingdom);
        }

        CountDown = 5;
        End();
    }

    // 目标：没有外敌的军府；朝廷任命的先撤、不要罪名，世袭的藩镇要有可追究的罪名。
    // 规则在 ClaimRules.json 的 target(见 ClaimTargetRules)
    public override bool CheckCondition() => TargetReady() && PickRuleTarget();

    protected override bool TargetReady()
    {
        Empire empire = GetEmpire();
        // 南明诸镇割据期间朝廷只是名义上的天子，削不了藩(见 SouthernGarrisonSystem)
        return empire?.kingdoms_list != null && !EmpireCraft.Scripts.GeneralSystems.SouthernGarrisonSystem.Active(empire);
    }

    private static bool Hereditary(Kingdom kingdom) =>
        kingdom?.GetRegime()?.GetLeaderSelectMethod() == LeaderSelectMethod.Succession;
}

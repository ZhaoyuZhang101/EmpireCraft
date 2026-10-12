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

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire == null) return false;
        // 先挑朝廷任命的，再挑世袭的
        foreach (var kingdom in empire.kingdoms_list.ToList().OrderBy(kingdom => Hereditary(kingdom) ? 1 : 0))
        {
            if (kingdom == null || kingdom.isRekt() || kingdom.IsEmpire()) continue;
            if (kingdom.GetKingdomType() != KingdomType.LvLing_jiedushi &&
                kingdom.GetKingdomType() != KingdomType.LvLing_duhufu) continue;
            if (kingdom.hasEnemies()) continue;
            SetKingdomTarget(kingdom);
            if (HasRequiredCrimeForCurrentTarget()) return true;
        }

        return false;
    }

    private static bool Hereditary(Kingdom kingdom) =>
        kingdom?.GetRegime()?.GetLeaderSelectMethod() == LeaderSelectMethod.Succession;
}

using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_转军府 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_转军府();
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
        Kingdom kingdom = GetKingdomTarget();
        if (kingdom != null)
        {
            Regime regime = kingdom.GetRegime();
            regime.SetAllowDiplomacy(true);
            regime.SetLeaderSelectMethod(LeaderSelectMethod.Exam);
            // 军府自收自支、不上缴中央(见 TreasurySystem.TaxShares)。以前把税级设成"无"，本军一分税收不上
            TreasurySystem.EnsureCollectsTax(regime);
            EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(kingdom);
        }
        End();
    }

    private const int RegionalCommandLegitimacy = 60;

    // 目标：不是封国、还不是军府的边地成员。规则在 ClaimRules.json 的 target(见 ClaimTargetRules)
    public override bool CheckCondition() => TargetReady() && PickRuleTarget();

    protected override bool TargetReady()
    {
        Empire empire = GetEmpire();
        if (empire?.kingdoms_list == null) return false;
        // 律令制不允许军府借由转制取得独立国家地位；行了"军镇强藩"、正统跌到 60 以下的例外——边镇坐大成藩镇
        // (用户 2026-10-21 选"华夏有真乱世"：以前这个节点只改兵制，从不出藩镇)
        return !(empire.CoreKingdom?.GetRegime()?.type == RegimeType.LvLing &&
                 (!InstitutionSystem.HasFeature(empire, CityPopulationSystem.FeatureRegional) ||
                  empire.Legitimacy >= RegionalCommandLegitimacy));
    }
}

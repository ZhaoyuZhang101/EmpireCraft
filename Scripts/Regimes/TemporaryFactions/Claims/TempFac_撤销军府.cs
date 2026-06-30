using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_撤销军府 : TemporaryFaction
{
    public override bool RequireCrimeTarget => true;

    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_撤销军府();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
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

            kingdom.GetRegime().SetAllowDiplomacy(false);
            kingdom.GetRegime().SetLeaderSelectMethod(LeaderSelectMethod.Exam);
            kingdom.GetRegime().SetAllowSupportCenterArmy(false);
        }

        CountDown = 5;
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire == null) return false;
        foreach (var kingdom in empire.kingdoms_list)
        {
            if (!kingdom.IsEmpire() && kingdom.GetKingdomType() == KingdomType.LvLing_jiedushi)
            {
                if (kingdom.hasEnemies()) continue;
                return TrySetTarget(kingdom);
            }
        }

        return false;
    }
}

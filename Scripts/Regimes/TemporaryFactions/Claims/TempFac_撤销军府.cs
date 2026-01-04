using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_撤销军府 : TemporaryFaction
{
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
        if (kingdom != null)
        {
            if (!CheckRebelling(kingdom))
            {
                kingdom.GetRegime().SetAllowDiplomacy(false);
                kingdom.GetRegime().SetLeaderSelectMethod(LeaderSelectMethod.Exam);
                kingdom.GetRegime().SetAllowSupportCenterArmy(false);
            }
        }
        CountDown = 5;
        End();
    }

    public override bool CheckCondition()
    {
        //如果存在军府则尝试撤销
        Empire empire = GetEmpire();
        if (empire == null) return false;
        foreach (var k in empire.kingdoms_list)
        {
            if (!k.IsEmpire())
            {
                if (k.GetKingdomType() == KingdomType.LvLing_jiedushi)
                {
                    if(k.hasEnemies()) continue;
                    SetKingdomTarget(k);
                    return true;
                }
            }
        }
        return false;
    }
}

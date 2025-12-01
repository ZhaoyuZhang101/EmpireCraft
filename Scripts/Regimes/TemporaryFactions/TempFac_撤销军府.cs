using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions;

public class TempFac_撤销军府 : TemporaryFaction
{

    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        var kingdom = GetKingdomTarget();
        if (kingdom != null)
        {
            LogService.LogInfo($"执行1{kingdom.data.name}");
            if (!CheckRebelling(kingdom))
            {
                LogService.LogInfo("执行2");
                kingdom.GetRegime().SetAllowDiplomacy(false);
                kingdom.GetRegime().SetLeaderSelectMethod(LeaderSelectMethod.Exam);
            }
        }
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
                    SetKingdomTarget(k);
                    return true;
                }
            }
        }
        return false;
    }
}

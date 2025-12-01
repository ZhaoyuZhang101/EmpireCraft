using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions;

public class TempFac_撤销军府 : TemporaryFaction
{
    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        if (GetTarget() != null)
        {
            Kingdom kingdom =  GetTarget();
            kingdom.GetRegime().SetAllowDiplomacy(false);
            kingdom.GetRegime().SetLeaderSelectMethod(LeaderSelectMethod.Exam);
        }
        End();
    }

    private Kingdom GetTarget()
    {
        if (targetType == MetaType.Kingdom)
        {
            return World.world.kingdoms.get(targetID);
        }

        return null;
    }
    public override bool CheckCondition()
    {
        //如果存在军府则尝试撤销
        Empire empire = GetEmpire();
        foreach (var k in empire.kingdoms_list)
        {
            if (k.IsEmpire()) continue;
            if (k.GetKingdomType() == KingdomType.LvLing_jiedushi)
            {
                targetID = k.getID();
                targetType = MetaType.Kingdom;
                return true;
            }
        }
        return false;
    }
}

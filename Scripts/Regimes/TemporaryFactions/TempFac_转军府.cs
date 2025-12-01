using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions;

public class TempFac_转军府 : TemporaryFaction
{
    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        End();
    }

    public override bool CheckCondition()
    {
        //将已有的省份转为军府
        Empire empire = GetEmpire();
        if (empire == null) return false;
        foreach (var k in empire.kingdoms_list)
        {
            if (k.IsEmpire()) continue;
            if (k.GetKingdomType() != KingdomType.LvLing_jiedushi)
            {
                if (k.IsBorder())
                {
                    targetID = k.getID();
                    targetType = MetaType.Kingdom;
                    return true;
                }
            }
        }
        return false;
    }
}

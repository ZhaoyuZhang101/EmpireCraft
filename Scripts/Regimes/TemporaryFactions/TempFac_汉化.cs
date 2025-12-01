using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions;

public class TempFac_汉化 : TemporaryFaction
{
    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        Kingdom pKingdom = GetTarget();
        if (pKingdom != null)
        {
            pKingdom.setCulture(pKingdom.GetEmpire().CoreKingdom.getCulture());
            pKingdom.SetRegimeType(GetEmpire().CoreKingdom.GetRegime().type);
            pKingdom.LoadRegime();
        }
        End();
    }

    public Kingdom GetTarget()
    {
        if (targetType == MetaType.Kingdom)
        {
            return World.world.kingdoms.get(targetID);
        }
        return null;
    }
    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        foreach (var k in empire.kingdoms_list)
        {
            if (k.IsEmpire()) continue;
            if (k.getCulture() != empire.CoreKingdom.getCulture())
            {
                targetType = MetaType.Kingdom;
                targetID = k.getID();
                return true;
            }
        }
        return false;
    }
}

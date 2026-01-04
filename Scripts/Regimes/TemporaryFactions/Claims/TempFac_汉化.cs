using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_汉化 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_汉化();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        return res;
    }

    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        Kingdom pKingdom = GetTarget();
        if (pKingdom != null)
        {
            var culture = pKingdom.GetEmpire().CoreKingdom.getCulture();
            pKingdom.setCulture(culture);
            pKingdom.units.ForEach(u=>u.setCulture(culture));
            pKingdom.SetRegimeType(GetEmpire().CoreKingdom.GetRegime().type);
            pKingdom.LoadRegime();
        }
        End();
    }

    public Kingdom GetTarget()
    {
        if (TargetType == MetaType.Kingdom)
        {
            return World.world.kingdoms.get(TargetID);
        }
        return null;
    }
    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        foreach (var k in empire.kingdoms_list)
        {
            if (k.IsEmpire()) continue;
            if (k.isRekt()) continue;
            if (k.getCulture() != empire.CoreKingdom.getCulture())
            {
                TargetType = MetaType.Kingdom;
                TargetID = k.getID();
                return true;
            }
        }
        return false;
    }
}

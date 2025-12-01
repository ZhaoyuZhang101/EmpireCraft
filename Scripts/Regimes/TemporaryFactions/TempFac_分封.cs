using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions;

public class TempFac_分封 : TemporaryFaction
{
    public override long EmpireID { get; protected set; }
    public override long TargetID { get; protected set; }
    public override MetaType TargetType { get; protected set; }

    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        Actor actor = GetActorTarget();
        if (actor != null)
        {
            foreach (var c in GetEmpire().CoreKingdom.cities)
            {
                if (c.isCapitalCity()) continue;
                c.makeOwnKingdom(actor);
            }
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire.CoreKingdom.cities.Count>1)
        {
            Actor actor = empire.Emperor?.getChildren()?.ToList().Find(c => !c.isKing());
            if ( actor != null)
            {
                SetActorTarget(actor);
                return true;
            }
        }
        return false;
    }
}

using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_分封 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_分封();
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
        Actor actor = GetActorTarget();
        Regime empireRegime = GetEmpire().CoreKingdom.GetRegime();
        if (actor != null)
        {
            foreach (var c in GetEmpire().CoreKingdom.cities)
            {
                if (c.isCapitalCity()) continue;
                var kingdom = c.makeOwnKingdom(actor);
                kingdom.SetRegimeType(empireRegime.type);
                kingdom.LoadRegime();
                Regime kingdomRegime = kingdom.GetRegime();
                kingdomRegime.SetLeaderSelectMethod(LeaderSelectMethod.Succession);
                kingdomRegime.SetAllowSupportCenterArmy(false);
                kingdomRegime.SetTaxLevel(TaxLevel.None);
                if (c.GetTitle()?.title_capital == c)
                {
                    KingdomTitle title = c.GetTitle();
                    kingdom.SetMainTitle(title);
                    kingdom.king.AddOwnedTitle(title);
                }
                GetEmpire().join(kingdom, pForce:true);
                GetEmpire().AddMandate(10);
                break;
            }
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire.CoreKingdom.cities.Count>1)
        {
            List<Actor> actor = empire.Emperor?.getChildren()?.ToList().FindAll(c => !c.isKing());
            if ( actor is { Count: > 1 })
            {
                SetActorTarget(actor[1]);
                return true;
            }
        }
        return false;
    }
}

using System.Collections.Generic;
using System.Linq;
using System;
using EmpireCraft.Scripts.Enums;
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
        Empire empire = GetEmpire();
        Regime empireRegime = empire.CoreKingdom.GetRegime();
        if (empireRegime.enfeoff_virtual_only)
        {
            empire.TryGrantNextLegalPeerage(actor);
            End();
            return;
        }
        EmpireCore empireCore = EmpireCoreManager.Get(empire);
        if (actor != null)
        {
            foreach (var c in empire.CoreKingdom.cities)
            {
                if (c.isCapitalCity()) continue;
                KingdomTitle title = c.GetTitle();
                if (empireCore != null && c.GetEmpireCore() == empireCore) continue;
                if (title != null && string.Equals(title.data.name, empire.GetEmpireName(), StringComparison.Ordinal)) continue;

                var kingdom = c.makeOwnKingdom(actor);
                kingdom.SetRegimeType(empireRegime.type);
                kingdom.LoadRegime();
                Regime kingdomRegime = kingdom.GetRegime();
                kingdomRegime.SetLeaderSelectMethod(LeaderSelectMethod.Succession);
                kingdomRegime.SetAllowSupportCenterArmy(false);
                kingdomRegime.SetTaxLevel(TaxLevel.None);
                if (title?.title_capital == c)
                {
                    kingdom.SetMainTitle(title);
                    kingdom.king.AddOwnedTitle(title);
                }
                empire.join(kingdom, pForce:true);
                empire.AddMandate(10);
                break;
            }
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire == null || empire.CoreKingdom == null) return false;
        Regime regime = empire.CoreKingdom.GetRegime();
        if (regime.enfeoff_virtual_only)
        {
            Actor candidate = empire.GetNextLegalPeerageCandidate();
            if (candidate == null) return false;
            SetActorTarget(candidate);
            return true;
        }
        if (empire.CoreKingdom.cities.Count>1)
        {
            List<Actor> actor = empire.Emperor?.getChildren()?.ToList().FindAll(c =>
                !c.isKing() && !c.HasVirtualEnfeoff(empire) &&
                (!regime.enfeoff_only_royal || c.GetSpecificClan() == empire.EmpireSpecificClan));
            if ( actor is { Count: > 1 })
            {
                SetActorTarget(actor[1]);
                return true;
            }
        }
        return false;
    }
}

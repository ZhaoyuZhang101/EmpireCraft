using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General.Game.extensions;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_转周制 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_转周制();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        return res;
    }

    public override void Execute()
    {
        //转周制后分封子嗣与功臣
        LogService.LogInfo($"执行{this.type}");
        Empire empire = GetEmpire();
        var members = GetFaction().Members.ToList();
        foreach (Kingdom kingdom in empire.kingdoms_list)
        {
            kingdom.SetRegimeType(RegimeType.ZhouFeudalism);
            if (!kingdom.IsEmpire())
            {
                kingdom.LoadRegime();
                kingdom.GetRegime().SetAllowArmy(true);
                kingdom.GetRegime().SetAllowDiplomacy(false);
            }
            else
            {
                kingdom.LoadRegime();
                kingdom.GetRegime().SetAllowArmy(true);
                kingdom.GetRegime().SetAllowDiplomacy(true);
            }
            if (kingdom.IsEmpire()) continue;
            var flag1 = false;
            if (empire.Emperor.getChildren().Any())
            {
                foreach (var child in empire.Emperor.getChildren())
                {
                    if (!child.isKing())
                    {
                        if (kingdom.king.GetSpecificClan() != empire.EmpireSpecificClan)
                        {
                            kingdom.setKing(child);
                            child.setCity(kingdom.capital);
                            flag1 = true;
                        }
                    }
                }
            }
            if (!flag1)
            {
                if (members.Any())
                {
                    if (kingdom.hasKing())
                    {
                        if (!members.Contains(kingdom.king.getID()))
                        {
                            Actor a = World.world.units.get(members.First());
                            if (a != null)
                            {
                                kingdom.setKing(a);
                                a.setCity(kingdom.capital); 
                                members.Remove(a.getID());
                            }
                        }
                    }
                    else
                    {
                        Actor a = World.world.units.get(members.First());
                        if (a != null)
                        {
                            kingdom.setKing(a);
                            a.setCity(kingdom.capital); 
                            members.Remove(a.getID());
                        }
                        
                    } 
                }
            }
        }
        empire.data.centerOffice.Init(empire.CoreKingdom);
        empire.CoreKingdom.SystemChange();
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire.CoreKingdom.GetSystemChangeYear() < 50)
        {
            return false;
        }
        if (empire.kingdoms_list.ToList().All(k => k.IsEmpire()||(k.GetKingdomType() == KingdomType.LvLing_jiedushi||k.GetKingdomType() == KingdomType.LvLing_kingdom||k.GetKingdomType() == KingdomType.LvLing_jimizhou)))
        {
            return true;
        }
        return false;
    }
}

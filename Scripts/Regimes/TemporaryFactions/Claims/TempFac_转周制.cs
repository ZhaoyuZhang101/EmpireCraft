using EmpireCraft.Scripts.System;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
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
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override void Execute()
    {
        //转周制后分封子嗣与功臣
        LogService.LogInfo($"执行{this.type}");
        Empire empire = GetEmpire();
        var members = GetFaction().Members.ToList();
        // 分封的皇子连族谱里的虚拟族人一起算(纯净地图下皇子多不在地图上，以前一个也封不出去)：
        // 地图上的在前，不够分的按长幼落成成年的，至多每个要分的封国一位
        int fiefs = empire.kingdoms_list.Count(kingdom => !kingdom.IsEmpire());
        var princes = (empire.Emperor.getChildren() ?? Enumerable.Empty<Actor>()).Where(child => child != null).ToList();
        if (princes.Count(child => !child.isKing()) < fiefs)
            princes.AddRange(KinRoster.Realize(KinRoster.Children(empire.Emperor)
                    .Where(person => KinRoster.Virtual(person) && person.age >= KinRoster.AdultAge)
                    .OrderBy(person => person.rank).Take(fiefs - princes.Count(child => !child.isKing())),
                child => child.isAlive() && child.isAdult()).Where(child => !princes.Contains(child)));
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
            if (princes.Any())
            {
                foreach (var child in princes)
                {
                    if (!child.isKing())
                    {
                        if (!SpecificClanManager.SameLineage(kingdom.king.GetSpecificClan(), empire.EmpireSpecificClan))
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
        // 同样的道理：这条诉求直接把政体改成分封制，补记一下对应节点的施行状态，
        // 免得科技树跟实际政体对不上。
        InstitutionSystem.SyncEnactedNodeForRegime(empire, RegimeType.ZhouFeudalism);
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire.Mandate<70) return false;
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

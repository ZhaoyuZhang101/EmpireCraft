using System;
using System.Collections.Generic;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.AI.KingdomAI;

public class EmpireCraftKingdomBehModernRegime : GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Kingdom pKingdom)
    {
        if (pKingdom.isRekt()) return BehResult.Continue;

        Regime regime = pKingdom.GetRegime();
        if (regime == null) return BehResult.Continue;

        bool isModern = regime.type == RegimeType.Modern;

        if (!isModern && regime.type == RegimeType.Feudalism)
        {
            if (World.world.getCurWorldTime() % 10.0f > 1.0f) return BehResult.Continue;

            int totalPop = 0;
            int revolutionaryPop = 0;

            foreach (var city in pKingdom.cities)
            {
                if (city.isRekt()) continue;
                foreach (var actor in city.units)
                {
                    totalPop++;
                    if (actor.hasTrait("revolutionary"))
                    {
                        revolutionaryPop++;
                    }
                }
            }

            if (totalPop > 10 && (float)revolutionaryPop / totalPop > 0.5f)
            {
                pKingdom.SetRegimeType(RegimeType.Modern);
                pKingdom.LoadRegime();
                ActionLibrary.showWhisperTip(pKingdom.GetKingdomName() + " 爆发了革命!");
                return BehResult.Continue;
            }
        }
        
        if (isModern)
        {
            if (World.world.getCurWorldTime() % 3.0f > 0.5f) return BehResult.Continue;

            Dictionary<FactionType, float> factionPower = new Dictionary<FactionType, float>
            {
                { FactionType.革命, 0f },
                { FactionType.民主, 0f },
                { FactionType.尊王, 0f },
                { FactionType.共和, 0f },
                { FactionType.攘夷, 0f }
            };
            
            Dictionary<FactionType, List<long>> factionMembers = new Dictionary<FactionType, List<long>>
            {
                { FactionType.革命, new List<long>() },
                { FactionType.民主, new List<long>() },
                { FactionType.尊王, new List<long>() },
                { FactionType.共和, new List<long>() },
                { FactionType.攘夷, new List<long>() }
            };

            foreach (var city in pKingdom.cities)
            {
                if (city.isRekt()) continue;
                foreach (var actor in city.units)
                {
                    float power = 1f;
                    if (actor.isNoble()) power += 5f;
                    if (actor.isKing()) power += 20f;
                    if (actor.isCityLeader()) power += 10f;
                    if (actor.isOfficer()) power += 5f;

                    FactionType primaryFaction = FactionType.无;

                    if (actor.hasTrait("revolutionary"))
                    {
                        primaryFaction = FactionType.革命;
                        factionPower[FactionType.革命] += power * 2f; 
                    }
                    else 
                    {
                        SocialClass sc = actor.GetOrCreate().socialClass;
                        switch (sc)
                        {
                            case SocialClass.Labour:
                                primaryFaction = FactionType.共产;
                                factionPower[FactionType.共产] += power;
                                break;
                            case SocialClass.Peasant:
                                primaryFaction = FactionType.革命;
                                factionPower[FactionType.革命] += power;
                                break;
                            case SocialClass.Merchant:
                                primaryFaction = FactionType.民主;
                                factionPower[FactionType.民主] += power;
                                factionPower[FactionType.共和] += power * 0.5f; 
                                break;
                            case SocialClass.Noble:
                                primaryFaction = FactionType.尊王;
                                factionPower[FactionType.尊王] += power;
                                factionPower[FactionType.攘夷] += power * 0.5f;
                                break;
                            case SocialClass.Officer:
                                primaryFaction = FactionType.共和;
                                factionPower[FactionType.共和] += power;
                                factionPower[FactionType.民主] += power * 0.5f;
                                break;
                            case SocialClass.Army:
                                primaryFaction = FactionType.攘夷;
                                factionPower[FactionType.攘夷] += power;
                                factionPower[FactionType.尊王] += power * 0.5f;
                                break;
                        }
                    }
                    
                    if (primaryFaction != FactionType.无)
                    {
                        factionMembers[primaryFaction].Add(actor.id);
                    }
                }
            }

            var topFactions = factionPower.OrderByDescending(kv => kv.Value).Take(2).ToList();
            if (topFactions.Count >= 2)
            {
                var f1 = topFactions[0];
                var f2 = topFactions[1];
                
                bool isLeft(FactionType t) => t == FactionType.革命;
                bool isRight(FactionType t) => t != FactionType.革命 && t != FactionType.无;

                bool conflict = (isLeft(f1.Key) && isRight(f2.Key)) || (isRight(f1.Key) && isLeft(f2.Key));
                
                if (conflict)
                {
                    float totalTop = f1.Value + f2.Value;
                    if (totalTop > 0)
                    {
                        float diff = Mathf.Abs(f1.Value - f2.Value);
                        if (diff / totalTop < 0.1f) 
                        {
                            if (Randy.randomInt(0, 100) < 5) 
                            {
                                string revName = isLeft(f2.Key) ? "无产阶级" : "资产阶级";
                                ActionLibrary.showWhisperTip($"{pKingdom.GetKingdomName()} 爆发了{revName}革命!");
                                TryTriggerCivilWar(pKingdom);
                            }
                        }
                    }
                }
            }

            topFactions = factionPower.OrderByDescending(kv => kv.Value).Take(3).ToList();
            
            string GetSocialClassName(FactionType type)
            {
                return type switch
                {
                    FactionType.革命 => LM.Get("class_labour"),
                    FactionType.民主 => LM.Get("class_merchant"),
                    FactionType.尊王 => LM.Get("class_noble"),
                    FactionType.共和 => LM.Get("class_officer"),
                    FactionType.攘夷 => LM.Get("class_army"),
                    _ => ""
                };
            }

            if (regime.PlayerFactions != null)
            {
                var blocked = regime.IsFactionChangeBlocked();
                foreach (var f in regime.PlayerFactions)
                {
                    f.Members.Clear();
                    if (factionMembers.ContainsKey(f.Type))
                    {
                        f.Members.AddRange(factionMembers[f.Type]);
                    }
                    
                    if (factionPower.TryGetValue(f.Type, out var value))
                    {
                        f.TotalPower = (int)value;
                    }
                    
                    var match = topFactions.Find(kv => kv.Key == f.Type);
                    if (match.Key != FactionType.无 && match.Value > 0) 
                    {
                        f.Hide = false;
                        string originalName = f.Name.Split('(')[0].Trim(); 
                        f.Name = $"{originalName} ({GetSocialClassName(f.Type)})";
                        
                        if (!blocked)
                        {
                            if (f.Type == topFactions[0].Key)
                            {
                                f.Force = true;
                            }
                            else
                            {
                                f.Force = false;
                            }
                        }
                    }
                    else
                    {
                        f.Hide = true;
                        if (!blocked)
                        {
                            f.Force = false;
                        }
                    }
                }
            }
        }

        return BehResult.Continue;
    }

    private static bool TryTriggerCivilWar(Kingdom oldKingdom)
    {
        if (oldKingdom == null || oldKingdom.isRekt()) return false;
        if (oldKingdom.cities == null || oldKingdom.cities.Count < 2) return false;
        if (oldKingdom.getWars().Count() > 0) return false;

        City rebelCity = null;
        foreach (var c in oldKingdom.cities)
        {
            if (c == null || c.isRekt()) continue;
            if (c.isCapitalCity()) continue;
            if (c.leader == null || !c.leader.isAlive()) continue;
            rebelCity = c;
            break;
        }

        if (rebelCity == null) return false;
        var leader = rebelCity.leader;
        if (leader == null || !leader.isAlive()) return false;

        rebelCity.removeFromCurrentKingdom();
        rebelCity.removeLeader();

        var newKingdom = World.world.kingdoms.makeNewCivKingdom(leader);
        rebelCity.setKingdom(newKingdom);
        rebelCity.newForceKingdomEvent(rebelCity.units, rebelCity._boats, newKingdom, "just_rebelled");
        rebelCity.switchedKingdom();
        newKingdom.copyMetasFromOtherKingdom(oldKingdom);
        newKingdom.setCityMetas(rebelCity);

        World.world.diplomacy.startWar(oldKingdom, newKingdom, WarTypeLibrary.rebellion);
        return true;
    }
}

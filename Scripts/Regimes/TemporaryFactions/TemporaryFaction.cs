using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using Newtonsoft.Json;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions;
[JsonConverter(typeof(TemporaryFactionConverter))]
public abstract class TemporaryFaction
{
    [JsonIgnore]
    public TemporaryFactionType type => Enum.TryParse(GetType().ToString().Split('_').Last(), out TemporaryFactionType res) ? res : default;
    public List<long>  kingdoms = new List<long>();
    public FactionType factionType = FactionType.无;
    public long EmpireID = -1L;
    public long targetID = -1L;
    public MetaType targetType;
    public int progress = 0;
    private bool started = false;
    public double timestamp = -1L;

    public void Init(FixedFaction faction)
    {
        factionType = faction.Type;
        EmpireID =  faction.EmpireId;
        timestamp = World.world.getCurWorldTime();
        kingdoms = new List<long>();
    }

    public void SetKingdomTarget(Kingdom pKingdom)
    {
        this.targetType = MetaType.Kingdom;
        this.targetID = pKingdom.getID();
    }

    public void SetActorTarget(Actor pActor)
    {
        this.targetType = MetaType.Unit;
        this.targetID = pActor.getID();
    }

    public Actor GetActorTarget()
    {
        if (targetType == MetaType.Unit)
        {
            return World.world.units.get(targetID);
        }

        return null;
    }

    public Kingdom GetKingdomTarget()
    {
        if (targetType == MetaType.Kingdom)
        {
            return World.world.kingdoms.get(targetID);
        }

        return null;
    }
    
    public bool CheckRebelling(Kingdom kingdom)
    {
        var targetFaction = kingdom?.king?.GetFaction();
        if (targetFaction != null)
        {
            var targetMembers = targetFaction.Members.ToList();
            //全部势力
            var cities = new List<City>();
            if (targetFaction != GetFaction())
            {
                foreach (var a in targetFaction.Members)
                {
                    if (targetMembers.Contains(a))
                    {
                        Actor actor = World.world.units.get(a);
                        if (actor?.isKing() ?? false)
                        {
                            targetMembers.Remove(a);
                            foreach (var city in actor.kingdom.cities)
                            {
                                if (GetEmpire().CoreKingdom.capital!=city)
                                {
                                    cities.Add(city);
                                }
                                targetMembers.Remove(city.leader?.getID() ?? -1L);
                            }
                        }
                        else
                        {
                            if (actor?.isCityLeader() ?? false)
                            {
                                if (GetEmpire().CoreKingdom.capital!=actor.city)
                                {
                                    cities.Add(actor.city);
                                }
                                targetMembers.Remove(a);
                            }
                        }
                    }
                }
            }

            var totalWarriors = cities.Sum(c => c.countWarriors());
            if (totalWarriors >= GetEmpire().countWarriors() - totalWarriors)
            {
                var leader = targetFaction.GetLeader();
                var royalMembers = targetFaction.Members.Select(id => World.world.units.get(id)).ToList().FindAll(a =>
                    a.GetSpecificClan() == GetEmpire().EmpireSpecificClan && a != null);
                if (royalMembers.Any())
                {
                    leader = royalMembers.OrderByDescending(a => a.age).First();
                }

                if (leader != null)
                {
                    Kingdom newKingdom =
                        cities.OrderByDescending(c => c.countWarriors()).First().makeOwnKingdom(leader);
                    GetEmpire().join(newKingdom, pForce: true);
                    newKingdom.StartFactionRebelling(targetFaction);
                    foreach (var c in cities)
                    {
                        if (c == newKingdom.capital) continue;
                        c.joinAnotherKingdom(newKingdom);
                    }

                    var war = World.world.diplomacy.startWar(newKingdom, GetEmpire().CoreKingdom,
                        WarTypeLibrary.normal);
                    war.SetEmpireWarType(EmpireWarType.派系叛乱);
                    war.data.name = targetFaction.Name + "叛乱";
                    return true;
                }
            }
        }
        return false;
    }
    public FixedFaction GetFaction()
    {
        Empire empire = GetEmpire();
        if (empire != null)
        {
            Kingdom kingdom = empire.CoreKingdom;
            Regime regime = kingdom.GetRegime();
            return regime?.Factions?.Find(f => f.Type == factionType);
        }

        return null;
    }

    public Empire GetEmpire()
    {
        return ModClass.EMPIRE_MANAGER.get(EmpireID);
    }
    public void Start(NanoObject ptarget = null)
    {
        targetID = ptarget?.getID()??-1L;
        targetType = ptarget?.meta_type??MetaType.Kingdom;
        started = true;
    }

    public bool IsStarted()
    {
        return started;
    }

    public void End()
    {
        kingdoms.Clear();
        started = false;
        progress = 0;
    }
    public void JoinKingdom(Kingdom kingdom)
    {
        kingdoms.Add(kingdom.id);
    }
    //更新：每年一次共计十年
    public void Update()
    {
        if (started)
        {
            LogService.LogInfo($"当前内阁派系：{GetEmpire().GetCabinetLeader()?.GetFaction()?.Type},进度{progress}");
            if (GetEmpire().GetCabinetLeader()?.GetFaction()?.Type != factionType)
            {
                LogService.LogInfo("触发终止诉求");
                End();
                return;
            }
            progress ++;
            if (progress >= 60) Execute();
        }
        else
        {
            progress = 0;
        }
    }

    public void CheckNeedToUpdate()
    {
        if (Date.getMonthsSince(timestamp) > 1)
        {
            Update();
            timestamp = World.world.getCurWorldTime();
        }
    }
    /// <summary>
    /// 触发条件成功后的执行动作
    /// </summary>
    public abstract void Execute();
    
    /// <summary>
    /// 检查条件是否满足
    /// </summary>
    /// <returns>返回条件是否满足的结果</returns>
    public abstract bool CheckCondition();

    public List<Kingdom> GetMembers()
    {
        return kingdoms.Select(k=>World.world.kingdoms.get(k)).ToList();
    }
}
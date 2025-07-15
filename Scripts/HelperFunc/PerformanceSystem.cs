using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.HelperFunc;
public class PerformanceEvent
{
    public PerformanceEventType eventType { get; set; }
    public bool is_good { get; set; }
    public double trigger_rate { get; set; }
    public double performance_add_on { get; set; }
    public List<OfficialLevel> official_levels {  get; set; }

    public double trigger(Actor actor, Empire empire)
    {
        System.Random rand = new System.Random();
        double minDouble = 0;
        double maxDouble = performance_add_on;
        double randomDouble = rand.NextDouble() * (maxDouble - minDouble) + minDouble;
        actor.GetIdentity(empire).OfficePerformance += randomDouble;
        return actor.GetIdentity(empire).OfficePerformance;
    }
}
public class PerformanceEvents
{
    public Dictionary<string, PerformanceEvent> events = null;
    [JsonIgnore]
    public Empire empire { get; set; }
    [JsonIgnore]
    public Actor actor { get; set; }
    public void init(Empire empire, Actor actor)
    {
        this.empire = empire;
        this.actor = actor;
        string filePath = Path.Combine(ModClass._declare.FolderPath, "Scripts", "Data", "PerformanceData.json");
        if (events == null)
        {
            if (File.Exists(filePath))
            {
                string text = File.ReadAllText(filePath);
                events = JsonConvert.DeserializeObject<Dictionary<string, PerformanceEvent>>(text);
            }
            else
            {
                LogService.LogInfo($"未发现绩效事件文件{filePath}");
            }
        }
        CalculateRate();


    }
    public double GetEmpirePerformance(Empire empire)
    {
        //事件触发基数
        double performance_base = 0.0;
        switch (empire.getEmpirePeriod())
        {
            case EmpirePeriod.平和:
                performance_base = 0.2;
                break;
            case EmpirePeriod.拓土扩业:
                performance_base = 0.4;
                break;
            case EmpirePeriod.下降:
                performance_base = 0.1;
                break;
            case EmpirePeriod.逐鹿群雄:
                performance_base = -0.3;
                break;
            case EmpirePeriod.天命丧失:
                performance_base = -0.4;
                break;
            default:
                performance_base = 0.0;
                break;
        }
        return performance_base;
    }
    public double GetPersonalPerformance(Actor actor)
    {
        //触发基数
        double performance_base = 0.0;

        double genius_base = 0.0;

        if (actor.hasTrait("evil"))
        {
            performance_base -= 0.1;
        }
        if (actor.hasTrait("madness"))
        {
            performance_base -= 0.1;
        }
        if (actor.hasTrait("deceitful"))
        {
            performance_base -= 0.1;
        }

        if (actor.hasTrait("honest"))
        {
            performance_base += 0.1;
        }
        if (actor.hasTrait("lucky"))
        {
            performance_base += 0.1;
        }
        if (actor.hasTrait("wise"))
        {
            performance_base += 0.1;
        }
        //乘积
        if (actor.hasTrait("genius"))
        {
            genius_base = 2;
        }
        return performance_base * genius_base;
    }
    public void CalculateRate()
    {
        //初始化绩效事件触发概率

        //帝国绩效基数
        double empire_performance_base = GetEmpirePerformance(empire);
        //个人绩效基数
        double personal_performance_base = GetPersonalPerformance(actor);
        foreach (KeyValuePair<string, PerformanceEvent> pairs in events)
        {
            if (pairs.Value.eventType == PerformanceEventType.None) { continue; }
            if (pairs.Value.is_good)
            {
                pairs.Value.trigger_rate = pairs.Value.trigger_rate + empire_performance_base + personal_performance_base;
            }
            else
            {
                pairs.Value.trigger_rate = pairs.Value.trigger_rate - empire_performance_base - personal_performance_base;
            }
        }
    }
    public (PerformanceEvent, double performance) TriggerEvent(string pEventName = "None")
    {
        if (pEventName != "None")
        {
            PerformanceEvent performanceEvent = events[pEventName];
            return (performanceEvent, performanceEvent.trigger(actor, empire));
        }
        List<(PerformanceEvent e, double weight)> weightedList = new List<(PerformanceEvent, double)>();
        double performance = 0;
        if (events == null) return (null, 0);
        foreach (var pair in events)
        {
            double weight = pair.Value.trigger_rate;
            bool flag = false;
            if (pair.Value.official_levels == null)
            {
                flag = true;
            } else if (pair.Value.official_levels.Count == 0)
            {
                flag = true;
            } else if (actor.GetIdentity(empire)==null) 
            {
                flag = false;
            }
            else if (pair.Value.official_levels.Contains(actor.GetIdentity(empire).officialLevel))
            {
                flag = true;
            }
            if (weight > 0.001 && flag)
            {
                weightedList.Add((pair.Value, weight));
            }
        }
        if (weightedList.Count == 0) return (null, 0);
        double totalWeight = weightedList.Sum(item => item.weight);
        double rand = new System.Random().NextDouble() * totalWeight;
        double cumulative = 0.0;
        foreach (var item in weightedList)
        {
            cumulative += item.weight;
            if (rand < cumulative)
            {
                performance = item.e.trigger(actor, empire);
                return (item.e,performance);
            }
        }
        performance = weightedList.Last().e.trigger(actor, empire);
        return (weightedList.Last().e, performance);
    }
}
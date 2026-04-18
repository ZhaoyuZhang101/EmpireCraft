using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using Newtonsoft.Json;

namespace EmpireCraft.Scripts.System;
public class PerformanceEvent
{
    internal static readonly Random s_random = new Random();
    public PerformanceEventType eventType { get; set; }
    public bool is_good { get; set; }
    public double trigger_rate { get; set; }
    public double performance_add_on { get; set; }
    public List<int> official_levels {  get; set; }

    public double trigger(Actor actor)
    {
        double minDouble = 0;
        double maxDouble = performance_add_on;
        double randomDouble = s_random.NextDouble() * (maxDouble - minDouble) + minDouble;
        var identity = actor.GetIdentity();
        identity.OfficePerformance += randomDouble;
        return identity.OfficePerformance;
    }
}
public class PerformanceEvents
{
    public Dictionary<string, PerformanceEvent> events = null;
    
    public void init(Actor actor)
    {
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
        CalculateRate(actor);


    }
    public static double GetPersonalPerformance(Actor actor)
    {
        //触发基数
        double score = 0;               // 证据分数，>0 越偏好，<0 越不偏好
        const double k = 0.6;           // 斜率系数，越大曲线越“陡”，可调

        foreach (var trait in actor.traits)
        {
            if (trait.type == TraitType.Positive) score += 1;
            if (trait.type == TraitType.Negative) score -= 1;
        }

        // 收束到 (0,1)，当 score = 0 时结果=0.5
        double performance_base = 1.0 / (1.0 + Math.Exp(-k * score));
        
        return performance_base;
    }
    public void CalculateRate(Actor actor)
    {
        //初始化绩效事件触发概率
        
        //个人绩效基数
        double personal_performance_base = GetPersonalPerformance(actor)-0.5f;
        foreach (KeyValuePair<string, PerformanceEvent> pairs in events)
        {
            if (pairs.Value.eventType == PerformanceEventType.None) { continue; }
            if (pairs.Value.is_good)
            {
                pairs.Value.trigger_rate += personal_performance_base;
            }
            else
            {
                pairs.Value.trigger_rate -= personal_performance_base;
            }
        }
    }
    public (PerformanceEvent, double performance) TriggerEvent(Actor actor, string pEventName = "None")
    {
        if (pEventName != "None")
        {
            PerformanceEvent performanceEvent = events[pEventName];
            return (performanceEvent, performanceEvent.trigger(actor));
        }
        List<(PerformanceEvent e, double weight)> weightedList = new List<(PerformanceEvent, double)>();
        double performance = 0;
        if (events == null) return (null, 0);
        var identity = actor?.GetIdentity();
        bool hasIdentity = identity != null;
        int officialLevel = hasIdentity ? identity.officialLevel : -1;
        double totalWeight = 0;
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
            } else if (!hasIdentity) 
            {
                flag = false;
            }
            else if (pair.Value.official_levels.Contains(officialLevel))
            {
                flag = true;
            }
            if (weight > 0.001 && flag)
            {
                weightedList.Add((pair.Value, weight));
                totalWeight += weight;
            }
        }
        if (weightedList.Count == 0) return (null, 0);
        double rand = PerformanceEvent.s_random.NextDouble() * totalWeight;
        double cumulative = 0.0;
        foreach (var item in weightedList)
        {
            cumulative += item.weight;
            if (rand < cumulative)
            {
                performance = item.e.trigger(actor);
                return (item.e,performance);
            }
        }
        performance = weightedList.Last().e.trigger(actor);
        return (weightedList.Last().e, performance);
    }
}

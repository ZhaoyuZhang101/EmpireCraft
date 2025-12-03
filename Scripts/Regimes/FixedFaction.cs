using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using EmpireCraft.Scripts.System;
using NeoModLoader.services;
using Newtonsoft.Json;

namespace EmpireCraft.Scripts.Regimes;

public class FactionManager
{
    public Dictionary<string, FixedFaction>  FixedFactions = new();

    public void RecordFactions(FixedFaction faction, Empire empire)
    {
        var id = $"{empire.id}_{faction.GetID()}";
        FixedFactions.Add(id, faction);
    }
}

public enum FactionType
{
    尊王,  //王室为主
    自治,  //诸侯为主
    攘夷,  //主战
    绥靖,  //主和
    血脉, //国王为单一血脉
    僭主, //霸者为王
    神权, //宗教至上
    共和,  //反移民，发展自己
    民主,  //移民，吸血国外
    革命,  //一党，革命输出
    无
}
public class FixedFaction
{
    private string _id;
    //特质需求（拥有该特质的人会更加倾向于加入此派系）
    private List<string> _requiredTraits = new();
    public FactionType Type { get; set; }
    public bool Ban { get; set; } = false;
    public string Name { set; get; }
    public long EmpireId { get; set; } = -1L;
    public bool Force = false; 
    [JsonIgnore] 
    public Empire Empire => ModClass.EMPIRE_MANAGER.get(EmpireId);
    public List<long> Members = new();
    [JsonIgnore]
    public int Count => Members.Count;
    [JsonIgnore]
    public int TotalPower => (int) Members.Sum(a=>World.world.units.get(a)?.GetIdentity()?.TotalPerformance??0);
    //倾向于推动的政策
    [JsonIgnore]
    public List<TemporaryFactionType> TemporaryFactionTypes => ConfigData.FactionConfig.TryGetValue(Type, out var tfList)? tfList : null;
    public List<TemporaryFaction> TemporaryFactions;
    
    public long Leader = -1L;
    [JsonIgnore]
    public float LastJoinProb { get; private set; } // 记录最近一次计算结果(0~1)

    public bool IsAnyTFactionRuns()
    {
        return TemporaryFactions.Any(tf => tf?.IsStarted() ?? false);
    }

    public TemporaryFaction GetAnyTFactionRuns()
    {
        return TemporaryFactions.Find(tf => tf?.IsStarted() ?? false);
    }
    public FixedFaction Clone()
    {
        LogService.LogInfo("重置派系");
        FixedFaction newFaction = new FixedFaction()
        {
            _id = _id,
            _requiredTraits = _requiredTraits,
            Type = Type,
            Ban = Ban,
            Name = Name,
            EmpireId = EmpireId,
            Members = new (),
            Leader = -1L
        };
        newFaction.TemporaryFactions = newFaction.ConvertToObjectFromFactionType();
        return newFaction;
    }

    public void FixMissedTemporaryFactions()
    {
        if (TemporaryFactions == null || TemporaryFactions.Count == 0 || TemporaryFactions.Any(tf => tf == null))
        {
            TemporaryFactions = ConvertToObjectFromFactionType();
            foreach (var tf in TemporaryFactions)
            {
                tf?.Init(this);   // ← 始终把 EmpireId 等运行时信息灌进去
            }
        }
        foreach (var tf in TemporaryFactions)
        {
            if (Empire != null)
            {
                tf.SetEmpire(Empire); // ← 始终把 EmpireId 等运行时信息灌进去
            }
        }
    }
    /// <summary>
    /// 将诉求类别转化为实例
    /// </summary>
    /// <returns></returns>
    private List<TemporaryFaction> ConvertToObjectFromFactionType()
    {
        var result = new List<TemporaryFaction>();
        var typesToBuild = TemporaryFactionTypes; // 你的属性：List<TemporaryFactionType>
        if (typesToBuild == null || typesToBuild.Count == 0)
            return result;

        // 收集所有可用类型（避免 ReflectionTypeLoadException）
        var allTypes = new List<Type>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try { allTypes.AddRange(asm.GetTypes()); }
            catch (ReflectionTypeLoadException e)
            {
                allTypes.AddRange(e.Types.Where(t => t != null));
            }
        }

        // 只保留派生自 TemporaryFaction 的具体类型
        var candidateTypes = allTypes
            .Where(t => t != null
                        && typeof(TemporaryFaction).IsAssignableFrom(t)
                        && !t.IsAbstract)
            .ToList();

        foreach (var e in typesToBuild)
        {
            // 约定：类名为 "TempFac_" + 枚举名
            string className = "TempFac_" + e.ToString();

            var t = candidateTypes.FirstOrDefault(x => x.Name == className);
            if (t == null)
            {
                LogService.LogError($"TemporaryFaction class not found: {className}");
                continue;
            }

            try
            {
                var inst = Activator.CreateInstance(t) as TemporaryFaction;
                if (inst == null) continue;
                result.Add(inst);
            }
            catch (Exception ex)
            {
                LogService.LogError($"Create TemporaryFaction failed: {className}, {ex}");
            }
        }

        return result;
    }
    public float CalcPossibility(Actor pActor, float minProb = 0.5f, float maxProb = 0.95f)
    {
        int required = _requiredTraits?.Count ?? 0;
        if (required <= 0) { LastJoinProb = minProb; return LastJoinProb; }

        int matched = 0;
        if (_requiredTraits != null)
            foreach (var trait in _requiredTraits)
                if (pActor.hasTrait(trait))
                    matched++;

        // 匹配占比 0~1
        float ratio = (float)matched / required;

        // 线性插值：匹配越多，越接近 maxProb
        float prob = minProb + (maxProb - minProb) * ratio;

        // 存一下并返回
        LastJoinProb = prob;
        return prob;
    }
    public void AddMember(Actor pActor)
    {
        Members.Add(pActor.id);
        if (Members.Count == 1)
        {
            SetLeader(pActor);
        }
    }

    public void Update()
    {
        if (GetLeader() == null)
        {
            if (Members.Count > 0)
            {
                var best = Members
                    .Select(id => new
                    {
                        Id   = id,
                        Perf = World.world.units.get(id)?.GetIdentity()?.TotalPerformance ?? 0
                    })
                    .OrderByDescending(x => x.Perf)
                    .FirstOrDefault();

                if (best != null)
                {
                    SetLeader(id: best.Id);
                }
            }
        }
    }

    public void BanFaction()
    {
        Ban = true;
        if (Members.Count > 0)
        {
            Members.ForEach(a=>World.world.units.get(a)?.RemoveFaction());
        }
        Members.Clear();
        Leader = -1L;
    }

    public void RemoveMember(Actor pActor)
    {
        Members.Remove(pActor.id);
        if (pActor.id == Leader)
        {
            Leader = -1L;
        }

        if (Members.Count == 0)
        {
            Leader = -1L;
        }
    }

    public void SetLeader(Actor pActor=null, long id=-1L)
    {
        Leader = pActor?.id??id;
        if (!Members.Contains(pActor?.id??id))
        {
            Members.Add(pActor?.id??id);
        }

        if (GetLeader() != null)
        {
            TranslateHelper.LogOfficerBecomeFactionLeader(GetLeader(),this);
        }
    }

    public Actor GetLeader()
    {
        return World.world.units.get(Leader);
    }

    public void RemoveLeader()
    {
        Leader = -1L;
    }

    public string GetID()
    {
        return _id;
    }
}
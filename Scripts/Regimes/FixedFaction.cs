using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using EmpireCraft.Scripts.System;
using EmpireCraft.Scripts.UI.Components;
using NCMS.Extensions;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.services;
using Newtonsoft.Json;

namespace EmpireCraft.Scripts.Regimes;

public static class FactionManager
{
    [JsonIgnore]
    public static List<TemporaryFactionType> DefaultCountryMind = new()
    {
        TemporaryFactionType.国_打击贪腐
    };
    [JsonIgnore] 
    public static Dictionary<FactionType, List<TemporaryFactionType>> FactionConfig =
        new()
        {
            {
                FactionType.僭主,
                new List<TemporaryFactionType>
                    { TemporaryFactionType.强者继承法, TemporaryFactionType.宗教同化, TemporaryFactionType.索取皇位 }
            },
            {
                FactionType.血脉,
                new List<TemporaryFactionType>
                    { TemporaryFactionType.转世袭, TemporaryFactionType.宗教同化, TemporaryFactionType.索取皇位 }
            },
            {
                FactionType.尊王,
                new List<TemporaryFactionType>
                {
                    TemporaryFactionType.削藩, TemporaryFactionType.夺取诸侯开战权, TemporaryFactionType.扩张地盘,
                    TemporaryFactionType.转天朝制度, TemporaryFactionType.索取皇位, TemporaryFactionType.谋求统一
                }
            },
            {
                FactionType.诸侯,
                new List<TemporaryFactionType>
                    { TemporaryFactionType.分封, TemporaryFactionType.允许诸侯自由开战, TemporaryFactionType.索取皇位 }
            },
            {
                FactionType.中央,
                new List<TemporaryFactionType>
                {
                    TemporaryFactionType.削藩, TemporaryFactionType.夺取诸侯开战权, TemporaryFactionType.扩张地盘,
                    TemporaryFactionType.索取皇位, TemporaryFactionType.谋求统一,
                    TemporaryFactionType.恢复世袭皇权, TemporaryFactionType.颁布帝国治安令,
                    TemporaryFactionType.争夺主教叙任权
                }
            },
            {
                FactionType.自治,
                new List<TemporaryFactionType>
                {
                    TemporaryFactionType.分封, TemporaryFactionType.允许诸侯自由开战,
                    TemporaryFactionType.索取皇位, TemporaryFactionType.颁布金玺诏书,
                    TemporaryFactionType.确认诸侯特权
                }
            },
            {
                FactionType.攘夷,
                new List<TemporaryFactionType>
                {
                    TemporaryFactionType.对外扩张, TemporaryFactionType.汉化, TemporaryFactionType.转军府,
                    TemporaryFactionType.谋求统一, TemporaryFactionType.迫使朝贡, TemporaryFactionType.转周制,
                    TemporaryFactionType.索取皇位
                }
            },
            {
                FactionType.绥靖,
                new List<TemporaryFactionType>
                {
                    TemporaryFactionType.撤销军府, TemporaryFactionType.提供岁币, TemporaryFactionType.割让城池,
                    TemporaryFactionType.削藩, TemporaryFactionType.设置行政区, TemporaryFactionType.开科取士,
                    TemporaryFactionType.索取皇位
                }
            },
            {
                FactionType.原始,
                new List<TemporaryFactionType>
                {
                    TemporaryFactionType.索取皇位, TemporaryFactionType.分封, TemporaryFactionType.加强神权, TemporaryFactionType.扩张地盘
                }
            },
            {
                FactionType.共和,
                new List<TemporaryFactionType>
                {
                    TemporaryFactionType.提高赋税, TemporaryFactionType.提高福利, TemporaryFactionType.清除移民,
                    TemporaryFactionType.缩减金融霸权
                }
            },
            {
                FactionType.民主,
                new List<TemporaryFactionType>
                {
                    TemporaryFactionType.开放移民, TemporaryFactionType.降低赋税, TemporaryFactionType.提高福利,
                    TemporaryFactionType.拓展金融霸权
                }
            },
            {
                FactionType.革命,
                new List<TemporaryFactionType> { TemporaryFactionType.输出革命, TemporaryFactionType.扶持革命党, TemporaryFactionType.禁党 }
            },
            {
                FactionType.神权,
                new List<TemporaryFactionType>
                {
                    TemporaryFactionType.宗教同化, TemporaryFactionType.划地给教廷,
                    TemporaryFactionType.恢复圣地, TemporaryFactionType.确立国教, TemporaryFactionType.神授君权,
                    TemporaryFactionType.索取皇位, TemporaryFactionType.颁布金玺诏书,
                    TemporaryFactionType.争夺主教叙任权
                }
            },
            {
                FactionType.融入,
                new List<TemporaryFactionType>
                {
                    TemporaryFactionType.宗教融入, TemporaryFactionType.制度融入, TemporaryFactionType.劫掠,
                    TemporaryFactionType.游牧扩张, TemporaryFactionType.分封, TemporaryFactionType.索取皇位,
                    TemporaryFactionType.谋求统一
                }
            },
            {
                FactionType.同化,
                new List<TemporaryFactionType>
                {
                    TemporaryFactionType.宗教同化, TemporaryFactionType.游牧化, TemporaryFactionType.劫掠,
                    TemporaryFactionType.对外扩张, TemporaryFactionType.分封, TemporaryFactionType.索取皇位,
                    TemporaryFactionType.谋求统一
                }
            }
        };

    public static PlayerFactionConfig Config = new PlayerFactionConfig();

    public static void init()
    {
        Load();
        ConvertToObjectFromFactionType();
    }
    public static void ConvertToObjectFromFactionType()
    {

        var candidateTypes = EmpireCraft.Scripts.HelperFunc.SafeTypeDiscovery.GetConcreteDerivedTypes(
                typeof(TemporaryFaction), AppDomain.CurrentDomain.GetAssemblies(),
                message => LogService.LogWarning(message))
            .ToList();

        foreach (var e in candidateTypes)
        {
            // 约定：类名为 "TempFac_" + 枚举名
            string className = e.ToString().Split('.').Last();
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
                if (!Config.StoredTemporaryFaction.ContainsKey(inst.type))
                {
                    Config.StoredTemporaryFaction.Add(inst.type, inst);
                    LogService.LogInfo($"初始化诉求{inst.type}"); 
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"Create TemporaryFaction failed: {className}, {ex}");
            }
        }
    }

    public static bool Save()
    {
        try
        {
            string SCP = JsonConvert.SerializeObject(Config, Formatting.Indented);
            string parentFolder = Directory.GetParent(ModClass._declare.FolderPath)?.FullName;
            if (parentFolder != null)
            {
                string path = Path.Combine(parentFolder, "PlayerFactionConfig.json");

                File.WriteAllText(path, SCP);
                LogService.LogInfo("储存用户派系配置数据成功");
                return true;
            }

            return false;
        }
        catch
        {
            LogService.LogInfo("储存用户派系配置数据失败");
            return false;
        }
    }

    public static void Load()
    {
        try
        {

            string parentFolder = Directory.GetParent(ModClass._declare.FolderPath)?.FullName;
            if (parentFolder != null)
            {
                string path = Path.Combine(parentFolder, "PlayerFactionConfig.json");
                if (File.Exists(path))
                {
                    string text = File.ReadAllText(path);
                    Config = JsonConvert.DeserializeObject<PlayerFactionConfig>(text);
                    if (Config?.PlayerRegimeFactions != null &&
                        Config.PlayerRegimeFactions.TryGetValue(RegimeType.Feudalism, out List<FixedFaction> factions))
                    {
                        foreach (FixedFaction faction in factions)
                        {
                            if (faction != null && faction.ClaimCatalogVersion < FixedFaction.CurrentClaimCatalogVersion)
                            {
                                faction.MigrateFeudalClaimCatalog();
                            }
                        }
                    }
                    return;
                }
            }
            LogService.LogInfo("无用户派系配置");
        }
        catch
        {
            LogService.LogInfo("加载用户派系配置出错");
        }
    }
}

public class PlayerFactionConfig
{
    public Dictionary<TemporaryFactionType, TemporaryFaction> StoredTemporaryFaction = new ();
    public List<FixedFaction> PlayerFactions = new List<FixedFaction>();
    public Dictionary<RegimeType, List<FixedFaction>> PlayerRegimeFactions = new Dictionary<RegimeType, List<FixedFaction>>();
}
public enum FactionType
{
    原始,
    尊王,  //王室为主
    诸侯,  //诸侯为主
    中央,  //西方王室
    自治,  //西方自治
    攘夷,  //主战
    绥靖,  //主和
    血脉, //国王为单一血脉
    僭主, //霸者为王
    神权, //宗教至上
    共和,  //反移民，发展自己
    民主,  //移民，吸血国外
    革命,  //一党，革命输出
    融入,  //融入帝国中最大的文明
    同化,  //同化掉其他地区的文明
    共产,  //社会主义
    无
}
public class FixedFaction
{
    public const int CurrentClaimCatalogVersion = 1;
    public string _id;
    public bool only_king;
    //特质需求（拥有该特质的人会更加倾向于加入此派系）
    public List<string> _requiredTraits = new();
    public List<string> RequiredTraits = new();
    public FactionType Type { get; set; }
    public bool Ban { get; set; } = false;
    public string Name { set; get; }
    public long EmpireId { get; set; } = -1L;
    public bool Hide { get; set; } = false;
    public bool Force = false; 
    [JsonIgnore]
    public AdvancedButton LockButton { get; set; }
    [JsonIgnore] 
    public AutoVertLayoutGroup CardUI;
    [JsonIgnore] 
    public Empire Empire => ModClass.EMPIRE_MANAGER.get(EmpireId);
    public List<long> Members = new();
    [JsonIgnore] public List<Actor> AllMembers => Members.Select(id => World.world.units.get(id))
        .Where(actor => actor != null && !actor.isRekt()).ToList();
    [JsonIgnore]
    public int Count => Members.Count;
    [JsonIgnore]
    public int CentralRatio => Empire?.CoreKingdom?.GetFactionRatioValue(this) ?? 0;
    [JsonIgnore]
    public int TotalPower => CentralRatio;
    //倾向于推动的政策
    [JsonIgnore]
    public List<TemporaryFactionType> TemporaryFactionTypes => FactionManager.FactionConfig.TryGetValue(Type, out var tfList)? tfList : null;
    public List<TemporaryFactionType> TemporaryFactionTypesRecord;
    public List<TemporaryFaction> TemporaryFactions;
    public int ClaimCatalogVersion;
    public long Leader = -1L;
    [JsonIgnore]
    public float LastJoinProb { get; private set; } // 记录最近一次计算结果(0~1)

    public bool IsAnyTFactionRuns()
    {
        return TemporaryFactions.Any(tf => tf?.IsStarted() ?? false);
    }

    public void RecoverTrait()
    {
        RequiredTraits = _requiredTraits.ToList();
    }
    public TemporaryFaction GetAnyTFactionRuns()
    {
        return TemporaryFactions.Find(tf => tf?.IsStarted() ?? false);
    }
    public FixedFaction Clone()
    {
        FixedFaction newFaction = new FixedFaction()
        {
            _id = Guid.NewGuid().ToString(),
            RequiredTraits = _requiredTraits.ToList(),
            _requiredTraits = _requiredTraits,
            only_king = only_king,
            Type = Type,
            Ban = Ban,
            Name = Name,
            EmpireId = EmpireId,
            Members = new (),
            Leader = -1L,
            TemporaryFactionTypesRecord = new List<TemporaryFactionType>(TemporaryFactionTypes),
            ClaimCatalogVersion = CurrentClaimCatalogVersion
        };
        TemporaryFactionTypesRecord = new List<TemporaryFactionType>(TemporaryFactionTypes);
        newFaction.TemporaryFactions = newFaction.ConvertToObjectFromFactionType();
        newFaction.TemporaryFactions.ForEach(tf=>tf.Init(newFaction));
        return newFaction;
    }
    public FixedFaction DeepClone()
    {
        FixedFaction newFaction = new FixedFaction()
        {
            _id = Guid.NewGuid().ToString(),
            RequiredTraits = RequiredTraits,
            _requiredTraits = _requiredTraits,
            only_king = only_king,
            Type = Type,
            Ban = Ban,
            Name = Name,
            EmpireId = EmpireId,
            Members = new (),
            Leader = -1L,
            TemporaryFactions = TemporaryFactions,
            TemporaryFactionTypesRecord = TemporaryFactionTypesRecord,
            ClaimCatalogVersion = ClaimCatalogVersion
        };
        newFaction.TemporaryFactions = newFaction.ConvertToObjectFromFactionType();
        newFaction.TemporaryFactions.ForEach(tf=>tf.Init(newFaction));
        return newFaction;
    }

    public void FixMissedTemporaryFactions()
    {
        bool catalogChanged = false;
        if (ClaimCatalogVersion < CurrentClaimCatalogVersion &&
            Empire?.CoreKingdom?.GetRegime()?.type == RegimeType.Feudalism)
        {
            catalogChanged = MigrateFeudalClaimCatalog();
        }
        if (catalogChanged || TemporaryFactions == null || TemporaryFactions.Count == 0 || TemporaryFactions.Any(tf => tf == null))
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

    public bool MigrateFeudalClaimCatalog()
    {
        TemporaryFactionTypesRecord ??= new List<TemporaryFactionType>();
        IEnumerable<TemporaryFactionType> additions = Type switch
        {
            FactionType.中央 => new[]
            {
                TemporaryFactionType.恢复世袭皇权,
                TemporaryFactionType.颁布帝国治安令,
                TemporaryFactionType.争夺主教叙任权
            },
            FactionType.自治 => new[]
            {
                TemporaryFactionType.颁布金玺诏书,
                TemporaryFactionType.确认诸侯特权
            },
            FactionType.神权 => new[]
            {
                TemporaryFactionType.颁布金玺诏书,
                TemporaryFactionType.争夺主教叙任权
            },
            _ => Array.Empty<TemporaryFactionType>()
        };

        bool changed = false;
        foreach (TemporaryFactionType addition in additions)
        {
            if (TemporaryFactionTypesRecord.Contains(addition)) continue;
            TemporaryFactionTypesRecord.Add(addition);
            changed = true;
        }
        ClaimCatalogVersion = CurrentClaimCatalogVersion;
        return changed;
    }
    /// <summary>
    /// 将诉求类别转化为实例
    /// </summary>
    /// <returns></returns>
    public List<TemporaryFaction> ConvertToObjectFromFactionType()
    {
        var result = new List<TemporaryFaction>();
        var typesToBuild = TemporaryFactionTypesRecord; // 你的属性：List<TemporaryFactionType>
        if (typesToBuild == null || typesToBuild.Count == 0)
            return result;

        foreach (var e in typesToBuild)
        {
            if (FactionManager.Config.StoredTemporaryFaction.TryGetValue(e, out var value))
            {
                var tf = TemporaryFactions?.Find(tf => tf.type == e);
                var new_tf = value.Clone(this);
                if (tf != null)
                {
                    new_tf.CopyRuntimeStateFrom(tf);
                }
                result.Add(new_tf);
            }
        }
        return result;
    }
    public float CalcPossibility(Actor pActor, float minProb = 0.5f, float maxProb = 0.95f)
    {
        int required = RequiredTraits?.Count ?? 0;
        if (required <= 0) { LastJoinProb = minProb; return LastJoinProb; }

        int matched = 0;
        if (RequiredTraits != null)
            foreach (var trait in RequiredTraits)
                if (pActor.hasTrait(trait))
                    matched++;

        // 匹配占比 0~1
        float ratio = (float) matched / required;

        // 线性插值：匹配越多，越接近 maxProb
        float prob = minProb + (maxProb - minProb) * ratio;

        // 存一下并返回
        LastJoinProb = prob;
        if (only_king && !pActor.isKing())
        {
            return 0;
        }
        return prob;
    }
    public void AddMember(Actor pActor)
    {
        if (pActor == null || pActor.isRekt()) return;
        pActor.GetOrCreate().factionID = GetID();
        if (!Members.Contains(pActor.id)) Members.Add(pActor.id);
        if (GetLeader() == null)
        {
            SetLeader(pActor);
        }
    }

    public void Update()
    {
        if (Ban || Empire == null || Empire.isRekt() || Empire.IsArchived()) return;
        Members ??= new List<long>();
        Members.RemoveAll(id =>
        {
            Actor member = World.world.units.get(id);
            return member == null || member.isRekt() || !member.isAlive() || member.kingdom?.GetEmpire() != Empire ||
                   member.GetFaction() != this;
        });

        if (GetLeader() != null) return;
        Actor candidate = Members.Select(id => World.world.units.get(id))
            .Where(IsEligibleLeader)
            .OrderByDescending(actor => actor.data?.renown ?? 0)
            .ThenByDescending(actor => actor.GetIdentity()?.TotalPerformance ?? 0d)
            .FirstOrDefault();
        candidate ??= Empire.getUnits().Where(actor => IsEligibleLeader(actor) && !actor.HasFaction() &&
                actor.IsOnOffice() && actor.HasOfficeIdentity())
            .OrderByDescending(actor => actor.data?.renown ?? 0).FirstOrDefault();
        candidate ??= Empire.getUnits().Where(actor => IsEligibleLeader(actor) && !actor.HasFaction())
            .OrderByDescending(actor => actor.data?.renown ?? 0).FirstOrDefault();
        if (candidate == null) return;
        if (candidate.GetFaction() != this) candidate.SetFaction(this);
        else SetLeader(candidate);
    }

    private bool IsEligibleLeader(Actor actor)
    {
        return actor != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() &&
               actor != Empire?.Emperor && actor.kingdom?.GetEmpire() == Empire;
    }

    public Actor GetLeader()
    {
        Actor leader = World.world.units.get(Leader);
        if (!IsEligibleLeader(leader) || Members?.Contains(Leader) != true || leader.GetFaction() != this)
        {
            return null;
        }
        return leader;
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
        long newLeaderId = pActor?.id ?? id;
        Actor newLeader = pActor ?? World.world.units.get(newLeaderId);
        if (newLeaderId <= 0 || !IsEligibleLeader(newLeader)) return;
        if (newLeader.GetFaction() != this)
        {
            newLeader.SetFaction(this);
            if (Leader == newLeaderId) return;
        }
        if (Leader == newLeaderId) return;
        Leader = newLeaderId;
        if (!Members.Contains(newLeaderId))
        {
            Members.Add(newLeaderId);
        }

        Actor leader = GetLeader();
        if (leader != null)
        {
            TranslateHelper.LogOfficerBecomeFactionLeader(leader,this);
            leader.RecordPersonalHistory(string.Format(LM.Get("personal_history_became_faction_leader"), Name));
        }
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

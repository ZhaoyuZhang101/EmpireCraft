using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
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
                    { TemporaryFactionType.强者继承法, TemporaryFactionType.宗教同化, TemporaryFactionType.索取皇位, TemporaryFactionType.文化同化 }
            },
            {
                FactionType.血脉,
                new List<TemporaryFactionType>
                    { TemporaryFactionType.转世袭, TemporaryFactionType.神授君权, TemporaryFactionType.索取皇位, TemporaryFactionType.文化转化 }
            },
            {
                FactionType.尊王,
                new List<TemporaryFactionType>
                {
                    TemporaryFactionType.削藩, TemporaryFactionType.夺取诸侯开战权, TemporaryFactionType.扩张地盘,
                    TemporaryFactionType.转天朝制度, TemporaryFactionType.索取皇位, TemporaryFactionType.谋求统一,
                    TemporaryFactionType.复辟帝制, TemporaryFactionType.修氏族志, TemporaryFactionType.抑豪强, TemporaryFactionType.文化同化
                }
            },
            {
                FactionType.诸侯,
                new List<TemporaryFactionType>
                    { TemporaryFactionType.分封, TemporaryFactionType.允许诸侯自由开战, TemporaryFactionType.索取皇位, TemporaryFactionType.文化转化 }
            },
            {
                FactionType.中央,
                new List<TemporaryFactionType>
                {
                    TemporaryFactionType.削藩, TemporaryFactionType.夺取诸侯开战权, TemporaryFactionType.扩张地盘,
                    TemporaryFactionType.转天朝制度, TemporaryFactionType.索取皇位, TemporaryFactionType.谋求统一,
                    TemporaryFactionType.恢复世袭皇权, TemporaryFactionType.颁布帝国治安令,
                    TemporaryFactionType.争夺主教叙任权, TemporaryFactionType.抑豪强, TemporaryFactionType.文化同化
                }
            },
            {
                FactionType.自治,
                new List<TemporaryFactionType>
                {
                    TemporaryFactionType.分封, TemporaryFactionType.允许诸侯自由开战,
                    TemporaryFactionType.索取皇位, TemporaryFactionType.颁布金玺诏书,
                    TemporaryFactionType.确认诸侯特权, TemporaryFactionType.文化转化,
                    // 有劫掠传统的文化(维京)由领主出海劫掠(见 ClaimRules.json)
                    TemporaryFactionType.劫掠
                }
            },
            {
                FactionType.攘夷,
                new List<TemporaryFactionType>
                {
                    TemporaryFactionType.对外扩张, TemporaryFactionType.文化同化, TemporaryFactionType.转军府,
                    TemporaryFactionType.谋求统一, TemporaryFactionType.迫使朝贡, TemporaryFactionType.索取皇位
                }
            },
            {
                FactionType.绥靖,
                new List<TemporaryFactionType>
                {
                    TemporaryFactionType.撤销军府, TemporaryFactionType.提供岁币, TemporaryFactionType.割让城池,
                    TemporaryFactionType.削藩, TemporaryFactionType.设置行政区, TemporaryFactionType.开科取士,
                    TemporaryFactionType.推行三省六部, TemporaryFactionType.设立内阁,
                    TemporaryFactionType.索取皇位, TemporaryFactionType.开放党禁, TemporaryFactionType.修氏族志, TemporaryFactionType.文化转化
                }
            },
            {
                FactionType.原始,
                new List<TemporaryFactionType>
                {
                    TemporaryFactionType.索取皇位, TemporaryFactionType.分封, TemporaryFactionType.加强神权, TemporaryFactionType.扩张地盘,
                    // 有劫掠传统的部落(草原、维京)出塞出海劫掠(见 ClaimRules.json)
                    TemporaryFactionType.劫掠
                }
            },
            {
                FactionType.共和,
                new List<TemporaryFactionType>
                {
                    TemporaryFactionType.提高赋税, TemporaryFactionType.提高福利, TemporaryFactionType.清除移民,
                    TemporaryFactionType.缩减金融霸权, TemporaryFactionType.开放党禁, TemporaryFactionType.文化转化
                }
            },
            {
                FactionType.民主,
                new List<TemporaryFactionType>
                {
                    TemporaryFactionType.开放移民, TemporaryFactionType.降低赋税, TemporaryFactionType.提高福利,
                    TemporaryFactionType.拓展金融霸权, TemporaryFactionType.推行普选, TemporaryFactionType.开放党禁
                }
            },
            {
                FactionType.革命,
                new List<TemporaryFactionType> { TemporaryFactionType.输出革命, TemporaryFactionType.扶持革命党, TemporaryFactionType.禁党 }
            },
            {
                FactionType.共产,
                new List<TemporaryFactionType>
                {
                    TemporaryFactionType.输出革命, TemporaryFactionType.扶持革命党, TemporaryFactionType.禁党,
                    TemporaryFactionType.土地改革, TemporaryFactionType.提高福利
                }
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
                    TemporaryFactionType.宗教融入, TemporaryFactionType.制度融入, TemporaryFactionType.文化转化,
                    TemporaryFactionType.分封, TemporaryFactionType.谋求统一, TemporaryFactionType.索取皇位
                }
            },
            {
                FactionType.同化,
                new List<TemporaryFactionType>
                {
                    TemporaryFactionType.宗教同化, TemporaryFactionType.游牧化, TemporaryFactionType.劫掠,
                    TemporaryFactionType.游牧扩张, TemporaryFactionType.对外扩张, TemporaryFactionType.索取皇位, TemporaryFactionType.文化同化
                }
            },
            {
                // 后党(见 CliqueSystem)
                FactionType.外戚,
                new List<TemporaryFactionType>
                {
                    TemporaryFactionType.太后临朝, TemporaryFactionType.封赏后族, TemporaryFactionType.册立储君,
                    TemporaryFactionType.转世袭, TemporaryFactionType.索取皇位
                }
            },
            {
                // 阉党
                FactionType.宦官,
                new List<TemporaryFactionType>
                {
                    TemporaryFactionType.宦官掌兵, TemporaryFactionType.恢复世袭皇权, TemporaryFactionType.颁布帝国治安令,
                    TemporaryFactionType.提高赋税
                }
            }
        };

    public static PlayerFactionConfig Config = new PlayerFactionConfig();

    static FactionManager()
    {
        foreach (var claims in FactionConfig.Values)
            if (!claims.Contains(TemporaryFactionType.开放党禁)) claims.Add(TemporaryFactionType.开放党禁);
    }

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
                    Config = JsonConvert.DeserializeObject<PlayerFactionConfig>(text) ?? new PlayerFactionConfig();
                    // 读不出类型而跳过的诉求是空值，清掉免得后面克隆时出错
                    Config.StoredTemporaryFaction ??= new Dictionary<TemporaryFactionType, TemporaryFaction>();
                    foreach (TemporaryFactionType key in Config.StoredTemporaryFaction
                                 .Where(pair => pair.Value == null).Select(pair => pair.Key).ToList())
                        Config.StoredTemporaryFaction.Remove(key);
                    foreach (FixedFaction saved in (Config.PlayerFactions ?? new List<FixedFaction>())
                             .Concat(Config.PlayerRegimeFactions?.Values.SelectMany(list => list ?? new List<FixedFaction>()) ??
                                     Enumerable.Empty<FixedFaction>()))
                        saved?.TemporaryFactions?.RemoveAll(claim => claim == null);
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
    无,
    外戚,  //后党(朋党，见 CliqueSystem)；追加在末尾
    宦官   //阉党
}
public class FixedFaction
{
    // 2：派系诉求重新分配(血脉、攘夷、绥靖、尊王、民主、融入、同化)
    // 3：加修氏族志(尊王、绥靖)、抑豪强(尊王、中央)，旧存档的派系按新清单重建一次
    // 4：劫掠也给领主(自治)、部落(原始)派；5：文化同化、文化转化开给各政体
    // 6：中央(法家变法派)也能提转天朝制度
    public const int CurrentClaimCatalogVersion = 6;
    public string _id;
    public bool only_king;
    //特质需求（拥有该特质的人会更加倾向于加入此派系）
    public List<string> _requiredTraits = new();
    public List<string> RequiredTraits = new();
    public FactionType Type { get; set; }
    public bool Ban { get; set; } = false;
    // —— 政党(开放党禁后派系改组为政党，见 PartySystem) ——
    public bool IsParty { get; set; }
    public PartyIdeology Ideology { get; set; } = PartyIdeology.Centrism;
    public string OriginFactionName { get; set; } = "";
    public long PartyFounderId { get; set; } = -1L;
    public double PartyFoundedAt { get; set; } = -1d;
    public int ZeroSeatElections { get; set; }
    // —— 朋党(党禁前的华夏朝堂派系像政党一样兴衰，见 CliqueSystem)：理念记在 Ideology ——
    public bool IsClique { get; set; }
    public double CliqueFoundedAt { get; set; } = -1d;
    // —— 手动设定(添加派系窗口、派系详情，见 FactionEditor) ——
    // 理念是手选的：不再由阶层倾向推算(以前自定义派系没有阶层倾向，一律落在中间派)
    public bool IdeologyChosen { get; set; }
    // 起名方式：0 自定(手改的名字)，1 默认名(随理念)，2 魁首之姓(魁首换人跟着改，如"严党")
    public int NameMode { get; set; }
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
    // 抽象阶层政治基础：亲和度(-100~100)是派系的长期定位；好感与支持份额(0~100)
    // 会被诉求、制度和年度竞争改变。它们不代表具体人物入党。
    public Dictionary<SocialClass, float> ClassAffinities = new();
    public Dictionary<SocialClass, float> ClassFavor = new();
    public Dictionary<SocialClass, float> ClassSupport = new();
    public Dictionary<SocialClass, string> ClassFavorSources = new();
    [JsonIgnore] public float ClassPower;
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
    // 诉求清单：政党用本理念的诉求(已存在 Record 里)；派系按类型取配置，类型没配诉求(如共产)时给空表而不是 null
    private List<TemporaryFactionType> GetClaimCatalog() =>
        IsParty && TemporaryFactionTypesRecord != null
            ? TemporaryFactionTypesRecord
            : TemporaryFactionTypes ?? TemporaryFactionTypesRecord ?? new List<TemporaryFactionType>();

    // 存档用的副本：保留 id、名称、党员、领袖、理念、阶层基础等全部持久字段；
    // 诉求对象(抽象类，无法反序列化)不存，读档后按诉求清单重新生成(FixMissedTemporaryFactions)
    public FixedFaction ToSaveCopy()
    {
        var copy = (FixedFaction)MemberwiseClone();
        copy.Members = new List<long>(Members ?? new List<long>());
        copy.TemporaryFactions = null;
        copy.LockButton = null;
        copy.CardUI = null;
        return copy;
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
            ClassAffinities = new Dictionary<SocialClass, float>(ClassAffinities ?? new()),
            ClassFavor = new Dictionary<SocialClass, float>(ClassFavor ?? new()),
            ClassSupport = new Dictionary<SocialClass, float>(ClassSupport ?? new()),
            ClassFavorSources = new Dictionary<SocialClass, string>(ClassFavorSources ?? new()),
            Leader = -1L,
            TemporaryFactionTypesRecord = new List<TemporaryFactionType>(GetClaimCatalog()),
            ClaimCatalogVersion = CurrentClaimCatalogVersion,
            IsParty = IsParty,
            Ideology = Ideology,
            OriginFactionName = OriginFactionName,
            PartyFounderId = PartyFounderId,
            PartyFoundedAt = PartyFoundedAt,
            IsClique = IsClique,
            CliqueFoundedAt = CliqueFoundedAt,
            IdeologyChosen = IdeologyChosen,
            NameMode = NameMode
        };
        TemporaryFactionTypesRecord = new List<TemporaryFactionType>(GetClaimCatalog());
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
            ClassAffinities = new Dictionary<SocialClass, float>(ClassAffinities ?? new()),
            ClassFavor = new Dictionary<SocialClass, float>(ClassFavor ?? new()),
            ClassSupport = new Dictionary<SocialClass, float>(ClassSupport ?? new()),
            ClassFavorSources = new Dictionary<SocialClass, string>(ClassFavorSources ?? new()),
            Leader = -1L,
            TemporaryFactions = TemporaryFactions,
            TemporaryFactionTypesRecord = TemporaryFactionTypesRecord,
            ClaimCatalogVersion = ClaimCatalogVersion,
            IsParty = IsParty,
            Ideology = Ideology,
            OriginFactionName = OriginFactionName,
            PartyFounderId = PartyFounderId,
            PartyFoundedAt = PartyFoundedAt,
            IsClique = IsClique,
            CliqueFoundedAt = CliqueFoundedAt,
            IdeologyChosen = IdeologyChosen,
            NameMode = NameMode
        };
        newFaction.TemporaryFactions = newFaction.ConvertToObjectFromFactionType();
        newFaction.TemporaryFactions.ForEach(tf=>tf.Init(newFaction));
        return newFaction;
    }

    public void FixMissedTemporaryFactions()
    {
        bool catalogChanged = false;
        // Eligibility follows population pressure and institutional prerequisites.
        TemporaryFactionTypesRecord ??= new List<TemporaryFactionType>();
        if (!TemporaryFactionTypesRecord.Contains(TemporaryFactionType.开放党禁))
        {
            TemporaryFactionTypesRecord.Add(TemporaryFactionType.开放党禁);
            catalogChanged = true;
        }
        // 公共议题(赋税、战和、取士)：每个派系都能上奏，见 CourtIssues
        if (!IsParty)
            foreach (TemporaryFactionType issue in GeneralSystems.CourtIssues.PublicIssues)
            {
                if (TemporaryFactionTypesRecord.Contains(issue)) continue;
                TemporaryFactionTypesRecord.Add(issue);
                catalogChanged = true;
            }
        if (ClaimCatalogVersion < CurrentClaimCatalogVersion && Empire?.CoreKingdom != null)
        {
            if (ClaimCatalogVersion < 2 && !IsParty) catalogChanged |= MigrateClaimCatalogV2();
            if (ClaimCatalogVersion < 3 && !IsParty) catalogChanged |= MigrateClaimCatalogV3();
            if (ClaimCatalogVersion < 4 && !IsParty) catalogChanged |= MigrateClaimCatalogV4();
            if (ClaimCatalogVersion < 5 && !IsParty) catalogChanged |= MigrateClaimCatalogV5();
            if (Empire.CoreKingdom.GetRegime()?.type == RegimeType.Feudalism)
                catalogChanged |= MigrateFeudalClaimCatalog();
            ClaimCatalogVersion = CurrentClaimCatalogVersion;
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
            if (IsParty && tf != null) tf.ShowAsPlot = false;
            if (Empire != null)
            {
                tf.SetEmpire(Empire); // ← 始终把 EmpireId 等运行时信息灌进去
            }
        }
    }

    // 版本 2：诉求重新分配后，旧存档里的派系照新清单增删(只动这次调整过的诉求，制度解锁等别处加的不动)
    private bool MigrateClaimCatalogV2()
    {
        TemporaryFactionTypesRecord ??= new List<TemporaryFactionType>();
        (TemporaryFactionType[] remove, TemporaryFactionType[] add) = Type switch
        {
            FactionType.血脉 => (new[] { TemporaryFactionType.宗教同化 }, new[] { TemporaryFactionType.神授君权 }),
            FactionType.攘夷 => (new[] { TemporaryFactionType.转周制 }, Array.Empty<TemporaryFactionType>()),
            FactionType.绥靖 => (Array.Empty<TemporaryFactionType>(),
                new[] { TemporaryFactionType.推行三省六部, TemporaryFactionType.设立内阁 }),
            FactionType.尊王 => (Array.Empty<TemporaryFactionType>(), new[] { TemporaryFactionType.复辟帝制 }),
            FactionType.民主 => (Array.Empty<TemporaryFactionType>(), new[] { TemporaryFactionType.推行普选 }),
            FactionType.融入 => (new[] { TemporaryFactionType.劫掠, TemporaryFactionType.游牧扩张 },
                Array.Empty<TemporaryFactionType>()),
            FactionType.同化 => (new[] { TemporaryFactionType.分封, TemporaryFactionType.谋求统一 },
                new[] { TemporaryFactionType.游牧扩张 }),
            _ => (Array.Empty<TemporaryFactionType>(), Array.Empty<TemporaryFactionType>())
        };
        bool changed = false;
        foreach (TemporaryFactionType claim in remove)
        {
            if (TemporaryFactions?.Any(tf => tf != null && tf.type == claim && tf.IsStarted()) == true) continue;
            changed |= TemporaryFactionTypesRecord.Remove(claim);
        }
        foreach (TemporaryFactionType claim in add)
        {
            if (TemporaryFactionTypesRecord.Contains(claim)) continue;
            TemporaryFactionTypesRecord.Add(claim);
            changed = true;
        }
        return changed;
    }

    // 版本 3：世家(见 GentrySystem)——尊王加修氏族志、抑豪强，绥靖加修氏族志，中央加抑豪强
    private bool MigrateClaimCatalogV3()
    {
        TemporaryFactionTypesRecord ??= new List<TemporaryFactionType>();
        TemporaryFactionType[] add = Type switch
        {
            FactionType.尊王 => new[] { TemporaryFactionType.修氏族志, TemporaryFactionType.抑豪强 },
            FactionType.绥靖 => new[] { TemporaryFactionType.修氏族志 },
            FactionType.中央 => new[] { TemporaryFactionType.抑豪强 },
            _ => Array.Empty<TemporaryFactionType>()
        };
        bool changed = false;
        foreach (TemporaryFactionType claim in add)
        {
            if (TemporaryFactionTypesRecord.Contains(claim)) continue;
            TemporaryFactionTypesRecord.Add(claim);
            changed = true;
        }
        return changed;
    }

    // 版本 4：劫掠按文化特性(用户 2026-10-19 选"加文化特性与节点过滤")——领主(自治)、部落(原始)也能提，有劫掠传统的文化才提得出
    private bool MigrateClaimCatalogV4()
    {
        TemporaryFactionTypesRecord ??= new List<TemporaryFactionType>();
        if (Type is not (FactionType.自治 or FactionType.原始) ||
            TemporaryFactionTypesRecord.Contains(TemporaryFactionType.劫掠)) return false;
        TemporaryFactionTypesRecord.Add(TemporaryFactionType.劫掠);
        return true;
    }

    // 版本 5：文化同化、文化转化开给各政体(用户 2026-10-19 选"可和平同化、留少数族群"；以前同化只有攘夷派、
    // 转化只有融入派——封建、阿拉伯、周制、幕府、城邦帝国都同化不了、也改不了法理文化)
    private bool MigrateClaimCatalogV5()
    {
        TemporaryFactionTypesRecord ??= new List<TemporaryFactionType>();
        TemporaryFactionType? claim = Type switch
        {
            FactionType.中央 or FactionType.僭主 or FactionType.尊王 or FactionType.同化 => TemporaryFactionType.文化同化,
            FactionType.自治 or FactionType.血脉 or FactionType.绥靖 or FactionType.诸侯 or FactionType.共和 =>
                TemporaryFactionType.文化转化,
            _ => null
        };
        if (claim == null || TemporaryFactionTypesRecord.Contains(claim.Value)) return false;
        TemporaryFactionTypesRecord.Add(claim.Value);
        return true;
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
        if (pActor == null || pActor.isRekt() || pActor.IsWarMachine()) return;
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
        FactionClassSystem.EnsureProfile(this);
        Members ??= new List<long>();
        Members.RemoveAll(id =>
        {
            Actor member = World.world.units.get(id);
            return member == null || member.isRekt() || !member.isAlive() || member.kingdom?.GetEmpire() != Empire ||
                   member.GetFaction() != this;
        });

        Actor currentLeader = GetLeader();
        if (currentLeader != null)
        {
            if (!OfficeSelector.IsPreferredOfficeCandidate(currentLeader, Empire.CoreKingdom))
            {
                Actor preferredMember = Members.Select(id => World.world.units.get(id))
                    .Where(actor => IsEligibleLeader(actor) && actor.IsOnOffice() && actor.HasOfficeIdentity() &&
                                    OfficeSelector.IsPreferredOfficeCandidate(actor, Empire.CoreKingdom))
                    .OrderByDescending(actor => actor.data?.renown ?? 0)
                    .ThenByDescending(actor => actor.GetIdentity()?.TotalPerformance ?? 0d)
                    .FirstOrDefault();
                if (preferredMember != null) SetLeader(preferredMember);
            }
            return;
        }
        Actor candidate = Members.Select(id => World.world.units.get(id))
            .Where(IsEligibleLeader)
            .OrderByDescending(actor => OfficeSelector.IsPreferredOfficeCandidate(actor, Empire.CoreKingdom))
            .ThenByDescending(actor => actor.data?.renown ?? 0)
            .ThenByDescending(actor => actor.GetIdentity()?.TotalPerformance ?? 0d)
            .FirstOrDefault();
        candidate ??= Empire.getUnits().Where(actor => IsEligibleLeader(actor) && !actor.HasFaction() &&
                actor.IsOnOffice() && actor.HasOfficeIdentity())
            .OrderByDescending(actor => OfficeSelector.IsPreferredOfficeCandidate(actor, Empire.CoreKingdom))
            .ThenByDescending(actor => actor.data?.renown ?? 0).FirstOrDefault();
        candidate ??= Empire.getUnits().Where(actor => IsEligibleLeader(actor) && !actor.HasFaction())
            .OrderByDescending(actor => OfficeSelector.IsPreferredOfficeCandidate(actor, Empire.CoreKingdom))
            .ThenByDescending(actor => actor.data?.renown ?? 0).FirstOrDefault();
        if (candidate == null) return;
        if (candidate.GetFaction() != this) candidate.SetFaction(this);
        else SetLeader(candidate);
        FactionEditor.FollowLeaderName(this);
    }

    private bool IsEligibleLeader(Actor actor)
    {
        return actor != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() &&
               !actor.IsWarMachine() && actor != Empire?.Emperor && actor.kingdom?.GetEmpire() == Empire;
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
        // 按魁首之姓起名的派系换了魁首跟着改名(见 FactionEditor)
        FactionEditor.FollowLeaderName(this);

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

using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using EmpireCraft.Scripts.GeneralSystems;

namespace EmpireCraft.Scripts.Data;

// 宪法(见 ConstitutionSystem)。现代国家(共和国、君主立宪国)各有一部，存在 ConstitutionalEconomyState 里随存档保存。
// 条款分两类：
//   · 由现有制度推出的(国体、权力中心、元首产生、选举权、政党制度)：每年按实际状况同步，玩家改过的除外；
//   · 颁布时按执政理念定下的(央地关系、经济、宗教、紧急状态、修宪门槛、任期)：之后只有修宪/玩家修改才变。
// 条款枚举按名字存档(StringEnumConverter)，调整选项顺序不会读错旧存档。

public enum ConstitutionFormOfState { ConstitutionalMonarchy, Republic }

// 权力中心：谁实际掌握行政权
public enum ConstitutionPowerCenter
{
    Presidential,       // 总统制：元首掌行政
    Parliamentary,      // 议会内阁制：议会多数产生的首相/总理掌行政
    SemiPresidential,   // 半总统制：元首与总理分掌
    Congress,           // 代表大会制(民主集中制)
    DualMonarchy,       // 二元君主制：有议会但内阁对君主负责
    MilitaryJunta       // 军政府(宪法中止时)
}

public enum ConstitutionHeadSelection
{
    Hereditary,         // 世袭(君主立宪)
    PopularVote,        // 全民普选
    ParliamentVote,     // 议会选举
    CongressVote,       // 代表大会选举
    PartyNomination,    // 执政党推举(没有议会)
    Designated          // 前任指定
}

public enum ConstitutionSuffrage { None, Property, Male, Universal }

public enum ConstitutionPartySystem { MultiParty, UnitedFront, OneParty, Banned }

public enum ConstitutionTerritory { Unitary, Federal }

public enum ConstitutionEconomy { PrivateProperty, Mixed, Planned }

public enum ConstitutionReligion { Secular, StateReligion }

public enum ConstitutionEmergency { Prohibited, Allowed }

public enum ConstitutionAmendment { ParliamentSupermajority, Referendum, PartyCongress }

// 意识形态强度。Medium 排第一：旧存档没有这一条时按"中等"读入
public enum ConstitutionIdeologyIntensity { Medium, High, Low }

// 言论自由。Limited 排第一：旧存档没有这一条时按"一般"读入
public enum ConstitutionSpeech { Limited, Free, Strict }

// 民族政策。Moderate 排第一：旧存档没有这一条时按"一般"读入
public enum ConstitutionNation { Moderate, Pluralist, Nationalist }

// 部门主官始终属于政治任命。混合制与职业文官制保留常任事务岗位；
// 职业文官制增加中立和廉洁收益，政党分肥制连事务岗位也随执政党更替。
public enum ConstitutionCivilService { Mixed, Professional, Spoils }

// 耕地红线(见 FarmlandSystem)：按生产力(红线比例随农业生产力下降) / 严守红线(固定比例) / 不设红线(规划农田不受保护)。
// Adaptive 排第一：旧存档没有这一条时按"按生产力"读入
public enum ConstitutionFarmland { Adaptive, Strict, None }

// 粮食征收(见 GranarySystem)：征收粮赋(收成三成交国家粮仓) / 留归地方(全留本城) / 统购统销(七成交国家粮仓)。
// Tribute 排第一：旧存档没有这一条时按"征收粮赋"读入
public enum ConstitutionGrain { Tribute, Local, Central }

public sealed class ConstitutionClauses
{
    [JsonConverter(typeof(StringEnumConverter))]
    public ConstitutionFormOfState form_of_state;
    // 国体(立国理念)：决定国号后缀与临时政府称呼；共和国与 ConstitutionalEconomyState.republic_ideology 同步，
    // 只有革命或修宪(含修改临时约法)才会改变，不随执政党轮替
    [JsonConverter(typeof(StringEnumConverter))]
    public PartyIdeology founding_ideology;
    [JsonConverter(typeof(StringEnumConverter))]
    public ConstitutionPowerCenter power_center;
    [JsonConverter(typeof(StringEnumConverter))]
    public ConstitutionHeadSelection head_selection;
    [JsonConverter(typeof(StringEnumConverter))]
    public ConstitutionSuffrage suffrage;
    [JsonConverter(typeof(StringEnumConverter))]
    public ConstitutionPartySystem party_system;
    [JsonConverter(typeof(StringEnumConverter))]
    public ConstitutionTerritory territory;
    [JsonConverter(typeof(StringEnumConverter))]
    public ConstitutionEconomy economy;
    [JsonConverter(typeof(StringEnumConverter))]
    public ConstitutionReligion religion;
    [JsonConverter(typeof(StringEnumConverter))]
    public ConstitutionEmergency emergency;
    [JsonConverter(typeof(StringEnumConverter))]
    public ConstitutionAmendment amendment;
    // 意识形态强度：高 = 高强度宣传立国理念，中 = 一般，低 = 放开人民的意识形态(见 IdeologyEducationSystem)
    [JsonConverter(typeof(StringEnumConverter))]
    public ConstitutionIdeologyIntensity ideology_intensity;
    // 言论自由：宽松 = 各种理念著作频繁出版；严格 = 出版的多是立国理念的著作(见 SpeechFreedomSystem)
    [JsonConverter(typeof(StringEnumConverter))]
    public ConstitutionSpeech speech;
    // 民族政策：多民族共存 / 一般 / 民族主义立国(见 NationalSentimentSystem)
    [JsonConverter(typeof(StringEnumConverter))]
    public ConstitutionNation nation;
    // 文官制度(旧存档没有这一条，按混合制)
    [JsonConverter(typeof(StringEnumConverter))]
    public ConstitutionCivilService civil_service;
    // 耕地红线(旧存档没有这一条，按生产力)
    [JsonConverter(typeof(StringEnumConverter))]
    public ConstitutionFarmland farmland;
    // 粮食征收(旧存档没有这一条，按征收粮赋)
    [JsonConverter(typeof(StringEnumConverter))]
    public ConstitutionGrain grain;
    // 元首(君主立宪为首相)一届的年数，也是议会改选周期(见 ParliamentSystem.TermYears)
    public int term_years = 10;
    // 旧存档的任期按游戏时间换算过(以前 2~8 年太短，换届太快，决议推不完)
    public bool term_rescaled;
    // 最多连任几届；0 = 不限
    public int max_terms;

    public ConstitutionClauses Clone() => (ConstitutionClauses)MemberwiseClone();
}

public sealed class ConstitutionAmendmentRecord
{
    public double time = -1d;
    public string clause = "";
    public string from = "";
    public string to = "";
    // 谁改的：player / party:<政党 id> / sync(随制度变化)
    public string by = "";
}

public sealed class ConstitutionData
{
    // true = 临时约法(共和过渡期)，正式宪法颁布后被取代
    public bool provisional;
    public string name = "";
    // 本国第几部正式宪法(临时约法不计)
    public int number;
    public double promulgated_at = -1d;
    public ConstitutionClauses clauses = new();
    // 玩家亲手改过的条款名(见 ConstitutionSystem.ClauseNames)：年度同步不再覆盖
    public List<string> player_locked = new();
    public List<ConstitutionAmendmentRecord> amendments = new();
    // 宪法中止(紧急状态、政变)，后续步骤使用
    public bool suspended;
    public double suspended_since = -1d;
    // 宪政稳定度 0~100，后续步骤使用
    public float stability = 50f;
    public double last_sync = -1d;
}

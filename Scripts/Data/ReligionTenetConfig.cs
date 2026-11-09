using System.Collections.Generic;

namespace EmpireCraft.Scripts.Data;

// 宗教教义、本土加成、融合、启示宗教的参数(读自模组根目录 ReligionTenets.json，读不到用这里的默认值；见 ReligionTenets)。
// 用户 2026-11-06 选：加成强度"中"、教义用模组自己的教义表、一神教"两段式"、融合"按类型分槽位"。
public sealed class ReligionTenetFile
{
    public ReligionHomeConfig home = new();
    public ReligionSyncretismConfig syncretism = new();
    public ReligionRevelationConfig revelation = new();
    public List<ReligionTenetDef> tenets = new();
}

// 本土加成：任何宗教在自己的天下(宗教的天下 = 城的文化天下)
public sealed class ReligionHomeConfig
{
    // 本天下的城惰性 +50%
    public float inertia = 0.5f;
    // 国教是本天下的本土宗教、国家文化也在本天下的君主，对外天下宗教的皈依意愿 ×0.3
    public float resist = 0.3f;
    // 奉本天下正教、治下过半人口同教：正统 +3
    public int legitimacy = 3;
    public float legitimacy_share = 0.5f;
    // 与国教同教、又是本天下宗教的城稳定 +2
    public float concord = 2f;
}

// 融合(见 SyncretismSystem)
public sealed class ReligionSyncretismConfig
{
    public bool enabled = true;
    // 接触度：每年 +exposure_per_year × 渠道权重 × 开放度；满 min_exposure 且接触满 min_years 年吸收一条，每教 cooldown_years 年至多一条
    public float exposure_per_year = 10f, min_exposure = 40f, max_exposure = 100f;
    public int min_years = 10, cooldown_years = 20;
    // 这一年没有接触的来源，接触度每年减这么多
    public float decay_per_year = 2f;
    // 每教记的接触来源至多几个(多了去掉接触度最低的)
    public int max_sources = 12;
    // 融来的教义按几成生效
    public float absorbed_strength = 0.6f;
    // 以融合御同化：A 每从 B(同一宗系)吸一条，B 对 A 的势能、A 国君主皈依 B 的意愿 ×pull_factor，至多降到 pull_floor
    public float pull_factor = 0.85f, pull_floor = 0.55f;
    // 施行宽容制度(religious_tolerance：三教并立、神佛习合、游牧宽容、世俗化)的：开放度 ×1.5、槽位 +1
    public float tolerance_openness = 1.5f;
    public int tolerance_slots = 1;
    // 渠道权重(每对每年取最大的一条)：被异教国统治或治下有异教之城、同国并存、两教之城相通(×通达)、朝贡与同盟
    public float ruled_weight = 1f, same_realm_weight = 0.6f, neighbour_weight = 0.5f, tributary_weight = 0.3f;
    // 皈依事件：君主皈依 B 的权重 ≥1 却没成，A 一次性 +10；A 国改奉 B 后，B 对 A 一次性 +20
    public float failed_conversion = 10f, after_conversion = 20f;
    // 升格：民间、腾格里、神道融到 organized_min 条、其中有"经典"或"仿效教阶"，在本天下可当皈依候选，传教 +organized_mission
    public int organized_min = 2;
    public float organized_mission = 0.1f;
    // 各类型的底子(本有教义里的槽位、开放度另加)
    public Dictionary<string, int> slots = new()
    {
        ["Folk"] = 2, ["Tengri"] = 3, ["Sanjiao"] = 2, ["Shinto"] = 2, ["Dharmic"] = 3, ["Church"] = 1, ["Islam"] = 1, ["Judaism"] = 0
    };
    public Dictionary<string, float> openness = new()
    {
        ["Folk"] = 1f, ["Tengri"] = 0.8f, ["Sanjiao"] = 0.8f, ["Shinto"] = 0.9f, ["Dharmic"] = 0.7f, ["Church"] = 0.3f, ["Islam"] = 0.2f,
        ["Judaism"] = 0.1f
    };
}

// 两段式立教(见 NativeFaithSystem)：教会、伊斯兰的天下先在礼仪祭祀时立"古神"(民间型)，
// 有了 techs、古神立满 min_years 年起每年 chance 立启示宗教；grace_years 年内不并入古神正教
public sealed class ReligionRevelationConfig
{
    public bool enabled = true;
    public int min_years = 60;
    public float chance = 0.02f;
    public List<string> techs = new() { "ironworking", "writing" };
    public int grace_years = 150;
    // 启示宗教还没成正教时，本天下奉古神的君主每年被它拉的权重(不必接壤；皈依几率 = 0.02 × 权重 × 意愿)
    public float realm_pull = 0.5f;
    // 剧本(科技年历开着)：不早于这一年
    public Dictionary<string, int> calendar_year = new() { ["Church"] = 30, ["Islam"] = 610 };
}

// 一条教义。kind：doctrine 教理(不外传)、ethic 伦理(不排他的才能吸)、practice 仪轨(谁都能吸)、
// organization 组织(只给民族宗教)、saint 本地圣徒(普世宗教只吸这一种，按来源的天下记)、structure 结构(槽位、开放度，不外传)
public sealed class ReligionTenetDef
{
    public string id = "";
    public string kind = "practice";
    // 本有于哪些类型(FaithType 名)、哪些天下的宗教
    public List<string> faiths = new();
    public List<string> spheres = new();
    // 效果(本有的足额，融来的按 absorbed_strength)：
    //   mission 传教力 +x、inertia 惰性 +x、home_inertia 本天下惰性再 +x、saint_inertia 在来源的天下惰性 +x(本地圣徒)、
    //   resist 本土抗皈依(意愿 ×(1−x))、defense 本土御侮战力 +x、temple_research 庙宇科研 +x、holy_city 圣地势能 +x、
    //   concord 同教之城稳定 +x、openness 开放度 +x、slots 融合槽 +n、legitimacy 正统 +n(治下 legitimacy_share 以上同教)、
    //   organizes 算"经典/教阶"(升格用)、capture_immune 圣战攻城不改宗本教之城
    public float mission, inertia, home_inertia, saint_inertia, resist, defense, temple_research, holy_city, concord, openness;
    public int slots, legitimacy;
    public float legitimacy_share;
    public bool organizes, capture_immune;
    // 本有时生不生效：经典、教阶、朝圣是教会、伊斯兰等的底色(已算进类型表)，本有时只作显示，被别教融去才生效
    public bool innate_effects = true;
}

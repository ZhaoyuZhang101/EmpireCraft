using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace EmpireCraft.Scripts.GeneralSystems;

// 通达层的纯数值规则(不碰游戏对象，后台线程与单元测试都能用)；层本身见 ConnectivitySystem，图与寻路见 ConnectivityJob。
// 调参在 Technology/TechTree.json 的 connectivity 一节(缺的字段按这里的默认值)。
// 用户 2026-11-06 选定：隔离强度"标准"(本节默认值)、海"视距可渡"(没有近海引航只能渡 sight_hop_tiles 格的海峡，
// 列岛文化 archipelago_hop_tiles 格——保住 D2"日本能出岛")、改动顺序"先信息类"(技术、制度、理念、教化先接入)。
//   · 成本单位：平原一块地(8×8 格)记 1；地形基础阻力同 TerrainResistance(林 1.3、丘 1.6、漠雪 2、沼丛 2.5、山 4、山脊 8)，
//     过河 +2，城墙 +6，关城(关门)+2；路按等级打折(土路 0.75 → 柏油 0.4)，工业化后 4 级路(铁路的替身)0.3、摩托化后 5 级路 0.2，
//     驿传(postal_roads)再打折：人员 ×0.85、消息 ×0.6；摩托化后路外人员 ×0.8；真游牧路外平原丘陵沙漠雪原人员 ×0.7、消息 ×0.8。
//   · 无人区(没修路)：山脉、丛林、沼泽、熔岩谁都过不去；沙漠、冻土对消息放行、阻力 ×2(商队、游牧转述)，
//     对人员按习性(定居过不去，阿拉伯过沙漠，游牧过沙漠冻土)。
//   · 海：航海档 0 无(视距渡海)、1 近海引航、2 远洋航海、3 工业化(蒸汽)、4 电力或摩托化；单程上限 12(列岛 48)/64/96+32×港口级/
//     384+64×港口级/不限；上船 3/2/2/1.5/1，航程每 16/3、16、16、48、96 格 +1；离岸 64 格以上要有一头建了码头。
//   · 衰减长度：消息 8 + 文字 2 + 印刷 4 + 科学方法 3 + 电力 5 + 计算机 8(同教化原来的数)；人员 6 + 砌石 1.5 + 营造 1.5 + 驿传 1 +
//     工业化 5.5 + 摩托化 5.5(石器 6 → 现代 21)。
//   · 强度 S = min(1, e^(−(成本 − 4)/衰减长度))：相邻两城城心之间的典型成本约 4，算满；紧密 ≥0.6、往来 ≥0.25、耳闻 ≥0.08，再低算隔绝。
//   · 通信捷径(只算消息)：两边都有电报(且同一块陆地，或两边都工业化又临海——海底电缆)成本至多 6，广播至多 3，互联网至多 1；
//     接收一方锁国或严审时没有。航空(只算人员)：出发一方有航空，成本至多 12。
//   · 二档(贸易与迁徙，用户 2026-11-06 同意)：货物走人员的路，另算运费——运费占货价 = 成本 × 档位系数(粮木石金属 0.03、原料 0.015、
//     制成品 0.01、奢侈品 0.005) × 运输科技(工业化 0.4、摩托化 0.25，买卖两边取好的)，每过一个第三国另加过境税 0.05，
//     与买卖任一方交战的第三国封路；"千里不贩籴"：长安到撒马尔罕成本约 60，丝绸 +30%、粮食 +180%(工业化后 +72%)。
//     迁徙、空城安置、经济圈、难民、互市、关税同盟、海上商路按人员成本与强度(数值见 ConnectivityConfig 二档一节)。
//   · 三档(外交与战争)：议婚、通好、结盟按发起一方的人员往来强度(航海档，不要战舰)不低于 diplomacy_min_strength(0.25)；
//     议婚"近便"加分 1.5 × 强度；两帝宿怨按隔绝度(≥0.8 −5、≥0.5 −20，否则 −50)；挑战争目标按人员成本排远近；
//     宣战闸的"同一块陆地上相距不远"改按近邻表的陆路成本(不过 24，同以前)。
public enum ConnectivityMode { People = 0, Goods = 1, Ideas = 2 }

// 障碍(悬停与日志写"隔海""隔山"……)：按最优路径上各类多出来的成本排出主次
public enum ConnectivityBarrier : byte
{
    None = 0, Distance = 1, Sea = 2, Ocean = 3, Mountain = 4, Desert = 5, Tundra = 6, Jungle = 7, Swamp = 8, Wall = 9,
    River = 10, Border = 11, Unreachable = 12
}

public enum ConnectivityLevel : byte { Isolated = 0, Heard = 1, Contact = 2, Close = 3 }

public sealed class ConnectivityConfig
{
    public bool enabled = true;
    // 隔离强度：衰减长度统一乘这个数(用户选"标准"= 1)
    public float length_scale = 1f;
    public float contact_offset = 4f;
    public float min_strength = 0.05f;
    public float close_strength = 0.6f, contact_strength = 0.25f, heard_strength = 0.08f;
    // 地形基础阻力：平原、林、丘、漠、雪、沼、丛、山、河、城、山脊(下标见 ConnectivityRules.TerrainCount)
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public float[] terrain_base = { 1f, 1.3f, 1.6f, 2f, 2f, 2.5f, 2.5f, 4f, 1f, 1f, 8f };
    public float river_crossing = 2f, wall_crossing = 6f, gate_crossing = 2f;
    // 路等级 0~5 的折扣(0 = 没有路)
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public float[] road_grade_factors = { 1f, 0.75f, 0.55f, 0.5f, 0.45f, 0.4f };
    public float postal_roads_people = 0.85f, postal_roads_ideas = 0.6f;
    public float industrial_road = 0.3f, motor_road = 0.2f, motor_offroad = 0.8f;
    public float nomad_people = 0.7f, nomad_ideas = 0.8f;
    public float ideas_wild_factor = 2f;
    // 跨入别的政权(帝国按整体)一次的成本(同教化原来的 BorderCost)
    public float border_cost = 1f;
    public float info_length_base = 8f;
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<string, float> info_length_techs = new()
        { ["writing"] = 2f, ["printing"] = 4f, ["scientific_method"] = 3f, ["electricity"] = 5f, ["computing"] = 8f };
    public float people_length_base = 6f;
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<string, float> people_length_techs = new()
    {
        ["masonry"] = 1.5f, ["architecture"] = 1.5f, ["postal_roads"] = 1f, ["industrialization"] = 5.5f, ["motorization"] = 5.5f
    };
    // 航海档：研究了哪项到哪一档(取最高)
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<string, int> navigation_bands = new()
        { ["coastal_pilotage"] = 1, ["ocean_navigation"] = 2, ["industrialization"] = 3, ["electricity"] = 4, ["motorization"] = 4 };
    public float sight_hop_tiles = 12f, archipelago_hop_tiles = 48f;
    // 各航海档：单程上限(格，≤0 不限)、每级码头加多少、上船成本、航程每几格 +1
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public float[] sea_range = { 12f, 64f, 96f, 384f, 0f };
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public float[] sea_range_per_port_level = { 0f, 0f, 32f, 64f, 0f };
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public float[] sea_embark = { 3f, 2f, 2f, 1.5f, 1f };
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public float[] sea_tiles_per_cost = { 16f / 3f, 16f, 16f, 48f, 96f };
    // 离岸多远起算远洋(要码头，障碍写"隔洋")
    public float coastal_tiles = 64f;
    public int max_port_level = 3;
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<string, float> telecom_caps = new() { ["telegraph"] = 6f, ["radio"] = 3f, ["internet"] = 1f };
    public float aviation_people_cap = 12f;
    // 一档接入：技术扩散
    public float tech_contact_min = 0.3f, tech_empire_floor = 0.8f, tech_tributary_floor = 0.6f, tech_war_floor = 0.5f;
    // 制度吸收：邻国 0.5×S、远播 0.3×S(S 不低于 0.15 才算)、同盟 0.7×max(S, 0.3)；改革加速只数 S 不低于 0.35 的更先进政权
    public float institution_remote_weight = 0.3f, institution_remote_min = 0.15f;
    public float institution_alliance_min = 0.3f, institution_advanced_min = 0.35f;
    // 理念：不接壤、通达够 0.15 的也传；外国理念压力不接壤的要 0.25
    public float ideology_remote_min = 0.15f, ideology_pressure_min = 0.25f;
    // 二档 · 贸易：货物路线最远算到多少成本；每个市场连运费最低的几个市场；运费档位系数、运输科技系数(取最低)、每过一个第三国的过境税；
    // 关税同盟要的通达；海上商路各航海档的单程上限(≤0 不限)与运费系数；国内市场卖家最远成本与粮木石金属每成本的运费；互市要的通达
    public float goods_max_cost = 240f;
    public int market_links = 8;
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<string, float> freight_tiers = new() { ["basic"] = 0.03f, ["raw"] = 0.015f, ["manufactured"] = 0.01f, ["luxury"] = 0.005f };
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<string, float> freight_techs = new() { ["industrialization"] = 0.4f, ["motorization"] = 0.25f };
    public float transit_tax = 0.05f;
    public float union_min_strength = 0.4f;
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public float[] trade_route_range = { 24f, 64f, 192f, 576f, 0f };
    public float route_freight = 0.01f;
    public float domestic_max_cost = 20f, domestic_freight = 0.03f;
    public float frontier_market_min = 0.3f;
    // 二档 · 迁徙：每城记下最近的几座城(人员口径)、最远成本；迁徙看几座、多远(衰减长度的几倍)；经济圈最远成本(乘世界地图比例尺)；
    // 空城安置最远成本；国家移民选址的成本预算与离原城最近成本(官办、自发)；殖民地按海路成本算多远
    public int near_count = 24;
    public float near_max_cost = 40f;
    public int migration_candidates = 8;
    public float migration_range = 2f;
    public float circle_max_cost = 10f;
    public float resettle_max_cost = 40f;
    public float settle_budget = 16f, settle_min_cost = 5f, settle_min_cost_spontaneous = 2f;
    public float colony_min_cost = 20f;
    // 三档 · 外交：议婚、通好、结盟要的人员往来强度；议婚近便加分；两帝宿怨按隔绝度分三档(远、中、近)
    public float diplomacy_min_strength = 0.25f;
    public float marriage_bonus = 1.5f;
    public float rivalry_far_isolation = 0.8f, rivalry_mid_isolation = 0.5f;
    public int rivalry_far = -5, rivalry_mid = -20, rivalry_near = -50;
    // 结算：几年一轮；主线程取快照每帧几毫秒；后台线程开不起来时主线程每帧算几毫秒；城际寻路最多展开几块；同岛多近的两城连线
    public float pass_years = 1f;
    public float frame_ms = 1f, inline_frame_ms = 2f;
    public int astar_max_expand = 2500;
    public float link_tiles = 64f;
    // 每座临海城连几座最近的临海城(同一水域)、另连几座别的陆地上的
    public int sea_neighbours = 8, sea_other_land = 4;
    public bool worker = true;

    // 读进来的配置缺了、长度不对的项按默认补齐
    public void Normalize()
    {
        var defaults = new ConnectivityConfig();
        terrain_base = Fit(terrain_base, defaults.terrain_base);
        road_grade_factors = Fit(road_grade_factors, defaults.road_grade_factors);
        sea_range = Fit(sea_range, defaults.sea_range);
        sea_range_per_port_level = Fit(sea_range_per_port_level, defaults.sea_range_per_port_level);
        sea_embark = Fit(sea_embark, defaults.sea_embark);
        sea_tiles_per_cost = Fit(sea_tiles_per_cost, defaults.sea_tiles_per_cost);
        info_length_techs ??= defaults.info_length_techs;
        people_length_techs ??= defaults.people_length_techs;
        navigation_bands ??= defaults.navigation_bands;
        telecom_caps ??= defaults.telecom_caps;
        freight_tiers ??= defaults.freight_tiers;
        freight_techs ??= defaults.freight_techs;
        trade_route_range = Fit(trade_route_range, defaults.trade_route_range);
        if (near_count < 4) near_count = 4;
        if (market_links < 1) market_links = 1;
        if (length_scale <= 0f) length_scale = 1f;
        if (astar_max_expand < 64) astar_max_expand = 64;
        if (pass_years <= 0f) pass_years = 1f;
    }

    private static float[] Fit(float[] value, float[] fallback)
    {
        if (value == null || value.Length < fallback.Length) return (float[])fallback.Clone();
        return value;
    }
}

// 出行一方的本事(一个政权或一个文化，按某种口径)：科技变了才重算
public struct ConnectivityTravel
{
    public ConnectivityMode Mode;
    public int Band;                // 航海档 0~4
    public bool Archipelago;        // 列岛文化：视距渡海放宽到 archipelago_hop_tiles
    public int Habit;               // 0 定居、1 惯走沙漠(阿拉伯)、2 游牧
    public bool Postal, Industrial, Motor, Aviation;
    public int Comm;                // 通信档 0 驿传、1 电报、2 广播、3 互联网
    public bool Open;               // 接收外来消息(不锁国、不严审)
    public float Length;            // 衰减长度
    public float FreightFactor;     // 运输科技给的运费系数(二档；没有为 1)

    // 算边成本时用得到的几项拼成的键(同一键的成本表可以共用)
    public int CostKey => (int)Mode | Band << 2 | (Archipelago ? 1 : 0) << 5 | Habit << 6 | (Postal ? 1 : 0) << 8 |
                          (Industrial ? 1 : 0) << 9 | (Motor ? 1 : 0) << 10;
}

// 一条边的地形剖面：寻一次路记下来，成本按出行一方的科技当场拼(科技变了不用重新寻路)。
// 建好后只读(下一轮没变的边直接沿用这个对象，两端是哪两个点记在图里，不记在这里)
public sealed class ConnectivityEdge
{
    public bool Sea;
    // 陆路：路外各地形类的基础阻力之和(下标见 ConnectivityRules.TerrainCount)、各等级路上的基础阻力之和
    public readonly float[] Off = new float[ConnectivityRules.TerrainCount];
    public readonly float[] Road = new float[ConnectivityRules.RoadGrades];
    // 路外穿过的沙漠、冻土无人区(已含在 Off 里)：消息 ×2、定居的人员过不去
    public float WildDesert, WildTundra;
    public int Rivers, Walls, Gates, Zones;
    // 穿过的无人区里分量最重的一片(起名"隔某某")
    public int WildRegion = -1;
    public float WildRegionWeight;
    // 海路：两城海岸最近处相距几格、两头码头的最高等级(-1 没有码头)
    public float SeaTiles;
    public int PortLevel = -1;
}

public static class ConnectivityRules
{
    public const int Plains = 0, Forest = 1, Hills = 2, Desert = 3, Snow = 4, Swamp = 5, Jungle = 6, Mountain = 7, River = 8,
        Urban = 9, Ridge = 10, TerrainCount = 11, Blocked = 255;
    public const int RoadGrades = 6, Bands = 5;
    public const int Settled = 0, DesertWise = 1, Nomadic = 2;
    public const int BarrierCount = 13;

    private static readonly ConnectivityBarrier[] TerrainBarrier =
    {
        ConnectivityBarrier.Distance, ConnectivityBarrier.Distance, ConnectivityBarrier.Mountain, ConnectivityBarrier.Desert,
        ConnectivityBarrier.Tundra, ConnectivityBarrier.Swamp, ConnectivityBarrier.Jungle, ConnectivityBarrier.Mountain,
        ConnectivityBarrier.Distance, ConnectivityBarrier.Distance, ConnectivityBarrier.Mountain
    };

    // 真游牧马背上快的地形
    private static bool Steppe(int terrain) => terrain is Plains or Hills or Desert or Snow;

    public static float RoadFactor(int grade, in ConnectivityTravel travel, ConnectivityConfig config)
    {
        if (grade <= 0) return 1f;
        if (grade >= RoadGrades) grade = RoadGrades - 1;
        float factor = config.road_grade_factors[grade];
        if (travel.Industrial && grade >= 4) factor = Math.Min(factor, config.industrial_road);
        if (travel.Motor && grade >= 5) factor = Math.Min(factor, config.motor_road);
        if (travel.Postal) factor *= travel.Mode == ConnectivityMode.Ideas ? config.postal_roads_ideas : config.postal_roads_people;
        return factor;
    }

    private static float OffFactor(int terrain, in ConnectivityTravel travel, ConnectivityConfig config)
    {
        float factor = 1f;
        bool ideas = travel.Mode == ConnectivityMode.Ideas;
        if (!ideas && travel.Motor) factor *= config.motor_offroad;
        if (travel.Habit == Nomadic && Steppe(terrain)) factor *= ideas ? config.nomad_ideas : config.nomad_people;
        return factor;
    }

    // 陆路这条边对出行一方的成本；走不了返回 +∞
    public static float LandCost(ConnectivityEdge edge, in ConnectivityTravel travel, ConnectivityConfig config)
    {
        bool ideas = travel.Mode == ConnectivityMode.Ideas;
        float wild = edge.WildDesert + edge.WildTundra;
        if (!ideas && wild > 0f)
        {
            if (travel.Habit == Settled) return float.PositiveInfinity;
            if (travel.Habit == DesertWise && edge.WildTundra > 0f) return float.PositiveInfinity;
        }
        float cost = 0f;
        for (int t = 0; t < TerrainCount; t++)
            if (edge.Off[t] > 0f) cost += edge.Off[t] * OffFactor(t, travel, config);
        for (int g = 1; g < RoadGrades; g++)
            if (edge.Road[g] > 0f) cost += edge.Road[g] * RoadFactor(g, travel, config);
        if (ideas) cost += wild * Math.Max(0f, config.ideas_wild_factor - 1f);
        return cost + edge.Rivers * config.river_crossing + edge.Walls * config.wall_crossing + edge.Gates * config.gate_crossing;
    }

    // 这一档航海单程最远几格(≤0 不限)；列岛文化没有航海时按视距放宽
    public static float SeaRange(in ConnectivityTravel travel, int portLevel, ConnectivityConfig config)
    {
        int band = Clamp(travel.Band, 0, Bands - 1);
        float range = config.sea_range[band];
        if (range <= 0f) return 0f;
        if (band == 0) range = travel.Archipelago ? Math.Max(config.archipelago_hop_tiles, config.sight_hop_tiles) : config.sight_hop_tiles;
        else if (travel.Archipelago) range = Math.Max(range, config.archipelago_hop_tiles);
        return range + Math.Max(0, Math.Min(config.max_port_level, portLevel)) * config.sea_range_per_port_level[band];
    }

    // 海路这条边的成本；够不着返回 +∞。archipelagoOnly：只因列岛文化的视距放宽才渡得过(D2，只算列岛那一边)
    public static float SeaCost(ConnectivityEdge edge, in ConnectivityTravel travel, ConnectivityConfig config, out bool archipelagoOnly)
    {
        archipelagoOnly = false;
        int band = Clamp(travel.Band, 0, Bands - 1);
        float range = SeaRange(travel, edge.PortLevel, config);
        if (range > 0f && edge.SeaTiles > range) return float.PositiveInfinity;
        // 离岸远航要有一头建了码头
        if (edge.SeaTiles > config.coastal_tiles && edge.PortLevel < 0) return float.PositiveInfinity;
        if (travel.Archipelago)
        {
            var plain = travel;
            plain.Archipelago = false;
            float own = SeaRange(plain, edge.PortLevel, config);
            archipelagoOnly = own > 0f && edge.SeaTiles > own;
        }
        float perCost = Math.Max(0.1f, config.sea_tiles_per_cost[band]);
        return config.sea_embark[band] + edge.SeaTiles / perCost;
    }

    public static float EdgeCost(ConnectivityEdge edge, in ConnectivityTravel travel, ConnectivityConfig config, out bool archipelagoOnly)
    {
        archipelagoOnly = false;
        return edge.Sea ? SeaCost(edge, travel, config, out archipelagoOnly) : LandCost(edge, travel, config);
    }

    public static float Strength(float cost, float length, ConnectivityConfig config)
    {
        if (float.IsNaN(cost) || float.IsPositiveInfinity(cost)) return 0f;
        float scaled = Math.Max(0.01f, length * config.length_scale);
        float value = (float)Math.Exp(-(cost - config.contact_offset) / scaled);
        return value >= 1f ? 1f : value;
    }

    // 强度到这个数的最大成本(Dijkstra 截断用)
    public static float CostFor(float strength, float length, ConnectivityConfig config) =>
        config.contact_offset - Math.Max(0.01f, length * config.length_scale) * (float)Math.Log(Math.Max(1e-4f, strength));

    public static ConnectivityLevel Level(float strength, ConnectivityConfig config) =>
        strength >= config.close_strength ? ConnectivityLevel.Close
        : strength >= config.contact_strength ? ConnectivityLevel.Contact
        : strength >= config.heard_strength ? ConnectivityLevel.Heard
        : ConnectivityLevel.Isolated;

    // 这条边上的成本按障碍分类加到 sums 里(下标是 ConnectivityBarrier)
    public static void AddBarriers(ConnectivityEdge edge, in ConnectivityTravel travel, ConnectivityConfig config, float[] sums)
    {
        if (edge.Sea)
        {
            float sea = SeaCost(edge, travel, config, out _);
            if (float.IsInfinity(sea)) return;
            sums[(int)(edge.SeaTiles > config.coastal_tiles ? ConnectivityBarrier.Ocean : ConnectivityBarrier.Sea)] += sea;
            return;
        }
        for (int t = 0; t < TerrainCount; t++)
        {
            if (edge.Off[t] <= 0f) continue;
            float total = edge.Off[t] * OffFactor(t, travel, config);
            float baseCost = Math.Max(0.01f, config.terrain_base[t]);
            float flat = Math.Min(total, total / baseCost);
            sums[(int)ConnectivityBarrier.Distance] += flat;
            sums[(int)TerrainBarrier[t]] += total - flat;
        }
        for (int g = 1; g < RoadGrades; g++)
            if (edge.Road[g] > 0f) sums[(int)ConnectivityBarrier.Distance] += edge.Road[g] * RoadFactor(g, travel, config);
        if (travel.Mode == ConnectivityMode.Ideas)
        {
            float extra = Math.Max(0f, config.ideas_wild_factor - 1f);
            sums[(int)ConnectivityBarrier.Desert] += edge.WildDesert * extra;
            sums[(int)ConnectivityBarrier.Tundra] += edge.WildTundra * extra;
        }
        sums[(int)ConnectivityBarrier.River] += edge.Rivers * config.river_crossing;
        sums[(int)ConnectivityBarrier.Wall] += edge.Walls * config.wall_crossing + edge.Gates * config.gate_crossing;
    }

    // 主次障碍：除"道远"外分量最重的两类；最重的不到总成本的四分之一时算"道远"
    public static void PickBarriers(float[] sums, out ConnectivityBarrier main, out ConnectivityBarrier second)
    {
        float total = 0f;
        for (int i = 0; i < sums.Length; i++) total += sums[i];
        main = second = ConnectivityBarrier.None;
        if (total <= 0f) return;
        int best = -1, next = -1;
        for (int i = 2; i < sums.Length; i++)
        {
            if (sums[i] <= 0f) continue;
            if (best < 0 || sums[i] > sums[best]) { next = best; best = i; }
            else if (next < 0 || sums[i] > sums[next]) next = i;
        }
        if (best < 0 || sums[best] < total * 0.25f)
        {
            main = ConnectivityBarrier.Distance;
            if (best >= 0 && sums[best] >= total * 0.1f) second = (ConnectivityBarrier)best;
            return;
        }
        main = (ConnectivityBarrier)best;
        second = next >= 0 && sums[next] >= total * 0.1f ? (ConnectivityBarrier)next : ConnectivityBarrier.Distance;
    }

    // 衰减长度：base + 已有科技的加成
    public static float Length(float baseLength, Dictionary<string, float> techs, Func<string, bool> has)
    {
        float length = baseLength;
        if (techs != null && has != null)
            foreach (KeyValuePair<string, float> pair in techs)
                if (has(pair.Key)) length += pair.Value;
        return length;
    }

    public static int Band(Dictionary<string, int> bands, Func<string, bool> has)
    {
        int band = 0;
        if (bands != null && has != null)
            foreach (KeyValuePair<string, int> pair in bands)
                if (pair.Value > band && has(pair.Key)) band = pair.Value;
        return Clamp(band, 0, Bands - 1);
    }

    // 通信捷径：两边通信档取低；接收一方不开放没有；电报要同一块陆地或两边都有海底电缆
    public static float TelecomCap(int fromComm, int toComm, bool toOpen, bool sameLand, bool bothCable, ConnectivityConfig config)
    {
        int level = Math.Min(fromComm, toComm);
        if (level <= 0 || !toOpen) return float.PositiveInfinity;
        if (level == 1 && !sameLand && !bothCable) return float.PositiveInfinity;
        string key = level >= 3 ? "internet" : level == 2 ? "radio" : "telegraph";
        return config.telecom_caps != null && config.telecom_caps.TryGetValue(key, out float cap) && cap > 0f ? cap : float.PositiveInfinity;
    }

    // 运费占货价的比例(二档)：成本 × 档位系数 × 运输科技系数；档位没配的按制成品
    public static float Freight(float cost, string tier, float techFactor, ConnectivityConfig config)
    {
        if (float.IsNaN(cost) || cost <= 0f) return 0f;
        if (float.IsPositiveInfinity(cost)) return float.PositiveInfinity;
        float factor = config.freight_tiers != null && tier != null && config.freight_tiers.TryGetValue(tier, out float value) ? value
            : config.freight_tiers != null && config.freight_tiers.TryGetValue("manufactured", out float fallback) ? fallback : 0.01f;
        return cost * factor * (techFactor > 0f ? techFactor : 1f);
    }

    // 运输科技系数：研究了的取最低，没有为 1
    public static float FreightTech(Dictionary<string, float> techs, Func<string, bool> has)
    {
        float factor = 1f;
        if (techs != null && has != null)
            foreach (KeyValuePair<string, float> pair in techs)
                if (pair.Value > 0f && pair.Value < factor && has(pair.Key)) factor = pair.Value;
        return factor;
    }

    // 海上商路这一档的单程上限(格；≤0 不限)
    public static float RouteRange(int band, ConnectivityConfig config)
    {
        float[] ranges = config.trade_route_range;
        return ranges == null || ranges.Length == 0 ? 0f : ranges[Clamp(band, 0, ranges.Length - 1)];
    }

    // 直线海程 tiles 格按这一档的上船与航程算的成本(海上商路用，不经图)
    public static float SeaLegCost(float tiles, int band, ConnectivityConfig config)
    {
        band = Clamp(band, 0, Bands - 1);
        return config.sea_embark[band] + Math.Max(0f, tiles) / Math.Max(0.1f, config.sea_tiles_per_cost[band]);
    }

    private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
}

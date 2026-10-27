using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 行政控制力(每国每年一次，分帧轮到哪国算哪国)：
//   · 控制力(0~1)：城离最近的治所(本国都城；帝国还有各成员国的都城，起步算 4)沿本国领土走过去的路程。
//     路程按地形阻力算(用户 2026-10-16："按照地形和距离来判断辐射范围"，见 TerrainResistance)：平原一块地算 1，
//     山地 4、过河 +2、翻长城 +6，有路减半——山后、长城外的城鞭长莫及，驿道通到的地方管得远；
//     港口之间在本国水师辐射内(见 StraitReach)可走海路(上船 2，航程每 16 格 +1)，隔海的城最多六成五(工业时代起八成)。
//     时代越新管得越远(满控路程 10 + 2×时代，律令制再远一成，分封、封建近一成)，超出部分每多走满控路程的两倍降到一成；
//     陆路海路都不通的飞地按直线走过去的地形阻力(见 TerrainResistance.LineCost)的 1.15 倍再加 8 计，最多八成——
//     以前按直线距离的 1.4 倍，隔着没人占的雪山、大漠也照平地算(用户 2026-10-26："印度打不到西藏")；
//     山地、冻土、沙漠连成的天堑腹地越往里越难走(见 TerrainResistance.BarrierDepth)；
//   · 效果：控制力低的城自然稳定度下降(满控不减，最低扣 20)；赋税收不齐(满控全收，最低收一半，见 TreasurySystem)；
//     征兵也少从这样的城里抽(见 CorpsSystem.Mobilize、CityPopulationSystem.DrawKingdomCity)；地图上见政令图层；
//     领土不在这里变动：扩张、边界侵蚀都按原版(侵蚀受原版"边界侵蚀"开关管、只在交战双方之间，见 CityExpansionPatch)。
//     以前这里的"边地易手"(太平时互相侵蚀)、"被围空地并入"(把山地、隔水的地并进来)不守原版规矩，已删；
//     旧存档里因此留下的山地、隔水地和飞地，读档后交给原版的"断开的地放弃"清掉(见 CityExpansionPatch.CheckAbandonOnLoad)；
//     飞地易帜：陆路不通的飞地城(不是都城)控制力不到 0.45 时，每年有概率改投接壤最长的邻国(双方都不在打仗)，
//     古典时代还有概率由当地首领自立为独立王国(见 EraSecession，近代以后不自立)。
//
// 扩张上限(用户 2026-10-16："扩张的毫无节制""远的扩张不到的就可以用羁縻统治，或者属国朝贡或者诸侯等，参考朝鲜和明朝的关系")：
//   · 控制范围：满控路程除了时代，还看本国已采纳的科技(驿传、工业化、电报、摩托化、航空)、政体(律令远、封建近)
//     和君主(雄主、正统高管得远，昏君、正统低收缩)；
//   · 硬限：30 年内新打下(含割让)的城，控制力不到三成(过度扩张时门槛更高)且没在打仗的，由当地首领自立，
//     按政体文化转为间接统治——施行羁縻的帝国设羁縻州(仍在帝国内，自治、少纳税)；封建、分封或不是帝国的立为附庸诸侯；
//     其余帝国收为朝贡国(如朝鲜之于明)。每国每年至多放出一片(最多 6 城)；
//   · 早期生产力只够这样统治(用户 2026-10-16："早期之所以是周朝制度是因为生产力只能允许这么统治")：
//     直辖容量起点很低，青铜时代只能直辖十几城，再大就只能分封——周制、封建帝国里的诸侯(周制的郡除外)
//     不算直辖，也不因路远失稳；青铜时代及以前，或周制天下，管不到的新征服之地封为诸侯(帝国成员)；
//   · 软限：直辖容量 = 4 + 4×时代 + 制度科技(官僚文书、驿传、考选取士、电报、计算机)，按政体、君主折算；
//     羁縻地区、分封诸侯不算直辖。直辖城数超过容量即过度扩张：远方城市稳定度再降，硬限门槛提高，
//     开战不再谋割地吞并、只迫其称臣(见 WarGoalSystem.Ensure)。
//
// 游牧低控制(用户 2026-10-20 定的游牧基调"高扩张，低控制"：能打下来、直辖不了多少)：游牧政体与汗国分封，且国家文化是游牧
// (真游牧，见 KingdomExtension.IsTrueNomad；用户 2026-10-21 选"阿兹特克、奥吉布瓦不再吃游牧的亏"——它们挂在游牧制度线上、
// 青铜后锁在游牧政体，却不逐水草而居，容量、路程、硬限照定居政体算；征服王朝从俗改了定居政体的本来就不算)——
//   · 直辖容量 ×NomadCapacity(0.6)：逐水草而居、没有郡县文书，汗庭直辖的城有限，多出来的很快就是过度扩张；
//   · 满控路程不减反长：游牧 ×NomadReach(1.1)、汗国分封 ×1(不再按分封打九折)——骑兵来去快，号令传得远；
//   · 硬限门槛提到 NomadHardLimit(0.45)：新打下的城控制力不到四成五就由当地首领自立，转为间接统治(朝贡国、附庸、
//     汗国分封与早期的帝国分封为成员，按 Subordinate 的老规矩)，比别的政体早得多。
public static class AdministrativeControlSystem
{
    private const int MemberSeatOffset = 4;
    // 沿直线估路程(飞地、开战定目标)：地形阻力照实算，再加一成五绕路；只估离得最近的几处
    private const float LineDetour = 1.15f;
    private const int LineCandidates = 3;
    private const int ExclavePenalty = 8;
    private const float ExclaveCap = 0.8f, OverseasCap = 0.65f, ModernOverseasCap = 0.8f;
    private const float MinControl = 0.1f;
    private const float StabilityPenalty = 20f;
    private const float DefectThreshold = 0.45f;
    private const float DefectChance = 0.5f;
    // 海路：上船 2，航程每 16 格 +1
    private const float SeaEmbark = 2f, SeaTilesPerCost = 16f;
    // 地块阻力算不出(城里的水面)时按过河算
    private const float WaterStep = 1f + TerrainResistance.RiverCrossing;
    // 鞭长莫及：赋税、征兵按 0.5 + 0.5 × 控制力
    private const float MinYield = 0.5f;
    // 扩张上限：新征服之地控制力不到三成转间接统治(过度扩张时门槛最多再加两成)；30 年内算新征服；一次最多放出 6 城
    private const float HardLimit = 0.3f, OverextendedHardLimit = 0.2f;
    private const float RecentConquestYears = 30f;
    private const int ReleaseClusterMax = 6;
    private const float OverextensionPenalty = 15f;
    // 游牧低控制：直辖容量、满控路程、硬限门槛
    private const float NomadCapacity = 0.6f, NomadReach = 1.1f, NomadHardLimit = 0.45f;
    // 律令制管得远、直辖得多：满控路程、直辖容量各 ×1.1(以前 ×1.15、×1.2)。用户 2026-10-21 选"华夏兵多一点、地吐一点"：
    // 华夏打下来最留得住、长局一家独大，这里收一点；前现代征兵少另由制度树补(府兵、募兵节点加征兵比例)
    private const float LvLingReach = 1.1f, LvLingCapacity = 1.1f;

    // 真游牧才低控制(见 KingdomExtension.IsTrueNomad)：阿兹特克、林地部族的游牧政体不算
    private static bool NomadRegime(Kingdom realm) => KingdomExtension.IsTrueNomad(realm);

    private static readonly Queue<long> Pending = new();
    private static int _cursor;

    public static void ResetWorldState()
    {
        Pending.Clear();
        _cursor = 0;
    }

    private static Kingdom Realm(Kingdom kingdom) => kingdom?.GetEmpire()?.CoreKingdom ?? kingdom;

    // 治所挪了地方(迁城，见 CityRelocationSystem)：本国(在帝国里的算整个帝国)排进队里重算路程，不等满一年
    public static void Refresh(Kingdom kingdom)
    {
        Kingdom realm = Realm(kingdom);
        if (!Valid(realm)) return;
        realm.GetOrCreate().admin_pass_at = -1d;
        if (!Pending.Contains(realm.id)) Pending.Enqueue(realm.id);
    }

    private static bool Valid(Kingdom kingdom) => kingdom?.data != null && !kingdom.isRekt() && !kingdom.wild &&
                                                  !AncientWarfareCompatibility.Owns(kingdom);

    private static bool Valid(City city) => city?.data != null && !city.isRekt() && city.zones != null &&
                                            city.zones.Count > 0 && Valid(city.kingdom) &&
                                            !AncientWarfareCompatibility.OwnsObject(city);

    // 尚未算过的城当作满控
    public static float Get(City city)
    {
        if (city?.data == null) return 1f;
        float control = city.GetOrCreate().admin_control;
        return control < 0f ? 1f : control;
    }

    public static bool Known(City city) => city?.data != null && city.GetOrCreate().admin_control >= 0f;

    // 羁縻地区本地首领自治，不因鞭长莫及而失稳；过度扩张时远方再扣(满控扣 3，控制力越低扣得越多，最多 18)
    public static float StabilityLoss(City city)
    {
        if (!Known(city) || IsIndirect(city.kingdom)) return 0f;
        float control = Get(city);
        float loss = (1f - control) * StabilityPenalty;
        Kingdom realm = Realm(city.kingdom);
        float over = realm?.data == null ? 0f : realm.GetOrCreate().admin_overextension;
        return loss + over * OverextensionPenalty * (1.2f - control);
    }

    // 间接统治：羁縻地区；自主外交的世袭成员与自治城邦；周制、封建帝国里的诸侯(周制直辖的郡、帝国核心除外)。
    // 水土(ShuiTuSystem)按同一口径分直辖与否
    public static bool IsIndirect(Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        if (empire == null || kingdom == empire.CoreKingdom) return false;
        if (JimiSystem.IsJimiAdministration(kingdom) || JimiSystem.IsJimiRegion(kingdom, empire)) return true;
        // 自主外交的世袭成员(游牧兀鲁思、世袭的埃米尔与苏丹国、封国藩镇)与自治城邦也是间接统治
        Regime own = kingdom.GetRegime();
        if (own != null && own.IsAllowDiplomacy() &&
            (own.GetLeaderSelectMethod() == LeaderSelectMethod.Succession || CityStateService.IsCityState(kingdom))) return true;
        // 政体配置(indirect_rule、direct_member_types)：封建、原始部落联盟、周制(郡除外)、幕府(天领除外)的成员自治
        Regime template = RegimeManager.GetTemplate(empire.CoreKingdom?.GetRegime()?.type);
        return template?.indirect_rule == true &&
               template.direct_member_types?.Contains(kingdom.GetKingdomType()) != true;
    }

    // 青铜时代及以前(时代 ≤ 3)
    private const int EarlyEraTier = 3;

    // 城易主时记下(CityPatch 攻城结算、WarGoalSystem 割地调用)
    public static void MarkConquered(City city, Kingdom receiver)
    {
        if (city?.data == null || receiver?.data == null) return;
        var data = city.GetOrCreate();
        data.conquered_at = World.world.getCurWorldTime();
        data.conquered_by = Realm(receiver).id;
        // 调用时城还在原主手里
        data.conquered_unification = UnificationBetween(receiver, city.kingdom);
    }

    // 两国(连同所在帝国的中央)之间正打着统一战争
    private static bool UnificationBetween(Kingdom receiver, Kingdom loser)
    {
        if (receiver?.data == null || loser?.data == null) return false;
        Kingdom realm = Realm(receiver), other = Realm(loser);
        foreach (Kingdom side in new[] { receiver, realm })
            foreach (War war in side.getWars())
            {
                if (war?.data == null || war.hasEnded() || !UnificationSystem.IsUnificationWar(war)) continue;
                bool attacker = war.isAttacker(side);
                if (attacker ? war.isDefender(loser) || war.isDefender(other) : war.isAttacker(loser) || war.isAttacker(other))
                    return true;
            }
        return false;
    }

    #region 代际扩张上限

    // 一代人能侵占多少(用户 2026-10-27："一代人只能侵占一个帝国核心、3 个王国核心""除非是统一战争"；截图里瑞典帝国
    // 横跨欧洲、北非、西亚 98 城，维京本族只有 9 城，古典、现代制度都维持不了)：GenerationYears 年内打下(含割让)、眼下仍在本国的城，
    // 涉及的王国法理满 KingdomCoresPerGeneration 个，或因此据有了别人的一个帝国法理(三分之二)，再开战不谋土地，
    // 改为迫其称臣(帝国则求赔款)，同过度扩张(见 WarGoalSystem.Ensure)。统一战争里打下的不算，统一战争也不受限
    public const float GenerationYears = 30f;
    public const int KingdomCoresPerGeneration = 3, EmpireCoresPerGeneration = 1;

    // 本国(帝国按中央，连同成员)这一代人侵占的王国法理、帝国法理各几个
    public static (int kingdoms, int empires) GenerationConquests(Kingdom kingdom)
    {
        Kingdom realm = Realm(kingdom);
        if (!Valid(realm) || World.world?.cities == null) return (0, 0);
        var titles = new HashSet<long>();
        var cores = new HashSet<EmpireCore>();
        EmpireCore own = realm.GetEmpire() is Empire empire ? EmpireCoreManager.Get(empire) : null;
        double now = World.world.getCurWorldTime();
        foreach (City city in World.world.cities.list)
        {
            if (city?.data == null || city.isRekt() || city.kingdom?.data == null || Realm(city.kingdom) != realm) continue;
            var data = city.GetOrCreate();
            if (data.conquered_by != realm.id || data.conquered_unification || data.conquered_at < 0d || now < data.conquered_at ||
                Date.getYearsSince(data.conquered_at) >= GenerationYears) continue;
            KingdomTitle title = city.GetTitle();
            if (title?.data != null) titles.Add(title.id);
            EmpireCore core = city.GetEmpireCore();
            if (core != null && core != own) cores.Add(core);
        }
        int empires = 0;
        foreach (EmpireCore core in cores)
        {
            (int held, int total) = RealmNamingSystem.Holding(core, realm);
            if (RealmNameRules.HoldsShare(held, total)) empires++;
        }
        return (titles.Count, empires);
    }

    // 这一代人的侵占已满(统一战争不算)
    public static bool GenerationExhausted(War war, Kingdom attacker, out string why)
    {
        why = null;
        if (UnificationSystem.IsUnificationWar(war)) return false;
        (int kingdoms, int empires) = GenerationConquests(attacker);
        if (kingdoms < KingdomCoresPerGeneration && empires < EmpireCoresPerGeneration) return false;
        why = $"{GenerationYears:0} 年内已侵占王国法理 {kingdoms} 个、帝国法理 {empires} 个";
        return true;
    }

    #endregion

    // 开战定目标用：对方都城是否远在本国控制范围之外(按本国离它最近的城的路程，加从那座城沿直线过去的地形阻力估算，
    // 见 TerrainResistance.LineCost——隔着雪山、大漠的不再当平地，用户 2026-10-26)，或本国已过度扩张
    public static bool BeyondReach(Kingdom attacker, City target)
    {
        Kingdom realm = Realm(attacker);
        if (!Valid(realm) || target?.data == null || target.isRekt()) return false;
        if (realm.GetOrCreate().admin_overextension > 0f) return true;
        TileZone goal = CenterZone(target);
        if (goal == null) return false;
        // 先按直线挑最近的几座，再按地形阻力细算
        var near = new List<(float rough, float admin, TileZone from)>();
        foreach (City city in Cities(realm))
        {
            if (!Known(city)) continue;
            TileZone from = CenterZone(city);
            if (from == null) continue;
            float admin = city.GetOrCreate().admin_distance;
            near.Add((admin + Vector2.Distance(new Vector2(from.x, from.y), new Vector2(goal.x, goal.y)), admin, from));
        }
        float best = float.MaxValue;
        foreach ((float _, float admin, TileZone from) in near.OrderBy(entry => entry.rough).Take(LineCandidates))
            best = Mathf.Min(best, admin + TerrainResistance.LineCost(from, goal, realm) * LineDetour);
        return best < float.MaxValue && Control(best, Reach(realm)) < HardLimit;
    }

    public static string DescribeCapacity(Kingdom kingdom)
    {
        Kingdom realm = Realm(kingdom);
        if (!Valid(realm)) return null;
        var data = realm.GetOrCreate();
        if (data.admin_capacity <= 0) return null;
        string text = string.Format(LM.Get("admin_capacity_line"), data.admin_direct_cities, data.admin_capacity);
        return data.admin_overextension > 0f
            ? text + "  " + string.Format(LM.Get("admin_overextended"), data.admin_overextension * 100f)
            : text;
    }

    // 赋税、征兵能收上来几成(满控全收，最低一半)
    public static float Yield(City city) => Known(city) ? MinYield + (1f - MinYield) * Get(city) : 1f;

    public static string Describe(City city)
    {
        if (!Known(city)) return null;
        var data = city.GetOrCreate();
        string where = data.admin_overseas ? LM.Get("admin_control_overseas")
            : data.admin_exclave ? LM.Get("admin_control_exclave")
            : string.Format(LM.Get("admin_control_distance"), data.admin_distance);
        return $"{data.admin_control * 100f:0}% · {where}";
    }

    // 每帧最多处理一国；同一国一年一次
    public static void Tick()
    {
        if (World.world?.kingdoms == null || ModClass.IS_CLEAR) return;
        if (Config.game_loaded && !SmoothLoader.isLoading()) EmpireCraft.Scripts.GamePatches.CityExpansionPatch.CheckAbandonOnLoad();
        if (!SimulationFrameBudget.HasTime) return;
        if (Pending.Count == 0) Refill();
        if (Pending.Count == 0) return;
        Kingdom realm = World.world.kingdoms.get(Pending.Dequeue());
        if (!Valid(realm) || Realm(realm) != realm) return;
        var data = realm.GetOrCreate();
        if (data.admin_pass_at >= 0d && World.world.getCurWorldTime() >= data.admin_pass_at &&
            Date.getYearsSince(data.admin_pass_at) < 1) return;
        try
        {
            using (FrameProfiler.Measure("行政控制力"))
            {
                data.admin_pass_at = World.world.getCurWorldTime();
                List<City> cities = Cities(realm);
                // 分项计时(嵌在"行政控制力"里)：最短路(含地形阻力、港口)与其余各步
                using (FrameProfiler.Measure("行政控制力·最短路")) Compute(realm, cities);
                using (FrameProfiler.Measure("行政控制力·其余"))
                {
                    UpdateCapacity(realm, cities);
                    if (!realm.hasEnemies()) Defect(realm, cities);
                    ReleaseOverreach(realm, cities);
                }
            }
        }
        catch (Exception error)
        {
            LogService.LogWarning($"[EmpireCraft][行政控制力] {realm.id}: {error.Message}");
        }
    }

    // 一次排进最多 16 国，轮着来
    private static void Refill()
    {
        var list = World.world.kingdoms.list;
        if (list == null || list.Count == 0) return;
        if (_cursor >= list.Count) _cursor = 0;
        for (int scanned = 0; scanned < list.Count && Pending.Count < 16; scanned++)
        {
            Kingdom kingdom = list[_cursor];
            _cursor = (_cursor + 1) % list.Count;
            if (Valid(kingdom) && Realm(kingdom) == kingdom && kingdom.hasCities()) Pending.Enqueue(kingdom.id);
        }
    }

    private static List<City> Cities(Kingdom realm)
    {
        Empire empire = realm.GetEmpire();
        IEnumerable<Kingdom> members = empire?.kingdoms_list?.Where(Valid) ?? new[] { realm };
        var result = new List<City>();
        foreach (Kingdom member in members.Distinct())
            if (member.cities != null)
                result.AddRange(member.cities.Where(Valid));
        return result;
    }

    #region 控制力

    public static TileZone CenterZone(City city)
    {
        TileZone zone = city.getTile()?.zone;
        if (zone != null && zone.city == city) return zone;
        WorldTile tile = World.world.GetTile((int)city.city_center.x, (int)city.city_center.y - 2);
        if (tile?.zone != null && tile.zone.city == city) return tile.zone;
        return city.zones.Count > 0 ? city.zones[0] : null;
    }

    // 满控路程：时代 + 本国已采纳的交通通信科技，按政体、君主折算
    private static readonly (string tech, float reach)[] ReachTechs =
        { ("postal_roads", 2f), ("industrialization", 2f), ("telegraph", 3f), ("motorization", 2f), ("aviation", 2f) };
    // 直辖容量：时代 + 本国已采纳的治理科技
    private static readonly (string tech, float cities)[] CapacityTechs =
        { ("bureaucracy", 4f), ("postal_roads", 4f), ("civil_examination", 4f), ("telegraph", 8f), ("computing", 6f) };

    private static float Reach(Kingdom realm)
    {
        int tier = TechnologySystem.GetEraTier(TechnologySystem.GetCultureOf(realm));
        float reach = 10f + 2f * tier;
        foreach ((string tech, float bonus) in ReachTechs)
            if (TechnologySystem.RealmHasTech(realm, tech)) reach += bonus;
        reach *= realm.GetRegime()?.type switch
        {
            RegimeType.LvLing => LvLingReach,
            // 非游牧文化的游牧政体(阿兹特克、林地部族)不加，按 1 算
            RegimeType.YouMu when NomadRegime(realm) => NomadReach,
            RegimeType.Feudalism or RegimeType.ZhouFeudalism or RegimeType.Bakufu => 0.9f,
            _ => 1f
        };
        return reach * RulerFactor(realm);
    }

    public static int Capacity(Kingdom realm)
    {
        int tier = TechnologySystem.GetEraTier(TechnologySystem.GetCultureOf(realm));
        float capacity = 4f + 4f * tier;
        foreach ((string tech, float bonus) in CapacityTechs)
            if (TechnologySystem.RealmHasTech(realm, tech)) capacity += bonus;
        // 分封、封建不缩容量：诸侯本就不算直辖，是生产力不够时的统治办法
        if (realm.GetRegime()?.type == RegimeType.LvLing) capacity *= LvLingCapacity;
        // 游牧低控制：直辖不了多少
        if (NomadRegime(realm)) capacity *= NomadCapacity;
        return Mathf.Max(3, Mathf.RoundToInt(capacity * RulerFactor(realm)));
    }

    // 君主：雄主 ×1.15、平庸 ×1、懦弱放纵 ×0.9、昏聩 ×0.85、暴君 ×0.95；帝国再按正统 0.85~1.15 折算
    private static float RulerFactor(Kingdom realm)
    {
        float factor = RulerQualitySystem.Of(realm.king) switch
        {
            RulerQualitySystem.Grade.Wise => 1.15f,
            RulerQualitySystem.Grade.Weak or RulerQualitySystem.Grade.Dissolute => 0.9f,
            RulerQualitySystem.Grade.Fatuous => 0.85f,
            RulerQualitySystem.Grade.Tyrant => 0.95f,
            _ => 1f
        };
        Empire empire = realm.GetEmpire();
        if (empire?.data != null && realm.IsEmpire())
            factor *= 0.85f + 0.3f * Mathf.Clamp01(MonarchyLegitimacy.Get(empire) / 100f);
        return factor;
    }

    private static void UpdateCapacity(Kingdom realm, List<City> cities)
    {
        var data = realm.GetOrCreate();
        data.admin_capacity = Capacity(realm);
        data.admin_direct_cities = cities.Count(city => !IsIndirect(city.kingdom));
        data.admin_overextension = Mathf.Clamp01((float)data.admin_direct_cities / data.admin_capacity - 1f);
    }

    private static float Control(float distance, float reach) =>
        distance <= reach ? 1f : Mathf.Max(MinControl, 1f - (distance - reach) / (2f * reach));

    // 最短路的工作数组(按地块编号，记的是第几趟，不必每趟清空)：本国领土、已走到(路程、是否走过海路)、本趟算过的地形阻力。
    // 以前每趟新建以地块为键的 HashSet、Dictionary，大国一趟几千块地
    private static readonly MinHeap<(TileZone zone, bool sea)> Heap = new();
    private static int _pass;
    private static int[] _member = new int[0], _done = new int[0], _stepPass = new int[0];
    private static float[] _cost = new float[0], _step = new float[0];
    private static bool[] _sea = new bool[0];

    private static void BeginPass()
    {
        int size = World.world?.zone_calculator?.zones?.Count ?? 0;
        if (_member.Length < size) GrowPass(size);
        if (++_pass < int.MaxValue) return;
        Array.Clear(_member, 0, _member.Length);
        Array.Clear(_done, 0, _done.Length);
        Array.Clear(_stepPass, 0, _stepPass.Length);
        _pass = 1;
    }

    private static void GrowPass(int size)
    {
        Array.Resize(ref _member, size);
        Array.Resize(ref _done, size);
        Array.Resize(ref _stepPass, size);
        Array.Resize(ref _cost, size);
        Array.Resize(ref _step, size);
        Array.Resize(ref _sea, size);
    }

    private static bool Member(TileZone zone) => zone.id >= 0 && zone.id < _member.Length && _member[zone.id] == _pass;

    private static bool Reached(TileZone zone) => zone.id >= 0 && zone.id < _done.Length && _done[zone.id] == _pass;

    // 一趟只算一国(realm)，城墙上的关门放本国与友邦过(见 TerrainResistance、WallGateSystem)
    private static float Step(TileZone zone, Kingdom realm)
    {
        int id = zone.id;
        if (_stepPass[id] == _pass) return _step[id];
        float step = TerrainResistance.Step(zone, realm);
        _step[id] = step;
        _stepPass[id] = _pass;
        return step;
    }

    private static void Compute(Kingdom realm, List<City> cities)
    {
        var seats = new List<(TileZone zone, int offset)>();
        foreach (City city in cities)
        {
            Kingdom owner = city.kingdom;
            if (owner.capital != city) continue;
            TileZone zone = CenterZone(city);
            if (zone != null) seats.Add((zone, owner == realm ? 0 : MemberSeatOffset));
        }
        // 陪京(见 MultiCapitalSystem)：与都城同起点
        foreach (City jing in MultiCapitalSystem.AuxiliaryCities(realm))
        {
            TileZone zone = jing.kingdom != null && cities.Contains(jing) ? CenterZone(jing) : null;
            if (zone != null) seats.Add((zone, 0));
        }
        if (seats.Count == 0) return;
        BeginPass();
        foreach (City city in cities)
            foreach (TileZone zone in city.zones)
                if (zone != null && zone.id >= 0)
                {
                    if (zone.id >= _member.Length) GrowPass(zone.id + 1);
                    _member[zone.id] = _pass;
                }
        // 港口(本国水师辐射内互通海路)：城心块 → 港口城、所在水域
        float radius = StraitReach.Radius(realm);
        var ports = new Dictionary<TileZone, (City city, HashSet<int> basins)>();
        if (radius > 0f)
            foreach (City city in cities)
            {
                if (NavySystem.Port(city) == null) continue;
                TileZone zone = CenterZone(city);
                var basins = new HashSet<int>(SeaRegionSystem.RegionsAround(city).Select(region => region.Basin));
                if (zone != null && basins.Count > 0) ports[zone] = (city, basins);
            }
        // 多源最短路(按地形阻力)：都城从 0 起、成员国都城从 4 起；记下路上有没有走过海路。
        // 每块地的阻力一趟只算一次(以前四邻各推一次就现算一次)
        Heap.Clear();
        foreach ((TileZone zone, int offset) in seats) Heap.Push(offset, (zone, false));
        while (Heap.Count > 0)
        {
            (float cost, (TileZone zone, bool sea)) = Heap.Pop();
            if (zone.id < 0 || Reached(zone)) continue;
            if (zone.id >= _member.Length) GrowPass(zone.id + 1);
            _done[zone.id] = _pass;
            _cost[zone.id] = cost;
            _sea[zone.id] = sea;
            foreach (TileZone neighbour in zone.neighbours ?? Array.Empty<TileZone>())
            {
                if (neighbour == null || !Member(neighbour) || Reached(neighbour)) continue;
                float step = Step(neighbour, realm);
                Heap.Push(cost + (step < 0f ? WaterStep : step), (neighbour, sea));
            }
            if (ports.Count == 0 || !ports.TryGetValue(zone, out var from)) continue;
            foreach (KeyValuePair<TileZone, (City city, HashSet<int> basins)> to in ports)
            {
                if (to.Key == zone || Reached(to.Key) || !from.basins.Overlaps(to.Value.basins)) continue;
                float span = (from.city.city_center - to.Value.city.city_center).magnitude;
                if (span <= radius) Heap.Push(cost + SeaEmbark + span / SeaTilesPerCost, (to.Key, true));
            }
        }
        Heap.Clear();
        float reach = Reach(realm);
        bool modern = TechnologySystem.GetEraTier(TechnologySystem.GetCultureOf(realm)) >= 7;
        foreach (City city in cities)
        {
            var data = city.GetOrCreate();
            TileZone center = CenterZone(city);
            if (center == null) continue;
            if (Reached(center))
            {
                float reachedCost = _cost[center.id];
                // 走海路才到、又与各治所不在同一块陆地上的，是隔海的城
                bool across = _sea[center.id] && !seats.Any(seat => seat.zone.centerTile != null && center.centerTile != null &&
                                                                     seat.zone.centerTile.isSameIsland(center.centerTile));
                data.admin_distance = Mathf.RoundToInt(reachedCost);
                data.admin_exclave = false;
                data.admin_overseas = across;
                float control = Control(reachedCost, reach);
                data.admin_control = across ? Mathf.Min(modern ? ModernOverseasCap : OverseasCap, control) : control;
                continue;
            }
            // 陆路不通：按到最近几处治所沿直线过去的地形阻力折算(雪山、大漠照实算，见 TerrainResistance.LineCost)
            bool overseas = true;
            foreach ((TileZone seat, int _) in seats)
                if (seat.centerTile != null && center.centerTile != null && seat.centerTile.isSameIsland(center.centerTile))
                    overseas = false;
            float best = float.MaxValue;
            foreach ((TileZone seat, int offset) in seats
                         .OrderBy(entry => Vector2.Distance(new Vector2(entry.zone.x, entry.zone.y), new Vector2(center.x, center.y)) +
                                           entry.offset)
                         .Take(LineCandidates))
                best = Mathf.Min(best, TerrainResistance.LineCost(seat, center, realm) * LineDetour + offset);
            int far = Mathf.RoundToInt(best) + ExclavePenalty;
            data.admin_distance = far;
            data.admin_exclave = true;
            data.admin_overseas = overseas;
            float cap = overseas ? modern ? ModernOverseasCap : OverseasCap : ExclaveCap;
            data.admin_control = Mathf.Min(cap, Control(far, reach));
        }
    }

    #endregion

    #region 扩张上限

    private static bool Overreached(Kingdom realm, City city, float limit)
    {
        if (!Valid(city) || city.kingdom.capital == city) return false;
        var data = city.GetOrCreate();
        return data.conquered_at >= 0d && data.conquered_by == realm.id && data.admin_control >= 0f &&
               data.admin_control < limit && World.world.getCurWorldTime() >= data.conquered_at &&
               Date.getYearsSince(data.conquered_at) < RecentConquestYears &&
               !(data.OccupiedStatus?.Count > 0) && !city.kingdom.hasEnemies() && !IsIndirect(city.kingdom);
    }

    // 管不到的新征服之地：由当地首领自立，按政体文化转为羁縻州、附庸诸侯或朝贡国
    private static void ReleaseOverreach(Kingdom realm, List<City> cities)
    {
        float limit = (NomadRegime(realm) ? NomadHardLimit : HardLimit) + OverextendedHardLimit * realm.GetOrCreate().admin_overextension;
        City seed = cities.Where(city => Overreached(realm, city, limit))
            .OrderBy(city => city.GetOrCreate().admin_control).ThenBy(city => city.id).FirstOrDefault();
        if (seed == null) return;
        Kingdom former = seed.kingdom;
        // 连成一片的同属一国的新征服城一起放出
        var cluster = new List<City> { seed };
        var seen = new HashSet<City> { seed };
        for (int i = 0; i < cluster.Count && cluster.Count < ReleaseClusterMax; i++)
            foreach (City next in (IEnumerable<City>)cluster[i].neighbours_cities ?? Array.Empty<City>())
            {
                if (cluster.Count >= ReleaseClusterMax) break;
                if (next == null || !seen.Add(next) || next.kingdom != former || !Overreached(realm, next, limit)) continue;
                cluster.Add(next);
            }
        string seedName = seed.data.name;
        Kingdom local = ReleaseToLocal(realm, cluster, null, out string form);
        if (local == null) return;
        EventRecorder.Record(realm, string.Format(LM.Get("admin_overreach_" + form + "_history"),
            realm.GetKingdomName(), local.GetKingdomName(), cluster.Count));
        LogService.LogInfo($"[EmpireCraft][扩张上限] {realm.GetKingdomName()} 管不到 {seedName} 等 {cluster.Count} 城，转为 {form}");
    }

    // 交给当地首领：cluster 的头一座城由当地首领自立为国，其余仍属原国的城并入，再按 place 安置
    // (为空时按政体文化转为分封诸侯、羁縻州、朝贡国或附庸，见 Subordinate)。水土的出路也走这里(见 ShuiTuSystem)。
    // form 是安置的形式(admin_overreach_{form}_history)；自立不成返回 null
    public static Kingdom ReleaseToLocal(Kingdom realm, List<City> cluster, Func<Kingdom, string> place, out string form)
    {
        form = null;
        City seed = cluster?.FirstOrDefault();
        if (realm?.data == null || !Valid(seed)) return null;
        Kingdom former = seed.kingdom;
        if (!seed.hasLeader()) CityPopulationSystem.EnsureLeader(seed);
        Actor leader = LocalLeader(seed);
        if (leader == null) return null;
        Kingdom local;
        try
        {
            local = seed.makeOwnKingdom(leader);
        }
        catch (Exception error)
        {
            LogService.LogWarning($"[EmpireCraft][扩张上限] {seed.data.name} 自立失败: {error.Message}");
            return null;
        }
        if (local == null || local.isRekt() || seed.kingdom != local) return null;
        foreach (City city in cluster.Skip(1))
            if (Valid(city) && city.kingdom == former) city.joinAnotherKingdom(local);
        foreach (City city in cluster)
        {
            var data = city.GetOrCreate();
            data.conquered_at = -1d;
            data.conquered_by = -1L;
            data.admin_control = -1f;
        }
        form = place != null ? place(local) : Subordinate(realm, local);
        return local;
    }

    private static Actor LocalLeader(City city)
    {
        Actor current = city.leader;
        if (current != null && !current.isRekt() && current.isAlive() && current.isAdult() && !current.isKing() &&
            current.isUnitFitToRule()) return current;
        return (city.units ?? new List<Actor>()).ToList()
            .Where(actor => actor != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() && !actor.isKing() &&
                            actor.isUnitFitToRule())
            .OrderByDescending(actor => actor.GetOrCreate().socialClass == SocialClass.Noble)
            .ThenByDescending(actor => actor.data?.renown ?? 0).FirstOrDefault();
    }

    // 周制天下、部落联盟或青铜时代及以前的帝国 → 分封诸侯、附从的部落(帝国成员)；施行羁縻的帝国 → 羁縻州；
    // 封建政体或不是帝国 → 附庸诸侯；其余帝国 → 朝贡国
    private static string Subordinate(Kingdom realm, Kingdom local)
    {
        Empire empire = realm.IsEmpire() ? realm.GetEmpire() : null;
        if (empire != null && (realm.GetRegime()?.type is RegimeType.ZhouFeudalism or RegimeType.Origin or RegimeType.HuaxiaOrigin or RegimeType.Bakufu ||
                               TechnologySystem.GetEraTier(TechnologySystem.GetCultureOf(realm)) <= EarlyEraTier))
        {
            if (local.hasAlliance()) local.getAlliance().leave(local);
            empire.join(local, pForce: true);
            if (local.GetEmpire() == empire)
            {
                local.GetOrCreate().last_kingdom_status_ts = -1d;
                EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(local);
                return "enfeoff";
            }
        }
        if (empire != null && JimiSystem.Enacted(empire))
        {
            if (local.hasAlliance()) local.getAlliance().leave(local);
            empire.join(local, pForce: true);
            if (local.GetEmpire() == empire)
            {
                var data = local.GetOrCreate();
                data.jimi_mode = (int)JimiMode.Jimi;
                data.jimi_homeland_checked = true;
                data.last_kingdom_status_ts = -1d;
                EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(local);
                return "jimi";
            }
        }
        RegimeType? regime = realm.GetRegime()?.type;
        bool feudal = regime == RegimeType.Feudalism || regime == RegimeType.ZhouFeudalism;
        if (empire != null && !feudal)
        {
            local.JoinTakenAlliance(empire, pForce: true);
            if (local.HasTakenAlliance()) return "tributary";
        }
        return FeudalVassalService.ForceBind(realm, local) ? "vassal" : "independent";
    }

    #endregion

    #region 飞地易帜

    // 古典时代留不住飞地(用户 2026-10-27 选"飞地城自己自立")：陆路不通、控制力不到 DefectThreshold 的飞地城，
    // 除了照旧改投邻国，还会由当地首领自立为独立王国——时代越早越常见(EraSecession)，近代以后不再自立
    private const int PremodernTier = 5;

    private static float EraSecession(Kingdom realm)
    {
        int tier = TechnologySystem.IsEnabled ? TechnologySystem.GetEraTier(TechnologySystem.GetCultureOf(realm)) : EarlyEraTier;
        return tier <= EarlyEraTier ? 1.5f : tier <= PremodernTier ? 1f : tier <= PremodernTier + 2 ? 0.3f : 0f;
    }

    // 飞地自立：这座城连同挨着的、同属一国的飞地城(至多 ReleaseClusterMax 座)由当地首领自立为国，不再安置为附庸、朝贡
    private static bool Secede(Kingdom realm, City seed)
    {
        Kingdom former = seed.kingdom;
        var cluster = new List<City> { seed };
        var seen = new HashSet<City> { seed };
        for (int i = 0; i < cluster.Count && cluster.Count < ReleaseClusterMax; i++)
            foreach (City next in (IEnumerable<City>)cluster[i].neighbours_cities ?? Array.Empty<City>())
            {
                if (cluster.Count >= ReleaseClusterMax) break;
                if (next == null || !seen.Add(next) || next.kingdom != former || next.kingdom.capital == next ||
                    !Valid(next) || !next.GetOrCreate().admin_exclave) continue;
                cluster.Add(next);
            }
        string seedName = seed.data.name;
        Kingdom local = ReleaseToLocal(realm, cluster, _ => "exclave", out _);
        if (local == null) return false;
        if (former != realm && former.cities.Count == 0 && former.GetEmpire() == realm.GetEmpire())
            former.GetEmpire()?.leave(former);
        EventRecorder.Record(realm, string.Format(LM.Get("admin_exclave_secede_history"), seedName, realm.GetKingdomName(),
            local.GetKingdomName(), cluster.Count));
        LogService.LogInfo($"[EmpireCraft][飞地自立] {realm.GetKingdomName()} 的飞地 {seedName} 等 {cluster.Count} 城自立为 {local.GetKingdomName()}");
        return true;
    }

    private static void Defect(Kingdom realm, List<City> cities)
    {
        float era = EraSecession(realm);
        foreach (City city in cities)
        {
            if (!Valid(city)) continue;
            var data = city.GetOrCreate();
            if (!data.admin_exclave || data.admin_control >= DefectThreshold || city.kingdom.capital == city ||
                city.kingdom.hasEnemies() || city.GetOrCreate().OccupiedStatus?.Count > 0 ||
                UnificationSystem.InGrace(city.kingdom)) continue;
            float chance = (DefectThreshold - data.admin_control) * DefectChance;
            // 陆上只挨着一家的飞地(被它包着)：改投那一家，不自立成一城小国(用户 2026-10-27 嫌版图太碎、选"飞地归包围它的国家")
            Kingdom surrounding = Surrounding(city, realm);
            if (surrounding != null)
            {
                if (UnityEngine.Random.value >= chance * Mathf.Max(1f, era)) continue;
                if (Hand(realm, city, surrounding)) return; // 每国每年至多一座
                continue;
            }
            if (era > 0f && UnityEngine.Random.value < chance * era)
            {
                if (Secede(realm, city)) return; // 每国每年至多一处
                continue;
            }
            if (UnityEngine.Random.value >= chance) continue;
            Kingdom receiver = Neighbour(city, realm);
            if (receiver == null) continue;
            if (Hand(realm, city, receiver)) return; // 每国每年至多一座
        }
    }

    // 飞地改投 receiver
    private static bool Hand(Kingdom realm, City city, Kingdom receiver)
    {
        Kingdom former = city.kingdom;
        city.joinAnotherKingdom(receiver);
        if (city.kingdom != receiver) return false;
        city.GetOrCreate().admin_control = -1f;
        if (former != realm && former.cities.Count == 0 && former.GetEmpire() == realm.GetEmpire())
            former.GetEmpire()?.leave(former);
        EventRecorder.Record(realm, string.Format(LM.Get("admin_control_defect_history"),
            city.data.name, realm.data.name, receiver.data.name));
        return true;
    }

    // 陆上只挨着一家(按宗主、帝国中央算)的飞地：返回那一家里和它接壤最长、不在打仗的国家；挨着几家、没有邻国的为空
    private static Kingdom Surrounding(City city, Kingdom realm)
    {
        Kingdom top = null;
        foreach (TileZone zone in city.border_zones)
            foreach (TileZone next in zone?.neighbours ?? Array.Empty<TileZone>())
            {
                Kingdom neighbour = next?.city?.kingdom;
                if (!Valid(neighbour) || Realm(neighbour) == realm || neighbour.wild) continue;
                Kingdom sovereign = EmpireCoreControl.Sovereign(neighbour);
                if (top == null) top = sovereign;
                else if (sovereign != top) return null;
            }
        if (top == null) return null;
        Kingdom receiver = Neighbour(city, realm);
        return receiver != null && EmpireCoreControl.Sovereign(receiver) == top ? receiver : null;
    }

    // 和这座城接壤最长、不在打仗的邻国
    private static Kingdom Neighbour(City city, Kingdom realm)
    {
        var contacts = new Dictionary<Kingdom, int>();
        foreach (TileZone zone in city.border_zones)
            foreach (TileZone next in zone?.neighbours ?? Array.Empty<TileZone>())
            {
                Kingdom neighbour = next?.city?.kingdom;
                if (!Valid(neighbour) || Realm(neighbour) == realm || neighbour.hasEnemies() ||
                    AncientWarfareCompatibility.OwnsObject(next.city)) continue;
                contacts.TryGetValue(neighbour, out int border);
                contacts[neighbour] = border + 1;
            }
        return contacts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key.id)
            .Select(pair => pair.Key).FirstOrDefault();
    }

    #endregion
}

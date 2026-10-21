using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 土崩瓦解：帝国内忧深重时，一省首义，各省陆续响应。
//   · 内忧(0~1)：正统 50 以下、王朝积弊 40 以上、国库亏空或欠饷、阶层怨气 60 以上、篡位之君在位(十年内 0.5)、
//     朝政积弊六条平均 50 以上(90 满，见 MaladySystem.CollapseFactor)，
//     取最重的一项再加其余的四分之一；
//   · 首义(每年)：概率 = 内忧 × 0.25；离心最重、离心度不低于 0.5 的一省宣布独立。
//     离心度 = (1 − 行政控制力) + 都城稳定不足 70 的部分/70，近代再看革新理念，异文化再加 0.3，
//     手握重兵的藩镇(节度使、都护府)、世袭的埃米尔再加 0.3(军阀割据)，天命动荡或权臣掌朝时的道、行了军镇强藩的幕府之藩
//     再加 0.2，正自顾打仗的减 0.2；古典共和(未行元首制)、原始政体首义与响应的机会减半；
//     开国雄主登基头 15 年、上次瓦解结束后 15 年内不发生；周制春秋战国另有规则，不在此列；
//     首义归大分裂冷却管(见 UpheavalGuard，用户 2026-10-21 选)：上次大分裂(更替之乱、应仁之乱、宗室另立等)15 年内不首义；
//     各省起义都记为大分裂。瓦解不受单次带走三分之一的上限(否则帝国永远倒不了)，收束见下；
//   · 响应(此后两年，每月)：离心度不低于 0.5 的各省按 0.06 × 内忧(至少 0.35) × 离心度 检定，与已独立之省接壤的再乘 1.6，单月最多 35%；
//     (玩家 2026-10-21 反馈"除了阿拉伯和游牧，其他国家很容易分裂"，用户选"土崩瓦解降频"：首义 0.5 → 0.25，响应以前不看离心门槛，一次约走一半成员)
//   · 近代(本文化可废君主，或已入工业时代且开放了党禁——先要立宪)：各省合组革命同盟与朝廷开战，沿用地方共和同盟的结局(见 RepublicSystem)：
//       独立各省的城过半即逼皇帝退位、帝国改行共和；打赢同样退位；议和且三省以上则联合建立共和国；打输各省回归；
//   · 古代：各省各自宣布独立、与朝廷开战(地方独立)，群雄割据；两年收束时起义各省(首义 + 响应)离开时带走的城超过瓦解前的四分之三
//     (即只算起义，帝国剩下的不到四分之一；封建帝国同样)，帝国灭亡：没有响应起义、听命于中央的直辖成员把城并入核心王国，
//     核心降为普通王国，自治、封建、附庸、羁縻成员照旧各自独立(见 TryFall)。
public static class ProvincialCollapseSystem
{
    private const float RiseChance = 0.25f;
    private const float MinDisaffection = 0.5f;
    private const int CascadeMonths = 24;
    private const float ResponseChance = 0.06f;
    private const float CascadeCrisis = 0.35f;
    private const float NeighbourBoost = 1.6f;
    private const float MaxResponse = 0.35f;
    private const int CooldownYears = 15;
    private const float AbdicationShare = 0.5f;
    private const float FallShare = 0.25f, FeudalFallShare = 0.25f;
    // 道在动荡、权臣掌朝时，幕府的藩在军镇强藩下，离心度另加
    private const float ProvinceUnrest = 0.2f;
    private const int ModernEraTier = 7;
    private const double SecondsPerMonth = 5d;

    private static ConstitutionalEconomyState State(Empire empire) => empire?.data?.constitutional_economy;

    public static bool Active(Empire empire) => State(empire)?.collapse_started_at >= 0d;

    // 君主制，以及古典共和施行了元首制的(用户 2026-10-20 定"罗马元首制也会分裂"：元首家传、行省派总督，与帝制无异；
    // 门槛、机会与君主制一样。以前古典共和不算君主制，元首制帝国从不瓦解)
    private static bool Applies(Empire empire) =>
        empire?.data != null && !empire.isRekt() && !empire.IsArchived() && empire.CoreKingdom != null &&
        !empire.CoreKingdom.isRekt() && !RepublicSystem.IsRepublic(empire) && RegimeFactor(empire) > 0f &&
        !ZhouEraSystem.RoyalReclaimBlocked(empire) && !AncientWarfareCompatibility.Owns(empire.CoreKingdom);

    // 首义、响应机会的政体系数：君主制、元首制照常；古典共和(未行元首制)、原始政体也有王朝积弊，以前却没有任何
    // 崩溃出口、成了僵尸国，现在也会瓦解、机会减半，只走古代(各省自立)那一路(用户 2026-10-21 选"古典共和/原始政体能瓦解")
    private const float MinorRegimeFactor = 0.5f;

    private static float RegimeFactor(Empire empire)
    {
        RegimeType? type = empire?.CoreKingdom?.GetRegime()?.type;
        if (RegimeManager.IsMonarchy(type) || empire.IsPrincipate()) return 1f;
        return type == RegimeType.ClassicalRepublic || RegimeManager.IsPrimitive(type) ? MinorRegimeFactor : 0f;
    }

    // 瓦解收束时剩下的城不到瓦解前的多少就灭亡：其余四分之一；封建帝国四成(诸侯本就各自为政，名号最难死；用户 2026-10-21 选
    // "西欧能整合、帝国能倒")→ 同样四分之一(同日玩家反馈"帝国容易突然碎一地"，用户选"瓦解收束不再全散"：
    // 以前剩下的城把两年里外战、更替之乱、决议自立丢的都算进去，封建帝国四成的门槛一碰就倒)
    private static float FallShareOf(Empire empire) =>
        empire?.CoreKingdom?.GetRegime()?.type == RegimeType.Feudalism ? FeudalFallShare : FallShare;

    // 以前只看技术进了工业时代：制度还停在三省六部、没立宪的帝国，一次土崩瓦解就成了共和革命，全文化跟着进入现代
    private static bool Modern(Empire empire) =>
        !TechnologySystem.PremodernLocked && (RepublicSystem.CanAbolish(empire) ||
                                              TechnologySystem.GetEraTier(InstitutionSystem.GetPrimaryCulture(empire)) >= ModernEraTier &&
                                              InstitutionSystem.GetFeature(InstitutionSystem.GetPrimaryCulture(empire), PartySystem.FeaturePartyPolitics) > 0f);

    public static float Crisis(Empire empire)
    {
        if (empire?.data == null) return 0f;
        float legitimacy = Mathf.Clamp01((50f - empire.Legitimacy) / 40f);
        float strain = Mathf.Clamp01((DynasticCycleSystem.GetPressure(empire) - 40f) / 40f);
        float fiscal = EmpireBankruptcySystem.IsBankrupt(empire) ? 0.6f : 0f;
        float grievance = Mathf.Clamp01((InstitutionSystem.GetClassGrievances(empire).Values.DefaultIfEmpty(0f).Max() - 60f) / 40f);
        // 权臣、外戚篡位登基，十年内人心未附
        float usurped = empire.Emperor != null && empire.data.minister_usurper_id == empire.Emperor.id &&
                        empire.data.newEmperor_timestamp >= 0d && Date.getYearsSince(empire.data.newEmperor_timestamp) < 10
            ? 0.5f : 0f;
        // 武人跋扈(藩镇)、诸侯坐大(见 FactionBalanceSystem)
        float faction = FactionBalanceSystem.CollapseFactor(empire);
        // 朝政积弊六条平均过五十(见 MaladySystem.CollapseFactor)
        float malady = MaladySystem.CollapseFactor(empire);
        float top = Mathf.Max(Mathf.Max(Mathf.Max(legitimacy, strain), Mathf.Max(fiscal, grievance)),
            Mathf.Max(Mathf.Max(usurped, faction), malady));
        return Mathf.Clamp01(top + 0.25f * (legitimacy + strain + fiscal + grievance + usurped + faction + malady - top));
    }

    private static List<Kingdom> Provinces(Empire empire) =>
        empire.kingdoms_list.Where(kingdom => kingdom?.data != null && !kingdom.isRekt() && kingdom != empire.CoreKingdom &&
                                              kingdom.hasKing() && kingdom.capital != null && kingdom.cities?.Count > 0 &&
                                              !kingdom.IsFactionRebelling() && !kingdom.IsLocalRebelling() &&
                                              RebellionSystem.CanAttempt(kingdom)).ToList();

    private static float Disaffection(Empire empire, Kingdom province, bool modern, PartyIdeology ideology)
    {
        float control = province.cities.Average(AdministrativeControlSystem.Get);
        City capital = province.capital;
        float stability = CityStabilitySystem.State(capital) != null ? CityStabilitySystem.Effective(capital) : 50f;
        float value = (1f - control) + Mathf.Clamp01((70f - stability) / 70f);
        if (modern) value += Mathf.Clamp01(IdeologyPopulationSystem.GetKingdomShare(province, ideology) * 2f) * 0.5f;
        string ruling = CompositeEmpireService.GetRulingCulture(empire);
        string local = CultureService.GetMainCulture(capital);
        if (CultureService.IsValidCulture(local) && !string.Equals(local, ruling, StringComparison.Ordinal)) value += 0.3f;
        // 军阀割据：手握重兵的藩镇；世袭的埃米尔同样(见 EmirateSystem)
        if (province.GetKingdomType() is KingdomType.LvLing_jiedushi or KingdomType.LvLing_duhufu ||
            EmirateSystem.IsHereditaryEmir(empire, province)) value += 0.3f;
        // 天命动荡、权臣掌朝时，朝廷派的道也人心思变(用户 2026-10-21 选"华夏有真乱世"：以前道控制力满、稳定不差，几乎从不响应)
        else if (province.GetKingdomType() == KingdomType.LvLing_province &&
                 (empire.data.is_been_controlled || TianxiaSystem.PhaseOf(empire.CoreKingdom) == TianxiaSystem.Unrest))
            value += ProvinceUnrest;
        // 行了"军镇强藩"的幕府，大名的藩手握重兵(用户 2026-10-21 选"幕府会倒、应仁之乱")
        else if (province.GetKingdomType() == KingdomType.Bakufu_han &&
                 InstitutionSystem.HasFeature(empire, CityPopulationSystem.FeatureRegional))
            value += ProvinceUnrest;
        if (province.getWars().Any(war => war != null && !war.hasEnded())) value -= 0.2f;
        return Mathf.Max(0f, value);
    }

    // 每年(ConstitutionalEconomySystem 年度结算)：首义
    public static void Yearly(Empire empire)
    {
        try
        {
            if (!Applies(empire) || Active(empire)) return;
            ConstitutionalEconomyState state = State(empire);
            if (state == null) return;
            double now = World.world.getCurWorldTime();
            if (state.collapse_ended_at >= 0d && now >= state.collapse_ended_at &&
                Date.getYearsSince(state.collapse_ended_at) < CooldownYears) return;
            if (!RebellionSystem.CanAttempt(empire.CoreKingdom)) return;
            float crisis = Crisis(empire);
            float factor = RegimeFactor(empire);
            if (crisis <= 0f || UnityEngine.Random.value >= crisis * RiseChance * factor) return;
            // 大分裂冷却里不首义(见 UpheavalGuard)；瓦解进行中的情形上面 Active 已挡
            if (UpheavalGuard.InCooldown(empire))
            {
                LogService.LogInfo($"[EmpireCraft][土崩瓦解] {empire.GetEmpireName()} 本当首义(内忧{crisis:0.00})，" +
                                   $"大分裂冷却中(上次在 {Date.getYearsSince(empire.data.last_upheaval_at)} 年前)，作罢");
                return;
            }
            bool modern = factor >= 1f && Modern(empire);
            if (modern && RepublicSystem.HasActiveRevolutionWar(empire)) return;
            PartyIdeology ideology = RepublicSystem.ReformIdeology(empire);
            Kingdom first = Provinces(empire)
                .Select(province => (province, value: Disaffection(empire, province, modern, ideology)))
                .Where(pair => pair.value >= MinDisaffection).OrderByDescending(pair => pair.value)
                .Select(pair => pair.province).FirstOrDefault();
            if (first == null) return;
            state.collapse_started_at = now;
            state.collapse_month_at = now;
            state.collapse_modern = modern;
            state.collapse_war_id = -1L;
            state.collapse_origin_cities = empire.kingdoms_list.Where(kingdom => kingdom?.cities != null && !kingdom.isRekt())
                .Sum(kingdom => kingdom.cities.Count);
            state.collapse_risen ??= new List<long>();
            state.collapse_risen.Clear();
            empire.data.collapse_risen_cities = 0;
            if (!Rise(empire, state, first, ideology, first: true)) End(state);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][土崩瓦解] 首义失败: {exception.Message}");
        }
    }

    // 每次帝国更新都调用，自带月度计时：各省响应、近代过半逼退位、两年后收束
    public static void Monthly(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || state.collapse_started_at < 0d || World.world == null) return;
        double now = World.world.getCurWorldTime();
        if (now >= state.collapse_month_at && now < state.collapse_month_at + SecondsPerMonth) return;
        state.collapse_month_at = now;
        try
        {
            if (!Applies(empire) || now < state.collapse_started_at ||
                Date.getMonthsSince(state.collapse_started_at) >= CascadeMonths)
            {
                bool completed = Applies(empire) && now >= state.collapse_started_at;
                End(state);
                if (completed) TryFall(empire, state);
                return;
            }
            War war = state.collapse_modern ? World.world.wars.get(state.collapse_war_id) : null;
            if (state.collapse_modern && (war == null || war.hasEnded()))
            {
                End(state);
                return;
            }
            float crisis = Mathf.Max(Crisis(empire), CascadeCrisis);
            PartyIdeology ideology = RepublicSystem.ReformIdeology(empire);
            List<Kingdom> risen = state.collapse_risen.Select(id => World.world.kingdoms.get(id))
                .Where(kingdom => kingdom?.data != null && !kingdom.isRekt()).ToList();
            foreach (Kingdom province in Provinces(empire))
            {
                float value = Disaffection(empire, province, state.collapse_modern, ideology);
                if (value < MinDisaffection) continue;
                bool near = risen.Any(other => other.IsNeighbourWith(province));
                float chance = Mathf.Min(MaxResponse, ResponseChance * crisis * value * (near ? NeighbourBoost : 1f)) *
                               RegimeFactor(empire);
                if (UnityEngine.Random.value < chance && Rise(empire, state, province, ideology, first: false))
                    risen.Add(province);
            }
            if (state.collapse_modern) CheckAbdication(state, war);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][土崩瓦解] 响应失败: {exception.Message}");
        }
    }

    // 古代瓦解收束：起义各省带走的城之外，帝国剩下的不到瓦解前的四分之一，帝国就此灭亡，中央降为普通王国。
    // 以前收束只清计时，朝廷剩几座城也一直算现存帝国，地图上早已四分五裂。
    // 只算起义(首义 + 响应)各省离开时带走的城(用户 2026-10-21 选"瓦解收束不再全散")：以前按帝国眼下剩的城算，
    // 两年里外战丢的、更替之乱走的、决议自立的都算成瓦解，帝国常常就此灭亡、所有成员各自独立
    private static bool TryFall(Empire empire, ConstitutionalEconomyState state)
    {
        if (state.collapse_modern || state.collapse_origin_cities <= 0 || empire?.data == null || empire.IsArchived() ||
            empire.CoreKingdom == null || empire.CoreKingdom.isRekt()) return false;
        int taken = RisenCities(empire, state);
        int held = Mathf.Max(0, state.collapse_origin_cities - taken);
        int remaining = UpheavalGuard.Cities(empire);
        LogService.LogInfo($"[EmpireCraft][土崩瓦解] {empire.GetEmpireName()} 两年收束：瓦解前 {state.collapse_origin_cities} 城，" +
                           $"起义各省带走 {taken} 城(只算起义剩 {held} 城，眼下实有 {remaining} 城)，" +
                           $"{(held >= state.collapse_origin_cities * FallShareOf(empire) ? "帝国存续" : "帝国灭亡")}");
        if (held >= state.collapse_origin_cities * FallShareOf(empire)) return false;
        // 史书照实记帝国眼下还有几座城
        EventRecorder.Record(empire, actor: empire.Emperor, logKingdom: empire.CoreKingdom, text: string.Format(
            LM.Get("collapse_fall_history"), empire.GetEmpireFullName(), remaining, state.collapse_origin_cities));
        Absorb(empire, state);
        ModClass.EMPIRE_MANAGER?.dissolveEmpire(empire);
        return true;
    }

    // 起义各省离开时带走的城；旧档(瓦解进行到一半读进来，没记过)按还在帝国外的起义各省眼下的城数估
    private static int RisenCities(Empire empire, ConstitutionalEconomyState state)
    {
        if (empire.data.collapse_risen_cities >= 0) return empire.data.collapse_risen_cities;
        return (state.collapse_risen ?? new List<long>()).Select(id => World.world.kingdoms.get(id))
            .Where(kingdom => kingdom?.data != null && !kingdom.isRekt() && kingdom.GetEmpire() != empire)
            .Sum(kingdom => kingdom.cities?.Count ?? 0);
    }

    // 帝国灭亡时，没有响应起义、听命于中央的直辖成员(道、阿拉伯行省、元首制行省、军府等没有自主外交权、朝廷任命的，
    // 见 EmpirePeacePatch.Obeys)把城并入核心王国，不再随解散各自独立(用户 2026-10-21 选"瓦解收束不再全散")；
    // 羁縻(异族自治)、自治、封建、附庸类成员照旧独立
    private static void Absorb(Empire empire, ConstitutionalEconomyState state)
    {
        Kingdom core = empire.CoreKingdom;
        var risen = new HashSet<long>(state.collapse_risen ?? new List<long>());
        var absorbed = new List<string>();
        int moved = 0;
        foreach (Kingdom member in empire.kingdoms_list.ToList())
        {
            if (member?.data == null || member.isRekt() || member == core || risen.Contains(member.id) ||
                !GamePatches.EmpirePeacePatch.Obeys(empire, member) || member.GetKingdomType() == KingdomType.LvLing_jimizhou ||
                JimiSystem.IsJimiRegion(member, empire)) continue;
            string name = member.GetKingdomName();
            List<City> cities = (member.cities ?? new List<City>()).Where(city => city?.data != null && !city.isRekt()).ToList();
            foreach (City city in cities) city.joinAnotherKingdom(core);
            int count = cities.Count(city => city.kingdom == core);
            if (count <= 0) continue;
            absorbed.Add(name);
            moved += count;
        }
        if (absorbed.Count > 0)
            LogService.LogInfo($"[EmpireCraft][土崩瓦解] {empire.GetEmpireName()} 灭亡前，直辖的 {string.Join("、", absorbed)} 共 {moved} 城并入" +
                               $"{core.GetKingdomName()}，不随解散独立");
    }

    private static void End(ConstitutionalEconomyState state)
    {
        state.collapse_started_at = -1d;
        state.collapse_ended_at = World.world?.getCurWorldTime() ?? -1d;
        state.collapse_war_id = -1L;
    }

    private static bool Rise(Empire empire, ConstitutionalEconomyState state, Kingdom province, PartyIdeology ideology, bool first)
    {
        Kingdom core = empire.CoreKingdom;
        // 离开时带走的城(收束时只按起义带走的算，见 TryFall)
        int cities = province.cities?.Count ?? 0;
        RebellionCauseData cause = RebellionSystem.Capture(province, province.capital, "rebellion_reason_collapse",
            empire.GetEmpireFullName());
        War war;
        if (state.collapse_modern)
        {
            war = RiseModern(empire, state, province, ideology);
            if (war == null) return false;
        }
        else
        {
            war = DiplomacyHelpers.wars.newWar(province, core, WarTypeLibrary.normal);
            if (war == null) return false;
            empire.leave(province, true, true);
            war.SetEmpireWarType(EmpireWarType.地方独立);
        }
        RebellionSystem.Record(province, war, cause);
        state.collapse_risen.Add(province.id);
        if (empire.data.collapse_risen_cities >= 0) empire.data.collapse_risen_cities += cities;
        // 各省起义都记为大分裂(见 UpheavalGuard)：首义记日志，响应只刷新时间——冷却从最后一省离开时算
        UpheavalGuard.Mark(empire, first ? "土崩瓦解首义" : null);
        EventRecorder.Record(empire, actor: province.king, logKingdom: province, text: string.Format(
            LM.Get(first ? "collapse_first_history" : "collapse_respond_history"), province.GetKingdomFullName(),
            empire.GetEmpireFullName()));
        return true;
    }

    // 近代：首义之省发起革命战争，响应的各省加入攻方、结为同盟
    private static War RiseModern(Empire empire, ConstitutionalEconomyState state, Kingdom province, PartyIdeology ideology)
    {
        Kingdom core = empire.CoreKingdom;
        War war = World.world.wars.get(state.collapse_war_id);
        if (war == null || war.hasEnded())
        {
            empire.leave(province, pRecalc: true, isLeave: true);
            province.StartFactionRebelling(null);
            war = World.world.diplomacy.startWar(province, core, WarTypeLibrary.rebellion);
            if (war == null)
            {
                province.EndFactionRebelling();
                empire.join(province, pForce: true, pLegitimacyTransfer: true);
                return null;
            }
            war.SetEmpireWarType(EmpireWarType.派系叛乱);
            WarExtension.WarExtraData snapshot = war.GetOrCreate();
            snapshot.republic_revolution_empire_id = empire.id;
            snapshot.republic_revolution_party_id = "";
            snapshot.republic_revolution_party_name = province.GetKingdomName();
            snapshot.republic_revolution_ideology = ideology;
            snapshot.republic_revolution_leader_id = province.king?.id ?? -1L;
            snapshot.republic_regional_coalition = true;
            snapshot.republic_regional_forced_abdication = true;
            snapshot.republic_regional_leader_kingdom_id = province.id;
            snapshot.republic_regional_alliance_id = -1L;
            snapshot.republic_revolution_allied_kingdom_ids = new List<long>();
            snapshot.republic_provincial = true;
            war.data.name = string.Format(LM.Get("collapse_war_name"), province.GetKingdomName());
            state.collapse_war_id = war.data.id;
            return war;
        }
        empire.leave(province, pRecalc: true, isLeave: true);
        province.StartFactionRebelling(null);
        war.joinAttackers(province);
        WarExtension.WarExtraData data = war.GetOrCreate();
        data.republic_revolution_allied_kingdom_ids ??= new List<long>();
        data.republic_revolution_allied_kingdom_ids.Add(province.id);
        Kingdom leader = World.world.kingdoms.get(data.republic_regional_leader_kingdom_id);
        Alliance alliance = World.world.alliances.get(data.republic_regional_alliance_id);
        if (alliance != null && alliance.isAlive())
        {
            if (!province.hasAlliance()) alliance.join(province, true, true);
        }
        else if (leader?.data != null && !leader.isRekt() && !leader.hasAlliance() && !province.hasAlliance())
        {
            alliance = World.world.alliances.newAlliance(leader, province);
            if (alliance != null)
            {
                ModAllianceService.Mark(alliance);
                alliance.setName(string.Format(LM.Get("regional_republic_alliance_name"), leader.GetKingdomName()));
                data.republic_regional_alliance_id = alliance.id;
            }
        }
        return war;
    }

    // 近代：独立各省的城过半，朝廷无力回天，逼皇帝退位
    private static void CheckAbdication(ConstitutionalEconomyState state, War war)
    {
        if (war == null || war.hasEnded() || state.collapse_origin_cities <= 0) return;
        int held = state.collapse_risen.Select(id => World.world.kingdoms.get(id))
            .Where(kingdom => kingdom?.data != null && !kingdom.isRekt() && kingdom.GetEmpire() == null)
            .Sum(kingdom => kingdom.cities?.Count ?? 0);
        if (held < state.collapse_origin_cities * AbdicationShare) return;
        DiplomacyHelpers.wars.endWar(war, WarWinner.Attackers);
    }
}

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
//     律令制(王朝积弊适用的中央集权官僚帝国)另有入口(用户 2026-10-26："只有当朝廷积弊满额时的民变触发后，满足任意一条，
//     则君权下放、地方权力扩张，军阀崛起，进入割据混战阶段：① 持续 10 年无法平定叛乱；② 民变规模达到总数 50%")：
//     积弊满额、满额民变已起之后，本朝民变/起义立的国最早一场满 10 年还没平定，或它们占的城达满额民变起时原有城数的五成，
//     才首义(门槛过了不再掷机会)；破产、正统低、篡位这些单项照旧推高内忧(响应、化解用)，不再单独触发律令制的瓦解。
//     游牧、阿拉伯、日本、封建各线照旧按内忧；
//     离心度 = (1 − 行政控制力) + 都城稳定不足 70 的部分/70，近代再看革新理念，异文化再加 0.3，
//     不与京畿(核心王国、陪都)接壤再加 0.1，边疆(与帝国外的国家接壤，朝贡国、附庸不算)再加 0.2(与下面藩镇的 0.3 只取大)，
//     手握重兵的藩镇(节度使、都护府)、世袭的埃米尔再加 0.3(军阀割据)，天命动荡或权臣掌朝时的道、行了军镇强藩的幕府之藩
//     再加 0.2，正自顾打仗的减 0.2；古典共和(未行元首制)、原始政体首义与响应的机会减半；
//     开国雄主登基头 15 年、上次瓦解结束后 15 年内不发生；周制春秋战国另有规则，不在此列；
//     首义归大分裂冷却管(见 UpheavalGuard，用户 2026-10-21 选)：上次大分裂(更替之乱、应仁之乱、宗室另立等)15 年内不首义；
//     各省起义都记为大分裂。瓦解不受单次带走三分之一的上限(否则帝国永远倒不了)，收束见下；
//   · 分裂进程(用户 2026-10-21 反馈"突然裂开这个设定很突兀"，选"预警前兆、先割据后独立、连锁观望"，见 SchismSystem)：
//     上面的首义检定中了不再当场首义——离心度不低于 0.5 的各省列为不稳之省，先预警 6~18 个月("天下骚然")、再割据 6~24 个月
//     (不缴税、不随征)，然后由其中离心最重的一省首义(BeginRising)，其余割据之省观望；预警、割据期间正统与积弊回落且内忧平息、
//     中央打赢一仗、新君在位满三年都能化解。下面的"首义""响应"都从破局时算起；
//   · 响应(此后两年，每月)：离心度不低于 0.5 的各省按 0.06 × 内忧(至少 0.35) × 离心度 检定，与已独立之省接壤的再乘 1.6，单月最多 35%；
//     分裂进程里只有观望的割据之省响应，机会再乘首义的声势(见 SchismSystem.Momentum)；收束时还在观望的复奉号令(帝国灭亡则照下面的规则)；
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
    // 远近(用户 2026-10-27 转群友提议"边疆行省、不与京畿接壤的行省更易叛"，选加在离心度上)：控制力从最近的治所算，
    // 行省自己的都城、陪都都算治所，远方的行省离自家省会近，离京城再远也不显得离心，京城在哪定不出帝国的疆域。现在：
    //   · 不与京畿(核心王国的城、陪都)接壤的省 +RemoteDisaffection；
    //   · 边疆省(与帝国以外的国家接壤；本帝国的朝贡国、附庸是缓冲，不算)+FrontierDisaffection，
    //     与藩镇、世袭埃米尔的军阀割据加成(WarlordDisaffection)只取较大的一项，免得边镇一下加满；
    //   离心度只在土崩瓦解里用，照旧要有内忧(正统低、积弊重、亏空……)才会首义、响应；天下一统没有外国就没有边疆
    private const float RemoteDisaffection = 0.1f, FrontierDisaffection = 0.2f, WarlordDisaffection = 0.3f;
    private const int ModernEraTier = 7;
    private const double SecondsPerMonth = 5d;
    // 分裂进程的化解门槛(见 SchismSystem)：内忧里正统一项从 50 以下算起、朝政积弊一项从 50 以上算起
    private const int GateLegitimacy = 50;
    private const float GateMalady = 50f;
    // 律令制群雄割据的入口：民变满 WarlordRebellionYears 年不平，或叛军占了原有城数的 WarlordRebelShare
    private const int WarlordRebellionYears = 10;
    private const float WarlordRebelShare = 0.5f;

    private static ConstitutionalEconomyState State(Empire empire) => empire?.data?.constitutional_economy;

    public static bool Active(Empire empire) => State(empire)?.collapse_started_at >= 0d;

    // 君主制，以及古典共和施行了元首制的(用户 2026-10-20 定"罗马元首制也会分裂"：元首家传、行省派总督，与帝制无异；
    // 门槛、机会与君主制一样。以前古典共和不算君主制，元首制帝国从不瓦解)
    private static bool Applies(Empire empire) =>
        empire?.data != null && !empire.isRekt() && !empire.IsArchived() && empire.CoreKingdom != null &&
        !empire.CoreKingdom.isRekt() && !RepublicSystem.IsRepublic(empire) && RegimeFactor(empire) > 0f &&
        !ZhouShielded(empire) && !AncientWarfareCompatibility.Owns(empire.CoreKingdom);

    // 周制：春秋诸侯尊王，周室不瓦解，正统跌破 ZhouSpringCollapseLegitimacy 例外；战国(只剩王畿诸郡，郡也会割据)、
    // 一统之后照常(用户 2026-10-27 截图西大和朝、赵朝：以前春秋、战国整段豁免，周室正统 0、积弊满额也没有出口)
    private const int ZhouSpringCollapseLegitimacy = 30;

    private static bool ZhouShielded(Empire empire) =>
        ZhouEraSystem.Era(empire) == ZhouEraSystem.Spring && empire.Legitimacy >= ZhouSpringCollapseLegitimacy;

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

    // excludeStrain：不算王朝积弊一项(它只增不减，分裂进程的化解不能等它回落，见 SchismSystem.Resolution)
    public static float Crisis(Empire empire, bool excludeStrain = false)
    {
        if (empire?.data == null) return 0f;
        float legitimacy = Mathf.Clamp01((50f - empire.Legitimacy) / 40f);
        float strain = excludeStrain ? 0f : Mathf.Clamp01((DynasticCycleSystem.GetPressure(empire) - 40f) / 40f);
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

    // 律令制、王朝积弊适用(立宪等不适用的照旧按内忧，否则积弊恒为零、永不瓦解)
    private static bool Centralized(Empire empire) =>
        empire.CoreKingdom?.GetRegime()?.type == RegimeType.LvLing && DynasticCycleSystem.Applies(empire);

    // 律令制群雄割据的门槛：积弊满额、满额民变已起，且 ① 本朝民变/起义最早一场满 10 年还没平定(立的国还在、没回归本帝国)，
    // 或 ② 它们占的城达满额民变起时原有城数的五成。过了返回缘由(日志用)，没过返回 null
    private static string WarlordGate(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || !DynasticCycleSystem.SaturatedUprisingStarted(empire)) return null;
        (int _, int cities, int years) = DynasticCycleSystem.PopularRebellions(empire);
        if (years >= WarlordRebellionYears) return $"民变{years}年不平";
        int origin = state.saturated_origin_cities;
        if (origin > 0 && cities >= origin * WarlordRebelShare) return $"叛军据{cities}城(原有{origin}城)";
        return null;
    }

    private static List<Kingdom> Provinces(Empire empire) =>
        empire.kingdoms_list.Where(kingdom => kingdom?.data != null && !kingdom.isRekt() && kingdom != empire.CoreKingdom &&
                                              kingdom.hasKing() && kingdom.capital != null && kingdom.cities?.Count > 0 &&
                                              !kingdom.IsFactionRebelling() && !kingdom.IsLocalRebelling() &&
                                              RebellionSystem.CanAttempt(kingdom)).ToList();

    // 与京畿接壤：省里有城是陪都，或挨着核心王国的城、陪都
    private static bool NearCapital(Empire empire, Kingdom province)
    {
        Kingdom core = empire.CoreKingdom;
        var jing = new HashSet<City>(MultiCapitalSystem.AuxiliaryCities(core));
        foreach (City city in province.cities)
        {
            if (city?.data == null) continue;
            if (jing.Contains(city)) return true;
            foreach (City neighbour in city.neighbours_cities)
                if (neighbour?.data != null && (neighbour.kingdom == core || jing.Contains(neighbour))) return true;
        }
        return false;
    }

    // 边疆：省里有城挨着帝国以外的国家；本帝国的朝贡国、附庸不算
    private static bool Frontier(Empire empire, Kingdom province)
    {
        foreach (City city in province.cities)
        {
            if (city?.data == null) continue;
            foreach (City neighbour in city.neighbours_cities)
            {
                Kingdom other = neighbour?.kingdom;
                if (other?.data == null || other.isRekt() || other.wild || other.GetEmpire() == empire ||
                    other.GetTakenAllianceEmpire() == empire || FeudalVassalService.GetOverlord(other)?.GetEmpire() == empire) continue;
                return true;
            }
        }
        return false;
    }

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
        // 远离京畿(见 RemoteDisaffection)
        if (!NearCapital(empire, province)) value += RemoteDisaffection;
        // 军阀割据：手握重兵的藩镇；世袭的埃米尔同样(见 EmirateSystem)。本就是边镇之意，边疆不再另加
        if (province.GetKingdomType() is KingdomType.LvLing_jiedushi or KingdomType.LvLing_duhufu ||
            EmirateSystem.IsHereditaryEmir(empire, province)) value += WarlordDisaffection;
        else
        {
            // 边疆(见 FrontierDisaffection)
            if (Frontier(empire, province)) value += FrontierDisaffection;
            // 天命动荡、权臣掌朝时，朝廷派的道也人心思变(用户 2026-10-21 选"华夏有真乱世"：以前道控制力满、稳定不差，几乎从不响应)
            if (province.GetKingdomType() == KingdomType.LvLing_province &&
                (empire.data.is_been_controlled || TianxiaSystem.PhaseOf(empire.CoreKingdom) == TianxiaSystem.Unrest))
                value += ProvinceUnrest;
            // 行了"军镇强藩"的幕府，大名的藩手握重兵(用户 2026-10-21 选"幕府会倒、应仁之乱")
            else if (province.GetKingdomType() == KingdomType.Bakufu_han &&
                     InstitutionSystem.HasFeature(empire, CityPopulationSystem.FeatureRegional))
                value += ProvinceUnrest;
        }
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
            // 律令制：只认积弊满额后的民变(见文件头)，门槛过了不再掷机会；其余照旧按内忧
            string warlords = null;
            if (Centralized(empire))
            {
                warlords = WarlordGate(empire);
                if (warlords == null) return;
            }
            else if (crisis <= 0f || UnityEngine.Random.value >= crisis * RiseChance * factor) return;
            // 大分裂冷却里、已有分裂进程时不首义(见 UpheavalGuard)；瓦解进行中的情形上面 Active 已挡
            if (UpheavalGuard.Blocked(empire, out string why))
            {
                LogService.LogInfo($"[EmpireCraft][土崩瓦解] {empire.GetEmpireName()} 本当首义(内忧{crisis:0.00})，{why}，作罢");
                return;
            }
            bool modern = factor >= 1f && Modern(empire);
            if (modern && RepublicSystem.HasActiveRevolutionWar(empire)) return;
            PartyIdeology ideology = RepublicSystem.ReformIdeology(empire);
            List<Kingdom> unstable = Provinces(empire)
                .Select(province => (province, value: Disaffection(empire, province, modern, ideology)))
                .Where(pair => pair.value >= MinDisaffection).OrderByDescending(pair => pair.value)
                .Select(pair => pair.province).ToList();
            if (unstable.Count == 0) return;
            // 分裂进程(用户 2026-10-21 选"预警前兆、先割据后独立、连锁观望"，见 SchismSystem)：以前这里当场由离心最重的一省首义；
            // 现在离心度够的各省先列为不稳之省、进入预警("天下骚然")，割据之后才由其中离心最重的一省首义(BeginRising)，其余观望。
            // 不受单次上限(否则帝国永远倒不了，见 UpheavalGuard)；化解门槛按内忧的两项：正统 50、积弊均值 50(另要内忧平息，见 SchismSystem)
            bool begun = SchismSystem.Begin(empire, SchismSystem.Collapse, unstable, GateLegitimacy, GateMalady, empire.Emperor,
                $"内忧{crisis:0.00}{(modern ? "，近代" : "")}{(warlords != null ? "，" + warlords : "")}");
            if (begun && warlords != null)
                EventRecorder.Record(empire, string.Format(LM.Get("collapse_warlord_history"), empire.GetEmpireFullName()));
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][土崩瓦解] 首义失败: {exception.Message}");
        }
    }

    // 分裂进程破局时首义(见 SchismSystem.BreakOutCollapse)：Started 已首义；Wait 近代革命战争还在打，下月再看；Failed 不再瓦解
    // (帝国已不适用、开国之君在位)或割据之省都起不了事
    public enum RisingStart
    {
        Started,
        Wait,
        Failed
    }

    // 分裂进程破局：割据之省里离心最重的一省首义(原 Yearly 的首义)，两年响应期从这时算起
    public static RisingStart BeginRising(Empire empire, List<Kingdom> candidates, out Kingdom first)
    {
        first = null;
        if (!Applies(empire) || Active(empire) || candidates == null || candidates.Count == 0) return RisingStart.Failed;
        ConstitutionalEconomyState state = State(empire);
        if (state == null || !RebellionSystem.CanAttempt(empire.CoreKingdom)) return RisingStart.Failed;
        bool modern = RegimeFactor(empire) >= 1f && Modern(empire);
        if (modern && RepublicSystem.HasActiveRevolutionWar(empire)) return RisingStart.Wait;
        PartyIdeology ideology = RepublicSystem.ReformIdeology(empire);
        var provinces = new HashSet<Kingdom>(Provinces(empire));
        first = candidates.Where(provinces.Contains)
            .OrderByDescending(province => Disaffection(empire, province, modern, ideology)).FirstOrDefault();
        if (first == null) return RisingStart.Failed;
        double now = World.world.getCurWorldTime();
        state.collapse_started_at = now;
        state.collapse_month_at = now;
        state.collapse_modern = modern;
        state.collapse_war_id = -1L;
        state.collapse_origin_cities = empire.kingdoms_list.Where(kingdom => kingdom?.cities != null && !kingdom.isRekt())
            .Sum(kingdom => kingdom.cities.Count);
        state.collapse_risen ??= new List<long>();
        state.collapse_risen.Clear();
        empire.data.collapse_risen_cities = 0;
        if (Rise(empire, state, first, ideology, first: true)) return RisingStart.Started;
        End(state);
        return RisingStart.Failed;
    }

    // 首义以来几个月(帝国窗口用)；没在瓦解返回 -1
    public static int RisingMonths(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || state.collapse_started_at < 0d || World.world == null ||
            World.world.getCurWorldTime() < state.collapse_started_at) return -1;
        return Date.getMonthsSince(state.collapse_started_at);
    }

    public const int RisingWindowMonths = CascadeMonths;

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
                // 分裂进程(见 SchismSystem)：还在观望、没响应的割据之省先复奉号令——帝国存续就照常听命；
                // 灭亡就照下面 TryFall 的规则，直辖的并入核心、自治封建的各自独立
                List<string> watchers = SchismSystem.ReleaseWatchers(empire);
                bool fell = completed && TryFall(empire, state);
                SchismSystem.EndRising(empire, watchers, fell);
                return;
            }
            War war = state.collapse_modern ? World.world.wars.get(state.collapse_war_id) : null;
            if (state.collapse_modern && (war == null || war.hasEnded()))
            {
                End(state);
                SchismSystem.EndRising(empire, SchismSystem.ReleaseWatchers(empire), false);
                return;
            }
            float crisis = Mathf.Max(Crisis(empire), CascadeCrisis);
            PartyIdeology ideology = RepublicSystem.ReformIdeology(empire);
            List<Kingdom> risen = state.collapse_risen.Select(id => World.world.kingdoms.get(id))
                .Where(kingdom => kingdom?.data != null && !kingdom.isRekt()).ToList();
            // 分裂进程里首义之后(见 SchismSystem，用户 2026-10-21 选"连锁观望")：只有观望的割据之省响应，机会再乘首义的声势
            // (首义打得顺 ×1.5、中央收复首义省都城 ×0.3、首义各省都平定 ×0)；旧档里没有进程的瓦解照旧各省都可响应
            bool staged = SchismSystem.InRising(empire);
            float momentum = staged ? SchismSystem.Momentum(empire) : 1f;
            List<Kingdom> responders = Provinces(empire);
            if (staged)
            {
                var watching = new HashSet<Kingdom>(SchismSystem.Watchers(empire));
                responders = responders.Where(watching.Contains).ToList();
            }
            foreach (Kingdom province in momentum > 0f ? responders : new List<Kingdom>())
            {
                float value = Disaffection(empire, province, state.collapse_modern, ideology);
                if (value < MinDisaffection) continue;
                bool near = risen.Any(other => other.IsNeighbourWith(province));
                float chance = Mathf.Min(MaxResponse, ResponseChance * crisis * value * (near ? NeighbourBoost : 1f)) *
                               RegimeFactor(empire) * momentum;
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
        Absorb(empire, state.collapse_risen);
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
    // 羁縻(异族自治)、自治、封建、附庸类成员照旧独立。汗国崩解、名号作废时也用(见 NomadCycleSystem.KhanateCollapse)，risen 给空、日志换 tag
    public static void Absorb(Empire empire, IEnumerable<long> risenIds, string tag = "[EmpireCraft][土崩瓦解]")
    {
        Kingdom core = empire.CoreKingdom;
        var risen = new HashSet<long>(risenIds ?? new List<long>());
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
            LogService.LogInfo($"{tag} {empire.GetEmpireName()} 灭亡前，直辖的 {string.Join("、", absorbed)} 共 {moved} 城并入" +
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
        // 离开时带走的城(收束时只按起义带走的算，见 TryFall)；起事时的都城(分裂进程的声势看它有没有被收复)
        int cities = province.cities?.Count ?? 0;
        City seat = province.capital;
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
        // 分裂进程(见 SchismSystem)：记下起事时的城数与都城(声势用)，移出观望名单
        SchismSystem.OnRisen(empire, province, cities, seat);
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

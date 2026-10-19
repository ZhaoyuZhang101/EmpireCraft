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

// 水土(用户 2026-10-20 问答定："游牧离开草原、定居政权占着旱地，迟迟不适应就逐档变弱，各有出路")：
//   · 地：游戏里没有专门的草原(biome_grass 是最普通的温带地，也能耕)，旱地 = LandKinds.Dry(荒漠、稀树草原、荒原；海滩沙地不算)，
//     与毡帐村镇同一口径(RuralSettlementSystem.YurtGround)。一座城领土里的陆地(不含水、岩浆)过半是旱地算"旱地城"，其余算"农地城"；
//     地块的格子构成随战线地形一起缓存(TerrainSystem.DryCounts，格子改了当场作废)，城按 CityDryYears 年缓存(领土地块数变了两成以上提前重算)；
//   · 生活方式(按"政权"：帝国核心连同它直辖的成员；间接统治的成员、定居帝国里的游牧成员自成一体)：
//     游牧(游牧政体，或原始政体的游牧文化)适应旱地；本族故地多在旱地的定居文化(游牧文化改行定居、制度线是 Arabic，
//     或以它为主流的城里旱地城过半)两种地都适应；其余定居政权只适应农地。原始政体的定居文化(部落)、近代政体、羁縻地区不论水土；
//   · 游牧离草(政权级)：直辖城(羁縻、附庸不算，口径同 AdministrativeControlSystem.IsIndirect)里旱地城不到 SteppeShare(六成)起算，
//     回到六成以上、或改了政体(不再是游牧)就清零；
//   · 定居入草(按城)：只适应农地的政权直辖的每座旱地城，记下占有起始时间与政权(易主按新主人重新计时)；
//   · 分档：满 FirstTierYears(20)年第一档，之后每满 TierStepYears(10)年加一档，最多 MaxTier(3)档。
//     旧档读进来没有这些时间的从第一次结算起算，不会一读档就到档；
//   · 每个政权每年结算一次(分帧，每帧至多一个)：走档、离草结束记入史书；Player.log 诊断行 [水土](到档的每国每年一行，状态变了也记一行)；
//   · 效果见"效果"一节，出路见 ShuiTuSystem.Outlets。
public static partial class ShuiTuSystem
{
    public enum Fit { None, Nomad, Both, Farm }

    // 旱地城：领土陆地里旱地过半
    private const float DryCityShare = 0.5f;
    private const int CityDryYears = 2;
    // 游牧离草：直辖城里旱地城不到六成
    public const float SteppeShare = 0.6f;
    // 分档
    private const int FirstTierYears = 20, TierStepYears = 10;
    public const int MaxTier = 3;
    // 本族故地在旱地：制度线是 DryLine，或以该文化为主流的城里旱地城占 DryNativeShare 以上
    private const string DryLine = "Arabic";
    private const float DryNativeShare = 0.5f;
    // 一年约 60 秒世界时间(5 秒一个月)
    private const double SecondsPerYear = 60d;

    private sealed class UnitInfo
    {
        public Fit Fit;
        public int Direct, Dry;
        public bool OffGrass;
        public int OffYears, OffTier;
        // 不服水土的旱地城：座数、最久的年数、最高档、各档座数
        public int Unfit, UnfitYears, UnfitTop;
        public readonly int[] TierCounts = new int[MaxTier + 1];
        public double At = -1d;
        public string Logged = "";
        public List<long> CityIds = new(), KingdomIds = new();
    }

    private struct CityInfo
    {
        public long Kingdom, Unit;
        public bool Dry;
        public int Tier, Years;
        public string Culture;
    }

    private struct DryEntry
    {
        public double At;
        public int Zones;
        public float Share;
        public bool Dry;
    }

    private static readonly Dictionary<long, UnitInfo> Units = new();
    private static readonly Dictionary<long, long> KingdomUnits = new();
    private static readonly Dictionary<long, CityInfo> CityInfos = new();
    private static readonly Dictionary<long, DryEntry> DryCache = new();
    private static readonly Dictionary<string, bool> DryNativeCultures = new(StringComparer.Ordinal);
    private static double _culturesAt = -1d;
    private static readonly Queue<long> Pending = new();
    private static int _cursor;

    public static void ResetWorldState()
    {
        Units.Clear();
        KingdomUnits.Clear();
        CityInfos.Clear();
        DryCache.Clear();
        DryNativeCultures.Clear();
        _culturesAt = -1d;
        Pending.Clear();
        _cursor = 0;
    }

    private static bool Valid(Kingdom kingdom) => kingdom?.data != null && !kingdom.isRekt() && !kingdom.wild &&
                                                  !AncientWarfareCompatibility.Owns(kingdom);

    private static bool Valid(City city) => city?.data != null && !city.isRekt() && city.zones != null && Valid(city.kingdom) &&
                                            !AncientWarfareCompatibility.OwnsObject(city);

    private static int YearsSince(double at) =>
        at < 0d || World.world == null || World.world.getCurWorldTime() < at ? 0 : Date.getYearsSince(at);

    public static int TierOf(int years) =>
        years < FirstTierYears ? 0 : Math.Min(MaxTier, 1 + (years - FirstTierYears) / TierStepYears);

    #region 档位系数

    // 游牧离草：农地城的征兵比例按档往 BaseLevyShare(一般的一成)回落，第三档回落到底；
    //   在非旱地的地块上交战攻守都打折；农地城自然稳定度扣
    private const float BaseLevyShare = 0.1f;
    private static readonly float[] NomadCombat = { 1f, 0.93f, 0.86f, 0.80f };
    private static readonly float[] StabilityByTier = { 0f, 4f, 8f, 12f };
    // 定居入草：旱地城兵源、赋税按档打折，自然稳定度同上；在旱地地块上交战按不服水土的旱地城里最高的档打折
    private static readonly float[] SettledLevy = { 1f, 0.8f, 0.6f, 0.4f };
    private static readonly float[] SettledTax = { 1f, 0.9f, 0.8f, 0.7f };
    private static readonly float[] SettledCombat = { 1f, 0.92f, 0.85f, 0.78f };

    private static int Clamp(int tier) => Math.Max(0, Math.Min(MaxTier, tier));
    public static float StabilityFor(int tier) => StabilityByTier[Clamp(tier)];
    public static float NomadCombatFor(int tier) => NomadCombat[Clamp(tier)];
    public static float SettledLevyFor(int tier) => SettledLevy[Clamp(tier)];
    public static float SettledTaxFor(int tier) => SettledTax[Clamp(tier)];
    public static float SettledCombatFor(int tier) => SettledCombat[Clamp(tier)];

    // 离草游牧的农地城征兵打几折：军制的征兵比例(十户制 army_mobilization 一成五)按档往一成回落；本就不过一成的不折
    public static float NomadLevy(Kingdom kingdom, int tier)
    {
        if (tier <= 0 || kingdom == null) return 1f;
        float share = CityPopulationSystem.DoctrineOf(kingdom).LevyShare;
        return share > BaseLevyShare ? (share - (share - BaseLevyShare) * Clamp(tier) / MaxTier) / share : 1f;
    }

    #endregion

    #region 效果

    // 这座城眼下所属政权的结算(换了主人、新主人还没结算的不算)
    private static bool Lookup(City city, out CityInfo info, out UnitInfo unit)
    {
        info = default;
        unit = null;
        return city?.data != null && city.kingdom != null && CityInfos.TryGetValue(city.id, out info) &&
               info.Kingdom == city.kingdom.id && Units.TryGetValue(info.Unit, out unit);
    }

    private static UnitInfo UnitInfoOf(Kingdom kingdom) =>
        kingdom?.data != null && KingdomUnits.TryGetValue(kingdom.id, out long id) && Units.TryGetValue(id, out UnitInfo info)
            ? info : null;

    // 离草游牧治下的农地城：离草档位；别的城 0
    public static int OffGrassTier(City city) =>
        Lookup(city, out CityInfo info, out UnitInfo unit) && unit.Fit == Fit.Nomad && unit.OffGrass && !info.Dry ? unit.OffTier : 0;

    // 政权(按所属政权)的离草档位；不在离草 0
    public static int OffGrassTier(Kingdom kingdom)
    {
        UnitInfo unit = UnitInfoOf(kingdom);
        return unit?.Fit == Fit.Nomad && unit.OffGrass ? unit.OffTier : 0;
    }

    // 只宜农地的政权直辖的旱地城：不服水土的档位；别的城 0
    public static int UnfitTier(City city) =>
        Lookup(city, out CityInfo info, out UnitInfo unit) && unit.Fit == Fit.Farm && info.Dry ? info.Tier : 0;

    // 政权不服水土的旱地城里最高的档(旱地交战按它打折)
    public static int UnfitTop(Kingdom kingdom)
    {
        UnitInfo unit = UnitInfoOf(kingdom);
        return unit?.Fit == Fit.Farm ? unit.UnfitTop : 0;
    }

    // 兵源打几折(见 CorpsSystem.Mobilize 逐城累加兵源)：离草游牧的农地城征兵比例回落；不服水土的旱地城兵源打折
    public static float LevyFactor(City city)
    {
        int off = OffGrassTier(city);
        return off > 0 ? NomadLevy(city.kingdom, off) : SettledLevyFor(UnfitTier(city));
    }

    // 自然稳定度扣多少(见 CityStabilitySystem.LocalPressures)：离草游牧的农地城、不服水土的旱地城
    public static float StabilityLoss(City city) => StabilityFor(Math.Max(OffGrassTier(city), UnfitTier(city)));

    public static string DescribeStability(City city) => string.Format(LM.Get("shuitu_stability_detail"), StabilityLoss(city));

    // 赋税收几成(见 TreasurySystem.CollectResidentTax，与鞭长莫及的系数相乘)：不服水土的旱地城少收
    public static float TaxFactor(City city) => SettledTaxFor(UnfitTier(city));

    // 交战攻守打几折(见 FrontSystem.ResolveCity)：dryGround 是交战的那块地是不是旱地(TerrainSystem.IsDry)；
    // 离草的游牧在非旱地上打折，不服水土的定居政权在旱地上按它最高的档打折
    public static float CombatFactor(Kingdom kingdom, bool dryGround)
    {
        UnitInfo unit = UnitInfoOf(kingdom);
        if (unit == null) return 1f;
        if (unit.Fit == Fit.Nomad && unit.OffGrass && !dryGround) return NomadCombatFor(unit.OffTier);
        if (unit.Fit == Fit.Farm && dryGround) return SettledCombatFor(unit.UnfitTop);
        return 1f;
    }

    #endregion

    #region 地

    // 旱地城(按城缓存)
    public static bool IsDryCity(City city) => Dry(city).Dry;

    // 城领土陆地里旱地占几成(显示用)
    public static float DryShare(City city) => Dry(city).Share;

    private static DryEntry Dry(City city)
    {
        if (city?.data == null || city.zones == null || World.world == null) return default;
        double now = World.world.getCurWorldTime();
        int zones = city.zones.Count;
        if (DryCache.TryGetValue(city.id, out DryEntry hit) && now >= hit.At && Date.getYearsSince(hit.At) < CityDryYears &&
            Math.Abs(zones - hit.Zones) * 5 <= hit.Zones) return hit;
        int land = 0, dry = 0;
        foreach (TileZone zone in city.zones)
        {
            (int l, int d) = TerrainSystem.DryCounts(zone);
            land += l;
            dry += d;
        }
        float share = land > 0 ? dry / (float)land : 0f;
        var entry = new DryEntry { At = now, Zones = zones, Share = share, Dry = land > 0 && share > DryCityShare };
        DryCache[city.id] = entry;
        return entry;
    }

    #endregion

    #region 生活方式

    // 政权：帝国核心连同它直辖的成员；间接统治的成员(羁縻、兀鲁思、封建诸侯……)、定居帝国里的游牧成员自成一体
    public static Kingdom UnitOf(Kingdom kingdom)
    {
        if (!Valid(kingdom)) return null;
        Kingdom core = kingdom.GetEmpire()?.CoreKingdom;
        if (core?.data == null || core.isRekt() || core == kingdom) return kingdom;
        if (AdministrativeControlSystem.IsIndirect(kingdom) || kingdom.GetOrCreate().shuitu_nomadized_at >= 0d) return kingdom;
        if (kingdom.GetRegime()?.type == RegimeType.YouMu && core.GetRegime()?.type != RegimeType.YouMu) return kingdom;
        return core;
    }

    public static Fit FitOf(Kingdom unit)
    {
        if (!Valid(unit) || JimiSystem.IsJimiAdministration(unit)) return Fit.None;
        Empire empire = unit.GetEmpire();
        if (empire != null && empire.CoreKingdom != unit && JimiSystem.IsJimiRegion(unit, empire)) return Fit.None;
        RegimeType? regime = unit.GetRegime()?.type;
        if (regime == null || regime is RegimeType.Modern or RegimeType.Republic) return Fit.None;
        string culture = CultureService.GetRealmCulture(unit);
        if (regime == RegimeType.YouMu) return Fit.Nomad;
        if (RegimeManager.IsPrimitive(regime)) return CompositeEmpireService.IsNomadicCulture(culture) ? Fit.Nomad : Fit.None;
        return DryNative(culture) ? Fit.Both : Fit.Farm;
    }

    // 本族故地多在旱地的文化：游牧文化(改行定居的)、制度线是 Arabic 的，或以它为主流的城里旱地城过半
    public static bool DryNative(string culture)
    {
        if (!CultureService.IsValidCulture(culture)) return false;
        if (CompositeEmpireService.IsNomadicCulture(culture) || InstitutionSystem.GetCultureLine(culture) == DryLine) return true;
        return DryNativeCultures.TryGetValue(culture, out bool dry) && dry;
    }

    // 各文化为主流的城里旱地城占几成：按各政权结算记下的城汇总，至多一月一次
    private static void UpdateCultures()
    {
        double now = World.world.getCurWorldTime();
        if (_culturesAt >= 0d && now >= _culturesAt && Date.getMonthsSince(_culturesAt) < 1 || CityInfos.Count == 0) return;
        _culturesAt = now;
        var totals = new Dictionary<string, (int dry, int all)>(StringComparer.Ordinal);
        foreach (CityInfo city in CityInfos.Values)
        {
            if (string.IsNullOrEmpty(city.Culture)) continue;
            totals.TryGetValue(city.Culture, out var count);
            totals[city.Culture] = (count.dry + (city.Dry ? 1 : 0), count.all + 1);
        }
        DryNativeCultures.Clear();
        foreach (var pair in totals)
            DryNativeCultures[pair.Key] = pair.Value.all > 0 && pair.Value.dry >= pair.Value.all * DryNativeShare;
    }

    #endregion

    #region 结算

    // 每帧至多结算一个政权；同一政权一年一次
    public static void Tick()
    {
        if (World.world?.kingdoms == null || ModClass.IS_CLEAR || !Config.game_loaded || SmoothLoader.isLoading()) return;
        if (!SimulationFrameBudget.HasTime) return;
        if (Pending.Count == 0) Refill();
        if (Pending.Count == 0) return;
        Kingdom unit = World.world.kingdoms.get(Pending.Dequeue());
        if (!Valid(unit) || UnitOf(unit) != unit || !Due(unit)) return;
        try
        {
            using (FrameProfiler.Measure("水土")) Pass(unit);
        }
        catch (Exception error)
        {
            LogService.LogWarning($"[EmpireCraft][水土] {unit.id}: {error.Message}");
        }
    }

    private static bool Due(Kingdom unit) =>
        !Units.TryGetValue(unit.id, out UnitInfo info) || info.At < 0d || World.world.getCurWorldTime() < info.At ||
        Date.getYearsSince(info.At) >= 1;

    // 一次排进最多 16 个政权，轮着来
    private static void Refill()
    {
        UpdateCultures();
        var list = World.world.kingdoms.list;
        if (list == null || list.Count == 0) return;
        if (_cursor >= list.Count) _cursor = 0;
        for (int scanned = 0; scanned < list.Count && Pending.Count < 16; scanned++)
        {
            Kingdom kingdom = list[_cursor];
            _cursor = (_cursor + 1) % list.Count;
            if (Valid(kingdom) && kingdom.hasCities() && UnitOf(kingdom) == kingdom && Due(kingdom)) Pending.Enqueue(kingdom.id);
        }
    }

    // 政权直辖的城(连同记下直辖的成员国)
    private static List<City> DirectCities(Kingdom unit, List<Kingdom> kingdoms)
    {
        kingdoms.Add(unit);
        Empire empire = unit.GetEmpire();
        if (empire?.CoreKingdom == unit && empire.kingdoms_list != null)
            foreach (Kingdom member in empire.kingdoms_list)
                if (member != unit && Valid(member) && UnitOf(member) == unit) kingdoms.Add(member);
        var cities = new List<City>();
        foreach (Kingdom kingdom in kingdoms)
            if (kingdom.cities != null)
                cities.AddRange(kingdom.cities.Where(Valid));
        return cities;
    }

    private static void Pass(Kingdom unit)
    {
        double now = World.world.getCurWorldTime();
        var kingdoms = new List<Kingdom>();
        List<City> cities = DirectCities(unit, kingdoms);
        Fit fit = FitOf(unit);
        if (!Units.TryGetValue(unit.id, out UnitInfo info)) Units[unit.id] = info = new UnitInfo();
        // 上一趟记下、这一趟不在本政权的城与国除去(还记在本政权名下的)
        var cityIds = new HashSet<long>(cities.Select(city => city.id));
        foreach (long id in info.CityIds)
            if (!cityIds.Contains(id) && CityInfos.TryGetValue(id, out CityInfo stale) && stale.Unit == unit.id) CityInfos.Remove(id);
        var kingdomIds = new HashSet<long>(kingdoms.Select(kingdom => kingdom.id));
        foreach (long id in info.KingdomIds)
            if (!kingdomIds.Contains(id) && KingdomUnits.TryGetValue(id, out long owner) && owner == unit.id) KingdomUnits.Remove(id);
        info.CityIds = cityIds.ToList();
        info.KingdomIds = kingdomIds.ToList();
        foreach (long id in kingdomIds) KingdomUnits[id] = unit.id;

        info.Fit = fit;
        info.At = now;
        info.Direct = cities.Count;
        info.Dry = info.Unfit = info.UnfitYears = info.UnfitTop = 0;
        Array.Clear(info.TierCounts, 0, info.TierCounts.Length);
        foreach (City city in cities)
        {
            bool dry = IsDryCity(city);
            if (dry) info.Dry++;
            var data = city.GetOrCreate();
            int years = 0, tier = 0;
            if (fit == Fit.Farm && dry)
            {
                // 新占的、易主的(按新主人)、旧档没记的，从现在起算
                if (data.shuitu_owner != unit.id || data.shuitu_since < 0d || data.shuitu_since > now)
                {
                    data.shuitu_owner = unit.id;
                    data.shuitu_since = now;
                }
                years = YearsSince(data.shuitu_since);
                tier = TierOf(years);
                info.Unfit++;
                info.TierCounts[tier]++;
                info.UnfitYears = Math.Max(info.UnfitYears, years);
                info.UnfitTop = Math.Max(info.UnfitTop, tier);
            }
            else if (data.shuitu_owner >= 0L || data.shuitu_since >= 0d)
            {
                data.shuitu_owner = -1L;
                data.shuitu_since = -1d;
            }
            CityInfos[city.id] = new CityInfo
            {
                Kingdom = city.kingdom.id, Unit = unit.id, Dry = dry, Tier = tier, Years = years,
                Culture = CultureService.GetMainCulture(city, initialize: false)
            };
        }

        var extra = unit.GetOrCreate();
        info.OffGrass = false;
        info.OffYears = info.OffTier = 0;
        if (fit == Fit.Nomad && cities.Count > 0 && info.Dry < cities.Count * SteppeShare)
        {
            if (extra.shuitu_offgrass_since < 0d || extra.shuitu_offgrass_since > now) extra.shuitu_offgrass_since = now;
            info.OffGrass = true;
            info.OffYears = YearsSince(extra.shuitu_offgrass_since);
            info.OffTier = TierOf(info.OffYears);
        }
        else if (extra.shuitu_offgrass_since >= 0d)
            EndOffGrass(unit, extra, fit == Fit.Nomad);
        RecordTiers(unit, info, extra);
        Diagnose(unit, info);
        // 到档后按局势走出路(见 ShuiTuSystem.Outlets)
        TryOutlet(unit, info, kingdoms, cities);
    }

    // 离草结束(回到旱地，或改了政体)：到过档的记入史书
    private static void EndOffGrass(Kingdom unit, KingdomExtension.KingdomExtraData extra, bool steppe)
    {
        if (extra.shuitu_offgrass_tier > 0)
        {
            string text = steppe
                ? string.Format(LM.Get("shuitu_offgrass_end_steppe_history"), unit.GetKingdomName())
                : string.Format(LM.Get("shuitu_offgrass_end_regime_history"), unit.GetKingdomName(),
                    CompositeEmpireService.GetRegimeName(unit.GetRegime()?.type ?? RegimeType.Feudalism));
            EventRecorder.Record(unit, text);
            LogService.LogInfo($"[EmpireCraft][水土] {unit.GetKingdomName()} 离草结束({(steppe ? "回到旱地" : "改了政体")})，" +
                               $"离草 {YearsSince(extra.shuitu_offgrass_since)} 年、第 {extra.shuitu_offgrass_tier} 档");
        }
        extra.shuitu_offgrass_since = -1d;
        extra.shuitu_offgrass_tier = 0;
    }

    // 走档记入史书(只记加深)
    private static void RecordTiers(Kingdom unit, UnitInfo info, KingdomExtension.KingdomExtraData extra)
    {
        if (info.OffTier > extra.shuitu_offgrass_tier)
            EventRecorder.Record(unit, string.Format(LM.Get("shuitu_offgrass_tier_history"), unit.GetKingdomName(),
                info.OffYears, info.OffTier));
        extra.shuitu_offgrass_tier = info.OffTier;
        if (info.UnfitTop > extra.shuitu_unfit_tier)
            EventRecorder.Record(unit, string.Format(LM.Get("shuitu_unfit_tier_history"), unit.GetKingdomName(),
                info.UnfitYears, info.Unfit, info.UnfitTop));
        extra.shuitu_unfit_tier = info.UnfitTop;
    }

    #endregion

    #region 诊断

    // [水土]：到档的政权每年一行(每个政权一年只结算一次)，没到档的只在状态变了时记一行
    private static void Diagnose(Kingdom unit, UnitInfo info)
    {
        bool offGrass = info.Fit == Fit.Nomad && info.OffGrass;
        bool unfit = info.Fit == Fit.Farm && info.Unfit > 0;
        string state = $"{info.Fit}/{offGrass}/{info.OffTier}/{info.Unfit}/{info.UnfitTop}";
        bool changed = state != info.Logged;
        info.Logged = state;
        if (!offGrass && !unfit)
        {
            if (changed && info.Direct > 0 && (info.Fit is Fit.Nomad or Fit.Farm))
                LogService.LogInfo($"[EmpireCraft][水土] {unit.GetKingdomName()} {FitName(info.Fit)} 直辖{info.Direct}城 旱地{info.Dry}城，水土相宜");
            return;
        }
        if (!changed && info.OffTier == 0 && info.UnfitTop == 0) return;
        float share = info.Direct > 0 ? info.Dry / (float)info.Direct : 0f;
        if (offGrass)
            LogService.LogInfo($"[EmpireCraft][水土] {unit.GetKingdomName()} 游牧离草 直辖{info.Direct}城 旱地{info.Dry}城({share:P0}) " +
                               $"离草{info.OffYears}年 第{info.OffTier}档 农地城征兵×{NomadLevy(unit, info.OffTier):0.00} " +
                               $"稳定−{StabilityFor(info.OffTier):0} 非旱地交战×{NomadCombatFor(info.OffTier):0.00}");
        else
            LogService.LogInfo($"[EmpireCraft][水土] {unit.GetKingdomName()} 定居入草 直辖{info.Direct}城 不服水土旱地{info.Unfit}城" +
                               $"(未到档{info.TierCounts[0]}/一档{info.TierCounts[1]}/二档{info.TierCounts[2]}/三档{info.TierCounts[3]}) " +
                               $"最久{info.UnfitYears}年 最高第{info.UnfitTop}档 旱地城兵源×{SettledLevyFor(info.UnfitTop):0.0} " +
                               $"赋税×{SettledTaxFor(info.UnfitTop):0.0} 旱地交战×{SettledCombatFor(info.UnfitTop):0.00}");
    }

    private static string FitName(Fit fit) => fit switch
    {
        Fit.Nomad => "游牧",
        Fit.Both => "两宜",
        Fit.Farm => "定居",
        _ => "不论"
    };

    #endregion
}

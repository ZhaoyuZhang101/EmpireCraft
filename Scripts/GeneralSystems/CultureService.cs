using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

public static class CultureService
{
    public const float AssimilatedThreshold = CultureShareRules.AssimilatedThreshold;
    public const float TitleConversionThreshold = CultureShareRules.TitleConversionThreshold;
    private const float Epsilon = 0.0001f;

    public static IEnumerable<string> CultureKeys => OnomasticsRule.ALL_CULTURE_RULE.Keys.OrderBy(key => key);

    // 当前世界里实际存在的文化(至少有一个王国的法理文化是它)，不是配置文件里全部登记过的文化。
    // 制度排行榜这类"当前局内有意义"的列表要用这个，而不是 CultureKeys。
    public static IEnumerable<string> GetActiveCultureKeys()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (World.world?.kingdoms == null) return result;
        foreach (Kingdom kingdom in World.world.kingdoms)
        {
            if (kingdom?.data == null || kingdom.isRekt()) continue;
            string culture = GetRealmCulture(kingdom);
            if (IsValidCulture(culture)) result.Add(culture);
        }
        return result.OrderBy(key => key, StringComparer.Ordinal);
    }

    public static bool IsValidCulture(string culture)
    {
        return !string.IsNullOrWhiteSpace(culture) && OnomasticsRule.ALL_CULTURE_RULE.ContainsKey(culture);
    }

    public static bool IsAssimilationEnabled()
    {
        // 人口文化系统始终运行；“固定法理文化”只额外阻止法理与辖内城市的
        // 官方主流文化转换，不冻结人口交流及人物文化。
        return true;
    }

    public static bool IsFixedDeJureCultureEnabled()
    {
        return EmpireCraftWorldLawLibrary.empirecraft_law_fixed_de_jure_culture?.isEnabled() == true;
    }

    public static string GetActorCulture(Actor actor)
    {
        return actor == null ? "" : CulturePatch.GetInjectedCultureName(actor.GetCulture());
    }

    public static string GetFounderCulture(Actor founder, bool allowSpeciesBootstrap = true)
    {
        if (founder?.data == null || founder.isRekt()) return "";

        string culture = GetActorCulture(founder);
        if (!IsValidCulture(culture) && founder.city != null)
        {
            culture = GetMainCulture(founder.city, initialize: false);
        }
        if (!IsValidCulture(culture) && allowSpeciesBootstrap)
        {
            // A newly placed founder receives its configured species culture once,
            // before the base game has created a native Culture object for it.
            culture = OverallHelperFunc.GetCultureFromSpecies(founder.asset?.id);
        }
        return IsValidCulture(culture) ? culture : "";
    }

    // Recording a temporarily detached actor must not manufacture a new culture or
    // replace a known minority culture with the host city's majority.
    public static string ResolveRecordedActorCulture(Actor actor, string recordedCulture = "")
    {
        string current = GetActorCulture(actor);
        if (IsValidCulture(current)) return current;
        if (IsValidCulture(recordedCulture)) return recordedCulture;
        return GetFounderCulture(actor);
    }

    public static string GetRealmCulture(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return "";
        KingdomExtension.KingdomExtraData data = kingdom.GetOrCreate();
        if (IsValidCulture(data.realm_culture)) return data.realm_culture;

        // 旧档缺少 realm_culture 时先恢复国家已有的绑定，不能让外文化的新君主改写国家身份。
        string culture = CulturePatch.GetInjectedCultureName(kingdom.culture);
        if (!IsValidCulture(culture)) culture = GetActorCulture(kingdom.king);
        if (!IsValidCulture(culture) && kingdom.capital != null)
        {
            culture = GetMainCulture(kingdom.capital, initialize: false);
        }
        if (!IsValidCulture(culture))
        {
            // Old saves only knew a species-to-template association. This is migration-only.
            culture = OverallHelperFunc.GetCultureFromSpecies(kingdom.getSpecies());
        }
        if (IsValidCulture(culture)) data.realm_culture = culture;
        return culture;
    }

    // 制度文化：政体、制度效果、制度特性按它算。改从文明等级更低的文化后旧制犹存的，是旧制所属的文化
    // (见 LegacyInstitutionSystem)；其余就是国家文化。风俗、命名、宗教、科技照旧按国家文化(GetRealmCulture)
    public static string GetInstitutionCulture(Kingdom kingdom)
    {
        string realm = GetRealmCulture(kingdom);
        if (kingdom?.data == null || kingdom.isRekt()) return realm;
        string legacy = kingdom.GetOrCreate().institution_culture;
        return !string.IsNullOrEmpty(legacy) && IsValidCulture(legacy) ? legacy : realm;
    }

    // 政体按哪个文化定：帝国核心按帝国的制度文化(见 InstitutionSystem.GetPrimaryCulture)——核心王国自己的文化可能与帝国不同
    // (外族成员接任中央、旧档)，以前每月按核心自己的文化改政体、每年又按帝国的改回，帝国的政体来回跳(用户 2026-10-27："一个汉族世袭
    // 传的好好的国家……突然因为一个皇帝的文化转了政体")；别的国按本国的制度文化
    public static string GetRegimeCulture(Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        if (empire?.data != null && !empire.isRekt() && !empire.IsArchived() && empire.CoreKingdom == kingdom)
        {
            string imperial = InstitutionSystem.GetPrimaryCulture(empire);
            if (IsValidCulture(imperial)) return imperial;
        }
        return GetInstitutionCulture(kingdom);
    }

    // 君主改从本国之俗(用户 2026-10-27："为什么皇帝的个人文化会直接改变政体，这不合理，应该根据国家文化反向影响皇帝个人")：
    // 外族出身的君主(联姻、入继、外姓议立、受封……)即位，他本人改从本国(征服王朝的帝国核心看统治文化)之俗——只改本人的文化、
    // 名字的写法，不改姓、不动宗族别的人；国家的文化、政体不随君主个人变(见 Empire.CreateNewEmpire)
    public static void AssimilateRuler(Kingdom kingdom, Actor ruler)
    {
        if (kingdom?.data == null || kingdom.isRekt() || ruler?.data == null || ruler.isRekt() ||
            EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(kingdom)) return;
        Empire empire = kingdom.GetEmpire();
        string culture = empire != null && !empire.isRekt() && empire.CoreKingdom == kingdom && CompositeEmpireService.IsComposite(empire)
            ? CompositeEmpireService.GetRulingCulture(empire)
            : GetRealmCulture(kingdom);
        string own = GetActorCulture(ruler);
        if (!IsValidCulture(culture) || string.Equals(own, culture, StringComparison.Ordinal)) return;
        Culture native = GetNativeCultureObject(culture);
        if (native != null && ruler.culture != native) ruler.setCulture(native);
        var identity = ruler.GetPersonalIdentity();
        if (identity != null) identity.culture = culture;
        ruler.GetModName()?.SetName(ruler);
        LogService.LogInfo($"[EmpireCraft][君主从俗] {kingdom.GetKingdomName()} 新君 {ruler.getName()} {own} → {culture}");
    }

    // allowLegacyInstitutions：换成文明等级更低的文化时旧制犹存(见 LegacyInstitutionSystem)，政体不按新文化改；
    // 叛乱自立、征服王朝、神力改俗、归化改制传 false(不保留，原有的旧制也一并作罢)。立国不满一年的新国
    // (分封、水土胡化分出来的游牧之国……)由 LegacyInstitutionSystem 自己排除。
    // previousInstitutionCulture：调用方在改动帝国文化之前先取下的制度文化(易俗改制先改了帝国的文化再改国家)；空则在这里取。
    // activeReform：主动易俗——"仍沿旧制"由易俗收尾时记进世界日志(见 LegacyInstitutionSystem.AnnounceReform)；
    // 其余(法理同化、原版改文化、称帝改从君主之俗)只记本帝国史书与诊断
    public static bool SetRealmCulture(Kingdom kingdom, string culture, bool updatePoliticalSystem = false,
        bool allowLegacyInstitutions = true, string previousInstitutionCulture = null, bool activeReform = false)
    {
        if (kingdom?.data == null || kingdom.isRekt() || !IsValidCulture(culture)) return false;
        KingdomExtension.KingdomExtraData data = kingdom.GetOrCreate();
        bool changed = !string.Equals(data.realm_culture, culture, StringComparison.Ordinal);
        if (changed && allowLegacyInstitutions && previousInstitutionCulture == null)
            previousInstitutionCulture = LegacyInstitutionSystem.CurrentInstitutionCulture(kingdom);
        data.realm_culture = culture;
        bool keepLegacy = false;
        if (changed)
        {
            // 主动易俗后不再沿用旧文化的水土改制来源。
            data.shuitu_regime = -1;
            Empire empire = kingdom.GetEmpire();
            if (empire?.CoreKingdom == kingdom)
                ConstitutionalEconomySystem.ResetCultureStability(empire);
        }
        if (!allowLegacyInstitutions) LegacyInstitutionSystem.Drop(kingdom, culture);
        // 主动易俗时国家文化可能已随法理先改过了(见 CultureReformSystem.Apply)，照样定夺
        else if (changed || activeReform)
            keepLegacy = LegacyInstitutionSystem.OnRealmCultureChanged(kingdom, previousInstitutionCulture, culture, activeReform);
        bool canCreateNativeCulture = kingdom.king?.city?.data != null;
        Culture nativeCulture = ResolveNativeCulture(culture, kingdom.king, canCreateNativeCulture);
        if (nativeCulture != null)
        {
            CulturePatch.SyncCultureDisplayName(nativeCulture, culture);
            if (kingdom.culture != nativeCulture) kingdom.setCulture(nativeCulture);
        }
        if (updatePoliticalSystem && !keepLegacy) ApplyCulturePoliticalSystem(kingdom, culture);
        return changed;
    }

    // 叛乱自立的新国取当地主文化(用户 2026-10-19 选"可和平同化、留少数族群"；以前取领头人个人的文化——异族守将、
    // 移民据城自立，整个新国就成了异族)；分封、分治、建国等别的立国照旧取立国者的文化。新立之国没有旧制可沿
    public static void ApplyLocalCulture(Kingdom kingdom, City seat)
    {
        if (kingdom?.data == null || kingdom.isRekt() || seat?.data == null || seat.isRekt()) return;
        string local = GetMainCulture(seat, initialize: false);
        if (!IsValidCulture(local) || string.Equals(local, GetRealmCulture(kingdom), StringComparison.Ordinal)) return;
        SetRealmCulture(kingdom, local, updatePoliticalSystem: true, allowLegacyInstitutions: false);
    }

    // 固定法理文化开着时(或立国之地的法理锁定了文化)，新国取立国之地(都城，没有都城取立国者所在的城)的官方文化，
    // 不取立国者本人的——以前异族守将、外来宗室受封、外族新君称帝，整个国家连同它的帝国法理就改成了外族文化
    // 正在自成一国的城(City.makeOwnKingdom 期间，见 CulturePatch)
    internal static City PendingSeat;

    // 立国：新立之国(立国不满一年)没有旧制可沿(见 LegacyInstitutionSystem)。已有的国家称帝不再走这里(以前改从君主的文化，
    // 用户 2026-10-27 定国家文化反过来同化君主，见 Empire.CreateNewEmpire、AssimilateRuler)。
    // preferDeJure：新立的国家(KingdomPatch.NewCivKingdom)按立国之城的法理文化定文化与政体，没有法理才看立国者本人
    // (玩家反馈"新建立的国家有些会不根据本地法理建议政体"，用户 2026-10-21 选"按立国之城的法理文化"：以前一律按立国者，
    // 外族官员在别的文化的法理上自立，政体跟着他本人走)；称帝不走这条，征服王朝照旧保留征服者的文化(见 CompositeEmpireService)
    public static void ApplyFounderCulture(Kingdom kingdom, Actor founder, bool preferDeJure = false)
    {
        if (kingdom?.data == null || founder == null) return;
        string culture = GetFounderCulture(founder);
        // 立国之地：分封、分治、自立都是"某城自成一国"(City.makeOwnKingdom)，这时新国还没有城，立国者常住在京城，
        // 要取这座城本身(见 CulturePatch.BeforeMakeOwnKingdom)
        City seat = PendingSeat ?? kingdom.capital ?? founder.city;
        if (seat?.data != null && !seat.isRekt() && IsCityMainCultureLocked(seat))
        {
            string local = GetMainCulture(seat, initialize: false);
            if (IsValidCulture(local)) culture = local;
        }
        else if (preferDeJure && seat?.data != null && !seat.isRekt())
        {
            string deJure = GetEffectiveTitleCulture(seat.GetTitle());
            if (IsValidCulture(deJure)) culture = deJure;
        }
        if (!IsValidCulture(culture)) return;
        SetRealmCulture(kingdom, culture, updatePoliticalSystem: true);
    }

    private static bool IsRegimeAvailable(RegimeType regimeType)
    {
        return RegimeManager.regimes != null &&
               RegimeManager.regimes.TryGetValue(regimeType, out Regime regime) &&
               regime != null;
    }

    private static void RepairUnavailableSavedRegime(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return;
        KingdomExtension.KingdomExtraData data = kingdom.GetOrCreate();
        if (IsRegimeAvailable(data.regimeType)) return;

        RegimeType fallback = kingdom.GetRegime()?.type ?? RegimeType.Feudalism;
        if (!IsRegimeAvailable(fallback)) fallback = RegimeType.Feudalism;
        if (!IsRegimeAvailable(fallback)) return;
        kingdom.SetRegimeType(fallback);
        kingdom.LoadRegime();
    }

    // 修复默认值污染的政体(读档迁移、新建国家收尾、国家例行检查身份时调用)：
    //   · 阿拉伯：历史上的阿拉伯默认值污染；
    //   · 律令：政体类型的默认值(枚举首项)就是律令，没装过政体的国家(立国时文化还没定下、或别处先调了 GetRegime)一取政体
    //     就按默认装成律令制；而政体迁移遇到原始政体的目标一律跳过(见 InstitutionSystem.TryAdvanceRegimeTransition)，
    //     开局各文化还在原始政体，这些国家就一直是律令制(玩家 2026-10-22 反馈"全图律令制")。
    // 正常改革、手动指定、共和锁定、复合帝国的区域制度、水土改制另有规则，都不动
    public static void RepairDefaultRegime(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt() ||
            EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(kingdom)) return;
        KingdomExtension.KingdomExtraData data = kingdom.GetOrCreate();
        if (data.regimeType != RegimeType.Arabic && data.regimeType != RegimeType.LvLing) return;
        if (data.regime_manually_selected || RepublicSystem.IsRegimeLocked(kingdom) ||
            CompositeEmpireService.IsComposite(kingdom.GetEmpire())) return;
        // 旧制犹存的按旧制所属文化看(旧制本身的政体不算污染)；帝国核心按帝国的制度文化(见 GetRegimeCulture)
        string culture = GetRegimeCulture(kingdom);
        if (!IsValidCulture(culture)) return;
        RegimeType polluted = data.regimeType;
        if (polluted == RegimeType.LvLing ? !DefaultLvLing(kingdom, data, culture) : !DefaultArabic(culture)) return;
        if (!ApplyCulturePoliticalSystem(kingdom, culture) || data.regimeType == polluted) return;
        EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(kingdom);
        Empire empire = kingdom.GetEmpire();
        if (empire?.CoreKingdom == kingdom && empire.data?.centerOffice != null)
            empire.data.centerOffice.Init(kingdom);
        LogService.LogInfo($"[EmpireCraft] 修复国家 {kingdom.id} 的默认值政体: {polluted} -> {data.regimeType}，绑定文化 {culture}");
    }

    private static bool LineDeclares(string culture, RegimeType regime)
    {
        string line = culture.GetInstitutionLine();
        return InstitutionDefinitionRegistry.HasLine(line) &&
               InstitutionDefinitionRegistry.GetForLine(line).Any(node =>
                   InstitutionDefinitionRegistry.TryGetNodeRegime(node, out RegimeType type) && type == regime);
    }

    private static bool DefaultArabic(string culture) =>
        !LineDeclares(culture, RegimeType.Arabic) &&
        !(OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting) && setting.regime == RegimeType.Arabic);

    // 律令是默认值污染：按本文化(连同所在帝国)此刻该是律令的不算——帝国成员随核心(AdjustForMonarchyEmpire)、本文化制度树已走到律令；
    // 余下的，本文化制度树里根本没有律令，或本文化此刻还在原始政体(开局；周制改郡县这类正当的律令都在原始政体之后)，才算污染。
    // 水土出路改过政体的不动
    private static bool DefaultLvLing(Kingdom kingdom, KingdomExtension.KingdomExtraData data, string culture)
    {
        if (data.shuitu_regime >= 0) return false;
        RegimeType expected = InstitutionSystem.TryResolveCultureRegime(culture, out RegimeType resolved)
            ? resolved
            : OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting) ? setting.regime : RegimeType.Feudalism;
        expected = InstitutionSystem.AdjustForMonarchyEmpire(kingdom, expected);
        if (expected == RegimeType.LvLing) return false;
        return !LineDeclares(culture, RegimeType.LvLing) || RegimeManager.IsPrimitive(expected);
    }

    public static bool ApplyCulturePoliticalSystem(Kingdom kingdom, string culture)
    {
        if (kingdom?.data == null || kingdom.isRekt() || !IsValidCulture(culture)) return false;
        if (RepublicSystem.IsRegimeLocked(kingdom)) return false;
        // 政体形态由该文化已掌握的制度反推（一级制度 = 初始政体），配置里的 setting.regime
        // 退居兜底，语义也随之变成"这个文化的政体归宿"而不是"它现在是什么政体"。
        RegimeType regimeType = InstitutionSystem.TryResolveCultureRegime(culture, out RegimeType resolvedRegime)
            ? resolvedRegime
            : OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting)
                ? setting.regime
                : RegimeType.Feudalism;
        regimeType = InstitutionSystem.AdjustForMonarchyEmpire(kingdom, regimeType);
        if (!IsRegimeAvailable(regimeType)) return false;
        if (kingdom.GetRegime()?.type == regimeType) return true;
        kingdom.SetRegimeType(regimeType);
        kingdom.LoadRegimeKeepingRole();
        return kingdom.GetRegime()?.type == regimeType;
    }

    /// <summary>
    /// 政体转变（例如"游牧"转为帝国官方文化对应的政体）目前只会在建国时由开国者的文化
    /// 决定一次（见 ApplyFounderCulture/ApplyCulturePoliticalSystem），之后不会随着
    /// "文化同化"决议自动更新——那条决议只会推高具体城市的文化占比，
    /// 不会触碰国家的 realm_culture 或政体类型。
    /// 这里补上后续的手动转变路径：检查某个（非帝国本身的）成员国是否已经被
    /// 充分同化，满足条件后由该国国王自行发起决议正式"归化"政体。纯检查，不修改任何状态。
    /// 条件与法理文化转化（FindTitleCultureConversionCandidate）保持一致：
    /// 王城主流文化已经是帝国官方文化，且至少 2/3 的城市也已经是帝国官方文化。
    /// </summary>
    public static bool FindRegimeConversionCandidate(Kingdom kingdom, out string targetCulture, out RegimeType targetRegimeType)
    {
        targetCulture = null;
        targetRegimeType = default;
        if (kingdom?.data == null || kingdom.isRekt() || kingdom.IsEmpire() || !IsAssimilationEnabled()) return false;

        Empire empire = kingdom.GetEmpire();
        if (empire == null) return false;
        string culture = GetEmpireDefaultCulture(empire);
        if (!IsValidCulture(culture)) return false;
        RegimeType regimeType = InstitutionSystem.TryResolveCultureRegime(culture, out RegimeType resolvedRegime)
            ? resolvedRegime
            : OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting)
                ? setting.regime
                : RegimeType.Feudalism;
        regimeType = InstitutionSystem.AdjustForMonarchyEmpire(kingdom, regimeType);
        // 配置里可能保留尚未实现的政体枚举。没有实际注册配置的政体不能生成归化谋划，
        // 否则 LoadRegime 会回退成封建制，而谋划又会在下一轮继续尝试，造成无限刷屏。
        if (!IsRegimeAvailable(regimeType)) return false;
        if (kingdom.GetRegime()?.type == regimeType) return false;
        // 帝国的文化不及本国现用的制度：改从其俗也是旧制犹存，不归化(见 LegacyInstitutionSystem)——
        // 否则谋划成了却改不过政体，下一轮又来
        if (LegacyInstitutionSystem.WouldKeep(kingdom, GetInstitutionCulture(kingdom), culture)) return false;

        List<City> cities = (kingdom.cities ?? new List<City>()).Where(city => city != null && !city.isRekt()).ToList();
        if (cities.Count == 0) return false;

        City capital = kingdom.king?.city;
        if (capital == null || capital.isRekt() || !cities.Contains(capital)) return false;
        if (!string.Equals(GetMainCulture(capital), culture, StringComparison.Ordinal)) return false;

        int requiredCities = (int)Math.Ceiling(cities.Count * CultureShareRules.TitleCultureDominanceRatio);
        int dominantCount = cities.Count(city => string.Equals(GetMainCulture(city), culture, StringComparison.Ordinal));
        if (dominantCount < requiredCities) return false;

        targetCulture = culture;
        targetRegimeType = regimeType;
        return true;
    }

    /// <summary>
    /// 由该国国王决议执行：把国家的 realm_culture 与政体正式转变为目标文化/政体。
    /// </summary>
    public static bool ConvertKingdomRegime(Kingdom kingdom, string targetCulture, RegimeType targetRegimeType)
    {
        if (kingdom?.data == null || kingdom.isRekt() || !IsValidCulture(targetCulture) ||
            !IsRegimeAvailable(targetRegimeType)) return false;
        RegimeType? previousRegimeType = kingdom.GetRegime()?.type;
        if (previousRegimeType == targetRegimeType) return false;
        // 归化是本国主动改制：一步改行新制，原有的旧制作罢
        SetRealmCulture(kingdom, targetCulture, updatePoliticalSystem: true, allowLegacyInstitutions: false);
        // 只有实际加载出的政体与目标一致才算成功；严禁把回退政体记录成成功转制。
        if (kingdom.GetRegime()?.type != targetRegimeType) return false;
        TranslateHelper.LogKingdomRegimeConversion(kingdom, previousRegimeType, targetRegimeType);
        return true;
    }

    public static string GetEmpireDefaultCulture(Empire empire)
    {
        if (empire == null || empire.IsArchived() || empire.isRekt()) return "";
        EmpireCore core = EmpireCoreManager.Get(empire);
        if (core != null && IsValidCulture(core.default_culture)) return core.default_culture;
        string culture = GetRealmCulture(empire.CoreKingdom);
        if (core != null && IsValidCulture(culture)) core.default_culture = culture;
        return culture;
    }

    public static Empire GetActiveEmpireForCulture(string culture, Empire excludedEmpire = null)
    {
        if (!IsValidCulture(culture) || ModClass.EMPIRE_MANAGER == null) return null;
        foreach (Empire empire in ModClass.EMPIRE_MANAGER)
        {
            if (empire == null || empire == excludedEmpire || empire.IsArchived() || empire.isRekt() ||
                empire.CoreKingdom == null || empire.CoreKingdom.isRekt() ||
                EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(empire.CoreKingdom))
                continue;
            if (string.Equals(GetEmpireDefaultCulture(empire), culture, StringComparison.Ordinal))
                return empire;
        }
        return null;
    }

    public static bool HasActiveEmpireForCulture(string culture, Empire excludedEmpire = null)
    {
        return GetActiveEmpireForCulture(culture, excludedEmpire) != null;
    }

    // 同一族类：文化相同，或同属一个天下标识(华夏、中国、山海……见 CultureRule 的 tianxia)——"异族统治""外族入侵"
    // "夷狄""羁縻""清除移民"都按它判(复查 2026-10-17：以前一字不差地比文化，华夏统治中国文化的城也算异族统治)
    public static bool SameKin(string a, string b) =>
        string.Equals(a, b, StringComparison.Ordinal) ||
        IsValidCulture(a) && IsValidCulture(b) && string.Equals(a.GetTianxiaId(), b.GetTianxiaId(), StringComparison.Ordinal);

    public static bool SetEmpireDefaultCulture(EmpireCore core, string culture, bool locked)
    {
        if (core == null || !IsValidCulture(culture)) return false;
        Empire currentEmpire = EmpireCoreManager.GetEmpires(core).FirstOrDefault();
        Empire otherEmpire = GetActiveEmpireForCulture(culture, currentEmpire);
        if (otherEmpire != null &&
            !ImperialLegitimacyChallengeService.AreRecognizedRivals(currentEmpire, otherEmpire)) return false;
        core.default_culture = culture;
        ApplyEmpireCultureLock(core, locked);
        return true;
    }

    /// <summary>
    /// 单独设置帝国的文化锁定状态（不改变文化本身）。帝国一旦锁定文化，
    /// 其下所有王国法理也会跟着锁定，避免"帝国已固定文化，王国法理还能被同化悄悄改变"的不一致状态。
    /// 解锁帝国不会自动解锁各王国法理——那些需要在各自的法理窗口里单独解锁。
    /// </summary>
    public static bool SetEmpireCultureLock(EmpireCore core, bool locked)
    {
        if (core == null) return false;
        ApplyEmpireCultureLock(core, locked);
        return true;
    }

    private static void ApplyEmpireCultureLock(EmpireCore core, bool locked)
    {
        core.culture_locked = locked;
        foreach (KingdomTitle title in EmpireCoreManager.GetTitles(core))
        {
            if (locked && title?.data != null) title.data.culture_locked = true;
            RefreshCultureDependents(title);
            RefreshTitleCityCultureLockState(title);
        }
    }

    public static EmpireCore GetInheritedEmpireCore(KingdomTitle title)
    {
        if (title == null || title.isRekt()) return null;
        return EmpireCoreManager.EmpireCores.Values.FirstOrDefault(core =>
            core != null && EmpireCoreManager.ContainsTitle(core, title));
    }

    public static bool IsInheritingEmpireCulture(KingdomTitle title)
    {
        return title?.data != null && !title.data.culture_explicit && GetInheritedEmpireCore(title) != null;
    }

    // 固定法理文化开着时，法理并入、移出帝国核心前先把眼下的文化定下来——没定过文化的法理随所在核心的默认文化走，
    // 以前外族称帝、并入别的帝国、帝国覆灭，名下法理的文化就跟着换了
    public static void PinTitleCultureIfFixed(KingdomTitle title)
    {
        if (!IsFixedDeJureCultureEnabled() || title?.data == null || title.isRekt() || title.data.culture_explicit) return;
        string current = GetEffectiveTitleCulture(title);
        if (!IsValidCulture(current)) return;
        title.data.culture = current;
        title.data.culture_explicit = true;
    }

    public static string GetEffectiveTitleCulture(KingdomTitle title)
    {
        if (title?.data == null || title.isRekt()) return "";
        if (title.data.culture_explicit && IsValidCulture(title.data.culture)) return title.data.culture;

        EmpireCore core = GetInheritedEmpireCore(title);
        if (core != null)
        {
            if (!IsValidCulture(core.default_culture))
            {
                Empire empire = EmpireCoreManager.GetEmpires(core).FirstOrDefault();
                core.default_culture = GetRealmCulture(empire?.CoreKingdom ?? title.main_kingdom);
            }
            if (IsValidCulture(core.default_culture)) return core.default_culture;
        }

        if (IsValidCulture(title.data.culture)) return title.data.culture;
        string culture = GetRealmCulture(title.main_kingdom ?? title.title_capital?.kingdom);
        if (!IsValidCulture(culture)) culture = GetActorCulture(title.owner);
        if (IsValidCulture(culture)) title.data.culture = culture;
        return culture;
    }

    public static void InitializeTitleCulture(KingdomTitle title)
    {
        if (title?.data == null) return;
        if (!IsValidCulture(title.data.culture))
        {
            Actor founder = title.data.founder_actor_id > 0
                ? World.world?.units?.get(title.data.founder_actor_id)
                : null;
            title.data.culture = GetActorCulture(founder);
            if (!IsValidCulture(title.data.culture))
                title.data.culture = GetRealmCulture(title.title_capital?.kingdom);
        }
        RememberInitialTitleCulture(title);
        foreach (City city in title.getCities()) InitializeCityCulture(city);
    }

    // 法理的初始文化(用户 2026-10-19："一键恢复法理的初始文化，法理里面能够变更初始文化")：法理头衔第一次有文化时记下；
    // 改文化前没记过的先记下改之前的；旧存档读档时以当时的文化补上(见 DataManager)
    public static void RememberInitialTitleCulture(KingdomTitle title)
    {
        if (title?.data == null || IsValidCulture(title.data.initial_culture)) return;
        // 记眼下显示的文化(随帝国默认文化走的法理，记的是帝国的文化，不是存着没用的那一份)
        string current = GetEffectiveTitleCulture(title);
        if (!IsValidCulture(current)) current = title.data.culture;
        if (IsValidCulture(current)) title.data.initial_culture = current;
    }

    public static string GetInitialTitleCulture(KingdomTitle title)
    {
        RememberInitialTitleCulture(title);
        return title?.data?.initial_culture ?? "";
    }

    public static bool SetInitialTitleCulture(KingdomTitle title, string culture)
    {
        if (title?.data == null || !IsValidCulture(culture)) return false;
        title.data.initial_culture = culture;
        return true;
    }

    // 恢复初始文化(锁定的也恢复)；已经是初始文化的返回 false
    public static bool RestoreInitialTitleCulture(KingdomTitle title)
    {
        string initial = GetInitialTitleCulture(title);
        if (!IsValidCulture(initial)) return false;
        // 眼下显示的已是初始文化：不算恢复，也不把随帝国走的法理定死
        if (string.Equals(GetEffectiveTitleCulture(title), initial, StringComparison.Ordinal)) return false;
        if (ConvertTitleCulture(title, initial)) return true;
        title.data.culture = initial;
        title.data.culture_explicit = true;
        RefreshCultureDependents(title);
        return true;
    }

    public static bool SetTitleCulture(KingdomTitle title, string culture, bool explicitCulture, bool locked)
    {
        if (title?.data == null || !IsValidCulture(culture)) return false;
        RememberInitialTitleCulture(title);
        title.data.culture = culture;
        title.data.culture_explicit = explicitCulture;
        title.data.culture_locked = locked;
        RefreshCultureDependents(title);
        RefreshTitleCityCultureLockState(title);
        return true;
    }

    public static void SetTitleCultureLock(KingdomTitle title, bool locked)
    {
        if (title?.data == null) return;
        title.data.culture_locked = locked;
        RefreshCultureDependents(title);
        RefreshTitleCityCultureLockState(title);
    }

    private static void RefreshTitleCityCultureLockState(KingdomTitle title)
    {
        if (title?.data == null || title.isRekt()) return;
        HashSet<Kingdom> administrations = new HashSet<Kingdom>();
        foreach (City city in title.getCities())
        {
            if (city?.data == null || city.isRekt()) continue;
            UpdateCityCultureShiftCandidate(city);
            if (city.kingdom != null) administrations.Add(city.kingdom);
        }
        foreach (Kingdom administration in administrations)
            UpdateCulturalAssimilationDuty(administration);
    }

    public static Dictionary<string, float> GetCityCultureShares(City city, bool initialize = true)
    {
        if (city == null) return new Dictionary<string, float>();
        CityExtension.CityExtraData data = city.GetOrCreate();
        data.culture_shares ??= new Dictionary<string, float>();
        if (initialize)
        {
            if (!HasValidShares(data.culture_shares)) InitializeCityCulture(city);
            ApplyPopulationPressure(city);
        }
        NormalizeShares(data.culture_shares);
        return data.culture_shares;
    }

    /// <summary>
    /// 居民构成每年只会把城市文化影响力向人口构成推动少量百分点。官方文化代表制度、教育与
    /// 公共生活，因此和平状态下始终保留最低影响力，不会因为一次人口迁徙被直接替换。
    /// </summary>
    public static bool ApplyPopulationPressure(City city, bool force = false)
    {
        if (city?.data == null || city.isRekt() || city.units == null) return false;
        CityExtension.CityExtraData data = city.GetOrCreate();
        data.culture_shares ??= new Dictionary<string, float>();
        if (!HasValidShares(data.culture_shares)) return false;

        double now = World.world?.getCurWorldTime() ?? -1d;
        if (!force && data.last_culture_share_sync_timestamp >= 0d && now >= 0d &&
            Date.getYearsSince(data.last_culture_share_sync_timestamp) < 1)
        {
            return false;
        }

        Dictionary<string, int> counts = new Dictionary<string, int>();
        int recognizedPopulation = 0;
        for (int i = 0; i < city.units.Count; i++)
        {
            Actor actor = city.units[i];
            if (actor == null || actor.isRekt() || !actor.isAlive()) continue;
            string culture = GetActorCulture(actor);
            if (!IsValidCulture(culture)) continue;
            counts[culture] = counts.TryGetValue(culture, out int count) ? count + 1 : 1;
            recognizedPopulation++;
        }
        // 无小人模式：背景人口的文化构成同样计入
        var backgroundCultures = new Dictionary<string, int>();
        CityPopulationSystem.AddBackgroundCounts(city, backgroundCultures, group => group.culture ?? "",
            group => IsValidCulture(group.culture));
        foreach (KeyValuePair<string, int> pair in backgroundCultures)
        {
            counts[pair.Key] = counts.TryGetValue(pair.Key, out int count) ? count + pair.Value : pair.Value;
            recognizedPopulation += pair.Value;
        }

        data.last_culture_share_sync_timestamp = now;
        if (recognizedPopulation <= 0) return false;

        Dictionary<string, float> targetShares = new Dictionary<string, float>();
        foreach (KeyValuePair<string, int> pair in counts)
            targetShares[pair.Key] = pair.Value * 100f / recognizedPopulation;
        // 教化(见 CultureRadiation)：目标三成看四方各文化教化到本城的比例，七成看本城人口——
        // 腹地本文化教化最盛，边地、山后、长城外受邻近文化浸染
        IReadOnlyDictionary<string, float> radiance = CultureRadiation.SharesOf(city);
        if (radiance != null && radiance.Count > 0)
        {
            foreach (string key in targetShares.Keys.ToList()) targetShares[key] *= 1f - CultureRadiation.PressureWeight;
            foreach (KeyValuePair<string, float> pair in radiance)
            {
                if (!IsValidCulture(pair.Key)) continue;
                targetShares.TryGetValue(pair.Key, out float value);
                targetShares[pair.Key] = value + pair.Value * 100f * CultureRadiation.PressureWeight;
            }
        }

        string officialCulture = data.main_culture;
        if (!IsValidCulture(officialCulture))
        {
            officialCulture = GetEffectiveTitleCulture(city.GetTitle());
            if (!IsValidCulture(officialCulture)) officialCulture = GetRealmCulture(city.kingdom);
            if (!IsValidCulture(officialCulture))
                officialCulture = data.culture_shares.OrderByDescending(pair => pair.Value).First().Key;
            if (IsValidCulture(officialCulture)) data.main_culture = officialCulture;
        }

        if (TryGetActiveOccupationCulture(city, out string occupationCulture))
            ApplyCultureFloor(targetShares, occupationCulture,
                CultureShareRules.ForeignOccupationCultureTarget);
        else if (IsValidCulture(officialCulture))
            ApplyCultureFloor(targetShares, officialCulture, OfficialFloor(targetShares, officialCulture));
        NormalizeShares(targetShares);

        // 行政同化代表持续的教育、礼制与官僚投入。人口迁徙仍可改变其他文化的相对占比，
        // 也能自然抬高目标文化，但不能冲销任务已经取得的同化进度。
        string assimilationTarget = GetCulturalAssimilationTarget(city);
        if (IsValidCulture(assimilationTarget))
        {
            float currentAssimilationShare = data.culture_shares.TryGetValue(assimilationTarget,
                out float currentValue) ? currentValue : 0f;
            float demographicTargetShare = targetShares.TryGetValue(assimilationTarget,
                out float targetValue) ? targetValue : 0f;
            if (demographicTargetShare < currentAssimilationShare)
            {
                CultureShareRules.SetShare(targetShares, assimilationTarget, currentAssimilationShare);
                NormalizeShares(targetShares);
            }
        }

        return MoveSharesToward(data.culture_shares, targetShares,
            CultureShareRules.PopulationPressurePerYear);
    }

    // 官方文化的保底：人口与教化里的占比再抬 25，至多 55(见 CultureShareRules.OfficialCultureInstitutionalLift)
    private static float OfficialFloor(Dictionary<string, float> shares, string official) =>
        Math.Min(CultureShareRules.OfficialCultureInstitutionalFloor,
            (shares.TryGetValue(official, out float value) ? value : 0f) + CultureShareRules.OfficialCultureInstitutionalLift);

    private static void ApplyCultureFloor(Dictionary<string, float> shares, string protectedCulture, float floor)
    {
        float current = shares.TryGetValue(protectedCulture, out float value) ? value : 0f;
        if (current >= floor) return;

        float otherTotal = Math.Max(0f, 100f - current);
        float remaining = 100f - floor;
        foreach (string culture in shares.Keys.Where(key => key != protectedCulture).ToList())
            shares[culture] = otherTotal <= Epsilon ? 0f : shares[culture] * remaining / otherTotal;
        shares[protectedCulture] = floor;
    }

    // 异族占领的文化压力(保底六成、可越过锁定改主流文化)：主流文化锁定(固定法理文化或法理锁定)时不算，
    // 以前锁着文化的城被攻占，过几年照样改从征服者的文化、改名
    private static bool TryGetActiveOccupationCulture(City city, out string culture)
    {
        culture = "";
        if (city?.data == null || city.isRekt() || city.kingdom?.data == null || city.kingdom.isRekt())
            return false;
        if (IsCityMainCultureLocked(city)) return false;
        CityExtension.CityExtraData data = city.GetOrCreate();
        string target = data.occupation_pressure_culture;
        if (!IsValidCulture(target) || string.Equals(data.main_culture, target, StringComparison.Ordinal))
            return false;
        string controllerCulture = GetRealmCulture(city.kingdom);
        if (!string.Equals(controllerCulture, target, StringComparison.Ordinal)) return false;
        culture = target;
        return true;
    }

    public static string GetOccupationCultureTarget(City city)
    {
        return TryGetActiveOccupationCulture(city, out string culture) ? culture : "";
    }

    private static bool MoveSharesToward(Dictionary<string, float> shares,
        Dictionary<string, float> targetShares, float maxPercentagePoints)
    {
        NormalizeShares(shares);
        HashSet<string> cultures = new HashSet<string>(shares.Keys);
        cultures.UnionWith(targetShares.Keys);
        float totalDeficit = cultures.Sum(culture => Math.Max(0f,
            (targetShares.TryGetValue(culture, out float target) ? target : 0f) -
            (shares.TryGetValue(culture, out float current) ? current : 0f)));
        if (totalDeficit <= Epsilon) return false;

        float factor = Math.Min(1f, Math.Max(0f, maxPercentagePoints) / totalDeficit);
        Dictionary<string, float> updated = new Dictionary<string, float>();
        foreach (string culture in cultures)
        {
            float current = shares.TryGetValue(culture, out float currentValue) ? currentValue : 0f;
            float target = targetShares.TryGetValue(culture, out float targetValue) ? targetValue : 0f;
            float value = current + (target - current) * factor;
            if (value > Epsilon) updated[culture] = value;
        }
        shares.Clear();
        foreach (KeyValuePair<string, float> pair in updated) shares[pair.Key] = pair.Value;
        NormalizeShares(shares);
        return true;
    }

    // 法理本身没有自己独立的"文化占比"存档字段——这里按它名下所有城市各自的
    // GetCityCultureShares 取平均，现算现用，不需要额外持久化。这样"文化地名历史"
    // 标签页才能标出每个文化在这个法理范围内大致占多少比重，跟"文治"决议要求
    // 首都+2/3城市文化优势的判定口径保持一致（都是按城市数量/占比累计，不是单看某一座城市）。
    public static Dictionary<string, float> GetTitleCultureShares(KingdomTitle title)
    {
        Dictionary<string, float> result = new Dictionary<string, float>();
        List<City> cities = title?.city_list?.Where(city => city != null && !city.isRekt()).ToList();
        if (cities == null || cities.Count == 0) return result;

        foreach (City city in cities)
        {
            foreach (KeyValuePair<string, float> share in GetCityCultureShares(city))
            {
                if (!IsValidCulture(share.Key)) continue;
                result[share.Key] = result.TryGetValue(share.Key, out float existing)
                    ? existing + share.Value
                    : share.Value;
            }
        }
        foreach (string culture in result.Keys.ToList())
            result[culture] /= cities.Count;
        return result;
    }

    public static void InitializeCityCulture(City city)
    {
        if (city?.data == null || city.isRekt()) return;
        CityExtension.CityExtraData data = city.GetOrCreate();
        data.culture_shares ??= new Dictionary<string, float>();
        if (HasValidShares(data.culture_shares))
        {
            NormalizeShares(data.culture_shares);
            return;
        }

        string culture = GetEffectiveTitleCulture(city.GetTitle());
        if (!IsValidCulture(culture)) culture = GetRealmCulture(city.kingdom);
        if (!IsValidCulture(culture)) return;
        data.culture_shares.Clear();
        data.culture_shares[culture] = 100f;
    }

    public static void MigrateWorldCultureData()
    {
        if (World.world == null) return;
        if (World.world.cultures != null)
        {
            foreach (Culture culture in World.world.cultures)
                CulturePatch.SyncCultureDisplayName(culture);
        }
        foreach (EmpireCore core in EmpireCoreManager.EmpireCores.Values)
        {
            if (core == null || IsValidCulture(core.default_culture)) continue;
            Empire empire = EmpireCoreManager.GetEmpires(core).FirstOrDefault();
            core.default_culture = GetRealmCulture(empire?.CoreKingdom ?? core.GetCoreCapital()?.kingdom);
        }
        foreach (KingdomTitle title in ModClass.KINGDOM_TITLE_MANAGER) InitializeTitleCulture(title);
        foreach (City city in World.world.cities)
        {
            InitializeCityCulture(city);
            UpdateCityCultureShiftCandidate(city);
        }
        foreach (Kingdom kingdom in World.world.kingdoms)
        {
            GetRealmCulture(kingdom);
            RepairUnavailableSavedRegime(kingdom);
            RepairDefaultRegime(kingdom);
        }
    }

    public static string GetMainCulture(City city, bool initialize = true)
    {
        if (city?.data == null) return "";
        Dictionary<string, float> shares = GetCityCultureShares(city, initialize);
        // 城市的“主流文化”是官方字段，不随人口占比实时改变。锁定时即便该文化
        // 暂时没有居民，也继续保留；旧档没有此字段时优先继承法理文化。
        CityExtension.CityExtraData data = city.GetOrCreate();
        bool locked = IsCityMainCultureLocked(city);
        if (IsValidCulture(data.main_culture) && (locked || shares.ContainsKey(data.main_culture)))
            return data.main_culture;
        if (locked)
        {
            string deJureCulture = GetEffectiveTitleCulture(city.GetTitle());
            if (IsValidCulture(deJureCulture))
            {
                data.main_culture = deJureCulture;
                return deJureCulture;
            }
        }
        if (shares.Count == 0) return "";
        string computed = shares.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).First().Key;
        data.main_culture = computed;
        return computed;
    }

    /// <summary>
    /// 法理文化锁定会向下锁住辖内城市的官方主流文化，但不冻结居民个人文化、
    /// 人口迁徙或 culture_shares。世界法则"固定法理文化"开着时不论有没有法理一律锁住
    /// (以前没归进法理的城不受影响，开局、新拓的城大多还没有法理，照样改从别的文化)。
    /// </summary>
    public static bool IsCityMainCultureLocked(City city)
    {
        if (city?.data == null) return false;
        if (IsFixedDeJureCultureEnabled()) return true;
        KingdomTitle title = city.GetTitle();
        return title?.data != null && !title.isRekt() && title.data.culture_locked;
    }

    /// <summary>
    /// 检查某座城市是否存在一个挑战文化，其占比已经超过阈值，可以由城主发起“改变城市主流文化”的决议。
    /// 纯检查，不会修改任何状态。
    /// </summary>
    public static bool FindCityCultureShiftCandidate(City city, out string candidate)
    {
        candidate = null;
        if (city?.data == null || city.isRekt()) return false;
        string trackedCandidate = UpdateCityCultureShiftCandidate(city);
        if (!IsValidCulture(trackedCandidate)) return false;
        if (GetCityCultureShiftStableYears(city) < CultureShareRules.CityCultureShiftStableYears) return false;
        candidate = trackedCandidate;
        return true;
    }

    public static string UpdateCityCultureShiftCandidate(City city)
    {
        if (city?.data == null || city.isRekt()) return "";
        CityExtension.CityExtraData data = city.GetOrCreate();
        bool occupationOverride = TryGetActiveOccupationCulture(city, out string occupationCulture);
        if (IsCityMainCultureLocked(city) && !occupationOverride)
        {
            // 解锁后必须重新满足完整稳定期，不能沿用锁定前积累的候选年数。
            data.culture_shift_candidate = "";
            data.culture_shift_candidate_since = -1d;
            return "";
        }
        string current = GetMainCulture(city);
        Dictionary<string, float> shares = GetCityCultureShares(city);
        float currentShare = IsValidCulture(current) && shares.TryGetValue(current, out float value) ? value : 0f;
        KeyValuePair<string, float> strongest = shares
            .Where(pair => IsValidCulture(pair.Key) &&
                           !string.Equals(pair.Key, current, StringComparison.Ordinal))
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .FirstOrDefault();

        bool qualifies = IsValidCulture(strongest.Key) &&
                          strongest.Value >= CultureShareRules.CityCultureShiftThreshold &&
                          strongest.Value - currentShare >= CultureShareRules.CityCultureShiftLeadMargin;
        if (IsCityMainCultureLocked(city) &&
            !string.Equals(strongest.Key, occupationCulture, StringComparison.Ordinal))
            qualifies = false;
        if (!qualifies)
        {
            data.culture_shift_candidate = "";
            data.culture_shift_candidate_since = -1d;
            return "";
        }

        if (!string.Equals(data.culture_shift_candidate, strongest.Key, StringComparison.Ordinal) ||
            data.culture_shift_candidate_since < 0d)
        {
            data.culture_shift_candidate = strongest.Key;
            data.culture_shift_candidate_since = World.world?.getCurWorldTime() ?? -1d;
        }
        return data.culture_shift_candidate;
    }

    public static int GetCityCultureShiftStableYears(City city)
    {
        if (city?.data == null) return 0;
        CityExtension.CityExtraData data = city.GetOrCreate();
        if (!IsValidCulture(data.culture_shift_candidate) || data.culture_shift_candidate_since < 0d) return 0;
        return Math.Max(0, Date.getYearsSince(data.culture_shift_candidate_since));
    }

    public static void UpdateCityCultureShiftCandidates(Kingdom kingdom)
    {
        if (kingdom?.cities == null || kingdom.isRekt()) return;
        foreach (City city in kingdom.cities)
            UpdateCityCultureShiftCandidate(city);
    }

    /// <summary>
    /// 由城主决议执行：把城市的官方主流文化改为 candidate。
    /// 现存居民保留个人文化，避免一次决议瞬间改写整座城市的人物文化。
    /// </summary>
    public static bool SetCityMainCulture(City city, string candidate)
    {
        if (city?.data == null || city.isRekt() || !IsValidCulture(candidate)) return false;
        bool occupationOverride = TryGetActiveOccupationCulture(city, out string occupationCulture) &&
                                  string.Equals(candidate, occupationCulture, StringComparison.Ordinal);
        if (IsCityMainCultureLocked(city) && !occupationOverride) return false;
        CityExtension.CityExtraData data = city.GetOrCreate();
        if (string.Equals(data.main_culture, candidate, StringComparison.Ordinal)) return false;
        string previousCulture = data.main_culture;
        data.main_culture = candidate;
        data.culture_shift_candidate = "";
        data.culture_shift_candidate_since = -1d;
        data.occupation_pressure_culture = "";
        ApplyCulturalCityRename(city, previousCulture, candidate);
        SyncVanillaCityCulture(city);
        EmpireCraftNamePlateLibrary.RequestRefresh();
        return true;
    }

    // 按模组文化名找原版文化对象要逐个比对全部文化，决策检查里每个君主、城主都会调：记十秒
    private static readonly Dictionary<string, (Culture culture, float at)> NativeCultureCache = new(StringComparer.Ordinal);

    private static Culture CachedNativeCulture(string culture)
    {
        float now = global::UnityEngine.Time.unscaledTime;
        if (NativeCultureCache.TryGetValue(culture, out var hit) && now - hit.at < 10f && now >= hit.at &&
            (hit.culture == null || hit.culture.data != null && !hit.culture.isRekt()))
            return hit.culture;
        Culture found = GetNativeCultureObject(culture);
        NativeCultureCache[culture] = (found, now);
        return found;
    }

    // 原版的国家文化对象(原版窗口、新城看它)跟着国家文化走
    public static void SyncVanillaKingdomCulture(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return;
        string realm = kingdom.GetOrCreate().realm_culture;
        if (!IsValidCulture(realm) || string.Equals(CulturePatch.GetInjectedCultureName(kingdom.culture), realm, StringComparison.Ordinal))
            return;
        Culture native = CachedNativeCulture(realm);
        if (native != null) kingdom.setCulture(native);
    }

    // 原版的城市文化对象(原版窗口、没有文化的新居民看它)跟着官方主流文化走
    public static void SyncVanillaCityCulture(City city)
    {
        if (city?.data == null || city.isRekt()) return;
        string main = city.GetOrCreate().main_culture;
        if (!IsValidCulture(main) || string.Equals(CulturePatch.GetInjectedCultureName(city.culture), main, StringComparison.Ordinal))
            return;
        Culture native = CachedNativeCulture(main);
        if (native == null || city.culture == native) return;
        city.culture = native;
        World.world?.cultures?.setDirtyCities();
    }

    // 原版决策"城主把城市文化改成自己的"(只改原版的文化对象)：主流文化锁定时不许，并把原版对象拉回官方文化；
    // 城主与城本是同一文化(两个同文化的原版对象)也不改，免得来回互改
    public static bool AllowLeaderCityCulture(Actor actor)
    {
        City city = actor?.city;
        if (city?.data == null || city.isRekt() || actor.culture == null ||
            EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(city)) return true;
        if (IsCityMainCultureLocked(city))
        {
            SyncVanillaCityCulture(city);
            return false;
        }
        string leaderCulture = GetActorCulture(actor);
        return !IsValidCulture(leaderCulture) ||
               !string.Equals(leaderCulture, CulturePatch.GetInjectedCultureName(city.culture), StringComparison.Ordinal);
    }

    /// <summary>
    /// 异文化征服只施加一次制度冲击并建立持续的占领文化压力，不会立即替换城市官方文化
    /// 或地名。达到文化影响门槛和稳定期后，仍需走城市文化转换谋划。
    /// </summary>
    public static bool ApplyForeignOccupationCulture(City city, Kingdom occupier)
    {
        if (city?.data == null || city.isRekt() || occupier?.data == null || occupier.isRekt()) return false;
        string newCulture = GetRealmCulture(occupier);
        string oldCulture = GetMainCulture(city);
        if (!IsValidCulture(newCulture) || !IsValidCulture(oldCulture)) return false;

        CityExtension.CityExtraData data = city.GetOrCreate();
        if (IsValidCulture(data.restoration_culture) &&
            string.Equals(newCulture, data.restoration_culture, StringComparison.Ordinal))
        {
            data.culture_restoration_available = true;
            data.occupation_pressure_culture = "";
            data.culture_shift_candidate = "";
            data.culture_shift_candidate_since = -1d;
            data.last_culture_share_sync_timestamp = World.world?.getCurWorldTime() ?? -1d;
            TranslateHelper.LogCultureRestorationAvailable(city, newCulture);
            return true;
        }

        if (string.Equals(newCulture, oldCulture, StringComparison.Ordinal)) return false;
        // 主流文化锁着：占领不改它(见 TryGetActiveOccupationCulture)，文化比例也不跳
        if (IsCityMainCultureLocked(city)) return false;
        if (!IsValidCulture(data.restoration_culture)) data.restoration_culture = oldCulture;
        data.culture_restoration_available = false;
        data.occupation_pressure_culture = newCulture;

        Dictionary<string, float> shares = GetCityCultureShares(city);
        float current = shares.TryGetValue(newCulture, out float value) ? value : 0f;
        CultureShareRules.SetShare(shares, newCulture,
            Math.Max(current, CultureShareRules.ForeignOccupationCultureShock));
        NormalizeShares(shares);

        data.culture_shift_candidate = "";
        data.culture_shift_candidate_since = -1d;
        data.last_culture_share_sync_timestamp = World.world?.getCurWorldTime() ?? -1d;
        TranslateHelper.LogForeignCultureOccupation(city, oldCulture, newCulture);
        return true;
    }

    public static bool FindCultureRestorationCandidate(City city, Kingdom controller, out string culture)
    {
        culture = "";
        if (city?.data == null || city.isRekt() || controller?.data == null || controller.isRekt() ||
            city.kingdom != controller) return false;
        CityExtension.CityExtraData data = city.GetOrCreate();
        if (!data.culture_restoration_available || !IsValidCulture(data.restoration_culture)) return false;
        // 主流文化锁定时，只许恢复成现行的官方文化(把被占领时带进来的人口改回来)，不许借复俗改掉官方文化
        if (IsCityMainCultureLocked(city) &&
            !string.Equals(data.main_culture, data.restoration_culture, StringComparison.Ordinal)) return false;
        string controllerCulture = GetRealmCulture(controller);
        if (!string.Equals(controllerCulture, data.restoration_culture, StringComparison.Ordinal)) return false;
        culture = data.restoration_culture;
        return true;
    }

    // 神力易俗(见 GodPowers/CityCulturePower)：这座城当即改从某文化——城里的人物、背景人口、文化比例、地名一并改过来，
    // 不看法理文化锁定、不走城市文化转换的谋划
    public static bool ForceCityCulture(City city, string culture)
    {
        if (city?.data == null || city.isRekt() || !IsValidCulture(culture)) return false;
        CityExtension.CityExtraData data = city.GetOrCreate();
        string previousCulture = GetMainCulture(city);
        Actor template = city.leader ?? city.kingdom?.king ?? city.units?.FirstOrDefault(actor =>
            actor != null && !actor.isRekt() && actor.isAlive());
        Culture nativeCulture = ResolveNativeCulture(culture, template, createIfMissing: true);
        if (nativeCulture != null)
            foreach (Actor resident in city.units?.Where(actor => actor?.data != null && !actor.isRekt() && actor.isAlive()).ToList() ??
                                       new List<Actor>())
                if (!string.Equals(GetActorCulture(resident), culture, StringComparison.Ordinal))
                    resident.setCulture(nativeCulture);
        CityPopulationSystem.ConvertBackgroundCulture(city, culture);
        data.culture_shares ??= new Dictionary<string, float>();
        data.culture_shares.Clear();
        data.culture_shares[culture] = 100f;
        data.main_culture = culture;
        data.culture_shift_candidate = "";
        data.culture_shift_candidate_since = -1d;
        data.occupation_pressure_culture = "";
        data.last_culture_share_sync_timestamp = World.world?.getCurWorldTime() ?? -1d;
        if (!string.Equals(previousCulture, culture, StringComparison.Ordinal)) ApplyCulturalCityRename(city, previousCulture, culture);
        if (nativeCulture != null && city.culture != nativeCulture)
        {
            city.culture = nativeCulture;
            World.world?.cultures?.setDirtyCities();
        }
        EmpireCraftNamePlateLibrary.RequestRefresh();
        return true;
    }

    public static bool RestoreCityCulture(City city, Kingdom controller)
    {
        if (!FindCultureRestorationCandidate(city, controller, out string culture)) return false;
        CityExtension.CityExtraData data = city.GetOrCreate();
        string previousCulture = GetMainCulture(city);
        Actor template = city.leader ?? controller.king ?? city.units?.FirstOrDefault(actor =>
            actor != null && !actor.isRekt() && actor.isAlive());
        Culture nativeCulture = ResolveNativeCulture(culture, template, createIfMissing: true);
        if (nativeCulture == null) return false;

        List<Actor> residents = city.units?
            .Where(actor => actor?.data != null && !actor.isRekt() && actor.isAlive())
            .ToList() ?? new List<Actor>();
        foreach (Actor resident in residents)
        {
            if (!string.Equals(GetActorCulture(resident), culture, StringComparison.Ordinal))
                resident.setCulture(nativeCulture);
        }
        CityPopulationSystem.ConvertBackgroundCulture(city, culture);

        data.culture_shares ??= new Dictionary<string, float>();
        data.culture_shares.Clear();
        data.culture_shares[culture] = 100f;
        data.main_culture = culture;
        data.culture_shift_candidate = "";
        data.culture_shift_candidate_since = -1d;
        data.occupation_pressure_culture = "";
        data.restoration_culture = "";
        data.culture_restoration_available = false;
        data.last_culture_share_sync_timestamp = World.world?.getCurWorldTime() ?? -1d;
        ApplyCulturalCityRename(city, previousCulture, culture);
        if (city.culture != nativeCulture)
        {
            city.culture = nativeCulture;
            World.world?.cultures?.setDirtyCities();
        }
        EmpireCraftNamePlateLibrary.RequestRefresh();
        TranslateHelper.LogCityCultureRestored(city, previousCulture, culture);
        return true;
    }

    // ================= 文化专属地名历史（城市 / 法理头衔 / 省份）=================

    /// <summary>
    /// 在世界上找一个当前文化等于目标文化、还存活着的单位，只是借用它调用
    /// Actor.generateName 按该文化的命名规则生成一个名字，不会修改这个单位本身的任何状态。
    /// </summary>
    private static Actor FindCultureTemplateActor(string culture)
    {
        if (!IsValidCulture(culture) || World.world?.units == null) return null;
        foreach (Actor actor in World.world.units)
        {
            if (actor == null || actor.isRekt()) continue;
            if (string.Equals(GetActorCulture(actor), culture, StringComparison.Ordinal)) return actor;
        }
        return null;
    }

    /// <summary>
    /// 现场按目标文化生成一个新地名（纯生成，不落地保存也不修改任何对象状态），
    /// 找不到该文化的任何存活单位可供借用时返回 null。人名要传性别(不传原版随手定男女，男丁会取成女名)。
    /// </summary>
    public static string GenerateCulturalName(MetaType metaType, long id, string culture, ActorSex sex = ActorSex.None)
    {
        Actor templateActor = FindCultureTemplateActor(culture);
        if (templateActor == null) return null;
        string generated;
        try { generated = templateActor.generateName(metaType, id, sex); }
        catch { return null; }
        return string.IsNullOrWhiteSpace(generated) ? null : generated;
    }

    public static string GetCityCulturalName(City city, string culture)
    {
        if (city?.data == null || !IsValidCulture(culture)) return null;
        CityExtension.CityExtraData data = city.GetOrCreate();
        return data.name_history != null && data.name_history.TryGetValue(culture, out string name) &&
               !string.IsNullOrWhiteSpace(name) ? name : null;
    }

    /// <summary>
    /// 把一个名字记录进城市某个文化的历史里：一旦记录就永久保留，不会被后续的文化转变
    /// 自动覆盖或清除，只有玩家在法理界面手动删除才会消失。
    /// </summary>
    public static void RecordCityCulturalName(City city, string culture, string name)
    {
        if (city?.data == null || !IsValidCulture(culture) || string.IsNullOrWhiteSpace(name)) return;
        CityExtension.CityExtraData data = city.GetOrCreate();
        data.name_history ??= new Dictionary<string, string>();
        data.name_history[culture] = name;
    }

    public static void RemoveCityCulturalName(City city, string culture)
    {
        if (city?.data == null || string.IsNullOrWhiteSpace(culture)) return;
        city.GetOrCreate().name_history?.Remove(culture);
    }

    /// <summary>
    /// 城市从 fromCulture 改为 toCulture 时调用：先把当前名字记进旧文化的历史，再解析新文化
    /// 应该用什么名字——历史里已经存过就恢复，没存过就现生成一个新的（同时也记录进历史，
    /// 这样以后同一种文化再次成为主流时可以直接复用）。只做解析，不修改 city 本身。
    /// </summary>
    public static bool ResolveCulturalCityName(City city, string fromCulture, string toCulture,
        out string resolvedName, out bool restored)
    {
        resolvedName = null;
        restored = false;
        if (city?.data == null || !IsValidCulture(toCulture)) return false;

        if (IsValidCulture(fromCulture) && !string.IsNullOrWhiteSpace(city.data.name))
            RecordCityCulturalName(city, fromCulture, city.data.name);

        string historic = GetCityCulturalName(city, toCulture);
        if (!string.IsNullOrWhiteSpace(historic))
        {
            resolvedName = historic;
            restored = true;
            return true;
        }

        string generated = GenerateCulturalName(MetaType.City, city.getID(), toCulture);
        if (string.IsNullOrWhiteSpace(generated)) return false;
        RecordCityCulturalName(city, toCulture, generated);
        resolvedName = generated;
        return true;
    }

    /// <summary>
    /// SetCityMainCulture 的联动：解析新文化该用的名字并真的改到 city 身上，
    /// 同时按恢复/新生成分别打不同的世界提示。解析不出名字（比如这个文化世界上还
    /// 完全没有出现过任何单位）就保留原名不动，不算错误。
    /// </summary>
    private static void ApplyCulturalCityRename(City city, string previousCulture, string newCulture)
    {
        if (city?.data == null) return;
        if (!ResolveCulturalCityName(city, previousCulture, newCulture, out string resolvedName, out bool restored))
            return;
        if (string.IsNullOrWhiteSpace(resolvedName) ||
            string.Equals(resolvedName, city.data.name, StringComparison.Ordinal)) return;
        string oldName = city.data.name;
        city.setName(resolvedName);
        if (restored)
            TranslateHelper.LogCityNameRestored(city, oldName, resolvedName, newCulture);
        else
            TranslateHelper.LogCityNameChanged(city, oldName, resolvedName, newCulture);
    }

    public static string GetTitleCulturalName(KingdomTitle title, string culture)
    {
        if (title?.data == null || !IsValidCulture(culture)) return null;
        return title.data.title_name_history != null &&
               title.data.title_name_history.TryGetValue(culture, out string name) &&
               !string.IsNullOrWhiteSpace(name) ? name : null;
    }

    public static void RecordTitleCulturalName(KingdomTitle title, string culture, string name)
    {
        if (title?.data == null || !IsValidCulture(culture) || string.IsNullOrWhiteSpace(name)) return;
        title.data.title_name_history ??= new Dictionary<string, string>();
        title.data.title_name_history[culture] = name;
    }

    public static void RemoveTitleCulturalName(KingdomTitle title, string culture)
    {
        if (title?.data == null || string.IsNullOrWhiteSpace(culture)) return;
        title.data.title_name_history?.Remove(culture);
    }

    public static string GetProvinceCulturalName(KingdomTitle title, string culture)
    {
        if (title?.data == null || !IsValidCulture(culture)) return null;
        return title.data.province_name_history != null &&
               title.data.province_name_history.TryGetValue(culture, out string name) &&
               !string.IsNullOrWhiteSpace(name) ? name : null;
    }

    public static void RecordProvinceCulturalName(KingdomTitle title, string culture, string name)
    {
        if (title?.data == null || !IsValidCulture(culture) || string.IsNullOrWhiteSpace(name)) return;
        title.data.province_name_history ??= new Dictionary<string, string>();
        title.data.province_name_history[culture] = name;
    }

    public static void RemoveProvinceCulturalName(KingdomTitle title, string culture)
    {
        if (title?.data == null || string.IsNullOrWhiteSpace(culture)) return;
        title.data.province_name_history?.Remove(culture);
    }

    /// <summary>
    /// 法理头衔从 fromCulture 改为 toCulture 时调用：头衔名和省份名各自独立解析——
    /// 历史里存过就恢复，没存过就借首都城市现生成一个新的（同时记录进历史）。
    /// 只做解析，不修改 title 本身。
    /// </summary>
    private static void ResolveCulturalTitleName(KingdomTitle title, string fromCulture, string toCulture,
        out string resolvedTitleName, out bool titleRestored,
        out string resolvedProvinceName, out bool provinceRestored)
    {
        resolvedTitleName = null;
        titleRestored = false;
        resolvedProvinceName = null;
        provinceRestored = false;
        if (title?.data == null || !IsValidCulture(toCulture)) return;

        if (IsValidCulture(fromCulture))
        {
            if (!string.IsNullOrWhiteSpace(title.data.name))
                RecordTitleCulturalName(title, fromCulture, title.data.name);
            if (!string.IsNullOrWhiteSpace(title.data.province_name))
                RecordProvinceCulturalName(title, fromCulture, title.data.province_name);
        }

        City capital = title.title_capital;
        string historicTitle = GetTitleCulturalName(title, toCulture);
        if (!string.IsNullOrWhiteSpace(historicTitle))
        {
            resolvedTitleName = historicTitle;
            titleRestored = true;
        }
        else if (capital != null && !capital.isRekt())
        {
            string generated = GenerateCulturalName(MetaType.City, capital.getID(), toCulture);
            if (!string.IsNullOrWhiteSpace(generated))
            {
                resolvedTitleName = generated;
                RecordTitleCulturalName(title, toCulture, generated);
            }
        }

        string historicProvince = GetProvinceCulturalName(title, toCulture);
        if (!string.IsNullOrWhiteSpace(historicProvince))
        {
            resolvedProvinceName = historicProvince;
            provinceRestored = true;
        }
        else if (capital != null && !capital.isRekt())
        {
            // 省份名字跟头衔名字各自独立生成一次，避免两者永远完全一样。
            string generated = GenerateCulturalName(MetaType.City, capital.getID(), toCulture);
            if (!string.IsNullOrWhiteSpace(generated))
            {
                resolvedProvinceName = generated;
                RecordProvinceCulturalName(title, toCulture, generated);
            }
        }
    }

    /// <summary>
    /// ConvertTitleCulture 的联动：解析并应用头衔名/省份名的文化对应名字，
    /// 分别按恢复/新生成打世界提示；解析不出就保留原名不动。
    /// </summary>
    private static void ApplyCulturalTitleRename(KingdomTitle title, string previousCulture, string newCulture)
    {
        if (title?.data == null) return;
        ResolveCulturalTitleName(title, previousCulture, newCulture,
            out string resolvedTitleName, out bool titleRestored,
            out string resolvedProvinceName, out bool provinceRestored);

        if (!string.IsNullOrWhiteSpace(resolvedTitleName) &&
            !string.Equals(resolvedTitleName, title.data.name, StringComparison.Ordinal))
        {
            string oldName = title.data.name;
            title.data.name = resolvedTitleName;
            if (titleRestored)
                TranslateHelper.LogTitleNameRestored(title, oldName, resolvedTitleName, newCulture, isProvince: false);
            else
                TranslateHelper.LogTitleNameChanged(title, oldName, resolvedTitleName, newCulture, isProvince: false);
        }

        if (!string.IsNullOrWhiteSpace(resolvedProvinceName) &&
            !string.Equals(resolvedProvinceName, title.data.province_name, StringComparison.Ordinal))
        {
            string oldProvinceName = title.data.province_name;
            title.data.province_name = resolvedProvinceName;
            title.RefreshAdministrativeDivisionNames();
            if (provinceRestored)
                TranslateHelper.LogTitleNameRestored(title, oldProvinceName, resolvedProvinceName, newCulture,
                    isProvince: true);
            else
                TranslateHelper.LogTitleNameChanged(title, oldProvinceName, resolvedProvinceName, newCulture,
                    isProvince: true);
        }
    }

    public static float GetCultureShare(City city, string culture)
    {
        Dictionary<string, float> shares = GetCityCultureShares(city);
        return culture != null && shares.TryGetValue(culture, out float value) ? value : 0f;
    }

    public static float GetNewbornLocalCultureChance(City city)
    {
        if (city?.data == null || city.isRekt())
            return CultureShareRules.NewbornLocalCultureBaseChance;

        Dictionary<string, float> shares = GetCityCultureShares(city);
        string officialCulture = GetMainCulture(city);
        if (!IsValidCulture(officialCulture))
            return CultureShareRules.NewbornLocalCultureBaseChance;

        float officialShare = shares.TryGetValue(officialCulture, out float value) ? value : 0f;
        float strongestOtherShare = shares
            .Where(pair => IsValidCulture(pair.Key) &&
                           !string.Equals(pair.Key, officialCulture, StringComparison.Ordinal))
            .Select(pair => pair.Value)
            .DefaultIfEmpty(0f)
            .Max();
        return CultureShareRules.CalculateNewbornLocalCultureChance(officialShare, strongestOtherShare);
    }

    public static bool IsAssimilated(City city, string culture)
    {
        return CultureShareRules.IsAssimilated(GetCultureShare(city, culture));
    }

    public static bool ApplyAssimilation(City city, string culture, float percentagePoints)
    {
        if (!IsAssimilationEnabled()) return false;
        return SetCultureShareInternal(city, culture, GetCultureShare(city, culture) + Math.Abs(percentagePoints));
    }

    public static bool SetCityCultureShare(City city, string culture, float percentage)
    {
        if (city?.data == null || city.isRekt() || !IsValidCulture(culture)) return false;
        SetCultureShareInternal(city, culture, percentage);
        return true;
    }

    private static bool SetCultureShareInternal(City city, string culture, float percentage)
    {
        if (city?.data == null || city.isRekt() || !IsValidCulture(culture)) return false;
        Dictionary<string, float> shares = GetCityCultureShares(city);
        percentage = Math.Max(0f, Math.Min(100f, percentage));
        float oldValue = shares.TryGetValue(culture, out float current) ? current : 0f;
        CultureShareRules.SetShare(shares, culture, percentage);
        NormalizeShares(shares);
        city.GetOrCreate().last_culture_share_sync_timestamp = World.world?.getCurWorldTime() ?? -1d;
        float newValue = shares.TryGetValue(culture, out float updated) ? updated : 0f;
        UpdateCityCultureShiftCandidate(city);
        return Math.Abs(oldValue - newValue) > Epsilon;
    }

    /// <summary>
    /// 检查某个法理是否已经满足“国家主流文化变更”的条件：
    /// 某个（非当前）文化已经是首都的主流文化，并且至少是辖下 2/3 城市的主流文化。
    /// 纯检查，不会修改任何状态——真正的变更只能通过“文治”派系发起并执行对应决议
    /// （见 Regimes/TemporaryFactions/Claims/TempFac_文化转化.cs），不再自动触发。
    /// </summary>
    public static bool FindTitleCultureConversionCandidate(KingdomTitle title, out string candidate)
    {
        candidate = null;
        if (title?.data == null || title.isRekt() || title.data.culture_locked ||
            IsFixedDeJureCultureEnabled() || !IsAssimilationEnabled()) return false;
        // 都护府辖区是中央有意保留的异文化军管区，不参与"文治"派系的法理文化转化决议。
        if (IsProtectorateGeneralTitle(title)) return false;

        List<City> cities = title.getCities().Where(city => city != null && !city.isRekt()).ToList();
        if (cities.Count == 0) return false;

        City capital = title.title_capital;
        if (capital == null || capital.isRekt() || !cities.Contains(capital)) return false;

        string current = GetEffectiveTitleCulture(title);
        string capitalCulture = GetMainCulture(capital);
        if (!IsValidCulture(capitalCulture) || string.Equals(capitalCulture, current, StringComparison.Ordinal))
            return false;

        int requiredCities = (int)Math.Ceiling(cities.Count * CultureShareRules.TitleCultureDominanceRatio);
        int dominantCount = cities.Count(city =>
            string.Equals(GetMainCulture(city), capitalCulture, StringComparison.Ordinal));
        if (dominantCount < requiredCities) return false;

        candidate = capitalCulture;
        return true;
    }

    /// <summary>
    /// 由“文治”派系决议执行：把法理的默认文化正式改为 candidate。
    /// </summary>
    public static bool ConvertTitleCulture(KingdomTitle title, string candidate)
    {
        if (title?.data == null || title.isRekt() || !IsValidCulture(candidate)) return false;
        string previous = GetEffectiveTitleCulture(title);
        if (string.Equals(previous, candidate, StringComparison.Ordinal)) return false;
        RememberInitialTitleCulture(title);

        title.data.culture = candidate;
        title.data.culture_explicit = true;
        RefreshCultureDependents(title);
        TranslateHelper.LogDeJureCultureChanged(title, previous, candidate);
        ApplyCulturalTitleRename(title, previous, candidate);
        return true;
    }

    /// <summary>
    /// 都护府判定：这个 administration 是否被政体引擎实际判定为"都护府"
    /// （KingdomType.LvLing_duhufu——见 EmpireCraftKingdomBehCheckKingdomType 里的
    /// culture_mismatch 条件 + Regimes/Configs/LvLing/SystemConfig.json 的 LvLing_duhufu
    /// 配置）。都护府本身就是一个真正的国家类型，不再是"军+文化不一致"的派生显示层，
    /// 由 SyncKingdomStatus 每月按条件自动判定/切换，其辖下城市的 CityType 也会跟着
    /// 自动变成 LvLing_jimizhou（羁縻州），见该 JSON 里 LvLing_duhufu 的 city_type 字段。
    /// 纯判断，不修改任何状态。
    /// </summary>
    public static bool IsProtectorateGeneral(Kingdom administration)
    {
        return administration?.data != null && !administration.isRekt() &&
               administration.GetKingdomType() == KingdomType.LvLing_duhufu;
    }

    /// <summary>
    /// 某个法理当前的实际管理机构里，只要有一个是都护府（见 IsProtectorateGeneral），
    /// 整个法理就视为都护府辖区：不参与"文治"派系的法理文化转化决议，
    /// 统治者的个人文化也不会跟着治下城市自动改变（见 SyncActorToCityMainCulture）。
    /// </summary>
    public static bool IsProtectorateGeneralTitle(KingdomTitle title)
    {
        if (title?.data == null || title.isRekt()) return false;
        foreach (Kingdom administration in KingdomTitleRelationResolver.FindCurrentAdministrations(title))
        {
            // 羁縻州同样保留本地习俗，不做法理文化转化
            if (IsProtectorateGeneral(administration) || JimiSystem.IsJimiAdministration(administration)) return true;
        }
        return false;
    }

    public static bool AssignCulturalAssimilationDuty(Kingdom administration, Empire empire,
        string targetCulture)
    {
        if (administration?.data == null || administration.isRekt() ||
            empire == null || empire.isRekt() || empire.IsArchived() ||
            administration.GetEmpire() != empire || administration.GetAdministrativeTitle() == null ||
            !IsValidCulture(targetCulture) ||
            !AdministrationNeedsCulturalAssimilation(administration, targetCulture)) return false;

        KingdomExtension.KingdomExtraData data = administration.GetOrCreate();
        data.cultural_assimilation_duty = true;
        data.cultural_assimilation_empire_id = empire.id;
        data.cultural_assimilation_target_culture = targetCulture;
        data.cultural_assimilation_started_timestamp = World.world?.getCurWorldTime() ?? -1d;
        return true;
    }

    public static bool AssignCulturalAssimilationDuty(City city, Empire empire, string targetCulture)
    {
        if (city?.data == null || city.isRekt() || empire == null || empire.isRekt() ||
            empire.IsArchived() || city.kingdom?.data == null || city.kingdom != empire.CoreKingdom ||
            city.kingdom.GetEmpire() != empire || !IsValidCulture(targetCulture) ||
            !CityNeedsCulturalAssimilation(city, targetCulture)) return false;

        CityExtension.CityExtraData data = city.GetOrCreate();
        data.cultural_assimilation_duty = true;
        data.cultural_assimilation_empire_id = empire.id;
        data.cultural_assimilation_target_culture = targetCulture;
        data.cultural_assimilation_started_timestamp = World.world?.getCurWorldTime() ?? -1d;
        data.last_cultural_assimilation_push_timestamp = -1d;
        return true;
    }

    public static void ClearCulturalAssimilationDuty(Kingdom administration)
    {
        if (administration == null) return;
        KingdomExtension.KingdomExtraData data = administration.GetOrCreate();
        data.cultural_assimilation_duty = false;
        data.cultural_assimilation_empire_id = -1L;
        data.cultural_assimilation_target_culture = "";
        data.cultural_assimilation_started_timestamp = -1d;
    }

    public static void ClearCulturalAssimilationDuty(City city)
    {
        if (city == null) return;
        CityExtension.CityExtraData data = city.GetOrCreate();
        data.cultural_assimilation_duty = false;
        data.cultural_assimilation_empire_id = -1L;
        data.cultural_assimilation_target_culture = "";
        data.cultural_assimilation_started_timestamp = -1d;
        data.last_cultural_assimilation_push_timestamp = -1d;
    }

    /// <summary>
    /// 月度维护同化任务：迁移旧档，清理失效任务，并在达到结果门槛后自动结案。
    /// 行政区需要三分之二的辖城承认目标文化，且按人口加权的目标文化占比达到 65%。
    /// </summary>
    public static void UpdateCulturalAssimilationDuty(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return;

        if (TryGetAdministrationAssimilationTarget(kingdom, out string administrationTarget) &&
            IsAdministrationAssimilationComplete(kingdom, administrationTarget))
        {
            TranslateHelper.LogCulturalAssimilationDutyCompleted(kingdom, administrationTarget);
            ClearCulturalAssimilationDuty(kingdom);
        }

        if (kingdom.cities == null) return;
        foreach (City city in kingdom.cities.ToList())
        {
            if (!TryGetDirectCityAssimilationTarget(city, out string cityTarget)) continue;
            if (!string.Equals(GetMainCulture(city), cityTarget, StringComparison.Ordinal)) continue;
            TranslateHelper.LogCulturalAssimilationDutyCompleted(city, cityTarget);
            ClearCulturalAssimilationDuty(city);
        }
    }

    public static bool AdministrationNeedsCulturalAssimilation(Kingdom administration, string targetCulture)
    {
        return GetAdministrationAssimilationCities(administration)
                   .Any(city => !IsCityMainCultureLocked(city)) &&
               !IsAdministrationAssimilationComplete(administration, targetCulture);
    }

    public static bool CityNeedsCulturalAssimilation(City city, string targetCulture)
    {
        return city?.data != null && !city.isRekt() && !IsCityMainCultureLocked(city) &&
               IsValidCulture(targetCulture) &&
               !string.Equals(GetMainCulture(city), targetCulture, StringComparison.Ordinal);
    }

    public static string GetCulturalAssimilationTarget(City city)
    {
        if (city?.data == null || city.isRekt()) return "";
        if (TryGetDirectCityAssimilationTarget(city, out string directTarget)) return directTarget;
        return TryGetAdministrationAssimilationTarget(city.kingdom, out string administrationTarget)
            ? administrationTarget
            : "";
    }

    public static string GetCulturalAssimilationPhase(City city)
    {
        string targetCulture = GetCulturalAssimilationTarget(city);
        if (!IsValidCulture(targetCulture)) return "";
        if (string.Equals(GetMainCulture(city), targetCulture, StringComparison.Ordinal))
            return "cultural_assimilation_phase_completed";
        return IsCityCultureShiftDemographicallyReady(city, targetCulture)
            ? "cultural_assimilation_phase_consolidation"
            : "cultural_assimilation_phase_promotion";
    }

    /// <summary>
    /// 被指定长官每座城市每年最多推进一次。达到 55% 且领先现主流文化 15 个百分点后，
    /// 行政推动停止，城市转入十年的自然巩固阶段，最后仍由城市主流文化 Plot 正式承认。
    /// </summary>
    public static bool FindCulturalAssimilationDutyCandidate(City city, out string targetCulture)
    {
        targetCulture = null;
        if (city?.data == null || city.isRekt()) return false;
        targetCulture = GetCulturalAssimilationTarget(city);
        if (!CityNeedsCulturalAssimilation(city, targetCulture) ||
            IsCityCultureShiftDemographicallyReady(city, targetCulture)) return false;

        double lastPush = city.GetOrCreate().last_cultural_assimilation_push_timestamp;
        if (lastPush >= 0d && Date.getYearsSince(lastPush) < 1) return false;
        return true;
    }

    /// <summary>
    /// 由被指定承担同化职责的长官发起：花费影响力，把目标文化人口占比提高 5%。
    /// </summary>
    public static bool ApplyCulturalAssimilationDuty(City city, string targetCulture)
    {
        if (!FindCulturalAssimilationDutyCandidate(city, out string assignedTarget) ||
            !string.Equals(assignedTarget, targetCulture, StringComparison.Ordinal)) return false;
        // 教化不到的地方(山后、长城外、远离文化中心)推得慢(见 CultureRadiation.AssimilationFactor)
        if (!ApplyAssimilation(city, targetCulture,
                CulturalAssimilationDutyPush * CultureRadiation.AssimilationFactor(city, targetCulture))) return false;
        city.GetOrCreate().last_cultural_assimilation_push_timestamp =
            World.world?.getCurWorldTime() ?? -1d;
        return true;
    }

    private static bool TryGetAdministrationAssimilationTarget(Kingdom administration,
        out string targetCulture)
    {
        targetCulture = "";
        if (administration?.data == null || administration.isRekt()) return false;
        KingdomExtension.KingdomExtraData data = administration.GetOrCreate();
        if (!data.cultural_assimilation_duty) return false;

        Empire empire = administration.GetEmpire();
        if (empire == null || empire.isRekt() || empire.IsArchived() ||
            administration.GetAdministrativeTitle() == null)
        {
            ClearCulturalAssimilationDuty(administration);
            return false;
        }
        List<City> administrationCities = GetAdministrationAssimilationCities(administration);
        if (administrationCities.Count > 0 && administrationCities.All(IsCityMainCultureLocked))
        {
            ClearCulturalAssimilationDuty(administration);
            return false;
        }

        // 旧档只有 bool 标记：首次维护时按当前帝国主文化补齐任务快照。
        if (data.cultural_assimilation_empire_id < 0L &&
            !IsValidCulture(data.cultural_assimilation_target_culture))
        {
            string legacyTarget = GetEmpireDefaultCulture(empire);
            if (!IsValidCulture(legacyTarget))
            {
                ClearCulturalAssimilationDuty(administration);
                return false;
            }
            data.cultural_assimilation_empire_id = empire.id;
            data.cultural_assimilation_target_culture = legacyTarget;
            data.cultural_assimilation_started_timestamp = World.world?.getCurWorldTime() ?? -1d;
        }

        if (data.cultural_assimilation_empire_id != empire.id ||
            !IsValidCulture(data.cultural_assimilation_target_culture))
        {
            ClearCulturalAssimilationDuty(administration);
            return false;
        }
        targetCulture = data.cultural_assimilation_target_culture;
        return true;
    }

    private static bool TryGetDirectCityAssimilationTarget(City city, out string targetCulture)
    {
        targetCulture = "";
        if (city?.data == null || city.isRekt()) return false;
        CityExtension.CityExtraData data = city.GetOrCreate();
        if (!data.cultural_assimilation_duty) return false;

        Empire empire = city.kingdom?.GetEmpire();
        if (empire == null || empire.isRekt() || empire.IsArchived() ||
            city.kingdom != empire.CoreKingdom || IsCityMainCultureLocked(city))
        {
            ClearCulturalAssimilationDuty(city);
            return false;
        }

        if (data.cultural_assimilation_empire_id < 0L &&
            !IsValidCulture(data.cultural_assimilation_target_culture))
        {
            string legacyTarget = GetEmpireDefaultCulture(empire);
            if (!IsValidCulture(legacyTarget))
            {
                ClearCulturalAssimilationDuty(city);
                return false;
            }
            data.cultural_assimilation_empire_id = empire.id;
            data.cultural_assimilation_target_culture = legacyTarget;
            data.cultural_assimilation_started_timestamp = World.world?.getCurWorldTime() ?? -1d;
        }

        if (data.cultural_assimilation_empire_id != empire.id ||
            !IsValidCulture(data.cultural_assimilation_target_culture))
        {
            ClearCulturalAssimilationDuty(city);
            return false;
        }
        targetCulture = data.cultural_assimilation_target_culture;
        return true;
    }

    private static bool IsCityCultureShiftDemographicallyReady(City city, string targetCulture)
    {
        if (!IsValidCulture(targetCulture)) return false;
        float targetShare = GetCultureShare(city, targetCulture);
        string currentCulture = GetMainCulture(city);
        float currentShare = GetCultureShare(city, currentCulture);
        return targetShare >= CultureShareRules.CityCultureShiftThreshold &&
               targetShare - currentShare >= CultureShareRules.CityCultureShiftLeadMargin;
    }

    private static List<City> GetAdministrationAssimilationCities(Kingdom administration)
    {
        KingdomTitle title = administration?.GetAdministrativeTitle();
        if (title?.data == null || title.isRekt()) return new List<City>();
        return title.getCities()
            .Where(city => city?.data != null && !city.isRekt() && city.kingdom == administration)
            .Distinct()
            .ToList();
    }

    private static bool IsAdministrationAssimilationComplete(Kingdom administration,
        string targetCulture)
    {
        if (!IsValidCulture(targetCulture)) return false;
        List<City> cities = GetAdministrationAssimilationCities(administration);
        if (cities.Count == 0) return false;

        int recognizedCities = cities.Count(city =>
            string.Equals(GetMainCulture(city), targetCulture, StringComparison.Ordinal));
        int requiredCities = (int)Math.Ceiling(cities.Count * 2d / 3d);
        if (recognizedCities < requiredCities) return false;

        double weightedShare = 0d;
        double totalWeight = 0d;
        foreach (City city in cities)
        {
            int population = Math.Max(1, city.CountLivingPopulation());
            weightedShare += GetCultureShare(city, targetCulture) * population;
            totalWeight += population;
        }
        return totalWeight > 0d && weightedShare / totalWeight >= CulturalAssimilationCompletionShare;
    }

    public const float CulturalAssimilationDutyPush = 5f;
    public const float CulturalAssimilationCompletionShare = 65f;
    public const int CulturalAssimilationDutyInfluenceCost = 50;
    public const int RegimeConversionInfluenceCost = 50;

    public static void SyncActorToCityMainCulture(Actor actor, City city, bool createIfMissing = true)
    {
        if (actor?.data == null || city?.data == null || actor.isRekt()) return;
        // 都护府（中央委派的异文化军府长官）不会因为治下城市的主流文化而被同化——
        // 无论城市文化如何，统治者本人的文化都跟其他道/军府长官一样由中央决定，保持不变。
        if ((actor == city.leader || actor == city.kingdom?.king) && IsProtectorateGeneralTitle(city.GetTitle()))
            return;
        string target = GetMainCulture(city);
        if (!IsValidCulture(target) || GetActorCulture(actor) == target) return;
        Culture nativeCulture = ResolveNativeCulture(target, actor, createIfMissing);
        if (nativeCulture != null) actor.setCulture(nativeCulture);
    }

    /// <summary>
    /// 只读查找：如果这个模组文化 key 已经关联过一个真正的原版 Culture 对象——
    /// SetRealmCulture/SyncActorToCityMainCulture 等既有逻辑本来就会在王国/演员的
    /// 文化被设成某个模组文化时，顺手在 World.world.cultures 里创建/关联一个真正的
    /// 原版 Culture 实体——就把它找出来；找不到就返回 null。
    /// 特意不在这里创建新的 Culture 对象（createIfMissing 固定传 false）：调用方
    /// 目前是文化地图图层的常驻渲染回调（每帧都跑），不适合在那里触发有副作用的
    /// 世界状态变更，哪怕 ResolveNativeCulture 本身对同一个 key 是幂等的。
    /// </summary>
    public static Culture GetNativeCultureObject(string culture)
    {
        return ResolveNativeCulture(culture, null, createIfMissing: false);
    }

    private static Culture ResolveNativeCulture(string target, Actor founder, bool createIfMissing = true)
    {
        if (!IsValidCulture(target) || World.world?.cultures == null) return null;
        foreach (Culture culture in World.world.cultures)
        {
            if (CulturePatch.GetInjectedCultureName(culture) == target) return culture;
        }
        if (!createIfMissing || founder?.data == null || founder.isRekt() || founder.city?.data == null) return null;
        Culture created = World.world.cultures.newCulture(founder, true);
        if (created != null) CulturePatch.insertCultureTemplate(created, target);
        return created;
    }

    public static void RefreshCultureDependents(KingdomTitle title)
    {
        if (title == null) return;
        title.recalculate();
        string culture = GetEffectiveTitleCulture(title);
        foreach (Kingdom kingdom in World.world.kingdoms)
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.GetMainTitle() != title) continue;
            SetRealmCulture(kingdom, culture, updatePoliticalSystem: false);
        }
        EmpireCraftNamePlateLibrary.RequestRefresh();
    }

    private static bool HasValidShares(Dictionary<string, float> shares)
    {
        return shares != null && shares.Any(pair => IsValidCulture(pair.Key) && pair.Value > Epsilon);
    }

    public static void NormalizeShares(Dictionary<string, float> shares)
    {
        CultureShareRules.Normalize(shares, IsValidCulture);
    }
}

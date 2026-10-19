using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

public static partial class CompositeEmpireService
{
    // 改用制度文化的国号：融合度到 60(以前 70——统治族群还在时融合度封顶 69，国号永远改不了)
    public const int CulturalNameIntegrationThreshold = 60;

    private sealed class CulturalNameCandidate
    {
        public string Name;
        public string Source;
    }

    public sealed class AdoptionStatus
    {
        public bool IsEligible;
        public string RulingCulture = "";
        public string InstitutionalCulture = "";
        public RegimeType InstitutionalRegime = RegimeType.Feudalism;
        public EmpireCore InstitutionalCore;
        public int ControlledTitles;
        public int TotalTitles;
        public int Mandate;
        // 统治族群是游牧，或被征服文化比本族先进(见 MoreAdvanced)：才走得上复合帝国这条路
        public bool RulerIsNomadic;
        public bool Applicable => RulerIsNomadic || HasInstitutionalTarget && MoreAdvanced(InstitutionalCulture, RulingCulture);

        public bool HasInstitutionalTarget =>
            CultureService.IsValidCulture(InstitutionalCulture) && TotalTitles > 0;
        public bool HasRequiredControl => HasInstitutionalTarget && ControlledTitles * 2 >= TotalTitles;
        public bool HasRequiredMandate => Mandate >= 40;
        public bool HasDistinctCultures =>
            CultureService.IsValidCulture(RulingCulture) &&
            CultureService.IsValidCulture(InstitutionalCulture) &&
            !string.Equals(RulingCulture, InstitutionalCulture, StringComparison.Ordinal);
    }

    // 多元帝国：完成整合的复合帝国，或治下四分之一以上的城是异族(不同天下的文化)——逃人法、游牧立宪用
    // (以前只认复合帝国，而复合帝国的制度按被征服文化的线走，游牧线上这两样永远到不了)
    public static bool IsMultiethnic(Empire empire)
    {
        if (empire?.data == null) return false;
        if (empire.data.composite_integration_stage >= CompositeEmpireIntegrationStage.CompositeEmpire) return true;
        string ruling = GetRulingCulture(empire);
        if (!CultureService.IsValidCulture(ruling)) return false;
        int total = 0, foreign = 0;
        foreach (Kingdom kingdom in empire.kingdoms_list ?? new List<Kingdom>())
        {
            if (kingdom?.cities == null || kingdom.isRekt()) continue;
            foreach (City city in kingdom.cities)
            {
                string main = city?.data == null || city.isRekt() ? "" : CultureService.GetMainCulture(city, initialize: false);
                if (!CultureService.IsValidCulture(main)) continue;
                total++;
                if (!CultureService.SameKin(main, ruling)) foreign++;
            }
        }
        return total > 0 && foreign * 4 >= total;
    }

    public static bool IsComposite(Empire empire)
    {
        return empire?.data != null &&
               empire.data.composite_integration_stage != CompositeEmpireIntegrationStage.None;
    }

    // 治理路线(用户 2026-10-19 选"三条路线由局势定")，每年按局势重定(见 UpdateRoute)：
    //   · 分治：本族旧地占三成五以上的城(或本族人口三成五以上)——朝廷照本族旧制，属地之民以其本制治之，
    //     融合度不过 49、不改国号，不能易俗改制；
    //   · 从俗：本族旧地不到一成五，且属地文化更先进或融入派当权(本俗反扑期间不从俗)——融合每年多一点，
    //     统治族群在也能融完，可以易俗改制；
    //   · 兼用：其余——朝廷用属地制度，本族保留旧俗与军事传统，统治族群在时融合度不过 79。
    // 已在某条路上的，要越过阈值五个百分点才改(免得年年来回)
    public const int RouteDual = 1, RouteJoint = 2, RouteAdopt = 3;

    public static int GetRoute(Empire empire)
    {
        if (!IsComposite(empire)) return 0;
        int route = empire.data.composite_route;
        return route is RouteDual or RouteJoint or RouteAdopt ? route : RouteJoint;
    }

    // 中央朝廷照哪种文化的制度：分治照本族旧制，兼用、从俗照属地制度
    public static string GetCourtCulture(Empire empire) =>
        GetRoute(empire) == RouteDual ? GetRulingCulture(empire) : GetInstitutionalCulture(empire);

    public static string GetRouteName(Empire empire) => LM.Get("composite_route_" + GetRoute(empire));

    public static string GetRouteDetail(Empire empire, int route)
    {
        string ruling = GetRulingCulture(empire), institutional = GetInstitutionalCulture(empire);
        return string.Format(LM.Get("composite_route_detail_" + route),
            CultureService.IsValidCulture(ruling) ? ruling.GetCultureTranslate() : ruling,
            CultureService.IsValidCulture(institutional) ? institutional.GetCultureTranslate() : institutional);
    }

    private static void UpdateRoute(Empire empire, string ruling, string institutional, float rulingShare)
    {
        int current = empire.data.composite_route;
        float homeland = Math.Max(rulingShare, HomelandCityShare(empire, ruling));
        int next;
        if (homeland >= (current == RouteDual ? 0.30f : 0.35f)) next = RouteDual;
        else if (!NativistActive(empire) && DrawnToConquered(empire, ruling, institutional) &&
                 homeland < (current == RouteAdopt ? 0.20f : 0.15f)) next = RouteAdopt;
        else next = RouteJoint;
        if (next == current) return;
        empire.data.composite_route = next;
        empire.data.composite_route_since = World.world?.getCurWorldTime() ?? -1d;
        SynchronizeRegionalInstitutions(empire);
        InstitutionSystem.ForgetRulingMajority(empire);
        InstitutionSystem.DropForeignReform(empire);
        string text = string.Format(LM.Get(current == 0 ? "composite_route_chosen_history" : "composite_route_changed_history"),
            empire.GetEmpireName(), LM.Get("composite_route_" + next), GetRouteDetail(empire, next));
        empire.RecordHistory(directContent: text, kingdomId: empire.CoreKingdom.id);
        if (current != 0) TranslateHelper.LogEventMessage(text, empire.CoreKingdom);
        LogService.LogInfo($"[EmpireCraft][征服王朝] {empire.GetEmpireName()} 治理路线 {current} → {next}(本族旧地 {homeland:P0})");
    }

    // 属地文化更先进，或融入派当权
    private static bool DrawnToConquered(Empire empire, string ruling, string institutional) =>
        MoreAdvanced(institutional, ruling) ||
        empire.CoreKingdom?.GetRegime()?.GetDominateFaction()?.Type == FactionType.融入;

    // 本族旧地：主流文化是统治族群文化的城占几成
    private static float HomelandCityShare(Empire empire, string ruling)
    {
        int total = 0, home = 0;
        foreach (Kingdom kingdom in empire?.kingdoms_list ?? new List<Kingdom>())
        {
            if (kingdom?.cities == null || kingdom.isRekt()) continue;
            foreach (City city in kingdom.cities)
            {
                if (city?.data == null || city.isRekt()) continue;
                total++;
                if (string.Equals(CultureService.GetMainCulture(city, initialize: false), ruling, StringComparison.Ordinal)) home++;
            }
        }
        return total > 0 ? (float)home / total : 0f;
    }

    public static string GetRulingCulture(Empire empire)
    {
        if (CultureService.IsValidCulture(empire?.data?.ruling_culture)) return empire.data.ruling_culture;
        return CultureService.GetRealmCulture(empire?.CoreKingdom);
    }

    public static string GetInstitutionalCulture(Empire empire)
    {
        if (CultureService.IsValidCulture(empire?.data?.institutional_culture))
            return empire.data.institutional_culture;
        return CultureService.GetEmpireDefaultCulture(empire);
    }

    public static bool UsesMilitaryTradition(Empire empire, RegimeType regimeType)
    {
        return IsComposite(empire) && Enum.TryParse(empire.data.military_tradition, true,
            out RegimeType saved) && saved == regimeType;
    }

    public static string GetMilitaryTraditionName(Empire empire)
    {
        string value = empire?.data?.military_tradition;
        if (string.IsNullOrWhiteSpace(value)) value = empire?.CoreKingdom?.GetRegime()?.type.ToString();
        if (string.IsNullOrWhiteSpace(value)) return LM.Get("label_none");
        string key = $"regime_type_{value}";
        string localized = LM.Get(key);
        return string.IsNullOrWhiteSpace(localized) || string.Equals(localized, key, StringComparison.Ordinal)
            ? value
            : localized;
    }

    public static string GetInstitutionalRegimeName(Empire empire)
    {
        string culture = GetCourtCulture(empire);
        RegimeType regimeType = empire?.CoreKingdom?.GetRegime()?.type ?? RegimeType.Feudalism;
        if (AdoptableRegime(culture, out RegimeType adoptable)) regimeType = adoptable;
        return GetRegimeName(regimeType);
    }

    // 征服王朝采纳某文化的制度时换成的政体(复查 2026-10-17)：按这个文化制度树实际走到的(TryResolveCultureRegime)，
    // 解析不出才用文化配置——以前一律用配置里的静态政体：征服日本、印度、阿兹特克成了原始政体(一城一部落地拆)，
    // 征服"中国"成了共和，征服西方成了城邦被拆城。原始、城邦、现代政体一律不采纳
    public static bool AdoptableRegime(string culture, out RegimeType regimeType)
    {
        regimeType = RegimeType.Feudalism;
        if (!CultureService.IsValidCulture(culture)) return false;
        if (InstitutionSystem.TryResolveCultureRegime(culture, out RegimeType resolved)) regimeType = resolved;
        else if (OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting)) regimeType = setting.regime;
        else return false;
        return !RegimeManager.IsPrimitive(regimeType) && regimeType != RegimeType.ClassicalRepublic &&
               regimeType != RegimeType.Modern;
    }

    public static string GetRegimeName(RegimeType regimeType)
    {
        string key = $"regime_type_{regimeType}";
        string localized = LM.Get(key);
        return string.IsNullOrWhiteSpace(localized) || string.Equals(localized, key, StringComparison.Ordinal)
            ? regimeType.ToString()
            : localized;
    }

    public static string GetRegionalInstitutionSummary(Empire empire)
    {
        if (!IsComposite(empire) || empire.kingdoms_list == null) return LM.Get("label_none");
        var groups = empire.kingdoms_list
            .Where(member => member?.data != null && !member.isRekt() && member != empire.CoreKingdom)
            .Select(member => new
            {
                Culture = ResolveRegionalInstitutionCulture(member),
                Regime = member.GetRegime()?.type
            })
            .Where(item => CultureService.IsValidCulture(item.Culture) && item.Regime.HasValue)
            .GroupBy(item => new { item.Culture, Regime = item.Regime.Value })
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key.Culture, StringComparer.Ordinal)
            .ToList();
        if (groups.Count == 0) return LM.Get("label_none");

        List<string> entries = groups.Take(3)
            .Select(group => $"{group.Key.Culture.GetCultureTranslate()}→{GetRegimeName(group.Key.Regime)}×{group.Count()}")
            .ToList();
        if (groups.Count > 3) entries.Add($"+{groups.Count - 3}");
        return string.Join("\n", entries);
    }

    public static string GetStageName(Empire empire)
    {
        string key = $"composite_empire_stage_{empire?.data?.composite_integration_stage ?? CompositeEmpireIntegrationStage.None}";
        string localized = LM.Get(key);
        return string.IsNullOrWhiteSpace(localized) || string.Equals(localized, key, StringComparison.Ordinal)
            ? (empire?.data?.composite_integration_stage ?? CompositeEmpireIntegrationStage.None).ToString()
            : localized;
    }

    public static bool CanAdoptCentralInstitutions(Actor actor)
    {
        Kingdom kingdom = actor?.kingdom;
        if (actor == null || kingdom == null || kingdom.isRekt() || !actor.isKing() || kingdom.king != actor ||
            !kingdom.IsEmpire()) return false;
        Empire empire = kingdom.GetEmpire();
        return GetAdoptionStatus(empire).IsEligible;
    }

    public static AdoptionStatus GetAdoptionStatus(Empire empire)
    {
        AdoptionStatus status = new AdoptionStatus
        {
            Mandate = empire?.Mandate ?? 0
        };
        if (empire?.data == null || empire.IsArchived() || empire.isRekt() ||
            empire.CoreKingdom == null || empire.CoreKingdom.isRekt() || IsComposite(empire))
        {
            return status;
        }

        status.RulingCulture = ResolveConquestRulingCulture(empire);
        if (CultureService.IsValidCulture(status.RulingCulture))
        {
            empire.data.conquest_founder_culture = status.RulingCulture;
        }
        // 任何征服者都可采纳更先进的被征服文化的制度(用户 2026-10-19 选"可和平同化、留少数族群"；以前只有游牧)：
        // 游牧照旧；别的统治族群要治下有比本族先进的文化的城才往下找(省得每次扫全世界的法理)
        status.RulerIsNomadic = IsNomadicCulture(status.RulingCulture);
        if (!status.RulerIsNomadic && !HoldsMoreAdvancedCulture(empire, status.RulingCulture))
        {
            return status;
        }

        if (ModClass.KINGDOM_TITLE_MANAGER != null)
        {
            var titleGroups = ModClass.KINGDOM_TITLE_MANAGER
                .Where(title => title?.data != null && !title.isRekt())
                .Select(title => new
                {
                    Title = title,
                    Culture = CultureService.GetEffectiveTitleCulture(title)
                })
                .Where(item => CultureService.IsValidCulture(item.Culture))
                .GroupBy(item => item.Culture, StringComparer.Ordinal);

            foreach (var group in titleGroups)
            {
                ConsiderInstitutionalCandidate(status, empire, group.Key,
                    group.Select(item => item.Title).ToList());
            }
        }

        // 受着被征服文化那个天下的天命，也触发采纳(外族入主，用户 2026-10-19 选"可受天命、本族可光复")
        bool mandateTrigger = TianxiaSystem.MandateOf(empire.CoreKingdom) is EmpireCore lead &&
                              CultureService.SameKin(lead.default_culture, status.InstitutionalCulture);
        status.IsEligible = status.HasDistinctCultures && status.Applicable &&
                            (status.HasRequiredControl || mandateTrigger) &&
                            status.HasRequiredMandate;
        return status;
    }

    // 被征服文化比本族先进：科技时代更晚，或同一时代文明等级更高
    public static bool MoreAdvanced(string conquered, string ruling)
    {
        if (!CultureService.IsValidCulture(conquered) || !CultureService.IsValidCulture(ruling)) return false;
        if (TechnologySystem.IsEnabled)
        {
            int conqueredEra = TechnologySystem.GetEraTier(conquered), rulingEra = TechnologySystem.GetEraTier(ruling);
            if (conqueredEra != rulingEra) return conqueredEra > rulingEra;
        }
        return InstitutionSystem.GetCultureLevel(conquered) > InstitutionSystem.GetCultureLevel(ruling);
    }

    private static bool HoldsMoreAdvancedCulture(Empire empire, string ruling)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Kingdom kingdom in empire.kingdoms_list ?? new List<Kingdom>())
        {
            if (kingdom?.cities == null || kingdom.isRekt()) continue;
            foreach (City city in kingdom.cities)
            {
                string culture = city?.data == null || city.isRekt() ? "" : CultureService.GetMainCulture(city, initialize: false);
                if (!CultureService.IsValidCulture(culture) || string.Equals(culture, ruling, StringComparison.Ordinal) ||
                    !seen.Add(culture)) continue;
                if (MoreAdvanced(culture, ruling)) return true;
            }
        }
        return false;
    }

    private static void ConsiderInstitutionalCandidate(AdoptionStatus status, Empire empire, string culture,
        List<KingdomTitle> titles)
    {
        if (status == null || empire == null || titles == null || titles.Count == 0 ||
            !CultureService.IsValidCulture(culture) ||
            string.Equals(culture, status.RulingCulture, StringComparison.Ordinal) ||
            !AdoptableRegime(culture, out RegimeType adoptable) ||
            RegimeManager.regimes == null ||
            !RegimeManager.regimes.TryGetValue(adoptable, out Regime targetRegime) || targetRegime == null)
        {
            return;
        }

        int controlled = titles.Count(title => title.getCities().Any(city =>
            city != null && !city.isRekt() && city.kingdom?.GetEmpire() == empire));
        if (controlled <= 0) return;

        bool candidateEligible = controlled * 2 >= titles.Count;
        bool currentEligible = status.HasRequiredControl;
        bool replace = status.TotalTitles <= 0 || candidateEligible && !currentEligible;
        if (!replace && candidateEligible == currentEligible)
        {
            replace = candidateEligible
                ? controlled > status.ControlledTitles ||
                  controlled == status.ControlledTitles &&
                  controlled * status.TotalTitles > status.ControlledTitles * titles.Count
                : controlled * status.TotalTitles > status.ControlledTitles * titles.Count ||
                  controlled * status.TotalTitles == status.ControlledTitles * titles.Count &&
                  controlled > status.ControlledTitles;
        }
        if (!replace) return;

        status.InstitutionalCore = EmpireCoreManager.EmpireCores.Values.FirstOrDefault(core =>
            core != null && string.Equals(core.default_culture, culture, StringComparison.Ordinal));
        status.InstitutionalCulture = culture;
        status.InstitutionalRegime = adoptable;
        status.ControlledTitles = controlled;
        status.TotalTitles = titles.Count;
    }

    private static List<KingdomTitle> GetInstitutionalTitles(string culture)
    {
        if (!CultureService.IsValidCulture(culture) || ModClass.KINGDOM_TITLE_MANAGER == null)
            return new List<KingdomTitle>();
        return ModClass.KINGDOM_TITLE_MANAGER
            .Where(title => title?.data != null && !title.isRekt() &&
                            string.Equals(CultureService.GetEffectiveTitleCulture(title), culture,
                                StringComparison.Ordinal))
            .ToList();
    }

    private static string ResolveConquestRulingCulture(Empire empire)
    {
        if (CultureService.IsValidCulture(empire?.data?.conquest_founder_culture))
            return empire.data.conquest_founder_culture;
        if (CultureService.IsValidCulture(empire?.data?.ruling_culture))
            return empire.data.ruling_culture;

        long[] founderIds =
        {
            empire?.data?.dynasty_founder_actor_id ?? -1L,
            empire?.data?.founder_actor_id ?? -1L
        };
        foreach (long founderId in founderIds.Distinct())
        {
            if (founderId <= 0) continue;
            Actor founder = World.world?.units?.get(founderId);
            string founderCulture = CultureService.GetActorCulture(founder);
            if (CultureService.IsValidCulture(founderCulture)) return founderCulture;
        }

        string emperorCulture = CultureService.GetActorCulture(empire?.Emperor);
        if (IsNomadicCulture(emperorCulture)) return emperorCulture;

        string survivingNomadicCulture = EmpirePopulation.Enumerate(empire?.kingdoms_hashset)
            .Where(actor => actor != null && !actor.isRekt() && actor.isAlive())
            .Select(CultureService.GetActorCulture)
            .Where(IsNomadicCulture)
            .GroupBy(culture => culture)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault();
        if (CultureService.IsValidCulture(survivingNomadicCulture)) return survivingNomadicCulture;

        if (CultureService.IsValidCulture(emperorCulture)) return emperorCulture;
        return CultureService.GetRealmCulture(empire?.CoreKingdom);
    }

    public static bool IsNomadicCulture(string culture)
    {
        return CultureService.IsValidCulture(culture) &&
               OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting) &&
               (setting.regime == RegimeType.YouMu || culture.IsNomadCulture());
    }

    public static bool TryGetCultureRegime(string culture, out RegimeType regimeType, out Regime regime)
    {
        regimeType = RegimeType.Feudalism;
        regime = null;
        if (!AdoptableRegime(culture, out RegimeType adoptable) || RegimeManager.regimes == null ||
            !RegimeManager.regimes.TryGetValue(adoptable, out regime) || regime == null)
        {
            return false;
        }
        regimeType = adoptable;
        return true;
    }

    public static string ResolveRegionalInstitutionCulture(Kingdom member)
    {
        if (member?.data == null || member.isRekt()) return "";
        List<City> cities = (member.cities ?? new List<City>())
            .Where(city => city?.data != null && !city.isRekt() && city.kingdom == member)
            .ToList();
        string capitalCulture = member.capital?.GetTitle() is KingdomTitle capitalTitle
            ? CultureService.GetEffectiveTitleCulture(capitalTitle)
            : "";
        string majorityCulture = cities
            .Select(city => city.GetTitle())
            .Where(title => title?.data != null && !title.isRekt())
            .Select(CultureService.GetEffectiveTitleCulture)
            .Where(CultureService.IsValidCulture)
            .GroupBy(culture => culture, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => string.Equals(group.Key, capitalCulture, StringComparison.Ordinal))
            .Select(group => group.Key)
            .FirstOrDefault();
        if (CultureService.IsValidCulture(majorityCulture)) return majorityCulture;
        if (CultureService.IsValidCulture(capitalCulture)) return capitalCulture;

        long administrativeTitleId = member.GetOrCreate().AdministrativeTitle;
        KingdomTitle administrativeTitle = ModClass.KINGDOM_TITLE_MANAGER?.get(administrativeTitleId);
        string administrativeCulture = CultureService.GetEffectiveTitleCulture(administrativeTitle);
        if (CultureService.IsValidCulture(administrativeCulture)) return administrativeCulture;
        return CultureService.GetRealmCulture(member);
    }

    public static bool SynchronizeRegionalInstitution(Empire empire, Kingdom member)
    {
        if (!IsComposite(empire) || member?.data == null || member.isRekt() || member.GetEmpire() != empire)
            return false;
        if (RepublicSystem.IsRegimeLocked(member)) return false;

        string culture = member == empire.CoreKingdom
            ? GetCourtCulture(empire)
            : ResolveRegionalInstitutionCulture(member);
        if (!TryGetCultureRegime(culture, out RegimeType targetType, out _)) return false;
        if (member.GetRegime()?.type == targetType) return false;

        KingdomTitle delegatedTitle = null;
        if (member.GetMainTitle() == null)
        {
            delegatedTitle = ModClass.KINGDOM_TITLE_MANAGER?.get(member.GetOrCreate().AdministrativeTitle);
        }

        member.SetRegimeType(targetType);
        member.LoadRegime();
        Regime loaded = member.GetRegime();
        if (loaded?.type != targetType) return false;

        if (member == empire.CoreKingdom)
        {
            loaded.SetLeaderSelectMethod(LeaderSelectMethod.Succession);
        }
        else if (targetType == RegimeType.LvLing && member.GetMainTitle() == null)
        {
            loaded.SetAllowDiplomacy(false);
            loaded.SetLeaderSelectMethod(LeaderSelectMethod.Exam);
        }
        else if (delegatedTitle != null && !delegatedTitle.isRekt() && member.hasKing())
        {
            delegatedTitle.EndJurisdiction(member, KingdomTitle.JurisdictionAdministration);
            member.GetOrCreate().AdministrativeTitle = -1L;
            member.king.AddOwnedTitle(delegatedTitle);
            member.SetMainTitle(delegatedTitle);
            member.ReconcileMainTitle(new[] { delegatedTitle });
        }

        EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(member);
        return true;
    }

    public static int SynchronizeRegionalInstitutions(Empire empire)
    {
        if (!IsComposite(empire) || empire.kingdoms_list == null) return 0;
        int changed = 0;
        foreach (Kingdom member in empire.kingdoms_list
                     .Where(value => value?.data != null && !value.isRekt()).ToList())
        {
            if (SynchronizeRegionalInstitution(empire, member)) changed++;
        }
        if (changed > 0)
        {
            empire.data.centerOffice ??= new CenterOffice();
            empire.data.centerOffice.Init(empire.CoreKingdom);
        }
        return changed;
    }

    public static bool AdoptCentralInstitutions(Actor actor)
    {
        if (!CanAdoptCentralInstitutions(actor)) return false;
        Empire empire = actor.kingdom.GetEmpire();
        AdoptionStatus status = GetAdoptionStatus(empire);
        if (!status.IsEligible) return false;
        string rulingCulture = status.RulingCulture;
        string institutionalCulture = status.InstitutionalCulture;
        Regime previousRegimeObject = empire.CoreKingdom.GetRegime();
        RegimeType previousRegime = previousRegimeObject?.type ?? RegimeType.YouMu;

        // The domestic apparatus changes below, but the dynasty's outward title remains the one
        // it used at conquest. This keeps a nomadic khaganate from being renamed as a Chinese-style
        // empire merely because it adopted the central bureaucracy.
        if (string.IsNullOrWhiteSpace(empire.data.empire_type_key))
            empire.data.empire_type_key = OverallHelperFunc.ResolveEmpireTypeKey(previousRegimeObject,
                empire.CoreKingdom);

        empire.data.conquest_founder_culture = rulingCulture;
        empire.data.ruling_culture = rulingCulture;
        empire.data.institutional_culture = institutionalCulture;
        empire.data.external_identity_culture = rulingCulture;
        empire.data.military_tradition = previousRegime.ToString();
        empire.data.composite_integration = CompositeEmpireRules.InitialIntegration;
        empire.data.composite_integration_stage = CompositeEmpireIntegrationStage.DualAdministration;
        empire.data.central_plains_legitimacy = 60;
        empire.data.ruling_tradition_legitimacy = 75;
        empire.data.composite_identity_adopted_timestamp = World.world.getCurWorldTime();
        empire.data.last_composite_identity_update_timestamp = empire.data.composite_identity_adopted_timestamp;
        empire.data.composite_route = 0;
        PopulationShares(empire, rulingCulture, institutionalCulture, out float rulingShare, out _);
        UpdateRoute(empire, rulingCulture, institutionalCulture, rulingShare);

        SynchronizeRegionalInstitutions(empire);
        empire.data.centerOffice ??= new CenterOffice();
        empire.data.centerOffice.Init(empire.CoreKingdom);
        SynchronizeMilitaryTraditionClaims(empire);
        empire.CoreKingdom.SystemChange();
        empire.AutoEnfeoff();
        SynchronizeRegionalInstitutions(empire);
        TranslateHelper.LogCompositeEmpireAdopted(empire, rulingCulture, institutionalCulture);
        return true;
    }

    public static void Update(Empire empire)
    {
        if (!IsComposite(empire) || empire.CoreKingdom == null || empire.CoreKingdom.isRekt() || World.world == null)
            return;
        SynchronizeMilitaryTraditionClaims(empire);
        double now = World.world.getCurWorldTime();
        if (empire.data.last_composite_identity_update_timestamp < 0)
        {
            empire.data.last_composite_identity_update_timestamp = now;
            return;
        }
        if (Date.getYearsSince(empire.data.last_composite_identity_update_timestamp) < 1) return;
        empire.data.last_composite_identity_update_timestamp = now;
        SynchronizeRegionalInstitutions(empire);
        SynchronizeMilitaryTraditionClaims(empire);

        string rulingCulture = GetRulingCulture(empire);
        string institutionalCulture = GetInstitutionalCulture(empire);
        PopulationShares(empire, rulingCulture, institutionalCulture, out float rulingShare, out float institutionalShare);
        bool integrated = string.Equals(rulingCulture, institutionalCulture, StringComparison.Ordinal);
        if (!integrated) UpdateRoute(empire, rulingCulture, institutionalCulture, rulingShare);
        int route = GetRoute(empire);
        string emperorCulture = CultureService.GetActorCulture(empire.Emperor);
        bool emperorUsesRuling = string.Equals(emperorCulture, rulingCulture, StringComparison.Ordinal);
        bool emperorUsesInstitution = string.Equals(emperorCulture, institutionalCulture, StringComparison.Ordinal);

        List<KingdomTitle> titles = GetInstitutionalTitles(institutionalCulture);
        int controlled = titles.Count(title => title.getCities().Any(city =>
            city != null && !city.isRekt() && city.kingdom?.GetEmpire() == empire));
        float controlledShare = titles.Count > 0 ? (float)controlled / titles.Count : 0f;
        // 北归(见 CompositeEmpireService.Conquest)：属地丢得差不多了，退回本族旧地
        if (!integrated && TryNorthernRetreat(empire, rulingCulture, institutionalCulture, controlledShare)) return;
        bool adoptable = AdoptableRegime(institutionalCulture, out RegimeType institutionalType);
        // 分治：属地以其本制治之，朝廷照旧制——两边的正统都算照顾到了
        bool institutionalRegime = route == RouteDual || adoptable && empire.CoreKingdom.GetRegime()?.type == institutionalType;
        bool preservesMilitary = route == RouteDual || !adoptable ||
            !string.Equals(empire.data.military_tradition, institutionalType.ToString(), StringComparison.OrdinalIgnoreCase);

        empire.data.central_plains_legitimacy = Clamp100(empire.data.central_plains_legitimacy +
            CompositeEmpireRules.GetCentralLegitimacyDelta(institutionalRegime, controlledShare, institutionalShare));
        empire.data.ruling_tradition_legitimacy = Clamp100(empire.data.ruling_tradition_legitimacy +
            CompositeEmpireRules.GetRulingLegitimacyDelta(emperorUsesRuling, rulingShare, preservesMilitary));

        int delta = CompositeEmpireRules.GetAnnualIntegrationDelta(institutionalShare, rulingShare,
            emperorUsesInstitution, emperorUsesRuling);
        if (route == RouteAdopt) delta += 1;
        else if (route == RouteDual) delta = Math.Min(delta, 1);
        // 融合完成后统治文化就是制度文化，不再有"保留旧俗的统治族群"压着融合度
        // (以前完成融合的王朝次年又被压回 69、退回"复合帝国")；从俗的王朝统治族群自己在改俗，也不压
        bool rulingEliteStillExists = !integrated && route != RouteAdopt && (emperorUsesRuling || rulingShare >= 0.08f);
        int nextIntegration = Math.Max(1, CompositeEmpireRules.CapIntegration(
            empire.data.composite_integration + delta, rulingEliteStillExists, !integrated && route == RouteDual));
        CompositeEmpireIntegrationStage oldStage = empire.data.composite_integration_stage;
        empire.data.composite_integration = nextIntegration;
        empire.data.composite_integration_stage = CompositeEmpireRules.GetStage(nextIntegration);
        TryAdoptInstitutionalCultureName(empire);

        if (empire.data.central_plains_legitimacy < 20 || empire.data.ruling_tradition_legitimacy < 20)
            empire.AddMandate(-2);
        else if (empire.data.central_plains_legitimacy >= 60 && empire.data.ruling_tradition_legitimacy >= 60)
            empire.AddMandate(1);

        if (oldStage != empire.data.composite_integration_stage)
            TranslateHelper.LogCompositeEmpireStageChanged(empire, oldStage, empire.data.composite_integration_stage);
        if (empire.data.composite_integration_stage == CompositeEmpireIntegrationStage.IntegratedDynasty)
            CompleteIntegration(empire, institutionalCulture);
    }

    // 统治族群、制度文化各占治下人口的几成：按各城的文化构成乘城中人口(含无小人模式的背景人口)。
    // 以前数实体小人——无小人模式下几乎没有小人，两个占比都是 0，融合只看皇帝本人的文化
    public static void PopulationShares(Empire empire, string rulingCulture, string institutionalCulture,
        out float rulingShare, out float institutionalShare)
    {
        double total = 0d, ruling = 0d, institutional = 0d;
        foreach (Kingdom kingdom in empire?.kingdoms_list ?? new List<Kingdom>())
        {
            if (kingdom?.cities == null || kingdom.isRekt()) continue;
            foreach (City city in kingdom.cities)
            {
                if (city?.data == null || city.isRekt()) continue;
                double people = Math.Max(1, city.CountLivingPopulation());
                Dictionary<string, float> shares = CultureService.GetCityCultureShares(city, initialize: false);
                float sum = shares.Values.Sum();
                total += people;
                if (sum <= 0f)
                {
                    string main = CultureService.GetMainCulture(city, initialize: false);
                    if (string.Equals(main, rulingCulture, StringComparison.Ordinal)) ruling += people;
                    if (string.Equals(main, institutionalCulture, StringComparison.Ordinal)) institutional += people;
                    continue;
                }
                if (shares.TryGetValue(rulingCulture ?? "", out float r)) ruling += people * r / sum;
                if (shares.TryGetValue(institutionalCulture ?? "", out float i)) institutional += people * i / sum;
            }
        }
        rulingShare = total > 0d ? (float)(ruling / total) : 0f;
        institutionalShare = total > 0d ? (float)(institutional / total) : 0f;
    }

    public static bool TryAdoptInstitutionalCultureName(Empire empire)
    {
        if (!IsComposite(empire) || empire.data.composite_cultural_name_adopted || GetRoute(empire) == RouteDual ||
            empire.data.composite_integration < CulturalNameIntegrationThreshold ||
            empire.CoreKingdom?.data == null || empire.CoreKingdom.isRekt() ||
            empire.CoreKingdom.HasCustomCountryNaming())
        {
            return false;
        }

        string culture = GetInstitutionalCulture(empire);
        if (!CultureService.IsValidCulture(culture)) return false;
        CulturalNameCandidate candidate = SelectInstitutionalCultureName(empire, culture);
        if (candidate == null || string.IsNullOrWhiteSpace(candidate.Name)) return false;

        string oldName = empire.GetEmpireName();
        empire.SetEmpireName(candidate.Name, preserveEmpireType: true);
        string adoptedName = empire.GetEmpireName();
        if (string.IsNullOrWhiteSpace(adoptedName)) return false;

        empire.data.composite_cultural_name_adopted = true;
        empire.data.composite_cultural_name = adoptedName;
        empire.data.composite_cultural_name_culture = culture;
        empire.data.composite_cultural_name_source = candidate.Source;
        empire.data.composite_cultural_name_adopted_timestamp = World.world?.getCurWorldTime() ?? -1L;
        if (!string.Equals(oldName, adoptedName, StringComparison.Ordinal))
            TranslateHelper.LogCompositeEmpireCulturalNameAdopted(empire, adoptedName, culture);
        return true;
    }

    private static CulturalNameCandidate SelectInstitutionalCultureName(Empire empire, string culture)
    {
        KingdomTitle mainTitle = empire.CoreKingdom.GetMainTitle();
        string mainHistoricName = CultureService.GetTitleCulturalName(mainTitle, culture);
        if (!string.IsNullOrWhiteSpace(mainHistoricName))
            return NewNameCandidate(mainHistoricName, "main_title_history");

        City seat = empire.Emperor?.city;
        if (seat == null || seat.isRekt()) seat = empire.CoreKingdom.capital;

        List<KingdomTitle> culturalCoreTitles = EmpireCoreManager.EmpireCores.Values
            .Where(core => core != null &&
                           string.Equals(core.default_culture, culture, StringComparison.Ordinal))
            .SelectMany(EmpireCoreManager.GetTitles)
            .Where(title => title?.data != null && !title.isRekt())
            .Distinct()
            .OrderByDescending(title => IsTitleControlledByEmpire(title, empire))
            .ThenBy(title => DistanceFromSeat(seat, title.title_capital))
            .ThenBy(title => title.id)
            .ToList();
        CulturalNameCandidate coreName = SelectTitleName(culturalCoreTitles, culture, "institutional_core");
        if (coreName != null) return coreName;

        List<KingdomTitle> nearbyTitles = (empire.kingdoms_list ?? new List<Kingdom>())
            .Where(kingdom => kingdom?.data != null && !kingdom.isRekt())
            .SelectMany(kingdom => kingdom.GetControlledTitles())
            .Where(title => title?.data != null && !title.isRekt() &&
                            string.Equals(CultureService.GetEffectiveTitleCulture(title), culture,
                                StringComparison.Ordinal))
            .Distinct()
            .OrderBy(title => DistanceFromSeat(seat, title.title_capital))
            .ThenBy(title => title.id)
            .ToList();
        CulturalNameCandidate nearbyTitleName = SelectTitleName(nearbyTitles, culture, "nearby_title");
        if (nearbyTitleName != null) return nearbyTitleName;

        City nearbyCity = (empire.kingdoms_list ?? new List<Kingdom>())
            .Where(kingdom => kingdom?.data != null && !kingdom.isRekt())
            .SelectMany(kingdom => kingdom.cities ?? new List<City>())
            .Where(city => city?.data != null && !city.isRekt() &&
                           (string.Equals(CultureService.GetMainCulture(city), culture, StringComparison.Ordinal) ||
                            string.Equals(CultureService.GetEffectiveTitleCulture(city.GetTitle()), culture,
                                StringComparison.Ordinal)))
            .OrderBy(city => DistanceFromSeat(seat, city))
            .ThenBy(city => city.id)
            .FirstOrDefault();
        string nearbyCityName = nearbyCity?.GetCityName();
        if (!string.IsNullOrWhiteSpace(nearbyCityName))
            return NewNameCandidate(nearbyCityName, "nearby_place");

        string seatName = seat?.GetCityName();
        return string.IsNullOrWhiteSpace(seatName) ? null : NewNameCandidate(seatName, "ruling_seat");
    }

    private static CulturalNameCandidate SelectTitleName(IEnumerable<KingdomTitle> titles, string culture,
        string source)
    {
        foreach (KingdomTitle title in titles)
        {
            string name = CultureService.GetTitleCulturalName(title, culture);
            if (string.IsNullOrWhiteSpace(name) &&
                string.Equals(CultureService.GetEffectiveTitleCulture(title), culture, StringComparison.Ordinal))
            {
                name = title.data.name;
            }
            if (!string.IsNullOrWhiteSpace(name)) return NewNameCandidate(name, source);
        }
        return null;
    }

    private static CulturalNameCandidate NewNameCandidate(string name, string source)
    {
        return new CulturalNameCandidate { Name = name.Trim(), Source = source };
    }

    private static bool IsTitleControlledByEmpire(KingdomTitle title, Empire empire)
    {
        return title.getCities().Any(city => city?.data != null && !city.isRekt() &&
                                             city.kingdom?.GetEmpire() == empire);
    }

    private static float DistanceFromSeat(City seat, City target)
    {
        if (seat == null || seat.isRekt() || target == null || target.isRekt()) return float.MaxValue;
        return UnityEngine.Vector2.Distance(seat.city_center, target.city_center);
    }

    // 易俗改制(见 CultureReformSystem)：君主改从制度文化之俗，融合一步到位
    public static void CompleteByReform(Empire empire)
    {
        if (!IsComposite(empire)) return;
        string institutionalCulture = GetInstitutionalCulture(empire);
        CompositeEmpireIntegrationStage oldStage = empire.data.composite_integration_stage;
        empire.data.composite_integration = Math.Max(empire.data.composite_integration, CompositeEmpireRules.IntegratedThreshold);
        empire.data.composite_integration_stage = CompositeEmpireRules.GetStage(empire.data.composite_integration);
        TryAdoptInstitutionalCultureName(empire);
        if (oldStage != empire.data.composite_integration_stage)
            TranslateHelper.LogCompositeEmpireStageChanged(empire, oldStage, empire.data.composite_integration_stage);
        CompleteIntegration(empire, institutionalCulture);
    }

    private static void CompleteIntegration(Empire empire, string institutionalCulture)
    {
        if (!CultureService.IsValidCulture(institutionalCulture) ||
            string.Equals(empire.data.ruling_culture, institutionalCulture, StringComparison.Ordinal)) return;
        string oldRuling = GetRulingCulture(empire);
        empire.data.ruling_culture = institutionalCulture;
        empire.data.conquest_founder_culture = institutionalCulture;
        empire.data.composite_route = RouteAdopt;
        empire.data.external_identity_culture = institutionalCulture;
        if (AdoptableRegime(institutionalCulture, out RegimeType institutionalType))
            empire.data.military_tradition = institutionalType.ToString();
        CultureService.SetRealmCulture(empire.CoreKingdom, institutionalCulture, updatePoliticalSystem: false);
        // 帝国核心的默认文化(没锁定、该文化此刻没有别的正统帝国时)也换过来，国号类别按现行政体重取：
        // 以前融合完成后核心仍是游牧文化(天下、同化目标都按旧文化)，对外仍称"汗国"
        EmpireCore core = EmpireCoreManager.Get(empire);
        if (core != null && !core.culture_locked && CultureService.GetActiveEmpireForCulture(institutionalCulture, empire) == null)
            core.default_culture = institutionalCulture;
        empire.data.empire_type_key = "";
        InstitutionSystem.ForgetRulingMajority(empire);
        SynchronizeMilitaryTraditionClaims(empire);
        // 旧部不服改俗，据地起兵(见 CompositeEmpireService.Conquest)
        OldGuardRevolt(empire, oldRuling, institutionalCulture);
    }

    // 征服王朝随统治族群带来的诉求：游牧扩张挂在攘夷、融入派上(保留游牧军事传统时)；
    // 国语骑射挂在攘夷、同化派上(统治族群游牧、尚未融完时)。没有这些派系的挂在当权派上
    private static void SynchronizeMilitaryTraditionClaims(Empire empire)
    {
        var factions = empire?.CoreKingdom?.GetRegime()?.GetPlayerFactions();
        if (factions == null) return;
        SyncClaim(empire, factions, TemporaryFactionType.游牧扩张, UsesMilitaryTradition(empire, RegimeType.YouMu),
            FactionType.攘夷, FactionType.融入);
        SyncClaim(empire, factions, TemporaryFactionType.国语骑射, NativistClaimApplies(empire),
            FactionType.攘夷, FactionType.同化);
    }

    private static void SyncClaim(Empire empire, List<FixedFaction> factions, TemporaryFactionType type, bool wanted,
        params FactionType[] hostTypes)
    {
        List<FixedFaction> hosts = factions.Where(f => f != null && hostTypes.Contains(f.Type)).ToList();
        if (hosts.Count == 0 && factions.FirstOrDefault(f => f?.Force == true) is FixedFaction dominate)
            hosts.Add(dominate);
        foreach (FixedFaction faction in factions.Where(f => f != null))
        {
            faction.TemporaryFactionTypesRecord ??= new List<TemporaryFactionType>();
            faction.TemporaryFactions ??= new List<TemporaryFaction>();
            TemporaryFaction existing = faction.TemporaryFactions.FirstOrDefault(tf => tf != null && tf.type == type);
            if (wanted && hosts.Contains(faction))
            {
                if (!faction.TemporaryFactionTypesRecord.Contains(type))
                    faction.TemporaryFactionTypesRecord.Add(type);
                if (existing == null && FactionManager.Config?.StoredTemporaryFaction != null &&
                    FactionManager.Config.StoredTemporaryFaction.TryGetValue(type, out TemporaryFaction prototype))
                {
                    TemporaryFaction claim = prototype.Clone(faction);
                    claim.Init(faction);
                    claim.SetEmpire(empire);
                    faction.TemporaryFactions.Add(claim);
                }
            }
            else if (existing?.IsStarted() != true)
            {
                faction.TemporaryFactionTypesRecord.Remove(type);
                faction.TemporaryFactions.RemoveAll(tf => tf != null && tf.type == type);
            }
        }
    }

    private static int Clamp100(int value)
    {
        return Math.Max(0, Math.Min(100, value));
    }
}

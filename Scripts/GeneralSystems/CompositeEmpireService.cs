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
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

public static class CompositeEmpireService
{
    public const int CulturalNameIntegrationThreshold = 70;

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

        public bool HasInstitutionalTarget =>
            CultureService.IsValidCulture(InstitutionalCulture) && TotalTitles > 0;
        public bool HasRequiredControl => HasInstitutionalTarget && ControlledTitles * 2 >= TotalTitles;
        public bool HasRequiredMandate => Mandate >= 40;
        public bool HasDistinctCultures =>
            CultureService.IsValidCulture(RulingCulture) &&
            CultureService.IsValidCulture(InstitutionalCulture) &&
            !string.Equals(RulingCulture, InstitutionalCulture, StringComparison.Ordinal);
    }

    public static bool IsComposite(Empire empire)
    {
        return empire?.data != null &&
               empire.data.composite_integration_stage != CompositeEmpireIntegrationStage.None;
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
        string culture = GetInstitutionalCulture(empire);
        RegimeType regimeType = empire?.CoreKingdom?.GetRegime()?.type ?? RegimeType.Feudalism;
        if (OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting))
            regimeType = setting.regime;
        return GetRegimeName(regimeType);
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
        if (!OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(status.RulingCulture, out Setting rulingSetting) ||
            rulingSetting.regime != RegimeType.YouMu)
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

        status.IsEligible = status.HasDistinctCultures &&
                            status.HasRequiredControl &&
                            status.HasRequiredMandate;
        return status;
    }

    private static void ConsiderInstitutionalCandidate(AdoptionStatus status, Empire empire, string culture,
        List<KingdomTitle> titles)
    {
        if (status == null || empire == null || titles == null || titles.Count == 0 ||
            !CultureService.IsValidCulture(culture) ||
            string.Equals(culture, status.RulingCulture, StringComparison.Ordinal) ||
            !OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting) ||
            RegimeManager.regimes == null ||
            !RegimeManager.regimes.TryGetValue(setting.regime, out Regime targetRegime) || targetRegime == null)
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
        status.InstitutionalRegime = setting.regime;
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

    private static bool IsNomadicCulture(string culture)
    {
        return CultureService.IsValidCulture(culture) &&
               OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting) &&
               setting.regime == RegimeType.YouMu;
    }

    public static bool TryGetCultureRegime(string culture, out RegimeType regimeType, out Regime regime)
    {
        regimeType = RegimeType.Feudalism;
        regime = null;
        if (!CultureService.IsValidCulture(culture) ||
            !OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting) ||
            RegimeManager.regimes == null ||
            !RegimeManager.regimes.TryGetValue(setting.regime, out regime) || regime == null)
        {
            return false;
        }
        regimeType = setting.regime;
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

        string culture = member == empire.CoreKingdom
            ? GetInstitutionalCulture(empire)
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
        int total = 0;
        int ruling = 0;
        int institutional = 0;
        foreach (Actor actor in EmpirePopulation.Enumerate(empire.kingdoms_hashset))
        {
            if (actor == null || actor.isRekt() || !actor.isAlive()) continue;
            total++;
            string culture = CultureService.GetActorCulture(actor);
            if (string.Equals(culture, rulingCulture, StringComparison.Ordinal)) ruling++;
            if (string.Equals(culture, institutionalCulture, StringComparison.Ordinal)) institutional++;
        }
        float rulingShare = total > 0 ? (float)ruling / total : 0f;
        float institutionalShare = total > 0 ? (float)institutional / total : 0f;
        string emperorCulture = CultureService.GetActorCulture(empire.Emperor);
        bool emperorUsesRuling = string.Equals(emperorCulture, rulingCulture, StringComparison.Ordinal);
        bool emperorUsesInstitution = string.Equals(emperorCulture, institutionalCulture, StringComparison.Ordinal);

        List<KingdomTitle> titles = GetInstitutionalTitles(institutionalCulture);
        int controlled = titles.Count(title => title.getCities().Any(city =>
            city != null && !city.isRekt() && city.kingdom?.GetEmpire() == empire));
        float controlledShare = titles.Count > 0 ? (float)controlled / titles.Count : 0f;
        bool institutionalRegime =
            OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(institutionalCulture, out Setting institutionalSetting) &&
            empire.CoreKingdom.GetRegime()?.type == institutionalSetting.regime;
        bool preservesMilitary = institutionalSetting == null ||
            !string.Equals(empire.data.military_tradition, institutionalSetting.regime.ToString(),
                StringComparison.OrdinalIgnoreCase);

        empire.data.central_plains_legitimacy = Clamp100(empire.data.central_plains_legitimacy +
            CompositeEmpireRules.GetCentralLegitimacyDelta(institutionalRegime, controlledShare, institutionalShare));
        empire.data.ruling_tradition_legitimacy = Clamp100(empire.data.ruling_tradition_legitimacy +
            CompositeEmpireRules.GetRulingLegitimacyDelta(emperorUsesRuling, rulingShare, preservesMilitary));

        int delta = CompositeEmpireRules.GetAnnualIntegrationDelta(institutionalShare, rulingShare,
            emperorUsesInstitution, emperorUsesRuling);
        bool rulingEliteStillExists = emperorUsesRuling || rulingShare >= 0.08f;
        int nextIntegration = Math.Max(1, CompositeEmpireRules.CapIntegration(
            empire.data.composite_integration + delta, rulingEliteStillExists));
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

    public static bool TryAdoptInstitutionalCultureName(Empire empire)
    {
        if (!IsComposite(empire) || empire.data.composite_cultural_name_adopted ||
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

    private static void CompleteIntegration(Empire empire, string institutionalCulture)
    {
        if (!CultureService.IsValidCulture(institutionalCulture) ||
            string.Equals(empire.data.ruling_culture, institutionalCulture, StringComparison.Ordinal)) return;
        empire.data.ruling_culture = institutionalCulture;
        empire.data.external_identity_culture = institutionalCulture;
        if (OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(institutionalCulture, out Setting institutionalSetting))
            empire.data.military_tradition = institutionalSetting.regime.ToString();
        CultureService.SetRealmCulture(empire.CoreKingdom, institutionalCulture, updatePoliticalSystem: false);
        SynchronizeMilitaryTraditionClaims(empire);
    }

    private static void SynchronizeMilitaryTraditionClaims(Empire empire)
    {
        var factions = empire?.CoreKingdom?.GetRegime()?.GetPlayerFactions();
        if (factions == null) return;
        bool preserveNomadicExpansion = UsesMilitaryTradition(empire, RegimeType.YouMu);
        List<FixedFaction> traditionFactions = factions
            .Where(f => f != null && (f.Type == FactionType.攘夷 || f.Type == FactionType.融入))
            .ToList();
        if (traditionFactions.Count == 0 && factions.FirstOrDefault(f => f?.Force == true) is FixedFaction dominate)
            traditionFactions.Add(dominate);
        foreach (FixedFaction faction in factions.Where(f => f != null))
        {
            faction.TemporaryFactionTypesRecord ??= new List<TemporaryFactionType>();
            faction.TemporaryFactions ??= new List<TemporaryFaction>();
            TemporaryFaction existing = faction.TemporaryFactions
                .FirstOrDefault(tf => tf != null && tf.type == TemporaryFactionType.游牧扩张);
            if (preserveNomadicExpansion && traditionFactions.Contains(faction))
            {
                if (!faction.TemporaryFactionTypesRecord.Contains(TemporaryFactionType.游牧扩张))
                    faction.TemporaryFactionTypesRecord.Add(TemporaryFactionType.游牧扩张);
                if (existing == null && FactionManager.Config?.StoredTemporaryFaction != null &&
                    FactionManager.Config.StoredTemporaryFaction.TryGetValue(
                        TemporaryFactionType.游牧扩张, out TemporaryFaction prototype))
                {
                    TemporaryFaction claim = prototype.Clone(faction);
                    claim.Init(faction);
                    claim.SetEmpire(empire);
                    faction.TemporaryFactions.Add(claim);
                }
            }
            else
            {
                faction.TemporaryFactionTypesRecord.Remove(TemporaryFactionType.游牧扩张);
                faction.TemporaryFactions.RemoveAll(tf => tf != null && tf.type == TemporaryFactionType.游牧扩张);
            }
        }
    }

    private static int Clamp100(int value)
    {
        return Math.Max(0, Math.Min(100, value));
    }
}

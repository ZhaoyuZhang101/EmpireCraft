using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;

namespace EmpireCraft.Scripts.GeneralSystems;

public readonly struct CultureModernizationResult
{
    public readonly int Realms;
    public readonly int States;
    public readonly int Citizens;
    public readonly int Parties;

    public CultureModernizationResult(int realms, int states, int citizens, int parties)
    {
        Realms = realms;
        States = states;
        Citizens = citizens;
        Parties = parties;
    }
}

// Player shortcut for starting a culture in the modern political era. This deliberately
// advances institutions and politics, but does not grant the culture every production tech.
public static class CultureModernizationSystem
{
    private static object _scanWorld;
    private static double _lastRealmTransitionScan = -1d;

    private static readonly PartyIdeology[] ModernBaseIdeologies =
    {
        PartyIdeology.Centrism,
        PartyIdeology.SocialLiberalism,
        PartyIdeology.ConservativeLiberalism
    };

    public static CultureModernizationResult ModernizeWorld()
    {
        CultureModernizationResult total = new(0, 0, 0, 0);
        foreach (string culture in CultureService.GetActiveCultureKeys().ToList())
        {
            CultureModernizationResult result = Modernize(culture);
            total = new CultureModernizationResult(
                total.Realms + result.Realms,
                total.States + result.States,
                total.Citizens + result.Citizens,
                total.Parties + result.Parties);
        }
        WarlordEraSystem.UpdateWorld(force: true);
        return total;
    }

    public static CultureModernizationResult Modernize(string culture)
    {
        if (!CultureService.IsValidCulture(culture) || World.world == null)
            return new CultureModernizationResult(0, 0, 0, 0);

        if (TechnologySystem.PremodernLocked)
            EmpireCraftWorldLawLibrary.empirecraft_law_premodern?.toggle(false);

        InstitutionSystem.ForceEnactAll(culture);
        var cultureState = InstitutionSystem.GetOrCreateCultureState(culture);
        if (cultureState != null)
        {
            cultureState.state_ideology = PartyIdeology.Centrism.ToString();
            cultureState.state_ideology_since = World.world.getCurWorldTime();
            cultureState.active_ideology_route = IdeologyRoute.Center.ToString();
        }
        int citizens = MoveConservativesIntoModernPolitics(culture);

        List<Kingdom> realms = World.world.kingdoms
            .Where(kingdom => kingdom?.data != null && !kingdom.isRekt() && kingdom.isCiv() &&
                              string.Equals(CultureService.GetRealmCulture(kingdom), culture,
                                  StringComparison.Ordinal))
            .ToList();

        List<Empire> existingStates = ActiveStates(culture);
        foreach (Empire empire in existingStates)
        {
            InstitutionSystem.ChangeRegimeForTransition(empire, RegimeType.Modern);
            RepublicSystem.SyncWithRegime(empire,
                foundingIdeology: ModernStateFormationSystem.ResolveGovernmentIdeology(empire.CoreKingdom));
            ReplaceConservativeGovernment(empire);
        }

        foreach (Kingdom kingdom in realms)
        {
            if (kingdom.GetRegime()?.type == RegimeType.Modern) continue;
            kingdom.SetRegimeType(RegimeType.Modern);
            kingdom.LoadRegime();
            kingdom.SystemChange();
        }

        // 有主法理的独立国与附庸国都尝试组建现代政府；纯行政区仍留在所属国家内部。
        // 不能组建政府者不论有无法理，都按理念显示为地方武装。
        foreach (Kingdom kingdom in realms.Where(kingdom =>
                     !kingdom.IsEmpire() && !kingdom.IsInEmpire() &&
                     kingdom.GetAdministrativeTitle() == null).ToList())
            ModernStateFormationSystem.TryUpdate(kingdom);

        foreach (Empire state in ActiveStates(culture))
            ModernStateFormationSystem.MergeSameCoreVassals(state);

        List<Empire> states = ActiveStates(culture);
        int parties = 0;
        foreach (Empire empire in states)
        {
            if (empire.CoreKingdom.GetRegime()?.type != RegimeType.Modern)
                InstitutionSystem.ChangeRegimeForTransition(empire, RegimeType.Modern);
            RepublicSystem.SyncWithRegime(empire,
                foundingIdeology: ModernStateFormationSystem.ResolveGovernmentIdeology(empire.CoreKingdom));
            ReplaceConservativeGovernment(empire);
            foreach (PartyIdeology ideology in ModernBaseIdeologies)
            {
                if (PartySystem.GetParties(empire).Any(party => party.Ideology == ideology)) continue;
                if (PartySystem.PlayerFoundParty(empire, ideology) != null) parties++;
            }
        }

        return new CultureModernizationResult(realms.Count, states.Count, citizens, parties);
    }

    // 文化自然进入现代政治后，旧王国不能永远停在封建政体。每年承接一次：
    // 有法理的独立国、附庸及次级附庸均可按首领理念组建政府；不能组建政府者
    // 不论有无法理，都走理念对应的地方武装命名。纯行政区不参与。
    public static void TryYearlyRealmTransitionScan()
    {
        if (World.world == null || ModClass.IS_CLEAR || TechnologySystem.PremodernLocked) return;
        double now = World.world.getCurWorldTime();
        if (!ReferenceEquals(_scanWorld, World.world) || now < _lastRealmTransitionScan)
        {
            _scanWorld = World.world;
            _lastRealmTransitionScan = now;
            return;
        }
        if (_lastRealmTransitionScan >= 0d && Date.getYearsSince(_lastRealmTransitionScan) < 1) return;
        _lastRealmTransitionScan = now;

        bool changed = false;
        foreach (Kingdom subject in World.world.kingdoms.ToList())
            if (subject?.GetRegime()?.type == RegimeType.Modern &&
                FeudalVassalService.GetOverlord(subject) != null)
                changed |= ModernStateFormationSystem.TryMergeIntoOverlordGovernment(subject);
        foreach (string culture in CultureService.GetActiveCultureKeys().ToList())
        {
            if (!HasEnteredModernPolitics(culture)) continue;
            changed |= TransitionIndependentRealms(culture);
        }
        WarlordEraSystem.UpdateWorld(force: changed);
    }

    private static bool HasEnteredModernPolitics(string culture)
    {
        if (IdeologySpreadSystem.TryGetStateIdeology(culture, out _)) return true;
        return InstitutionSystem.GetFeature(culture, PartySystem.FeaturePartyPolitics) > 0f &&
               InstitutionSystem.GetFeature(culture, RepublicSystem.FeatureAbolishMonarchy) > 0f;
    }

    private static bool TransitionIndependentRealms(string culture)
    {
        List<Kingdom> candidates = World.world.kingdoms
            .Where(kingdom => kingdom?.data != null && !kingdom.isRekt() && kingdom.isCiv() &&
                              !kingdom.IsEmpire() && !kingdom.IsInEmpire() &&
                              kingdom.GetAdministrativeTitle() == null &&
                              string.Equals(CultureService.GetRealmCulture(kingdom), culture,
                                  StringComparison.Ordinal))
            .ToList();
        bool changed = false;
        foreach (Kingdom kingdom in candidates)
        {
            if (kingdom.GetRegime()?.type != RegimeType.Modern)
            {
                kingdom.SetRegimeType(RegimeType.Modern);
                kingdom.LoadRegime();
                kingdom.SystemChange();
                changed = true;
            }
            if (ModernStateFormationSystem.TryUpdate(kingdom)) changed = true;
        }
        return changed;
    }

    private static void ReplaceConservativeGovernment(Empire empire)
    {
        var state = empire?.data?.constitutional_economy;
        if (state?.is_republic != true || state.republic_ideology != PartyIdeology.Conservatism) return;
        state.republic_ideology = PartyIdeology.Centrism;
        empire.CoreKingdom.GetOrCreate().ideology_country_suffix =
            PartySystem.PickCountrySuffix(empire, PartyIdeology.Centrism);
        RepublicSystem.EnsureIdeologyBureau(empire);
    }

    private static List<Empire> ActiveStates(string culture) =>
        (ModClass.EMPIRE_MANAGER ?? Enumerable.Empty<Empire>())
        .Where(empire => empire?.data != null && !empire.IsArchived() && !empire.isRekt() &&
                         empire.CoreKingdom != null &&
                         string.Equals(InstitutionSystem.GetPrimaryCulture(empire), culture,
                             StringComparison.Ordinal))
        .ToList();

    private static int MoveConservativesIntoModernPolitics(string culture)
    {
        int changed = 0;
        foreach (Actor actor in World.world.units.getSimpleList().ToList())
        {
            if (actor == null || actor.isRekt() || !actor.isAlive() || actor.city == null ||
                actor.IsWarMachine() ||
                !string.Equals(CultureService.GetActorCulture(actor), culture, StringComparison.Ordinal) ||
                IdeologyPopulationSystem.Get(actor) != PartyIdeology.Conservatism) continue;

            int bucket = (int)((actor.id & long.MaxValue) % 10L);
            PartyIdeology ideology = bucket < 5
                ? PartyIdeology.Centrism
                : bucket < 8
                    ? PartyIdeology.SocialLiberalism
                    : PartyIdeology.ConservativeLiberalism;
            IdeologyPopulationSystem.Set(actor, ideology);
            changed++;
        }
        return changed;
    }
}

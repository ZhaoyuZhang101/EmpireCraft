using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

public static class ImperialLegitimacyChallengeService
{
    private const string AnlePeerageKey = "tang_honorary_anle_gong";

    public static bool TryFindTarget(Kingdom kingdom, out Empire target)
    {
        target = null;
        if (kingdom == null || kingdom.isRekt() || !kingdom.hasKing() || kingdom.king == null ||
            kingdom.king.isRekt() || kingdom.IsInEmpire() || kingdom.IsEmpire() || kingdom.hasEnemies() ||
            kingdom.GetMoney() < 0 || AncientWarfareCompatibility.BlocksEmpireFormation(kingdom) ||
            EmpireCraftWorldLawLibrary.empirecraft_law_ban_empire.isEnabled()) return false;
        if (!kingdom.HasMainTitle() && !kingdom.GetControlledTitle().Any()) return false;

        string culture = CultureService.GetRealmCulture(kingdom);
        if (!CultureService.IsValidCulture(culture) || ModClass.EMPIRE_MANAGER == null) return false;
        List<Empire> sameCultureEmpires = ModClass.EMPIRE_MANAGER
            .Where(empire => IsActiveEmpire(empire) &&
                             string.Equals(CultureService.GetEmpireDefaultCulture(empire), culture,
                                 StringComparison.Ordinal))
            .ToList();
        if (sameCultureEmpires.Count != 1) return false;

        Empire incumbent = sameCultureEmpires[0];
        // 帝国正在打仗也可以趁机发起
        if (incumbent.data.legitimacy_rivalry_recognized) return false;
        bool sharesLandBorder = SharesLandBorder(kingdom, incumbent);
        if (!ImperialLegitimacyRules.CanChallenge(kingdom.GetNationalPower(), incumbent.GetNationalPower(),
                sameCultureEmpires.Count, !kingdom.IsInEmpire(), sharesLandBorder)) return false;
        target = incumbent;
        return true;
    }

    private static bool SharesLandBorder(Kingdom challenger, Empire incumbent)
    {
        if (challenger?.cities == null || incumbent?.kingdoms_hashset == null) return false;
        foreach (City city in challenger.cities)
        {
            if (city == null || city.isRekt() || city.neighbours_kingdoms == null) continue;
            foreach (Kingdom neighbour in city.neighbours_kingdoms)
            {
                if (neighbour != null && !neighbour.isRekt() && incumbent.kingdoms_hashset.Contains(neighbour))
                    return true;
            }
        }
        return false;
    }

    public static bool CanChallenge(Actor actor)
    {
        return actor != null && !actor.isRekt() && actor.isKing() && actor.kingdom?.king == actor &&
               actor.plot?.isActive() != true && TryFindTarget(actor.kingdom, out _);
    }

    public static bool CanContinueChallenge(Actor actor)
    {
        return actor != null && !actor.isRekt() && actor.isKing() && actor.kingdom?.king == actor &&
               TryFindTarget(actor.kingdom, out _);
    }

    public static bool StartChallenge(Actor actor)
    {
        Kingdom kingdom = actor?.kingdom;
        if (kingdom == null || kingdom.king != actor || !TryFindTarget(kingdom, out Empire incumbent))
            return false;

        bool contestsIncumbentCore = EmpireFormationService.GetSeatCoreEmpire(kingdom) == incumbent;
        Empire challenger = ModClass.EMPIRE_MANAGER.NewEmpire(kingdom, allowCultureRival: true,
            forceNewCore: true);
        if (challenger == null) return false;
        MarkRivalry(challenger, incumbent, challengerIsClaimant: true);
        if (contestsIncumbentCore)
        {
            EmpireCore falseCore = EmpireCoreManager.Get(challenger);
            if (falseCore != null) falseCore.false_core_against_empire_id = incumbent.id;
        }

        War war = DiplomacyHelpers.wars.newWar(challenger.CoreKingdom, incumbent.CoreKingdom,
            WarTypeLibrary.normal);
        if (war == null)
        {
            ClearRivalry(challenger);
            ClearRivalry(incumbent);
            ModClass.EMPIRE_MANAGER.dissolveEmpire(challenger);
            return false;
        }

        war.SetEmpireWarType(EmpireWarType.去帝号, nanoObject: incumbent);
        war.InitializeLegitimacyChallenge(challenger, incumbent);
        foreach (Kingdom member in challenger.kingdoms_list.ToList())
            if (member != null && member != challenger.CoreKingdom && !member.isRekt()) war.joinAttackers(member);
        foreach (Kingdom member in incumbent.kingdoms_list.ToList())
            if (member != null && member != incumbent.CoreKingdom && !member.isRekt()) war.joinDefenders(member);

        RecordDeclaration(challenger, incumbent, actor, incumbent.Emperor);
        return true;
    }

    // 僭越称帝：与主法理所在核心的现存帝国并立，互为正统对手(先不开战)，天命仅 20
    public static void DeclareUsurpation(Empire usurper, Empire incumbent)
    {
        if (!IsActiveEmpire(usurper) || !IsActiveEmpire(incumbent) || usurper == incumbent) return;
        MarkRivalry(usurper, incumbent, challengerIsClaimant: true);
        EmpireCore falseCore = EmpireCoreManager.Get(usurper);
        if (falseCore != null) falseCore.false_core_against_empire_id = incumbent.id;
        usurper.data.Mandate = EmpireFormationService.UsurpationMandate;
        string content = string.Format(LM.Get("history_usurpation_declared"),
            usurper.Emperor?.getName() ?? usurper.GetEmpireFullName(), usurper.GetEmpireFullName(),
            incumbent.GetEmpireFullName());
        usurper.RecordHistory(directContent: content, actorId: usurper.Emperor?.id ?? -1L,
            kingdomId: usurper.CoreKingdom?.id ?? -1L);
        incumbent.RecordHistory(directContent: content, actorId: usurper.Emperor?.id ?? -1L,
            kingdomId: usurper.CoreKingdom?.id ?? -1L);
        ActionLibrary.showWhisperTip(content);
    }

    // 互为正统对手的两个帝国：较强的一方(双方之间没有战争时)可以发起正统之争；每次检查有一定概率
    public static bool TryStartRivalryWar(Empire empire)
    {
        if (!IsActiveEmpire(empire) || !empire.data.legitimacy_rivalry_recognized) return false;
        Empire rival = ModClass.EMPIRE_MANAGER?.get(empire.data.legitimacy_rival_empire_id);
        if (!AreRecognizedRivals(empire, rival)) return false;
        Kingdom core = empire.CoreKingdom;
        Kingdom rivalCore = rival.CoreKingdom;
        if (core == null || rivalCore == null || core.isRekt() || rivalCore.isRekt()) return false;
        if (core.isInWarWith(rivalCore) || core.GetMoney() < 0) return false;
        if (empire.GetNationalPower() <= rival.GetNationalPower()) return false;
        if (UnityEngine.Random.value > RivalryWarChance) return false;

        War war = DiplomacyHelpers.wars.newWar(core, rivalCore, WarTypeLibrary.normal);
        if (war == null) return false;
        war.SetEmpireWarType(EmpireWarType.去帝号, nanoObject: rival);
        war.InitializeLegitimacyChallenge(empire, rival);
        foreach (Kingdom member in empire.kingdoms_list.ToList())
            if (member != null && member != core && !member.isRekt()) war.joinAttackers(member);
        foreach (Kingdom member in rival.kingdoms_list.ToList())
            if (member != null && member != rivalCore && !member.isRekt()) war.joinDefenders(member);
        RecordDeclaration(empire, rival, empire.Emperor, rival.Emperor);
        return true;
    }

    private const float RivalryWarChance = 0.1f;

    public static bool AreRecognizedRivals(Empire first, Empire second)
    {
        if (!IsActiveEmpire(first) || !IsActiveEmpire(second) || first == second) return false;
        return first.data.legitimacy_rivalry_recognized && second.data.legitimacy_rivalry_recognized &&
               first.data.legitimacy_rival_empire_id == second.id &&
               second.data.legitimacy_rival_empire_id == first.id;
    }

    public static void ClearRivalry(Empire empire)
    {
        if (empire?.data == null) return;
        EmpireCore core = EmpireCoreManager.Get(empire);
        if (core != null) core.false_core_against_empire_id = -1L;
        empire.data.legitimacy_rival_empire_id = -1L;
        empire.data.legitimacy_rivalry_recognized = false;
        empire.data.legitimacy_challenger = false;
    }

    public static void ResolveWar(War war, WarWinner winner)
    {
        if (war == null) return;
        WarExtension.WarExtraData data = war.GetOrCreate();
        Empire challenger = ModClass.EMPIRE_MANAGER?.get(data.legitimacy_challenger_empire_id);
        Empire incumbent = ModClass.EMPIRE_MANAGER?.get(data.legitimacy_defender_empire_id);
        if (winner == WarWinner.Attackers && challenger != null && !challenger.IsArchived() &&
            (war.IsMainDefenderEliminated() || war.HasLegitimacyOccupationThreshold()))
        {
            ResolveVictory(war, challenger, incumbent);
            return;
        }
        RecordFailure(war, challenger, incumbent);
    }

    private static void MarkRivalry(Empire challenger, Empire incumbent, bool challengerIsClaimant)
    {
        challenger.data.legitimacy_rival_empire_id = incumbent.id;
        challenger.data.legitimacy_rivalry_recognized = true;
        challenger.data.legitimacy_challenger = challengerIsClaimant;
        incumbent.data.legitimacy_rival_empire_id = challenger.id;
        incumbent.data.legitimacy_rivalry_recognized = true;
        incumbent.data.legitimacy_challenger = !challengerIsClaimant;
    }

    private static void ResolveVictory(War war, Empire challenger, Empire incumbent)
    {
        if (challenger == null || challenger.IsArchived()) return;
        bool challengerWasClaimant = challenger.data.legitimacy_challenger;
        WarExtension.WarExtraData warData = war.GetOrCreate();
        Actor challengerEmperor = challenger.Emperor;
        Actor formerEmperor = incumbent?.Emperor ?? World.world?.units?.get(warData.legitimacy_defender_emperor_id);
        Kingdom formerCore = incumbent?.CoreKingdom ??
                             World.world?.kingdoms?.get(warData.legitimacy_defender_core_kingdom_id);
        Kingdom newCore = challenger.CoreKingdom;
        string oldEmpireName = incumbent?.GetEmpireFullName() ?? warData.legitimacy_defender_empire_name ?? "";
        string newEmpireName = challenger.GetEmpireFullName();

        string success = string.Format(LM.Get("history_legitimacy_challenge_victory"),
            challengerEmperor?.getName() ?? newEmpireName, oldEmpireName, newEmpireName);
        challenger.RecordHistory(directContent: success, actorId: challengerEmperor?.id ?? -1L,
            kingdomId: newCore?.id ?? -1L);
        if (incumbent != null)
            incumbent.RecordHistory(directContent: success, actorId: challengerEmperor?.id ?? -1L,
                kingdomId: newCore?.id ?? -1L);
        else
            RecordArchivedEmpireHistory(warData.legitimacy_defender_empire_id, success,
                challengerEmperor?.id ?? -1L, newCore?.id ?? -1L);
        challengerEmperor?.RecordPersonalHistory(success, "legitimacy_challenge_victory",
            relatedActorId: formerEmperor?.id ?? -1L);
        formerEmperor?.RecordPersonalHistory(success, "legitimacy_challenge_defeat",
            relatedActorId: challengerEmperor?.id ?? -1L);

        IEnumerable<Kingdom> formerMembers = incumbent != null && !incumbent.IsArchived()
            ? incumbent.kingdoms_list
            : (warData.legitimacy_defender_kingdom_ids ?? new List<long>())
                .Select(id => World.world?.kingdoms?.get(id));
        foreach (Kingdom administration in formerMembers
                     .Where(member => member != null && member != formerCore && !member.isRekt()).ToList())
        {
            challenger.join(administration, pRecalc: false, pForce: true, pLegitimacyTransfer: true);
        }

        City retirementCity = null;
        Kingdom retirementKingdom = formerCore;
        if (formerCore != null && !formerCore.isRekt() && formerCore.cities?.Count > 0)
        {
            retirementCity = formerCore.capital != null && !formerCore.capital.isRekt()
                ? formerCore.capital
                : formerCore.cities.FirstOrDefault(city => city != null && !city.isRekt());
            foreach (City city in formerCore.cities.ToList())
                if (city != null && city != retirementCity && !city.isRekt() && newCore != null)
                    city.joinAnotherKingdom(newCore);
            if (retirementCity != null) formerCore.setCapital(retirementCity);
        }
        else if (formerEmperor != null && !formerEmperor.isRekt())
        {
            retirementCity = (warData.legitimacy_defender_city_ids ?? new List<long>())
                .Select(id => World.world?.cities?.get(id))
                .FirstOrDefault(city => city != null && !city.isRekt());
            if (retirementCity != null)
            {
                retirementKingdom = retirementCity.makeOwnKingdom(formerEmperor);
                retirementKingdom?.setCapital(retirementCity);
            }
        }

        EmpireCore inheritedCore = EmpireCoreManager.Get(incumbent) ??
                                   EmpireCoreManager.Get(warData.legitimacy_defender_core_id);
        EmpireCore provisionalCore = EmpireCoreManager.Get(challenger);
        if (challengerWasClaimant)
        {
            // 挑战/僭越一方胜出：销毁自己的临时核心，接管原帝国的核心
            if (provisionalCore != null && provisionalCore != inheritedCore)
                EmpireCoreManager.DestroyEmpireCore(provisionalCore);
            if (inheritedCore != null) EmpireCoreManager.RebindEmpire(challenger, inheritedCore);
        }
        else if (inheritedCore != null && inheritedCore != provisionalCore)
        {
            // 原帝国平定僭越者：保留自己的核心，僭越者的临时核心随之销毁
            EmpireCoreManager.DestroyEmpireCore(inheritedCore);
        }

        if (incumbent != null && !incumbent.IsArchived()) ModClass.EMPIRE_MANAGER.dissolveEmpire(incumbent);
        if (retirementKingdom != null && !retirementKingdom.isRekt())
            challenger.join(retirementKingdom, pRecalc: false, pForce: true, pLegitimacyTransfer: true);
        challenger.recalculate();
        ClearRivalry(challenger);

        if (formerEmperor != null && !formerEmperor.isRekt())
        {
            challenger.RememberDefeatedEmpireHouse(new DefeatedEmpireHouse
            {
                empire_id = warData.legitimacy_defender_empire_id,
                emperor_id = formerEmperor.id,
                royal_clan_id = formerEmperor.GetSpecificClan()?.id ?? -1L
            });
            ForceGrantAnlePeerage(formerEmperor, challenger);
            string enfeoffed = string.Format(LM.Get("history_deposed_emperor_enfeoffed_anle"),
                formerEmperor.getName(), retirementCity?.GetCityName() ?? "");
            challenger.RecordHistory(directContent: enfeoffed, actorId: formerEmperor.id,
                kingdomId: retirementKingdom?.id ?? -1L);
            formerEmperor.RecordPersonalHistory(enfeoffed, "deposed_emperor_enfeoffed_anle",
                relatedActorId: challengerEmperor?.id ?? -1L);
        }
        ActionLibrary.showWhisperTip(success);
    }

    private static void ForceGrantAnlePeerage(Actor actor, Empire empire)
    {
        if (actor == null || actor.isRekt() || empire == null) return;
        actor.CheckSpecificClan(false);
        var actorData = actor.GetOrCreate();
        actorData.honorary_peerage_key = AnlePeerageKey;
        actorData.honorary_peerage_empire_id = empire.id;
        actor.RemoveEmpire();
        empire.RememberHonoraryPeerageHolder(AnlePeerageKey, actor);
        TranslateHelper.LogHonoraryPeerageGranted(actor, empire, AnlePeerageKey);
    }

    private static void RecordDeclaration(Empire challenger, Empire incumbent, Actor challengerEmperor,
        Actor incumbentEmperor)
    {
        string content = string.Format(LM.Get("history_legitimacy_challenge_declared"),
            challengerEmperor?.getName() ?? challenger.GetEmpireFullName(), incumbent.GetEmpireFullName());
        challenger.RecordHistory(directContent: content, actorId: challengerEmperor?.id ?? -1L,
            kingdomId: challenger.CoreKingdom?.id ?? -1L);
        incumbent.RecordHistory(directContent: content, actorId: challengerEmperor?.id ?? -1L,
            kingdomId: challenger.CoreKingdom?.id ?? -1L);
        challengerEmperor?.RecordPersonalHistory(content, "legitimacy_challenge_declared",
            relatedActorId: incumbentEmperor?.id ?? -1L);
        incumbentEmperor?.RecordPersonalHistory(content, "legitimacy_challenge_defended",
            relatedActorId: challengerEmperor?.id ?? -1L);
        ActionLibrary.showWhisperTip(content);
    }

    private static void RecordFailure(War war, Empire challenger, Empire incumbent)
    {
        if (war == null) return;
        WarExtension.WarExtraData data = war.GetOrCreate();
        Actor challengerEmperor = challenger?.Emperor ??
                                   World.world?.units?.get(data.legitimacy_challenger_emperor_id);
        Actor incumbentEmperor = incumbent?.Emperor ??
                                  World.world?.units?.get(data.legitimacy_defender_emperor_id);
        string content = string.Format(LM.Get("history_legitimacy_challenge_failed"),
            challenger?.GetEmpireFullName() ?? data.legitimacy_challenger_empire_name ?? "",
            incumbent?.GetEmpireFullName() ?? data.legitimacy_defender_empire_name ?? "");
        if (challenger != null)
            challenger.RecordHistory(directContent: content, actorId: incumbentEmperor?.id ?? -1L,
                kingdomId: incumbent?.CoreKingdom?.id ?? data.legitimacy_defender_core_kingdom_id);
        else
            RecordArchivedEmpireHistory(data.legitimacy_challenger_empire_id, content,
                incumbentEmperor?.id ?? -1L, data.legitimacy_defender_core_kingdom_id);
        if (incumbent != null)
            incumbent.RecordHistory(directContent: content, actorId: challengerEmperor?.id ?? -1L,
                kingdomId: challenger?.CoreKingdom?.id ?? data.legitimacy_challenger_core_kingdom_id);
        else
            RecordArchivedEmpireHistory(data.legitimacy_defender_empire_id, content,
                challengerEmperor?.id ?? -1L, data.legitimacy_challenger_core_kingdom_id);
        challengerEmperor?.RecordPersonalHistory(content, "legitimacy_challenge_failed",
            relatedActorId: incumbentEmperor?.id ?? -1L);
        incumbentEmperor?.RecordPersonalHistory(content, "legitimacy_challenge_survived",
            relatedActorId: challengerEmperor?.id ?? -1L);
        ActionLibrary.showWhisperTip(content);
    }

    private static void RecordArchivedEmpireHistory(long empireId, string content, long actorId, long kingdomId)
    {
        if (empireId <= 0 || string.IsNullOrWhiteSpace(content) ||
            !ModClass.ALL_HISTORY_DATA.TryGetValue(empireId, out List<EmpireCraftHistory> histories) ||
            histories == null || histories.Count == 0) return;
        EmpireCraftHistory history = histories.LastOrDefault(item => item != null);
        if (history == null) return;
        history.descriptions ??= new List<HistoryDescription>();
        if (history.descriptions.Any(item => item?.description == content)) return;
        history.descriptions.Add(new HistoryDescription
        {
            time = Date.getDate(World.world.getCurWorldTime()),
            timestamp = World.world.getCurWorldTime(),
            description = content,
            actor_id = actorId,
            kingdom_id = kingdomId
        });
    }

    private static bool IsActiveEmpire(Empire empire)
    {
        return empire != null && !empire.IsArchived() && !empire.isRekt() &&
               empire.CoreKingdom != null && !empire.CoreKingdom.isRekt() &&
               !AncientWarfareCompatibility.Owns(empire.CoreKingdom);
    }
}

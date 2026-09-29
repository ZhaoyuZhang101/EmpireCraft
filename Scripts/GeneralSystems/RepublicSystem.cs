using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.AI.CityAI;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 共和改制。制度"废除君主制"(特性 abolish_monarchy)只是让本文化的帝国可以改制，各帝国各自走：
//   · 军方支持的改制：单一执政党完成本理念的文化支线、取得过半军队支持和足够议席，
//     推动"建立共和"诉求 → 皇帝退位，改为现代共和政体；
//   · 革命：政党在正统崩溃(<30)、其所依靠的阶层怨气高涨时推动"建立共和"；
//     革命战争胜利后废除帝制，共产/法西斯建立一党制；战败后该党被取缔。
//   · 复辟：共和之后保守主义政党完成路线、执政过半并掌握军队，推动"复辟帝制"。
// 改制后国号后缀按执政党理念从词库里取；本文化第一次有帝国以某理念共和，这个理念就成为本文化的
// 主导理念，开始向接壤的异文化传播(见 IdeologySpreadSystem)。
public static class RepublicSystem
{
    public const int CommonEraCalendar = 1;
    public const int RepublicEraCalendar = 2;
    public const string FeatureAbolishMonarchy = "abolish_monarchy";
    private const float SupermajorityShare = 2f / 3f;
    private const int RevolutionMandate = 30;
    private const float RevolutionGrievance = 50f;
    private const float CoreClassAffinity = 40f;
    private const int DraftingStartsAfterYears = 1;
    private const int FirstElectionAfterYears = 3;

    private static readonly HashSet<PartyIdeology> OnePartyIdeologies =
        new() { PartyIdeology.Communism, PartyIdeology.Fascism };

    private static ConstitutionalEconomyState State(Empire empire) => empire?.data?.constitutional_economy;

    public static bool IsRepublic(Empire empire) => State(empire)?.is_republic == true;

    // 改制共和的帝国不受文化政体同步(复合帝国区域制度、文化变更、制度同步)的影响，否则刚改制就被拉回君主制
    public static bool IsRegimeLocked(Kingdom kingdom) => IsRepublic(kingdom?.GetEmpire());

    public static bool IsOneParty(Empire empire) => !string.IsNullOrWhiteSpace(State(empire)?.one_party_id);

    public static bool IsTransitioning(Empire empire) => IsRepublic(empire) &&
        State(empire)?.republic_transition_stage > 0;

    public static bool CanAbolish(Empire empire) =>
        !TechnologySystem.PremodernLocked &&
        empire?.CoreKingdom != null && !IsRepublic(empire) &&
        RegimeManager.IsMonarchy(empire.CoreKingdom.GetRegime()?.type) &&
        InstitutionSystem.GetFeature(empire, FeatureAbolishMonarchy) > 0f;

    #region 建立共和

    public static bool CanAbdicate(Empire empire, FixedFaction party)
    {
        if (!CanAbolish(empire) || party == null || !party.IsParty || party.Ban ||
            party.Ideology == PartyIdeology.Conservatism || !CanCoup(empire, party)) return false;
        ConstitutionalEconomyState state = State(empire);
        int total = state?.parliament_seats?.Count ?? 0;
        if (ParliamentSystem.HasParliament(empire) && total > 0)
        {
            int abolitionSeats = state.parliament_seats.Count(seat => seat.faction_id == party.GetID());
            return ParliamentSystem.IsGoverningFaction(empire, party.GetID()) &&
                   abolitionSeats >= Mathf.CeilToInt(total * SupermajorityShare);
        }
        // 没有议会：主导朝局、中央占比达到三分之二的政党
        return empire.CoreKingdom.GetRegime()?.GetDominateFaction() == party &&
               party.CentralRatio >= Mathf.CeilToInt(100f * SupermajorityShare);
    }

    // A party cannot substitute a coalition's seats for its own completed program
    // or for actual support among the empire's soldiers.
    public static bool CanCoup(Empire empire, FixedFaction party)
    {
        if (empire?.CoreKingdom == null || party?.IsParty != true || party.Ban) return false;
        string culture = InstitutionSystem.GetPrimaryCulture(empire);
        if (InstitutionSystem.GetFeature(culture,
                IdeologyInstitutionPaths.StageFeature(party.Ideology, 3)) <= 0f) return false;
        return IdeologyPopulationSystem.GetMilitaryShare(empire, party.Ideology) > 0.5f;
    }

    public static bool CanRevolt(Empire empire, FixedFaction party)
    {
        if (!CanAbolish(empire) || party == null || !party.IsParty || party.Ban ||
            party.Ideology == PartyIdeology.Conservatism || party.GetLeader() == null) return false;
        if (HasActiveRevolutionWar(empire)) return false;
        return empire.Mandate < RevolutionMandate && GetCoreGrievance(empire, party) >= RevolutionGrievance &&
               (FindRebelKingdom(empire, party) != null || FindSplitLeader(empire, party) != null);
    }

    // 该党所依靠的阶层(理念对其吸引力高的)平均怨气
    private static float GetCoreGrievance(Empire empire, FixedFaction party)
    {
        IReadOnlyDictionary<SocialClass, float> grievances = InstitutionSystem.GetClassGrievances(empire);
        List<float> values = Enum.GetValues(typeof(SocialClass)).Cast<SocialClass>()
            .Where(socialClass => PartySystem.GetAffinity(party.Ideology, socialClass) >= CoreClassAffinity)
            .Select(socialClass => grievances.TryGetValue(socialClass, out float value) ? value : 0f).ToList();
        return values.Count == 0 ? 0f : values.Average();
    }

    // "建立共和"诉求执行：能和平退位就退位，否则走革命
    public static void PushRepublic(Empire empire, FixedFaction party)
    {
        if (CanAbdicate(empire, party))
        {
            Establish(empire, party, false, "republic_coup_history");
            return;
        }
        if (CanRevolt(empire, party)) StartRevolutionWar(empire, party);
    }

    private static Kingdom FindRebelKingdom(Empire empire, FixedFaction party) => empire.kingdoms_list
        .Where(kingdom => kingdom != null && !kingdom.isRekt() && kingdom != empire.CoreKingdom &&
                          kingdom.hasKing() && !kingdom.IsFactionRebelling() &&
                          !kingdom.IsLocalRebelling() && !kingdom.getWars().Any() &&
                          party.AllMembers.Any(actor => actor?.kingdom == kingdom))
        .OrderByDescending(kingdom => IdeologyPopulationSystem.GetKingdomShare(kingdom, party.Ideology))
        .ThenByDescending(kingdom => party.AllMembers.Count(actor => actor?.kingdom == kingdom))
        .ThenByDescending(kingdom => kingdom.countTotalWarriors()).FirstOrDefault();

    private static Actor FindSplitLeader(Empire empire, FixedFaction party) => party.AllMembers
        .Concat(EmpirePopulation.Enumerate(empire.kingdoms_hashset))
        .Where(actor => actor != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() &&
                        actor.hasCity() && actor.city.kingdom != null &&
                        empire.kingdoms_hashset.Contains(actor.city.kingdom) &&
                        actor.city != actor.city.kingdom.capital && !actor.isKing() &&
                        PartySystem.GetAffinity(party.Ideology, actor.GetOrCreate().socialClass) >= CoreClassAffinity)
        .Distinct().OrderByDescending(actor => IdeologyPopulationSystem.GetCityShare(actor.city, party.Ideology))
        .ThenByDescending(actor => actor.GetFaction() == party)
        .ThenByDescending(actor => actor.renown).FirstOrDefault();

    private static bool StartRevolutionWar(Empire empire, FixedFaction party)
    {
        Kingdom rebel = FindRebelKingdom(empire, party);
        City splitSeat = null;
        Kingdom splitOrigin = null;
        if (rebel == null)
        {
            Actor leader = FindSplitLeader(empire, party);
            splitSeat = leader?.city;
            splitOrigin = splitSeat?.kingdom;
            if (splitSeat == null) return false;
            rebel = splitSeat.makeOwnKingdom(leader, pRebellion: true);
            if (rebel == null) return false;
        }
        if (splitSeat == null) rebel.StartFactionRebelling(party);
        else if (!rebel.StartLocalRebelling(EmpireWarType.地方叛乱))
        {
            RebellionStartupService.RollbackCitySplit(splitSeat, splitOrigin, rebel);
            return false;
        }
        War war = World.world.diplomacy.startWar(rebel, empire.CoreKingdom, WarTypeLibrary.rebellion);
        if (war == null)
        {
            if (splitSeat == null) rebel.EndFactionRebelling();
            else
            {
                rebel.EndLocalRebelling();
                RebellionStartupService.RollbackCitySplit(splitSeat, splitOrigin, rebel);
            }
            return false;
        }
        war.SetEmpireWarType(splitSeat == null ? EmpireWarType.派系叛乱 : EmpireWarType.地方叛乱);
        war.data.name = string.Format(LM.Get("republic_revolution_war_name"), party.Name);
        WarExtension.WarExtraData snapshot = war.GetOrCreate();
        snapshot.republic_revolution_empire_id = empire.id;
        snapshot.republic_revolution_party_id = party.GetID();
        snapshot.republic_revolution_party_name = party.Name;
        snapshot.republic_revolution_ideology = party.Ideology;
        snapshot.republic_revolution_leader_id = party.GetLeader()?.id ?? -1L;
        snapshot.republic_revolution_split_realm = splitSeat != null;
        int alliedRealms = RecruitRevolutionaryRealms(empire, party, war, rebel, snapshot);
        int volunteers = RaiseRevolutionaryMilitia(empire, rebel, party.Ideology, splitSeat != null);
        int defectors = DefectImperialSoldiers(empire, party, war, rebel);
        snapshot.republic_revolution_defected_soldiers = defectors;
        OrganizeRevolutionaryGarrison(rebel.capital, rebel);
        Record(empire, string.Format(LM.Get("republic_revolution_started_history"), party.Name,
            rebel.GetKingdomName(), empire.GetEmpireFullName()), party.GetLeader());
        if (alliedRealms > 0)
            Record(empire, string.Format(LM.Get("republic_revolution_allies_history"), alliedRealms),
                party.GetLeader());
        if (defectors > 0 || volunteers > 0)
            Record(empire, string.Format(LM.Get("republic_revolution_forces_history"), defectors, volunteers,
                rebel.GetKingdomName()), party.GetLeader());
        return true;
    }

    private static int RecruitRevolutionaryRealms(Empire empire, FixedFaction party, War war, Kingdom rebel,
        WarExtension.WarExtraData snapshot)
    {
        float grievance = GetCoreGrievance(empire, party) / 100f;
        int joined = 0;
        foreach (Kingdom member in empire.kingdoms_list.ToList())
        {
            if (member == null || member.isRekt() || member == empire.CoreKingdom || member == rebel ||
                member.IsFactionRebelling() || member.IsLocalRebelling() ||
                member.getWars().Any(other => other != war && !other.hasEnded())) continue;
            if (war._list_attackers.Contains(member) || war._list_defenders.Contains(member)) continue;

            int supporters = party.AllMembers.Count(actor => actor != null && actor.kingdom == member);
            float localSupport = IdeologyPopulationSystem.GetKingdomShare(member, party.Ideology);
            float chance = Mathf.Clamp(0.12f + 0.10f * Mathf.Min(4, supporters) +
                0.22f * localSupport + 0.32f * grievance +
                0.18f * (RevolutionMandate - empire.Mandate) / RevolutionMandate,
                0.12f, 0.85f);
            if (UnityEngine.Random.value < chance)
            {
                war.joinAttackers(member);
                if (war._list_attackers.Contains(member))
                {
                    member.StartFactionRebelling(party);
                    snapshot.republic_revolution_allied_kingdom_ids.Add(member.id);
                    joined++;
                }
            }
            else if (UnityEngine.Random.value < 0.35f)
            {
                war.joinDefenders(member);
            }
        }
        return joined;
    }

    private static int DefectImperialSoldiers(Empire empire, FixedFaction party, War war, Kingdom rebel)
    {
        City capital = rebel.capital;
        if (capital == null || capital.isRekt()) return 0;
        IReadOnlyDictionary<SocialClass, float> grievances = InstitutionSystem.GetClassGrievances(empire);
        string culture = InstitutionSystem.GetPrimaryCulture(empire);
        float programBonus = InstitutionSystem.GetFeature(culture,
            IdeologyInstitutionPaths.StageFeature(party.Ideology, 3)) > 0f
            ? IdeologyInstitutionPaths.GetProfile(party.Ideology).DefectionBonus : 0f;
        int total = 0;
        foreach (Kingdom source in empire.kingdoms_list.ToList())
        {
            if (source?.units == null || source == rebel || source.isRekt() ||
                !war._list_defenders.Contains(source)) continue;
            List<Actor> soldiers = source.units.ToList().Where(actor => actor != null && !actor.isRekt() &&
                actor.isAlive() && actor.isWarrior() && !actor.isKing()).ToList();
            int limit = Mathf.CeilToInt(soldiers.Count * 0.45f);
            List<Actor> defectors = new();
            foreach (Actor soldier in soldiers.OrderByDescending(actor => actor.city == capital))
            {
                if (defectors.Count >= limit) break;
                SocialClass socialClass = soldier.GetOrCreate().socialClass;
                float grievance = grievances.TryGetValue(socialClass, out float value) ? value / 100f : 0f;
                float affinity = Mathf.Max(0f, PartySystem.GetAffinity(party.Ideology, socialClass)) / 100f;
                float chance = Mathf.Clamp(0.02f + 0.22f * affinity + 0.23f * grievance +
                    (IdeologyPopulationSystem.Get(soldier) == party.Ideology ? 0.24f : -0.08f) +
                    (soldier.GetFaction() == party ? 0.18f : 0f) +
                    (soldier.city == capital ? 0.15f : 0f) +
                    programBonus +
                    0.10f * (RevolutionMandate - empire.Mandate) / RevolutionMandate, 0.02f, 0.7f);
                if (UnityEngine.Random.value < chance) defectors.Add(soldier);
            }
            total += InstitutionSystem.TransferDefectingSoldiers(source, rebel, defectors);
        }
        return total;
    }

    private static int RaiseRevolutionaryMilitia(Empire empire, Kingdom rebel, PartyIdeology ideology, bool splitRealm)
    {
        City capital = rebel.capital;
        if (capital == null || capital.isRekt() || capital.units == null) return 0;
        float organization = InstitutionSystem.GetFeature(InstitutionSystem.GetPrimaryCulture(empire),
            IdeologyInstitutionPaths.StageFeature(ideology, 3)) > 0f
            ? IdeologyInstitutionPaths.GetProfile(ideology).MilitiaMultiplier : 1f;
        int target = Mathf.Clamp(Mathf.CeilToInt(empire.CoreKingdom.countTotalWarriors() * 0.3f * organization), 8, 52);
        int needed = Mathf.Max(0, target - rebel.countTotalWarriors());
        if (splitRealm) needed = Mathf.Max(8, needed);
        int raised = 0;
        foreach (Actor resident in capital.units.ToList())
        {
            if (raised >= needed) break;
            if (resident == null || resident.isRekt() || !resident.isAlive() || resident.isWarrior() ||
                !capital.checkCanMakeWarrior(resident)) continue;
            capital.makeWarrior(resident);
            if (resident.isWarrior()) raised++;
        }
        return raised;
    }

    private static void OrganizeRevolutionaryGarrison(City capital, Kingdom rebel)
    {
        if (capital == null || capital.isRekt() || capital.units == null) return;
        capital.checkArmyExistence();
        if (!capital.hasArmy()) EmpireCraftCityBehCheckArmy.CreateNewArmy(capital);
        Army army = capital.army;
        if (army != null && army._captain?.kingdom == rebel)
        {
            foreach (Actor warrior in capital.units.ToList().Where(actor => actor != null && actor.isAlive() &&
                         actor.isWarrior() && actor.kingdom == rebel && !actor.hasArmy()))
            {
                if (army.units.Count >= army._captain.warfare) break;
                warrior.setArmy(army);
            }
        }
    }

    public static bool IsRevolutionWar(War war) =>
        war != null && war.GetOrCreate().republic_revolution_empire_id > 0;

    public static bool HasActiveRevolutionWar(Empire empire) => empire?.CoreKingdom?.getWars()
        ?.Any(war => war != null && !war.hasEnded() && IsRevolutionWar(war)) == true;

    public static void ResolveRevolutionWar(War war, WarWinner winner)
    {
        if (!IsRevolutionWar(war)) return;
        WarExtension.WarExtraData snapshot = war.GetOrCreate();
        foreach (long kingdomId in snapshot.republic_revolution_allied_kingdom_ids ?? new List<long>())
        {
            Kingdom ally = World.world.kingdoms?.get(kingdomId);
            if (ally != null && !ally.isRekt()) ally.EndFactionRebelling();
        }
        Empire empire = ModClass.EMPIRE_MANAGER?.get(snapshot.republic_revolution_empire_id);
        if (empire?.CoreKingdom == null || empire.IsArchived()) return;
        FixedFaction party = PartySystem.GetParties(empire)
            .FirstOrDefault(candidate => candidate.GetID() == snapshot.republic_revolution_party_id);
        Actor leader = World.world.units?.get(snapshot.republic_revolution_leader_id);
        if (leader == null || leader.isRekt() || !leader.isAlive()) leader = war.getMainAttacker()?.king;
        Kingdom rebelKingdom = war.getMainAttacker();
        if (winner == WarWinner.Attackers && rebelKingdom != null && !rebelKingdom.isRekt())
        {
            if (snapshot.republic_revolution_split_realm)
                empire.join(rebelKingdom, pForce: true, pLegitimacyTransfer: true);
            Establish(empire, party, snapshot.republic_revolution_ideology, leader,
                OnePartyIdeologies.Contains(snapshot.republic_revolution_ideology),
                "republic_revolution_history");
            return;
        }
        if (winner == WarWinner.Defenders)
        {
            party?.BanFaction();
            Record(empire, string.Format(LM.Get("republic_revolution_failed_history"),
                snapshot.republic_revolution_party_name, empire.GetEmpireName()), leader);
        }
    }

    private static void Establish(Empire empire, FixedFaction party, bool oneParty, string historyKey) =>
        Establish(empire, party, party.Ideology, party.GetLeader(), oneParty, historyKey);

    private static void Establish(Empire empire, FixedFaction party, PartyIdeology ideology, Actor leader,
        bool oneParty, string historyKey)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null) return;
        string formerEmperor = empire.Emperor?.getName() ?? LM.Get("label_none");
        state.previous_regime = (int)empire.CoreKingdom.GetRegime().type;
        state.deposed_royal_clan_id = empire.data.empire_specific_clan;
        state.is_republic = true;
        state.republic_ideology = ideology;
        state.republic_since = World.world.getCurWorldTime();
        ChooseCalendar(state);
        state.one_party_id = oneParty && party != null ? party.GetID() : "";
        BeginTransition(state);

        TransitionRegime(empire, RegimeType.Modern);
        empire.CoreKingdom.RemoveHeir();
        empire.CoreKingdom.GetOrCreate().is_need_to_choose_heir = false;
        empire.CoreKingdom.GetOrCreate().ideology_country_suffix = PartySystem.PickCountrySuffix(empire, ideology);
        if (oneParty && party != null)
            foreach (FixedFaction other in PartySystem.GetParties(empire).Where(other => other != party).ToList())
                other.BanFaction();
        if (leader != null) empire.InstallHeadOfState(leader);
        empire.data.year_name = "";

        EnsureIdeologyBureau(empire);
        IdeologySpreadSystem.SetStateIdeology(InstitutionSystem.GetPrimaryCulture(empire), ideology, empire);
        EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(empire.CoreKingdom);
        Record(empire, string.Format(LM.Get(historyKey), formerEmperor, party?.Name ?? "",
            PartySystem.GetIdeologyName(ideology), empire.GetEmpireFullName()), leader);
        Record(empire, LM.Get("republic_provisional_government_history"), leader);
        RecordCalendarChoice(empire);
    }

    private static void BeginTransition(ConstitutionalEconomyState state)
    {
        state.republic_transition_stage = 1;
        state.republic_transition_started = World.world.getCurWorldTime();
        state.republic_first_election_pending = true;
        ParliamentSystem.Dissolve(state);
    }

    public static void UpdateTransition(Empire empire)
    {
        if (!IsTransitioning(empire)) return;
        ConstitutionalEconomyState state = State(empire);
        if (state.republic_transition_started < 0)
            state.republic_transition_started = World.world.getCurWorldTime();
        int years = Date.getYearsSince(state.republic_transition_started);
        if (years >= FirstElectionAfterYears)
        {
            state.republic_transition_stage = 0;
            Record(empire, LM.Get("republic_constitution_adopted_history"), empire.Emperor);
        }
        else if (years >= DraftingStartsAfterYears && state.republic_transition_stage == 1)
        {
            state.republic_transition_stage = 2;
            Record(empire, LM.Get("republic_constitution_drafting_history"), empire.Emperor);
        }
    }

    public static void OnFirstRepublicElection(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state?.republic_first_election_pending != true || IsTransitioning(empire) ||
            state.parliament_seats?.Count == 0) return;
        state.republic_first_election_pending = false;
        Record(empire, LM.Get("republic_first_election_history"), ParliamentSystem.GetPrimeMinister(empire));
    }

    #endregion

    #region 复辟帝制

    public static bool CanRestore(Empire empire, FixedFaction party)
    {
        if (!IsRepublic(empire) || party == null || !party.IsParty || party.Ban ||
            party.Ideology != PartyIdeology.Conservatism) return false;
        ConstitutionalEconomyState state = State(empire);
        int total = state?.parliament_seats?.Count ?? 0;
        return total > 0 && CanCoup(empire, party) &&
               ParliamentSystem.IsGoverningFaction(empire, party.GetID()) &&
               state.parliament_seats.Count(seat => seat.faction_id == party.GetID()) * 2 > total;
    }

    public static void Restore(Empire empire, FixedFaction party)
    {
        if (!CanRestore(empire, party)) return;
        ConstitutionalEconomyState state = State(empire);
        RegimeType previous = state.previous_regime >= 0 ? (RegimeType)state.previous_regime : RegimeType.Feudalism;
        state.is_republic = false;
        state.republic_calendar_mode = 0;
        state.one_party_id = "";
        state.republic_transition_stage = 0;
        state.republic_first_election_pending = false;
        empire.CoreKingdom.GetOrCreate().ideology_country_suffix = "";
        TransitionRegime(empire, previous);
        empire.data.has_year_name = empire.CoreKingdom.GetRegime()?.HasEraName() == true;
        // 原皇族有能继承的在世者就迎回，否则由执政的保守党领袖登基
        Actor monarch = SpecificClanManager.Get(state.deposed_royal_clan_id)?.all_valid_members
                            .Select(identity => identity._actor)
                            .FirstOrDefault(actor => actor != null && !actor.isRekt() && actor.isAdult())
                        ?? party.GetLeader();
        if (monarch != null)
        {
            if (empire.Emperor?.id == monarch.id)
            {
                empire.EmperorLeft();
                empire.NewEmperor(monarch, isNew: true);
            }
            else empire.InstallHeadOfState(monarch);
        }
        EnsureIdeologyBureau(empire);
        EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(empire.CoreKingdom);
        Record(empire, string.Format(LM.Get("republic_restoration_history"), party.Name,
            monarch?.getName() ?? LM.Get("label_none"), empire.GetEmpireFullName()), monarch);
    }

    #endregion

    #region 元首更替

    // 共和国元首：多党制下每届大选后由执政党领袖(总理)出任；一党制下为执政党领袖，领袖换人即换
    public static void UpdateHeadOfState(Empire empire)
    {
        if (!IsRepublic(empire) || IsTransitioning(empire) ||
            State(empire)?.republic_first_election_pending == true) return;
        Actor head = IsOneParty(empire)
            ? PartySystem.GetParties(empire).FirstOrDefault(party => party.GetID() == State(empire).one_party_id)?.GetLeader()
            : ParliamentSystem.GetPrimeMinister(empire);
        head ??= empire.CoreKingdom.GetRegime()?.GetDominateFaction()?.GetLeader();
        if (head == null || head.isRekt() || head.id == empire.Emperor?.id) return;
        string previous = empire.Emperor?.getName() ?? LM.Get("label_none");
        if (!empire.InstallHeadOfState(head)) return;
        Record(empire, string.Format(LM.Get("republic_head_of_state_history"), head.getName(), previous), head);
    }

    #endregion

    #region 理念政体机构(只作用于本帝国，不随文化同步)

    private sealed class IdeologyBureau
    {
        public List<BureauSetting> cores = new();
        public List<BureauSetting> division = new();
    }

    private static Dictionary<string, IdeologyBureau> _bureaus;

    private static Dictionary<string, IdeologyBureau> Bureaus
    {
        get
        {
            if (_bureaus != null) return _bureaus;
            _bureaus = new Dictionary<string, IdeologyBureau>();
            try
            {
                string path = global::System.IO.Path.Combine(ModClass._declare.FolderPath, "IdeologyBureaus.json");
                if (global::System.IO.File.Exists(path))
                    _bureaus = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, IdeologyBureau>>(
                        global::System.IO.File.ReadAllText(path)) ?? _bureaus;
            }
            catch (Exception exception)
            {
                NeoModLoader.services.LogService.LogError($"读取 IdeologyBureaus.json 失败: {exception}");
            }
            return _bureaus;
        }
    }

    // 共和国核心国的中央机构按执政理念成套；其他情况返回 null(用政体默认机构)
    public static BureauConfig GetBureauOverride(Kingdom kingdom, BureauConfig fallback)
    {
        Empire empire = kingdom?.GetEmpire();
        if (empire == null || empire.CoreKingdom != kingdom || !IsRepublic(empire)) return null;
        if (!Bureaus.TryGetValue(State(empire).republic_ideology.ToString(), out IdeologyBureau bureau) ||
            bureau?.cores == null || bureau.division == null) return null;
        return new BureauConfig
        {
            cores = bureau.cores,
            division = bureau.division,
            harems = new List<BureauSetting>(),
            kingdoms = fallback?.kingdoms,
            cities = fallback?.cities,
            armies = fallback?.armies
        };
    }

    // 执政理念变了(改制共和、切换路线、复辟……)就把中央机构重建一次
    public static void EnsureIdeologyBureau(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || empire.CoreKingdom == null || empire.CoreKingdom.GetRegime()?.bureau_config == null) return;
        string wanted = IsRepublic(empire) ? state.republic_ideology.ToString() : "";
        if ((state.bureau_ideology ?? "") == wanted) return;
        state.bureau_ideology = wanted;
        empire.data.centerOffice ??= new CenterOffice();
        empire.data.centerOffice.Init(empire.CoreKingdom);
    }

    #endregion

    #region 与实际政体同步(玩家可随时在政体窗口切换国家制度)

    // 共和标记跟着实际政体走：切到现代共和政体就算改行共和(召开议会，理念取第一大党的，没有政党取本文化
    // 主导理念，再没有就是中间派)；切回任何君主政体就恢复君主制。君主制期间记下最近的君主政体，复辟时回到它。
    // manual = 玩家在政体窗口手动切换。只有手动切换(或复辟帝制)才算真的恢复君主制；
    // 其他代码路径把共和国的政体改回君主制，一律视为误改，直接改回现代共和政体。
    public static void SyncWithRegime(Empire empire, bool manual = false)
    {
        ConstitutionalEconomyState state = State(empire);
        RegimeType? current = empire?.CoreKingdom?.GetRegime()?.type;
        if (state == null || current == null || World.world == null) return;
        if (RegimeManager.IsMonarchy(current))
        {
            if (!state.is_republic)
            {
                state.previous_regime = (int)current.Value;
                return;
            }
            if (!manual)
            {
                // 改回失败(配置问题等)就放弃这次改制，别每次更新都重试刷一遍错误
                try
                {
                    TransitionRegime(empire, RegimeType.Modern);
                    return;
                }
                catch (Exception exception)
                {
                    NeoModLoader.services.LogService.LogError($"共和国政体恢复失败，改为君主制: {exception}");
                }
            }
            state.previous_regime = (int)current.Value;
            state.is_republic = false;
            state.republic_calendar_mode = 0;
            state.one_party_id = "";
            state.republic_transition_stage = 0;
            state.republic_first_election_pending = false;
            empire.CoreKingdom.GetOrCreate().ideology_country_suffix = "";
            empire.data.empire_type_key = "";
            empire.data.has_year_name = empire.CoreKingdom.GetRegime()?.HasEraName() == true;
            Actor monarch = empire.Emperor;
            if (monarch != null && !monarch.isRekt())
            {
                empire.EmperorLeft();
                empire.NewEmperor(monarch, isNew: true);
            }
            Record(empire, string.Format(LM.Get("republic_manual_monarchy_history"), empire.GetEmpireFullName()), null);
            return;
        }
        if (state.is_republic || current != RegimeType.Modern) return;
        FixedFaction largest = PartySystem.GetParties(empire).OrderByDescending(party => party.CentralRatio).FirstOrDefault();
        PartyIdeology ideology = largest?.Ideology ??
            (IdeologySpreadSystem.TryGetStateIdeology(InstitutionSystem.GetPrimaryCulture(empire), out PartyIdeology stateIdeology)
                ? stateIdeology : PartyIdeology.Centrism);
        state.is_republic = true;
        state.republic_ideology = ideology;
        state.republic_since = World.world.getCurWorldTime();
        ChooseCalendar(state);
        state.one_party_id = "";
        state.deposed_royal_clan_id = empire.data.empire_specific_clan;
        BeginTransition(state);
        if (largest != null)
            empire.CoreKingdom.GetOrCreate().ideology_country_suffix = PartySystem.PickCountrySuffix(empire, ideology);
        empire.data.empire_type_key = "";
        empire.data.year_name = "";
        Record(empire, string.Format(LM.Get("republic_manual_republic_history"), empire.GetEmpireFullName()), null);
        RecordCalendarChoice(empire);
    }

    // 换政体会从模板重建政体(派系表被重置)：有政党时把政党和中央占比原样搬到新政体上
    public static void CarryPartiesAcrossRegimeChange(Kingdom core, Action change)
    {
        List<FixedFaction> factions = core?.GetRegime()?.GetPlayerFactions()?.ToList() ?? new List<FixedFaction>();
        bool hasParties = factions.Any(faction => faction != null && faction.IsParty);
        Dictionary<FixedFaction, int> ratios = new(core?.GetOrCreate().FactionRatio ?? new Dictionary<FixedFaction, int>());
        change();
        if (!hasParties || core == null) return;
        Regime regime = core.GetRegime();
        Empire empire = core.GetEmpire();
        if (regime == null || empire == null) return;
        regime.PlayerFactions = factions;
        foreach (FixedFaction faction in factions)
        {
            faction.EmpireId = empire.getID();
            faction.FixMissedTemporaryFactions();
        }
        core.GetOrCreate().FactionRatio = ratios;
        core.ReconcileFactionRatios(factions);
        regime.FactionSpace = null;
    }

    #endregion

    #region 元首与反对党

    // 元首去世/离任：立即由总理(没有就是主导派系领袖)接任，不等下一届大选
    public static void EnsureHeadOfState(Empire empire)
    {
        if (!IsRepublic(empire)) return;
        Actor head = empire.Emperor;
        if (head != null && !head.isRekt() && head.isAlive()) return;
        if (IsTransitioning(empire) || State(empire)?.republic_first_election_pending == true)
        {
            Actor successor = PartySystem.GetParties(empire)
                                  .FirstOrDefault(party => party.Ideology == State(empire).republic_ideology)
                                  ?.GetLeader()
                              ?? empire.CoreKingdom.units?.Where(actor => actor != null && !actor.isRekt() &&
                                  actor.isAlive() && actor.isAdult()).OrderByDescending(actor => actor.renown)
                                  .FirstOrDefault();
            if (successor != null && empire.InstallHeadOfState(successor))
                Record(empire, string.Format(LM.Get("republic_head_of_state_history"),
                    successor.getName(), head?.getName() ?? LM.Get("label_none")), successor);
            return;
        }
        UpdateHeadOfState(empire);
    }

    public static Actor GetOppositionLeader(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state?.parliament_seats == null || state.parliament_seats.Count == 0) return null;
        FixedFaction opposition = state.parliament_seats
            .Where(seat => !ParliamentSystem.IsGoverningFaction(empire, seat.faction_id))
            .GroupBy(seat => seat.faction_id).OrderByDescending(group => group.Count())
            .Select(group => PartySystem.GetParties(empire).FirstOrDefault(party => party.GetID() == group.Key))
            .FirstOrDefault(party => party != null);
        return opposition?.GetLeader();
    }

    public static int GetRepublicYears(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        return state == null || state.republic_since < 0 ? 0 : Date.getYearsSince(state.republic_since) + 1;
    }

    public static int GetCalendarMode(Empire empire)
    {
        int mode = State(empire)?.republic_calendar_mode ?? 0;
        return mode == CommonEraCalendar ? CommonEraCalendar : RepublicEraCalendar;
    }

    public static string GetCalendarText(Empire empire)
    {
        if (!IsRepublic(empire)) return "";
        int mode = GetCalendarMode(empire);
        int year = mode == CommonEraCalendar
            ? Date.getYearsSince(0d) + 1
            : GetRepublicYears(empire);
        return string.Format(LM.Get(mode == CommonEraCalendar
            ? "republic_common_era_format" : "republic_calendar_format"), year);
    }

    public static void SetCalendarMode(Empire empire, int mode)
    {
        if (!IsRepublic(empire) || (mode != CommonEraCalendar && mode != RepublicEraCalendar)) return;
        ConstitutionalEconomyState state = State(empire);
        if (state.republic_calendar_mode == mode) return;
        state.republic_calendar_mode = mode;
        RecordCalendarChoice(empire);
    }

    private static void ChooseCalendar(ConstitutionalEconomyState state)
    {
        state.republic_calendar_mode = UnityEngine.Random.value < 0.5f
            ? CommonEraCalendar : RepublicEraCalendar;
    }

    private static void RecordCalendarChoice(Empire empire)
    {
        string content = string.Format(LM.Get("republic_calendar_chosen_history"),
            empire.GetEmpireFullName(), GetCalendarMode(empire) == CommonEraCalendar
                ? LM.Get("republic_common_era_label") : LM.Get("republic_era_label"));
        Record(empire, content, null);
    }

    #endregion

    #region 工具

    private static void TransitionRegime(Empire empire, RegimeType regimeType)
    {
        CarryPartiesAcrossRegimeChange(empire.CoreKingdom,
            () => InstitutionSystem.ChangeRegimeForTransition(empire, regimeType));
        empire.data.empire_type_key = "";
    }

    private static void Record(Empire empire, string content, Actor actor)
    {
        empire.RecordHistory(directContent: content, actorId: actor?.id ?? -1L, kingdomId: empire.CoreKingdom.id);
        actor?.RecordPersonalHistory(content, "republic");
        TranslateHelper.LogEventMessage(content, empire.CoreKingdom);
    }

    #endregion
}

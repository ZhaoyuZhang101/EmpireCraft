using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 宪法系统(数据见 ConstitutionData)。
//   · 共和过渡期(临时政府、制宪会议)先颁行临时约法，过渡结束颁布正式宪法；
//   · 君主立宪：议会召开(或已完成立宪)时颁布；
//   · 国体变了(君宪改共和、共和复辟)就废止旧宪、另立新宪，第几部宪法随之累加；回到非立宪君主制则宪法废止。
// 正式宪法由制宪会议起草：各政党按议席(无议会按中央占比)为本党理念的条款投票，每条取得票最多的方案。
// 由现有制度推出的条款(国体、权力中心、元首产生、选举权、政党制度)每年按实际状况同步；
// 玩家在宪法页改过的条款被"手定"，不再同步，并由各系统反过来遵守：
//   · 选举权 → PartySystem.HasUniversalSuffrage；权力中心(共和) → PartyBanSystem.UsesDemocraticCentralism；
//   · 元首产生 → 元首更替的史书措辞；政党制度 → 立即开关党禁，此后独裁、民意等合法途径不再改动它，
//     只有政变、革命这类法外手段能推翻(推翻时解除手定，见 PartyBanSystem.Close/Open)。
// 央地关系、经济、宗教、紧急状态、修宪程序、任期暂只记录，后续步骤生效。
// 每次 ConstitutionalEconomySystem.Update 调用；只有宪法种类变化或满一年才重新推导，开销很小。
public static class ConstitutionSystem
{
    private enum Kind { None, Provisional, Full }

    public const string ClauseFormOfState = "form_of_state";
    public const string ClauseFoundingIdeology = "founding_ideology";
    public const string ClausePowerCenter = "power_center";
    public const string ClauseHeadSelection = "head_selection";
    public const string ClauseSuffrage = "suffrage";
    public const string ClausePartySystem = "party_system";
    public const string ClauseTerritory = "territory";
    public const string ClauseEconomy = "economy";
    public const string ClauseReligion = "religion";
    public const string ClauseEmergency = "emergency";
    public const string ClauseAmendment = "amendment";
    public const string ClauseIdeologyIntensity = "ideology_intensity";
    public const string ClauseSpeech = "speech";
    public const string ClauseNation = "nation";
    public const string ClauseCivilService = "civil_service";
    public const string ClauseFarmland = "farmland";
    public const string ClauseGrain = "grain";
    public const string ClauseTerm = "term";
    // 宪法页上"任期"拆成两行编辑，共用 ClauseTerm 的手定标记
    public const string ClauseTermYears = "term_years";
    public const string ClauseMaxTerms = "max_terms";

    public static readonly string[] ClauseNames =
    {
        ClauseFormOfState, ClausePowerCenter, ClauseHeadSelection, ClauseSuffrage, ClausePartySystem,
        ClauseTerritory, ClauseEconomy, ClauseReligion, ClauseEmergency, ClauseAmendment, ClauseTerm,
        ClauseIdeologyIntensity, ClauseSpeech, ClauseNation, ClauseCivilService, ClauseFarmland, ClauseGrain
    };

    // 宪法页的编辑行顺序
    public static readonly string[] EditableRows =
    {
        ClauseFormOfState, ClauseFoundingIdeology, ClauseIdeologyIntensity, ClauseSpeech, ClauseNation, ClausePowerCenter, ClauseHeadSelection, ClauseSuffrage, ClausePartySystem,
        ClauseCivilService, ClauseFarmland, ClauseGrain,
        ClauseTerritory, ClauseEconomy, ClauseReligion, ClauseEmergency, ClauseAmendment, ClauseTermYears,
        ClauseMaxTerms
    };

    private static readonly HashSet<string> DerivedClauses = new()
        { ClauseFormOfState, ClausePowerCenter, ClauseHeadSelection, ClauseSuffrage, ClausePartySystem };

    // 共和建立满这么久的旧存档：补建宪法时不再写"颁布"史书，免得读档时一口气刷出来
    private const int SilentMigrationYears = 1;
    // 游戏里的一年很短，派系决议动辄要推进好几年：任期 5~20 年，默认 10 年
    public const int MinTermYears = 5;
    public const int MaxTermYears = 20;
    private const int MaxTermLimit = 4;

    public static ConstitutionData Get(Empire empire) => empire?.data?.constitutional_economy?.constitution;

    public static ConstitutionClauses GetClauses(Empire empire) => Get(empire)?.clauses;

    public static bool HasConstitution(Empire empire) => Get(empire) != null;

    public static bool IsDerived(string clause) => DerivedClauses.Contains(clause);

    public static bool IsPlayerLocked(Empire empire, string clause) =>
        Get(empire)?.player_locked?.Contains(LockKey(clause)) == true;

    private static string LockKey(string clause) =>
        clause is ClauseTermYears or ClauseMaxTerms ? ClauseTerm : clause;

    #region 各系统读取手定条款

    // 手定了选举权时返回 true 并给出条款值；没手定时各系统照旧按制度判断
    public static bool TryGetLockedSuffrage(Empire empire, out ConstitutionSuffrage suffrage)
    {
        ConstitutionData constitution = Get(empire);
        suffrage = constitution?.clauses?.suffrage ?? ConstitutionSuffrage.None;
        return constitution != null && IsPlayerLocked(empire, ClauseSuffrage);
    }

    public static bool TryGetLockedPowerCenter(Empire empire, out ConstitutionPowerCenter powerCenter)
    {
        ConstitutionData constitution = Get(empire);
        powerCenter = constitution?.clauses?.power_center ?? ConstitutionPowerCenter.Presidential;
        return constitution != null && IsPlayerLocked(empire, ClausePowerCenter);
    }

    public static bool TryGetLockedHeadSelection(Empire empire, out ConstitutionHeadSelection headSelection)
    {
        ConstitutionData constitution = Get(empire);
        headSelection = constitution?.clauses?.head_selection ?? ConstitutionHeadSelection.PartyNomination;
        return constitution != null && IsPlayerLocked(empire, ClauseHeadSelection);
    }

    // 政变、革命等法外手段改变了实际制度：解除该条的手定，下次同步按实际状况改写(记为修正)
    public static void ReleaseLock(Empire empire, string clause)
    {
        ConstitutionData constitution = Get(empire);
        if (constitution?.player_locked == null || !constitution.player_locked.Remove(LockKey(clause))) return;
        constitution.last_sync = -1d;
    }

    #endregion

    #region 年度更新

    public static void Update(Empire empire, ConstitutionalEconomyState state)
    {
        if (state == null || empire?.CoreKingdom == null || World.world == null) return;
        Kind kind = RequiredKind(empire, state);
        ConstitutionData current = state.constitution;
        if (kind == Kind.None)
        {
            if (current != null) Abolish(empire, state);
            return;
        }
        ConstitutionFormOfState form = state.is_republic ? ConstitutionFormOfState.Republic
            : ConstitutionFormOfState.ConstitutionalMonarchy;
        bool needNew = current == null ||
                       current.provisional != (kind == Kind.Provisional) ||
                       current.clauses == null || current.clauses.form_of_state != form;
        if (needNew)
        {
            if (current != null && current.clauses?.form_of_state != form && !current.provisional)
                Abolish(empire, state);
            Promulgate(empire, state, kind == Kind.Provisional);
            return;
        }
        // 旧存档的任期(以前 2~8 年)按 2.5 倍换算到现在的范围
        if (current.clauses != null && !current.clauses.term_rescaled)
        {
            current.clauses.term_years = UnityEngine.Mathf.Clamp(
                UnityEngine.Mathf.RoundToInt(current.clauses.term_years * 2.5f), MinTermYears, MaxTermYears);
            current.clauses.term_rescaled = true;
        }
        if (current.last_sync >= 0d && Date.getYearsSince(current.last_sync) < 1) return;
        SyncDerived(empire, state, current);
    }

    // 共和：过渡期为临时约法，过渡结束为正式宪法；君主立宪：议会召开或已完成立宪
    private static Kind RequiredKind(Empire empire, ConstitutionalEconomyState state)
    {
        // 共和过渡期、以及军阀时期另立的临时政府：施行临时约法；取胜成为中央后颁布正式宪法
        if (state.is_republic)
            return state.republic_transition_stage > 0 || WarlordEraSystem.IsProvisionalGovernment(empire)
                ? Kind.Provisional
                : Kind.Full;
        if (!RegimeManager.IsMonarchy(empire.CoreKingdom.GetRegime()?.type)) return Kind.None;
        return state.constitutional_monarchy || ParliamentSystem.HasParliament(empire) ? Kind.Full : Kind.None;
    }

    #endregion

    #region 颁布与废止

    private static void Promulgate(Empire empire, ConstitutionalEconomyState state, bool provisional)
    {
        bool migrating = state.constitution == null && IsLegacyModernState(empire, state);
        var constitution = new ConstitutionData
        {
            provisional = provisional,
            promulgated_at = World.world.getCurWorldTime(),
            clauses = new ConstitutionClauses { term_rescaled = true }
        };
        if (!provisional)
        {
            state.constitution_count++;
            constitution.number = state.constitution_count;
        }
        // 临时约法由执政一方拟定；正式宪法由制宪会议表决
        string drafter = provisional ? "" : DraftByConvention(empire, state, constitution.clauses);
        if (provisional)
            ApplyTemplate(Template(IdeologyFamilies.StateIdeology(empire), !state.is_republic,
                RepublicSystem.IsOneParty(empire), InstitutionSystem.GetPrimaryCulture(empire)), constitution.clauses);
        SyncDerived(empire, state, constitution, record: false);
        string country = empire.GetEmpireFullName();
        constitution.name = ConstitutionName(empire, provisional);
        state.constitution = constitution;
        if (migrating) return;
        string key = provisional ? "constitution_provisional_history"
            : constitution.number > 1 ? "constitution_renewed_history" : "constitution_promulgated_history";
        EventRecorder.Record(empire, string.Format(LM.Get(key), country, constitution.name, constitution.number));
        if (!string.IsNullOrEmpty(drafter))
            EventRecorder.Record(empire, string.Format(LM.Get("constitution_convention_history"), country,
                constitution.name, drafter));
    }

    private static void Abolish(Empire empire, ConstitutionalEconomyState state)
    {
        ConstitutionData old = state.constitution;
        state.constitution = null;
        if (old == null || old.provisional) return;
        EventRecorder.Record(empire, string.Format(LM.Get("constitution_abolished_history"),
            empire.GetEmpireFullName(), old.name));
    }

    // 读旧存档时已经是现代国家很久了：补建宪法不写史书
    private static bool IsLegacyModernState(Empire empire, ConstitutionalEconomyState state)
    {
        if (state.constitution_count > 0) return false;
        if (state.is_republic)
            return state.republic_since >= 0d && Date.getYearsSince(state.republic_since) >= SilentMigrationYears;
        return state.constitutional_monarchy;
    }

    #endregion

    #region 制宪会议

    // 制宪会议上各理念主张的条款，读自 ConstitutionTemplates.json(读不到用内置默认)
    private sealed class TemplateFile
    {
        public Dictionary<string, string> defaults = new();
        public Dictionary<string, Dictionary<string, string>> ideologies = new();
        public Dictionary<string, Dictionary<string, string>> monarchy = new();
        public Dictionary<string, string> one_party = new();
    }

    private static TemplateFile _templates;

    private static TemplateFile Templates
    {
        get
        {
            if (_templates != null) return _templates;
            _templates = new TemplateFile();
            try
            {
                string path = global::System.IO.Path.Combine(ModClass._declare.FolderPath, "ConstitutionTemplates.json");
                if (global::System.IO.File.Exists(path))
                    _templates = Newtonsoft.Json.JsonConvert.DeserializeObject<TemplateFile>(
                        global::System.IO.File.ReadAllText(path)) ?? _templates;
            }
            catch (Exception exception)
            {
                NeoModLoader.services.LogService.LogError($"读取 ConstitutionTemplates.json 失败: {exception}");
            }
            return _templates;
        }
    }

    // 某个理念主张的宪法(只含颁布时定下的条款)：默认 → 理念 → 君主立宪 → 一党制 → 本文化倾向，依次覆盖
    private static ConstitutionClauses Template(PartyIdeology ideology, bool monarchy, bool oneParty, string culture)
    {
        var clauses = new ConstitutionClauses { term_rescaled = true };
        TemplateFile file = Templates;
        ApplyValues(clauses, file.defaults);
        if (file.ideologies != null && file.ideologies.TryGetValue(ideology.ToString(), out var byIdeology))
            ApplyValues(clauses, byIdeology);
        if (monarchy && file.monarchy != null && file.monarchy.TryGetValue(ideology.ToString(), out var byMonarchy))
            ApplyValues(clauses, byMonarchy);
        if (oneParty) ApplyValues(clauses, file.one_party);
        ApplyValues(clauses, CultureSetting(culture)?.clauses);
        return clauses;
    }

    // 该理念是否倾向统一：它在 ConstitutionTemplates.json 里主张单一制(不计文化倾向)
    public static bool IdeologyPrefersUnitary(PartyIdeology ideology) =>
        Template(ideology, false, false, null).territory == ConstitutionTerritory.Unitary;

    private static ConstitutionSetting CultureSetting(string culture) =>
        !string.IsNullOrEmpty(culture) && OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting)
            ? setting?.Constitution
            : null;

    // 按条款名写入(忽略不认识的条款名与方案名，免得一处笔误让整部宪法起草失败)
    private static void ApplyValues(ConstitutionClauses clauses, Dictionary<string, string> values)
    {
        if (values == null) return;
        foreach (KeyValuePair<string, string> pair in values)
        {
            string value = pair.Value?.Trim() ?? "";
            switch (pair.Key)
            {
                case ClauseTerritory when Enum.TryParse(value, out ConstitutionTerritory territory):
                    clauses.territory = territory; break;
                case ClauseEconomy when Enum.TryParse(value, out ConstitutionEconomy economy):
                    clauses.economy = economy; break;
                case ClauseReligion when Enum.TryParse(value, out ConstitutionReligion religion):
                    clauses.religion = religion; break;
                case ClauseEmergency when Enum.TryParse(value, out ConstitutionEmergency emergency):
                    clauses.emergency = emergency; break;
                case ClauseAmendment when Enum.TryParse(value, out ConstitutionAmendment amendment):
                    clauses.amendment = amendment; break;
                case ClauseIdeologyIntensity when Enum.TryParse(value, out ConstitutionIdeologyIntensity intensity):
                    clauses.ideology_intensity = intensity; break;
                case ClauseSpeech when Enum.TryParse(value, out ConstitutionSpeech speech):
                    clauses.speech = speech; break;
                case ClauseNation when Enum.TryParse(value, out ConstitutionNation nation):
                    clauses.nation = nation; break;
                case ClauseCivilService when Enum.TryParse(value, out ConstitutionCivilService civilService):
                    clauses.civil_service = civilService; break;
                case ClauseFarmland when Enum.TryParse(value, out ConstitutionFarmland farmland):
                    clauses.farmland = farmland; break;
                case ClauseGrain when Enum.TryParse(value, out ConstitutionGrain grain):
                    clauses.grain = grain; break;
                case ClauseTermYears when int.TryParse(value, out int years):
                    clauses.term_years = UnityEngine.Mathf.Clamp(years, MinTermYears, MaxTermYears); break;
                case ClauseMaxTerms when int.TryParse(value, out int terms):
                    clauses.max_terms = UnityEngine.Mathf.Clamp(terms, 0, MaxTermLimit); break;
            }
        }
    }

    // 宪法名称：本文化词库里随机取一条({0} 代入国号)，没配用默认
    private static string ConstitutionName(Empire empire, bool provisional)
    {
        string culture = InstitutionSystem.GetPrimaryCulture(empire);
        ConstitutionSetting setting = CultureSetting(culture);
        List<string> pool = PartySystem.GetCultureWordPool(culture,
            provisional ? setting?.provisional_name_group : setting?.name_group);
        string template = pool.Count > 0
            ? pool[UnityEngine.Random.Range(0, pool.Count)]
            : LM.Get(provisional ? "constitution_provisional_name" : "constitution_name");
        return template.Replace("{0}", empire.GetEmpireFullName());
    }

    private static void ApplyTemplate(ConstitutionClauses source, ConstitutionClauses target)
    {
        target.territory = source.territory;
        target.economy = source.economy;
        target.religion = source.religion;
        target.emergency = source.emergency;
        target.amendment = source.amendment;
        target.ideology_intensity = source.ideology_intensity;
        target.speech = source.speech;
        target.nation = source.nation;
        target.civil_service = source.civil_service;
        target.farmland = source.farmland;
        target.grain = source.grain;
        target.term_years = source.term_years;
        target.max_terms = source.max_terms;
    }

    // 各合法政党按议席(无议会按中央占比)为本党理念的主张投票，每条取得票最多的方案；
    // 没有政党时由执政理念一方拟定。返回得票最多的政党名(史书用)，没有政党返回空
    private static string DraftByConvention(Empire empire, ConstitutionalEconomyState state, ConstitutionClauses clauses)
    {
        bool monarchy = !state.is_republic;
        bool oneParty = RepublicSystem.IsOneParty(empire);
        string culture = InstitutionSystem.GetPrimaryCulture(empire);
        int totalSeats = state.parliament_seats?.Count ?? 0;
        var delegates = new List<(FixedFaction party, ConstitutionClauses plan, float weight)>();
        foreach (FixedFaction party in PartySystem.GetParties(empire).Where(party => party != null && !party.Ban))
        {
            float weight = totalSeats > 0
                ? state.parliament_seats.Count(seat => seat.faction_id == party.GetID()) / (float)totalSeats
                : party.CentralRatio / 100f;
            if (weight > 0f) delegates.Add((party, Template(party.Ideology, monarchy, oneParty, culture), weight));
        }
        if (delegates.Count == 0)
        {
            ApplyTemplate(Template(IdeologyFamilies.StateIdeology(empire), monarchy, oneParty, culture), clauses);
            return "";
        }
        T Vote<T>(Func<ConstitutionClauses, T> pick) => delegates
            .GroupBy(delegate_ => pick(delegate_.plan))
            .OrderByDescending(group => group.Sum(delegate_ => delegate_.weight)).First().Key;
        clauses.territory = Vote(plan => plan.territory);
        clauses.economy = Vote(plan => plan.economy);
        clauses.religion = Vote(plan => plan.religion);
        clauses.emergency = Vote(plan => plan.emergency);
        clauses.amendment = Vote(plan => plan.amendment);
        clauses.ideology_intensity = Vote(plan => plan.ideology_intensity);
        clauses.speech = Vote(plan => plan.speech);
        clauses.nation = Vote(plan => plan.nation);
        clauses.civil_service = Vote(plan => plan.civil_service);
        clauses.farmland = Vote(plan => plan.farmland);
        clauses.grain = Vote(plan => plan.grain);
        clauses.max_terms = Vote(plan => plan.max_terms);
        float total = delegates.Sum(delegate_ => delegate_.weight);
        clauses.term_years = UnityEngine.Mathf.Clamp(UnityEngine.Mathf.RoundToInt(
            delegates.Sum(delegate_ => delegate_.plan.term_years * delegate_.weight) / total), MinTermYears, MaxTermYears);
        return delegates.OrderByDescending(delegate_ => delegate_.weight).First().party.Name;
    }

    #endregion

    #region 条款同步

    // 由现有制度推出的条款：按实际状况同步(手定的不动)，变化计入修正记录
    private static void SyncDerived(Empire empire, ConstitutionalEconomyState state, ConstitutionData constitution,
        bool record = true)
    {
        ConstitutionClauses clauses = constitution.clauses ??= new ConstitutionClauses { term_rescaled = true };
        constitution.player_locked ??= new List<string>();
        constitution.amendments ??= new List<ConstitutionAmendmentRecord>();
        bool monarchy = !state.is_republic;
        ConstitutionHeadSelection head = DeriveHeadSelection(empire, monarchy);
        clauses.founding_ideology = monarchy ? IdeologyFamilies.StateIdeology(empire) : state.republic_ideology;
        Set(constitution, ClauseFormOfState, clauses.form_of_state,
            monarchy ? ConstitutionFormOfState.ConstitutionalMonarchy : ConstitutionFormOfState.Republic,
            value => clauses.form_of_state = value, record);
        Set(constitution, ClauseHeadSelection, clauses.head_selection, head,
            value => clauses.head_selection = value, record);
        Set(constitution, ClausePowerCenter, clauses.power_center, DerivePowerCenter(empire, monarchy, head),
            value => clauses.power_center = value, record);
        Set(constitution, ClauseSuffrage, clauses.suffrage, DeriveSuffrage(empire),
            value => clauses.suffrage = value, record);
        Set(constitution, ClausePartySystem, clauses.party_system, DerivePartySystem(empire, state),
            value => clauses.party_system = value, record);
        constitution.last_sync = World.world.getCurWorldTime();
    }

    private static void Set<T>(ConstitutionData constitution, string clause, T current, T derived, Action<T> apply,
        bool record) where T : struct, Enum
    {
        if (EqualityComparer<T>.Default.Equals(current, derived) || constitution.player_locked.Contains(clause)) return;
        apply(derived);
        if (record) AddAmendment(constitution, clause, current.ToString(), derived.ToString(), "sync");
    }

    // 新领导人上台的路线调整(见 IdeologyDynamicsSystem)：改写意识形态强度并记一次修宪；
    // 玩家手定过这一条、或还在临时约法期间的不改
    public static bool SetIdeologyIntensityByLine(Empire empire, ConstitutionIdeologyIntensity value)
    {
        ConstitutionData constitution = Get(empire);
        if (constitution == null || constitution.provisional || IsPlayerLocked(empire, ClauseIdeologyIntensity)) return false;
        ConstitutionIdeologyIntensity before = constitution.clauses.ideology_intensity;
        if (before == value) return false;
        constitution.clauses.ideology_intensity = value;
        AddAmendment(constitution, ClauseIdeologyIntensity, before.ToString(), value.ToString(), "line");
        return true;
    }

    // 迫于压力改变言论自由(见 IdeologyDynamicsSystem)；玩家手定过、或临时约法期间不改
    public static bool SetSpeechByPressure(Empire empire, ConstitutionSpeech value)
    {
        ConstitutionData constitution = Get(empire);
        if (constitution == null || constitution.provisional || IsPlayerLocked(empire, ClauseSpeech)) return false;
        ConstitutionSpeech before = constitution.clauses.speech;
        if (before == value) return false;
        constitution.clauses.speech = value;
        AddAmendment(constitution, ClauseSpeech, before.ToString(), value.ToString(), "pressure");
        return true;
    }

    private static void AddAmendment(ConstitutionData constitution, string clause, string from, string to, string by)
    {
        constitution.amendments ??= new List<ConstitutionAmendmentRecord>();
        constitution.amendments.Add(new ConstitutionAmendmentRecord
        {
            time = World.world.getCurWorldTime(),
            clause = clause,
            from = from,
            to = to,
            by = by
        });
    }

    // 与 RepublicSystem.HeadOfStateHistory 的判定一致
    private static ConstitutionHeadSelection DeriveHeadSelection(Empire empire, bool monarchy)
    {
        if (monarchy) return ConstitutionHeadSelection.Hereditary;
        if (PartyBanSystem.GetAutocracy(empire) >= RepublicSystem.DesignatedSuccessionAutocracy)
            return ConstitutionHeadSelection.Designated;
        if (!ParliamentSystem.HasParliament(empire)) return ConstitutionHeadSelection.PartyNomination;
        if (PartyBanSystem.UsesDemocraticCentralism(empire)) return ConstitutionHeadSelection.CongressVote;
        return PartySystem.HasUniversalSuffrage(empire) ? ConstitutionHeadSelection.PopularVote
            : ConstitutionHeadSelection.ParliamentVote;
    }

    private static ConstitutionPowerCenter DerivePowerCenter(Empire empire, bool monarchy, ConstitutionHeadSelection head)
    {
        if (monarchy)
            return ParliamentSystem.HasResponsibleGovernment(empire) ? ConstitutionPowerCenter.Parliamentary
                : ConstitutionPowerCenter.DualMonarchy;
        return head switch
        {
            ConstitutionHeadSelection.CongressVote => ConstitutionPowerCenter.Congress,
            ConstitutionHeadSelection.ParliamentVote => ConstitutionPowerCenter.Parliamentary,
            _ => ConstitutionPowerCenter.Presidential
        };
    }

    private static ConstitutionSuffrage DeriveSuffrage(Empire empire)
    {
        if (!ParliamentSystem.HasParliament(empire)) return ConstitutionSuffrage.None;
        return PartySystem.HasUniversalSuffrage(empire) ? ConstitutionSuffrage.Universal : ConstitutionSuffrage.Property;
    }

    private static ConstitutionPartySystem DerivePartySystem(Empire empire, ConstitutionalEconomyState state)
    {
        if (!PartySystem.IsActive(empire)) return ConstitutionPartySystem.Banned;
        if (!RepublicSystem.IsOneParty(empire)) return ConstitutionPartySystem.MultiParty;
        return state.allied_party_ids?.Count > 0 ? ConstitutionPartySystem.UnitedFront
            : ConstitutionPartySystem.OneParty;
    }

    #endregion

    // 意识形态强度：没有宪法的国家按"中等"
    public static ConstitutionIdeologyIntensity GetIdeologyIntensity(Empire empire) =>
        Get(empire)?.clauses?.ideology_intensity ?? ConstitutionIdeologyIntensity.Medium;

    // 民族政策：没有宪法的国家按"一般"
    public static ConstitutionNation GetNation(Empire empire) =>
        Get(empire)?.clauses?.nation ?? ConstitutionNation.Moderate;

    // 言论自由：没有宪法的国家按"一般"
    public static ConstitutionSpeech GetSpeech(Empire empire) =>
        Get(empire)?.clauses?.speech ?? ConstitutionSpeech.Limited;

    #region 玩家修改(宪法页)

    // 这一行现在的取值(存档名)；任期两行返回数字
    public static string CurrentValue(ConstitutionClauses clauses, string row) => row switch
    {
        ClauseFormOfState => clauses.form_of_state.ToString(),
        ClauseFoundingIdeology => clauses.founding_ideology.ToString(),
        ClausePowerCenter => clauses.power_center.ToString(),
        ClauseHeadSelection => clauses.head_selection.ToString(),
        ClauseSuffrage => clauses.suffrage.ToString(),
        ClausePartySystem => clauses.party_system.ToString(),
        ClauseTerritory => clauses.territory.ToString(),
        ClauseEconomy => clauses.economy.ToString(),
        ClauseReligion => clauses.religion.ToString(),
        ClauseEmergency => clauses.emergency.ToString(),
        ClauseAmendment => clauses.amendment.ToString(),
        ClauseIdeologyIntensity => clauses.ideology_intensity.ToString(),
        ClauseSpeech => clauses.speech.ToString(),
        ClauseNation => clauses.nation.ToString(),
        ClauseCivilService => clauses.civil_service.ToString(),
        ClauseFarmland => clauses.farmland.ToString(),
        ClauseGrain => clauses.grain.ToString(),
        ClauseTermYears => clauses.term_years.ToString(),
        ClauseMaxTerms => clauses.max_terms.ToString(),
        _ => ""
    };

    // 玩家可选的方案(按显示顺序)；空表示这一行只读(国体、君主立宪的权力中心与元首、临时约法、未开放政党政治的政党制度)
    public static List<string> Options(Empire empire, string row)
    {
        ConstitutionData constitution = Get(empire);
        var options = new List<string>();
        if (constitution == null || constitution.provisional) return options;
        bool monarchy = constitution.clauses.form_of_state == ConstitutionFormOfState.ConstitutionalMonarchy;
        switch (row)
        {
            case ClauseFoundingIdeology:
                // 共和国才有国体可改；可选本文化已掌握的理念(当前的一并列出)
                if (monarchy) break;
                foreach (PartyIdeology ideology in Enum.GetValues(typeof(PartyIdeology)))
                    if (ideology == constitution.clauses.founding_ideology || PartySystem.IsIdeologyUnlocked(empire, ideology))
                        options.Add(ideology.ToString());
                break;
            case ClausePowerCenter:
                if (monarchy) break;
                options.AddRange(new[] { ConstitutionPowerCenter.Presidential, ConstitutionPowerCenter.Parliamentary,
                    ConstitutionPowerCenter.SemiPresidential }.Select(value => value.ToString()));
                if (PartyBanSystem.CanChooseElectoralSystem(empire)) options.Add(ConstitutionPowerCenter.Congress.ToString());
                break;
            case ClauseHeadSelection:
                if (monarchy) break;
                if (PartySystem.HasUniversalSuffragePolicy(empire)) options.Add(ConstitutionHeadSelection.PopularVote.ToString());
                if (ParliamentSystem.HasParliament(empire)) options.Add(ConstitutionHeadSelection.ParliamentVote.ToString());
                if (PartyBanSystem.CanChooseElectoralSystem(empire)) options.Add(ConstitutionHeadSelection.CongressVote.ToString());
                options.Add(ConstitutionHeadSelection.PartyNomination.ToString());
                options.Add(ConstitutionHeadSelection.Designated.ToString());
                break;
            case ClauseSuffrage:
                if (!ParliamentSystem.HasParliament(empire)) break;
                options.Add(ConstitutionSuffrage.Property.ToString());
                options.Add(ConstitutionSuffrage.Male.ToString());
                if (PartySystem.HasUniversalSuffragePolicy(empire)) options.Add(ConstitutionSuffrage.Universal.ToString());
                break;
            case ClausePartySystem:
                if (!PartySystem.IsActive(empire) || RepublicSystem.IsTransitioning(empire)) break;
                options.Add(ConstitutionPartySystem.MultiParty.ToString());
                if (PartyBanSystem.CanUnitedFront(empire)) options.Add(ConstitutionPartySystem.UnitedFront.ToString());
                options.Add(ConstitutionPartySystem.OneParty.ToString());
                break;
            case ClauseTerritory: options.AddRange(Enum.GetNames(typeof(ConstitutionTerritory))); break;
            case ClauseEconomy: options.AddRange(Enum.GetNames(typeof(ConstitutionEconomy))); break;
            case ClauseReligion: options.AddRange(Enum.GetNames(typeof(ConstitutionReligion))); break;
            case ClauseEmergency: options.AddRange(Enum.GetNames(typeof(ConstitutionEmergency))); break;
            case ClauseAmendment: options.AddRange(Enum.GetNames(typeof(ConstitutionAmendment))); break;
            case ClauseIdeologyIntensity:
                options.AddRange(new[] { ConstitutionIdeologyIntensity.High, ConstitutionIdeologyIntensity.Medium,
                    ConstitutionIdeologyIntensity.Low }.Select(value => value.ToString()));
                break;
            case ClauseSpeech:
                options.AddRange(new[] { ConstitutionSpeech.Free, ConstitutionSpeech.Limited, ConstitutionSpeech.Strict }
                    .Select(value => value.ToString()));
                break;
            case ClauseNation:
                options.AddRange(new[] { ConstitutionNation.Pluralist, ConstitutionNation.Moderate,
                    ConstitutionNation.Nationalist }.Select(value => value.ToString()));
                break;
            case ClauseCivilService:
                options.AddRange(new[] { ConstitutionCivilService.Mixed, ConstitutionCivilService.Professional,
                    ConstitutionCivilService.Spoils }.Select(value => value.ToString()));
                break;
            case ClauseFarmland:
                options.AddRange(new[] { ConstitutionFarmland.Adaptive, ConstitutionFarmland.Strict,
                    ConstitutionFarmland.None }.Select(value => value.ToString()));
                break;
            case ClauseGrain:
                options.AddRange(new[] { ConstitutionGrain.Local, ConstitutionGrain.Tribute,
                    ConstitutionGrain.Central }.Select(value => value.ToString()));
                break;
            case ClauseTermYears:
                for (int years = MinTermYears; years <= MaxTermYears; years++) options.Add(years.ToString());
                break;
            case ClauseMaxTerms:
                for (int terms = 0; terms <= MaxTermLimit; terms++) options.Add(terms.ToString());
                break;
        }
        return options;
    }

    // 宪法页点一下：换到下一个方案并手定；由制度推出的条款转完一圈后回到"自动"(解除手定、按实际状况重新推导)
    public static bool CycleByPlayer(Empire empire, string row)
    {
        ConstitutionData constitution = Get(empire);
        List<string> options = Options(empire, row);
        if (constitution == null || options.Count == 0) return false;
        string current = CurrentValue(constitution.clauses, row);
        bool locked = IsPlayerLocked(empire, row);
        int index = options.IndexOf(current);
        int next = index + 1;
        if (IsDerived(row) && locked && next >= options.Count)
        {
            constitution.player_locked.Remove(LockKey(row));
            ConstitutionalEconomyState state = empire.data.constitutional_economy;
            SyncDerived(empire, state, constitution);
            EventRecorder.Record(empire, string.Format(LM.Get("constitution_unlocked_history"),
                empire.GetEmpireFullName(), constitution.name, LM.Get($"constitution_clause_{row}")));
            return true;
        }
        return SetByPlayer(empire, row, options[next % options.Count]);
    }

    public static bool SetByPlayer(Empire empire, string row, string value)
    {
        ConstitutionData constitution = Get(empire);
        ConstitutionalEconomyState state = empire?.data?.constitutional_economy;
        if (constitution == null || state == null || !Options(empire, row).Contains(value)) return false;
        ConstitutionClauses clauses = constitution.clauses;
        string before = CurrentValue(clauses, row);
        switch (row)
        {
            case ClauseFoundingIdeology:
                clauses.founding_ideology = Parse<PartyIdeology>(value);
                RepublicSystem.SetFoundingIdeology(empire, clauses.founding_ideology);
                break;
            case ClausePowerCenter:
                clauses.power_center = Parse<ConstitutionPowerCenter>(value);
                bool congress = clauses.power_center == ConstitutionPowerCenter.Congress;
                if (state.democratic_centralism != congress)
                {
                    state.democratic_centralism = congress;
                    state.last_parliament_election = -1d;
                }
                break;
            case ClauseHeadSelection: clauses.head_selection = Parse<ConstitutionHeadSelection>(value); break;
            case ClauseSuffrage:
                clauses.suffrage = Parse<ConstitutionSuffrage>(value);
                state.last_parliament_election = -1d;
                break;
            case ClausePartySystem:
                ConstitutionPartySystem mode = Parse<ConstitutionPartySystem>(value);
                // 开关党禁会先解除本条手定(见 PartyBanSystem.Close/Open)，成功后下面重新手定
                if (!PartyBanSystem.SetModeByConstitution(empire, mode)) return false;
                clauses.party_system = mode;
                break;
            case ClauseTerritory: clauses.territory = Parse<ConstitutionTerritory>(value); break;
            case ClauseEconomy: clauses.economy = Parse<ConstitutionEconomy>(value); break;
            case ClauseReligion: clauses.religion = Parse<ConstitutionReligion>(value); break;
            case ClauseEmergency: clauses.emergency = Parse<ConstitutionEmergency>(value); break;
            case ClauseAmendment: clauses.amendment = Parse<ConstitutionAmendment>(value); break;
            case ClauseIdeologyIntensity: clauses.ideology_intensity = Parse<ConstitutionIdeologyIntensity>(value); break;
            case ClauseSpeech: clauses.speech = Parse<ConstitutionSpeech>(value); break;
            case ClauseNation: clauses.nation = Parse<ConstitutionNation>(value); break;
            case ClauseCivilService: clauses.civil_service = Parse<ConstitutionCivilService>(value); break;
            case ClauseFarmland: clauses.farmland = Parse<ConstitutionFarmland>(value); break;
            case ClauseGrain: clauses.grain = Parse<ConstitutionGrain>(value); break;
            case ClauseTermYears: clauses.term_years = int.Parse(value); break;
            case ClauseMaxTerms: clauses.max_terms = int.Parse(value); break;
            default: return false;
        }
        constitution.player_locked ??= new List<string>();
        string lockKey = LockKey(row);
        // 国体直接改写立国理念本身，不需要手定
        if (row != ClauseFoundingIdeology && !constitution.player_locked.Contains(lockKey))
            constitution.player_locked.Add(lockKey);
        if (before == value) return true;
        AddAmendment(constitution, row, before, value, "player");
        EventRecorder.Record(empire, string.Format(LM.Get("constitution_amended_history"), empire.GetEmpireFullName(),
            constitution.name, LM.Get($"constitution_clause_{row}"), ValueText(row, before), ValueText(row, value)));
        return true;
    }

    // 条款取值的显示文字
    public static string ValueText(string row, string value) => row switch
    {
        ClauseFoundingIdeology => Enum.TryParse(value, out PartyIdeology ideology)
            ? PartySystem.GetIdeologyName(ideology)
            : value,
        ClauseTermYears => string.Format(LM.Get("constitution_term_years_value"), value),
        ClauseMaxTerms => value == "0" ? LM.Get("constitution_max_terms_unlimited")
            : string.Format(LM.Get("constitution_max_terms_value"), value),
        _ => LM.Get($"constitution_{row}_{value}")
    };

    private static T Parse<T>(string value) where T : struct, Enum => (T)Enum.Parse(typeof(T), value);

    #endregion

    #region 执政党施政议程(见 ParliamentSystem.UpdateAgenda)

    // 执政党可以通过议会推动修改的条款(国体、政党制度这些随制度变化的条款不在其内)
    public static readonly string[] AgendaClauses =
    {
        ClauseEconomy, ClauseTerritory, ClauseReligion, ClauseSpeech, ClauseNation, ClauseIdeologyIntensity,
        ClauseEmergency, ClauseCivilService, ClauseFarmland, ClauseGrain
    };

    // 某个理念的政党在这一条上的主张(与制宪会议用的同一套理念模板)
    public static string PartyPosition(Empire empire, PartyIdeology ideology, string clause)
    {
        ConstitutionalEconomyState state = empire?.data?.constitutional_economy;
        if (state == null) return "";
        return CurrentValue(Template(ideology, !state.is_republic, RepublicSystem.IsOneParty(empire),
            InstitutionSystem.GetPrimaryCulture(empire)), clause);
    }

    // 修宪所需的议席占比：议会特别多数 2/3，公投与党代会过半
    public static float AmendmentThreshold(Empire empire) =>
        GetClauses(empire)?.amendment == ConstitutionAmendment.ParliamentSupermajority ? 2f / 3f : 0.5f;

    // 议会通过执政党的议程：改写条款并记一次修宪(史书由调用方写)；玩家手定过的条款不改
    public static bool AmendByGovernment(Empire empire, string clause, string value, string partyId)
    {
        ConstitutionData constitution = Get(empire);
        if (constitution == null || constitution.provisional || IsPlayerLocked(empire, clause)) return false;
        ConstitutionClauses clauses = constitution.clauses;
        string before = CurrentValue(clauses, clause);
        if (before == value) return false;
        switch (clause)
        {
            case ClauseTerritory: clauses.territory = Parse<ConstitutionTerritory>(value); break;
            case ClauseEconomy: clauses.economy = Parse<ConstitutionEconomy>(value); break;
            case ClauseReligion: clauses.religion = Parse<ConstitutionReligion>(value); break;
            case ClauseEmergency: clauses.emergency = Parse<ConstitutionEmergency>(value); break;
            case ClauseIdeologyIntensity: clauses.ideology_intensity = Parse<ConstitutionIdeologyIntensity>(value); break;
            case ClauseSpeech: clauses.speech = Parse<ConstitutionSpeech>(value); break;
            case ClauseNation: clauses.nation = Parse<ConstitutionNation>(value); break;
            case ClauseCivilService: clauses.civil_service = Parse<ConstitutionCivilService>(value); break;
            case ClauseFarmland: clauses.farmland = Parse<ConstitutionFarmland>(value); break;
            case ClauseGrain: clauses.grain = Parse<ConstitutionGrain>(value); break;
            default: return false;
        }
        AddAmendment(constitution, clause, before, value, $"party:{partyId}");
        return true;
    }

    #endregion
}

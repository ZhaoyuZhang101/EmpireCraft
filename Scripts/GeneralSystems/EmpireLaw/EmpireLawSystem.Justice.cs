using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems.EmpireLaw;

// 执法在人、律随时变(2026-11，用户："律法系统感觉很不完善")：
//   · 查案：查出未发之罪的机会 = 犯人才智定的底数 × 司法官(廷尉、大理寺、御史台……有"司法"职权的中央官职，在任与否、
//     才干操守) × 吏治(本城、本国腐败率) × 身份(宗室、公以上的贵族、名望高的难动)。寻常的国家合起来约为 1，与以前持平；
//   · 宽严：一国奉行的理念(执政理念、本朝官学：法家严、儒家黄老宽……)与君主(暴君、残忍好杀严，明君、好和平宽)
//     定宽严(-1 ~ +1)：严则人畏法少犯、量刑加一等、处决激起民怨并添暴君之名；宽则多犯、量刑减一等、多行恩赦；
//   · 量刑：律文 → 时代与政体(工业时代废笞杖、废连坐；现代政体以监禁为主，民主国家废死) → 宽严 →
//     量刑天平(罪责：前科、叛逆、严苛、宿怨；情面：皇室、议贵、议功、政绩、影响力、宠信、宽仁，见 SentencingScale)加减等 →
//     赎刑(富者纳金赎笞杖徒流)。十恶只看罪责、不许赎、不在大赦之列。
public static partial class EmpireLawSystem
{
    // 刑等(轻 → 重)：罚金、笞、杖、徒(监禁)、流、死、族。没收财产、削爵、罢官是附加的处分，不在刑等之内
    private static readonly PunishmentLevel[] Grades =
    {
        PunishmentLevel.罚金, PunishmentLevel.笞刑, PunishmentLevel.杖刑, PunishmentLevel.监禁,
        PunishmentLevel.流放, PunishmentLevel.死刑, PunishmentLevel.夷三族
    };

    // 宽严分档：|宽严| ≥ 0.3 为严苛/宽仁
    private const float StanceThreshold = 0.3f;
    // 发现几率的上下限(以前 0.05~0.45)
    private const float MinDiscovery = 0.03f;
    private const float MaxDiscovery = 0.6f;
    // 身份难动：几项相乘，最低到这么多
    private const float MinStandingFactor = 0.45f;
    // 勋级在此以上(数字越小越高：0 上柱国 … 3 护军)算功臣
    private const int GreatMeritLevel = 3;
    // 赎刑：家资至少这么多，赎金不过家资的六成
    private const int RansomMinWealth = 100;
    private const float RansomMaxShare = 0.6f;

    public static bool IsGraveCrime(LawType type) => Eras?.GraveCrimes != null && Eras.GraveCrimes.Contains(type);

    #region 论处的主体

    // 论处的主体：帝国之内是帝国(核心国)，不在帝国里是本国
    public static Kingdom RealmKingdom(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return null;
        if (!kingdom.IsInEmpire()) return kingdom;
        Kingdom core = kingdom.GetEmpire()?.CoreKingdom;
        return core?.data != null && !core.isRekt() ? core : kingdom;
    }

    public static Empire RealmEmpire(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt() || !kingdom.IsInEmpire()) return null;
        Empire empire = kingdom.GetEmpire();
        return empire == null || empire.isRekt() || empire.IsArchived() ? null : empire;
    }

    // 本朝君主(帝国之内是天子)
    public static Actor RealmRuler(Kingdom kingdom)
    {
        Empire empire = RealmEmpire(kingdom);
        Actor ruler = empire != null ? empire.Emperor : kingdom?.king;
        return ruler != null && !ruler.isRekt() && ruler.isAlive() ? ruler : null;
    }

    // 宗室：与本朝君主同一宗(含分出去的支系)
    public static bool IsRoyalKin(Actor actor, Kingdom kingdom)
    {
        Actor ruler = RealmRuler(kingdom);
        if (actor == null || ruler == null || ruler == actor) return false;
        // 宗人府废为庶人的不再算宗室(见 ApplyRoyalCourt)
        if (actor.GetPersonalIdentity()?.stripped_royal == true) return false;
        SpecificClan own = actor.GetSpecificClan();
        SpecificClan royal = ruler.GetSpecificClan();
        return own != null && royal != null && SpecificClanManager.SameLineage(own, royal);
    }

    #endregion

    #region 缓存(一国一年算一次)

    private static object _lawCacheWorld;
    private static readonly Dictionary<long, (int year, LawStanceInfo info)> StanceCache = new();
    private static readonly Dictionary<long, (int year, JudicialStrengthInfo info)> JudicialCache = new();

    private static void CheckLawCacheWorld()
    {
        if (ReferenceEquals(_lawCacheWorld, World.world)) return;
        _lawCacheWorld = World.world;
        StanceCache.Clear();
        JudicialCache.Clear();
    }

    #endregion

    #region 宽严

    // 一国的宽严(帝国之内按帝国)。refresh：界面要看即时的(换了君主、改了理念)
    public static LawStanceInfo GetLawStance(Kingdom kingdom, bool refresh = false)
    {
        Kingdom realm = RealmKingdom(kingdom);
        if (realm == null || World.world == null) return new LawStanceInfo();
        CheckLawCacheWorld();
        int year = Date.getCurrentYear();
        if (!refresh && StanceCache.TryGetValue(realm.getID(), out var cached) && cached.year == year) return cached.info;
        LawStanceInfo info;
        try
        {
            info = ComputeLawStance(realm);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[律法] 算 {realm.name} 的宽严失败：{exception.Message}");
            info = new LawStanceInfo();
        }
        if (StanceCache.Count > 512) StanceCache.Clear();
        StanceCache[realm.getID()] = (year, info);
        return info;
    }

    private static LawStanceInfo ComputeLawStance(Kingdom realm)
    {
        var info = new LawStanceInfo();
        EmpireLawEraConfig config = Eras ?? EmpireLawEraConfig.Defaults();
        Empire empire = RealmEmpire(realm);

        // 理念：执政理念(执政党、共和建国理念、君主本人的信念……见 IdeologyFamilies.StateIdeology)与本朝官学
        PartyIdeology state = IdeologyFamilies.StateIdeology(realm);
        string culture = empire != null ? InstitutionSystem.GetPrimaryCulture(empire) : CultureService.GetRealmCulture(realm);
        PartyIdeology? school = string.IsNullOrEmpty(culture) ? null : AncientSchoolsSystem.Orthodox(culture);
        float schoolWeight = school.HasValue ? config.OfficialSchoolWeight : 0f;
        float stateValue = IdeologySeverity(config, state) * (1f - schoolWeight);
        if (Mathf.Abs(stateValue) > 0.001f)
            info.Breakdown.Add(new LawFactor("law_stance_ideology", stateValue, PartySystem.GetIdeologyName(state, culture)));
        if (school.HasValue)
        {
            float schoolValue = IdeologySeverity(config, school.Value) * schoolWeight;
            if (Mathf.Abs(schoolValue) > 0.001f)
                info.Breakdown.Add(new LawFactor("law_stance_school", schoolValue, PartySystem.GetIdeologyName(school.Value, culture)));
        }

        // 君主：暴君、明君，残忍好杀、好和平
        Actor ruler = RealmRuler(realm);
        if (ruler != null)
        {
            RulerQualitySystem.Grade grade = RulerQualitySystem.Of(ruler);
            if (grade == RulerQualitySystem.Grade.Tyrant && config.TyrantRulerSeverity != 0f)
                info.Breakdown.Add(new LawFactor("law_stance_tyrant", config.TyrantRulerSeverity, ruler.getName()));
            else if (grade == RulerQualitySystem.Grade.Wise && config.WiseRulerSeverity != 0f)
                info.Breakdown.Add(new LawFactor("law_stance_wise", config.WiseRulerSeverity, ruler.getName()));
            float traits = 0f;
            foreach (KeyValuePair<string, float> pair in config.TraitSeverity)
                if (ruler.hasTrait(pair.Key)) traits += pair.Value;
            traits = Mathf.Clamp(traits, -0.5f, 0.5f);
            if (Mathf.Abs(traits) > 0.001f) info.Breakdown.Add(new LawFactor("law_stance_ruler_traits", traits, ruler.getName()));
        }

        float severity = Mathf.Clamp(info.Breakdown.Sum(item => item.Value), -1f, 1f);
        info.Severity = severity;
        info.Tier = severity >= StanceThreshold ? LawStanceTier.严苛
            : severity <= -StanceThreshold ? LawStanceTier.宽仁
            : LawStanceTier.适中;
        // 严苛最少七成、宽仁最多一成三的犯罪；罚金 ±3 成、刑期 ±4 成
        info.CrimeRateFactor = 1f - 0.3f * severity;
        info.FineFactor = 1f + 0.3f * severity;
        info.PrisonFactor = 1f + 0.4f * severity;
        // 宽仁之国距上次大赦满十年后，每年至多一成五机会恩赦
        info.AmnestyChance = info.Tier == LawStanceTier.宽仁 ? 0.15f * -severity : 0f;
        return info;
    }

    private static float IdeologySeverity(EmpireLawEraConfig config, PartyIdeology ideology) =>
        config.IdeologySeverity != null && config.IdeologySeverity.TryGetValue(ideology, out float value) ? value : 0f;

    #endregion

    #region 司法

    // 一国司法的力量(帝国之内看中央的司法官)。refresh：界面要看即时的
    public static JudicialStrengthInfo GetJudicialStrength(Kingdom kingdom, bool refresh = false)
    {
        Kingdom realm = RealmKingdom(kingdom);
        if (realm == null || World.world == null) return new JudicialStrengthInfo();
        CheckLawCacheWorld();
        int year = Date.getCurrentYear();
        if (!refresh && JudicialCache.TryGetValue(realm.getID(), out var cached) && cached.year == year) return cached.info;
        JudicialStrengthInfo info;
        try
        {
            info = ComputeJudicialStrength(realm);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[律法] 算 {realm.name} 的司法失败：{exception.Message}");
            info = new JudicialStrengthInfo();
        }
        if (JudicialCache.Count > 512) JudicialCache.Clear();
        JudicialCache[realm.getID()] = (year, info);
        return info;
    }

    // 有"司法"职权的中央官职(廷尉、大理寺、御史台/都察院、共和国的司法部……)：
    //   发现倍数 = 0.55 + 0.6 × 各官平均得分(空缺 0；在任者 0.4 + 0.6 × 才干，清廉 +0.15、贪婪狡诈 -0.25)。
    //   满员、才干平平时约为 1，全部空缺 0.55，得人 1.15；
    // 没有司法官的国家(独立王国、没设这类官的政体)由君主亲断：0.85 + 0.25 × 君主才干，平常约为 1
    private static JudicialStrengthInfo ComputeJudicialStrength(Kingdom realm)
    {
        var info = new JudicialStrengthInfo();
        Empire empire = RealmEmpire(realm);
        if (empire != null)
        {
            foreach (OfficeObject office in CourtSystem.CoreOfficeObjects(empire).Concat(CourtSystem.DivisionOfficeObjects(empire)))
            {
                if (office?.powers == null || !office.powers.Contains(OfficerPowerType.司法)) continue;
                Actor holder = office.GetActor();
                if (holder?.data == null || holder.isRekt() || !holder.isAlive()) holder = null;
                float score = holder != null ? JusticeScore(holder) : 0f;
                info.Offices.Add(new JudicialOfficeInfo { Office = office, Holder = holder, Score = score });
                info.Total++;
                if (holder != null) info.Filled++;
            }
        }
        if (info.Total > 0)
        {
            info.HasJudicialOffices = true;
            float average = info.Offices.Average(item => item.Score);
            info.Factor = 0.55f + 0.6f * average;
            info.Breakdown.Add(new LawFactor("law_judicial_offices", info.Factor, $"{info.Filled}/{info.Total}"));
            return info;
        }
        Actor ruler = RealmRuler(realm);
        info.Factor = 0.85f + 0.25f * (ruler != null ? Ability(ruler) : 0f);
        info.Breakdown.Add(ruler != null
            ? new LawFactor("law_judicial_ruler", info.Factor, ruler.getName())
            : new LawFactor("law_judicial_none", info.Factor));
        return info;
    }

    // 才干：智力与管理之和，30 为满
    private static float Ability(Actor actor) =>
        actor == null ? 0f : Mathf.Clamp01((Mathf.Max(0, actor.intelligence) + Mathf.Max(0, actor.stewardship)) / 30f);

    // 司法官的得分(0~1)
    public static float JusticeScore(Actor holder)
    {
        if (holder == null) return 0f;
        float score = 0.4f + 0.6f * Ability(holder);
        if (HasAnyTrait(holder, "honest")) score += 0.15f;
        if (HasAnyTrait(holder, "greedy", "deceitful")) score -= 0.25f;
        return Mathf.Clamp01(score);
    }

    #endregion

    #region 查案

    // 某人某年未发之罪被查出的机会与构成(kingdom 为论处的国家)
    public static DiscoveryChanceInfo GetDiscoveryChanceInfo(Actor actor, Kingdom kingdom)
    {
        var info = new DiscoveryChanceInfo();
        if (actor == null)
        {
            info.Chance = 0.1f;
            return info;
        }

        // 底数：犯人越聪明越难查(与以前相同，0.45 ~ 0.15)
        int intelligence = Mathf.Clamp(actor.intelligence, 0, 40);
        float baseline = Mathf.Clamp(0.45f - intelligence / 40f * 0.30f, 0.05f, 0.45f);
        info.Breakdown.Add(new LawFactor("law_discovery_base", baseline));
        float chance = baseline;

        if (kingdom != null && !kingdom.isRekt())
        {
            float judicial = GetJudicialStrength(kingdom).Factor;
            chance *= judicial;
            info.Breakdown.Add(new LawFactor("law_discovery_judicial", judicial));

            float corruption = CorruptionFactor(actor, kingdom);
            chance *= corruption;
            info.Breakdown.Add(new LawFactor("law_discovery_corruption", corruption));

            chance *= StandingFactor(actor, kingdom, info.Breakdown);
        }

        info.Chance = Mathf.Clamp(chance, MinDiscovery, MaxDiscovery);
        return info;
    }

    private static float GetCrimeDiscoveryChance(Actor actor, Kingdom kingdom) => GetDiscoveryChanceInfo(actor, kingdom).Chance;

    // 吏治：本国与本城腐败率各半，腐败率 0 → 1.1、0.2 → 1.0、1 → 0.6
    private static float CorruptionFactor(Actor actor, Kingdom kingdom)
    {
        double rate = CorruptionRules.Bound(kingdom.GetCorruptionRate());
        City city = actor.city;
        if (city != null && !city.isRekt() && city.kingdom == kingdom)
            rate = rate * 0.5d + CorruptionRules.Bound(city.GetCorruptionRate()) * 0.5d;
        return Mathf.Clamp(1.1f - 0.5f * (float)rate, 0.6f, 1.1f);
    }

    // 身份：宗室 ×0.7；公以上的贵族 ×0.8、侯伯与封号 ×0.9；名望 300 以上 ×0.8、120 以上 ×0.9。合起来最低 0.45
    private static float StandingFactor(Actor actor, Kingdom kingdom, List<LawFactor> breakdown)
    {
        float factor = 1f;
        if (IsRoyalKin(actor, kingdom))
        {
            factor *= 0.7f;
            breakdown?.Add(new LawFactor("law_discovery_royal_kin", 0.7f));
        }

        int peerage = (int)actor.GetPeeragesLevel();
        float noble = peerage >= (int)PeeragesLevel.peerages_3 ? 0.8f
            : peerage > (int)PeeragesLevel.peerages_6 || actor.HasTitle() ? 0.9f
            : 1f;
        if (noble < 1f)
        {
            factor *= noble;
            breakdown?.Add(new LawFactor("law_discovery_noble", noble));
        }

        int renown = actor.renown;
        float fame = renown >= 300 ? 0.8f : renown >= 120 ? 0.9f : 1f;
        if (fame < 1f)
        {
            factor *= fame;
            breakdown?.Add(new LawFactor("law_discovery_renown", fame));
        }

        return Mathf.Max(MinStandingFactor, factor);
    }

    #endregion

    #region 时代与政体

    private struct PenalRegime
    {
        public int EraTier;
        public bool Modern;
        public bool AbolishDeath;
    }

    // 本国(帝国之内按核心国)文化所处的科技时代档次
    public static int GetEraTier(Kingdom kingdom)
    {
        Kingdom realm = RealmKingdom(kingdom);
        string culture = realm != null ? TechnologySystem.GetCultureOf(realm) : "";
        return string.IsNullOrEmpty(culture) ? 0 : TechnologySystem.GetEraTier(culture);
    }

    // 现代政体：现代、穿越政体(ModernStability.IsModern)，以及改制共和的帝国
    public static bool IsModernLaw(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt()) return false;
        return ModernStability.IsModern(kingdom) || RepublicSystem.IsRepublic(RealmEmpire(kingdom));
    }

    // 现代政体里保不保留死刑：一党专政、独裁、或由 ModernDeathPenaltyIdeologies 的理念执政的保留，其余(民主共和国)废除
    public static bool KeepsDeathPenalty(Kingdom kingdom)
    {
        if (!IsModernLaw(kingdom)) return true;
        EmpireLawEraConfig config = Eras ?? EmpireLawEraConfig.Defaults();
        Kingdom realm = RealmKingdom(kingdom);
        Empire empire = RealmEmpire(kingdom);
        if (empire != null)
        {
            if (config.ModernDeathPenaltyOneParty && RepublicSystem.IsOneParty(empire)) return true;
            if (PartyBanSystem.GetAutocracy(empire) >= config.ModernDeathPenaltyAutocracy) return true;
        }
        return config.ModernDeathPenaltyIdeologies != null &&
               config.ModernDeathPenaltyIdeologies.Contains(IdeologyFamilies.StateIdeology(realm));
    }

    private static PenalRegime GetPenalRegime(Kingdom kingdom)
    {
        bool modern = IsModernLaw(kingdom);
        return new PenalRegime
        {
            EraTier = GetEraTier(kingdom),
            Modern = modern,
            AbolishDeath = modern && !KeepsDeathPenalty(kingdom)
        };
    }

    private static void AddNote(ICollection<string> notes, string key)
    {
        if (notes != null && !string.IsNullOrEmpty(key) && !notes.Contains(key)) notes.Add(key);
    }

    // 给界面逐项写"原刑→今刑"用：一种刑罚在这个国家按时代、政体改成什么(notes 收集用到的说明键)
    public static PunishmentLevel AdaptPunishment(Kingdom kingdom, PunishmentLevel punishment, ICollection<string> notes = null) =>
        Adapt(punishment, GetPenalRegime(kingdom), notes);

    // 一种刑罚按时代、政体改成什么(notes 收集用到的规则)
    private static PunishmentLevel Adapt(PunishmentLevel punishment, PenalRegime regime, ICollection<string> notes)
    {
        EmpireLawEraConfig config = Eras ?? EmpireLawEraConfig.Defaults();
        PunishmentLevel current = punishment;
        foreach (LawEraRule rule in config.EraRules ?? new List<LawEraRule>())
        {
            if (rule?.Replace == null || regime.EraTier < rule.MinEraTier) continue;
            if (rule.Replace.TryGetValue(current, out PunishmentLevel replaced) && replaced != current)
            {
                current = replaced;
                AddNote(notes, rule.NoteKey);
            }
        }
        if (regime.Modern && config.ModernReplace != null &&
            config.ModernReplace.TryGetValue(current, out PunishmentLevel modern) && modern != current)
        {
            current = modern;
            AddNote(notes, config.ModernNoteKey);
        }
        if (regime.AbolishDeath && (current == PunishmentLevel.死刑 || current == PunishmentLevel.夷三族))
        {
            current = config.ModernDeathReplacement;
            AddNote(notes, config.ModernDeathNoteKey);
        }
        return current;
    }

    // 按时代、政体改过的刑罚(去重，致死的在最后)；lifeTerm：死刑改成了监禁(长期监禁)
    private static List<PunishmentLevel> AdaptPunishments(List<PunishmentLevel> punishments, PenalRegime regime,
        ICollection<string> notes, out bool lifeTerm)
    {
        lifeTerm = false;
        var result = new List<PunishmentLevel>();
        foreach (PunishmentLevel punishment in punishments ?? new List<PunishmentLevel>())
        {
            PunishmentLevel adapted = Adapt(punishment, regime, notes);
            if (IsLethal(punishment) && !IsLethal(adapted)) lifeTerm = true;
            if (!result.Contains(adapted)) result.Add(adapted);
        }
        return OrderedPunishments(result);
    }

    private static bool IsLethal(PunishmentLevel punishment) =>
        punishment == PunishmentLevel.死刑 || punishment == PunishmentLevel.夷三族;

    // 本国现行的刑等(轻 → 重)：按时代、政体改过、去重
    private static List<PunishmentLevel> AdaptedGrades(PenalRegime regime)
    {
        var chain = new List<PunishmentLevel>();
        foreach (PunishmentLevel grade in Grades)
        {
            PunishmentLevel adapted = Adapt(grade, regime, null);
            if (Array.IndexOf(Grades, adapted) >= 0 && !chain.Contains(adapted)) chain.Add(adapted);
        }
        return chain;
    }

    // 一条律法在本国现行的刑罚(界面用)
    public static List<PunishmentLevel> GetEffectivePunishments(Kingdom kingdom, LawType type, List<string> notes = null)
    {
        Law law = type.GetConfig();
        if (law == null) return new List<PunishmentLevel>();
        return AdaptPunishments(OrderedPunishments(law.Punishments), GetPenalRegime(kingdom), notes, out _);
    }

    // 本国现行的律法(政体配置里有、律法表里有的)，含按时代、政体改过的刑罚(界面用)
    public static List<EffectiveLawInfo> GetEffectiveLaws(Kingdom kingdom)
    {
        var result = new List<EffectiveLawInfo>();
        Regime regime = kingdom?.GetRegime();
        if (regime?.laws == null) return result;
        PenalRegime penal = GetPenalRegime(kingdom);
        foreach (LawType type in regime.laws.Distinct())
        {
            Law law = type.GetConfig();
            if (law == null) continue;
            var info = new EffectiveLawInfo
            {
                Type = type,
                Law = law,
                Category = law.Category,
                Name = GetLawName(type),
                Description = GetLawDescription(type),
                Grave = IsGraveCrime(type),
                BasePunishments = OrderedPunishments(law.Punishments)
            };
            info.EffectivePunishments = AdaptPunishments(info.BasePunishments, penal, info.AdaptationNotes, out _);
            result.Add(info);
        }
        return result;
    }

    #endregion

    #region 量刑

    // 量刑：律文 → 时代与政体 → 量刑天平(宽严、八议都在天平上) → 赎刑。十恶不论情面、不赎；现代政体只论宽仁、不赎
    private static void BuildSentence(LawEnforcementContext context)
    {
        Kingdom kingdom = context.Kingdom;
        PenalRegime regime = GetPenalRegime(kingdom);
        List<PunishmentLevel> grades = AdaptedGrades(regime);
        context.BasePunishments = OrderedPunishments(context.Law?.Punishments);
        List<PunishmentLevel> sentence = AdaptPunishments(context.BasePunishments, regime, context.Notes, out bool lifeTerm);
        context.LifeTerm = lifeTerm;

        LawStanceInfo stance = GetLawStance(kingdom);
        context.Severity = stance.Severity;
        // 宽严、八议都化成量刑天平上的砝码(以前严苛、宽仁按机会加减一等，八议一律减一等)
        ApplyScale(context, sentence, grades, regime, stance);

        PlanRansom(context, sentence, regime);
        context.Sentence = OrderedPunishments(sentence);
        context.PrisonYears = PrisonTerm(context, stance);
    }

    // 刑罚里最重的一等在现行刑等里的位置；没有刑等(只有附加处分)为 -1
    private static int HeaviestGrade(List<PunishmentLevel> sentence, List<PunishmentLevel> grades)
    {
        int heaviest = -1;
        foreach (PunishmentLevel punishment in sentence)
            heaviest = Math.Max(heaviest, grades.IndexOf(punishment));
        return heaviest;
    }

    // 加一等：最重的一等换成高一等的，加不至死；已到流(现代到徒)的，监禁的刑期加五成。只有附加处分的加罚金
    private static bool Escalate(List<PunishmentLevel> sentence, List<PunishmentLevel> grades, LawEnforcementContext context)
    {
        int heaviest = HeaviestGrade(sentence, grades);
        if (heaviest < 0)
        {
            if (grades.Count == 0) return false;
            sentence.Add(grades[0]);
            return true;
        }
        PunishmentLevel current = grades[heaviest];
        if (IsLethal(current)) return false;
        if (heaviest + 1 < grades.Count && !IsLethal(grades[heaviest + 1]))
        {
            sentence.Remove(current);
            if (!sentence.Contains(grades[heaviest + 1])) sentence.Add(grades[heaviest + 1]);
            return true;
        }
        if (current == PunishmentLevel.监禁 || sentence.Contains(PunishmentLevel.监禁))
        {
            context.PrisonBoost = true;
            return true;
        }
        return false;
    }

    // 减一等：最重的一等换成低一等的(死罪减为流、夷三族减为死……)，最轻的罚金免去；
    // 长期监禁(现代废死改判的)减为寻常刑期
    private static bool Reduce(List<PunishmentLevel> sentence, List<PunishmentLevel> grades, LawEnforcementContext context)
    {
        int heaviest = HeaviestGrade(sentence, grades);
        if (heaviest < 0) return false;
        PunishmentLevel current = grades[heaviest];
        if (current == PunishmentLevel.监禁 && context.LifeTerm)
        {
            context.LifeTerm = false;
            return true;
        }
        sentence.Remove(current);
        if (heaviest > 0 && !sentence.Contains(grades[heaviest - 1])) sentence.Add(grades[heaviest - 1]);
        return true;
    }

    #region 量刑天平

    private enum RoyalRank { None, Kin, Close, Heir }

    // 皇室：本朝储君；君主的子女、兄弟、父母(按族谱父母，死了的也认)；同宗(含支系)。废为庶人的不算(见 ApplyRoyalCourt)
    private static RoyalRank RoyalRankOf(Actor actor, Kingdom kingdom)
    {
        if (actor?.data == null || actor.isRekt()) return RoyalRank.None;
        PersonalClanIdentity own = actor.GetPersonalIdentity();
        if (own?.stripped_royal == true) return RoyalRank.None;
        Kingdom realm = RealmKingdom(kingdom);
        if (realm != null && realm.HasHeir() && realm.GetHeir() == actor) return RoyalRank.Heir;
        if (!IsRoyalKin(actor, kingdom)) return RoyalRank.None;
        PersonalClanIdentity ruler = RealmRuler(kingdom)?.GetPersonalIdentity();
        if (own != null && ruler != null &&
            (own.father == ruler.id || own.mother == ruler.id || ruler.father == own.id || ruler.mother == own.id ||
             own.father >= 0 && own.father == ruler.father || own.mother >= 0 && own.mother == ruler.mother))
            return RoyalRank.Close;
        return RoyalRank.Kin;
    }

    // 皇室身份：0 不是、1 宗室、2 近支、3 储君(名望年入也按它，见 RenownSystem)
    public static int RoyalStanding(Actor actor, Kingdom kingdom) => (int)RoyalRankOf(actor, kingdom);

    // 叛逆：起兵叛乱、拥兵自立的次数(见 NoteDefiance)与犯过的十恶。这一案就是那场兵败的叛乱的，那一回不重算
    private static int DefianceCount(PersonalClanIdentity identity, string reasonKey)
    {
        if (identity == null) return 0;
        int tally = identity.law_rebellions + identity.law_secessions;
        if (tally > 0 && reasonKey is "law_reason_failed_rebellion" or "law_reason_failed_secession"
                or "law_reason_failed_claim" or "law_reason_failed_sect_uprising") tally--;
        int grave = identity.law_convictions?.Count(record => record?.Crime is LawType type && IsGraveCrime(type)) ?? 0;
        return tally + grave;
    }

    // 未发之罪累计的赃额(人物页预览用；论罪时用那一桩的，见 CrimeRecord.amount)
    private static int PendingLoot(PersonalClanIdentity identity) =>
        identity?.crime_records?.Where(record => record != null && !record.resolved).Sum(record => record.amount) ?? 0;

    // 量刑天平的读数(见 SentencingScale)。context 为空是人物页的预览：按寻常罪名(不是十恶)、未发之罪的赃额、没有告发者算
    //   罪责：前科、叛逆、赃额、告发者、严苛之政、与君主有宿怨、暴君从重；
    //   情面：皇室、议贵、议功、政绩、影响力、宠信(昏君偏袒)、宽仁之政。十恶、暴君情面不上秤；现代政体只论宽仁
    public static SentencingScaleInfo GetScale(Actor actor, Kingdom kingdom, LawEnforcementContext context = null)
    {
        var info = new SentencingScaleInfo();
        if (actor?.data == null || actor.isRekt() || kingdom?.data == null || kingdom.isRekt()) return info;
        PenalRegime regime = GetPenalRegime(kingdom);
        LawStanceInfo stance = GetLawStance(kingdom);
        PersonalClanIdentity identity = context?.Identity ?? actor.GetPersonalIdentity();
        Actor ruler = RealmRuler(kingdom);
        RulerQualitySystem.Grade temper = ruler != null ? RulerQualitySystem.Of(ruler) : RulerQualitySystem.Grade.Average;
        info.Grave = context?.Grave == true;
        info.Tyrant = temper == RulerQualitySystem.Grade.Tyrant && ruler != actor;
        void Weigh(List<LawFactor> side, string key, float value, string arg = null)
        {
            if (Mathf.Abs(value - 1f) > 0.001f) side.Add(new LawFactor(key, value, arg));
        }

        Weigh(info.Guilt, "law_scale_priors", SentencingScale.PriorFactor(identity?.law_convictions?.Count ?? 0));
        Weigh(info.Guilt, "law_scale_defiance", SentencingScale.DefianceFactor(DefianceCount(identity, context?.ReasonKey)));
        int royalKills = RoyalBloodDebt(actor, kingdom);
        Weigh(info.Guilt, "law_scale_royal_blood", SentencingScale.RoyalBloodFactor(royalKills), royalKills.ToString());
        int loot = context != null ? context.Loot : PendingLoot(identity);
        Weigh(info.Guilt, "law_scale_loot", SentencingScale.LootFactor(loot), loot.ToString());
        Actor accuser = context?.Accuser;
        if (accuser?.data != null && !accuser.isRekt())
            Weigh(info.Guilt, "law_scale_accuser", SentencingScale.AccuserFactor(accuser.renown, actor.renown), accuser.getName());
        Weigh(info.Guilt, "law_scale_harsh", SentencingScale.HarshFactor(stance.Severity));
        if (ruler != null && ruler != actor && PersonalTieSystem.AreRivals(ruler, actor))
            Weigh(info.Guilt, "law_scale_rival", SentencingScale.RulerRival, ruler.getName());
        if (info.Tyrant) Weigh(info.Guilt, "law_scale_tyrant", SentencingScale.TyrantGuilt, ruler.getName());
        // 神力加的砝码与保释(见 EmpireLawSystem.Divine)：各朝各政体都算
        AddDivineWeights(identity, info.Guilt, info.Mercy);

        RoyalRank royal = RoyalRankOf(actor, kingdom);
        info.RoyalKey = royal switch
        {
            RoyalRank.Heir => "law_scale_heir",
            RoyalRank.Close => "law_scale_royal_close",
            RoyalRank.Kin => "law_scale_royal_kin",
            _ => ""
        };
        if (!regime.Modern)
        {
            if (info.Royal)
                Weigh(info.Mercy, info.RoyalKey, royal switch
                {
                    RoyalRank.Heir => SentencingScale.RoyalHeir,
                    RoyalRank.Close => SentencingScale.RoyalClose,
                    _ => SentencingScale.RoyalKin
                });
            int peerage = (int)actor.GetPeeragesLevel();
            Weigh(info.Mercy, "law_scale_noble", peerage >= (int)PeeragesLevel.peerages_3 ? SentencingScale.PeerHigh
                : peerage > (int)PeeragesLevel.peerages_6 || actor.HasTitle() ? SentencingScale.PeerLow : 1f);
            OfficeIdentity office = actor.GetOrCreate().officeIdentity;
            if (office != null && office.meritLevel <= GreatMeritLevel) Weigh(info.Mercy, "law_scale_merit", SentencingScale.GreatMerit);
            if (office != null) Weigh(info.Mercy, "law_scale_performance", SentencingScale.PerformanceFactor(office.TotalPerformance));
            Weigh(info.Mercy, "law_scale_influence", SentencingScale.InfluenceFactor(actor.renown, ruler?.renown ?? 0, ruler != null));
            Empire empire = RealmEmpire(kingdom);
            if (empire != null && FavouriteSystem.Of(empire) == actor)
            {
                bool indulgent = temper is RulerQualitySystem.Grade.Fatuous or RulerQualitySystem.Grade.Dissolute;
                Weigh(info.Mercy, indulgent ? "law_scale_favourite_indulged" : "law_scale_favourite",
                    indulgent ? SentencingScale.FatuousFavourite : SentencingScale.Favourite, ruler?.getName());
            }
        }
        if (!info.Grave) Weigh(info.Mercy, "law_scale_lenient", SentencingScale.LenientFactor(stance.Severity));

        info.GuiltWeight = info.Guilt.Aggregate(1f, (product, factor) => product * factor.Value);
        info.MercyWeight = info.Mercy.Aggregate(1f, (product, factor) => product * factor.Value);
        info.Balance = SentencingScale.Balance(info.GuiltWeight, info.MercyWeight, info.MercyIgnored);
        info.Shift = SentencingScale.Shift(info.Balance);
        return info;
    }

    // 加减几等的说法：加一等、减三等、按律
    public static string ShiftText(int shift) =>
        shift > 0 ? LM.Get("law_scale_heavier") : shift < 0 ? string.Format(LM.Get("law_scale_lighter"), -shift) : LM.Get("law_scale_even");

    // 一边砝码的说法："前科×1.40、赃额 120×1.30"
    public static string WeightsText(IEnumerable<LawFactor> factors)
    {
        string sep = LM.Has("law_scale_sep") ? LM.Get("law_scale_sep") : ", ";
        return string.Join(sep, (factors ?? Enumerable.Empty<LawFactor>()).Select(factor => $"{factor.Label}×{factor.Value:0.00}"));
    }

    // 量刑：天平定加减几等。减两等以上免抄家(改罚金)、减三等免削爵；皇室交宗人府议处(见 RoyalCourtSentence)，
    // 罢官不禁锢子孙(子孙是皇位所系)
    private static void ApplyScale(LawEnforcementContext context, List<PunishmentLevel> sentence, List<PunishmentLevel> grades,
        PenalRegime regime, LawStanceInfo stance)
    {
        SentencingScaleInfo info = GetScale(context.Actor, context.Kingdom, context);
        context.Scale = info;
        context.RoyalKin = info.Royal;
        context.Faction = context.Actor?.GetFaction();
        int shift = info.Shift;
        int applied = 0;
        if (shift > 0 && Escalate(sentence, grades, context)) applied = 1;
        for (int step = 0; step < -shift && Reduce(sentence, grades, context); step++) applied--;
        context.Escalated = applied > 0;
        context.Reduced = applied < 0;
        context.ScaleShift = shift;

        if (-shift >= SentencingScale.SpareEstateAt && sentence.Remove(PunishmentLevel.没收财产))
        {
            if (grades.Contains(PunishmentLevel.罚金) && !sentence.Contains(PunishmentLevel.罚金)) sentence.Add(PunishmentLevel.罚金);
            AddNote(context.Notes, "law_note_spare_estate");
        }
        if (-shift >= SentencingScale.SparePeerageAt)
        {
            context.SparePeerage = true;
            if (sentence.Remove(PunishmentLevel.剥夺爵位)) AddNote(context.Notes, "law_note_spare_peerage");
        }
        if (info.Royal && !regime.Modern) RoyalCourtSentence(context, sentence, grades, info);
        if (info.Royal && context.Law?.AffectDescendants == true && sentence.Contains(PunishmentLevel.剥夺官职))
        {
            context.NoDescendantBan = true;
            AddNote(context.Notes, "law_note_royal_no_ban");
        }

        if (info.Guilt.Count == 0 && info.Mercy.Count == 0) return;
        context.Notes.Add("law_note_scale|" + (info.MercyIgnored && info.Mercy.Count > 0
            ? LM.Get(info.Grave ? "law_scale_grave" : "law_scale_tyrant_ignores") + " · " + ShiftText(shift)
            : ShiftText(shift)));
        LogService.LogInfo($"[律法][量刑] {context.ActorName} {context.LawType}：罪责 " +
                           (info.Guilt.Count == 0 ? "—" : string.Join(" ", info.Guilt.Select(f => $"{f.Label}×{f.Value:0.00}"))) +
                           $" → {info.GuiltWeight:0.00}｜情面 " +
                           (info.Mercy.Count == 0 ? "—" : string.Join(" ", info.Mercy.Select(f => $"{f.Label}×{f.Value:0.00}"))) +
                           $" → {info.MercyWeight:0.00}{(info.Grave ? "(十恶不议)" : info.Tyrant ? "(暴君不论情面)" : "")}｜" +
                           $"天平 {info.Balance:0.00} → {shift:+0;-0;0} 等，实施 {applied:+0;-0;0}" +
                           (info.Royal ? "｜宗人府 " + string.Join(" ", context.Notes.Where(n => n.StartsWith("law_note_royal") || n.StartsWith("law_note_heir"))) : ""));
    }

    // 宗人府议处(用户 2026-11-10 选"宗室重罪交宗人府")：
    //   · 寻常罪名：皇室不受笞杖(折罚俸)、徒流改圈禁、免抄家、削爵改降一等；储君判到圈禁的废储；
    //   · 十恶：储君废为庶人、终身圈禁(唐太子承乾谋反故事；暴君照律处死)，宗室照律论死，夷三族改赐死(三族即皇族)
    private static void RoyalCourtSentence(LawEnforcementContext context, List<PunishmentLevel> sentence, List<PunishmentLevel> grades,
        SentencingScaleInfo info)
    {
        if (!context.Grave)
        {
            bool changed = false;
            if (sentence.RemoveAll(p => p == PunishmentLevel.笞刑 || p == PunishmentLevel.杖刑) > 0)
            {
                if (grades.Contains(PunishmentLevel.罚金) && !sentence.Contains(PunishmentLevel.罚金)) sentence.Add(PunishmentLevel.罚金);
                changed = true;
            }
            if (sentence.Remove(PunishmentLevel.流放))
            {
                if (!sentence.Contains(PunishmentLevel.监禁)) sentence.Add(PunishmentLevel.监禁);
                changed = true;
            }
            if (sentence.Remove(PunishmentLevel.没收财产))
            {
                if (grades.Contains(PunishmentLevel.罚金) && !sentence.Contains(PunishmentLevel.罚金)) sentence.Add(PunishmentLevel.罚金);
                changed = true;
            }
            context.DemotePeerage = true;
            if (sentence.Contains(PunishmentLevel.剥夺爵位)) changed = true;
            if (changed) AddNote(context.Notes, "law_note_royal_court");
            if (info.Heir && sentence.Contains(PunishmentLevel.监禁))
            {
                context.DeposeHeir = true;
                AddNote(context.Notes, "law_note_heir_deposed");
            }
            return;
        }
        if (info.Heir && !info.Tyrant)
        {
            sentence.Remove(PunishmentLevel.死刑);
            sentence.Remove(PunishmentLevel.夷三族);
            if (!sentence.Contains(PunishmentLevel.监禁)) sentence.Add(PunishmentLevel.监禁);
            context.LifeTerm = true;
            context.DeposeHeir = true;
            context.StripRoyal = true;
            AddNote(context.Notes, "law_note_heir_commoner");
            return;
        }
        if (sentence.Remove(PunishmentLevel.夷三族))
        {
            if (!sentence.Contains(PunishmentLevel.死刑)) sentence.Add(PunishmentLevel.死刑);
            AddNote(context.Notes, "law_note_royal_no_clan");
        }
        if (info.Heir) context.DeposeHeir = true;
    }

    // 宗人府议处的后事(施刑以后)：废储——储君之位另立，此人不再入继承人之列；废为庶人——不再算宗室，去爵
    private static void ApplyRoyalCourt(LawEnforcementContext context)
    {
        Actor actor = context?.Actor;
        if (actor?.data == null || !context.DeposeHeir && !context.StripRoyal) return;
        PersonalClanIdentity identity = context.Identity ?? actor.GetPersonalIdentity();
        Kingdom realm = RealmKingdom(context.Kingdom);
        Actor ruler = RealmRuler(context.Kingdom);
        if (context.DeposeHeir && realm != null)
        {
            if (identity != null) identity.disinherited = true;
            var data = realm.GetOrCreate();
            if (data.designated_heir_id == actor.id) data.designated_heir_id = -1L;
            if (realm.GetHeir() == actor)
            {
                realm.RemoveHeir();
                realm.StartToChooseHeir();
            }
            string text = string.Format(LM.Get(context.StripRoyal ? "law_heir_commoner_history" : "law_heir_deposed_history"),
                context.ActorName, context.LawName);
            Empire empire = RealmEmpire(context.Kingdom);
            if (empire != null) EventRecorder.Record(empire, text, actor, realm);
            else EventRecorder.Record(realm, text, actor);
            BiographySystem.Note(actor, BiographySystem.Format("bio_heir_deposed_law", context.LawName), ruler);
        }
        if (context.StripRoyal && identity != null)
        {
            identity.stripped_royal = true;
            ApplyStripPeerage(actor);
        }
    }

    // 量刑天平的代价(用户 2026-11-10 选"硬判、放过都有代价")：
    //   · 硬判：单看情面本该减两等以上，却因罪责、暴君、十恶少减了两等以上，又判了徒以上——他那一派怨望 +HardlineGrievance；
    //     皇室再损正统 2；暴君不念亲贵添暴虐 3；
    //   · 放过：罪责到了 ×1.6 却因情面减了等——吏治败坏积弊 +2，本朝腐败 +0.5%
    private const float HardlineGrievance = 5f;

    private static void ApplyScaleCosts(LawEnforcementContext context)
    {
        SentencingScaleInfo info = context?.Scale;
        if (info == null) return;
        Empire empire = RealmEmpire(context.Kingdom);
        Kingdom realm = RealmKingdom(context.Kingdom);
        int mercyShift = SentencingScale.MercyShift(info.MercyWeight);
        bool severe = context.AppliedPunishments.Any(p => p is PunishmentLevel.监禁 or PunishmentLevel.流放 or PunishmentLevel.死刑
            or PunishmentLevel.夷三族);
        if (-mercyShift >= SentencingScale.HardlineMercyGrades && info.Shift - mercyShift >= SentencingScale.HardlineMercyGrades && severe)
        {
            if (empire != null && context.Faction != null)
            {
                CourtFactionState state = CourtSystem.FactionState(empire, context.Faction);
                if (state != null) state.grievance = Mathf.Min(100f, state.grievance + HardlineGrievance);
            }
            if (info.Royal) empire?.AddMandate(-2);
            if (info.Tyrant) RealmRuler(context.Kingdom)?.AddTyrantValue(3);
            AddNote(context.Notes, "law_note_cost_hardline");
            LogService.LogInfo($"[律法][量刑] 硬判 {context.ActorName}：情面本可减 {-mercyShift} 等，实判 {info.Shift:+0;-0;0}，" +
                               $"派系怨望 +{HardlineGrievance}{(info.Royal ? "，正统 -2" : "")}");
            return;
        }
        if (info.GuiltWeight >= SentencingScale.LenientGuiltWeight && info.Shift < 0)
        {
            if (empire != null) MaladySystem.Aggravate(empire, MaladySystem.Graft, 2f);
            realm?.AddCorruptionRate(0.005d);
            AddNote(context.Notes, "law_note_cost_lenient");
            LogService.LogInfo($"[律法][量刑] 放过 {context.ActorName}：罪责 ×{info.GuiltWeight:0.00} 仍减 {-info.Shift} 等，吏治败坏 +2");
        }
    }

    // 血债宗室：这人亲手杀过几个本朝皇亲(与本朝君主同一宗，含支系)。按他生平里"手刃某某"那几笔回查——死者的死因记录写着
    // 凶手就是他，旧档里早先杀的也算得上(用户 2026-11-10 "都杀了皇室的亲人了这罪名呢")
    public static int RoyalBloodDebt(Actor actor, Kingdom kingdom)
    {
        PersonalClanIdentity identity = actor?.GetPersonalIdentity();
        SpecificClan royal = RealmRuler(kingdom)?.GetSpecificClan();
        if (identity?.personal_history == null || royal == null) return 0;
        var victims = new HashSet<long>();
        foreach (PersonalHistoryRecord record in identity.personal_history)
        {
            if (record == null || record.related_actor_id < 0 || victims.Contains(record.related_actor_id)) continue;
            PersonalClanIdentity victim = SpecificClanManager.PersonByActorId(record.related_actor_id);
            if (victim == null || victim.is_alive || victim.id == identity.id ||
                !SpecificClanManager.SameLineage(victim._specificClan, royal)) continue;
            bool killedByHim = victim.personal_history?.Any(death => death != null && death.event_key == BiographySystem.DeathKey &&
                                                                     death.related_actor_id == actor.id) == true;
            if (killedByHim) victims.Add(record.related_actor_id);
        }
        return victims.Count;
    }

    // 要人死于人手(BiographySystem.RecordKiller)：同一朝的人、两边又不在交战，下手的人记一桩未发之罪——杀了本朝君主记谋反，
    // 别的记杀人(十恶)，日后年审查出来按律论处；打仗阵前杀的不算(用户 2026-11-10 "都杀了皇室的亲人了这罪名呢")
    public static void OnNotableKilled(Actor victim, Actor killer)
    {
        if (!Enabled || victim?.data == null || killer?.data == null || killer.isRekt() || !killer.isAlive() || killer == victim) return;
        Kingdom realm = killer.kingdom, home = victim.kingdom;
        if (realm?.data == null || home?.data == null || realm.isRekt() || home.isRekt()) return;
        bool sameRealm = realm == home || realm.IsInEmpire() && home.IsInEmpire() && realm.GetEmpire() == home.GetEmpire();
        if (!sameRealm || realm.isInWarWith(home)) return;
        LawType crime = RealmRuler(home) == victim ? LawType.谋反 : LawType.杀人;
        try
        {
            if (killer.RecordCrime(crime))
                LogService.LogInfo($"[律法][实事] {killer.getName()} 杀 {victim.getName()}(同朝、未交战)，记{crime}");
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[律法][实事] 给 {killer.getName()} 记{crime}失败: {exception.Message}");
        }
    }

    // 不打仗的拥兵自立(据地自专、自立为汗、各自为汗、对立国王相持离去、投靠外国、改投入侵者、飞地自立……)：
    // 记在离去那一国的君主身上。各处在确认已离开帝国后调用(用户 2026-11-10 "接着做"自立的判定)
    public static void NoteSecession(Kingdom kingdom)
    {
        Actor ruler = kingdom?.data != null && !kingdom.isRekt() ? kingdom.king : null;
        if (ruler?.data != null && !ruler.isRekt()) NoteDefiance(ruler, true);
    }

    // 起兵叛乱、拥兵自立记在首领的族谱身份上(叛逆砝码)：每场仗每位首领记一次(见 LawEvents.TrackRebelLeader)
    public static void NoteDefiance(Actor leader, bool secession)
    {
        PersonalClanIdentity identity = leader?.GetPersonalIdentity();
        if (identity == null) return;
        if (secession) identity.law_secessions++;
        else identity.law_rebellions++;
    }

    #endregion

    // 赎刑：判了笞、杖、徒、流而不判死的，家资不少于 100、赎金不过家资六成的纳金赎免(严则赎重)。
    // 十恶、现代政体不许赎；判了抄家的赎了也是白赎，不赎
    private static void PlanRansom(LawEnforcementContext context, List<PunishmentLevel> sentence, PenalRegime regime)
    {
        EmpireLawEraConfig config = Eras ?? EmpireLawEraConfig.Defaults();
        if (context.Grave || regime.Modern || config.RansomCost == null) return;
        if (sentence.Any(IsLethal) || sentence.Contains(PunishmentLevel.没收财产)) return;
        List<PunishmentLevel> commutable = sentence.Where(punishment => config.RansomCost.ContainsKey(punishment)).ToList();
        if (commutable.Count == 0) return;
        float factor = GetLawStance(context.Kingdom).FineFactor;
        int cost = Mathf.RoundToInt(commutable.Sum(punishment => config.RansomCost[punishment]) * factor);
        int money = context.Actor?.money ?? 0;
        if (cost <= 0 || money < RansomMinWealth || cost > money * RansomMaxShare) return;
        foreach (PunishmentLevel punishment in commutable) sentence.Remove(punishment);
        context.RansomPaid = cost;
        context.Notes.Add("law_note_ransom|" + cost);
    }

    // 收赎金入国库(施刑前收，不算追回的赃款)
    private static void PayRansom(LawEnforcementContext context)
    {
        if (context == null || context.RansomPaid <= 0) return;
        Actor actor = context.Actor;
        int paid = Math.Min(context.RansomPaid, Math.Max(0, actor?.money ?? 0));
        context.RansomPaid = paid;
        if (paid <= 0) return;
        actor.addMoney(-paid);
        if (context.Kingdom != null && !context.Kingdom.isRekt()) context.Kingdom.AddMoney(paid, TreasuryCategory.Other);
    }

    // 监禁的刑期(年)：按律文最重的原定刑罚(PrisonYears)，宽严增减，从重的加五成；现代废死改判的为长期监禁
    private static int PrisonTerm(LawEnforcementContext context, LawStanceInfo stance)
    {
        if (context.Sentence == null || !context.Sentence.Contains(PunishmentLevel.监禁)) return 0;
        EmpireLawEraConfig config = Eras ?? EmpireLawEraConfig.Defaults();
        if (context.LifeTerm) return config.LifeImprisonmentYears;
        float years = BasePrisonYears(context.BasePunishments) * (stance?.PrisonFactor ?? 1f);
        if (context.PrisonBoost) years *= 1.5f;
        return Mathf.Clamp(Mathf.RoundToInt(years), 1, config.LifeImprisonmentYears);
    }

    private static int BasePrisonYears(List<PunishmentLevel> punishments)
    {
        EmpireLawEraConfig config = Eras ?? EmpireLawEraConfig.Defaults();
        int years = 0;
        foreach (PunishmentLevel punishment in punishments ?? new List<PunishmentLevel>())
            if (config.PrisonYears != null && config.PrisonYears.TryGetValue(punishment, out int value))
                years = Math.Max(years, value);
        return years > 0 ? years : 4;
    }

    #endregion
}

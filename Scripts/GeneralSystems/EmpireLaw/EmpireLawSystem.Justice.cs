using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems.EmpireLaw;

// 执法在人、律随时变(2026-11，用户："律法系统感觉很不完善")：
//   · 查案：查出未发之罪的机会 = 犯人才智定的底数 × 司法官(廷尉、大理寺、御史台……有"司法"职权的中央官职，在任与否、
//     才干操守) × 吏治(本城、本国腐败率) × 身份(宗室、公以上的贵族、名望高的难动)。寻常的国家合起来约为 1，与以前持平；
//   · 宽严：一国奉行的理念(执政理念、本朝官学：法家严、儒家黄老宽……)与君主(暴君、残忍好杀严，明君、好和平宽)
//     定宽严(-1 ~ +1)：严则人畏法少犯、量刑加一等、处决激起民怨并添暴君之名；宽则多犯、量刑减一等、多行恩赦；
//   · 量刑：律文 → 时代与政体(工业时代废笞杖、废连坐；现代政体以监禁为主，民主国家废死) → 宽严 →
//     八议(宗室、贵族、功臣减一等) → 赎刑(富者纳金赎笞杖徒流)。十恶不减等、不许赎、不在大赦之列。
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
        // 严苛、宽仁之国每案至多五成机会加/减一等
        info.SentenceShiftChance = info.Tier == LawStanceTier.适中 ? 0f : 0.5f * Mathf.Abs(severity);
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

    // 量刑：律文 → 时代与政体 → 宽严(严加一等、宽减一等) → 八议减一等 → 赎刑。十恶不减、不赎；现代政体不议、不赎
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
        if (stance.Tier == LawStanceTier.严苛 && UnityEngine.Random.value < stance.SentenceShiftChance)
        {
            if (Escalate(sentence, grades, context))
            {
                context.Escalated = true;
                AddNote(context.Notes, "law_note_harsh");
            }
        }
        else if (stance.Tier == LawStanceTier.宽仁 && !context.Grave && UnityEngine.Random.value < stance.SentenceShiftChance)
        {
            if (Reduce(sentence, grades, context))
            {
                context.Reduced = true;
                AddNote(context.Notes, "law_note_lenient");
            }
        }

        // 八议：宗室(议亲)、公以上的贵族(议贵)、勋级护军以上的功臣(议功)减一等
        if (!context.Grave && !regime.Modern)
        {
            string privilege = PrivilegeOf(context.Actor, kingdom);
            if (privilege != null && Reduce(sentence, grades, context))
            {
                context.Reduced = true;
                AddNote(context.Notes, privilege);
            }
        }

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

    // 八议(议亲、议贵、议功)：返回量刑说明的本地化键，不在八议之列为 null
    public static string PrivilegeOf(Actor actor, Kingdom kingdom)
    {
        if (actor == null || actor.isRekt()) return null;
        if (IsRoyalKin(actor, kingdom)) return "law_note_privilege_kin";
        if ((int)actor.GetPeeragesLevel() >= (int)PeeragesLevel.peerages_3) return "law_note_privilege_noble";
        OfficeIdentity identity = actor.GetOrCreate().officeIdentity;
        if (identity != null && identity.meritLevel <= GreatMeritLevel) return "law_note_privilege_merit";
        return null;
    }

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

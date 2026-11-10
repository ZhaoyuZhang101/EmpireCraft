using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems.EmpireLaw;

// 卷宗、前科、牢狱、大赦与律法的名称：
//   · 卷宗：每国留最近 MaxCases 桩结案(日期、犯人、罪名、所施刑罚、结果、案由与量刑说明)，存在 KingdomExtraData.law_state；
//   · 前科：记在犯人的族谱身份上(至多 MaxConvictions 条)，与未发之罪(crime_records)分开；
//   · 牢狱：监禁按刑期收押(LawImprisonment)，在押不得出任官职，刑满或大赦释放(要人出狱上公屏)；
//   · 大赦：新君即位(帝国看天子，独立王国看国王；只限君主制)、改元、朝堂"大赦天下"、宽仁之国的恩赦：
//     赦免此前犯下而未发觉的寻常罪行、释放在押的寻常罪犯(十恶不赦)，一年内只赦一次、只记一条；
//   · 滥刑：五年内处决满 MassExecutionCount 人，帝国正统 -3(独立王国官僚、农民怨气 +3)，五年至多一次。
public static partial class EmpireLawSystem
{
    public const int MaxCases = 30;
    public const int MaxConvictions = 8;
    private const int PurgeWindowYears = 5;
    private const int MassExecutionCount = 8;
    private const int MassExecutionMandate = 3;
    private const int GraceAmnestyYears = 10;

    #region 国家的执法记录

    public static LawRealmState GetRealmState(Kingdom kingdom, bool create = false)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return null;
        KingdomExtension.KingdomExtraData data = kingdom.GetOrCreate();
        if (data == null) return null;
        if (data.law_state == null && create) data.law_state = new LawRealmState();
        LawRealmState state = data.law_state;
        if (state != null)
        {
            state.cases ??= new List<LawCaseRecord>();
            state.prisoners ??= new List<long>();
            state.executions ??= new List<double>();
        }
        return state;
    }

    private static void AddCase(Kingdom kingdom, LawCaseRecord record)
    {
        LawRealmState state = GetRealmState(kingdom, true);
        if (state == null || record == null) return;
        state.cases.Add(record);
        if (state.cases.Count > MaxCases) state.cases.RemoveRange(0, state.cases.Count - MaxCases);
    }

    #endregion

    #region 结案

    // 判了什么先写进生平(施刑前写：伏诛的人死后不再记经历)
    private static void RecordSentenceHistory(LawEnforcementContext context)
    {
        Actor actor = context?.Actor;
        if (actor == null || actor.isRekt()) return;
        string punishments = context.Sentence != null && context.Sentence.Count > 0
            ? GetPunishmentsText(context.Sentence)
            : LM.Get("law_outcome_no_punishment");
        if (context.RansomPaid > 0)
            punishments = string.Format(LM.Get("law_history_ransomed"), punishments, context.RansomPaid);
        bool major = context.Sentence != null && context.Sentence.Any(p => IsLethal(p) || p == PunishmentLevel.流放);
        actor.RecordPersonalHistory(string.Format(LM.Get("personal_history_law_convicted"), context.LawName, punishments),
            relatedActorId: context.Accuser?.id ?? -1L, major: major);
    }

    // 定下结果，记卷宗与前科(同一案只记一次)。揭发罪行的计谋自己施刑，在写日志时补记(见 TranslateHelper.LogLawEnforcement)
    public static void FinishCase(LawEnforcementContext context)
    {
        if (context == null || context.CaseRecorded || context.Kingdom == null) return;
        context.CaseRecorded = true;
        List<PunishmentLevel> applied = (context.AppliedPunishments ?? new List<PunishmentLevel>())
            .Where(p => p != PunishmentLevel.无罪).Distinct().ToList();
        if (context.RansomPaid > 0) context.Outcome = LawCaseOutcome.赎罪;
        else if (context.Reduced) context.Outcome = LawCaseOutcome.减等;
        else if (applied.Count == 0) context.Outcome = LawCaseOutcome.开释;
        else if (context.Escalated) context.Outcome = LawCaseOutcome.从重;
        else context.Outcome = LawCaseOutcome.定罪;

        double now = World.world != null ? World.world.getCurWorldTime() : 0d;
        string date = World.world != null ? Date.getDate(now) : "";
        PersonalClanIdentity identity = context.Identity ?? context.Actor?.GetPersonalIdentity();
        var notes = new List<string>(context.Notes ?? new List<string>());
        if (context.FinePaid > 0) notes.Add("law_note_fine|" + context.FinePaid);
        if (context.PrisonYears > 0 && applied.Contains(PunishmentLevel.监禁))
            notes.Add((context.LifeTerm ? "law_note_life_term|" : "law_note_prison_term|") + context.PrisonYears);
        AddCase(context.Kingdom, new LawCaseRecord
        {
            timestamp = now,
            date = date,
            actor_id = context.Actor?.data != null ? context.Actor.id : -1L,
            identity_id = identity?.id ?? -1L,
            actor_name = context.ActorName ?? context.Actor?.getName() ?? identity?.name ?? "",
            law_type = (int)context.LawType,
            punishments = new List<PunishmentLevel>(applied),
            outcome = context.Outcome,
            reason_key = context.ReasonKey ?? "",
            notes = notes,
            accuser_id = context.Accuser?.data != null ? context.Accuser.id : -1L,
            accuser_name = context.Accuser?.getName() ?? "",
            // 量刑天平两边的砝码(卷宗悬停分两栏列，见 LawPanel.DrawCases)
            scale_guilt = ScaleEntries(context.Scale?.Guilt),
            scale_mercy = ScaleEntries(context.Scale?.Mercy),
            scale_guilt_weight = context.Scale?.GuiltWeight ?? 1f,
            scale_mercy_weight = context.Scale?.MercyWeight ?? 1f,
            scale_shift = context.ScaleShift,
            scale_mercy_ignored = context.Scale?.MercyIgnored == true
        });

        if (identity == null || context.Outcome == LawCaseOutcome.开释) return;
        identity.law_convictions ??= new List<LawConvictionRecord>();
        identity.law_convictions.Add(new LawConvictionRecord
        {
            timestamp = now,
            date = date,
            kingdom_id = context.Kingdom.getID(),
            kingdom_name = context.Kingdom.data?.name ?? "",
            law_type = (int)context.LawType,
            punishments = new List<PunishmentLevel>(applied),
            outcome = context.Outcome
        });
        if (identity.law_convictions.Count > MaxConvictions)
            identity.law_convictions.RemoveRange(0, identity.law_convictions.Count - MaxConvictions);
    }

    // 天平砝码存成"键|倍数|参数"(参数是人名、赃额，存下时的文字)
    private static List<string> ScaleEntries(List<LawFactor> factors) =>
        factors == null || factors.Count == 0 ? null
            : factors.Select(factor => $"{factor.Key}|{factor.Value.ToString("0.00", global::System.Globalization.CultureInfo.InvariantCulture)}|{factor.Arg ?? ""}").ToList();

    // 卷宗里存下的砝码还原成 LawFactor(界面用)
    public static List<LawFactor> ScaleFactors(List<string> entries)
    {
        var factors = new List<LawFactor>();
        foreach (string entry in entries ?? new List<string>())
        {
            string[] parts = (entry ?? "").Split(new[] { '|' }, 3);
            if (parts.Length < 2 || !float.TryParse(parts[1], global::System.Globalization.NumberStyles.Float,
                    global::System.Globalization.CultureInfo.InvariantCulture, out float value)) continue;
            factors.Add(new LawFactor(parts[0], value, parts.Length > 2 && parts[2].Length > 0 ? parts[2] : null));
        }
        return factors;
    }

    // 当场拿获的同一罪名、本国尚未发觉的旧案一并了结
    private static void ResolvePendingRecords(PersonalClanIdentity identity, LawType type, Kingdom kingdom)
    {
        if (identity?.crime_records == null) return;
        foreach (CrimeRecord record in identity.crime_records)
            if (record != null && !record.resolved && record.law_type == (int)type && InJurisdiction(record, kingdom))
                record.resolved = true;
    }

    #endregion

    #region 牢狱

    // 收押(由 ApplyImprisonment 调)。封国国君、天子下狱只去官，不收押(收押会让他失去君位)
    private static void Imprison(LawEnforcementContext context)
    {
        Actor actor = context?.Actor;
        if (actor == null || actor.isRekt() || actor.isKing() || context.Kingdom == null || World.world == null) return;
        PersonalClanIdentity identity = context.Identity ?? actor.GetPersonalIdentity();
        if (identity == null)
        {
            actor.CheckSpecificClan(false);
            identity = actor.GetPersonalIdentity();
            context.Identity = identity;
        }
        if (identity == null) return;
        int years = context.PrisonYears > 0 ? context.PrisonYears : BasePrisonYears(OrderedPunishments(context.Law?.Punishments));
        identity.law_imprisonment = new LawImprisonment
        {
            since = World.world.getCurWorldTime(),
            years = Math.Max(1, years),
            kingdom_id = context.Kingdom.getID(),
            law_type = (int)context.LawType,
            grave = context.Grave,
            notable = context.Broadcast
        };
        LawRealmState state = GetRealmState(context.Kingdom, true);
        if (state != null && !state.prisoners.Contains(identity.id)) state.prisoners.Add(identity.id);
    }

    private static bool ServedOut(LawImprisonment imprisonment) =>
        imprisonment == null || imprisonment.since < 0d || World.world == null ||
        World.world.getCurWorldTime() < imprisonment.since || Date.getYearsSince(imprisonment.since) >= imprisonment.years;

    // 在押(刑期未满；刑满没来得及放的也不算在押)
    public static bool IsImprisoned(Actor actor) => IsImprisoned(actor?.GetPersonalIdentity());

    public static bool IsImprisoned(PersonalClanIdentity identity) =>
        identity?.law_imprisonment != null && !ServedOut(identity.law_imprisonment);

    public static LawImprisonment GetImprisonment(Actor actor)
    {
        LawImprisonment imprisonment = actor?.GetPersonalIdentity()?.law_imprisonment;
        return imprisonment != null && !ServedOut(imprisonment) ? imprisonment : null;
    }

    // 还要关几年(不在押为 0)
    public static int GetImprisonmentYearsLeft(Actor actor)
    {
        LawImprisonment imprisonment = GetImprisonment(actor);
        return imprisonment == null ? 0 : Math.Max(1, imprisonment.years - Date.getYearsSince(imprisonment.since));
    }

    // 能不能出任官职(ActorExtension.CanServeOffice 调)：在押的不能；不在帝国里的国家按本国株连禁锢查祖上
    public static bool IsBarredFromOffice(Actor actor, Kingdom kingdom)
    {
        if (actor?.data == null) return false;
        if (IsImprisoned(actor)) return true;
        if (kingdom?.data == null || kingdom.isRekt() || kingdom.IsInEmpire()) return false;
        if (kingdom.GetOrCreate().law_state?.descendant_bans != true) return false;
        return BannedInKingdom(actor, kingdom.getID(), 0);
    }

    private static bool BannedInKingdom(Actor actor, long kingdomId, int depth)
    {
        if (actor?.data == null || depth > 4) return false;
        List<long> bans = actor.GetOrCreate().banned_office_kingdom_ids;
        if (bans != null && bans.Contains(kingdomId)) return true;
        var parents = actor.getParents();
        if (parents == null) return false;
        foreach (Actor parent in parents)
            if (parent != null && BannedInKingdom(parent, kingdomId, depth + 1)) return true;
        return false;
    }

    // 不在帝国里的国家株连子孙(罢官的 AffectDescendants)：本人与子孙不得在本国出仕
    private static void BanFromKingdomOffice(Actor actor, Kingdom kingdom)
    {
        if (actor?.data == null || kingdom?.data == null) return;
        ActorExtension.ActorExtraData data = actor.GetOrCreate();
        data.banned_office_kingdom_ids ??= new List<long>();
        if (!data.banned_office_kingdom_ids.Contains(kingdom.getID())) data.banned_office_kingdom_ids.Add(kingdom.getID());
        LawRealmState state = GetRealmState(kingdom, true);
        if (state != null) state.descendant_bans = true;
    }

    // 释放本国在押者：amnesty 为大赦(十恶不赦)，否则只放刑满的。返回放了几人
    private static int ReleasePrisoners(Kingdom kingdom, LawRealmState state, bool amnesty)
    {
        if (kingdom == null || state?.prisoners == null || state.prisoners.Count == 0) return 0;
        int released = 0;
        for (int i = state.prisoners.Count - 1; i >= 0; i--)
        {
            PersonalClanIdentity person = SpecificClanManager.getPerson(state.prisoners[i]);
            LawImprisonment imprisonment = person?.law_imprisonment;
            // 死了、已放了、改押别国的不再记在本国
            if (person == null || !person.is_alive || imprisonment == null || imprisonment.kingdom_id != kingdom.getID())
            {
                state.prisoners.RemoveAt(i);
                continue;
            }
            bool due = ServedOut(imprisonment);
            if (!due && !(amnesty && !imprisonment.grave)) continue;
            person.law_imprisonment = null;
            state.prisoners.RemoveAt(i);
            released++;
            if (due && !amnesty && imprisonment.notable)
                TranslateHelper.LogEventOnly(string.Format(LM.Get("law_prisoner_released_log"), person.name ?? "",
                    kingdom.GetKingdomName() ?? kingdom.name, GetLawName((LawType)imprisonment.law_type)), kingdom);
        }
        return released;
    }

    #endregion

    #region 狱政与大赦

    // 一年一次(国家 AI、首都查案时调)：刑满释放、清理处决记录；宽仁之国距上次大赦满十年后有机会恩赦
    public static void TickRealm(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt() || World.world == null) return;
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(kingdom)) return;
        LawRealmState state = GetRealmState(kingdom, true);
        if (state == null) return;
        double now = World.world.getCurWorldTime();
        if (state.last_tick >= 0d && now >= state.last_tick && Date.getYearsSince(state.last_tick) < 1) return;
        state.last_tick = now;

        ReleasePrisoners(kingdom, state, false);
        state.executions.RemoveAll(time => time < 0d || time > now || Date.getYearsSince(time) >= PurgeWindowYears);

        if (!Enabled || RealmKingdom(kingdom) != kingdom || IsModernLaw(kingdom)) return;
        LawStanceInfo stance = GetLawStance(kingdom);
        if (stance.AmnestyChance <= 0f) return;
        if (state.last_amnesty >= 0d && now >= state.last_amnesty && Date.getYearsSince(state.last_amnesty) < GraceAmnestyYears) return;
        if (UnityEngine.Random.value < stance.AmnestyChance) GrantAmnesty(kingdom, "law_amnesty_reason_grace");
    }

    // 大赦：赦免此前犯下而未发觉的寻常罪行(查案时按大赦的时间作废，见 PardonedByAmnesty)，释放在押的寻常罪犯。
    // 帝国之内赦遍全帝国。一年内只赦一次(即位、改元同年只记一条)。返回赦了没有
    public static bool GrantAmnesty(Kingdom kingdom, string reasonKey)
    {
        if (!Enabled || World.world == null) return false;
        Kingdom realm = RealmKingdom(kingdom);
        if (realm == null) return false;
        LawRealmState state = GetRealmState(realm, true);
        if (state == null) return false;
        double now = World.world.getCurWorldTime();
        if (state.last_amnesty >= 0d && now >= state.last_amnesty && Date.getYearsSince(state.last_amnesty) < 1) return false;
        state.last_amnesty = now;

        int released = 0;
        Empire empire = RealmEmpire(realm);
        var scope = new List<Kingdom> { realm };
        if (empire?.kingdoms_list != null)
            foreach (Kingdom member in empire.kingdoms_list)
                if (member?.data != null && !member.isRekt() && !scope.Contains(member)) scope.Add(member);
        foreach (Kingdom member in scope)
        {
            LawRealmState memberState = GetRealmState(member);
            if (memberState != null) released += ReleasePrisoners(member, memberState, true);
        }

        Actor ruler = RealmRuler(realm);
        AddCase(realm, new LawCaseRecord
        {
            timestamp = now,
            date = Date.getDate(now),
            actor_id = ruler?.data != null ? ruler.id : -1L,
            identity_id = ruler?.GetPersonalIdentity()?.id ?? -1L,
            actor_name = ruler?.getName() ?? "",
            law_type = -1,
            outcome = LawCaseOutcome.大赦,
            reason_key = reasonKey ?? "",
            count = released
        });
        if (empire != null || realm.countCities() >= SmallRealmCities)
            TranslateHelper.LogEventMessage(string.Format(LM.Get("law_amnesty_log"), RealmName(realm),
                GetReasonText(reasonKey), released), realm);
        return true;
    }

    // 新君即位大赦(帝国由 Empire.NewEmperor 调，不在帝国里的王国由 KingdomPatch 调)：只限君主制，
    // 帝国之内封国换君不赦，共和国、现代政体换元首不赦
    public static void OnRulerAccession(Kingdom kingdom, Actor ruler)
    {
        try
        {
            if (kingdom?.data == null || kingdom.isRekt() || ruler == null) return;
            if (RealmKingdom(kingdom) != kingdom) return;
            // 新立的国家(没有上一任君主)不算即位
            if (!kingdom.IsInEmpire() && kingdom.GetOrCreate().last_ruler_identity_id < 0) return;
            if (kingdom.GetRegime()?.is_monarchy != true || IsModernLaw(kingdom)) return;
            GrantAmnesty(kingdom, "law_amnesty_reason_accession");
        }
        catch (Exception exception)
        {
            NeoModLoader.services.LogService.LogWarning($"[律法] 即位大赦失败：{exception.Message}");
        }
    }

    // 改元大赦(由改元的计谋调)
    public static void OnEraNameChanged(Empire empire)
    {
        try
        {
            Kingdom core = empire?.CoreKingdom;
            if (core == null || core.isRekt() || IsModernLaw(core)) return;
            GrantAmnesty(core, "law_amnesty_reason_era");
        }
        catch (Exception exception)
        {
            NeoModLoader.services.LogService.LogWarning($"[律法] 改元大赦失败：{exception.Message}");
        }
    }

    // 未发之罪是不是已在大赦之列(十恶不赦)
    private static bool PardonedByAmnesty(CrimeRecord record)
    {
        if (record == null || record.crime_timestamp <= 0d) return false;
        if (IsGraveCrime((LawType)record.law_type)) return false;
        double amnesty = LastAmnestyFor(record);
        return amnesty >= 0d && record.crime_timestamp < amnesty;
    }

    private static double LastAmnestyFor(CrimeRecord record)
    {
        Kingdom realm = null;
        if (record.empire_id >= 0 && ModClass.EMPIRE_MANAGER != null)
            realm = ModClass.EMPIRE_MANAGER.get(record.empire_id)?.CoreKingdom;
        if ((realm?.data == null || realm.isRekt()) && record.kingdom_id >= 0 && World.world?.kingdoms != null)
            realm = RealmKingdom(World.world.kingdoms.get(record.kingdom_id));
        if (realm?.data == null || realm.isRekt()) return -1d;
        return realm.GetOrCreate().law_state?.last_amnesty ?? -1d;
    }

    public static double GetLastAmnesty(Kingdom kingdom) => GetRealmState(RealmKingdom(kingdom))?.last_amnesty ?? -1d;

    #endregion

    #region 处决的后果

    // 由 ApplyDeathPenalty 调(killed 含连坐的亲属)：
    //   · 严苛之国：每处决一案，官僚怨气 +1、农民 +0.5(夷三族加倍)，君主暴君值 +2(夷三族 +4)；
    //   · 不论宽严：五年内处决满 MassExecutionCount 人为滥刑，帝国正统 -3(独立王国官僚、农民怨气 +3)，五年至多一次
    private static void OnExecutions(LawEnforcementContext context, int killed, bool kin)
    {
        Kingdom realm = RealmKingdom(context?.Kingdom);
        if (realm == null || killed <= 0 || World.world == null) return;
        if (GetLawStance(realm).Tier == LawStanceTier.严苛)
        {
            float weight = kin ? 2f : 1f;
            AddRealmGrievance(realm, SocialClass.Officer, 1f * weight);
            AddRealmGrievance(realm, SocialClass.Peasant, 0.5f * weight);
            Actor ruler = RealmRuler(realm);
            if (ruler != null && ruler != context.Actor) ruler.AddTyrantValue(kin ? 4 : 2);
        }

        LawRealmState state = GetRealmState(realm, true);
        if (state == null) return;
        double now = World.world.getCurWorldTime();
        for (int i = 0; i < killed; i++) state.executions.Add(now);
        state.executions.RemoveAll(time => time < 0d || time > now || Date.getYearsSince(time) >= PurgeWindowYears);
        if (state.executions.Count < MassExecutionCount) return;
        if (state.last_purge_penalty >= 0d && now >= state.last_purge_penalty &&
            Date.getYearsSince(state.last_purge_penalty) < PurgeWindowYears) return;
        state.last_purge_penalty = now;
        Empire empire = RealmEmpire(realm);
        if (empire != null) empire.AddMandate(-MassExecutionMandate);
        else
        {
            AddRealmGrievance(realm, SocialClass.Officer, MassExecutionMandate);
            AddRealmGrievance(realm, SocialClass.Peasant, MassExecutionMandate);
        }
        TranslateHelper.LogEventMessage(string.Format(LM.Get("law_mass_execution_log"), RealmName(realm),
            state.executions.Count), realm);
    }

    // 阶层怨气(民怨的来源之一，见 PopularGrievance)：帝国按帝国，独立王国按本国(本国还没有阶层怨气的不加)
    private static void AddRealmGrievance(Kingdom realm, SocialClass socialClass, float delta)
    {
        Empire empire = RealmEmpire(realm);
        if (empire != null)
        {
            ClaimPolicySystem.AddGrievance(empire, socialClass, delta);
            return;
        }
        Dictionary<SocialClass, float> grievances = realm?.GetOrCreate()?.institution_state?.class_grievances;
        if (grievances == null) return;
        grievances.TryGetValue(socialClass, out float current);
        grievances[socialClass] = Mathf.Clamp(current + delta, 0f, 100f);
    }

    #endregion

    #region 名称

    // 罪名：law_name_<罪名>，没有这个键用律法表里的 Name，再没有用罪名本身
    public static string GetLawName(LawType type)
    {
        string key = "law_name_" + type;
        if (LM.Has(key)) return LM.Get(key);
        Law law = type.GetConfig();
        return law != null && !string.IsNullOrEmpty(law.Name) ? law.Name : type.ToString();
    }

    public static string GetLawDescription(LawType type)
    {
        string key = "law_desc_" + type;
        if (LM.Has(key)) return LM.Get(key);
        return type.GetConfig()?.Description ?? "";
    }

    // 罪名文本(以前各处传罪名枚举名或律法表的中文名)：认得出的换成本地化的罪名，认不出的原样返回
    public static string LocalizeCrimeName(string crime)
    {
        if (string.IsNullOrWhiteSpace(crime)) return crime ?? "";
        string text = crime.Trim();
        if (Enum.TryParse(text, out LawType type) && Enum.IsDefined(typeof(LawType), type)) return GetLawName(type);
        foreach (KeyValuePair<LawType, Law> pair in Laws)
            if (pair.Value != null && pair.Value.Name == text) return GetLawName(pair.Key);
        return crime;
    }

    public static string GetPunishmentName(PunishmentLevel punishment)
    {
        string key = "punishment_" + punishment;
        return LM.Has(key) ? LM.Get(key) : punishment.ToString();
    }

    public static string GetPunishmentsText(IEnumerable<PunishmentLevel> punishments)
    {
        List<string> names = (punishments ?? Enumerable.Empty<PunishmentLevel>())
            .Where(p => p != PunishmentLevel.无罪).Distinct().Select(GetPunishmentName)
            .Where(name => !string.IsNullOrEmpty(name)).ToList();
        return names.Count > 0 ? string.Join(LM.Has("law_list_separator") ? LM.Get("law_list_separator") : ", ", names)
            : LM.Get("law_outcome_no_punishment");
    }

    public static string GetOutcomeName(LawCaseOutcome outcome)
    {
        string key = "law_outcome_" + outcome;
        return LM.Has(key) ? LM.Get(key) : outcome.ToString();
    }

    public static string GetStanceName(LawStanceTier tier)
    {
        string key = "law_stance_" + tier;
        return LM.Has(key) ? LM.Get(key) : tier.ToString();
    }

    // 案由(Prosecute 的 reasonKey；大赦的缘由)。没有这个本地化键的不显示(卷宗里照存键名)
    public static string GetReasonText(string reasonKey)
    {
        if (string.IsNullOrWhiteSpace(reasonKey) || !LM.Has(reasonKey)) return "";
        return LM.Get(reasonKey);
    }

    // 量刑说明的一条("键|参数")
    public static string FormatNote(string note)
    {
        if (string.IsNullOrWhiteSpace(note)) return "";
        int split = note.IndexOf('|');
        string key = split >= 0 ? note.Substring(0, split) : note;
        string arg = split >= 0 ? note.Substring(split + 1) : null;
        return new LawFactor(key, 0f, arg).Label;
    }

    // 卷宗的说明：案由、告发者、量刑说明，逗号隔开
    public static string GetCaseNoteText(LawCaseRecord record)
    {
        if (record == null) return "";
        var parts = new List<string>();
        string reason = GetReasonText(record.reason_key);
        if (!string.IsNullOrEmpty(reason)) parts.Add(reason);
        if (!string.IsNullOrEmpty(record.accuser_name))
            parts.Add(string.Format(LM.Get("law_note_accuser"), record.accuser_name));
        if (record.outcome == LawCaseOutcome.大赦)
            parts.Add(string.Format(LM.Get("law_note_amnesty_released"), record.count));
        foreach (string note in record.notes ?? new List<string>())
        {
            string text = FormatNote(note);
            if (!string.IsNullOrEmpty(text)) parts.Add(text);
        }
        return string.Join(LM.Has("law_list_separator") ? LM.Get("law_list_separator") : ", ", parts);
    }

    private static string RealmName(Kingdom realm)
    {
        Empire empire = RealmEmpire(realm);
        if (empire != null) return empire.GetEmpireName() ?? realm.name;
        return realm.GetKingdomName() ?? realm.name;
    }

    #endregion

    #region 给界面的只读接口

    // 本国最近的卷宗(新的在前)
    public static List<LawCaseRecord> GetRecentCases(Kingdom kingdom)
    {
        LawRealmState state = GetRealmState(kingdom);
        if (state?.cases == null) return new List<LawCaseRecord>();
        var result = new List<LawCaseRecord>(state.cases);
        result.Reverse();
        return result;
    }

    // 帝国全境(核心国与各成员国)最近的卷宗，新的在前；不在帝国里的同 GetRecentCases
    public static List<LawCaseRecord> GetRealmRecentCases(Kingdom kingdom, int limit = MaxCases)
    {
        Kingdom realm = RealmKingdom(kingdom);
        Empire empire = RealmEmpire(realm);
        if (empire?.kingdoms_list == null) return GetRecentCases(realm).Take(limit).ToList();
        var all = new List<LawCaseRecord>(GetRealmState(realm)?.cases ?? new List<LawCaseRecord>());
        foreach (Kingdom member in empire.kingdoms_list)
            if (member != realm && GetRealmState(member)?.cases is List<LawCaseRecord> cases) all.AddRange(cases);
        return all.OrderByDescending(record => record.timestamp).Take(limit).ToList();
    }

    // 某人的前科(新的在前)
    public static List<LawConvictionRecord> GetConvictions(Actor actor) => GetConvictions(actor?.GetPersonalIdentity());

    public static List<LawConvictionRecord> GetConvictions(PersonalClanIdentity identity)
    {
        if (identity?.law_convictions == null) return new List<LawConvictionRecord>();
        var result = new List<LawConvictionRecord>(identity.law_convictions);
        result.Reverse();
        return result;
    }

    // 某人尚未发觉的罪行(只读；含已被大赦作废、还没清理的不算)
    public static List<CrimeRecord> GetPendingCrimes(Actor actor)
    {
        PersonalClanIdentity identity = actor?.GetPersonalIdentity();
        if (identity?.crime_records == null) return new List<CrimeRecord>();
        return identity.crime_records.Where(record => record != null && !record.resolved && !PardonedByAmnesty(record) &&
                                                      (record.crime_timestamp <= 0d || Date.getYearsSince(record.crime_timestamp) < 20))
            .ToList();
    }

    // 本国在押的人(族谱身份)
    public static List<PersonalClanIdentity> GetPrisoners(Kingdom kingdom)
    {
        var result = new List<PersonalClanIdentity>();
        LawRealmState state = GetRealmState(kingdom);
        if (state?.prisoners == null) return result;
        foreach (long id in state.prisoners)
        {
            PersonalClanIdentity person = SpecificClanManager.getPerson(id);
            if (person != null && person.is_alive && IsImprisoned(person)) result.Add(person);
        }
        return result;
    }

    #endregion
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems.EmpireLaw;

public enum LawCategory
{
    王权与国家安全,
    官制与行政,
    刑法,
    财产与经济,
    土地与农业,
    军事,
    婚姻与家族,
    宗教与礼制,
    商贸与契约,
    城市与公共秩序
}

public enum LawType
{
    谋反 = 0,
    叛国 = 1,
    篡位 = 2,
    私通敌国 = 3,
    伪造诏令 = 4,
    煽动叛乱 = 5,

    贪污 = 6,
    受贿 = 7,
    买官 = 8,
    卖官 = 9,
    滥用职权 = 10,
    玩忽职守 = 11,
    冒充官员 = 12,

    杀人 = 13,
    故意伤害 = 14,
    强奸 = 15,
    绑架 = 16,
    放火 = 17,
    投毒 = 18,
    陷害 = 19,
    抢劫 = 20,
    盗窃 = 21,

    诈骗 = 22,
    伪造货币 = 23,
    偷税漏税 = 24,
    走私 = 25,
    哄抬物价 = 26,
    非法侵占土地 = 27,

    破坏农田 = 28,
    破坏水利 = 29,
    隐瞒田亩 = 30,
    逃避徭役 = 31,

    临阵脱逃 = 32,
    违抗军令 = 33,
    私卖军械 = 34,
    谎报军功 = 35,
    抢掠平民 = 36,

    重婚 = 37,
    遗弃家庭 = 38,
    虐待亲属 = 39,
    非法继承 = 40,
    伪造血统 = 41,

    亵渎神庙 = 42,
    破坏祭祀 = 43,
    冒充神职 = 44,
    宣扬异端 = 45,

    违约 = 46,
    伪造契约 = 47,
    欺诈交易 = 48,
    缺斤少两 = 49,

    非法持械 = 50,
    聚众斗殴 = 51,
    违反宵禁 = 52,
    扰乱集市 = 53,
    污染水源 = 54,
    散布恐慌谣言 = 55,
    过于强大 = 56
}

public enum PunishmentLevel
{
    无罪,
    罚金,
    笞刑,
    杖刑,
    监禁,
    流放,
    没收财产,
    剥夺爵位,
    剥夺官职,
    死刑,
    夷三族
}

public class EmpireLawConfig
{
    public List<Law> Laws { get; set; } = new List<Law>();
}

public class Law
{
    public LawType Type { get; set; }
    public LawCategory Category { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public bool IsForbidden { get; set; } = false;
    public bool AffectDescendants { get; set; } = false;
    public List<PunishmentLevel> Punishments { get; set; }
    public string Note { get; set; }
}

public class LawEnforcementContext
{
    public Actor Actor { get; set; }
    public Kingdom Kingdom { get; set; }
    public Regimes.Regime Regime { get; set; }
    public LawType LawType { get; set; }
    public Law Law { get; set; }
    public string CrimeDate { get; set; }
    public List<PunishmentLevel> AppliedPunishments { get; set; } = new();
}

public static class EmpireLawSystem
{
    private const string AUTO_SCAN_KEY = "empire_law_auto_scan";
    private const string MERCENARY_OVERMIGHTY_KEY = "law_mercenary_overmighty";

    public static Dictionary<LawType, Law> Laws { get; set; } = new Dictionary<LawType, Law>();

    public static void init()
    {
        LoadFromJson(Path.Combine(ModClass._declare.FolderPath, "Scripts", "GeneralSystems", "EmpireLaw", "EmpireLawConfig.json"));
    }

    public static void LoadFromJson(string path)
    {
        string json = File.ReadAllText(path);
        JToken token = JToken.Parse(json);
        List<Law> lawList = null;

        if (token.Type == JTokenType.Array)
        {
            lawList = token.ToObject<List<Law>>();
        }
        else
        {
            EmpireLawConfig config = token.ToObject<EmpireLawConfig>();
            lawList = config != null ? config.Laws : null;
        }

        Laws = new Dictionary<LawType, Law>();
        if (lawList == null)
        {
            lawList = new List<Law>();
        }

        lawList.ForEach(delegate(Law l)
        {
            if (l != null && !Laws.ContainsKey(l.Type))
            {
                Laws.Add(l.Type, l);
            }
        });
    }

    public static Law GetConfig(this LawType type)
    {
        Law law;
        bool success = Laws.TryGetValue(type, out law);
        if (!success) return Laws.FirstOrDefault().Value;
        return law;
    }

    public static bool HasLaw(this Regimes.Regime regime, LawType type)
    {
        return regime != null && regime.laws != null && regime.laws.Contains(type);
    }

    public static bool HasLaw(this Kingdom kingdom, LawType type)
    {
        return kingdom != null && kingdom.GetRegime().HasLaw(type);
    }

    public static bool CanEnforceLawInEmpireScope(Actor actor, Kingdom kingdom)
    {
        if (actor == null || actor.isRekt() || kingdom == null || kingdom.isRekt())
        {
            return false;
        }

        if (!kingdom.IsInEmpire())
        {
            return false;
        }

        Empire empire = kingdom.GetEmpire();
        if (empire == null || empire.isRekt() || empire.IsArchived())
        {
            return false;
        }

        if (!actor.hasKingdom() || !actor.kingdom.IsInEmpire())
        {
            return false;
        }

        return actor.kingdom.GetEmpireID() == kingdom.GetEmpireID();
    }

    public static LawEnforcementContext TryEnforceLaw(this Actor actor, LawType type, Kingdom kingdom)
    {
        return TryEnforceLaw(actor, type, kingdom, null, null);
    }

    public static LawEnforcementContext TryEnforceLaw(Actor actor, LawType type, Kingdom kingdom,
        Action<LawEnforcementContext> extraPunishment)
    {
        return TryEnforceLaw(actor, type, kingdom, extraPunishment, null);
    }

    public static LawEnforcementContext TryEnforceLaw(Actor actor, LawType type, Kingdom kingdom,
        Action<LawEnforcementContext> extraPunishment, string crimeDate)
    {
        if (actor == null || actor.isRekt()) return null;
        if (kingdom == null)
        {
            kingdom = actor.kingdom;
        }
        if (kingdom == null || kingdom.isRekt()) return null;
        if (!CanEnforceLawInEmpireScope(actor, kingdom)) return null;

        Regimes.Regime regime = kingdom.GetRegime();
        if (!regime.HasLaw(type))
        {
            return null;
        }

        Law law = type.GetConfig();
        if (law == null)
        {
            return null;
        }

        LawEnforcementContext context = new LawEnforcementContext
        {
            Actor = actor,
            Kingdom = kingdom,
            Regime = regime,
            LawType = type,
            Law = law,
            CrimeDate = crimeDate
        };

        foreach (PunishmentLevel punishment in law.Punishments ?? new List<PunishmentLevel>())
        {
            if (ApplyPunishment(context, punishment))
            {
                context.AppliedPunishments.Add(punishment);
            }
        }

        if (extraPunishment != null)
        {
            extraPunishment(context);
        }

        if (context.AppliedPunishments.Count > 0)
        {
            LogService.LogInfo("Law enforced: " + type + " -> " + actor.getName() + " | " + string.Join(", ", context.AppliedPunishments));
        }

        TranslateHelper.LogLawEnforcement(context);
        return context;
    }

    public static bool TryApplyOptionalPunishment(LawEnforcementContext context, PunishmentLevel punishment)
    {
        if (context == null) return false;
        if (!ApplyPunishment(context, punishment)) return false;
        context.AppliedPunishments.Add(punishment);
        return true;
    }

    public static void CheckAutomaticLawTriggers(Actor actor)
    {
        if (actor == null || actor.isRekt() || !actor.isAlive() || !actor.isAdult()) return;
        if (!ShouldAutoCheckCrimeActor(actor)) return;
        if (!actor.hasKingdom()) return;

        Kingdom kingdom = actor.kingdom;
        if (!CanEnforceLawInEmpireScope(actor, kingdom)) return;
        if (!actor.IsLawCheckDue(AUTO_SCAN_KEY, 1f)) return;

        actor.RecordLawCheck(AUTO_SCAN_KEY);

        if (TryDetectRecordedCrime(actor, kingdom)) return;
        if (TryTriggerOfficialLaws(actor, kingdom)) return;
        TryTriggerGeneralLaws(actor, kingdom);
    }

    public static bool CheckAutomaticLawTriggersForCity(City city)
    {
        if (city == null || city.isRekt() || !city.hasKingdom()) return false;

        Kingdom kingdom = city.kingdom;
        if (kingdom == null || kingdom.isRekt() || !kingdom.IsInEmpire()) return false;

        HashSet<long> checkedActors = new HashSet<long>();

        bool TryCheckActor(Actor actor)
        {
            if (!ShouldAutoCheckCrimeActor(actor)) return false;
            if (!checkedActors.Add(actor.getID())) return false;
            CheckAutomaticLawTriggers(actor);
            return true;
        }

        if (TryCheckActor(city.leader)) return true;
        if (TryCheckActor(city.GetOffice()?.GetActor())) return true;
        if (city.isCapitalCity() && TryCheckActor(kingdom.king)) return true;

        foreach (Actor actor in city.units)
        {
            if (TryCheckActor(actor))
            {
                return true;
            }
        }

        return false;
    }

    public static void CheckMercenaryOvermightyLaw(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt() || !kingdom.IsInEmpire()) return;

        Empire empire = kingdom.GetEmpire();
        if (empire == null || empire.isRekt() || empire.IsArchived()) return;
        if (kingdom == empire.CoreKingdom) return;
        if (!kingdom.hasKing()) return;

        Actor leader = kingdom.king;
        Actor emperor = empire.Emperor;
        if (leader == null || leader.isRekt() || emperor == null || emperor.isRekt()) return;
        if (!leader.IsLawCheckDue(MERCENARY_OVERMIGHTY_KEY, 1f)) return;

        leader.RecordLawCheck(MERCENARY_OVERMIGHTY_KEY);

        LawType lawType = LawType.过于强大;
        if (!kingdom.HasLaw(lawType)) return;

        int empireWarriors = empire.countWarriors();
        if (empireWarriors <= 0) return;
        if (kingdom.countTotalWarriors() * 2 < empireWarriors) return;

        int emperorInfluence = emperor.data != null ? emperor.data.renown : 0;
        int leaderInfluence = leader.data != null ? leader.data.renown : 0;
        if (leaderInfluence <= 0 || emperorInfluence <= leaderInfluence) return;

        leader.RecordCrime(lawType);
    }

    private static bool TryTriggerOfficialLaws(Actor actor, Kingdom kingdom)
    {
        if (actor == null || kingdom == null) return false;

        bool isOfficial = actor.isOfficer() || actor.IsOnOffice() || actor.GetOffice() != null || actor.isKing();
        if (!isOfficial) return false;

        float corruptionModifier = GetEconomicCrimeModifier(actor);
        float abuseModifier = GetAuthorityCrimeModifier(actor);
        float negligenceModifier = GetNeglectCrimeModifier(actor);

        if (TryTriggerProbabilisticLaw(actor, LawType.受贿, 0.03f + corruptionModifier, ApplyOptionalPeerageRemoval)) return true;
        if (TryTriggerProbabilisticLaw(actor, LawType.买官, 0.02f + corruptionModifier, ApplyOptionalPeerageRemoval)) return true;
        if (TryTriggerProbabilisticLaw(actor, LawType.卖官, 0.025f + corruptionModifier, ApplyOptionalPeerageRemoval)) return true;
        if (TryTriggerProbabilisticLaw(actor, LawType.滥用职权, 0.03f + abuseModifier, ApplyOptionalPeerageRemoval)) return true;
        if (TryTriggerProbabilisticLaw(actor, LawType.玩忽职守, 0.025f + negligenceModifier, null)) return true;

        return TryTriggerProbabilisticLaw(actor, LawType.谎报军功, 0.02f + GetMilitaryCrimeModifier(actor), null);
    }

    private static bool TryTriggerGeneralLaws(Actor actor, Kingdom kingdom)
    {
        if (actor == null || kingdom == null) return false;

        float economicModifier = GetEconomicCrimeModifier(actor);
        float violentModifier = GetViolentCrimeModifier(actor);
        float deceitModifier = GetDeceitCrimeModifier(actor);
        float disorderModifier = GetDisorderCrimeModifier(actor);

        if (TryTriggerProbabilisticLaw(actor, LawType.杀人, 0.005f + violentModifier, null)) return true;
        if (TryTriggerProbabilisticLaw(actor, LawType.故意伤害, 0.015f + violentModifier, null)) return true;
        if (TryTriggerProbabilisticLaw(actor, LawType.绑架, 0.008f + violentModifier + deceitModifier * 0.5f, null)) return true;
        if (TryTriggerProbabilisticLaw(actor, LawType.陷害, 0.012f + deceitModifier, null)) return true;
        if (TryTriggerProbabilisticLaw(actor, LawType.盗窃, 0.02f + economicModifier, null)) return true;
        if (TryTriggerProbabilisticLaw(actor, LawType.诈骗, 0.02f + deceitModifier + economicModifier * 0.5f, null)) return true;
        if (TryTriggerProbabilisticLaw(actor, LawType.偷税漏税, 0.02f + economicModifier, null)) return true;
        if (TryTriggerProbabilisticLaw(actor, LawType.走私, 0.015f + economicModifier + deceitModifier * 0.5f, null)) return true;
        if (TryTriggerProbabilisticLaw(actor, LawType.哄抬物价, 0.012f + economicModifier, null)) return true;
        if (TryTriggerProbabilisticLaw(actor, LawType.非法侵占土地, 0.01f + economicModifier + GetNobleCrimeModifier(actor), null)) return true;
        if (TryTriggerProbabilisticLaw(actor, LawType.隐瞒田亩, 0.015f + economicModifier, null)) return true;
        if (TryTriggerProbabilisticLaw(actor, LawType.逃避徭役, 0.02f + GetDutyEvasionModifier(actor), null)) return true;
        if (TryTriggerProbabilisticLaw(actor, LawType.违约, 0.015f + deceitModifier, null)) return true;
        if (TryTriggerProbabilisticLaw(actor, LawType.欺诈交易, 0.018f + deceitModifier + economicModifier * 0.5f, null)) return true;
        if (TryTriggerProbabilisticLaw(actor, LawType.缺斤少两, 0.012f + deceitModifier, null)) return true;
        if (TryTriggerProbabilisticLaw(actor, LawType.非法持械, 0.01f + violentModifier, null)) return true;
        if (TryTriggerProbabilisticLaw(actor, LawType.聚众斗殴, 0.02f + disorderModifier + violentModifier * 0.5f, null)) return true;
        if (TryTriggerProbabilisticLaw(actor, LawType.扰乱集市, 0.015f + disorderModifier, null)) return true;

        return TryTriggerProbabilisticLaw(actor, LawType.散布恐慌谣言, 0.02f + deceitModifier + disorderModifier * 0.5f, null);
    }

    public static bool TryTriggerProbabilisticLaw(this Actor actor, LawType lawType, float chance,
        Action<LawEnforcementContext> extraPunishment)
    {
        var kingdom = actor?.kingdom;
        if (kingdom == null) return false;
        if (!kingdom.HasLaw(lawType)) return false;

        float finalChance = Mathf.Clamp01(chance);
        if (finalChance <= 0f) return false;
        if (UnityEngine.Random.value > finalChance) return false;

        return RecordCrime(actor, lawType);
    }

    public static bool RecordCrime(this Actor actor, LawType lawType)
    {
        if (actor == null || actor.isRekt()) return false;
        var kingdom = actor.kingdom;
        if (kingdom == null || !CanEnforceLawInEmpireScope(actor, kingdom)) return false;
        if (!kingdom.HasLaw(lawType)) return false;

        if (actor.isKing())
        {
            actor.AddTyrantValue(10);
            return true;
        }

        actor.CheckSpecificClan(false);
        PersonalClanIdentity identity = actor.GetPersonalIdentity();
        if (identity == null) return false;

        CleanupCrimeRecords(identity);
        List<CrimeRecord> records = identity.crime_records;
        if (records == null)
        {
            records = new List<CrimeRecord>();
            identity.crime_records = records;
        }

        for (int i = 0; i < records.Count; i++)
        {
            CrimeRecord existing = records[i];
            if (existing == null || existing.resolved) continue;
            if (existing.law_type != (int)lawType) continue;
            if (existing.empire_id != kingdom.GetEmpireID()) continue;
            if (existing.crime_timestamp > 0 && Date.getYearsSince(existing.crime_timestamp) < 20f)
            {
                return false;
            }
        }

        double now = World.world.getCurWorldTime();
        CrimeRecord record = new CrimeRecord
        {
            law_type = (int)lawType,
            kingdom_id = kingdom.getID(),
            empire_id = kingdom.GetEmpireID(),
            crime_timestamp = now,
            crime_date = Date.getDate(now)
        };
        records.Add(record);
        return true;
    }

    public static CrimeRecord GetResolvableCrimeRecord(Actor actor, Kingdom kingdom)
    {
        if (actor == null || actor.isRekt() || kingdom == null)
        {
            return null;
        }

        PersonalClanIdentity identity = actor.GetPersonalIdentity();
        if (identity == null)
        {
            return null;
        }

        CleanupCrimeRecords(identity);
        if (identity.crime_records == null || identity.crime_records.Count <= 0)
        {
            return null;
        }

        for (int i = 0; i < identity.crime_records.Count; i++)
        {
            CrimeRecord record = identity.crime_records[i];
            if (record == null || record.resolved) continue;
            if (record.empire_id != kingdom.GetEmpireID()) continue;
            if (!CanResolveCrimeRecord(actor, kingdom, record)) continue;
            return record;
        }

        return null;
    }

    public static bool HasResolvableCrimeRecord(Actor actor, Kingdom kingdom)
    {
        return GetResolvableCrimeRecord(actor, kingdom) != null;
    }

    public static string GetResolvableCrimeName(Actor actor, Kingdom kingdom)
    {
        CrimeRecord record = GetResolvableCrimeRecord(actor, kingdom);
        if (record == null)
        {
            return null;
        }

        LawType lawType = (LawType)record.law_type;
        Law law = lawType.GetConfig();
        return law != null && !string.IsNullOrEmpty(law.Name) ? law.Name : lawType.ToString();
    }

    public static LawEnforcementContext TryEnforceCrimeForClaim(Actor actor, Kingdom kingdom)
    {
        if (actor == null || actor.isRekt() || kingdom == null)
        {
            return null;
        }

        CrimeRecord record = GetResolvableCrimeRecord(actor, kingdom);
        if (record == null)
        {
            return null;
        }

        record.discovered = true;
        record.discovered_timestamp = World.world.getCurWorldTime();
        record.discovered_date = Date.getDate(record.discovered_timestamp);

        LawType lawType = (LawType)record.law_type;
        Law law = lawType.GetConfig();
        string lawName = law != null && !string.IsNullOrEmpty(law.Name) ? law.Name : lawType.ToString();

          LawEnforcementContext context = TryEnforceLaw(actor, lawType, kingdom, ApplyRecordedOptionalPunishments, record.crime_date);
          if (context != null)
          {
              if (context.AppliedPunishments != null && context.AppliedPunishments.Any(p =>
                      p == PunishmentLevel.剥夺官职 || p == PunishmentLevel.剥夺爵位))
              {
                  TranslateHelper.LogTemporaryFactionOfficialFall(kingdom.GetEmpire(), actor, lawName);
              }
              record.resolved = true;
          }

        return context;
    }

    private static bool TryDetectRecordedCrime(Actor actor, Kingdom kingdom)
    {
        if (actor == null || kingdom == null) return false;
        if (!ShouldAutoCheckCrimeActor(actor)) return false;

        PersonalClanIdentity identity = actor.GetPersonalIdentity();
        if (identity == null || identity.crime_records == null || identity.crime_records.Count <= 0)
        {
            return false;
        }

        CleanupCrimeRecords(identity);

        for (int i = 0; i < identity.crime_records.Count; i++)
        {
            CrimeRecord record = identity.crime_records[i];
            if (record == null || record.resolved) continue;
            if (record.empire_id != kingdom.GetEmpireID()) continue;
            if (!CanResolveCrimeRecord(actor, kingdom, record)) continue;
            if (UnityEngine.Random.value > GetCrimeDiscoveryChance(actor)) continue;

            record.discovered = true;
            record.discovered_timestamp = World.world.getCurWorldTime();
            record.discovered_date = Date.getDate(record.discovered_timestamp);

            LawType lawType = (LawType)record.law_type;
            LawEnforcementContext context = TryEnforceLaw(actor, lawType, kingdom, ApplyRecordedOptionalPunishments, record.crime_date);
            if (context != null)
            {
                record.resolved = true;
                return true;
            }
        }

        CleanupCrimeRecords(identity);
        return false;
    }

    private static bool ShouldAutoCheckCrimeActor(Actor actor)
    {
        if (actor == null || actor.isRekt() || !actor.isAlive() || !actor.isAdult())
        {
            return false;
        }

        if (actor.IsEmperor())
        {
            return false;
        }

        return actor.isOfficer() || actor.IsOnOffice() || actor.GetOffice() != null || actor.isKing() || actor.HasTitle() || actor.isCityLeader();
    }

    private static void CleanupCrimeRecords(PersonalClanIdentity identity)
    {
        if (identity == null)
        {
            return;
        }

        if (identity.crime_records == null)
        {
            identity.crime_records = new List<CrimeRecord>();
            return;
        }

        identity.crime_records.RemoveAll(delegate(CrimeRecord record)
        {
            if (record == null) return true;
            if (record.resolved) return true;
            return record.crime_timestamp > 0 && Date.getYearsSince(record.crime_timestamp) >= 20f;
        });
    }

    private static float GetCrimeDiscoveryChance(Actor actor)
    {
        if (actor == null) return 0.1f;

        int intelligence = actor.intelligence;
        if (intelligence < 0) intelligence = 0;
        if (intelligence > 40) intelligence = 40;

        float discoverProb = 0.45f - (intelligence / 40f) * 0.30f;
        if (discoverProb < 0.05f) discoverProb = 0.05f;
        if (discoverProb > 0.45f) discoverProb = 0.45f;
        return discoverProb;
    }

    private static bool CanResolveCrimeRecord(Actor actor, Kingdom kingdom, CrimeRecord record)
    {
        if (actor == null || kingdom == null || record == null) return false;

        LawType lawType = (LawType)record.law_type;
        if (lawType == LawType.过于强大)
        {
            Empire empire = kingdom.GetEmpire();
            if (empire == null || empire.isRekt() || empire.IsArchived()) return false;
            if (kingdom == empire.CoreKingdom) return false;
            if (kingdom.countTotalWarriors() * 2 < empire.countWarriors()) return false;

            Actor emperor = empire.Emperor;
            int emperorInfluence = emperor != null && emperor.data != null ? emperor.data.renown : 0;
            int actorInfluence = actor.data?.renown ?? 0;
            return emperor != null && emperorInfluence > actorInfluence && actorInfluence > 0;
        }

        return true;
    }

    private static void ApplyRecordedOptionalPunishments(LawEnforcementContext context)
    {
        if (context == null) return;

        switch (context.LawType)
        {
            case LawType.贪污:
            case LawType.受贿:
            case LawType.买官:
            case LawType.卖官:
            case LawType.滥用职权:
                ApplyOptionalPeerageRemoval(context);
                break;
            case LawType.过于强大:
                ConsumeMercenaryOvermightyInfluence(context);
                break;
        }
    }

    private static void ApplyOptionalPeerageRemoval(LawEnforcementContext context)
    {
        Actor actor = context != null ? context.Actor : null;
        if (actor == null) return;
        if (actor.HasTitle() || actor.GetPeeragesLevel() != PeeragesLevel.peerages_6)
        {
            TryApplyOptionalPunishment(context, PunishmentLevel.剥夺爵位);
        }
    }

    private static void ConsumeMercenaryOvermightyInfluence(LawEnforcementContext context)
    {
        if (context == null || context.Kingdom == null || context.Actor == null) return;

        Empire empire = context.Kingdom.GetEmpire();
        Actor emperor = empire?.Emperor;
        if (emperor == null || emperor.data == null || context.Actor.data == null) return;

        int cost = context.Actor.data.renown;
        if (cost <= 0) return;
        if (emperor.data.renown < cost) return;

        emperor.data.renown -= cost;
        if (emperor.data.renown < 0)
        {
            emperor.data.renown = 0;
        }
    }

    private static float GetEconomicCrimeModifier(Actor actor)
    {
        float chance = 0f;
        if (HasAnyTrait(actor, "greedy", "deceitful", "evil", "ambitious")) chance += 0.04f;
        if (HasAnyTrait(actor, "honest", "content", "wise")) chance -= 0.02f;
        if (actor.money > 150) chance += 0.015f;
        if (actor.stewardship >= 10) chance += 0.01f;
        return chance;
    }

    private static float GetAuthorityCrimeModifier(Actor actor)
    {
        float chance = 0f;
        if (HasAnyTrait(actor, "ambitious", "evil", "deceitful")) chance += 0.05f;
        if (HasAnyTrait(actor, "content", "honest")) chance -= 0.015f;
        if (actor.stewardship >= 8 || actor.intelligence >= 8) chance += 0.01f;
        return chance;
    }

    private static float GetNeglectCrimeModifier(Actor actor)
    {
        float chance = 0f;
        if (HasAnyTrait(actor, "lazy", "stupid", "madness")) chance += 0.05f;
        if (HasAnyTrait(actor, "wise", "honest")) chance -= 0.02f;
        return chance;
    }

    private static float GetViolentCrimeModifier(Actor actor)
    {
        float chance = 0f;
        if (HasAnyTrait(actor, "evil", "bloodlust", "madness")) chance += 0.04f;
        if (actor.warfare >= 10) chance += 0.01f;
        if (HasAnyTrait(actor, "pacifist")) chance -= 0.02f;
        return chance;
    }

    private static float GetDeceitCrimeModifier(Actor actor)
    {
        float chance = 0f;
        if (HasAnyTrait(actor, "deceitful", "greedy", "evil")) chance += 0.04f;
        if (actor.intelligence >= 10) chance += 0.01f;
        if (HasAnyTrait(actor, "honest")) chance -= 0.02f;
        return chance;
    }

    private static float GetDisorderCrimeModifier(Actor actor)
    {
        float chance = 0f;
        if (HasAnyTrait(actor, "evil", "madness", "deceitful")) chance += 0.03f;
        if (HasAnyTrait(actor, "wise", "pacifist")) chance -= 0.015f;
        return chance;
    }

    private static float GetNobleCrimeModifier(Actor actor)
    {
        if (actor == null) return 0f;
        if (actor.HasTitle() || actor.isNoble() || actor.isKing()) return 0.015f;
        return 0f;
    }

    private static float GetDutyEvasionModifier(Actor actor)
    {
        float chance = 0f;
        if (HasAnyTrait(actor, "lazy", "pacifist")) chance += 0.03f;
        if (HasAnyTrait(actor, "honest")) chance -= 0.015f;
        return chance;
    }

    private static float GetMilitaryCrimeModifier(Actor actor)
    {
        float chance = 0f;
        if (HasAnyTrait(actor, "ambitious", "evil", "deceitful")) chance += 0.03f;
        if (actor.warfare >= 10) chance += 0.015f;
        return chance;
    }

    private static bool HasAnyTrait(Actor actor, params string[] traitIds)
    {
        if (actor == null || traitIds == null) return false;
        for (int i = 0; i < traitIds.Length; i++)
        {
            string traitId = traitIds[i];
            if (!string.IsNullOrEmpty(traitId) && actor.hasTrait(traitId))
            {
                return true;
            }
        }
        return false;
    }

    public static bool ApplyPunishment(LawEnforcementContext context, PunishmentLevel punishment)
    {
        switch (punishment)
        {
            case PunishmentLevel.无罪:
                return false;
            case PunishmentLevel.罚金:
                return ApplyFine(context.Actor);
            case PunishmentLevel.笞刑:
                return ApplyCaning(context.Actor, 60);
            case PunishmentLevel.杖刑:
                return ApplyCaning(context.Actor, 20);
            case PunishmentLevel.监禁:
                return ApplyImprisonment(context.Actor);
            case PunishmentLevel.流放:
                return ApplyExile(context);
            case PunishmentLevel.没收财产:
                return ApplyConfiscateProperty(context.Actor);
            case PunishmentLevel.剥夺爵位:
                return ApplyStripPeerage(context.Actor);
            case PunishmentLevel.剥夺官职:
                return ApplyStripOffice(context);
            case PunishmentLevel.死刑:
            case PunishmentLevel.夷三族:
                return ApplyDeathPenalty(context.Actor);
            default:
                return false;
        }
    }

    private static bool ApplyFine(Actor actor)
    {
        if (actor == null || actor.isRekt()) return false;
        actor.addMoney(-50);
        return true;
    }

    private static bool ApplyCaning(Actor actor, int remainingHealth)
    {
        if (actor == null || actor.isRekt()) return false;
        if (remainingHealth < 0)
        {
            remainingHealth = 0;
        }
        actor.setHealth(remainingHealth);
        return true;
    }

    private static bool ApplyImprisonment(Actor actor)
    {
        if (actor == null || actor.isRekt()) return false;
        actor.addTrait("death_mark");
        actor.ChangeDeathRate(0.08f);
        return true;
    }

    private static bool ApplyExile(LawEnforcementContext context)
    {
        Actor actor = context != null ? context.Actor : null;
        if (actor == null || actor.isRekt()) return false;

        Kingdom kingdom = context != null ? context.Kingdom : actor.kingdom;
        ApplyStripOffice(new LawEnforcementContext
        {
            Actor = actor,
            Kingdom = kingdom,
            Law = new Law()
        });
        actor.RemoveFaction();

        City exileCity = kingdom != null ? kingdom.FindExileCity() : null;
        if (exileCity == null || exileCity.isRekt())
        {
            return false;
        }

        actor.joinCity(exileCity);
        return true;
    }

    private static bool ApplyConfiscateProperty(Actor actor)
    {
        if (actor == null || actor.isRekt()) return false;
        actor.addMoney(-actor.money);
        return true;
    }

    private static bool ApplyStripPeerage(Actor actor)
    {
        if (actor == null || actor.isRekt()) return false;

        bool changed = false;
        if (actor.GetPeeragesLevel() != PeeragesLevel.peerages_6)
        {
            actor.SetPeeragesLevel(PeeragesLevel.peerages_6);
            changed = true;
        }

        if (actor.HasTitle())
        {
            actor.ClearTitle();
            changed = true;
        }

        return changed;
    }

    private static bool ApplyStripOffice(LawEnforcementContext context)
    {
        Actor actor = context != null ? context.Actor : null;
        Kingdom kingdom = context != null ? context.Kingdom : null;
        if (actor == null || actor.isRekt()) return false;

        bool changed = false;
        OfficeObject actorOffice = actor.GetOffice();
        if (actorOffice != null)
        {
            actorOffice.RemoveActor();
            changed = true;
        }

        if (actor.HasOfficeIdentity() && actor.GetIdentity() != null && actor.GetIdentity().HasOffice())
        {
            actor.GetIdentity().RemoveOffice();
            changed = true;
        }

        OfficeObject localOffice = kingdom != null ? kingdom.GetOffice() : null;
        if (localOffice != null && localOffice.actor_id == actor.getID())
        {
            localOffice.RemoveActor();
            changed = true;
        }

        if (context != null && context.Law != null && context.Law.AffectDescendants && kingdom != null)
        {
            actor.BanFromOffice(kingdom.GetEmpireID());
            changed = true;
        }

        return changed;
    }

    private static bool ApplyDeathPenalty(Actor actor)
    {
        if (actor == null || actor.isRekt()) return false;
        actor.addTrait("death_mark");
        foreach (var c in actor.getChildren())
        {
            c.addTrait("death_mark");
        }
        actor.lover?.addTrait("death_mark");
        actor.ChangeDeathRate(1f);
        return true;
    }
}


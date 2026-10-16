using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 名将名臣：功业成名的少数人物，保留时代的活力(第 1 期：成名判定、特质、称号、数量上限、名册)。
//   · 候选：名将靠战功(战线上统领、战争统帅打赢战役：守城、克城、以弱胜强)，名臣靠政绩(中央官员每年按官位、
//     治事智力、政绩增量攒)。功业攒在名册的候选条目里(rank = None)，不立功的候选逐年衰减，攒到门槛才可能成名；
//   · 成名：功业到良才/名/国柱三档(300 / 900 / 1800)才可能，另有门槛(兵略/治事)、数量上限(每个势力同时在世
//     名将名臣各 2 人)、同势力两次成名至少隔 12 年，再按时势掷一次——战时、开国、中兴期名将名臣易出，
//     治世的名将、末世昏君手下的名臣难出；成名是一次性事件，记入史册与世界日志，给档位特质；
//   · 特质：talentGeneral/Minister + Fine/Famous/Pillar，一人只带一条，晋档时换掉旧的(数值见 EmpireCraftActorTraitLibrary)；
//   · 称号：按成名前最近一次的功业与地点拼(守某城名将、克某城名将、理政名臣……)，只存本地化键与参数；
//   · 名册：全世界一份，在世与已故都在里面，已故的留作青史；势力信息在世时每年刷新，去世时定格成快照；
//   · 效果：本期只有特质自带的属性(含兵略/治事，战时统帅按兵略给全军加成，见 WarCommandSystem)；
//     军团火力、积弊、腐败、政令、内阁、功高震主等留给后续各期。
public static class FamousTalentSystem
{
    #region 常量(调平衡改这里)

    public const string GeneralFine = "talentGeneralFine", GeneralFamous = "talentGeneralFamous",
        GeneralPillar = "talentGeneralPillar", MinisterFine = "talentMinisterFine",
        MinisterFamous = "talentMinisterFamous", MinisterPillar = "talentMinisterPillar";

    private static readonly string[] AllTraits =
        { GeneralFine, GeneralFamous, GeneralPillar, MinisterFine, MinisterFamous, MinisterPillar };

    // 三档所需功业
    private static readonly float[] RankFame = { 300f, 900f, 1800f };
    private const int MaxPerRealmKind = 2;
    private const int FoundingCooldownYears = 12;
    private const float FoundChance = 0.5f;
    // 成名的人本事得跟得上：名将兵略、名臣治事
    private const int GeneralGate = 5, MinisterGate = 5;
    // 候选不立功每年留这么多
    private const float CandidateDecay = 0.9f;
    private const float CandidateFloor = 5f;
    // 候选功业超过这个数就不再因为另一行的功改行
    private const float SwitchKindFame = 20f;

    // 战功：打赢一场(按月)、克城、守城；以弱胜强按兵力比加成，最多 2.5 倍
    private const float WinFame = 8f, CaptureFame = 30f, DefendFame = 6f, UnderdogMax = 2.5f;
    // 战争统帅(不是军团统领)分到的比例
    private const float CommanderShare = 0.5f;
    // 名臣每年：内阁 / 其他中央官位，治事、智力超过 8 的部分，政绩增量
    private const float CabinetFame = 12f, OfficeFame = 6f, StewardFame = 1.5f, ScholarFame = 0.5f,
        PerformanceFame = 0.5f, PerformanceCap = 80f;

    #endregion

    #region 名册

    private static readonly List<TalentRecord> Records = new();
    private static readonly Dictionary<long, TalentRecord> ByActor = new();
    private static readonly Dictionary<string, double> LastFounded = new();
    private static double _lastScan = -1d;

    public static void ResetWorldState()
    {
        Records.Clear();
        ByActor.Clear();
        LastFounded.Clear();
        _lastScan = -1d;
    }

    public static void Import(TalentSaveData data)
    {
        ResetWorldState();
        if (data == null) return;
        foreach (TalentRecord record in data.records ?? new List<TalentRecord>())
        {
            if (record == null || record.actor_id < 0 || ByActor.ContainsKey(record.actor_id)) continue;
            Records.Add(record);
            ByActor[record.actor_id] = record;
        }
        if (data.last_founded != null)
            foreach (var pair in data.last_founded) LastFounded[pair.Key] = pair.Value;
    }

    public static TalentSaveData Export() => new()
    {
        records = Records.ToList(),
        last_founded = new Dictionary<string, double>(LastFounded)
    };

    public static IEnumerable<TalentRecord> Alive() =>
        Records.Where(record => record.alive && record.rank >= TalentRank.Fine);

    public static IEnumerable<TalentRecord> Fallen() =>
        Records.Where(record => !record.alive && record.rank >= TalentRank.Fine);

    public static TalentRecord Of(Actor actor) =>
        actor?.data != null && ByActor.TryGetValue(actor.id, out TalentRecord record) && record.rank >= TalentRank.Fine
            ? record : null;

    public static bool IsFamous(Actor actor) => Of(actor)?.alive == true;

    public static Actor ActorOf(TalentRecord record)
    {
        if (record == null || !record.alive || World.world == null) return null;
        Actor actor = World.world.units.get(record.actor_id);
        return actor?.data != null && !actor.isRekt() && actor.isAlive() ? actor : null;
    }

    #endregion

    #region 特质

    public static string TraitOf(TalentKind kind, TalentRank rank)
    {
        int tier = Mathf.Clamp((int)rank, 0, 2);
        return AllTraits[(kind == TalentKind.General ? 0 : 3) + tier];
    }

    public static IReadOnlyList<string> Traits => AllTraits;

    private static void ApplyTrait(Actor actor, TalentRecord record)
    {
        string wanted = TraitOf(record.kind, record.rank);
        foreach (string trait in AllTraits)
        {
            if (trait != wanted && actor.hasTrait(trait)) actor.removeTrait(trait);
        }
        if (!actor.hasTrait(wanted)) actor.addTrait(wanted);
    }

    #endregion

    #region 立功

    private static bool IsRealActor(Actor actor) =>
        actor?.data != null && !actor.isRekt() && actor.isAlive() && actor.isAdult();

    private static TalentRecord Track(Actor actor, TalentKind kind)
    {
        if (!ByActor.TryGetValue(actor.id, out TalentRecord record))
        {
            record = new TalentRecord { actor_id = actor.id, kind = kind, name = actor.getName() };
            Records.Add(record);
            ByActor[actor.id] = record;
        }
        else if (record.kind != kind)
        {
            // 已经成名的、或功业已有些积累的不改行(带兵的人兼着中央官职，不能每年来回清零)；
            // 刚冒头的候选才改行，跟着新立的功走
            if (record.rank >= TalentRank.Fine || record.fame >= SwitchKindFame) return null;
            record.kind = kind;
            record.fame = 0f;
            record.performance_seen = -1d;
        }
        return record;
    }

    private static void Credit(Actor actor, TalentKind kind, float amount, string deed, string deedArg)
    {
        if (amount <= 0f || !IsRealActor(actor)) return;
        TalentRecord record = Track(actor, kind);
        if (record == null || !record.alive) return;
        record.fame += amount;
        record.credited_at = World.world.getCurWorldTime();
        if (!string.IsNullOrEmpty(deed))
        {
            record.deed = deed;
            record.deed_arg = deedArg ?? "";
        }
        RefreshRealm(record, actor);
    }

    // 战线上一场仗打完(FrontSystem.ResolveCity 调用)：赢的一方的军团统领、战争统帅记战功
    public static void OnBattle(IEnumerable<(Kingdom kingdom, CorpsData corps)> winners, string cityName,
        float winnerStrength, float loserStrength, bool captured, bool defended)
    {
        if (winners == null) return;
        float underdog = Mathf.Clamp(loserStrength / Mathf.Max(1f, winnerStrength), 1f, UnderdogMax);
        float fame = (defended ? DefendFame : WinFame) * underdog + (captured ? CaptureFame : 0f);
        string deed = captured ? "capture" : defended ? "defend" : underdog > 1.3f ? "underdog" : "campaign";
        var commanders = new HashSet<long>();
        foreach ((Kingdom kingdom, CorpsData corps) in winners)
        {
            if (corps == null || corps.militia) continue;
            if (corps.general_id >= 0)
                Credit(World.world.units.get(corps.general_id), TalentKind.General, fame, deed, cityName);
            Actor commander = WarCommandSystem.Commander(kingdom);
            if (commander != null && commanders.Add(commander.id))
                Credit(commander, TalentKind.General, fame * CommanderShare, deed, cityName);
        }
    }

    #endregion

    #region 势力

    private static void RefreshRealm(TalentRecord record, Actor actor)
    {
        Kingdom kingdom = actor?.kingdom;
        if (kingdom?.data == null || kingdom.isRekt()) return;
        Empire empire = kingdom.IsInEmpire() ? kingdom.GetEmpire() : null;
        record.name = actor.getName();
        record.kingdom_id = kingdom.id;
        record.empire_id = empire?.id ?? -1L;
        record.realm_name = empire != null ? empire.GetEmpireFullName() : kingdom.GetKingdomFullName();
        ColorAsset color = empire?.getColor() ?? kingdom.getColor();
        if (color != null)
        {
            record.realm_color = "#" + ColorUtility.ToHtmlStringRGB(color.getColorMain());
            record.realm_text_color = "#" + ColorUtility.ToHtmlStringRGB(color._color_text);
        }
        record.post = PostOf(actor, kingdom);
    }

    // 官职/职务：统领写军名，战争统帅写统帅官名，名臣取在任官职
    private static string PostOf(Actor actor, Kingdom kingdom)
    {
        if (CorpsSystem.IsGeneral(actor))
        {
            foreach ((Kingdom _, CorpsData corps) in CorpsSystem.All())
                if (corps.general_id == actor.id) return string.Format(LM.Get("talent_post_general"), CorpsSystem.Name(corps));
        }
        if (WarCommandSystem.Commander(kingdom) == actor)
        {
            string title = WarCommandSystem.TitleOf(kingdom);
            if (!string.IsNullOrEmpty(title)) return title;
        }
        string office = actor.GetPersonalIdentity()?.officeName;
        return string.IsNullOrWhiteSpace(office) ? "" : office;
    }

    private static string RealmKey(TalentRecord record, TalentKind kind) =>
        (record.empire_id >= 0 ? "e" + record.empire_id : "k" + record.kingdom_id) + "/" + (int)kind;

    #endregion

    #region 每年

    // 每年一次(CityPatch 的全世界年度扫描挂着)：名臣记政绩、候选衰减、成名与晋档、清理
    public static void TryYearlyScan()
    {
        if (World.world == null || ModClass.EMPIRE_MANAGER == null) return;
        double now = World.world.getCurWorldTime();
        if (_lastScan >= 0d && now >= _lastScan && Date.getYearsSince(_lastScan) < 1) return;
        _lastScan = now;
        try
        {
            ScanMinisters();
            Assess();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][名将名臣] 年度结算失败: {exception.Message}");
        }
    }

    // 名臣候选：各帝国中央官员(中枢、各部；不含后宫、地方官)
    private static void ScanMinisters()
    {
        foreach (Empire empire in ModClass.EMPIRE_MANAGER.ToList())
        {
            if (empire?.data == null || empire.isRekt() || empire.IsArchived()) continue;
            CenterOffice center = empire.data.centerOffice;
            if (center == null) continue;
            foreach (long officeId in center.CoreOffices.Concat(center.Divisions).ToList())
            {
                if (!OfficeManager.Offices.TryGetValue(officeId, out OfficeObject office) || office == null) continue;
                Actor actor = office.GetActor();
                if (!IsRealActor(actor) || actor.isKing()) continue;
                CreditMinister(actor);
            }
        }
    }

    private static void CreditMinister(Actor actor)
    {
        bool tracked = ByActor.ContainsKey(actor.id);
        if (!tracked && actor.stewardship < MinisterGate) return;
        OfficeIdentity identity = actor.GetIdentity();
        bool cabinet = identity?.IsCabinet() == true;
        float gain = (cabinet ? CabinetFame : OfficeFame) + Mathf.Max(0f, actor.stewardship - 8f) * StewardFame +
                     Mathf.Max(0f, actor.intelligence - 8f) * ScholarFame;
        string deed = cabinet ? "cabinet"
            : actor.diplomacy > actor.stewardship && actor.diplomacy >= actor.intelligence ? "envoy"
            : actor.intelligence > actor.stewardship ? "scholar" : "steward";
        Credit(actor, TalentKind.Minister, gain, deed, "");
        if (!ByActor.TryGetValue(actor.id, out TalentRecord record) || record.kind != TalentKind.Minister) return;
        double performance = identity?.TotalPerformance ?? 0d;
        if (record.performance_seen >= 0d)
            record.fame += Mathf.Clamp((float)(performance - record.performance_seen), 0f, PerformanceCap) * PerformanceFame;
        record.performance_seen = performance;
    }

    private static void Assess()
    {
        foreach (TalentRecord record in Records.ToList())
        {
            if (!record.alive) continue;
            Actor actor = World.world.units.get(record.actor_id);
            if (actor?.data == null || actor.isRekt() || !actor.isAlive())
            {
                // 实体没了而不是死了(统领解散回人口、被替换之类)：候选直接清掉，已成名的记成"亡故"
                if (record.rank < TalentRank.Fine) Remove(record);
                else Fall(record, null, TalentFall.Other);
                continue;
            }
            RefreshRealm(record, actor);
            if (record.rank < TalentRank.Fine && (record.credited_at < 0d || Date.getYearsSince(record.credited_at) >= 1))
            {
                record.fame *= CandidateDecay;
                if (record.fame < CandidateFloor)
                {
                    Remove(record);
                    continue;
                }
            }
            Advance(record, actor);
        }
    }

    private static TalentRank RankFor(float fame)
    {
        TalentRank rank = TalentRank.None;
        for (int i = 0; i < RankFame.Length; i++)
            if (fame >= RankFame[i]) rank = (TalentRank)i;
        return rank;
    }

    private static void Advance(TalentRecord record, Actor actor)
    {
        TalentRank target = RankFor(record.fame);
        if (target == TalentRank.None) return;
        if (record.rank >= TalentRank.Fine)
        {
            if (target > record.rank) Promote(record, actor, target);
            else ApplyTrait(actor, record);
            return;
        }
        if (record.kind == TalentKind.General ? actor.warfare < GeneralGate : actor.stewardship < MinisterGate) return;
        string key = RealmKey(record, record.kind);
        if (Alive().Count(other => other != record && other.kind == record.kind && RealmKey(other, other.kind) == key) >=
            MaxPerRealmKind) return;
        if (LastFounded.TryGetValue(key, out double last) && Date.getYearsSince(last) < FoundingCooldownYears) return;
        if (UnityEngine.Random.value > FoundChance * Season(actor, record.kind)) return;
        Found(record, actor, target, key);
    }

    // 时势：战时名将易出、治世少；开国、中兴期都旺；末世(积弊重)逼出贤臣，昏君手下的贤臣被埋没
    private static float Season(Actor actor, TalentKind kind)
    {
        Kingdom kingdom = actor.kingdom;
        Empire empire = kingdom != null && kingdom.IsInEmpire() ? kingdom.GetEmpire() : null;
        float season = 1f;
        if (kind == TalentKind.General) season *= kingdom != null && kingdom.hasEnemies() ? 1.6f : 0.6f;
        if (empire != null && !empire.isRekt() && !empire.IsArchived())
        {
            if (empire.InFoundingGrace) season *= 1.3f;
            if (kind == TalentKind.Minister)
            {
                if (DynasticCycleSystem.GetPressure(empire) >= 60f) season *= 1.5f;
                if (RulerQualitySystem.Of(empire.Emperor) is RulerQualitySystem.Grade.Fatuous or RulerQualitySystem.Grade.Tyrant)
                    season *= 0.7f;
            }
        }
        return season;
    }

    #endregion

    #region 成名与晋档

    private static void Found(TalentRecord record, Actor actor, TalentRank rank, string realmKey)
    {
        record.rank = rank;
        record.founded_at = World.world.getCurWorldTime();
        LastFounded[realmKey] = record.founded_at;
        SetTitle(record);
        ApplyTrait(actor, record);
        Announce(record, actor, "talent_founded_history");
    }

    private static void Promote(TalentRecord record, Actor actor, TalentRank rank)
    {
        record.rank = rank;
        ApplyTrait(actor, record);
        Announce(record, actor, "talent_promoted_history");
    }

    // 神力册封：不看功业门槛、数量上限、冷却与时势，直接成名；已成名的再封一次晋一档(国柱封顶)
    public static bool Bestow(Actor actor, TalentKind kind)
    {
        if (!IsRealActor(actor)) return false;
        TalentRecord record = Track(actor, kind);
        if (record == null || !record.alive) return false;
        TalentRank next = record.rank >= TalentRank.Fine ? (TalentRank)Math.Min((int)record.rank + 1, 2) : TalentRank.Fine;
        if (next == record.rank) return false;
        record.fame = Mathf.Max(record.fame, RankFame[(int)next]);
        RefreshRealm(record, actor);
        if (record.rank < TalentRank.Fine) Found(record, actor, next, RealmKey(record, record.kind));
        else Promote(record, actor, next);
        return true;
    }

    private static void SetTitle(TalentRecord record)
    {
        string deed = string.IsNullOrEmpty(record.deed)
            ? record.kind == TalentKind.General ? "campaign" : "steward"
            : record.deed;
        record.title_key = (record.kind == TalentKind.General ? "talent_title_general_" : "talent_title_minister_") + deed;
        record.title_arg = record.deed_arg ?? "";
    }

    private static void Announce(TalentRecord record, Actor actor, string key)
    {
        string content = string.Format(LM.Get(key), record.name, TitleText(record), RankName(record.rank), record.realm_name);
        TranslateHelper.LogEventMessage(content, actor.kingdom);
    }

    #endregion

    #region 陨落

    public static void OnActorDied(Actor actor, AttackType type)
    {
        if (actor?.data == null || !ByActor.TryGetValue(actor.id, out TalentRecord record) || !record.alive) return;
        if (record.rank < TalentRank.Fine)
        {
            Remove(record);
            return;
        }
        RefreshRealm(record, actor);
        TalentFall fall = type switch
        {
            AttackType.Age => TalentFall.Age,
            AttackType.Weapon or AttackType.Explosion => TalentFall.Battle,
            _ => TalentFall.Illness
        };
        Fall(record, actor, fall);
    }

    private static void Fall(TalentRecord record, Actor actor, TalentFall fall)
    {
        record.alive = false;
        record.fall = fall;
        record.died_at = World.world.getCurWorldTime();
        record.died_age = actor?.data != null ? Mathf.Max(0, (int)Math.Round((double)actor.getAge())) : record.died_age;
    }

    private static void Remove(TalentRecord record)
    {
        Records.Remove(record);
        ByActor.Remove(record.actor_id);
    }

    #endregion

    #region 显示

    public static string KindName(TalentKind kind) =>
        LM.Get(kind == TalentKind.General ? "talent_kind_general" : "talent_kind_minister");

    public static string RankName(TalentRank rank) => LM.Get(rank switch
    {
        TalentRank.Pillar => "talent_rank_pillar",
        TalentRank.Famous => "talent_rank_famous",
        _ => "talent_rank_fine"
    });

    public static string FallName(TalentFall fall) => LM.Get(fall switch
    {
        TalentFall.Battle => "talent_fall_battle",
        TalentFall.Age => "talent_fall_age",
        TalentFall.Executed => "talent_fall_executed",
        TalentFall.Illness => "talent_fall_illness",
        _ => "talent_fall_other"
    });

    public static string TitleText(TalentRecord record) =>
        string.IsNullOrEmpty(record?.title_key) ? "" : string.Format(LM.Get(record.title_key), record.title_arg);

    public static string RankHex(TalentRank rank) => rank switch
    {
        TalentRank.Pillar => "#F3C34A",
        TalentRank.Famous => "#7FD8EA",
        _ => "#8FA0A8"
    };

    public static string KindHex(TalentKind kind) => kind == TalentKind.General ? "#E9A85B" : "#7FD8EA";

    // 在世者现取官职(统领、统帅、官位都会变)，没取到用快照
    public static string PostText(TalentRecord record)
    {
        Actor actor = ActorOf(record);
        if (actor?.kingdom != null)
        {
            string live = PostOf(actor, actor.kingdom);
            if (!string.IsNullOrEmpty(live)) return live;
        }
        return record.post ?? "";
    }

    // 特质本身带的属性，一行说清(数值见 EmpireCraftActorTraitLibrary 里的登记)
    public static string EffectLine(TalentRecord record)
    {
        ActorTrait trait = AssetManager.traits?.get(TraitOf(record.kind, record.rank));
        if (trait?.base_stats == null || !trait.base_stats.hasStats()) return "";
        var parts = new List<string>();
        foreach (string stat in record.kind == TalentKind.General
                     ? new[] { "warfare", "damage", "health", "armor" }
                     : new[] { "stewardship", "intelligence", "diplomacy" })
        {
            if (trait.base_stats.hasStat(stat))
                parts.Add($"{LM.Get("talent_stat_" + stat)} +{trait.base_stats.get(stat):0.#}");
        }
        return string.Join(" · ", parts);
    }

    #endregion
}

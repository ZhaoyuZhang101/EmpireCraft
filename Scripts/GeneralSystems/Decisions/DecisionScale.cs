using System;
using System.Collections.Generic;

namespace EmpireCraft.Scripts.GeneralSystems.Decisions;

// 发起天平(用户 2026-11-14 "要不要发起决议也走博弈""我是说天平"，并要"放到别的核"算)：
// 一个人想不想办某项决议，左盘是推力、右盘是阻力，砝码都是倍数，推力 ÷ 阻力 ≥ Threshold 才列进候选，候选按读数从高到低排。
// 主线程验过条件不合的项，UnfitMonths 个月里这个人不再拿它上秤。
//   推力：才干、资望超出门槛、时局(战时的战事类、太平时的名分外交类)、威望(名分外交朝争类)、一时之念(按人按月定的小起伏)
//   阻力：兵事缠身(非战事类)、用度(花费比可用的钱)、国中多事(本国在办几件)、新近办过(同项三年、同类一年)、人心不稳(内政文化名分类)
// 这里只有算术，不碰游戏对象：主线程每月取一份快照(Snapshot、DefInfo，见 DecisionSystem.Plan)，在别的核上逐人逐项过秤，
// 过秤后的候选交回主线程，主线程只对排在前头的几项跑决议自己的条件(读活的世界)再发起。
public enum DecisionKind
{
    War, Title, Diplomacy, Domestic, Culture, Court
}

public sealed class ScaleWeight
{
    public string key;
    public float factor;
    // true = 推力，false = 阻力
    public bool push;

    public ScaleWeight()
    {
    }

    public ScaleWeight(string key, float factor, bool push)
    {
        this.key = key;
        this.factor = factor;
        this.push = push;
    }
}

public static class DecisionScale
{
    public const float Threshold = 1f;
    public const int Shortlist = 8;
    // 主线程验过条件不合的，这么多个月里不再上秤(条件读活的世界，过秤时看不到；记下来免得每回都排在前头又落空)
    public const int UnfitMonths = 12;

    // 决议的类别(模组自己的分法)；没列的算内政
    private static readonly Dictionary<string, DecisionKind> Kinds = new()
    {
        ["war_forage"] = DecisionKind.War, ["member_request_aid"] = DecisionKind.War, ["member_aid_neighbour"] = DecisionKind.War,
        ["member_aid_center"] = DecisionKind.War, ["force_stop_war"] = DecisionKind.War, ["feudal_independence_war"] = DecisionKind.War,
        ["empirecraft_war"] = DecisionKind.War, ["kingdom_start_religion_war"] = DecisionKind.War, ["rebellion"] = DecisionKind.War,
        ["become_empire"] = DecisionKind.Title, ["usurp_imperial_legitimacy"] = DecisionKind.Title, ["new_empire_royal"] = DecisionKind.Title,
        ["kingdom_get_title"] = DecisionKind.Title, ["king_acquire_title"] = DecisionKind.Title, ["kingdom_create_title"] = DecisionKind.Title,
        ["kingdom_destroy_title"] = DecisionKind.Title, ["kingdom_petition_title"] = DecisionKind.Title,
        ["empire_take_back_title"] = DecisionKind.Title, ["kingdom_change_capital_title"] = DecisionKind.Title,
        ["kingdom_add_city_into_title"] = DecisionKind.Title, ["combine_kingdom"] = DecisionKind.Title,
        ["feudal_offer_vassalage"] = DecisionKind.Diplomacy, ["feudal_tighten_vassalage"] = DecisionKind.Diplomacy,
        ["feudal_annex_vassal"] = DecisionKind.Diplomacy, ["kingdom_join_empire"] = DecisionKind.Diplomacy,
        ["kingdom_start_join_taken_alliance"] = DecisionKind.Diplomacy, ["alliance_join"] = DecisionKind.Diplomacy,
        ["empirecraft_road_treaty"] = DecisionKind.Diplomacy,
        ["empirecraft_city_culture_shift"] = DecisionKind.Culture, ["empirecraft_independent_title_culture_conversion"] = DecisionKind.Culture,
        ["empirecraft_restore_native_culture"] = DecisionKind.Culture, ["empirecraft_cultural_assimilation_duty"] = DecisionKind.Culture,
        ["adopt_central_plains_institutions"] = DecisionKind.Culture, ["kingdom_culture_reform"] = DecisionKind.Culture,
        ["rebuild_city_style"] = DecisionKind.Culture,
        ["kingdom_start_invite_to_faction"] = DecisionKind.Court, ["faction_leader_influence_local_kingdom"] = DecisionKind.Court,
        ["kingdom_expose_crime"] = DecisionKind.Court, ["empire_plots"] = DecisionKind.Court,
        ["empress_dowager_install_son"] = DecisionKind.Court, ["minister_acquire_empire"] = DecisionKind.Court,
        ["minister_acquire_title"] = DecisionKind.Court, ["minister_receive_nine_bestowments"] = DecisionKind.Court
    };

    public static DecisionKind KindOf(string id) => id != null && Kinds.TryGetValue(id, out DecisionKind kind) ? kind : DecisionKind.Domestic;

    // 决议的门槛与花费(主线程接手时抄一份，别的核只读)
    public sealed class DefInfo
    {
        public int index;
        public string id;
        public DecisionKind kind;
        public int minStanding, minRenown, minRealmRenown, minIntelligence, minDiplomacy, minWarfare, minStewardship, cost;
        public bool byKing, byLeader, byClan, lawsAllow;
    }

    // 一个人此刻的情形(主线程取)
    public sealed class Snapshot
    {
        public long actor, realm;
        public bool king, leader;
        public int standing, renown, realmRenown, intelligence, diplomacy, warfare, stewardship, talent;
        public int money, treasury, wars, busy;
        // 0~1：帝国看天命(正统)，王国看君权
        public float authority;
        public int month;
        // 本国各项决议上回办成的世界时刻(按 DefInfo.index；没办过为负)，同类的按 DecisionKind
        public double now;
        public double[] lastByDef, lastByKind;
        // 这个人各项决议上回验条件不合的世界时刻(按 DefInfo.index；没有为空)
        public double[] unfitByDef;
        public double monthSeconds;
    }

    public sealed class Option
    {
        public int def;
        public float ratio;
        public List<ScaleWeight> weights;
    }

    // 一个人过秤：门槛(都是数字)过了的逐项称，读数够的留下，从高到低至多 Shortlist 项
    // all：亲自掌着的君主看"可议之事"——门槛过了的都列出来(读数不够 1 的也列)，不截断
    public static List<Option> Weigh(Snapshot s, IReadOnlyList<DefInfo> defs, bool all = false)
    {
        var options = new List<Option>();
        for (int i = 0; i < defs.Count; i++)
        {
            DefInfo d = defs[i];
            if (!Admits(s, d) || Within(s, s.unfitByDef, d.index, UnfitMonths)) continue;
            var weights = new List<ScaleWeight>(8);
            float ratio = Measure(s, d, weights);
            if (!all && ratio < Threshold) continue;
            options.Add(new Option { def = d.index, ratio = ratio, weights = weights });
        }
        options.Sort((a, b) => b.ratio.CompareTo(a.ratio));
        if (!all && options.Count > Shortlist) options.RemoveRange(Shortlist, options.Count - Shortlist);
        return options;
    }

    // 身份(君主、城主；族长另由 PlotAssetPatch 认的官员、派系首领在主线程再认)与数字门槛
    private static bool Admits(Snapshot s, DefInfo d)
    {
        if (!d.lawsAllow) return false;
        if (!(d.byKing && s.king || d.byLeader || d.byClan)) return false;
        if (s.standing < d.minStanding || s.renown < d.minRenown || s.realmRenown < d.minRealmRenown) return false;
        return s.intelligence >= d.minIntelligence && s.diplomacy >= d.minDiplomacy && s.warfare >= d.minWarfare &&
               s.stewardship >= d.minStewardship;
    }

    private static float Measure(Snapshot s, DefInfo d, List<ScaleWeight> weights)
    {
        float push = 1f, pull = 1f;
        void Push(string key, float factor)
        {
            if (factor <= 1.001f) return;
            push *= factor;
            weights.Add(new ScaleWeight(key, factor, true));
        }
        void Pull(string key, float factor)
        {
            if (factor <= 1.001f) return;
            pull *= factor;
            weights.Add(new ScaleWeight(key, factor, false));
        }

        bool atWar = s.wars > 0;
        Push("decision_w_talent", 1f + Clamp(s.talent, 0, 30) / 40f);
        Push("decision_w_standing", 1f + Math.Min(0.5f, 0.1f * Math.Max(0, s.standing - d.minStanding)));
        if (d.kind == DecisionKind.War && atWar) Push("decision_w_war_need", 1.8f);
        if (!atWar && d.kind is DecisionKind.Title or DecisionKind.Diplomacy) Push("decision_w_peace", 1.2f);
        if (d.kind is DecisionKind.Title or DecisionKind.Diplomacy or DecisionKind.Court) Push("decision_w_authority", 1f + 0.5f * s.authority);
        float whim = Whim(s.actor, d.index, s.month);
        if (whim >= 1f) Push("decision_w_whim", whim);
        else Pull("decision_w_whim", 1f / whim);

        if (atWar && d.kind != DecisionKind.War) Pull("decision_w_war_burden", 1f + 0.3f * Math.Min(3, s.wars));
        if (d.cost > 0)
        {
            int available = s.money + (s.king || s.leader ? s.treasury : 0);
            Pull("decision_w_cost", 1f + Math.Min(2f, d.cost / (float)Math.Max(1, available)));
        }
        if (s.busy > 0) Pull("decision_w_busy", 1f + 0.4f * Math.Min(5, s.busy));
        if (Within(s, s.lastByDef, d.index, 36)) Pull("decision_w_recent_same", 2.5f);
        else if (Within(s, s.lastByKind, (int)d.kind, 12)) Pull("decision_w_recent_kind", 1.5f);
        if (s.authority < 0.4f && d.kind is DecisionKind.Domestic or DecisionKind.Culture or DecisionKind.Title)
            Pull("decision_w_unrest", 1f + (0.4f - s.authority));
        return push / pull;
    }

    private static bool Within(Snapshot s, double[] last, int index, int months) =>
        last != null && index >= 0 && index < last.Length && last[index] >= 0d && s.now - last[index] < months * s.monthSeconds;

    // 一时之念：按人、按项、按月定下的 0.85~1.15，同一个月里同一人对同一项总是同一个数(不用随机数，别的核上算得一样)
    private static float Whim(long actor, int def, int month)
    {
        ulong h = (ulong)actor * 0x9E3779B97F4A7C15UL ^ (ulong)(def + 1) * 0xC2B2AE3D27D4EB4FUL ^ (ulong)(month + 7) * 0x165667B19E3779F9UL;
        h ^= h >> 29;
        h *= 0xBF58476D1CE4E5B9UL;
        h ^= h >> 32;
        return 0.85f + 0.3f * (h % 1000UL) / 1000f;
    }

    private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
}

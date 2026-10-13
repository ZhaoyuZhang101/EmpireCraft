using System.Collections.Generic;
using EmpireCraft.Scripts.AI.ActorAI;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 战时临时职位：一国(帝国按中央算)谋划开战或卷入战争时拜统帅与粮草官，太平(不再交战、也不再谋划开战)时罢去——
//   · 统帅：挑兵略最好的人(在任官员、宗室优先；君主兵略最好时御驾亲征)。本国各军攻守兵力按其兵略加成
//     (兵略每点 +0.8%，最多 +20%，见 FrontSystem.ResolveCity)；就地征粮由统帅发起(见 LogisticsSystem)；
//   · 粮草官：挑治事最好的人(不选君主与统帅)。补给按其治事加成(每点 +1%，最多 +20%)，军粮按其治事省一些(每点 0.8%，最多两成)。
// 官名按时代、文化与这仗的性质定，写在 Locales 里，本文化没配的退回制度线(Huaxia、Roma、Arabic、Youmu)，再退回通用名(generic)：
//   统帅 war_commander_<范围>_<类别>：古代 attack 出征、annex 吞并之战、defend 守土、rebellion 平叛(含讨伐不臣)、
//        barbarian 征讨异族、raid 劫掠(annex、barbarian、raid 没配时按 attack)；现代 modern / modern_defend / modern_rebellion；
//   粮草官 war_quartermaster_<范围>_<ancient 或 modern>。
public static class WarCommandSystem
{
    private const float PerWarfare = 0.008f;
    private const float MaxBonus = 0.2f;
    private const float SupplyPerStewardship = 0.01f;
    private const float SavingPerStewardship = 0.008f;

    private static readonly HashSet<EmpireWarType> Rebellions = new()
    {
        EmpireWarType.派系叛乱, EmpireWarType.地方叛乱, EmpireWarType.民族叛乱, EmpireWarType.宗教叛乱,
        EmpireWarType.地方独立, EmpireWarType.附庸独立, EmpireWarType.清君侧
    };

    // 讨伐不臣：中央是攻方，同样算平叛
    private static readonly HashSet<EmpireWarType> Punitive = new() { EmpireWarType.伐不臣, EmpireWarType.不奉诏 };

    private static Kingdom Realm(Kingdom kingdom) => FrontSystem.Realm(kingdom);

    private static Actor Living(long id)
    {
        Actor actor = id >= 0 ? World.world?.units?.get(id) : null;
        return actor?.data != null && !actor.isRekt() && actor.isAlive() ? actor : null;
    }

    public static Actor Commander(Kingdom kingdom)
    {
        Kingdom realm = Realm(kingdom);
        return realm?.data == null ? null : Living(realm.GetOrCreate().war_commander_id);
    }

    public static Actor Quartermaster(Kingdom kingdom)
    {
        Kingdom realm = Realm(kingdom);
        return realm?.data == null ? null : Living(realm.GetOrCreate().war_quartermaster_id);
    }

    public static string TitleOf(Kingdom kingdom)
    {
        Kingdom realm = Realm(kingdom);
        string key = realm?.data == null ? null : realm.GetOrCreate().war_commander_title;
        return string.IsNullOrEmpty(key) ? "" : LM.Get(key);
    }

    public static string QuartermasterTitleOf(Kingdom kingdom)
    {
        Kingdom realm = Realm(kingdom);
        string key = realm?.data == null ? null : realm.GetOrCreate().war_quartermaster_title;
        return string.IsNullOrEmpty(key) ? "" : LM.Get(key);
    }

    // 统帅对本国各军兵力的加成
    public static float Factor(Kingdom kingdom)
    {
        Actor commander = Commander(kingdom);
        return commander == null ? 1f : 1f + Mathf.Min(MaxBonus, Mathf.Max(0, commander.warfare) * PerWarfare);
    }

    // 粮草官对补给的加成、对军粮的节省
    public static float QuartermasterSupply(Kingdom kingdom)
    {
        Actor quartermaster = Quartermaster(kingdom);
        return quartermaster == null ? 0f : Mathf.Min(MaxBonus, Mathf.Max(0, quartermaster.stewardship) * SupplyPerStewardship);
    }

    public static float QuartermasterSaving(Kingdom kingdom)
    {
        Actor quartermaster = Quartermaster(kingdom);
        return quartermaster == null ? 0f : Mathf.Min(MaxBonus, Mathf.Max(0, quartermaster.stewardship) * SavingPerStewardship);
    }

    // 每月(CorpsSystem 月度结算里逐国调用；开战时 WarGoalSystem 也立即调一次)：交战或谋划开战、缺人的补拜；太平了罢去
    public static void MonthTick(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt() || Realm(kingdom) != kingdom) return;
        var data = kingdom.GetOrCreate();
        bool war = kingdom.hasEnemies() || CityPopulationSystem.IsMobilizing(kingdom);
        if (!war)
        {
            if (data.war_commander_id >= 0 || data.war_quartermaster_id >= 0) Dismiss(kingdom);
            return;
        }
        Actor commander = Living(data.war_commander_id);
        if (commander == null || commander.kingdom != kingdom) Appoint(kingdom);
        Actor quartermaster = Living(data.war_quartermaster_id);
        if (quartermaster == null || quartermaster.kingdom != kingdom || quartermaster.id == data.war_commander_id)
            AppointQuartermaster(kingdom);
    }

    public static void Appoint(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt() || kingdom.units == null) return;
        Actor pick = Best(kingdom, actor => actor.warfare, -1L);
        // 君主兵略最好(或无人可用)时御驾亲征
        Actor king = kingdom.king;
        if (king?.data != null && !king.isRekt() && king.isAlive() &&
            (pick == null || king.warfare >= pick.warfare)) pick = king;
        if (pick == null) return;
        var data = kingdom.GetOrCreate();
        data.war_commander_id = pick.id;
        data.war_commander_title = TitleKey(kingdom, MainWar(kingdom));
        Announce(kingdom, pick, LM.Get(data.war_commander_title), "war_commander_appointed");
    }

    private static void AppointQuartermaster(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt() || kingdom.units == null) return;
        var data = kingdom.GetOrCreate();
        Actor pick = Best(kingdom, actor => actor.stewardship, data.war_commander_id);
        if (pick == null) return;
        data.war_quartermaster_id = pick.id;
        data.war_quartermaster_title = QuartermasterKey(kingdom);
        Announce(kingdom, pick, LM.Get(data.war_quartermaster_title), "war_quartermaster_appointed");
    }

    // 本国成年人里某项能力最好的(在任官员 +3、贵族 +2)，不选君主与 exclude
    private static Actor Best(Kingdom kingdom, global::System.Func<Actor, int> skill, long exclude)
    {
        Actor pick = null;
        float best = float.MinValue;
        foreach (Actor actor in kingdom.units)
        {
            if (actor?.data == null || actor.isRekt() || !actor.isAlive() || !actor.isAdult() || actor.isKing() ||
                actor.id == exclude || actor.asset == null || !actor.asset.civ || CityPopulationSystem.IsVehicle(actor)) continue;
            float score = skill(actor) + (actor.IsOnOffice() ? 3f : 0f) + (EmpireCaftActorJudgeClass.IsNoble(actor) ? 2f : 0f);
            if (score <= best) continue;
            best = score;
            pick = actor;
        }
        return pick;
    }

    private static void Announce(Kingdom kingdom, Actor actor, string title, string logKey)
    {
        TranslateHelper.LogEventMessage(string.Format(LM.Get(logKey), kingdom.GetKingdomName(), actor.getName(), title), kingdom);
        actor.RecordPersonalHistory(string.Format(LM.Get("personal_history_war_commander"), kingdom.GetKingdomName(), title));
    }

    private static void Dismiss(Kingdom kingdom)
    {
        var data = kingdom.GetOrCreate();
        foreach ((long id, string title) in new[] { (data.war_commander_id, data.war_commander_title),
                     (data.war_quartermaster_id, data.war_quartermaster_title) })
        {
            Actor former = Living(id);
            if (former != null && !string.IsNullOrEmpty(title))
                former.RecordPersonalHistory(string.Format(LM.Get("personal_history_war_commander_dismissed"), LM.Get(title)));
        }
        data.war_commander_id = -1L;
        data.war_commander_title = "";
        data.war_quartermaster_id = -1L;
        data.war_quartermaster_title = "";
    }

    // 这国眼下主打的那场仗(主攻或主守优先)；只在谋划开战时为 null
    private static War MainWar(Kingdom kingdom)
    {
        War fallback = null;
        foreach (War war in kingdom.getWars())
        {
            if (war?.data == null || war.hasEnded()) continue;
            if (war.main_attacker == kingdom || war.main_defender == kingdom) return war;
            fallback ??= war;
        }
        return fallback;
    }

    private static string Kind(Kingdom kingdom, War war)
    {
        bool attacking = war == null || war.isAttacker(kingdom);
        EmpireWarType type = war?.GetEmpireWarType() ?? EmpireWarType.None;
        bool rebellion = war != null && (!attacking && (war.getAsset() == WarTypeLibrary.rebellion || Rebellions.Contains(type)) ||
                                         attacking && Punitive.Contains(type));
        if (ModernStability.IsModern(kingdom))
            return rebellion ? "modern_rebellion" : attacking ? "modern" : "modern_defend";
        if (rebellion) return "rebellion";
        if (!attacking) return "defend";
        WarGoal goal = WarGoalSystem.Get(war);
        if (goal == WarGoal.劫掠) return "raid";
        Kingdom enemy = war == null ? CityPopulationSystem.EnemyOf(kingdom) : war.main_defender;
        string ours = CultureService.GetRealmCulture(kingdom), theirs = CultureService.GetRealmCulture(enemy);
        if (type == EmpireWarType.攘夷 || CultureService.IsValidCulture(ours) && CultureService.IsValidCulture(theirs) &&
            ours.GetInstitutionLine() != theirs.GetInstitutionLine()) return "barbarian";
        return goal == WarGoal.吞并 ? "annex" : "attack";
    }

    private static string TitleKey(Kingdom kingdom, War war)
    {
        string kind = Kind(kingdom, war);
        string[] kinds = kind switch
        {
            "annex" or "barbarian" or "raid" => new[] { kind, "attack" },
            "modern_defend" or "modern_rebellion" => new[] { kind, "modern" },
            _ => new[] { kind }
        };
        return Resolve(kingdom, "war_commander", kinds) ?? "war_commander_generic_attack";
    }

    private static string QuartermasterKey(Kingdom kingdom) =>
        Resolve(kingdom, "war_quartermaster", new[] { ModernStability.IsModern(kingdom) ? "modern" : "ancient" }) ??
        "war_quartermaster_generic_ancient";

    // 本文化 → 制度线 → 通用，找第一个配了的官名
    private static string Resolve(Kingdom kingdom, string prefix, string[] kinds)
    {
        string culture = CultureService.GetRealmCulture(kingdom);
        string line = CultureService.IsValidCulture(culture) ? culture.GetInstitutionLine() : "";
        foreach (string scope in new[] { culture, line, "generic" })
        {
            if (string.IsNullOrEmpty(scope)) continue;
            foreach (string candidate in kinds)
            {
                string key = $"{prefix}_{scope}_{candidate}";
                if (LM.Has(key)) return key;
            }
        }
        return null;
    }
}

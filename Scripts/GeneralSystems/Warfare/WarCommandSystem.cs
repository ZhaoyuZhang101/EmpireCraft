using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.AI.ActorAI;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 战时统帅(临时职位)：一国(帝国按中央算)谋划开战或卷入战争时，从本国挑兵略最好的人(在任官员、宗室优先；君主兵略最好时御驾亲征)
// 拜为统帅，太平(不再交战、也不再谋划开战)时罢去。官名按时代、文化与这仗的性质定，写在 Locales 的
// war_commander_<文化或制度线>_<类别> 里：
//   古代：attack 出征、annex 吞并之战、defend 守土、rebellion 平叛(含讨伐不臣)、barbarian 征讨异族、raid 劫掠；
//   现代：modern 出征、modern_defend 守土、modern_rebellion 平叛；
//   本文化没配的退回制度线(Huaxia、Roma、Arabic、Youmu)，再退回通用名(generic)；annex、barbarian、raid 没配时按 attack。
// 统帅在任时本国各军攻守的兵力按其兵略加成(兵略每点 +0.8%，最多 +20%，见 FrontSystem.ResolveCity)。
public static class WarCommandSystem
{
    private const float PerWarfare = 0.008f;
    private const float MaxBonus = 0.2f;

    private static readonly HashSet<EmpireWarType> Rebellions = new()
    {
        EmpireWarType.派系叛乱, EmpireWarType.地方叛乱, EmpireWarType.民族叛乱, EmpireWarType.宗教叛乱,
        EmpireWarType.地方独立, EmpireWarType.附庸独立, EmpireWarType.清君侧
    };

    // 讨伐不臣：中央是攻方，同样算平叛
    private static readonly HashSet<EmpireWarType> Punitive = new() { EmpireWarType.伐不臣, EmpireWarType.不奉诏 };

    private static Kingdom Realm(Kingdom kingdom) => FrontSystem.Realm(kingdom);

    public static Actor Commander(Kingdom kingdom)
    {
        Kingdom realm = Realm(kingdom);
        long id = realm?.data == null ? -1L : realm.GetOrCreate().war_commander_id;
        Actor actor = id >= 0 ? World.world?.units?.get(id) : null;
        return actor?.data != null && !actor.isRekt() && actor.isAlive() ? actor : null;
    }

    public static string TitleOf(Kingdom kingdom)
    {
        Kingdom realm = Realm(kingdom);
        string key = realm?.data == null ? null : realm.GetOrCreate().war_commander_title;
        return string.IsNullOrEmpty(key) ? "" : LM.Get(key);
    }

    // 统帅对本国各军兵力的加成
    public static float Factor(Kingdom kingdom)
    {
        Actor commander = Commander(kingdom);
        return commander == null ? 1f : 1f + Mathf.Min(MaxBonus, Mathf.Max(0, commander.warfare) * PerWarfare);
    }

    // 每月(CorpsSystem 月度结算里逐国调用)：交战或谋划开战、还没有统帅的拜一位；太平了罢去
    public static void MonthTick(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt() || Realm(kingdom) != kingdom) return;
        var data = kingdom.GetOrCreate();
        bool war = kingdom.hasEnemies() || CityPopulationSystem.IsMobilizing(kingdom);
        if (!war)
        {
            if (data.war_commander_id >= 0) Dismiss(kingdom);
            return;
        }
        Actor current = data.war_commander_id >= 0 ? World.world.units.get(data.war_commander_id) : null;
        if (current?.data != null && !current.isRekt() && current.isAlive() && current.kingdom == kingdom) return;
        Appoint(kingdom);
    }

    public static void Appoint(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt() || kingdom.units == null) return;
        Actor pick = null;
        float best = float.MinValue;
        foreach (Actor actor in kingdom.units)
        {
            if (actor?.data == null || actor.isRekt() || !actor.isAlive() || !actor.isAdult() || actor.isKing() ||
                actor.asset == null || !actor.asset.civ || CityPopulationSystem.IsVehicle(actor)) continue;
            float score = actor.warfare + (actor.IsOnOffice() ? 3f : 0f) + (EmpireCaftActorJudgeClass.IsNoble(actor) ? 2f : 0f);
            if (score <= best) continue;
            best = score;
            pick = actor;
        }
        // 君主兵略最好(或无人可用)时御驾亲征
        Actor king = kingdom.king;
        if (king?.data != null && !king.isRekt() && king.isAlive() && (pick == null || king.warfare >= best)) pick = king;
        if (pick == null) return;
        var data = kingdom.GetOrCreate();
        data.war_commander_id = pick.id;
        data.war_commander_title = TitleKey(kingdom, MainWar(kingdom));
        string title = LM.Get(data.war_commander_title);
        TranslateHelper.LogEventMessage(string.Format(LM.Get("war_commander_appointed"), kingdom.GetKingdomName(),
            pick.getName(), title), kingdom);
        pick.RecordPersonalHistory(string.Format(LM.Get("personal_history_war_commander"), kingdom.GetKingdomName(), title));
    }

    private static void Dismiss(Kingdom kingdom)
    {
        var data = kingdom.GetOrCreate();
        Actor former = World.world.units.get(data.war_commander_id);
        if (former?.data != null && !former.isRekt() && former.isAlive() && !string.IsNullOrEmpty(data.war_commander_title))
            former.RecordPersonalHistory(string.Format(LM.Get("personal_history_war_commander_dismissed"),
                LM.Get(data.war_commander_title)));
        data.war_commander_id = -1L;
        data.war_commander_title = "";
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

    // 本文化 → 制度线 → 通用；annex、barbarian、raid 没配时按 attack，现代的守土、平叛没配时按 modern
    private static string TitleKey(Kingdom kingdom, War war)
    {
        string kind = Kind(kingdom, war);
        string[] kinds = kind switch
        {
            "annex" or "barbarian" or "raid" => new[] { kind, "attack" },
            "modern_defend" or "modern_rebellion" => new[] { kind, "modern" },
            _ => new[] { kind }
        };
        string culture = CultureService.GetRealmCulture(kingdom);
        string line = CultureService.IsValidCulture(culture) ? culture.GetInstitutionLine() : "";
        foreach (string scope in new[] { culture, line, "generic" })
        {
            if (string.IsNullOrEmpty(scope)) continue;
            foreach (string candidate in kinds)
            {
                string key = $"war_commander_{scope}_{candidate}";
                if (LM.Has(key)) return key;
            }
        }
        return "war_commander_generic_attack";
    }
}

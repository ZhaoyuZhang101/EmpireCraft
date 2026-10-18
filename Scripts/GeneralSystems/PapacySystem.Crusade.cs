using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 圣战(每个宗教每年随教权结算检定一次；同一宗教同时只有一场，打完冷却 15 年)：
//   · 号召者：教廷；圣地丢了没有教廷时，由本宗教最强的国家(帝国按中央)号召。
//   · 目标(依次检定，满足一项就有一半可能号召)：
//       收复圣地——圣地被异教国家占着(教权 30 以上)；
//       援救同教——同宗教国家被异教国家攻打、已经丢了城(教权 50 以上)，讨伐攻方；
//       讨伐异端——被绝罚满两年、拒不悔罪的帝国(教权 50 以上)；
//       东征异教——教权 80 以上、号召者太平无事时，打与本宗教国家接壤的异教国家里城最多的一个。
//   · 响应：同宗教各国(帝国按中央，被绝罚的除外)按 35%、教宗加冕过 +25%、西方文化 +10%、离号召者每 20 块地 -5% 检定加入攻方，最多八国；
//     被讨伐方的同教国家(异教时)按三成检定加入守方，最多五国(护教圣战)。攻下的城改信攻方宗教(见 CityPatch)。
//   · 结局：开战目标为割地，攻方占了一半以上的城归占领者；收复圣地时圣地交给教廷(没有教廷就交给号召者)。
//     攻方胜：教权 +15，参战的帝国正统上升；攻方未胜：教权 -10，圣战失利次数 +1(宗教改革的苗头)。
public static partial class PapacySystem
{
    public const int CrusadeHolyLand = 1, CrusadeRescue = 2, CrusadeHeretic = 3, CrusadeInfidel = 4;

    private const float CrusadeChance = 0.5f;
    private const int CrusadeCooldownYears = 15;
    private const int MaxResponders = 8, MaxDefenders = 5;
    private const double SecondsPerYear = 60d;

    public static War ActiveCrusade(Religion religion)
    {
        if (religion?.data == null) return null;
        long id = religion.GetOrCreate().CrusadeWarId;
        War war = id >= 0 ? World.world.wars.get(id) : null;
        return war != null && war.isAlive() && !war.hasEnded() ? war : null;
    }

    private static void TryCrusade(Religion religion)
    {
        var data = religion.GetOrCreate();
        double now = World.world.getCurWorldTime();
        if (ActiveCrusade(religion) != null || now < data.CrusadeCooldownUntil) return;
        float authority = Authority(religion);
        Kingdom papacy = Papacy(religion);
        Kingdom caller = papacy != null && !IsCaptive(religion) ? papacy : Strongest(religion);
        if (caller == null || caller.hasEnemies() && papacy != null) return;
        (Kingdom target, int kind) = FindTarget(religion, caller, authority);
        if (target == null || target == caller || UnityEngine.Random.value >= CrusadeChance) return;
        Declare(religion, caller, target, kind);
    }

    private static (Kingdom target, int kind) FindTarget(Religion religion, Kingdom caller, float authority)
    {
        City holy = religion.GetCity();
        if (authority >= 30f && holy?.data != null && !holy.isRekt() && holy.kingdom != null && !holy.kingdom.wild &&
            holy.kingdom.religion != religion)
            return (Realm(holy.kingdom), CrusadeHolyLand);
        // 讨伐异端：宗教改革分出去的国教会
        if (authority >= 40f && HereticTarget(religion) is Kingdom heretic) return (heretic, CrusadeHeretic);
        if (authority < 50f) return (null, 0);
        foreach (War war in World.world.wars)
        {
            if (war == null || war.hasEnded()) continue;
            Kingdom attacker = war.main_attacker, defender = war.main_defender;
            if (attacker?.data == null || defender?.data == null || defender.religion != religion ||
                attacker.religion == religion || !attacker.hasReligion()) continue;
            if (WarSituation.CitiesLost(war, defender) > 0) return (Realm(attacker), CrusadeRescue);
        }
        foreach (Empire empire in ModClass.EMPIRE_MANAGER?.ToList() ?? new List<Empire>())
        {
            if (empire?.data == null || empire.IsArchived() || ReligionOf(empire) != religion || !IsExcommunicated(empire)) continue;
            if (Date.getYearsSince(State(empire).excommunicated_at) >= 2) return (empire.CoreKingdom, CrusadeHeretic);
        }
        if (authority < 80f || caller.hasEnemies()) return (null, 0);
        Kingdom best = null;
        foreach (Kingdom kingdom in World.world.kingdoms)
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild || !kingdom.hasReligion() ||
                kingdom.religion == religion || Realm(kingdom) != kingdom) continue;
            if (!religion.kingdoms.Any(member => member != null && member.IsNeighbourWith(kingdom))) continue;
            if (best == null || kingdom.cities.Count > best.cities.Count) best = kingdom;
        }
        return (best, CrusadeInfidel);
    }

    private static Kingdom Realm(Kingdom kingdom) => kingdom?.GetEmpire()?.CoreKingdom ?? kingdom;

    // 本宗教最强的国家(帝国按中央)
    private static Kingdom Strongest(Religion religion) =>
        World.world.kingdoms.Where(kingdom => kingdom?.data != null && !kingdom.isRekt() && !kingdom.wild &&
                                              kingdom.religion == religion && Realm(kingdom) == kingdom)
            .OrderByDescending(kingdom => kingdom.GetEmpire()?.countCities() ?? kingdom.cities.Count).FirstOrDefault();

    private static void Declare(Religion religion, Kingdom caller, Kingdom target, int kind)
    {
        // 远距离不开战：发起国(帝国按全体成员)与目标接壤或隔海相望才发(见 StraitReach.Within)
        if (!StraitReach.Within(caller, target)) return;
        War war = World.world.diplomacy.startWar(caller, target, WarTypeLibrary.normal);
        if (war == null) return;
        var data = religion.GetOrCreate();
        data.CrusadeCount++;
        data.CrusadeWarId = war.data.id;
        war.SetEmpireWarType(EmpireWarType.神圣, pre: religion.name);
        WarExtension.WarExtraData snapshot = war.GetOrCreate();
        snapshot.crusade_religion_id = religion.data.id;
        snapshot.crusade_kind = kind;
        snapshot.crusade_city_id = religion.GetCity()?.data?.id ?? -1L;
        snapshot.war_goal = WarGoal.割地;
        war.data.name = string.Format(LM.Get("crusade_war_name"), data.CrusadeCount, religion.name);
        string goal = kind switch
        {
            CrusadeHolyLand => string.Format(LM.Get("crusade_kind_holy"), religion.GetCity()?.GetCityName() ?? ""),
            CrusadeRescue => LM.Get("crusade_kind_rescue"),
            CrusadeHeretic => LM.Get("crusade_kind_heretic"),
            _ => LM.Get("crusade_kind_infidel")
        };
        string caller_name = Pope(religion) is Actor pope && Papacy(religion) == caller
            ? string.Format(LM.Get("crusade_caller_pope"), pope.getName())
            : caller.GetKingdomName();
        TranslateHelper.LogEventMessage(string.Format(LM.Get("crusade_called_history"), caller_name, goal,
            target.GetKingdomName(), religion.name), caller);
        RecordReligion(caller, string.Format(LM.Get("crusade_called_history"), caller_name, goal, target.GetKingdomName(),
            religion.name));
        Rally(religion, war, caller, target);
    }

    private static void Rally(Religion religion, War war, Kingdom caller, Kingdom target)
    {
        City seat = caller.capital;
        int joined = 0;
        foreach (Kingdom kingdom in World.world.kingdoms.ToList())
        {
            if (joined >= MaxResponders) break;
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild || kingdom == caller || kingdom.religion != religion ||
                Realm(kingdom) != kingdom || Realm(kingdom) == Realm(target) || war.hasKingdom(kingdom) ||
                kingdom.isInWarWith(caller)) continue;
            Empire empire = kingdom.GetEmpire();
            if (empire != null && IsExcommunicated(empire)) continue;
            float chance = 0.35f;
            if (empire != null && IsCrowned(empire) && !State(empire).self_crowned) chance += 0.25f;
            // 西方文化、阿拉伯一线(吉哈德)的集结机会多一成
            if (empire != null ? IsWestern(empire) || IsCaliphal(empire)
                    : WagesHolyWar(CultureService.GetRealmCulture(kingdom))) chance += 0.1f;
            chance += OrderHostBonus(kingdom, religion);
            if (seat != null && kingdom.capital != null)
                chance -= Vector2.Distance(kingdom.capital.city_center, seat.city_center) / 8f / 20f * 0.05f;
            if (UnityEngine.Random.value >= chance) continue;
            war.joinAttackers(kingdom);
            joined++;
            TranslateHelper.LogEventMessage(string.Format(LM.Get("crusade_join_log"), kingdom.GetKingdomName(),
                target.GetKingdomName()), kingdom);
        }
        // 护教圣战：被讨伐方的同教国家来援(异教之间才有)
        if (!target.hasReligion() || target.religion == religion) return;
        int defenders = 0;
        foreach (Kingdom kingdom in World.world.kingdoms.ToList())
        {
            if (defenders >= MaxDefenders) break;
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild || kingdom == target ||
                kingdom.religion != target.religion || Realm(kingdom) != kingdom || war.hasKingdom(kingdom) ||
                UnityEngine.Random.value >= 0.3f) continue;
            war.joinDefenders(kingdom);
            defenders++;
            TranslateHelper.LogEventMessage(string.Format(LM.Get("crusade_defend_log"), kingdom.GetKingdomName(),
                target.GetKingdomName()), kingdom);
        }
    }

    // 战争结束(WarPatch 在开战目标结算之后调用)：圣地交接、教权与正统
    public static void OnWarEnded(War war, WarWinner winner)
    {
        try
        {
            WarExtension.WarExtraData snapshot = war?.GetOrCreate();
            if (snapshot == null || snapshot.crusade_religion_id < 0) return;
            Religion religion = World.world.religions.get(snapshot.crusade_religion_id);
            if (religion?.data == null) return;
            var data = religion.GetOrCreate();
            data.CrusadeWarId = -1L;
            data.CrusadeCooldownUntil = World.world.getCurWorldTime() + CrusadeCooldownYears * SecondsPerYear;
            (int attackerGain, int defenderGain) = WarGoalSystem.Gains(war);
            // 讨伐异端打完：教随国定
            if (snapshot.crusade_kind == CrusadeHeretic) ReligiousPeace(religion);
            bool won = winner == WarWinner.Attackers || winner == WarWinner.Peace && attackerGain >= defenderGain + 1;
            Kingdom caller = war.main_attacker;
            if (won)
            {
                AddAuthority(religion, 15f);
                foreach (Kingdom kingdom in war._list_attackers.ToList())
                    if (kingdom?.data != null && !kingdom.isRekt() && kingdom.IsEmpire()) kingdom.GetEmpire()?.AddMandate(25);
                // 收复圣地：圣地交给教廷；没有教廷时由骑士团在圣地立国，没有骑士团就交给号召者
                City holy = snapshot.crusade_kind == CrusadeHolyLand ? World.world.cities.get(snapshot.crusade_city_id) : null;
                if (holy?.data != null && !holy.isRekt() && holy.kingdom?.religion != religion &&
                    !(Papacy(religion) == null && TryFoundOrderState(religion, holy)))
                {
                    Kingdom receiver = Papacy(religion) ?? (caller?.data != null && !caller.isRekt() ? caller : null);
                    if (receiver != null)
                    {
                        holy.joinAnotherKingdom(receiver);
                        holy.setReligion(religion);
                    }
                }
                // 圣战告捷，可能成立骑士团(驻在号召国)
                if (caller?.data != null && !caller.isRekt() && caller.religion == religion) TryFoundOrder(religion, caller);
                if (caller?.data != null && !caller.isRekt())
                    RecordReligion(caller, string.Format(LM.Get("crusade_won_history"), war.data.name, religion.name));
            }
            else
            {
                AddAuthority(religion, -10f);
                data.CrusadeLosses++;
                if (caller?.data != null && !caller.isRekt())
                    RecordReligion(caller, string.Format(LM.Get("crusade_lost_history"), war.data.name, religion.name));
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][圣战] 结算失败: {exception.Message}");
        }
    }

    private static void RecordReligion(Kingdom kingdom, string text)
    {
        Empire empire = kingdom?.GetEmpire();
        if (empire != null) empire.RecordHistory(directContent: text, actorId: kingdom.king?.id ?? -1L, kingdomId: kingdom.id);
    }
}

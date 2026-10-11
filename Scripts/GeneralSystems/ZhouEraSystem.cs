using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;
using Random = UnityEngine.Random;

namespace EmpireCraft.Scripts.GeneralSystems;

// 周制模拟春秋战国：诸侯可以互相征伐，但都尊奉天子、共同攘夷。
//   西周：王室强盛，诸侯不得擅自开战，天子征伐、诸侯从征(诸侯军团本就随王师作战，见 FrontSystem.Realm)；
//   春秋(王畿兵力不及最强诸侯、天命 < 60、或王畿失陷东迁)：
//     · 诸侯互相征伐兼并，打下都城即灭其国、并入自己，仍是周室诸侯；
//     · 尊王：诸侯不得攻王畿，敢犯天子者霸主与诸侯共讨；
//     · 攘夷：夷狄(他族、他文化之国)攻打任一诸侯，天子与霸主出兵来援(诸侯军团随之同上战线)；
//     · 霸主会盟：最强且尊王的诸侯受天子赐胙为霸主，诸侯每年以赋税一成纳贡，霸主可令交战诸侯罢兵、讨伐不朝之国与僭王；
//     · 卿大夫专权：大国可能被权臣瓜分(三家分晋)或篡位(田氏代齐)；子爵的夷狄大国可能僭越称王；
//   战国(发生权臣瓜分、诸侯兼并到七国以下、或天命 < 30)：
//     · 大国相继称王，霸主与会盟废止，可以攻王畿、灭周；
//     · 变法：大国推行变法，战时兵额 +25%、赋税每年多收一成，旧贵族怨望；
//     · 合纵连横：最强一国独大时，其余诸侯合纵，它攻打合纵之国则各国来援；弱小之国可能连横依附于它，彼此不攻；
//   统一：攻下王畿者取周而代之(接续为新王朝)；天子(或代周者)直辖天下三分之二以上的城即称皇帝，改行郡县律令制。
public static class ZhouEraSystem
{
    public const int WesternZhou = 0, Spring = 1, Warring = 2, Unified = 3;
    private const int ConferenceYears = 10;
    private const int LogSize = 30;

    public static bool Applies(Empire empire) =>
        empire != null && !empire.isRekt() && !empire.IsArchived() &&
        empire.CoreKingdom?.GetRegime()?.type == RegimeType.ZhouFeudalism;

    public static ZhouEraState State(Empire empire)
    {
        if (empire?.data == null) return null;
        return empire.data.zhou ??= new ZhouEraState();
    }

    public static int Era(Empire empire) => Applies(empire) ? State(empire).era : -1;

    // 春秋、战国王室衰微：天子不能再从诸侯手里收地。削藩、收回法理、按法理整合与划界、
    // 战时代管、收复旧都、绝嗣收归都不再发生；诸侯打下的地归自己(尽有其地)，王畿只靠自己的兵守土开疆
    public static bool RoyalReclaimBlocked(Empire empire) => Applies(empire) && State(empire).era >= Spring;

    public static IEnumerable<Kingdom> Vassals(Empire empire) =>
        empire?.kingdoms_list?.Where(kingdom => kingdom?.data != null && !kingdom.isRekt() &&
                                                kingdom != empire.CoreKingdom && kingdom.hasCities()) ??
        Enumerable.Empty<Kingdom>();

    public static int Power(Kingdom kingdom) =>
        kingdom?.data == null || kingdom.isRekt() ? 0 : (kingdom.cities?.Count ?? 0) * 4 + kingdom.countTotalWarriors();

    public static bool IsHegemon(Kingdom kingdom) =>
        kingdom?.GetEmpire() is Empire empire && Applies(empire) && State(empire).hegemon_id == kingdom.id;

    public static bool IsKing(Kingdom kingdom) =>
        kingdom?.GetEmpire() is Empire empire && Applies(empire) && State(empire).kings.Contains(kingdom.id);

    public static bool IsReformed(Kingdom kingdom) =>
        kingdom?.GetEmpire() is Empire empire && Applies(empire) && State(empire).reforms.Contains(kingdom.id);

    // 夷狄：不在周室之内、与天子不同族或不同文化的国家
    public static bool IsBarbarian(Empire empire, Kingdom kingdom)
    {
        Kingdom core = empire?.CoreKingdom;
        if (core == null || kingdom?.data == null || kingdom.isRekt() || kingdom.GetEmpire() == empire) return false;
        return kingdom.species_id != core.species_id ||
               CultureService.GetRealmCulture(kingdom) != CultureService.GetRealmCulture(core);
    }

    // 春秋、战国周室内部的战争：交战双方都是同一周室的成员(西周时的叛乱照旧由诸侯随天子平定)
    public static bool IsInternalWar(War war)
    {
        Kingdom attacker = war?.main_attacker, defender = war?.main_defender;
        Empire empire = attacker?.GetEmpire();
        return empire != null && Applies(empire) && State(empire).era >= Spring && defender?.GetEmpire() == empire;
    }

    // 卿族坐大：春秋时六城以上(可能被三分)、或四城以上而国君年幼年老的大国
    public static bool MinistersRising(Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        if (!Applies(empire) || State(empire).era != Spring || kingdom == empire.CoreKingdom) return false;
        int cities = kingdom.cities?.Count ?? 0;
        Actor ruler = kingdom.king;
        bool weakRuler = ruler == null || !ruler.isAdult() || ruler.getAge() > 60;
        return cities >= 6 || cities >= 4 && weakRuler;
    }

    // 战国变法：战时兵额 +25%
    public static float LevyFactor(Kingdom kingdom) => IsReformed(kingdom) ? 1.25f : 1f;

    #region 诸侯征伐(供宣战规则调用)

    // 春秋、战国：周室诸侯可以自己开战
    public static bool VassalMayWage(Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        return empire != null && kingdom != empire.CoreKingdom && Applies(empire) && State(empire).era >= Spring &&
               kingdom.GetRegime()?.IsAllowDiplomacy() == true;
    }

    // 诸侯能否向 target 开战：同为周室诸侯可以(春秋不得攻王畿)；周室之外按常规
    public static bool IsValidVassalWarTarget(Kingdom initiator, Kingdom target, out bool handled)
    {
        handled = false;
        if (!VassalMayWage(initiator) || target?.data == null || target.isRekt()) return false;
        Empire empire = initiator.GetEmpire();
        if (target.GetEmpire() != empire) return false;
        handled = true;
        if (target == initiator) return false;
        if (target == empire.CoreKingdom && State(empire).era < Warring) return false;
        if (!initiator.IsNeighbourWith(target)) return false;
        if (initiator.hasAlliance() && initiator.getAlliance() == target.getAlliance()) return false;
        // 合纵之国不互攻；连横之国与所依附之国不互攻
        ZhouEraState state = State(empire);
        if (state.coalition.Contains(initiator.id) && state.coalition.Contains(target.id)) return false;
        if (state.horizontal.Contains(initiator.id) && target.id == state.coalition_target ||
            state.horizontal.Contains(target.id) && initiator.id == state.coalition_target) return false;
        bool hostile = (World.world?.diplomacy?.getOpinion(initiator, target)?.total ?? 0) < 0;
        return hostile || Power(initiator) >= Power(target) * 1.3f;
    }

    #endregion

    #region 灭国：打下诸侯都城即灭其国

    [ThreadStatic] private static bool _annexing;

    // 城破事件先记下，下次结算时处理：不在攻城流程中途改朝换代、成批转移城市
    private static readonly List<(long city, long loser, long winner, bool royal)> Pending = new();

    public static void QueueCapitalTaken(City city, Kingdom loser, Kingdom winner, bool royal)
    {
        if (city == null || loser == null || winner == null) return;
        Pending.Add((city.id, loser.id, winner.id, royal));
    }

    private static void ProcessPending()
    {
        if (Pending.Count == 0) return;
        var events = Pending.ToList();
        Pending.Clear();
        foreach (var item in events)
        {
            City city = World.world.cities.get(item.city);
            Kingdom loser = World.world.kingdoms.get(item.loser);
            Kingdom winner = World.world.kingdoms.get(item.winner);
            if (city == null || loser == null || winner == null || city.kingdom != winner) continue;
            if (item.royal) OnRoyalCapitalTaken(city, loser, winner);
            else OnCapitalTaken(city, loser, winner);
        }
    }

    public static void OnCapitalTaken(City city, Kingdom loser, Kingdom winner)
    {
        if (_annexing || city == null || loser?.data == null || winner?.data == null || loser == winner) return;
        Empire empire = winner.GetEmpire();
        if (!Applies(empire) || loser.GetEmpire() != empire || State(empire).era < Spring) return;
        if (loser == empire.CoreKingdom) return;
        _annexing = true;
        try
        {
            string loserName = loser.GetKingdomName();
            foreach (City other in loser.cities?.ToList() ?? new List<City>())
                if (other != null && !other.isRekt() && other.kingdom == loser) other.joinAnotherKingdom(winner, true);
            ZhouEraState state = State(empire);
            state.annexed++;
            state.kings.Remove(loser.id);
            state.reforms.Remove(loser.id);
            Log(empire, string.Format(LM.Get("zhou_log_annexed"), winner.GetKingdomName(), loserName));
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][春秋战国] 灭国处理失败: {exception.Message}");
        }
        finally
        {
            _annexing = false;
        }
    }

    // 战国攻下王畿：周亡，攻者取周而代之
    public static void OnRoyalCapitalTaken(City city, Kingdom royal, Kingdom winner)
    {
        Empire empire = royal?.GetEmpire();
        if (!Applies(empire) || empire.CoreKingdom != royal || winner?.data == null || winner.GetEmpire() != empire ||
            State(empire).era < Warring) return;
        string zhou = empire.GetEmpireName();
        Log(empire, string.Format(LM.Get("zhou_log_zhou_fell"), winner.GetKingdomName(), zhou));
        ZhouEraState carried = State(empire);
        if (!empire.ReplaceEmpire(winner)) return;
        Empire successor = winner.GetEmpire();
        if (successor?.data == null) return;
        successor.data.zhou = carried;
        carried.hegemon_id = -1L;
        carried.royal_capital_id = winner.capital?.id ?? -1L;
        carried.coalition.Clear();
        carried.coalition_target = -1L;
        successor.RecordHistory(directContent: string.Format(LM.Get("zhou_history_zhou_fell"), winner.GetKingdomName(), zhou),
            kingdomId: winner.id);
    }

    #endregion

    #region 定期结算

    public static void Update(Empire empire)
    {
        if (World.world == null) return;
        try { ProcessPending(); }
        catch (Exception exception) { LogService.LogWarning($"[EmpireCraft][春秋战国] 城破处理失败: {exception.Message}"); }
        if (!Applies(empire)) return;
        ZhouEraState state = State(empire);
        double now = World.world.getCurWorldTime();
        if (state.era_since < 0d)
        {
            state.era_since = now;
            state.royal_capital_id = empire.CoreKingdom.capital?.id ?? -1L;
        }
        if (state.last_month >= 0d && now >= state.last_month && Date.getMonthsSince(state.last_month) < 1) return;
        state.last_month = now;
        try
        {
            Monthly(empire, state);
            if (state.last_year < 0d || now < state.last_year || Date.getYearsSince(state.last_year) >= 1)
            {
                state.last_year = now;
                Yearly(empire, state);
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][春秋战国] {empire.GetEmpireName()} 结算失败: {exception}");
        }
    }

    private static void Monthly(Empire empire, ZhouEraState state)
    {
        if (state.era < Spring) return;
        Kingdom core = empire.CoreKingdom;
        // 尊王：诸侯犯天子，霸主与诸侯共讨(加入天子一方)
        foreach (War war in core.getWars().ToList())
        {
            if (war == null || war.hasEnded()) continue;
            bool coreAttacker = war.isAttacker(core);
            Kingdom foe = coreAttacker ? war.main_defender : war.main_attacker;
            if (foe == null || foe.GetEmpire() != empire) continue;
            if (state.era >= Warring) continue;
            foreach (Kingdom loyal in Loyalists(empire, state, foe).Take(3))
                JoinSide(war, loyal, coreAttacker);
        }
        // 攘夷：夷狄攻打诸侯，天子与霸主出兵来援
        foreach (Kingdom vassal in Vassals(empire).ToList())
            foreach (War war in vassal.getWars().ToList())
            {
                if (war == null || war.hasEnded() || !war.isDefender(vassal)) continue;
                Kingdom attacker = war.main_attacker;
                if (!IsBarbarian(empire, attacker)) continue;
                JoinSide(war, core, attackerSide: false);
                if (state.era == Spring && World.world.kingdoms.get(state.hegemon_id) is Kingdom hegemon &&
                    hegemon != vassal) JoinSide(war, hegemon, attackerSide: false);
            }
        // 合纵：所抗之国攻打合纵之国，各国来援
        if (state.era >= Warring && World.world.kingdoms.get(state.coalition_target) is Kingdom target)
            foreach (War war in target.getWars().ToList())
            {
                if (war == null || war.hasEnded() || !war.isAttacker(target)) continue;
                if (!state.coalition.Contains(war.main_defender?.id ?? -1L)) continue;
                foreach (long id in state.coalition)
                    if (World.world.kingdoms.get(id) is Kingdom member && member != war.main_defender)
                        JoinSide(war, member, attackerSide: false);
            }
    }

    // 尊王的诸侯：不是犯上者、不在与天子交战，按国力排序，霸主优先
    private static IEnumerable<Kingdom> Loyalists(Empire empire, ZhouEraState state, Kingdom rebel) =>
        Vassals(empire).Where(kingdom => kingdom != rebel && !kingdom.isInWarWith(empire.CoreKingdom) &&
                                         !state.kings.Contains(kingdom.id))
            .OrderByDescending(kingdom => kingdom.id == state.hegemon_id).ThenByDescending(Power);

    private static void JoinSide(War war, Kingdom kingdom, bool attackerSide)
    {
        if (war == null || kingdom?.data == null || kingdom.isRekt() || war.hasEnded()) return;
        if (war.isAttacker(kingdom) || war.isDefender(kingdom)) return;
        Kingdom enemy = attackerSide ? war.main_defender : war.main_attacker;
        if (enemy == null || enemy == kingdom) return;
        try
        {
            if (attackerSide) war.joinAttackers(kingdom);
            else war.joinDefenders(kingdom);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][春秋战国] 参战失败: {exception.Message}");
        }
    }

    private static void Yearly(Empire empire, ZhouEraState state)
    {
        Kingdom core = empire.CoreKingdom;
        // 新加入的诸侯在春秋、战国也有外交与征伐之权(郡除外)
        if (state.era >= Spring)
            foreach (Kingdom vassal in Vassals(empire))
                if (vassal.GetKingdomType() != KingdomType.ZhouFeudalism_jun)
                    vassal.GetRegime()?.SetAllowDiplomacy(true);
        state.kings.RemoveAll(id => World.world.kingdoms.get(id)?.GetEmpire() != empire);
        state.reforms.RemoveAll(id => World.world.kingdoms.get(id)?.GetEmpire() != empire);
        switch (state.era)
        {
            case WesternZhou:
                if (EastwardMove(empire, state) || WeakRoyalty(empire)) EnterSpring(empire, state);
                break;
            case Spring:
                SpringYear(empire, state);
                int vassals = Vassals(empire).Count();
                if (state.partitioned || empire.Mandate < 30 || vassals <= 7 && state.annexed >= 1)
                    EnterWarring(empire, state);
                break;
            case Warring:
                WarringYear(empire, state);
                TryUnify(empire, state);
                break;
        }
    }

    // 王畿失陷：周室东迁
    private static bool EastwardMove(Empire empire, ZhouEraState state)
    {
        City old = World.world.cities.get(state.royal_capital_id);
        Kingdom core = empire.CoreKingdom;
        if (old == null || old.isRekt() || old.kingdom == core || core.capital == null) return false;
        Log(empire, string.Format(LM.Get("zhou_log_eastward"), old.GetCityName(), core.capital.GetCityName()));
        state.royal_capital_id = core.capital.id;
        state.eastward_at = World.world.getCurWorldTime();
        return true;
    }

    // 王畿兵力不及最强诸侯(只比兵力)，或天命低于 60
    private static bool WeakRoyalty(Empire empire)
    {
        int royal = empire.CoreKingdom.countTotalWarriors();
        return empire.Mandate < 60 || Vassals(empire).Any(vassal => vassal.countTotalWarriors() > royal);
    }

    public static void EnterSpring(Empire empire, ZhouEraState state)
    {
        if (state.era >= Spring) return;
        state.era = Spring;
        state.era_since = World.world.getCurWorldTime();
        state.vassals_at_spring = Vassals(empire).Count();
        state.annexed = 0;
        foreach (Kingdom vassal in Vassals(empire))
            if (vassal.GetKingdomType() != KingdomType.ZhouFeudalism_jun)
                vassal.GetRegime()?.SetAllowDiplomacy(true);
        Log(empire, LM.Get("zhou_log_spring"));
        empire.RecordHistory(directContent: string.Format(LM.Get("zhou_history_spring"), empire.GetEmpireName()),
            kingdomId: empire.CoreKingdom.id);
        HoldConference(empire, state, null);
    }

    public static void EnterWarring(Empire empire, ZhouEraState state)
    {
        if (state.era >= Warring) return;
        state.era = Warring;
        state.era_since = World.world.getCurWorldTime();
        if (state.hegemon_id >= 0) Log(empire, LM.Get("zhou_log_hegemony_ends"));
        state.hegemon_id = -1L;
        Log(empire, LM.Get("zhou_log_warring"));
        empire.RecordHistory(directContent: string.Format(LM.Get("zhou_history_warring"), empire.GetEmpireName()),
            kingdomId: empire.CoreKingdom.id);
    }

    #endregion

    #region 春秋：霸主、卿大夫、僭越

    private static void SpringYear(Empire empire, ZhouEraState state)
    {
        Kingdom hegemon = World.world.kingdoms.get(state.hegemon_id);
        bool valid = hegemon != null && !hegemon.isRekt() && hegemon.GetEmpire() == empire &&
                     !hegemon.isInWarWith(empire.CoreKingdom) && !state.kings.Contains(hegemon.id) &&
                     Power(hegemon) >= 0.7f * Vassals(empire).Where(other => other != hegemon).Select(Power).DefaultIfEmpty(0).Max();
        if (!valid)
        {
            if (hegemon != null) Log(empire, string.Format(LM.Get("zhou_log_hegemon_lost"), hegemon.GetKingdomName()));
            state.hegemon_id = -1L;
            HoldConference(empire, state, null);
        }
        else if (Date.getYearsSince(state.hegemon_since) >= ConferenceYears)
            HoldConference(empire, state, null);
        hegemon = World.world.kingdoms.get(state.hegemon_id);
        if (hegemon != null)
        {
            CollectTribute(empire, state, hegemon);
            Arbitrate(empire, state, hegemon);
            PunishUsurper(empire, state, hegemon);
            PunishDefiant(empire, state, hegemon);
        }
        foreach (Kingdom vassal in Vassals(empire).ToList())
        {
            if (TryPartition(empire, state, vassal)) break;
            TryUsurpation(empire, vassal);
            TryProclaimKing(empire, state, vassal, spring: true);
        }
    }

    // 会盟：最强且尊王的诸侯为霸主；chosen 不为空时由天子直接赐胙
    public static Kingdom HoldConference(Empire empire, ZhouEraState state, Kingdom chosen)
    {
        if (state.era != Spring) return null;
        Kingdom hegemon = chosen ?? Vassals(empire)
            .Where(kingdom => !kingdom.isInWarWith(empire.CoreKingdom) && !state.kings.Contains(kingdom.id) &&
                              kingdom.GetKingdomType() != KingdomType.ZhouFeudalism_jun)
            .OrderByDescending(Power).FirstOrDefault();
        if (hegemon == null) return null;
        state.conferences++;
        bool renewed = state.hegemon_id == hegemon.id;
        state.hegemon_id = hegemon.id;
        state.hegemon_since = World.world.getCurWorldTime();
        empire.AddMandate(3);
        string place = hegemon.capital?.GetCityName() ?? hegemon.GetKingdomName();
        Log(empire, string.Format(LM.Get(renewed ? "zhou_log_conference_renew" : "zhou_log_conference"),
            hegemon.GetKingdomName(), place));
        if (!renewed)
            empire.RecordHistory(directContent: string.Format(LM.Get("zhou_history_hegemon"), hegemon.GetKingdomName(), place),
                kingdomId: hegemon.id);
        return hegemon;
    }

    // 一年的赋税收入(按国库账目折算；没有账目时按国库的十分之一估)
    private static int YearlyRevenue(Kingdom kingdom)
    {
        TreasuryReport report = TreasurySystem.Report(kingdom);
        if (report != null && report.months > 0 && report.income > 0)
            return (int)Math.Min(int.MaxValue, report.income * 12L / report.months);
        return Mathf.Max(0, kingdom.GetMoney()) / 10;
    }

    // 纳贡：诸侯每年以赋税一成献给霸主；国库交不出、或与霸主交恶不肯交的，记为不朝
    private static void CollectTribute(Empire empire, ZhouEraState state, Kingdom hegemon)
    {
        int total = 0;
        state.defiant.Clear();
        foreach (Kingdom vassal in Vassals(empire).Where(vassal => vassal != hegemon))
        {
            if (vassal.isInWarWith(hegemon) || state.kings.Contains(vassal.id) ||
                (World.world.diplomacy?.getOpinion(vassal, hegemon)?.total ?? 0) < -20)
            {
                state.defiant.Add(vassal.id);
                continue;
            }
            int tribute = YearlyRevenue(vassal) / 10;
            if (tribute <= 0) continue;
            if (vassal.GetMoney() < tribute)
            {
                state.defiant.Add(vassal.id);
                continue;
            }
            vassal.SubMoney(tribute, TreasuryCategory.Tribute);
            total += tribute;
        }
        if (total > 0) hegemon.AddMoney(total, TreasuryCategory.Tribute);
    }

    // 讨不庭：霸主讨伐接壤的不朝之国(比它弱的才动手)
    private static void PunishDefiant(Empire empire, ZhouEraState state, Kingdom hegemon)
    {
        foreach (long id in state.defiant.ToList())
        {
            Kingdom defiant = World.world.kingdoms.get(id);
            if (defiant == null || defiant.isRekt() || defiant == hegemon || hegemon.isInWarWith(defiant) ||
                !hegemon.IsNeighbourWith(defiant) || Power(defiant) * 1.2f > Power(hegemon) || Random.value > 0.3f)
                continue;
            War war = World.world.diplomacy.startWar(hegemon, defiant, WarTypeLibrary.normal);
            if (war == null) continue;
            Log(empire, string.Format(LM.Get("zhou_log_punish_defiant"), hegemon.GetKingdomName(), defiant.GetKingdomName()));
            return;
        }
    }

    // 霸主调停：诸侯互攻，霸主令其罢兵(存亡继绝：弱小的一方被打时更可能出面)
    private static void Arbitrate(Empire empire, ZhouEraState state, Kingdom hegemon)
    {
        foreach (Kingdom vassal in Vassals(empire).ToList())
            foreach (War war in vassal.getWars().ToList())
            {
                if (war == null || war.hasEnded() || !war.isAttacker(vassal)) continue;
                Kingdom victim = war.main_defender;
                if (victim == null || victim.GetEmpire() != empire || victim == empire.CoreKingdom) continue;
                if (vassal == hegemon || victim == hegemon || state.kings.Contains(victim.id)) continue;
                float chance = Power(victim) * 2 < Power(vassal) ? 0.5f : 0.25f;
                if (Random.value > chance) continue;
                vassal.EndWarWith(victim);
                Log(empire, string.Format(LM.Get("zhou_log_arbitrate"), hegemon.GetKingdomName(), vassal.GetKingdomName(),
                    victim.GetKingdomName()));
            }
    }

    // 霸主讨伐僭越称王者(攘夷)
    private static void PunishUsurper(Empire empire, ZhouEraState state, Kingdom hegemon)
    {
        foreach (long id in state.kings.ToList())
        {
            Kingdom usurper = World.world.kingdoms.get(id);
            if (usurper == null || usurper.isRekt() || usurper == hegemon || hegemon.isInWarWith(usurper) ||
                !hegemon.IsNeighbourWith(usurper) || Random.value > 0.25f) continue;
            War war = World.world.diplomacy.startWar(hegemon, usurper, WarTypeLibrary.normal);
            if (war == null) continue;
            war.SetEmpireWarType(EmpireCraft.Scripts.Enums.EmpireWarType.攘夷);
            Log(empire, string.Format(LM.Get("zhou_log_punish"), hegemon.GetKingdomName(), usurper.GetKingdomName()));
            return;
        }
    }

    // 三家分晋：六城以上的大国，国君年幼或年老时更易被权臣瓜分为三
    private static bool TryPartition(Empire empire, ZhouEraState state, Kingdom vassal)
    {
        List<City> cities = vassal.cities?.Where(city => city != null && !city.isRekt()).ToList();
        if (cities == null || cities.Count < 6 || vassal.capital == null) return false;
        Actor ruler = vassal.king;
        float chance = 0.02f + (ruler == null || !ruler.isAdult() || ruler.getAge() > 60 ? 0.02f : 0f);
        if (Random.value > chance) return false;
        return Partition(empire, state, vassal, cities);
    }

    public static bool Partition(Empire empire, ZhouEraState state, Kingdom vassal, List<City> cities = null)
    {
        cities ??= vassal.cities?.Where(city => city != null && !city.isRekt()).ToList() ?? new List<City>();
        City capital = vassal.capital;
        if (capital == null || cities.Count < 3) return false;
        WorldTile Tile(City city) => city.getTile();
        float Distance(City a, City b) => Tile(a) == null || Tile(b) == null ? 0f : Toolbox.SquaredDistTile(Tile(a), Tile(b));
        City second = cities.Where(city => city != capital).OrderByDescending(city => Distance(city, capital)).First();
        City third = cities.Where(city => city != capital && city != second)
            .OrderByDescending(city => Mathf.Min(Distance(city, capital), Distance(city, second))).First();
        var seeds = new[] { capital, second, third };
        var groups = seeds.ToDictionary(seed => seed, _ => new List<City>());
        foreach (City city in cities)
            groups[seeds.OrderBy(seed => Distance(city, seed)).First()].Add(city);
        string name = vassal.GetKingdomName();
        var heirs = new List<string>();
        foreach (City seed in new[] { second, third })
        {
            Actor minister = seed.leader ?? seed.units?.FirstOrDefault(actor => actor != null && actor.isAlive() &&
                                                                                actor.isAdult() && actor != vassal.king);
            if (minister == null) continue;
            Kingdom split = seed.makeOwnKingdom(minister);
            if (split == null) continue;
            foreach (City city in groups[seed])
                if (city != seed && city.kingdom == vassal) city.joinAnotherKingdom(split);
            split.SetRegimeType(RegimeType.ZhouFeudalism);
            split.LoadRegime();
            split.GetRegime()?.SetAllowDiplomacy(true);
            split.GetRegime()?.SetLeaderSelectMethod(LeaderSelectMethod.Succession);
            empire.join(split, pForce: true);
            heirs.Add(split.GetKingdomName());
        }
        if (heirs.Count == 0) return false;
        state.partitioned = true;
        Log(empire, string.Format(LM.Get("zhou_log_partition"), name, string.Join("、", heirs)));
        empire.RecordHistory(directContent: string.Format(LM.Get("zhou_history_partition"), name, string.Join("、", heirs)),
            kingdomId: vassal.id);
        return true;
    }

    // 田氏代齐：四城以上的大国，权臣(最大的非都城城主)篡夺君位
    private static void TryUsurpation(Empire empire, Kingdom vassal)
    {
        if ((vassal.cities?.Count ?? 0) < 4 || vassal.capital == null || Random.value > 0.015f) return;
        City seat = vassal.cities.Where(city => city != null && !city.isRekt() && city != vassal.capital && city.leader != null)
            .OrderByDescending(city => city.units?.Count ?? 0).FirstOrDefault();
        Actor usurper = seat?.leader;
        Actor ruler = vassal.king;
        if (usurper == null || usurper.isRekt() || !usurper.isAlive() || ruler == null ||
            SpecificClanManager.SameLineage(usurper.GetSpecificClan(), ruler.GetSpecificClan())) return;
        seat.removeLeader();
        vassal.setKing(usurper);
        usurper.setCity(vassal.capital);
        Log(empire, string.Format(LM.Get("zhou_log_usurp"), usurper.getName(), vassal.GetKingdomName()));
    }

    // 称王：春秋时子爵的夷狄大国僭越称王；战国时三城以上的大国相继称王
    private static void TryProclaimKing(Empire empire, ZhouEraState state, Kingdom vassal, bool spring)
    {
        if (state.kings.Contains(vassal.id) || vassal.GetKingdomType() == KingdomType.ZhouFeudalism_jun) return;
        if (spring)
        {
            if (vassal.GetKingdomType() != KingdomType.ZhouFeudalism_zi) return;
            int strongest = Vassals(empire).Select(Power).DefaultIfEmpty(0).Max();
            if (Power(vassal) < 0.6f * strongest || Random.value > 0.03f) return;
            ProclaimKing(empire, state, vassal, usurp: true);
        }
        else
        {
            if ((vassal.cities?.Count ?? 0) < 3 || Random.value > 0.35f) return;
            ProclaimKing(empire, state, vassal, usurp: false);
        }
    }

    public static void ProclaimKing(Empire empire, ZhouEraState state, Kingdom vassal, bool usurp)
    {
        if (state.kings.Contains(vassal.id)) return;
        state.kings.Add(vassal.id);
        if (usurp) empire.AddMandate(-10);
        Log(empire, string.Format(LM.Get(usurp ? "zhou_log_usurp_king" : "zhou_log_proclaim_king"),
            vassal.GetKingdomName()));
        if (state.hegemon_id == vassal.id) state.hegemon_id = -1L;
    }

    #endregion

    #region 战国：称王、变法、合纵、统一

    private static void WarringYear(Empire empire, ZhouEraState state)
    {
        foreach (Kingdom vassal in Vassals(empire).ToList())
        {
            TryProclaimKing(empire, state, vassal, spring: false);
            if (!state.reforms.Contains(vassal.id) && (vassal.cities?.Count ?? 0) >= 3 && Random.value < 0.06f)
            {
                state.reforms.Add(vassal.id);
                empire.AddMandate(-1);
                // 旧贵族怨望
                ClaimPolicySystem.AddGrievance(empire, SocialClass.Noble, 5f);
                Log(empire, string.Format(LM.Get("zhou_log_reform"), vassal.GetKingdomName()));
            }
            // 变法之国赋税每年多收一成
            if (state.reforms.Contains(vassal.id))
            {
                int bonus = YearlyRevenue(vassal) / 10;
                if (bonus > 0) vassal.AddMoney(bonus, TreasuryCategory.ResidentTax);
            }
        }
        UpdateCoalition(empire, state);
    }

    private static void UpdateCoalition(Empire empire, ZhouEraState state)
    {
        var realms = Vassals(empire).Append(empire.CoreKingdom).Where(kingdom => kingdom != null).ToList();
        int total = realms.Sum(Power);
        Kingdom strongest = realms.OrderByDescending(Power).FirstOrDefault();
        int second = realms.Where(kingdom => kingdom != strongest).Select(Power).DefaultIfEmpty(0).Max();
        bool dominant = strongest != null && total > 0 && Power(strongest) >= 0.3f * total && Power(strongest) >= 1.5f * second;
        if (!dominant)
        {
            if (state.coalition_target >= 0) Log(empire, LM.Get("zhou_log_coalition_ends"));
            state.coalition_target = -1L;
            state.coalition.Clear();
            state.horizontal.Clear();
            return;
        }
        bool formed = state.coalition_target != strongest.id;
        if (formed) state.horizontal.Clear();
        state.horizontal.RemoveAll(id => World.world.kingdoms.get(id)?.GetEmpire() != empire);
        // 连横：与独大之国接壤、国力不到它一成半的弱国，每年有一成可能依附于它(不入合纵，彼此不攻)
        foreach (Kingdom weak in realms.Where(kingdom => kingdom != strongest && kingdom != empire.CoreKingdom &&
                                                         !state.horizontal.Contains(kingdom.id) &&
                                                         Power(kingdom) < 0.15f * Power(strongest) &&
                                                         kingdom.IsNeighbourWith(strongest) &&
                                                         !kingdom.isInWarWith(strongest)).ToList())
        {
            if (Random.value > 0.1f) continue;
            state.horizontal.Add(weak.id);
            Log(empire, string.Format(LM.Get("zhou_log_horizontal"), weak.GetKingdomName(), strongest.GetKingdomName()));
        }
        var members = realms.Where(kingdom => kingdom != strongest && !state.horizontal.Contains(kingdom.id) &&
                                              Power(kingdom) >= 0.1f * Power(strongest))
            .Select(kingdom => kingdom.id).ToList();
        if (members.Count < 2) return;
        state.coalition_target = strongest.id;
        state.coalition = members;
        if (formed)
            Log(empire, string.Format(LM.Get("zhou_log_coalition"), members.Count, strongest.GetKingdomName()));
    }

    // 天子(或代周者)直辖天下三分之二的城：称皇帝，改行郡县
    private static void TryUnify(Empire empire, ZhouEraState state)
    {
        int total = empire.kingdoms_list.Where(kingdom => kingdom?.cities != null && !kingdom.isRekt())
            .Sum(kingdom => kingdom.cities.Count);
        int core = empire.CoreKingdom?.cities?.Count ?? 0;
        if (total <= 0 || core * 3 < total * 2) return;
        Unify(empire, state);
    }

    public static void Unify(Empire empire, ZhouEraState state)
    {
        Kingdom core = empire.CoreKingdom;
        state.era = Unified;
        state.era_since = World.world.getCurWorldTime();
        foreach (Kingdom kingdom in empire.kingdoms_list.ToList())
        {
            if (kingdom?.data == null || kingdom.isRekt()) continue;
            kingdom.SetRegimeType(RegimeType.LvLing);
            kingdom.LoadRegime();
            if (kingdom == core) continue;
            kingdom.GetRegime()?.SetAllowDiplomacy(false);
        }
        // 先在制度树里补上郡县，再按新阶段重建中央官制(秦汉三公九卿)
        InstitutionSystem.SyncEnactedNodeForRegime(empire, RegimeType.LvLing);
        empire.data.centerOffice?.Init(core);
        core.SystemChange();
        Log(empire, string.Format(LM.Get("zhou_log_unified"), core.GetKingdomName()));
        empire.RecordHistory(directContent: string.Format(LM.Get("zhou_history_unified"), core.GetKingdomName(),
            empire.GetEmpireName()), kingdomId: core.id);
    }

    #endregion

    #region 神力

    // 推进时代：西周 → 春秋 → 战国 → 统一(由天下最强者统一，诸侯最强时先取周而代之)
    public static void GodAdvance(Empire empire)
    {
        if (!Applies(empire)) return;
        ZhouEraState state = State(empire);
        switch (state.era)
        {
            case WesternZhou:
                EnterSpring(empire, state);
                break;
            case Spring:
                EnterWarring(empire, state);
                break;
            case Warring:
                Kingdom strongest = Vassals(empire).OrderByDescending(Power).FirstOrDefault();
                if (strongest != null && Power(strongest) > Power(empire.CoreKingdom))
                {
                    City royal = empire.CoreKingdom.capital;
                    OnRoyalCapitalTaken(royal, empire.CoreKingdom, strongest);
                    empire = strongest.GetEmpire();
                    if (!Applies(empire)) return;
                }
                Unify(empire, State(empire));
                break;
        }
    }

    #endregion

    public static void Log(Empire empire, string text)
    {
        ZhouEraState state = State(empire);
        if (state == null || string.IsNullOrEmpty(text)) return;
        state.log.Insert(0, new CourtLogEntry { time = World.world?.getCurWorldTime() ?? 0d, text = text });
        if (state.log.Count > LogSize) state.log.RemoveRange(LogSize, state.log.Count - LogSize);
    }
}

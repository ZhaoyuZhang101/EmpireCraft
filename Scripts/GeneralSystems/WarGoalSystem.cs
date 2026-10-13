using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 开战目标(战后分配的一部分，参照钢铁雄心：战时占领与最终归属分开)：国战开打时按形势定一个目标，议和时按目标结算——
//   · 吞并：攻方兵力两倍以上、与对方接壤、对方是小国；
//   · 割地(局部入侵)：接壤、兵力相当；
//   · 赔款：不接壤(跨海)或攻方兵力不如对方；
//   · 劫掠：原有的劫掠战争(洗劫第一座城即收兵，见 CityPatch)；
//   · 收复：对方手里有我方法理(法理主国是我方)的城，且接壤——优先于其他目标；
//   · 迫使附庸：攻方兵力一倍半以上、对方不是帝国，攻方行分封(封建、分封制)或对方大到吞不下。
// 战争窗口里点"战争目标"一行可以轮换目标(劫掠除外)。
// 议和时按这场仗里双方各丢了多少城(含被对方占了一半以上地块的城)判胜负，相差不到一城就是白和——不赔不割：
//   攻方胜：赔款 → 对方赔款；割地 → 被攻方占了一半以上的城割给占领者；吞并 → 同割地，对方丢了一半城或都城时余下的城一并吞并；
//           收复 → 我方法理的城中占了一半以上的归还占领者，占了法理城的一半以上时全部归还；迫使附庸 → 对方成为攻方的附庸；
//   守方胜：攻方赔款(劫掠的不赔，打退就是)。
// 以前不论怎么收场都按"有胜负"处理，议和(原版最常见的结局)也让攻方赔款，几乎每场仗打完都赔款。
// 叛乱、独立、索取法理、去帝号这类有自己结局的战争不在此列，照旧各按各的规则。
public static class WarGoalSystem
{
    // 吞并：攻方兵力至少是对方几倍、对方城数不超过多少(或不超过攻方一半)
    private const float AnnexStrength = 2f;
    private const int AnnexSmallCities = 3;
    // 割地：攻方兵力至少是对方几成
    private const float ConquestStrength = 0.8f;
    // 迫使附庸：攻方兵力至少是对方几倍
    private const float VassalStrength = 1.5f;
    // 战争窗口里轮换的顺序(劫掠不参与)
    private static readonly WarGoal[] Cycle = { WarGoal.吞并, WarGoal.割地, WarGoal.收复, WarGoal.迫使附庸, WarGoal.赔款 };
    // 占了一座城一半以上的地块才算到手
    private const float HeldShare = 0.5f;
    // 每帧最多易主几座城(易主有原版开销)
    private const int CessionsPerFrame = 3;

    private static readonly HashSet<EmpireWarType> OwnEnding = new()
    {
        EmpireWarType.游牧扩张, EmpireWarType.迫使朝贡, EmpireWarType.藩王索取皇位, EmpireWarType.派系叛乱,
        EmpireWarType.地方叛乱, EmpireWarType.民族叛乱, EmpireWarType.宗教叛乱, EmpireWarType.地方独立,
        EmpireWarType.附庸独立, EmpireWarType.清君侧, EmpireWarType.索取法理, EmpireWarType.去帝号,
        EmpireWarType.帝国正统, EmpireWarType.不奉诏, EmpireWarType.获取帝国
    };

    private static readonly List<(long city, long to)> PendingCessions = new();
    private static readonly List<(long lord, long subject)> PendingVassals = new();

    public static void ResetWorldState()
    {
        PendingCessions.Clear();
        PendingVassals.Clear();
    }

    private static bool Applies(War war) =>
        war?.data != null && war.getAsset() != WarTypeLibrary.rebellion && !OwnEnding.Contains(war.GetEmpireWarType());

    public static WarGoal Get(War war)
    {
        if (war?.data == null) return WarGoal.None;
        if (war.GetEmpireWarType() == EmpireWarType.劫掠) return WarGoal.劫掠;
        return Applies(war) ? war.GetOrCreate().war_goal : WarGoal.None;
    }

    // 开打时定目标(WarPatch.update 每场仗第一次结算时调用)；读档后已定的不变
    public static void Ensure(War war)
    {
        if (war?.data == null || war.hasEnded()) return;
        var data = war.GetOrCreate();
        if (data.war_goal != WarGoal.None || !Applies(war)) return;
        Kingdom attacker = war.main_attacker, defender = war.main_defender;
        if (attacker?.data == null || defender?.data == null || attacker.isRekt() || defender.isRekt()) return;
        if (war.GetEmpireWarType() == EmpireWarType.劫掠)
        {
            data.war_goal = WarGoal.劫掠;
            return;
        }
        float strength = WarSituation.StrengthRatio(defender, attacker);
        int theirs = WarSituation.SideCities(defender), ours = WarSituation.SideCities(attacker);
        bool borders = attacker.IsNeighbourWith(defender);
        RegimeType? regime = attacker.GetRegime()?.type;
        bool feudal = regime == RegimeType.Feudalism || regime == RegimeType.ZhouFeudalism;
        bool small = theirs <= AnnexSmallCities || theirs * 2 <= ours;
        // 战国诸侯互伐：兵力够就以兼并(对方小)或割地为务，不再只求赔款
        if (borders && ZhouEraSystem.WarringRivals(attacker, defender) && Claimed(attacker, defender.cities).Count == 0)
            data.war_goal = strength >= ConquestStrength ? small ? WarGoal.吞并 : WarGoal.割地 : WarGoal.赔款;
        else data.war_goal = !borders ? OverseasGoal(attacker, defender, strength, small)
            : Claimed(attacker, defender.cities).Count > 0 ? WarGoal.收复
            : strength >= AnnexStrength && small && !feudal ? WarGoal.吞并
            : strength >= VassalStrength && !defender.IsEmpire() && (feudal || strength >= AnnexStrength) ? WarGoal.迫使附庸
            : strength >= ConquestStrength ? WarGoal.割地
            : WarGoal.赔款;
        data.war_goal_capital_id = defender.capital?.data != null ? defender.capital.id : -1L;
        TranslateHelper.LogEventMessage(string.Format(LM.Get("war_goal_declared"), attacker.GetKingdomName(),
            defender.GetKingdomName(), Name(data.war_goal)), attacker);
        // 开打即拜统帅(谋划开战时已拜的不变，见 WarCommandSystem)
        WarCommandSystem.MonthTick(FrontSystem.Realm(attacker));
        WarCommandSystem.MonthTick(FrontSystem.Realm(defender));
    }

    // 跨海：兵力两倍以上、战舰是对方一倍半以上、对方都城在舰种航程内，才谋割地(小国则吞并)，否则只求赔款
    private static WarGoal OverseasGoal(Kingdom attacker, Kingdom defender, float strength, bool small)
    {
        if (strength < AnnexStrength || defender.capital?.data == null) return WarGoal.赔款;
        int ours = NavySystem.Warships(attacker), theirs = NavySystem.Warships(defender);
        if (ours <= 0 || ours < theirs * 1.5f) return WarGoal.赔款;
        List<SeaRegionSystem.Region> route = NavySystem.LandingRoute(attacker, defender.capital);
        if (route == null || route.Count > NavySystem.MaxRange(attacker)) return WarGoal.赔款;
        return small ? WarGoal.吞并 : WarGoal.割地;
    }

    // 窗口里能不能轮换目标：定了目标、还没打完、不是劫掠
    public static bool Switchable(War war)
    {
        WarGoal goal = Get(war);
        return goal != WarGoal.None && goal != WarGoal.劫掠 && war?.data != null && !war.hasEnded();
    }

    public static void Next(War war)
    {
        if (!Switchable(war)) return;
        var data = war.GetOrCreate();
        int index = Array.IndexOf(Cycle, data.war_goal);
        data.war_goal = Cycle[(index + 1) % Cycle.Length];
        if (data.war_goal_capital_id < 0 && war.main_defender?.capital?.data != null)
            data.war_goal_capital_id = war.main_defender.capital.id;
    }

    // cities 里法理主国(或法理持有人所在国)是攻方一方(帝国按整个帝国算)的城
    private static List<City> Claimed(Kingdom claimant, IEnumerable<City> cities)
    {
        var result = new List<City>();
        Kingdom realm = FrontSystem.Realm(claimant);
        if (cities == null || realm == null) return result;
        foreach (City city in cities)
        {
            if (city?.data == null || city.isRekt()) continue;
            var title = city.GetTitle();
            if (title == null || title.isRekt()) continue;
            Kingdom holder = title.main_kingdom ?? title.owner?.kingdom;
            if (holder?.data != null && !holder.isRekt() && holder != city.kingdom && FrontSystem.Realm(holder) == realm)
                result.Add(city);
        }
        return result;
    }

    public static string Name(WarGoal goal) => LM.Get("war_goal_" + goal);

    // 战争窗口：目标与战况(双方各已得几城)
    public static string Describe(War war)
    {
        WarGoal goal = Get(war);
        if (goal == WarGoal.None) return null;
        if (war.hasEnded()) return Name(goal);
        (int attackerGain, int defenderGain) = Gains(war);
        return string.Format(LM.Get("war_goal_standing"), Name(goal), attackerGain, defenderGain);
    }

    // 攻守双方在这场仗里各得几城：对方参战以来少了的城 + 己方占了一半以上地块的对方城市
    private static (int attackerGain, int defenderGain) Gains(War war)
    {
        Kingdom attacker = war.main_attacker, defender = war.main_defender;
        int defenderLost = Math.Max(0, WarSituation.CitiesLost(war, defender));
        int attackerLost = Math.Max(0, WarSituation.CitiesLost(war, attacker));
        return (defenderLost + HeldCities(war, war._list_defenders, war._list_attackers).Count,
            attackerLost + HeldCities(war, war._list_attackers, war._list_defenders).Count);
    }

    // owners 的城里被 occupiers 一方占了一半以上地块的：城 → 占得最多的那国
    private static List<(City city, Kingdom holder)> HeldCities(War war, IEnumerable<Kingdom> owners, IEnumerable<Kingdom> occupiers)
    {
        var result = new List<(City, Kingdom)>();
        List<Kingdom> side = occupiers.Where(kingdom => kingdom?.data != null && !kingdom.isRekt()).ToList();
        foreach (Kingdom owner in owners)
        {
            if (owner?.cities == null || owner.isRekt()) continue;
            foreach (City city in owner.cities)
            {
                if (city?.data == null || city.isRekt() || city.zones == null || city.zones.Count == 0) continue;
                int total = 0, best = 0;
                Kingdom holder = null;
                foreach (Kingdom kingdom in side)
                {
                    int count = city.GetOccupiedTileZoneCount(kingdom);
                    if (count <= 0) continue;
                    total += count;
                    if (count > best)
                    {
                        best = count;
                        holder = kingdom;
                    }
                }
                if (holder != null && total >= city.zones.Count * HeldShare) result.Add((city, holder));
            }
        }
        return result;
    }

    // 战争收尾时(WarPatch.end_war，清掉占领之前)：判胜负、按目标结算
    public static void Settle(War war, WarWinner winner)
    {
        try
        {
            if (war?.data == null || winner == WarWinner.Nobody || winner == WarWinner.Merged || !Applies(war) &&
                war.GetEmpireWarType() != EmpireWarType.劫掠) return;
            Kingdom attacker = war.main_attacker, defender = war.main_defender;
            if (attacker?.data == null || defender?.data == null || attacker == defender) return;
            WarGoal goal = Get(war);
            if (goal == WarGoal.None) goal = WarGoal.赔款;
            (int attackerGain, int defenderGain) = Gains(war);
            bool attackersWon = winner == WarWinner.Attackers || winner == WarWinner.Peace && attackerGain >= defenderGain + 1;
            bool defendersWon = winner == WarWinner.Defenders || winner == WarWinner.Peace && defenderGain >= attackerGain + 1;
            if (attackersWon)
            {
                if (defender.isRekt()) return;
                switch (goal)
                {
                    case WarGoal.赔款:
                        WarReparationsSystem.Impose(defender, attacker);
                        break;
                    case WarGoal.割地:
                        Cede(war, attacker, defender, annex: false);
                        break;
                    case WarGoal.吞并:
                        Cede(war, attacker, defender, annex: true);
                        break;
                    case WarGoal.收复:
                        Reclaim(war, attacker, defender);
                        break;
                    case WarGoal.迫使附庸:
                        // 帝国不会整个沦为附庸，退回赔款
                        if (defender.IsEmpire()) WarReparationsSystem.Impose(defender, attacker);
                        else PendingVassals.Add((attacker.id, defender.id));
                        break;
                }
            }
            else if (defendersWon)
            {
                if (goal != WarGoal.劫掠 && !attacker.isRekt()) WarReparationsSystem.Impose(attacker, defender);
            }
            if (attackersWon) Modernize(war, defender, attacker, war._list_defenders, war._list_attackers);
            else if (defendersWon) Modernize(war, attacker, defender, war._list_attackers, war._list_defenders);
            else if (!attacker.isRekt() && !defender.isRekt())
            {
                TranslateHelper.LogEventMessage(string.Format(LM.Get("war_peace_white"), attacker.GetKingdomName(),
                    defender.GetKingdomName()), attacker);
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][开战目标] 议和结算失败: {exception.Message}");
        }
    }

    // 被迫现代化：败方是帝国中央或独立君主国，战胜方是立宪、共和或现代国家时，按败方丢城多少决定是否被强行改造
    private static void Modernize(War war, Kingdom loser, Kingdom victor, IEnumerable<Kingdom> losers, IEnumerable<Kingdom> victors)
    {
        if (loser?.data == null || loser.isRekt()) return;
        Empire empire = loser.GetEmpire();
        if (empire?.CoreKingdom != loser) return;
        List<(City city, Kingdom holder)> held = HeldCities(war, new[] { loser }, victors);
        City capital = loser.capital;
        bool lostCapital = capital != null && held.Exists(entry => entry.city == capital);
        ConstitutionalEconomySystem.ImposeModernization(empire, victor, held.Count, lostCapital);
    }

    // 割让攻方占了一半以上的城；吞并且对方丢了一半城或都城时，主守方余下的城一并归主攻方(帝国核心不整体吞并，只割城)
    private static void Cede(War war, Kingdom attacker, Kingdom defender, bool annex)
    {
        List<(City city, Kingdom holder)> held = HeldCities(war, war._list_defenders, war._list_attackers);
        foreach ((City city, Kingdom holder) in held) PendingCessions.Add((city.id, holder.id));
        int ceded = held.Count;
        if (annex && !defender.IsEmpire())
        {
            Dictionary<long, int> initial = war.GetOrCreate().initial_cities;
            int before = initial != null && initial.TryGetValue(WarSituation.SideKey(defender), out int count) ? count : 0;
            int lost = Math.Max(0, WarSituation.CitiesLost(war, defender)) + held.Count;
            City capital = World.world.cities.get(war.GetOrCreate().war_goal_capital_id);
            bool capitalTaken = capital?.data != null && (capital.kingdom != defender || held.Any(entry => entry.city == capital));
            if (capitalTaken || before > 0 && lost * 2 >= before)
            {
                var queued = new HashSet<long>(held.Select(entry => entry.city.id));
                foreach (City city in defender.cities.ToList())
                    if (city?.data != null && !city.isRekt() && queued.Add(city.id)) PendingCessions.Add((city.id, attacker.id));
                TranslateHelper.LogEventMessage(string.Format(LM.Get("war_peace_annexed"), attacker.GetKingdomName(),
                    defender.GetKingdomName()), attacker);
                return;
            }
        }
        if (ceded > 0)
            TranslateHelper.LogEventMessage(string.Format(LM.Get("war_peace_ceded"), attacker.GetKingdomName(),
                defender.GetKingdomName(), ceded), attacker);
    }

    // 收复：我方法理的城里占了一半以上的归还占领者；占了法理城的一半以上时全部归还主攻方。对方手里没有我方法理的城时按割地
    private static void Reclaim(War war, Kingdom attacker, Kingdom defender)
    {
        List<City> claimed = Claimed(attacker, war._list_defenders.Where(kingdom => kingdom?.cities != null)
            .SelectMany(kingdom => kingdom.cities).ToList());
        if (claimed.Count == 0)
        {
            Cede(war, attacker, defender, annex: false);
            return;
        }
        var claimedSet = new HashSet<City>(claimed);
        List<(City city, Kingdom holder)> held = HeldCities(war, war._list_defenders, war._list_attackers)
            .Where(entry => claimedSet.Contains(entry.city)).ToList();
        var queued = new HashSet<long>();
        foreach ((City city, Kingdom holder) in held)
            if (queued.Add(city.id)) PendingCessions.Add((city.id, holder.id));
        if (held.Count * 2 >= claimed.Count)
            foreach (City city in claimed)
                if (queued.Add(city.id)) PendingCessions.Add((city.id, attacker.id));
        if (queued.Count > 0)
            TranslateHelper.LogEventMessage(string.Format(LM.Get("war_peace_reclaimed"), attacker.GetKingdomName(),
                defender.GetKingdomName(), queued.Count), attacker);
    }

    // 战争结束后几帧内逐城易主、立附庸(不在原版结束战争的同一帧里改归属)
    public static void Tick()
    {
        if (World.world == null) return;
        while (PendingVassals.Count > 0)
        {
            (long lordId, long subjectId) = PendingVassals[0];
            PendingVassals.RemoveAt(0);
            Kingdom lord = World.world.kingdoms.get(lordId), subject = World.world.kingdoms.get(subjectId);
            if (lord?.data == null || subject?.data == null || lord.isRekt() || subject.isRekt()) continue;
            try
            {
                if (FeudalVassalService.ForceBind(lord, subject))
                    TranslateHelper.LogEventMessage(string.Format(LM.Get("war_peace_vassalized"), subject.GetKingdomName(),
                        lord.GetKingdomName()), lord);
                else WarReparationsSystem.Impose(subject, lord);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][开战目标] 立附庸失败: {exception.Message}");
            }
        }
        if (PendingCessions.Count == 0) return;
        int done = 0;
        while (PendingCessions.Count > 0 && done < CessionsPerFrame)
        {
            (long cityId, long toId) = PendingCessions[0];
            PendingCessions.RemoveAt(0);
            City city = World.world.cities.get(cityId);
            Kingdom to = World.world.kingdoms.get(toId);
            if (city?.data == null || city.isRekt() || to?.data == null || to.isRekt() || city.kingdom == to) continue;
            try
            {
                Kingdom from = city.kingdom;
                bool capital = from?.data != null && from.capital == city;
                city.joinAnotherKingdom(to);
                done++;
                // 周室诸侯的都城在和约中割走，与攻破都城一样灭其国；割走王畿则攻者取而代之
                if (capital && city.kingdom == to && ZhouEraSystem.SphereOf(from) is Empire sphere)
                    ZhouEraSystem.QueueCapitalTaken(city, from, to, sphere.CoreKingdom == from);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][开战目标] {city.data.name} 割让失败: {exception.Message}");
            }
        }
    }
}

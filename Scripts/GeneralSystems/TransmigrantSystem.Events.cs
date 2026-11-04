using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 原著的节点事件(《临高启明》，取自原著设定整理的词条与正文)，按原著先后串起来，每件都有实际后果：
//   · 第一次反围剿(1628.10)：乡勇来犯被打退，俘虏充作劳力，伏波军见了血；
//   · 博铺保卫战(1629.1)：击退海盗联手进攻，表彰大会上成立保安团，真多一支治安军；
//   · 女仆革命(1629.12)：外派站先买了女仆，执委会无章可循，元老从咖啡馆游行到执委会大院，主席当众认错辞职，
//     全体执委辞职组成看守内阁，设女仆状态委员会；看守期间归化流程慢下来；
//   · 第二次全体大会(1630.1)：接着女仆革命开，通过共同纲领，明文规定归化民的地位，归化容量加大；
//   · 第二次反围剿(1630.7)：共同纲领以后头一回打赢大仗，阅兵庆功；
//   · 全岛平定(琼北、琼南治安战)：据有本岛或本地法理全境，归化容量再加大；
//   · 发动机行动(1631.11~1633)：灾区、战乱的沿海有大批流民时，大洋舰队成船把人运回本土，两年，要运输船、要花钱，
//     人运回来先进检疫营排队，后面慢慢消化；
//   · 霸王行动(1632.9)：打这片海上水师最强的对手，开战先突袭港湾，打赢了航路更远；
//   · 太阳伞专案(1634)：武林高手潜入行刺，元老无一伤亡，归化民死伤，暴露安全部门的问题，引出机构改革；
//   · 第三次全体大会(1634 年底)：调查报告、对明战争指导纲要，改行元老院内阁制；
//   · 大陆攻略(1635)：调兵北上，以后用兵更勤、够得更远。
// 撕破伪装仍照"据有两处省会或宣布大宋"(见 Reveal)，第三次大会要等大宋旗号打出来才开。
// 旧档：过了窗口的事件不补发，记 -1，界面显示"未记"。
public static partial class TransmigrantSystem
{
    public static readonly string[] EventOrder =
    {
        "first_siege", "bopu_defense", "maid_revolution", "second_congress", "second_encirclement", "homeland_secured",
        "engine_operation", "engine_end", "overlord_operation", "sunshade_case", "third_congress", "mainland_campaign"
    };

    private const float SiegePrisoners = 130f, SiegeVeterancy = 10f, BopuVeterancy = 10f, BopuSecurityTroops = 200f;
    private const int MaidAge = 2, CongressGap = 1, HomelandMinCities = 3;
    private const float CaretakerBoost = 0.75f, CongressBoost = 1.25f, HomelandBoost = 1.25f, ParadeStability = 5f, ParadeVeterancy = 15f, MaidHousingPerSeat = 1f;
    private const int EngineMinAge = 3, EngineYears = 2, EngineTrips = 6;
    private const float EngineShare = 0.1f, EngineMaxShare = 0.4f, EngineMinPeople = 1000f, EngineFee = 0.3f, EngineCityShare = 0.5f, EngineTreasuryShare = 0.5f;
    private const int OverlordMinShips = 3;
    private const float OverlordSurprise = 0.7f, OverlordOdds = 2f;
    private const int SunshadeAge = 6;
    private const float SunshadeMaxDead = 60f, SunshadeDeadShare = 0.01f, SunshadeStability = 6f;

    public static bool EventFired(TransmigrantState state, string id) => state?.events != null && state.events.TryGetValue(id, out int year) && year >= 0;

    // 这件事发生的年份；没到是 0；旧档没赶上是 -1
    public static int EventYear(TransmigrantState state, string id) =>
        state?.events != null && state.events.TryGetValue(id, out int year) ? year : 0;

    private static bool Settled(TransmigrantState state, string id) => state.events != null && state.events.ContainsKey(id);

    private static void Mark(TransmigrantState state, string id, int year)
    {
        state.events ??= new Dictionary<string, int>();
        state.events[id] = year;
    }

    private static string RosterName(TransmigrantState state, string wanted, string fallback)
    {
        SenatorSeat seat = state.seats?.FirstOrDefault(entry => FullName(entry) == wanted);
        return seat != null ? FullName(seat) : fallback;
    }

    private static City BaseCity(Kingdom kingdom, TransmigrantState state) =>
        World.world.cities.get(state.base_city_id) is City city && city.data != null && !city.isRekt() && city.kingdom == kingdom ? city : kingdom.capital;

    private static void HardenFubo(Kingdom kingdom, float veterancy)
    {
        CorpsData fubo = Fubo(kingdom);
        if (fubo != null) fubo.veterancy = Mathf.Min(FormationSystem.VeteranCap(fubo), fubo.veterancy + veterancy);
    }

    private static void ShiftStability(City city, float amount)
    {
        CityStabilityData data = city == null ? null : CityStabilitySystem.State(city);
        if (data != null) data.stability = Mathf.Clamp(data.stability + amount, 0f, 100f);
    }

    // 过了窗口的不补发：旧档读进来不会突然闹一场女仆革命
    private static void PassOverdue(TransmigrantState state)
    {
        if (state.stage >= 3)
            foreach (string id in new[] { "first_siege", "bopu_defense", "maid_revolution", "second_congress" })
                if (!Settled(state, id)) Mark(state, id, -1);
        if (state.stage >= 5)
            foreach (string id in new[] { "sunshade_case", "third_congress" })
                if (!Settled(state, id)) Mark(state, id, -1);
    }

    // 每年一次(见 Yearly)，在制度施行之前，当年的事件当年就能推动施行
    private static void StoryEvents(Kingdom kingdom, TransmigrantState state, int year)
    {
        PassOverdue(state);
        int age = year - state.landed_year;
        if (!Settled(state, "first_siege")) FirstSiege(kingdom, state, year);
        if (!Settled(state, "bopu_defense") && age >= 1 && Fubo(kingdom) != null) BopuDefense(kingdom, state, year);
        if (!Settled(state, "maid_revolution") && age >= MaidAge && InstitutionSystem.IsEnacted(Culture, "aosong_first_committee"))
            MaidRevolution(kingdom, state, year);
        if (!Settled(state, "second_congress") && EventFired(state, "maid_revolution") && year >= EventYear(state, "maid_revolution") + CongressGap)
            SecondCongress(kingdom, state, year);
        if (!Settled(state, "homeland_secured") && EventFired(state, "second_encirclement") && HomelandClear(kingdom, state))
            HomelandSecured(kingdom, state, year);
        if (!Settled(state, "engine_operation") && state.stage >= 3 && age >= EngineMinAge) EngineStart(kingdom, state, year);
        if (state.engine_until_year > 0) EngineYear(kingdom, state, year);
        if (!Settled(state, "overlord_operation") && EventFired(state, "engine_operation")) OverlordOperation(kingdom, state, year);
        if (!Settled(state, "sunshade_case") && age >= SunshadeAge && InstitutionSystem.IsEnacted(Culture, "aosong_political_security"))
            SunshadeCase(kingdom, state, year);
        if (!Settled(state, "third_congress") && EventFired(state, "sunshade_case") && year > EventYear(state, "sunshade_case") &&
            InstitutionSystem.IsEnacted(Culture, "aosong_great_song"))
            ThirdCongress(kingdom, state, year);
        if (!Settled(state, "mainland_campaign") && state.revealed && state.stage >= 5) MainlandCampaign(kingdom, state, year);
    }

    #region 早期

    private static void FirstSiege(Kingdom kingdom, TransmigrantState state, int year)
    {
        City home = BaseCity(kingdom, state);
        if (home == null) return;
        City from = null;
        int best = RefugeeReach + 1;
        foreach (City city in World.world.cities.list)
        {
            if (city?.data == null || city.isRekt() || city.kingdom == null || city.kingdom == kingdom ||
                CityPopulationSystem.CivilianBackground(city) < 50f) continue;
            int distance = ZoneDistance(home, city);
            if (distance >= best) continue;
            best = distance;
            from = city;
        }
        // 周围还没人烟：等有了邻居再说
        if (from == null) return;
        float prisoners = Mathf.Min(SiegePrisoners, CityPopulationSystem.CivilianBackground(from) * 0.1f);
        float moved = CityPopulationSystem.TransferCivilianBackground(from, home, prisoners);
        Shelter(home, moved);
        HardenFubo(kingdom, SiegeVeterancy);
        Mark(state, "first_siege", year);
        TranslateHelper.LogMajor(string.Format(LM.Get("transmigrant_event_first_siege_history"), kingdom.GetKingdomFullName(), home.GetCityName(),
            from.GetCityName(), Mathf.RoundToInt(moved)), kingdom);
    }

    private static void BopuDefense(Kingdom kingdom, TransmigrantState state, int year)
    {
        City home = BaseCity(kingdom, state);
        if (home == null) return;
        HardenFubo(kingdom, BopuVeterancy);
        CorpsData troop = CorpsSystem.RaiseStanding(kingdom, home, BopuSecurityTroops);
        Mark(state, "bopu_defense", year);
        TranslateHelper.LogMajor(string.Format(LM.Get("transmigrant_event_bopu_defense_history"), kingdom.GetKingdomFullName(), home.GetCityName(),
            troop == null ? "" : CorpsSystem.Name(troop)), kingdom);
    }

    // 女仆革命：外派站的人先买了女仆，执委会没有章程，元老们从咖啡馆一路游行到执委会大院。
    // 主席当众认错辞职，全体执委辞职组成看守内阁，设女仆状态委员会；取消配给、盖住宅要花钱，都城民心一时不稳
    private static void MaidRevolution(Kingdom kingdom, TransmigrantState state, int year)
    {
        SenatorSeat chair = state.seats?.FirstOrDefault(seat => seat.seat == state.chairman_seat);
        string resigned = chair != null ? FullName(chair) : LM.Get("transmigrant_event_chair_fallback");
        string leader = RosterName(state, "单良", LM.Get("transmigrant_event_opposition_fallback"));
        Mark(state, "maid_revolution", year);
        state.caretaker_until = year + CongressGap;
        state.maid_committee = true;
        int housing = Mathf.Min(Mathf.Max(0, kingdom.GetMoney()) / 10, Mathf.RoundToInt(SeatCount * MaidHousingPerSeat));
        if (housing > 0) kingdom.SubMoney(housing, TreasuryCategory.Welfare);
        ShiftStability(kingdom.capital, -SunshadeStability);
        // 辞职的主席这一届不再选上
        int barred = state.chairman_seat;
        state.barred_seat = barred;
        InstallRuler(kingdom, Elect(kingdom, state, termEnd: false));
        state.barred_seat = barred;
        TranslateHelper.LogMajor(string.Format(LM.Get("transmigrant_event_maid_revolution_history"), kingdom.GetKingdomFullName(), leader, resigned), kingdom);
    }

    // 第二次全体大会：看守内阁开的会，共同纲领、女仆培养与分配、归化民地位都在这里定(见 Milestones)
    private static void SecondCongress(Kingdom kingdom, TransmigrantState state, int year)
    {
        Mark(state, "second_congress", year);
        state.caretaker_until = -1;
        state.barred_seat = -1;
        state.naturalization_boost *= CongressBoost;
        InstallRuler(kingdom, Elect(kingdom, state, termEnd: true));
        TranslateHelper.LogMajor(string.Format(LM.Get("transmigrant_event_second_congress_history"), kingdom.GetKingdomFullName()), kingdom);
    }

    #endregion

    #region 打出来的事件

    // 战争结束(WarPatch.end_war)：共同纲领以后头一回打赢大仗算第二次反围剿；霸王行动的收场
    public static void OnWarEnded(War war, WarWinner winner)
    {
        if (war?.data == null || winner != WarWinner.Attackers && winner != WarWinner.Defenders) return;
        Kingdom ours = winner == WarWinner.Attackers ? war.main_attacker : war.main_defender;
        Kingdom enemy = winner == WarWinner.Attackers ? war.main_defender : war.main_attacker;
        // 霸王行动打输了：下回还能再打
        TransmigrantState loser = Of(enemy);
        if (loser != null && loser.overlord_war_id == war.data.id)
        {
            loser.overlord_war_id = -1L;
            loser.events?.Remove("overlord_operation");
        }
        TransmigrantState state = Of(ours);
        if (state == null || enemy?.data == null) return;
        int year = Date.getCurrentYear();
        if (state.overlord_war_id == war.data.id)
        {
            state.overlord_war_id = -1L;
            state.sea_hegemony = true;
            TranslateHelper.LogMajor(string.Format(LM.Get("transmigrant_event_overlord_won_history"), ours.GetKingdomFullName(), enemy.GetKingdomName()), ours);
        }
        if (Settled(state, "second_encirclement") || state.stage < 3) return;
        Mark(state, "second_encirclement", year);
        HardenFubo(ours, ParadeVeterancy);
        foreach (City city in ours.cities ?? new List<City>()) ShiftStability(city, ParadeStability);
        state.next_campaign_at = World.world.getCurWorldTime();
        TranslateHelper.LogMajor(string.Format(LM.Get("transmigrant_event_second_encirclement_history"), ours.GetKingdomFullName(), enemy.GetKingdomName()), ours);
    }

    // 据有落脚那座岛的全部城，或落脚地法理的全部城
    private static bool HomelandClear(Kingdom kingdom, TransmigrantState state)
    {
        City home = BaseCity(kingdom, state);
        if (home?.getTile() == null || (kingdom.cities?.Count ?? 0) < HomelandMinCities) return false;
        object island = home.getTile().region?.island;
        object title = home.GetTitle();
        bool islandClear = island != null, titleClear = title != null;
        foreach (City city in World.world.cities.list)
        {
            if (city?.data == null || city.isRekt() || city.kingdom == kingdom) continue;
            if (islandClear && ReferenceEquals(city.getTile()?.region?.island, island)) islandClear = false;
            if (titleClear && ReferenceEquals(city.GetTitle(), title)) titleClear = false;
            if (!islandClear && !titleClear) return false;
        }
        return islandClear || titleClear;
    }

    private static void HomelandSecured(Kingdom kingdom, TransmigrantState state, int year)
    {
        Mark(state, "homeland_secured", year);
        state.naturalization_boost *= HomelandBoost;
        TranslateHelper.LogMajor(string.Format(LM.Get("transmigrant_event_homeland_secured_history"), kingdom.GetKingdomFullName(),
            BaseCity(kingdom, state)?.GetCityName() ?? ""), kingdom);
    }

    #endregion

    #region 发动机行动

    private static bool Coastal(City city)
    {
        if (city?.zones == null) return false;
        foreach (TileZone zone in city.zones)
            foreach (WorldTile tile in zone?.tiles ?? Array.Empty<WorldTile>())
                if (tile?.Type?.ocean == true) return true;
        return false;
    }

    private static List<City> EngineHomes(Kingdom kingdom)
    {
        var homes = new List<City>(kingdom.cities);
        foreach (Kingdom colony in ColonySystem.ColoniesOf(kingdom)) homes.AddRange(colony.cities);
        homes.RemoveAll(city => city?.data == null || city.isRekt());
        return homes;
    }

    // 灾区、战乱的沿海城：船够得着、有人可运
    private static List<(City city, float amount)> EngineSources(Kingdom kingdom, List<City> homes)
    {
        var sources = new List<(City, float)>();
        foreach (City from in World.world.cities.list)
        {
            if (from?.data == null || from.isRekt() || from.kingdom == null || from.kingdom == kingdom || homes.Contains(from)) continue;
            float distress = Distress(from);
            if (distress <= 1f || Nearest(homes, from, out int distance) == null || distance > SeaTradeReach) continue;
            float people = CityPopulationSystem.CivilianBackground(from);
            if (people < 50f || !Coastal(from)) continue;
            sources.Add((from, people * Mathf.Min(EngineMaxShare, EngineShare * distress)));
        }
        sources.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        return sources;
    }

    // 缺粮数是按月平滑的，饿过一次要很多年才衰减到 0，大多数"缺粮"的城其实缺不到一个人：闹没闹饥荒看人口系统的判定
    private static List<City> EngineLandings(List<City> homes) =>
        homes.Where(city => !CityPopulationSystem.GetGrowthFactors(city, CityPopulationSystem.Get(city)).Famine && !NavySystem.IsBlockaded(city)).ToList();

    // 今年最多花多少船费：只动国库一半，军饷和归化的钱不能全搭进去
    private static float EnginePurse(Kingdom kingdom) => kingdom.GetMoney() * EngineTreasuryShare;

    private static void EngineStart(Kingdom kingdom, TransmigrantState state, int year)
    {
        if (NavySystem.TransportCapacity(kingdom) <= 0f || CorpsSystem.Unpaid(kingdom)) return;
        List<City> homes = EngineHomes(kingdom);
        if (EngineLandings(homes).Count == 0 || EnginePurse(kingdom) / EngineFee < EngineMinPeople) return;
        if (EngineSources(kingdom, homes).Sum(source => source.amount) < EngineMinPeople) return;
        Mark(state, "engine_operation", year);
        state.engine_until_year = year + EngineYears;
        state.engine_moved = 0f;
        TranslateHelper.LogMajor(string.Format(LM.Get("transmigrant_event_engine_operation_history"), kingdom.GetKingdomFullName(),
            RosterName(state, "陈海阳", LM.Get("transmigrant_event_commander_fallback"))), kingdom);
    }

    // 行动期间每年：运力定上限，每人付船费和救济口粮钱，人落到不闹饥荒、还住得下的城。人是搬过来的，全世界总人口不变
    private static void EngineYear(Kingdom kingdom, TransmigrantState state, int year)
    {
        List<City> allHomes = EngineHomes(kingdom);
        List<City> homes = EngineLandings(allHomes);
        float budget = NavySystem.TransportCapacity(kingdom) * EngineTrips;
        float purse = EnginePurse(kingdom), startPurse = purse;
        float moved = 0f;
        string limit = homes.Count == 0 ? "没有能落脚的城" : "没有更多灾民";
        var arrivals = new Dictionary<City, float>();
        if (homes.Count > 0)
            foreach ((City from, float amount) in EngineSources(kingdom, allHomes))
            {
                float ships = budget - moved;
                if (ships < 1f) { limit = "运力"; break; }
                float paid = purse / EngineFee;
                if (paid < 1f) { limit = "船费"; break; }
                City to = homes.Where(city => CityPopulationSystem.GetTotal(city) * EngineCityShare - (arrivals.TryGetValue(city, out float had) ? had : 0f) >= 1f)
                    .OrderBy(city => ZoneDistance(city, from)).FirstOrDefault();
                if (to == null) { limit = "落脚城住满"; break; }
                float room = CityPopulationSystem.GetTotal(to) * EngineCityShare - (arrivals.TryGetValue(to, out float already) ? already : 0f);
                // 船费按真能上岸的人数算：钱不够就少运，不是整批都不走
                float take = Mathf.Min(amount, Mathf.Min(ships, Mathf.Min(paid, room)));
                if (take < amount) limit = take == room ? "落脚城住满" : take == ships ? "运力" : "船费";
                float arrived = CityPopulationSystem.TransferCivilianBackground(from, to, take);
                if (arrived <= 0f) continue;
                int fee = Mathf.CeilToInt(arrived * EngineFee);
                kingdom.SubMoney(fee, TreasuryCategory.Welfare);
                purse -= fee;
                Shelter(to, arrived);
                arrivals[to] = (arrivals.TryGetValue(to, out float sum) ? sum : 0f) + arrived;
                moved += arrived;
            }
        state.engine_moved += moved;
        if (moved >= 1f)
        {
            CityPopulationSystem.InvalidateHouseholdCaches();
            TranslateHelper.LogEventOnly(string.Format(LM.Get("transmigrant_event_engine_year_history"), kingdom.GetKingdomFullName(), Mathf.RoundToInt(moved)), kingdom);
        }
        LogService.LogInfo($"[EmpireCraft][穿越] 发动机行动 {year} 年运回 {Mathf.RoundToInt(moved)} 人，累计 {Mathf.RoundToInt(state.engine_moved)}，到 {state.engine_until_year} 年；止于{limit}(运力 {Mathf.RoundToInt(budget)} 船费 {Mathf.RoundToInt(startPurse)} 落脚城 {homes.Count}/{allHomes.Count})");
        if (year < state.engine_until_year) return;
        state.engine_until_year = -1;
        Mark(state, "engine_end", year);
        TranslateHelper.LogMajor(string.Format(LM.Get("transmigrant_event_engine_end_history"), kingdom.GetKingdomFullName(), Mathf.RoundToInt(state.engine_moved)), kingdom);
    }

    #endregion

    #region 霸王行动

    // 这片海上水师最强、打得过的对手。开战先突袭它的港湾，七成舰船当场报销；打赢了航路更远(见 SeaTradePartners)
    private static void OverlordOperation(Kingdom kingdom, TransmigrantState state, int year)
    {
        int ours = NavySystem.Warships(kingdom);
        if (ours < OverlordMinShips || kingdom.hasEnemies() || CorpsSystem.CannotFundWar(kingdom) || ArmyOf(kingdom) < CampaignMinArmy) return;
        List<City> homes = EngineHomes(kingdom);
        Kingdom target = null;
        int strongest = 0;
        var seen = new HashSet<Kingdom>();
        foreach (City city in World.world.cities.list)
        {
            Kingdom other = city?.kingdom;
            if (other?.data == null || city.isRekt() || other == kingdom || other.isRekt() || !seen.Add(other)) continue;
            int ships = NavySystem.Warships(other);
            if (ships <= strongest || ships > ours * OverlordOdds || other.IsInSameEmpire(kingdom) || ColonySystem.Bound(kingdom, other) ||
                WarGate.Forbidden(kingdom, other) || !WarReach.MayDeclare(kingdom, other, WarTypeLibrary.normal)) continue;
            bool reachable = false;
            foreach (City theirs in other.cities)
                if (Nearest(homes, theirs, out int distance) != null && distance <= SeaTradeReach)
                {
                    reachable = true;
                    break;
                }
            if (!reachable) continue;
            strongest = ships;
            target = other;
        }
        if (target == null) return;
        War war = World.world.diplomacy.startWar(kingdom, target, WarTypeLibrary.normal);
        if (war?.data == null) return;
        WarGoalSystem.Preset(war, WarGoal.夺城);
        war.data.name = LM.Get("transmigrant_event_overlord_operation");
        state.overlord_war_id = war.data.id;
        int sunk = 0;
        foreach (FleetData fleet in NavySystem.Of(target))
        {
            int lost = Mathf.CeilToInt(fleet.warships * OverlordSurprise);
            fleet.warships -= lost;
            sunk += lost;
        }
        Mark(state, "overlord_operation", year);
        TranslateHelper.LogMajor(string.Format(LM.Get("transmigrant_event_overlord_operation_history"), kingdom.GetKingdomFullName(), target.GetKingdomName(), sunk), kingdom);
    }

    #endregion

    #region 太阳伞专案与第三次大会

    // 武林高手潜入都城行刺：元老无一伤亡，归化民死伤，安全部门的漏洞暴露，引出机构改革和第三次全体大会
    private static void SunshadeCase(Kingdom kingdom, TransmigrantState state, int year)
    {
        City capital = kingdom.capital;
        if (capital == null) return;
        float dead = NaturalizationSystem.Casualties(capital, Mathf.Min(SunshadeMaxDead, CityPopulationSystem.GetBackgroundTotal(capital) * SunshadeDeadShare));
        ShiftStability(capital, -SunshadeStability);
        Mark(state, "sunshade_case", year);
        TranslateHelper.LogMajor(string.Format(LM.Get("transmigrant_event_sunshade_case_history"), kingdom.GetKingdomFullName(), capital.GetCityName(),
            Mathf.RoundToInt(dead), RosterName(state, "姬信", LM.Get("transmigrant_event_investigator_fallback"))), kingdom);
    }

    // 第三次全体大会：内阁制在 Milestones 里施行，改选在 Stage 里
    private static void ThirdCongress(Kingdom kingdom, TransmigrantState state, int year)
    {
        Mark(state, "third_congress", year);
        TranslateHelper.LogMajor(string.Format(LM.Get("transmigrant_event_third_congress_history"), kingdom.GetKingdomFullName()), kingdom);
    }

    private static void MainlandCampaign(Kingdom kingdom, TransmigrantState state, int year)
    {
        Mark(state, "mainland_campaign", year);
        state.mainland = true;
        state.next_campaign_at = World.world.getCurWorldTime();
        string lord = state.disguise_lord_id >= 0 && ModClass.EMPIRE_MANAGER?.get(state.disguise_lord_id) is Layer.Empire empire && empire.data != null
            ? empire.GetEmpireFullName() : LM.Get("transmigrant_event_mainland_fallback");
        TranslateHelper.LogMajor(string.Format(LM.Get("transmigrant_event_mainland_campaign_history"), kingdom.GetKingdomFullName(), lord), kingdom);
    }

    #endregion
}

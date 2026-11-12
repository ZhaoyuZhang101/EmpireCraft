using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.Terminal;

// 终端第二期续：暗探、主动降罪、招募新军(群里 2026-11-11 补的要求)。
//   · 暗探——"皇帝可以用自己的钱买暗探去刺探情报""拿回来模糊情报""另一个玩家可以搜寻暗探，然后收买或者怎么样给个假情报"
//     "哪怕没接壤也可以""也不是一次性获取所有的情报""也取决于暗探的能力"。游戏里没有这套东西，整个做在终端这一头：
//     君主拿私财(人物自己的钱，不是国库)雇一名暗探进一国(已知、传闻之国都行)，每半年带回一项密报(库银、兵额、丁口、积弊、城数轮着来)，
//     数是约数、能力越低越不准；每回都可能失陷。对面要是别的席位领着、又在彻查暗探，失陷的暗探落到他手里听候发落：
//     诛杀、收买(放回去接着报，报的都是假的，主人不知情)、纵归。彻查要花库银，管两年
//   · 降罪——君主点一位要人、定一个罪名，交律法论处(量刑照量刑天平走，硬判的代价照旧)
//   · 招募——在本国一座城招一军(走军事页"组建新军"的同一个入口，钱从国库出)
public static partial class TerminalHost
{
    internal sealed class Spy
    {
        public long Id, Target;
        public string TargetName, Last = "";
        public int Skill, Next, Round;
        // 被对面收买了：照常回报，报的是假数
        public bool Turned;
    }

    internal sealed class CaughtSpy
    {
        public long Id;
        public Seat Owner;
        public Spy Spy;
        public string From;
        public float Since;
    }

    private const int MaxSpies = 6, SpyMonths = 6, SweepMonths = 24, SweepShare = 50, MaxNotables = 24, MaxRecruitCities = 20;
    // 一至三等暗探：雇金(君主私财)、密报的出入、每回失陷的机会
    private static readonly int[] SpyCost = { 0, 20, 60, 150 };
    private static readonly double[] SpyError = { 0d, 0.4d, 0.2d, 0.08d }, SpyRisk = { 0d, 0.12d, 0.08d, 0.04d };
    private static readonly string[] SpyItems = { "treasury", "army", "people", "corruption", "cities" };
    // 君主能直接点的罪名(本朝没有这条律的办不了)
    private static readonly LawType[] Charges = { LawType.贪污, LawType.受贿, LawType.滥用职权, LawType.玩忽职守, LawType.私通敌国, LawType.谋反 };

    // 本国的要人：中枢各官职的现任、各城的长官
    private static List<(Actor actor, string post)> Notables(Seat seat)
    {
        var found = new List<(Actor, string)>();
        var seen = new HashSet<long>();
        void Take(Actor actor, string post)
        {
            if (actor == null || !actor.isAlive() || actor == seat.Ruler || found.Count >= MaxNotables || !seen.Add(actor.getID())) return;
            found.Add((actor, post));
        }
        Empire empire = OwnEmpire(seat);
        if (empire != null)
            foreach (OfficeObject office in empire.data.centerOffice?.GetAllOffices(empire) ?? new List<OfficeObject>())
                if (office != null) Take(office.GetActor(), TerminalIntel.Strip(office.GetOfficeName(office.meta_object) ?? ""));
        foreach (City city in seat.Realm.cities ?? new List<City>())
            if (city?.data != null && !city.isRekt()) Take(city.leader, city.data.name);
        return found;
    }

    // 政令清单里这几样：降罪、暗探，以及拿获的暗探(待批)
    private static void AddIntrigue(Seat seat, JArray groups, JArray pending)
    {
        Kingdom realm = seat.Realm;
        int month = MonthNow();

        var people = new JArray();
        foreach ((Actor actor, string post) in Notables(seat))
            people.Add(new JObject { ["id"] = actor.getID().ToString(), ["name"] = actor.getName(), ["post"] = post, ["look"] = Look(actor) });
        var charges = new JArray();
        foreach (LawType charge in Charges)
            charges.Add(new JObject { ["id"] = charge.ToString(), ["name"] = TerminalIntel.Strip(EmpireLawSystem.GetLawName(charge)) });
        groups.Add(new JObject { ["id"] = "law", ["people"] = people, ["charges"] = charges });

        var spies = new JArray();
        foreach (Spy spy in seat.Spies)
            spies.Add(new JObject
            {
                ["id"] = spy.Id.ToString(), ["name"] = spy.TargetName, ["skill"] = spy.Skill, ["left"] = Math.Max(0, spy.Next - month), ["last"] = spy.Last
            });
        var targets = new JArray();
        foreach (long id in seat.Known.Concat(seat.Rumoured))
        {
            Kingdom other = World.world.kingdoms.get(id);
            if (other?.data == null || other.isRekt() || other == realm || targets.Count >= MaxTargets) continue;
            (string tianxia, string inEmpire) = Belong(other);
            targets.Add(new JObject
            {
                ["id"] = id.ToString(), ["name"] = NameOf(other), ["tianxia"] = tianxia, ["empire"] = inEmpire, ["kind"] = Role(other),
                ["cities"] = other.cities?.Count ?? 0, ["has"] = seat.Spies.Any(spy => spy.Target == id)
            });
        }
        groups.Add(new JObject
        {
            ["id"] = "spy", ["purse"] = seat.Ruler.money, ["costs"] = new JArray(SpyCost[1], SpyCost[2], SpyCost[3]), ["spies"] = spies,
            ["targets"] = targets, ["room"] = seat.Spies.Count < MaxSpies, ["sweep"] = Math.Max(0, seat.SweepUntil - month),
            ["sweepShare"] = SweepShare / 10, ["months"] = SpyMonths
        });

        foreach (CaughtSpy held in seat.Caught)
            pending.Add(new JObject
            {
                ["id"] = "caught:" + held.Id, ["kind"] = "spy", ["title"] = string.Format(LM.Get("terminal_caught_title"), held.From),
                ["left"] = Mathf.Max(0, Mathf.RoundToInt((HoldSeconds - (Time.realtimeSinceStartup - held.Since)) / 60f)),
                ["choices"] = new JArray("kill", "turn", "release")
            });
    }

    // 对一国已经探到的几项
    private static JArray Dossier(Seat seat, long kingdomId)
    {
        var lines = new JArray();
        if (seat.Dossiers.TryGetValue(kingdomId, out Dictionary<string, string> known))
            foreach (string item in SpyItems)
                if (known.TryGetValue(item, out string line)) lines.Add(line);
        return lines;
    }

    private static JObject RecruitItem(Kingdom realm)
    {
        var cities = new JArray();
        foreach (City city in (realm.cities ?? new List<City>()).Where(city => city?.data != null && !city.isRekt())
                 .OrderByDescending(city => city == realm.capital).ThenByDescending(city => city.getPopulationPeople()).Take(MaxRecruitCities))
            cities.Add(new JObject { ["id"] = city.getID().ToString(), ["name"] = city.data.name });
        return new JObject { ["id"] = "recruit", ["ok"] = cities.Count > 0, ["cities"] = cities };
    }

    // 当场办的几样(降罪、雇暗探、召回、彻查、发落拿获的暗探)：是这几样就办了并返回真
    private static bool Intrigue(Seat seat, string id, string target, string choice)
    {
        Kingdom realm = seat.Realm;
        int month = MonthNow();
        if (id == "charge")
        {
            if (!long.TryParse(target, out long accusedId) || !Enum.TryParse(choice, out LawType charge) || !Charges.Contains(charge)) return true;
            Actor accused = Notables(seat).Select(person => person.actor).FirstOrDefault(actor => actor.getID() == accusedId);
            if (accused == null) return true;
            string name = accused.getName(), law = TerminalIntel.Strip(EmpireLawSystem.GetLawName(charge));
            LawEnforcementContext context;
            _passCase = true;
            try
            {
                context = EmpireLawSystem.Prosecute(accused, charge, accused.kingdom ?? realm, "terminal_reason_decree", seat.Ruler);
            }
            finally
            {
                _passCase = false;
            }
            if (context == null) Notify(seat, string.Format(LM.Get("terminal_charge_fail"), name, law));
            else
            {
                string sentence = string.Join("、", context.AppliedPunishments.Select(level => EmpireLawSystem.GetPunishmentName(level)));
                Notify(seat, string.Format(LM.Get("terminal_charge_done"), name, law, sentence.Length > 0 ? sentence : LM.Get("terminal_charge_none")));
            }
            return true;
        }
        if (id == "spy.hire")
        {
            if (!long.TryParse(target, out long kingdomId) || !int.TryParse(choice, out int skill) || skill < 1 || skill > 3) return true;
            Kingdom other = World.world.kingdoms.get(kingdomId);
            if (other?.data == null || other.isRekt() || other == realm || !(seat.Known.Contains(kingdomId) || seat.Rumoured.Contains(kingdomId)) ||
                seat.Spies.Count >= MaxSpies || seat.Spies.Any(spy => spy.Target == kingdomId)) return false;
            if (seat.Ruler.money < SpyCost[skill])
            {
                Notify(seat, LM.Get("terminal_spy_poor"));
                return true;
            }
            seat.Ruler.spendMoney(SpyCost[skill]);
            seat.Spies.Add(new Spy { Id = ++_seq, Target = kingdomId, TargetName = NameOf(other), Skill = skill, Next = month + SpyMonths });
            Notify(seat, string.Format(LM.Get("terminal_spy_sent"), NameOf(other)));
            return true;
        }
        if (id == "spy.recall")
        {
            Spy spy = seat.Spies.FirstOrDefault(found => found.Id.ToString() == target);
            if (spy == null) return false;
            seat.Spies.Remove(spy);
            Notify(seat, string.Format(LM.Get("terminal_spy_recalled"), spy.TargetName));
            return true;
        }
        if (id == "sweep")
        {
            long cost = Math.Max(0L, realm.GetTreasuryBalance()) * SweepShare / 1000;
            if (cost <= 0)
            {
                Notify(seat, LM.Get("terminal_sweep_broke"));
                return true;
            }
            realm.AddMoney(-(int)Math.Min(int.MaxValue, cost));
            seat.SweepUntil = month + SweepMonths;
            Notify(seat, LM.Get("terminal_sweep_on"));
            return true;
        }
        if (id.StartsWith("caught:", StringComparison.Ordinal))
        {
            CaughtSpy held = seat.Caught.FirstOrDefault(found => "caught:" + found.Id == id);
            if (held == null || (choice != "kill" && choice != "turn" && choice != "release")) return false;
            Dispose(seat, held, choice);
            return true;
        }
        return false;
    }

    // 发落拿获的暗探：诛杀(主人只知道失陷了)、收买(放回去报假数，主人不知情)、纵归(放回去照常报)
    private static void Dispose(Seat seat, CaughtSpy held, string choice)
    {
        seat.Caught.Remove(held);
        seat.NextDecrees = seat.NextState = 0f;
        Seat owner = held.Owner;
        bool ownerThere = owner?.Ruler != null && SeatByToken.ContainsKey(owner.Token);
        if (choice == "kill" || !ownerThere)
        {
            if (ownerThere) Notify(owner, string.Format(LM.Get("terminal_spy_lost"), held.Spy.TargetName));
            Notify(seat, string.Format(LM.Get("terminal_caught_kill"), held.From));
            return;
        }
        held.Spy.Turned = choice == "turn";
        held.Spy.Next = MonthNow() + SpyMonths;
        owner.Spies.Add(held.Spy);
        Notify(seat, string.Format(LM.Get("terminal_caught_" + choice), held.From));
    }

    // 每秒：到了日子的暗探回一项密报，或者失陷；拿获的暗探搁太久照诛杀办
    private static void RunSpies(Seat seat)
    {
        for (int i = seat.Caught.Count - 1; i >= 0; i--)
            if (Time.realtimeSinceStartup - seat.Caught[i].Since >= HoldSeconds) Dispose(seat, seat.Caught[i], "kill");
        int month = MonthNow();
        for (int i = seat.Spies.Count - 1; i >= 0; i--)
        {
            Spy spy = seat.Spies[i];
            if (spy.Next > month) continue;
            Kingdom other = World.world.kingdoms.get(spy.Target);
            if (other?.data == null || other.isRekt())
            {
                seat.Spies.RemoveAt(i);
                Notify(seat, string.Format(LM.Get("terminal_spy_gone"), spy.TargetName));
                continue;
            }
            spy.Round++;
            spy.Next = month + SpyMonths;
            uint seed = TerminalIntel.Hash(spy.Id, spy.Round, month);
            // 那一国要是别的席位领着、又在彻查，失陷的机会三倍，人落到他手里
            Seat host = SeatByToken.Values.FirstOrDefault(found => found != seat && found.Ruler != null && found.Realm == other);
            bool swept = host != null && host.SweepUntil > month;
            if (!spy.Turned && TerminalIntel.Unit(seed) < SpyRisk[spy.Skill] * (swept ? 3d : 1d))
            {
                seat.Spies.RemoveAt(i);
                if (swept)
                {
                    host.Caught.Add(new CaughtSpy { Id = ++_seq, Owner = seat, Spy = spy, From = NameOf(seat.Realm), Since = Time.realtimeSinceStartup });
                    host.NextDecrees = 0f;
                    Notify(host, string.Format(LM.Get("terminal_caught"), NameOf(seat.Realm)));
                }
                else Notify(seat, string.Format(LM.Get("terminal_spy_lost"), spy.TargetName));
                continue;
            }
            string item = SpyItems[(spy.Round - 1) % SpyItems.Length];
            long truth = item switch
            {
                "treasury" => other.GetTreasuryBalance(),
                "army" => other.countTotalWarriors(),
                "people" => other.getPopulationPeople(),
                "corruption" => (long)Math.Round(CorruptionSystem.GetRate(other) * 100d),
                _ => other.cities?.Count ?? 0
            };
            double roll = TerminalIntel.Unit(TerminalIntel.Hash(spy.Id, spy.Round, 99));
            // 被收买的报假数(四成到两倍半之间乱报)；没被收买的按能力有出入。城数、积弊不取整到太粗
            double factor = spy.Turned ? 0.4d + roll * 2.1d : 1d + (roll * 2d - 1d) * SpyError[spy.Skill];
            long shown = (long)Math.Round(truth * factor);
            if (item == "treasury" || item == "army" || item == "people") shown = TerminalIntel.RoundSig(shown, spy.Skill >= 3 ? 2 : 1);
            else if (item == "corruption") shown = Math.Max(0, Math.Min(100, shown));
            else shown = Math.Max(1, shown);
            string line = string.Format(LM.Get("terminal_spy_" + item), NameOf(other), shown);
            spy.Last = line;
            if (!seat.Dossiers.TryGetValue(spy.Target, out Dictionary<string, string> known)) seat.Dossiers[spy.Target] = known = new Dictionary<string, string>();
            known[item] = string.Format(LM.Get("terminal_spy_dated"), line, Date.getCurrentYear());
            Notify(seat, line, "spy");
        }
    }
}

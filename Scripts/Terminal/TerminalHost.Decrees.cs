using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.AI;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using NeoModLoader.General;
using NeoModLoader.services;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.Terminal;

// 终端第二期：下命令(用户 2026-11-11 "现实中统治者能看到的获取到的信息能办到的事情都可以办到"
// "但是都只能做简单的命令，剩下的让下面的人去办")。君主不是神，所以——
//   · 命令"只迟不改"：下了照原样办，但要过些日子才办到(都城里的事一月，遣使按远近)，办结回一条奏报；
//   · 外交要对方答应：游戏里的外交动作都是强写(神力口径，见 DiplomacyActions.Do)，这里先按对方的好感判答不答应；
//     议和走议和台，两边都肯才签(PeaceTable.Sign)；
//   · 朝议照原样走：上奏只是替某一派把奏议递上去，之后票拟、廷议、批红照旧(正统越高批红越不看廷议)；
//     廷议毕该批红时，奏议停下来等手机批(准、驳、留中)，过 HoldSeconds 没批朝廷自决；
//   · 改元没有对应的诉求，按君主权威掷一回(权威越高越易成：成算 = 两成 + 七成 × 权威)；
//   · 称帝、立储接现成的入口，前提照旧。
// 还没做的：定罪待批、禅让、改国号改名、暗探、亲征私访
public static partial class TerminalHost
{
    // 待批的事等手机多久(现实秒)：过了朝廷自决
    private const float HoldSeconds = 1800f, DecreeSeconds = 10f;
    private const int MaxOrders = 40, MaxClaims = 30, MaxTargets = 40;

    internal sealed class Order
    {
        public long Id;
        public string Kind, Arg, Text, State = "sent", Note = "";
        public long Target;
        public double At;
        public int Due;
        // 走天平的外交带的条件(条件的编号用逗号连着，见 TerminalHost.Scale.cs)
        public string Extra = "";
    }

    // 开放给终端的外交动作(DiplomacyActions 里 key 的结尾) → 对方的好感至少多少才答应；int.MinValue = 单方面的事，不用对方答应
    private static readonly Dictionary<string, int> DiplomacyNeeds = new()
    {
        ["declare"] = int.MinValue, ["peace"] = int.MinValue, ["ally"] = 30, ["leave_alliance"] = int.MinValue, ["marry"] = 0,
        ["pact"] = 10, ["opinion_up"] = int.MinValue
    };

    private static string Suffix(string key) => key != null && key.StartsWith("diplomacy_act_", StringComparison.Ordinal)
        ? key.Substring("diplomacy_act_".Length) : key ?? "";

    private static IEnumerable<DiplomacyActions.Action> OpenActions() =>
        DiplomacyActions.Groups.SelectMany(group => group.actions).Where(action => DiplomacyNeeds.ContainsKey(Suffix(action.key)));

    private static string Words(string keyOrText) =>
        string.IsNullOrEmpty(keyOrText) ? "" : LM.Has(keyOrText) ? TerminalIntel.Strip(LM.Get(keyOrText)) : TerminalIntel.Strip(keyOrText);

    private static int Authority(Kingdom realm)
    {
        Empire empire = realm.GetEmpire();
        return empire != null && empire.CoreKingdom == realm ? empire.Legitimacy : RealmAuthority.Get(realm);
    }

    private static float EraChance(Kingdom realm) => Mathf.Clamp(0.2f + 0.7f * Authority(realm) / 100f, 0.05f, 0.95f);

    // 本国是帝国的中央、且这位君主就是皇帝
    private static Empire OwnEmpire(Seat seat)
    {
        Empire empire = seat.Realm?.GetEmpire();
        return empire != null && empire.CoreKingdom == seat.Realm && empire.Emperor == seat.Ruler ? empire : null;
    }

    // ───────── 待批：廷议已毕的奏议 ─────────
    // 朝议每月推进到批红时问这里(TerminalPatch 拦 CourtSystem.Decide)：这位皇帝被席位领着、亲自批红、还没等过头，就先不批
    public static bool HoldsMemorial(Empire empire)
    {
        if (!Enabled || !_anyRuler || empire?.CoreKingdom == null) return false;
        CourtMemorial memorial = CourtSystem.State(empire)?.memorial;
        if (memorial == null) return false;
        foreach (Seat seat in SeatByToken.Values)
        {
            if (seat.Ruler == null || seat.Realm != empire.CoreKingdom || !RulesInPerson(seat, empire)) continue;
            if (seat.Delegated || seat.Away != null) return false;
            string mark = memorial.claim + "@" + memorial.stage_time;
            if (seat.HoldMark != mark)
            {
                seat.HoldMark = mark;
                seat.HoldSince = Time.realtimeSinceStartup;
                seat.NextDecrees = seat.NextState = 0f;
            }
            return Time.realtimeSinceStartup - seat.HoldSince < HoldSeconds;
        }
        return false;
    }

    private static bool RulesInPerson(Seat seat, Empire empire)
    {
        (Actor decider, string role) = CourtSystem.Decider(empire);
        return role == "emperor" && decider == seat.Ruler;
    }

    // ───────── 给手机的清单 ─────────
    private static JObject Decrees(Seat seat)
    {
        Kingdom realm = seat.Realm;
        var groups = new JArray();
        var pending = new JArray();
        Empire empire = OwnEmpire(seat);

        // 朝议
        if (empire != null && CourtSystem.Applies(empire))
        {
            CourtMemorial memorial = CourtSystem.State(empire)?.memorial;
            if (memorial != null && memorial.stage == 2 && RulesInPerson(seat, empire) && seat.HoldMark == memorial.claim + "@" + memorial.stage_time)
            {
                var claim = CourtSystem.FindClaim(empire, memorial);
                var proposer = CourtSystem.FindFaction(empire, memorial.proposer);
                var item = new JObject
                {
                    ["id"] = "memorial", ["kind"] = "memorial", ["title"] = TerminalIntel.Strip(CourtSystem.IssueName(memorial.claim, empire)),
                    ["left"] = Mathf.Max(0, Mathf.RoundToInt((HoldSeconds - (Time.realtimeSinceStartup - seat.HoldSince)) / 60f)),
                    ["choices"] = new JArray("approve", "reject", "shelve")
                };
                if (memorial.support >= 0f) item["support"] = Mathf.RoundToInt(memorial.support * (memorial.support <= 1f ? 100f : 1f));
                if (claim != null && proposer != null)
                {
                    item["chance"] = Mathf.RoundToInt(CourtSystem.ApprovalChance(empire, memorial, claim.type, proposer) * 100f);
                    // 各派表态，和还能对它用的筹码
                    var sides = new JArray();
                    foreach ((FixedFaction faction, int stance, float weight) in CourtSystem.DebateStances(empire, claim, proposer, memorial))
                    {
                        string factionId = faction.GetID();
                        var chips = new JArray();
                        if (faction != proposer && stance < 1)
                            foreach (string kind in ChipKinds)
                                if (!seat.Chips.Contains(seat.HoldMark + "|" + factionId + "|" + kind)) chips.Add(kind);
                        sides.Add(new JObject
                        {
                            ["id"] = factionId, ["name"] = TerminalIntel.Strip(faction.Name ?? ""), ["stance"] = stance,
                            ["weight"] = Math.Round(weight, 1), ["favor"] = Mathf.RoundToInt(CourtSystem.FactionState(empire, faction).favor),
                            ["own"] = faction == proposer, ["chips"] = chips
                        });
                    }
                    item["sides"] = sides;
                }
                pending.Add(item);
            }
            var court = new JArray();
            if (memorial == null && empire.RunningTemporaryFaction?.IsStarted() != true)
            {
                var seen = new HashSet<string>();
                foreach (var faction in CourtSystem.Factions(empire))
                foreach (var claim in faction.TemporaryFactions)
                {
                    if (court.Count >= MaxClaims) break;
                    string type = claim.type.ToString();
                    if (!seen.Add(type) || CourtSystem.CannotGodPropose(empire, faction, claim) != null) continue;
                    court.Add(new JObject { ["id"] = "court:" + type, ["name"] = TerminalIntel.Strip(CourtSystem.IssueName(claim.type, empire)) });
                }
            }
            groups.Add(new JObject { ["id"] = "court", ["busy"] = memorial != null, ["items"] = court });
        }

        // 人事：中枢官职任免、立首辅、委首辅代批(用户 2026-11-11 "皇帝可以封小人虚官，让某个人升值，成为自己的首辅，替自己批阅奏折")
        if (empire != null)
        {
            var offices = new JArray();
            List<OfficeObject> all = empire.data.centerOffice?.GetAllOffices(empire) ?? new List<OfficeObject>();
            for (int i = 0; i < all.Count && offices.Count < MaxOffices; i++)
            {
                if (all[i] == null) continue;
                Actor holder = all[i].GetActor();
                var office = new JObject { ["id"] = i.ToString(), ["name"] = TerminalIntel.Strip(all[i].GetOfficeName(all[i].meta_object) ?? "") };
                if (holder != null && holder.isAlive()) office["holder"] = holder.getName();
                offices.Add(office);
            }
            var people = new JArray();
            foreach (Actor candidate in CourtSystem.CabinetCandidates(empire).Take(MaxCandidates))
            {
                var person = new JObject { ["id"] = candidate.getID().ToString(), ["name"] = candidate.getName(), ["age"] = candidate.getAge() };
                FixedFaction side = FactionOf(empire, candidate);
                if (side != null) person["faction"] = TerminalIntel.Strip(side.Name ?? "");
                people.Add(person);
            }
            var staff = new JObject { ["id"] = "staff", ["offices"] = offices, ["people"] = people, ["delegated"] = seat.Delegated };
            Actor chief = CourtSystem.AgendaSetter(empire);
            if (chief != null && chief.isAlive()) staff["chief"] = chief.getName();
            groups.Add(staff);
        }

        foreach (Case held in seat.Cases)
            pending.Add(new JObject
            {
                ["id"] = "case:" + held.Id, ["kind"] = "case", ["title"] = held.Name + " · " + held.LawName, ["detail"] = held.Reason,
                ["left"] = Mathf.Max(0, Mathf.RoundToInt((HoldSeconds - (Time.realtimeSinceStartup - held.Since)) / 60f)),
                ["choices"] = new JArray("law", "redeem", "death")
            });

        // 宫廷
        var palace = new JArray();
        Empire anyEmpire = realm.GetEmpire();
        if (empire != null)
        {
            bool allowed = empire.IsAllowToMakeYearName();
            palace.Add(new JObject { ["id"] = "era", ["ok"] = allowed, ["chance"] = Mathf.RoundToInt(EraChance(realm) * 100f) });
        }
        if (anyEmpire == null)
        {
            bool can = EmpireCraftKingdomBehCheckEmpire.CanStartEmpireFormation(realm);
            var item = new JObject { ["id"] = "empire", ["ok"] = can };
            if (!can) item["why"] = Words(EmpireFormationService.Diagnose(realm));
            palace.Add(item);
        }
        var heirs = new JArray();
        Actor heir = realm.GetHeir();
        foreach (Actor candidate in EmpireCraftKingdomBehCheckHeir.HeirCandidates(realm) ?? new List<Actor>())
        {
            if (candidate == null || !candidate.isAlive() || heirs.Count >= 12) continue;
            heirs.Add(new JObject
            {
                ["id"] = candidate.getID().ToString(), ["name"] = candidate.getName(), ["age"] = candidate.getAge(), ["now"] = candidate == heir
            });
        }
        var heirItem = new JObject { ["id"] = "heir", ["ok"] = heirs.Count > 0, ["targets"] = heirs };
        if (heir != null && heir.isAlive()) heirItem["current"] = heir.getName();
        // 君位世袭的才谈得上立储、禅让；流官的位子是朝廷的。国号也只有一国之主改得了
        bool hereditary = Hereditary(realm);
        if (hereditary)
        {
            palace.Add(heirItem);
            palace.Add(new JObject { ["id"] = "abdicate", ["ok"] = heirs.Count > 0, ["targets"] = heirs.DeepClone() });
        }
        if (realm.GetEmpire() == null || empire != null) palace.Add(new JObject
        {
            ["id"] = "rename_realm", ["ok"] = true, ["text"] = true, ["current"] = realm.data.name,
            ["chance"] = Mathf.RoundToInt(ScaleChance(realm, RealmNameBase, RealmNameSlope) * 100f)
        });
        palace.Add(new JObject
        {
            ["id"] = "rename_ruler", ["ok"] = true, ["text"] = true, ["current"] = seat.Ruler.getName(),
            ["chance"] = Mathf.RoundToInt(ScaleChance(realm, RulerNameBase, RulerNameSlope) * 100f)
        });
        AddJourney(seat, palace);
        groups.Add(new JObject { ["id"] = "palace", ["items"] = palace });

        // 内政：拨库银整饬吏治(用户 2026-11-11 "玩家可以设计如果花掉库银子用于减少积弊")
        var tiers = new JArray();
        for (int i = 0; i < PurgeShare.Length; i++)
            tiers.Add(new JObject { ["id"] = (i + 1).ToString(), ["share"] = PurgeShare[i] / 10, ["drop"] = Mathf.RoundToInt((float)PurgeDrop[i] * 100f) });
        groups.Add(new JObject
        {
            ["id"] = "home", ["items"] = new JArray(new JObject { ["id"] = "purge", ["ok"] = realm.GetTreasuryBalance() > 0, ["tiers"] = tiers },
                RecruitItem(realm))
        });

        // 外交：已知之国各能办什么
        var actions = OpenActions().ToList();
        var names = new JObject();
        foreach (DiplomacyActions.Action action in actions) names[Suffix(action.key)] = Words(action.key);
        var realms = new JArray();
        foreach (long id in seat.Known)
        {
            if (realms.Count >= MaxTargets) break;
            Kingdom other = World.world.kingdoms.get(id);
            if (other?.data == null || other.isRekt() || other == realm) continue;
            var can = new JArray();
            var cannot = new JObject();
            foreach (DiplomacyActions.Action action in actions)
            {
                // 对面是真人：结盟不看游戏里的条件(他应允就成)；有盟约在，随时可以背盟
                string suffix = Suffix(action.key);
                bool free = suffix == "ally" && HostOf(other, seat) != null && !Allied(realm, other) || suffix == "leave_alliance" && Allied(realm, other);
                string reason = free ? null : DiplomacyActions.Check(action, realm, other);
                if (reason == null) can.Add(Suffix(action.key));
                else cannot[Suffix(action.key)] = Words(reason);
            }
            (string tianxia, string inEmpire) = Belong(other);
            realms.Add(new JObject
            {
                ["id"] = id.ToString(), ["name"] = other.data.name, ["opinion"] = Opinion(other, realm), ["can"] = can, ["cannot"] = cannot,
                ["months"] = EnvoyMonths(seat, other), ["tianxia"] = tianxia, ["empire"] = inEmpire, ["kind"] = Role(other),
                ["cities"] = other.cities?.Count ?? 0, ["dossier"] = Dossier(seat, id),
                ["scale"] = ScaleJson(seat, other), ["pact"] = Allied(realm, other)
            });
        }
        groups.Add(new JObject
        {
            ["id"] = "diplomacy", ["names"] = names, ["realms"] = realms,
            ["consent"] = new JArray(ScaleDeals.Keys.ToArray())
        });

        // 附庸博弈与招安(见 TerminalHost.Vassal.cs)
        AddVassals(seat, groups);
        AddOffers(seat, pending);
        AddIntrigue(seat, groups, pending);

        var orders = new JArray();
        for (int i = seat.Orders.Count - 1; i >= 0; i--)
        {
            Order order = seat.Orders[i];
            orders.Add(new JObject
            {
                ["id"] = order.Id, ["text"] = order.Text, ["state"] = order.State, ["note"] = order.Note,
                ["year"] = Date.getYear(order.At), ["month"] = Date.getMonth(order.At), ["left"] = Math.Max(0, order.Due - MonthNow())
            });
        }
        return new JObject { ["groups"] = groups, ["pending"] = pending, ["orders"] = orders, ["home"] = HomeMonths(seat) };
    }

    private static int Opinion(Kingdom from, Kingdom about)
    {
        try
        {
            return World.world.diplomacy.getOpinion(from, about).total;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    // 都城里办的事要几个月、遣使来回要几个月(有电报就当月)
    private static int HomeMonths(Seat seat) => seat.Sight == TerminalIntel.Sight.World ? 0 : 1;

    private static int EnvoyMonths(Seat seat, Kingdom other)
    {
        if (seat.Sight == TerminalIntel.Sight.World) return 0;
        return ConnectivitySystem.CanContact(seat.Realm, other) == false ? 6 : 3;
    }

    // ───────── 下令(网络线程排进队列，主线程办) ─────────
    // 手机发来一道命令。受理不受理都得有回音(用户 2026-11-11 进游戏后："为什么修官道点了没反应")：
    // 受理的进"已下政令"或当场回一条奏报；条件变了、办不了的回一条"未受理"
    private static void Issue(Seat seat, string id, string target, string choice)
    {
        int before = seat.Acks;
        IssueCore(seat, id, target, choice);
        if (seat.Acks == before && seat.Ruler != null && WorldReady()) Notify(seat, LM.Get("terminal_order_rejected"));
    }

    private static void IssueCore(Seat seat, string id, string target, string choice)
    {
        if (seat.Ruler == null || !WorldReady() || string.IsNullOrEmpty(id)) return;
        Kingdom realm = seat.Realm;
        seat.NextDecrees = seat.NextState = 0f;
        if (id == "speed_propose" || id == "speed_vote")
        {
            // 流速之议(见 TerminalHost.Speed)
            SpeedDecree(seat, id, target, choice);
            return;
        }
        if (id == "memorial")
        {
            // 批红：皇帝就在宫里，当场生效
            Empire empire = OwnEmpire(seat);
            if (empire == null || !RulesInPerson(seat, empire) || (choice != "approve" && choice != "reject" && choice != "shelve")) return;
            CourtMemorial memorial = CourtSystem.State(empire)?.memorial;
            if (memorial == null || memorial.stage != 2) return;
            string name = TerminalIntel.Strip(CourtSystem.IssueName(memorial.claim, empire));
            if (CourtSystem.GodDecide(empire, choice))
                Notify(seat, string.Format(LM.Get("terminal_decided_" + choice), name));
            return;
        }
        if (id == "chip")
        {
            Empire court = OwnEmpire(seat);
            if (court != null && target != null && ChipKinds.Contains(choice)) UseChip(seat, court, target, choice);
            return;
        }
        if (id == "delegate")
        {
            if (OwnEmpire(seat) == null) return;
            seat.Delegated = choice == "on";
            seat.DelegateMonth = MonthNow();
            Notify(seat, LM.Get(seat.Delegated ? "terminal_delegate_on" : "terminal_delegate_off"));
            return;
        }
        if (id.StartsWith("offer:", StringComparison.Ordinal))
        {
            Offer offer = seat.Offers.FirstOrDefault(found => "offer:" + found.Id == id);
            if (offer != null && (choice == "accept" || choice == "refuse")) Answer(seat, offer, choice == "accept");
            return;
        }
        if (Journey(seat, id, target)) return;
        if (FieldDecree(seat, id, target, choice)) return;
        if (Intrigue(seat, id, target, choice)) return;
        if (id.StartsWith("case:", StringComparison.Ordinal))
        {
            Case held = seat.Cases.FirstOrDefault(c => "case:" + c.Id == id);
            if (held != null && (choice == "law" || choice == "redeem" || choice == "death")) Judge(seat, held, choice);
            return;
        }
        if (seat.Orders.Count(order => order.State == "sent") >= 12) return;
        var made = new Order { Id = ++_seq, At = World.world.getCurWorldTime(), Due = MonthNow() + HomeMonths(seat) };
        if (id.StartsWith("court:", StringComparison.Ordinal))
        {
            made.Kind = "court";
            made.Arg = id.Substring(6);
            Empire empire = OwnEmpire(seat);
            var found = empire == null ? default : FindProposal(empire, made.Arg);
            if (found.claim == null) return;
            made.Text = string.Format(LM.Get("terminal_order_court"), TerminalIntel.Strip(CourtSystem.IssueName(found.claim.type, empire)));
        }
        else if (id == "era" && OwnEmpire(seat) != null)
        {
            made.Kind = "era";
            made.Text = LM.Get("terminal_order_era");
        }
        else if (id == "empire" && realm.GetEmpire() == null)
        {
            made.Kind = "empire";
            made.Text = LM.Get("terminal_order_empire");
        }
        else if (id == "heir" && Hereditary(realm) && long.TryParse(target, out long heirId))
        {
            Actor heir = World.world.units.get(heirId);
            if (heir == null || !heir.isAlive()) return;
            made.Kind = "heir";
            made.Target = heirId;
            made.Text = string.Format(LM.Get("terminal_order_heir"), heir.getName());
        }
        else if (id == "abdicate" && Hereditary(realm) && long.TryParse(target, out long successorId))
        {
            Actor successor = World.world.units.get(successorId);
            if (successor == null || !successor.isAlive() || successor == seat.Ruler) return;
            made.Kind = "abdicate";
            made.Target = successorId;
            made.Text = string.Format(LM.Get("terminal_order_abdicate"), successor.getName());
        }
        else if (id == "rename_ruler" || id == "rename_realm" && (realm.GetEmpire() == null || OwnEmpire(seat) != null))
        {
            string name = CleanText(choice);
            if (name == null) return;
            made.Kind = id;
            made.Arg = name;
            made.Text = string.Format(LM.Get("terminal_order_" + id), name);
        }
        else if (id == "purge" && (choice == "1" || choice == "2" || choice == "3"))
        {
            made.Kind = "purge";
            made.Arg = choice;
            made.Text = string.Format(LM.Get("terminal_order_purge"), PurgeShare[int.Parse(choice) - 1] / 10);
        }
        else if ((id == "appoint" || id == "dismiss" || id == "chief") && OwnEmpire(seat) != null && target != null)
        {
            Empire court = OwnEmpire(seat);
            string[] parts = target.Split(':');
            Actor person = null;
            if (id != "dismiss")
            {
                if (!long.TryParse(parts[parts.Length - 1], out long personId)) return;
                person = World.world.units.get(personId);
                if (person == null || !person.isAlive()) return;
                made.Target = personId;
            }
            made.Kind = id;
            if (id == "chief") made.Text = string.Format(LM.Get("terminal_order_chief"), person.getName());
            else
            {
                List<OfficeObject> all = court.data.centerOffice?.GetAllOffices(court);
                if (all == null || !int.TryParse(parts[0], out int slot) || slot < 0 || slot >= all.Count || all[slot] == null) return;
                made.Arg = slot.ToString();
                string office = TerminalIntel.Strip(all[slot].GetOfficeName(all[slot].meta_object) ?? "");
                made.Text = id == "appoint" ? string.Format(LM.Get("terminal_order_appoint"), person.getName(), office)
                    : string.Format(LM.Get("terminal_order_dismiss"), office);
            }
        }
        else if (id == "recruit" && long.TryParse(target, out long homeId))
        {
            City home = World.world.cities.get(homeId);
            if (home?.data == null || home.isRekt() || home.kingdom != realm) return;
            made.Kind = "recruit";
            made.Target = homeId;
            made.Text = string.Format(LM.Get("terminal_order_recruit"), home.data.name);
        }
        else if (id == "road" && long.TryParse(target, out long cityId))
        {
            City city = World.world.cities.get(cityId);
            if (city?.data == null || city.isRekt() || city.kingdom != realm || city == realm.capital) return;
            made.Kind = "road";
            made.Target = cityId;
            made.Due = MonthNow() + RoadMonths;
            made.Text = string.Format(LM.Get("terminal_order_road"), city.data.name);
        }
        else if (id.StartsWith("diplo:", StringComparison.Ordinal) && long.TryParse(target, out long otherId))
        {
            Kingdom other = World.world.kingdoms.get(otherId);
            DiplomacyActions.Action action = OpenActions().FirstOrDefault(a => Suffix(a.key) == id.Substring(6));
            if (other?.data == null || other.isRekt() || action == null || !seat.Known.Contains(otherId)) return;
            made.Kind = "diplo";
            made.Arg = Suffix(action.key);
            made.Target = otherId;
            made.Due = MonthNow() + EnvoyMonths(seat, other);
            // 走天平的事(结盟、和亲、通好)带上摆在台上的条件
            if (ScaleDeals.ContainsKey(made.Arg)) made.Extra = string.Join(",", ParseTerms(choice, realm, other).Select(term => term.Id));
            string terms = made.Extra.Length > 0 ? Describe(ParseTerms(made.Extra, realm, other)) : "";
            made.Text = string.Format(LM.Get("terminal_order_diplo"), other.data.name, Words(action.key)) + (terms.Length > 0 ? "（" + terms + "）" : "");
        }
        else if (id == "vassal")
        {
            if (!VassalOrder(seat, made, target, choice)) return;
        }
        else return;
        // 同一道令已经在办，不再下第二遍
        if (seat.Orders.Any(order => order.State == "sent" && order.Kind == made.Kind && order.Target == made.Target && order.Arg == made.Arg))
        {
            Notify(seat, string.Format(LM.Get("terminal_order_dup"), made.Text));
            return;
        }
        seat.Orders.Add(made);
        seat.Acks++;
        if (seat.Orders.Count > MaxOrders) seat.Orders.RemoveAt(0);
    }

    private static (FixedFaction faction, TemporaryFaction claim) FindProposal(Empire empire, string type)
    {
        foreach (FixedFaction faction in CourtSystem.Factions(empire))
        foreach (TemporaryFaction claim in faction.TemporaryFactions)
            if (claim.type.ToString() == type && CourtSystem.CannotGodPropose(empire, faction, claim) == null)
                return (faction, claim);
        return default;
    }

    // 办结(成不成都算)直接回一条奏报，不再另算迟报：来回的日子已经算在命令里
    private static void Notify(Seat seat, string text, string kind = "order")
    {
        seat.Acks++;
        seat.Reports.Add(new Report { Id = ++_seq, Text = text, Kind = kind, At = World.world.getCurWorldTime(), Late = 0, Own = true });
        if (seat.Reports.Count > MaxReports) seat.Reports.RemoveRange(0, seat.Reports.Count - MaxReports);
        seat.NextState = seat.NextDecrees = 0f;
    }

    // 每秒：到了日子的命令办掉
    private static void RunOrders(Seat seat)
    {
        int month = MonthNow();
        // 委首辅代批的代价：首辅那一派的政治资本月月见长(容易被架空)
        if (seat.Delegated && month != seat.DelegateMonth)
        {
            seat.DelegateMonth = month;
            Empire court = OwnEmpire(seat);
            Actor chief = court == null ? null : CourtSystem.AgendaSetter(court);
            FixedFaction side = chief == null ? null : FactionOf(court, chief);
            if (side != null) CourtSystem.AdjustFaction(court, side, "capital", DelegateCapital);
        }
        for (int index = 0; index < seat.Orders.Count; index++)
        {
            Order order = seat.Orders[index];
            if (order.State != "sent" || order.Due > month) continue;
            string failure;
            try
            {
                failure = Execute(seat, order);
            }
            catch (Exception exception)
            {
                failure = LM.Get("terminal_fail_generic");
                LogService.LogWarning($"[EmpireCraft][终端] 政令 {order.Kind} 没办成: {exception.Message}");
            }
            if (failure == Waiting)
            {
                // 提议递到对面真人手里了：这道令算送到，成不成等他回话
                order.State = "done";
                order.Note = LM.Get("terminal_offer_waiting_note");
                Notify(seat, string.Format(LM.Get("terminal_offer_waiting"), order.Text));
                continue;
            }
            order.State = failure == null ? "done" : "failed";
            order.Note = failure ?? "";
            Notify(seat, failure == null
                ? string.Format(LM.Get("terminal_order_done"), order.Text)
                : string.Format(LM.Get("terminal_order_failed"), order.Text, failure));
        }
        // 禅让办成了：这位已不在位，席位空出来等着再挑(用户："跟君主本人，可以立嗣或者禅让")
        if (seat.Abdicated)
        {
            seat.Abdicated = false;
            seat.Vacate("abdicated");
        }
    }

    // ───────── 筹码 ─────────
    // (用户 2026-11-11 "可以用筹码，比如封官，比如人情")奏议等批红时，对还没赞成的一派用一样筹码，它的表态进一档
    // (反对→观望→赞成；写进奏议的表态覆盖 stance_overrides，廷议赞成率跟着重算)。每派每件奏议每样只用一回：
    //   grant 授它的领袖一个虚衔(圣眷另 +5)；favor 动用情分(圣眷 −15，不足 20 用不了)；silver 赏银(库银 3%，它的政治资本 +5)
    private static readonly string[] ChipKinds = { "grant", "favor", "silver" };
    private const int MaxOffices = 24, MaxCandidates = 16;
    private const float DelegateCapital = 1f;

    private static FixedFaction FactionOf(Empire empire, Actor actor)
    {
        long id = actor.getID();
        return CourtSystem.Factions(empire).FirstOrDefault(faction => faction.Members != null && faction.Members.Contains(id));
    }

    private static void UseChip(Seat seat, Empire empire, string factionId, string kind)
    {
        CourtMemorial memorial = CourtSystem.State(empire)?.memorial;
        if (memorial == null || memorial.stage != 2 || !RulesInPerson(seat, empire)) return;
        FixedFaction faction = CourtSystem.FindFaction(empire, factionId), proposer = CourtSystem.FindFaction(empire, memorial.proposer);
        TemporaryFaction claim = CourtSystem.FindClaim(empire, memorial);
        if (faction == null || proposer == null || claim == null || faction == proposer) return;
        int stance = CourtSystem.DebateStances(empire, claim, proposer, memorial).Where(side => side.faction == faction).Select(side => side.stance)
            .DefaultIfEmpty(1).First();
        string mark = seat.HoldMark + "|" + factionId + "|" + kind;
        if (stance >= 1 || !seat.Chips.Add(mark)) return;
        string name = TerminalIntel.Strip(faction.Name ?? "");
        bool worked = false;
        switch (kind)
        {
            case "grant":
            {
                Actor leader = faction.GetLeader();
                string title = empire.CoreKingdom.GetRegime()?.virtual_honorary_peerages?.FirstOrDefault(key => !empire.IsHonoraryPeerageReserved(key));
                worked = leader != null && leader.isAlive() && title != null && leader.GrantHonoraryPeerage(empire, title);
                if (worked) CourtSystem.AdjustFaction(empire, faction, "favor", 5f);
                break;
            }
            case "favor":
                worked = CourtSystem.FactionState(empire, faction).favor >= 20f;
                if (worked) CourtSystem.AdjustFaction(empire, faction, "favor", -15f);
                break;
            case "silver":
            {
                long cost = Math.Max(0L, seat.Realm.GetTreasuryBalance()) * 30 / 1000;
                worked = cost > 0;
                if (worked)
                {
                    seat.Realm.AddMoney(-(int)Math.Min(int.MaxValue, cost));
                    CourtSystem.AdjustFaction(empire, faction, "capital", 5f);
                }
                break;
            }
        }
        if (!worked)
        {
            Notify(seat, string.Format(LM.Get("terminal_chip_fail_" + kind), name));
            return;
        }
        memorial.stance_overrides[factionId] = stance + 1;
        memorial.stances[factionId] = stance + 1;
        memorial.support = CourtSystem.SupportShare(CourtSystem.DebateStances(empire, claim, proposer, memorial));
        Notify(seat, string.Format(LM.Get("terminal_chip_done_" + kind), name));
    }

    // 没有对应诉求的事(改元、改国号、更名)按君主权威掷一回：成算 = 底数 + 斜率 × 权威，夹在 5%~95%
    private const float RealmNameBase = 0.1f, RealmNameSlope = 0.6f, RulerNameBase = 0.4f, RulerNameSlope = 0.5f;
    // 拨库银的几成(千分数)整饬吏治，中央腐败降多少(各城降一半)
    private static readonly int[] PurgeShare = { 100, 250, 500 };
    private static readonly double[] PurgeDrop = { 0.02d, 0.05d, 0.10d };

    // 修官道：工期几个月、每段多少库银
    private const int RoadMonths = 6, RoadCost = 15;

    private static float ScaleChance(Kingdom realm, float floor, float slope) =>
        Mathf.Clamp(floor + slope * Authority(realm) / 100f, 0.05f, 0.95f);

    private static bool ScaleRoll(Order order, Kingdom realm, float chance) =>
        TerminalIntel.Unit(TerminalIntel.Hash(order.Id, realm.getID(), order.Due)) < chance;

    // 手机上填的名字：去掉标记符号和控制符，至多十二个字
    private static string CleanText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var clean = new global::System.Text.StringBuilder();
        foreach (char c in text.Trim())
            if (!char.IsControl(c) && c != '<' && c != '>' && c != '{' && c != '}' && clean.Length < 12) clean.Append(c);
        string result = clean.ToString().Trim();
        return result.Length == 0 ? null : result;
    }

    // 办一道命令：成了返回 null，不成返回缘由
    private static string Execute(Seat seat, Order order)
    {
        Kingdom realm = seat.Realm;
        switch (order.Kind)
        {
            case "court":
            {
                Empire empire = OwnEmpire(seat);
                if (empire == null) return LM.Get("terminal_fail_generic");
                var found = FindProposal(empire, order.Arg);
                if (found.claim == null || !CourtSystem.GodPropose(empire, found.faction, found.claim)) return LM.Get("terminal_fail_court");
                return null;
            }
            case "era":
            {
                Empire empire = OwnEmpire(seat);
                if (empire == null || !empire.IsAllowToMakeYearName()) return LM.Get("terminal_fail_generic");
                float chance = EraChance(realm);
                if (TerminalIntel.Unit(TerminalIntel.Hash(order.Id, realm.getID(), order.Due)) >= chance)
                    return string.Format(LM.Get("terminal_fail_era"), Mathf.RoundToInt(chance * 100f));
                empire.create_year_name();
                TranslateHelper.LogEmperorNewYearName(seat.Ruler, empire);
                EmpireLawSystem.OnEraNameChanged(empire);
                return null;
            }
            case "empire":
                if (realm.GetEmpire() != null) return LM.Get("terminal_fail_generic");
                if (!EmpireCraftKingdomBehCheckEmpire.CanStartEmpireFormation(realm)) return Words(EmpireFormationService.Diagnose(realm));
                return EmpireCraftPlotsAddition.BecomeEmpireAndStartEnfeoff(seat.Ruler) ? null : LM.Get("terminal_fail_generic");
            case "heir":
            {
                Actor heir = World.world.units.get(order.Target);
                List<Actor> candidates = EmpireCraftKingdomBehCheckHeir.HeirCandidates(realm);
                if (heir == null || !heir.isAlive() || candidates == null || !candidates.Contains(heir)) return LM.Get("terminal_fail_heir");
                realm.SetHeir(heir);
                return null;
            }
            case "abdicate":
            {
                Actor successor = World.world.units.get(order.Target);
                List<Actor> candidates = EmpireCraftKingdomBehCheckHeir.HeirCandidates(realm);
                City capital = realm.capital;
                if (successor == null || !successor.isAlive() || candidates == null || !candidates.Contains(successor) || capital == null)
                    return LM.Get("terminal_fail_heir");
                Empire empire = OwnEmpire(seat);
                if (empire != null)
                {
                    if (!empire.InstallHeadOfState(successor)) return LM.Get("terminal_fail_generic");
                }
                else
                {
                    // 照 Empire.InstallHeadOfState 的换法
                    if (successor.isKing() && successor.kingdom != realm) successor.kingdom.removeKing();
                    realm.removeKing();
                    successor.joinCity(capital);
                    successor.setKingdom(realm);
                    realm.setKing(successor);
                    if (realm.king != successor) return LM.Get("terminal_fail_generic");
                }
                seat.Abdicated = true;
                return null;
            }
            case "rename_realm":
            {
                float chance = ScaleChance(realm, RealmNameBase, RealmNameSlope);
                if (!ScaleRoll(order, realm, chance)) return string.Format(LM.Get("terminal_fail_era"), Mathf.RoundToInt(chance * 100f));
                Empire empire = OwnEmpire(seat);
                if (empire != null) empire.SetEmpireName(order.Arg);
                else
                {
                    // 独立国战时不改国号(Kingdom.SetKingdomName 会不声不响地不改)
                    if (realm.GetEmpire() == null && realm.getWars().Any(war => war?.data != null && !war.hasEnded()))
                        return LM.Get("terminal_fail_wartime");
                    realm.SetKingdomName(order.Arg);
                }
                return null;
            }
            case "rename_ruler":
            {
                float chance = ScaleChance(realm, RulerNameBase, RulerNameSlope);
                if (!ScaleRoll(order, realm, chance)) return string.Format(LM.Get("terminal_fail_era"), Mathf.RoundToInt(chance * 100f));
                seat.Ruler.setName(order.Arg);
                return null;
            }
            case "purge":
            {
                int tier = order.Arg == "3" ? 2 : order.Arg == "2" ? 1 : 0;
                long treasury = realm.GetTreasuryBalance();
                if (treasury <= 0) return LM.Get("terminal_fail_treasury");
                long cost = treasury * PurgeShare[tier] / 1000;
                realm.AddMoney(-(int)Math.Min(int.MaxValue, cost));
                realm.AddCorruptionRate(-PurgeDrop[tier]);
                foreach (City city in realm.cities)
                    if (city?.data != null && !city.isRekt()) city.AddCorruptionRate(-PurgeDrop[tier] * 0.5d);
                return null;
            }
            case "appoint":
            case "dismiss":
            {
                Empire court = OwnEmpire(seat);
                List<OfficeObject> all = court?.data.centerOffice?.GetAllOffices(court);
                if (all == null || !int.TryParse(order.Arg, out int slot) || slot < 0 || slot >= all.Count || all[slot] == null)
                    return LM.Get("terminal_fail_generic");
                if (order.Kind == "dismiss")
                {
                    all[slot].RemoveActor();
                    return null;
                }
                Actor person = World.world.units.get(order.Target);
                if (person == null || !person.isAlive() || !all[slot].Accepts(person)) return LM.Get("terminal_fail_office");
                all[slot].SetActor(person);
                return null;
            }
            case "chief":
            {
                Empire court = OwnEmpire(seat);
                Actor person = World.world.units.get(order.Target);
                if (court == null || person == null || !person.isAlive()) return LM.Get("terminal_fail_generic");
                return court.SetCabinetLeader(person) ? null : LM.Get("terminal_fail_chief");
            }
            case "recruit":
            {
                // 招募新军：和军事页"组建新军"同一个入口，钱从国库出；没成时带上它给的缘由
                City home = World.world.cities.get(order.Target);
                if (home?.data == null || home.isRekt() || home.kingdom != realm) return LM.Get("terminal_fail_generic");
                return CorpsSystem.RaiseManual(realm, home, true) != null ? null : Words(CorpsSystem.LastManualFailure ?? "terminal_fail_generic");
            }
            case "road":
            {
                // 加强对一座城的管控(用户 2026-11-11 "政令到达不了的地方可以采取什么政策加强管控")：行政控制力按从都城出发的路程算，
                // 有路的地块阻力减半(AdministrativeControlSystem)，所以修一条官道通过去；按新修的段数扣库银
                City city = World.world.cities.get(order.Target);
                if (city?.data == null || city.isRekt() || city.kingdom != realm || realm.capital == null) return LM.Get("terminal_fail_generic");
                if (RoadBuildingSystem.ConnectByPower(realm.capital, city, out int built) < 0) return LM.Get("terminal_fail_road");
                if (built <= 0) return LM.Get("terminal_fail_road_done");
                realm.AddMoney(-built * RoadCost);
                AdministrativeControlSystem.Refresh(realm);
                return null;
            }
            case "vassal":
                return ExecuteVassal(seat, order);
            case "diplo":
            {
                Kingdom other = World.world.kingdoms.get(order.Target);
                DiplomacyActions.Action action = OpenActions().FirstOrDefault(a => Suffix(a.key) == order.Arg);
                if (other?.data == null || other.isRekt() || action == null) return LM.Get("terminal_fail_gone");
                bool free = order.Arg == "ally" && HostOf(other, seat) != null || order.Arg == "leave_alliance" && Allied(realm, other);
                string reason = free ? null : DiplomacyActions.Check(action, realm, other);
                if (reason != null) return Words(reason);
                return Negotiate(seat, order, other, action);
            }
        }
        return LM.Get("terminal_fail_generic");
    }

    // ───────── 待批：要人失职的案子 ─────────
    // (用户 2026-11-11 "还有派系给谁定罪这种""比如谁谁谁失守是否让其戴罪立功还是直接诛杀")
    // 律法本来是当场判完施刑(EmpireLawSystem.Prosecute)。这位君主被席位领着时，本国要人的军事失职(临阵脱逃、违抗军令)先不判，
    // 存成待批：依律论处(照原样量刑)、戴罪立功(不施刑、留任，吏治添弊)、诛杀(不是十恶而杀，君主添暴虐)。过 HoldSeconds 没批就依律论处。
    // 叛逆这类大罪不拦，照旧当场办
    internal sealed class Case
    {
        public long Id, ActorId, AccuserId, KingdomId;
        public LawType Type;
        public string ReasonKey, Name, LawName, Reason;
        public float Since;
    }

    private const int MaxCases = 8, CaseQuietMonths = 24;
    private static bool _passCase;

    // TerminalPatch 拦 EmpireLawSystem.Prosecute 时问这里：真 = 这案子终端接了(待批，或刚批过不再提)，原流程不走
    public static bool HoldsCase(Actor actor, LawType type, Kingdom kingdom, string reasonKey, Actor accuser)
    {
        if (_passCase || !Enabled || !_anyRuler || actor == null || kingdom == null) return false;
        if (type != LawType.临阵脱逃 && type != LawType.违抗军令) return false;
        foreach (Seat seat in SeatByToken.Values)
        {
            if (seat.Ruler == null || actor == seat.Ruler) continue;
            Empire empire = OwnEmpire(seat);
            if (seat.Realm != kingdom && (empire == null || kingdom.GetEmpire() != empire)) continue;
            if (seat.Delegated || seat.Away != null || !TranslateHelper.ShouldBroadcastLawActor(actor)) return false;
            long id = actor.getID();
            if (seat.Cases.Any(held => held.ActorId == id)) return true;
            if (seat.Judged.TryGetValue(id, out int month) && MonthNow() - month < CaseQuietMonths) return true;
            if (seat.Cases.Count >= MaxCases) return false;
            seat.Cases.Add(new Case
            {
                Id = ++_seq, ActorId = id, AccuserId = accuser?.getID() ?? -1, KingdomId = kingdom.getID(), Type = type, ReasonKey = reasonKey,
                Name = TerminalIntel.Strip(TranslateHelper.GetActorFullLogName(actor)), LawName = TerminalIntel.Strip(EmpireLawSystem.GetLawName(type)),
                Reason = string.IsNullOrEmpty(reasonKey) ? "" : Words(reasonKey), Since = Time.realtimeSinceStartup
            });
            seat.NextDecrees = seat.NextState = 0f;
            return true;
        }
        return false;
    }

    private static void RunCases(Seat seat)
    {
        for (int i = seat.Cases.Count - 1; i >= 0; i--)
            if (Time.realtimeSinceStartup - seat.Cases[i].Since >= HoldSeconds) Judge(seat, seat.Cases[i], "law");
    }

    private static void Judge(Seat seat, Case held, string choice)
    {
        seat.Cases.Remove(held);
        seat.Judged[held.ActorId] = MonthNow();
        seat.NextDecrees = seat.NextState = 0f;
        Actor actor = World.world.units.get(held.ActorId);
        Kingdom kingdom = World.world.kingdoms.get(held.KingdomId);
        if (actor == null || !actor.isAlive() || kingdom?.data == null || kingdom.isRekt())
        {
            Notify(seat, string.Format(LM.Get("terminal_case_void"), held.Name));
            return;
        }
        Empire empire = kingdom.GetEmpire();
        switch (choice)
        {
            case "redeem":
                // 放过的代价照量刑天平"放过"一支(EmpireLawSystem 的 ApplyScaleCosts)：吏治添弊
                if (empire != null) MaladySystem.Aggravate(empire, MaladySystem.Graft, 2f);
                kingdom.AddCorruptionRate(0.005d);
                Notify(seat, string.Format(LM.Get("terminal_case_redeem"), held.Name));
                break;
            case "death":
            {
                LawEnforcementContext context = EmpireLawSystem.PrepareEnforcement(actor, held.Type, kingdom);
                if (context == null || !EmpireLawSystem.ApplyPunishment(context, PunishmentLevel.死刑))
                {
                    Notify(seat, string.Format(LM.Get("terminal_case_void"), held.Name));
                    break;
                }
                if (!context.Grave) seat.Ruler.AddTyrantValue(3);
                if (empire != null && EmpireLawSystem.IsRoyalKin(actor, kingdom)) empire.AddMandate(-2);
                Notify(seat, string.Format(LM.Get("terminal_case_death"), held.Name));
                break;
            }
            default:
                _passCase = true;
                try
                {
                    Actor accuser = held.AccuserId >= 0 ? World.world.units.get(held.AccuserId) : null;
                    EmpireLawSystem.Prosecute(actor, held.Type, kingdom, held.ReasonKey, accuser);
                }
                finally
                {
                    _passCase = false;
                }
                Notify(seat, string.Format(LM.Get("terminal_case_law"), held.Name));
                break;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.GeneralSystems.Decisions;
using EmpireCraft.Scripts.Layer;

namespace EmpireCraft.Server;

// 排查用(--try-decrees)：在眼下这个世界里替各国君主把大政(迁都、设陪京、撤陪京、设直隶、改制四项，见 RulerDecrees)过一遍天平——
// 各样办得成的有几位、办不成的卡在哪；每样找头一位办得成的君主照手机席位的走法发起(DecisionSystem.Launch)，
// 跑完之后报各样办成了没有。不是游戏里的验证，只是让这几条在真的世界数据上从过秤走到施行。
internal static class DecreeProbe
{
    private sealed class Case
    {
        public string Id;
        public Actor Ruler;
        public Kingdom Realm;
        public City Target, OldCapital;
        public string Regime;
    }

    private static readonly List<Case> Cases = new();

    internal static void Launch(MapBox world)
    {
        var tally = new SortedDictionary<string, Dictionary<string, int>>();
        var busy = new HashSet<long>();
        foreach (Kingdom realm in world.kingdoms.list.Where(realm => realm?.data != null && realm.isAlive() && !realm.wild)
                     .OrderByDescending(realm => realm.cities.Count).ToList())
        {
            Actor ruler = realm.king;
            if (ruler == null || !ruler.isAlive()) continue;
            List<DecisionSystem.Advice> advices;
            try
            {
                advices = DecisionSystem.AdviseFor(ruler);
            }
            catch (Exception exception)
            {
                Console.WriteLine("  [大政] " + realm.name + " 过秤出错: " + exception.Message);
                continue;
            }
            foreach (IGrouping<string, DecisionSystem.Advice> group in advices.Where(advice => RulerDecrees.IsManual(advice.id)).GroupBy(advice => advice.id))
            {
                DecisionSystem.Advice first = group.First();
                string state = first.blocked ?? "办得成";
                if (!tally.TryGetValue(group.Key, out Dictionary<string, int> states)) tally[group.Key] = states = new Dictionary<string, int>();
                states.TryGetValue(state, out int count);
                states[state] = count + 1;
                if (first.blocked != null || busy.Contains(ruler.id) || Cases.Any(item => item.Id == group.Key)) continue;
                City target = first.target >= 0 ? world.cities.get(first.target) : null;
                var made = new Case
                {
                    Id = group.Key, Ruler = ruler, Realm = realm, Target = target, OldCapital = realm.capital,
                    Regime = realm.GetRegime()?.type.ToString() ?? "?"
                };
                string failed = DecisionSystem.Launch(ruler, group.Key, first.target);
                Console.WriteLine("  [大政] " + realm.name + "(权威 " + RulerDecrees.Authority(realm) + "，读数 " + first.ratio.ToString("0.00") + "，可选 " +
                                  group.Count() + " 项)发起 " + group.Key + (target != null ? " → " + target.name : "") + "：" + (failed ?? "已立项"));
                if (failed != null) continue;
                Cases.Add(made);
                busy.Add(ruler.id);
            }
        }
        foreach (KeyValuePair<string, Dictionary<string, int>> pair in tally)
            Console.WriteLine("  [大政] " + pair.Key + "：" + string.Join("，", pair.Value.OrderByDescending(state => state.Value).Select(state => state.Key + " " + state.Value + " 国")));
        if (tally.Count == 0) Console.WriteLine("  [大政] 没有哪位君主的天平上列得出这几条(门槛都没过)");
    }

    // force：不看权威、不看天平，替各帝国的皇帝把各项的施行直接走一遍(每位皇帝一项；设陪京的那位接着设直隶、再撤陪京)，
    // 看在真的世界数据上会不会出岔子。照决议自己的走法：立项、记下点的城、调它的施行函数
    internal static void Force(MapBox world)
    {
        RulerDecrees.ProbeIgnoresAuthority = true;
        var done = new HashSet<string>();
        var used = new HashSet<long>();
        string[] order =
        {
            RulerDecrees.AddJing, RulerDecrees.ReformCabinet, RulerDecrees.ReformPrefecture, RulerDecrees.ReformProvince, RulerDecrees.ReformEnfeoff,
            RulerDecrees.MoveCapital
        };
        foreach (string id in order)
        foreach (Kingdom realm in world.kingdoms.list.Where(realm => realm?.data != null && realm.isAlive() && !realm.wild && realm.king != null)
                     .OrderByDescending(realm => realm.cities.Count).ToList())
        {
            if (done.Contains(id) || used.Contains(realm.id)) continue;
            Empire empire = realm.GetEmpire();
            if (empire == null || empire.CoreKingdom != realm) continue;
            if (!Run(world, realm, id, out string outcome)) continue;
            done.Add(id);
            used.Add(realm.id);
            Console.WriteLine("  [大政·强行] " + realm.name + " " + id + "：" + outcome);
            if (id != RulerDecrees.AddJing) continue;
            if (Run(world, realm, RulerDecrees.SetZhili, out outcome)) Console.WriteLine("  [大政·强行] " + realm.name + " " + RulerDecrees.SetZhili + "：" + outcome);
            else Console.WriteLine("  [大政·强行] " + realm.name + " " + RulerDecrees.SetZhili + "：办不成(" + outcome + ")");
            if (Run(world, realm, RulerDecrees.RemoveJing, out outcome)) Console.WriteLine("  [大政·强行] " + realm.name + " " + RulerDecrees.RemoveJing + "：" + outcome);
        }
        foreach (string id in order.Where(id => !done.Contains(id))) Console.WriteLine("  [大政·强行] " + id + "：没有哪位皇帝条件合(不看权威也不合)");
        // 改制各项卡在哪：各帝国的政体、是不是华夏、上回改制多少年、有没有领土战争、制度树上对应的节点与发不起的缘由
        foreach (Empire empire in EmpireCraft.Scripts.ModClass.EMPIRE_MANAGER.Where(item => item?.data != null && !item.isRekt() && !item.IsArchived() && item.CoreKingdom != null)
                     .OrderByDescending(item => item.kingdoms_list.Sum(member => member.cities.Count)).Take(8))
        {
            Kingdom core = empire.CoreKingdom;
            string Node(string claim)
            {
                EmpireCraft.Scripts.Data.InstitutionNodeConfig node = InstitutionSystem.FindClaimReformTarget(empire, claim);
                if (node == null) return "树上没有";
                bool plain = InstitutionSystem.CanStartReform(empire, node, out string why, false);
                bool forced = plain || InstitutionSystem.CanStartReform(empire, node, out _, true);
                return node.id + (plain ? " 可推" : forced ? " 可强推" : " 推不了(" + why + ")");
            }
            Console.WriteLine("  [大政·诊断] " + core.name + "：权威 " + RulerDecrees.Authority(core) + "，政体 " + core.GetRegime()?.type + "，华夏 " +
                              RealmNamingSystem.IsHuaxia(empire) + "，上回改制 " + core.GetSystemChangeYear() + " 年前，领土战争 " + empire.HasTerritorialWar() +
                              "；内阁节点 " + Node("设立内阁") + "；郡县节点 " + Node("转天朝制度") + "；改划 " +
                              (core.king != null && AdministrationReorganizationSystem.CanPropose(core.king) ? "可" : "不可"));
        }
        RulerDecrees.ProbeIgnoresAuthority = false;
    }

    private static bool Run(MapBox world, Kingdom realm, string id, out string outcome)
    {
        outcome = "条件不合";
        Actor ruler = realm.king;
        PlotAsset def = DecisionSystem.Def(id);
        if (def == null || ruler == null || !ruler.isAlive() || ruler.plot != null && ruler.plot.isActive()) return false;
        try
        {
            if (!def.check_is_possible(ruler)) return false;
            City target = RulerDecrees.HasTargets(id) ? RulerDecrees.Candidates(id, ruler).FirstOrDefault() : null;
            if (RulerDecrees.HasTargets(id) && target == null) return false;
            Plot plot = world.plots.newPlot(ruler, def, true);
            if (plot == null)
            {
                outcome = "立不了项";
                return false;
            }
            plot.target_city = target;
            City oldCapital = realm.capital;
            string regime = realm.GetRegime()?.type.ToString() ?? "?";
            bool acted = def.action(ruler);
            plot.setAlive(false);
            ruler.leavePlot();
            Empire empire = realm.GetEmpire();
            string detail;
            if (id == RulerDecrees.MoveCapital) detail = "都城 " + oldCapital?.name + " → " + realm.capital?.name;
            else if (id == RulerDecrees.AddJing || id == RulerDecrees.RemoveJing)
                detail = "眼下陪京 " + MultiCapitalSystem.AuxiliaryCount(empire) + " 座" +
                         (target != null && MultiCapitalSystem.IsJing(target) ? "(" + target.name + " 是" + MultiCapitalSystem.JingNameOf(target) + ")" : "");
            else if (id == RulerDecrees.SetZhili)
                detail = target?.kingdom != null && MultiCapitalSystem.IsZhili(target.kingdom) ? target.kingdom.name + " 已是直隶" : "没设成直隶";
            else detail = "政体 " + regime + " → " + (realm.GetRegime()?.type.ToString() ?? "?");
            outcome = (acted ? "施行了" : "施行返回不成") + (target != null ? "，点的城 " + target.name : "") + "；" + detail;
            return true;
        }
        catch (Exception exception)
        {
            outcome = "出错: " + exception;
            return true;
        }
    }

    internal static void Report()
    {
        foreach (Case item in Cases)
        {
            Kingdom realm = item.Realm;
            Empire empire = realm.isAlive() ? realm.GetEmpire() : null;
            string outcome;
            bool pending = item.Ruler.isAlive() && item.Ruler.plot != null && item.Ruler.plot.isActive();
            switch (item.Id)
            {
                case RulerDecrees.MoveCapital:
                    outcome = realm.capital == item.Target ? "都城已在 " + item.Target.name + "(旧都 " + item.OldCapital?.name + ")" : "都城仍在 " + realm.capital?.name;
                    break;
                case RulerDecrees.AddJing:
                    outcome = item.Target != null && MultiCapitalSystem.IsJing(item.Target)
                        ? item.Target.name + " 已是" + MultiCapitalSystem.JingNameOf(item.Target) + "，眼下 " + MultiCapitalSystem.AuxiliaryCount(empire) + " 座陪京"
                        : item.Target?.name + " 还不是京";
                    break;
                case RulerDecrees.RemoveJing:
                    outcome = item.Target != null && !MultiCapitalSystem.IsJing(item.Target) ? item.Target.name + " 已不是京" : item.Target?.name + " 还是京";
                    break;
                case RulerDecrees.SetZhili:
                    outcome = item.Target?.kingdom != null && MultiCapitalSystem.IsZhili(item.Target.kingdom) ? item.Target.kingdom.name + " 已是直隶" : "没设成直隶";
                    break;
                default:
                    outcome = "政体 " + item.Regime + " → " + (realm.GetRegime()?.type.ToString() ?? "?");
                    break;
            }
            Console.WriteLine("  [大政] " + realm.name + " " + item.Id + "：" + (pending ? "还在办；" : "") + outcome);
        }
    }
}

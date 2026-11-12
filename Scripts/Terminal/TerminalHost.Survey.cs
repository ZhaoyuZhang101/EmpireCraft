using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.Terminal;

// 探路与购舆图(群里 2026-11-12 "能不能问别人买地图""或者招募探险家")：迷雾不是只能等科技，君主可以花库银去揭。
//   · 遣人探路：在图上点一处未通之地。一队人从最近的已知之地出发，来回按远近要几个月，也有一去不回的；
//     回来了，沿途和那一带的山川就画在图上，碰上的邦国算有了传闻。钱是出发时就花了的；
//   · 购舆图：向一个已知之国买它的图。遣使去(同别的外交一样要走几个月)，它对本国没有恶感才肯卖，银子归它的国库；
//     买到了，它境内连同四邻一圈的山川疆界都画在图上。交战之国不卖。
// 画上图的地方记在席位上(Seat.Charted)，换君主、换世界就清；不存档。数值都是我定的，用户没逐条确认
public static partial class TerminalHost
{
    // 探路至多去多远(块)、一个月走几块、到了看多大一圈、沿途看多宽；舆图连同国境外多宽一圈
    private const int ExploreMax = 16, ExploreSpeed = 4, ExploreRadius = 5, ExplorePath = 2, ChartMargin = 2;
    // 各花库银的几成、至少多少；探路的人回不来的机会；对方好感至少多少才肯卖图
    private const float ExploreShare = 0.02f, ChartShare = 0.04f;
    private const long ExploreFloor = 60, ChartFloor = 150;
    private const int ExploreLostPercent = 15, ChartOpinion = 0;

    private static long SurveyCost(Kingdom realm, float share, long floor) =>
        Math.Max(floor, (long)(Math.Max(0L, realm.GetTreasuryBalance()) * share));

    // 八方：0 北，顺时针
    private static int Direction(float dx, float dy)
    {
        if (Mathf.Abs(dx) < 0.01f && Mathf.Abs(dy) < 0.01f) return 0;
        float angle = Mathf.Atan2(dx, -dy) * Mathf.Rad2Deg;
        return ((Mathf.RoundToInt(angle / 45f) % 8) + 8) % 8;
    }

    // 下令。受理了返回 true(made 填好)；不受理的自己回一条奏报、返回 false
    private static bool SurveyOrder(Seat seat, Order made, string id, string target)
    {
        Kingdom realm = seat.Realm;
        long treasury = realm.GetTreasuryBalance();
        if (id == "explore")
        {
            string[] parts = (target ?? "").Split(',');
            if (seat.MapCells == null || parts.Length != 2 || !int.TryParse(parts[0], out int x) || !int.TryParse(parts[1], out int y) ||
                x < 0 || y < 0 || x >= seat.MapW || y >= seat.MapH) return false;
            if (seat.MapCells[y * seat.MapW + x] != TerminalIntel.Fog)
            {
                Notify(seat, LM.Get("terminal_fail_explore_known"));
                return false;
            }
            // 从最近的已知之地出发
            int best = int.MaxValue, fromX = x, fromY = y;
            for (int at = 0; at < seat.MapCells.Length; at++)
            {
                if (seat.MapCells[at] == TerminalIntel.Fog) continue;
                int dx = at % seat.MapW - x, dy = at / seat.MapW - y, d = dx * dx + dy * dy;
                if (d >= best) continue;
                best = d;
                fromX = at % seat.MapW;
                fromY = at / seat.MapW;
            }
            int distance = best == int.MaxValue ? int.MaxValue : Mathf.CeilToInt(Mathf.Sqrt(best));
            if (distance > ExploreMax)
            {
                Notify(seat, LM.Get("terminal_fail_explore_far"));
                return false;
            }
            long cost = SurveyCost(realm, ExploreShare, ExploreFloor);
            if (treasury < cost)
            {
                Notify(seat, LM.Get("terminal_fail_treasury"));
                return false;
            }
            realm.AddMoney(-(int)Math.Min(int.MaxValue, cost));
            // 说方位照都城算
            (double cx, double cy) = realm.capital?.data != null ? CellOf(seat, realm.capital.city_center.x, realm.capital.city_center.y) : ((double)fromX, (double)fromY);
            int direction = Direction(x - (float)cx, y - (float)cy);
            made.Kind = "explore";
            made.Arg = x + "," + y;
            made.Extra = fromX + "," + fromY + "," + direction;
            made.Due = MonthNow() + Math.Max(2, Mathf.CeilToInt(distance * 2f / ExploreSpeed));
            made.Text = string.Format(LM.Get("terminal_order_explore"), LM.Get("terminal_dir_" + direction));
            return true;
        }
        if (!long.TryParse(target, out long kingdomId) || !seat.Known.Contains(kingdomId)) return false;
        Kingdom other = World.world.kingdoms.get(kingdomId);
        if (other?.data == null || other.isRekt() || other == realm) return false;
        if (realm.isInWarWith(other))
        {
            Notify(seat, LM.Get("terminal_fail_chart_war"));
            return false;
        }
        if (treasury < SurveyCost(realm, ChartShare, ChartFloor))
        {
            Notify(seat, LM.Get("terminal_fail_treasury"));
            return false;
        }
        made.Kind = "buy_map";
        made.Target = kingdomId;
        made.Due = MonthNow() + EnvoyMonths(seat, other);
        made.Text = string.Format(LM.Get("terminal_order_buy_map"), NameOf(other));
        return true;
    }

    // 到日子了。办成返回 null，没办成返回缘由
    private static string SurveyExecute(Seat seat, Kingdom realm, Order order)
    {
        if (seat.MapCells == null) return LM.Get("terminal_fail_generic");
        if (seat.ChartedW != seat.MapW || seat.ChartedH != seat.MapH)
        {
            seat.Charted.Clear();
            seat.ChartedW = seat.MapW;
            seat.ChartedH = seat.MapH;
        }
        ZoneCalculator calc = World.world.zone_calculator;
        if (order.Kind == "explore")
        {
            string[] to = (order.Arg ?? "").Split(','), from = (order.Extra ?? "").Split(',');
            if (to.Length != 2 || from.Length != 3) return LM.Get("terminal_fail_generic");
            int x = int.Parse(to[0]), y = int.Parse(to[1]), fromX = int.Parse(from[0]), fromY = int.Parse(from[1]);
            if (TerminalIntel.Hash(order.Id, realm.getID(), 77) % 100 < ExploreLostPercent) return LM.Get("terminal_fail_explore_lost");
            int steps = Math.Max(1, Math.Max(Math.Abs(x - fromX), Math.Abs(y - fromY)));
            for (int step = 0; step <= steps; step++)
                Chart(seat, fromX + (x - fromX) * step / steps, fromY + (y - fromY) * step / steps, ExplorePath);
            Chart(seat, x, y, ExploreRadius);
            // 那一带碰上了哪些还不认得的邦国
            var met = new List<string>();
            for (int dy = -ExploreRadius; dy <= ExploreRadius; dy++)
            for (int dx = -ExploreRadius; dx <= ExploreRadius; dx++)
            {
                int cx = x + dx, cy = y + dy;
                if (dx * dx + dy * dy > ExploreRadius * ExploreRadius || cx < 0 || cy < 0 || cx >= seat.MapW || cy >= seat.MapH) continue;
                Kingdom owner = OwnerAt(calc, cx, seat.MapH - 1 - cy);
                if (owner == null || owner == realm || seat.Known.Contains(owner.getID()) || seat.Hidden.Contains(owner.getID())) continue;
                string name = NameOf(owner);
                if (name.Length > 0 && !met.Contains(name) && met.Count < 4) met.Add(name);
            }
            string where = LM.Get("terminal_dir_" + from[2]);
            Notify(seat, met.Count > 0 ? string.Format(LM.Get("terminal_explore_found"), where, string.Join("、", met))
                : string.Format(LM.Get("terminal_explore_empty"), where));
            seat.NextSight = 0f;
            return null;
        }
        Kingdom other = World.world.kingdoms.get(order.Target);
        if (other?.data == null || other.isRekt()) return LM.Get("terminal_fail_generic");
        if (realm.isInWarWith(other)) return LM.Get("terminal_fail_chart_war");
        if (Opinion(other, realm) < ChartOpinion) return string.Format(LM.Get("terminal_fail_refused"), NameOf(other));
        long cost = SurveyCost(realm, ChartShare, ChartFloor);
        if (realm.GetTreasuryBalance() < cost) return LM.Get("terminal_fail_treasury");
        int paid = (int)Math.Min(int.MaxValue, cost);
        realm.AddMoney(-paid);
        other.AddMoney(paid);
        for (int zy = 0; zy < seat.MapH; zy++)
        for (int zx = 0; zx < seat.MapW; zx++)
            if (OwnerAt(calc, zx, zy) == other)
                Chart(seat, zx, seat.MapH - 1 - zy, ChartMargin);
        Notify(seat, string.Format(LM.Get("terminal_chart_bought"), NameOf(other), paid));
        seat.NextSight = 0f;
        return null;
    }

    // 这块地(游戏里的地块坐标，y 朝上)是哪国的
    private static Kingdom OwnerAt(ZoneCalculator calc, int zoneX, int zoneY)
    {
        TileZone zone = calc.getZone(zoneX, zoneY);
        if (zone == null) return null;
        City city = zone.city;
        if (city?.data == null || city.isRekt()) city = null;
        Kingdom kingdom = city?.kingdom ?? WildernessSystem.OwnerOf(zone);
        return kingdom?.data == null || kingdom.isRekt() || kingdom.wild ? null : kingdom;
    }

    // 以(x, y)为心、radius 块以内的地画上图(总图的格：行 0 在上)
    private static void Chart(Seat seat, int x, int y, int radius)
    {
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            int cx = x + dx, cy = y + dy;
            if (dx * dx + dy * dy > radius * radius || cx < 0 || cy < 0 || cx >= seat.MapW || cy >= seat.MapH) continue;
            seat.Charted.Add(cy * seat.MapW + cx);
        }
    }
}

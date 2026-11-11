using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;

namespace EmpireCraft.Scripts.GeneralSystems;

// 手令(用户 2026-11-12 "地图那里应该有更多的功能比如微操军队""军队应该在地图上显示, 手指点击下达命令")：
// 君主点名一支军、指一座城——
//   · 本国的城、同一帝国里的城(皇帝调兵进驻行政区、封国)：开过去驻守。太平时就驻在那里；打仗时算这座城的守军，不被抽去别处；
//   · 交战敌国的城：去打它。城就是战线的投那一段，在敌境深处的投离它最近的一段；
//   · 手令一直管到撤销、这支军没了、城没了、或那座城既不是自家的也不是敌国的(仗打完了、转给了别国)。
// 溃退休整、被围、组织度不够上阵的照旧(见 FrontSystem.Assign)，缓过来再接着奉令。
// 与军令(CorpsCommandSystem，管全国出征的军往哪集中)各管各的：手令在先。手令不存档，只有终端下得了
public static class CorpsDirectOrders
{
    private static readonly Dictionary<long, long> Orders = new();

    public static void ResetWorldState() => Orders.Clear();

    public static bool Has(CorpsData corps) => corps != null && Orders.Count > 0 && Orders.ContainsKey(corps.id);

    public static void Set(CorpsData corps, City city)
    {
        if (corps == null || city?.data == null) return;
        Orders[corps.id] = city.data.id;
    }

    public static void Clear(CorpsData corps)
    {
        if (corps != null) Orders.Remove(corps.id);
    }

    // 君主不在位了(席位腾空)：这一国的手令都撤掉，交还给朝廷自己调度
    public static void ClearKingdom(Kingdom kingdom)
    {
        if (Orders.Count == 0) return;
        List<CorpsData> list = kingdom?.data == null ? null : kingdom.GetOrCreate().corps;
        if (list == null) return;
        foreach (CorpsData corps in list) Orders.Remove(corps.id);
    }

    // 这支军奉令去的城；作废的手令顺手清掉
    public static City Target(Kingdom kingdom, CorpsData corps)
    {
        if (corps == null || Orders.Count == 0 || !Orders.TryGetValue(corps.id, out long id)) return null;
        City city = World.world.cities.get(id);
        if (kingdom != null && city?.data != null && !city.isRekt() && city.kingdom != null &&
            (Ours(kingdom, city.kingdom) || kingdom.isInWarWith(city.kingdom))) return city;
        Orders.Remove(corps.id);
        return null;
    }

    // 自家的城：本国的，或同一帝国里的
    public static bool Ours(Kingdom kingdom, Kingdom owner) =>
        owner == kingdom || owner != null && kingdom.GetEmpire() != null && owner.GetEmpire() == kingdom.GetEmpire();

    // 一国里奉着手令的军
    public static int Count(Kingdom kingdom)
    {
        if (Orders.Count == 0) return 0;
        List<CorpsData> list = kingdom?.data == null ? null : kingdom.GetOrCreate().corps;
        return list?.Count(corps => Orders.ContainsKey(corps.id)) ?? 0;
    }
}

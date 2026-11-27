using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 自立按法理成片(用户 2026-10-27 嫌版图太碎，问答四条全选)：一座城起事、或欠饷撤守自立时，同一个王国法理里挨着它的、
// 原主手里的城，人心也已经散了的(开着财政看实际稳定度不到起事门槛，不开看忠诚为负)，一并跟着走——至多 MaxCities 座，
// 不带原主的都城。分封诸侯国里的小乱(PettyRebellion)本来就只一城一邑，不跟。
// 叛乱隔断(用户 2026-10-27："叛乱的时候如果隔绝了其他行政区使他们变成了飞地，也应当将他们同时转为叛乱")：起事(连同跟着走的城)之后，
// 原主手里与都城同在一块陆地、却已沿本国(在帝国里的算整个帝国)相邻的城走不到都城的，这一片挨着起事一方的，一并转入起事一方；
// 本来就是飞地、不挨着起事一方的不在此列(归飞地审计，见 ExclaveMaintenanceSystem)；起事之前就走不到都城的也不在此列。
public static class SecessionSpread
{
    private const int MaxCities = 4;

    // rebellion：起事(跟着走的城按原版起事那样易主)；欠饷撤守自立为 false
    public static int Spread(Kingdom rebel, City seed, Kingdom former, bool rebellion = true)
    {
        if (rebel?.data == null || rebel.isRekt() || seed?.data == null || former?.data == null || former.isRekt() ||
            rebel == former || PettyRebellion.IsPetty(rebel)) return 0;
        int joined = Contiguous(rebel, seed, former, rebellion);
        return joined + CutOff(rebel, former, rebellion);
    }

    private static int Contiguous(Kingdom rebel, City seed, Kingdom former, bool rebellion)
    {
        KingdomTitle title = seed.GetTitle();
        if (title == null || title.isRekt()) return 0;
        var frontier = new Queue<City>();
        var seen = new HashSet<City> { seed };
        frontier.Enqueue(seed);
        int joined = 0;
        while (frontier.Count > 0 && joined < MaxCities)
        {
            City city = frontier.Dequeue();
            foreach (City next in (IEnumerable<City>)city.neighbours_cities ?? Array.Empty<City>())
            {
                if (joined >= MaxCities) break;
                if (next?.data == null || next.isRekt() || !seen.Add(next) || next.kingdom != former ||
                    next == former.capital || next.GetTitle() != title || !CityStabilitySystem.Disaffected(next)) continue;
                next.joinAnotherKingdom(rebel, pCaptured: false, pRebellion: rebellion);
                if (next.kingdom != rebel) continue;
                joined++;
                frontier.Enqueue(next);
            }
        }
        if (joined > 0)
            LogService.LogInfo($"[EmpireCraft][自立成片] {rebel.GetKingdomName()} 起于 {seed.data.name}，同一法理里 {joined} 座城跟着脱离 {former.GetKingdomName()}");
        return joined;
    }

    private static Kingdom Realm(Kingdom kingdom) => kingdom?.GetEmpire()?.CoreKingdom ?? kingdom;

    private static IEnumerable<City> Neighbours(City city) => (IEnumerable<City>)city.neighbours_cities ?? Array.Empty<City>();

    // 被起事隔断的城：从原主都城沿本国(帝国按整个帝国)相邻的城走不到、与都城同在一块陆地、所在那一片挨着起事一方的，转入起事一方
    private static int CutOff(Kingdom rebel, Kingdom former, bool rebellion)
    {
        City capital = former.capital;
        WorldTile seat = capital?.getTile();
        if (seat == null || capital.kingdom != former || rebel.isRekt()) return 0;
        Kingdom realm = Realm(former);
        var queue = new Queue<City>();
        HashSet<City> Reach(bool throughRebel)
        {
            var reached = new HashSet<City> { capital };
            queue.Enqueue(capital);
            while (queue.Count > 0)
                foreach (City next in Neighbours(queue.Dequeue()))
                    if (next?.data != null && !next.isRekt() && next.kingdom?.data != null &&
                        (Realm(next.kingdom) == realm || throughRebel && next.kingdom == rebel) && reached.Add(next))
                        queue.Enqueue(next);
            return reached;
        }
        HashSet<City> now = Reach(false);
        // 起事之前就走不到都城的(隔着无人区、山的城，邻城表里本来就不连着)不算被这次起事隔断。以前不分，满洲各城隔着
        // 兴安岭、长白山的无人区，邻城表里本来就是一块块的，北边一城闹饥荒起事，挨着的一大片三四十座城全被算成隔断，
        // 清开局两三年就把满洲老家整个丢了
        HashSet<City> before = Reach(true);
        var stranded = new HashSet<City>(former.cities.Where(city => city?.data != null && !city.isRekt() && !now.Contains(city) &&
                                                                     before.Contains(city) &&
                                                                     city.getTile() is WorldTile at && at.isSameIsland(seat)));
        if (stranded.Count == 0) return 0;
        var done = new HashSet<City>();
        var names = new List<string>();
        foreach (City start in stranded.ToList())
        {
            if (!done.Add(start)) continue;
            var part = new List<City> { start };
            queue.Enqueue(start);
            while (queue.Count > 0)
                foreach (City next in Neighbours(queue.Dequeue()))
                    if (stranded.Contains(next) && done.Add(next))
                    {
                        part.Add(next);
                        queue.Enqueue(next);
                    }
            if (!part.Any(city => Neighbours(city).Any(next => next?.kingdom == rebel))) continue;
            // 被隔开的一片比起事一方还大的不跟：小股起事吞不下一大片，那一片留给原主当飞地(归飞地审计)。以前满洲北边一城闹饥荒，
            // 连同跟着走的四座城，一口气隔断带走二三十座，清开局两三年就丢了满洲老家
            if (part.Count > Math.Max(MaxCities, rebel.cities?.Count ?? 0)) continue;
            foreach (City city in part)
            {
                if (city.kingdom != former) continue;
                city.joinAnotherKingdom(rebel, pCaptured: false, pRebellion: rebellion);
                if (city.kingdom == rebel) names.Add(city.data.name);
            }
        }
        if (names.Count > 0)
            LogService.LogInfo($"[EmpireCraft][叛乱隔断] {rebel.GetKingdomName()} 起事隔断了 {former.GetKingdomName()} 的 {string.Join("、", names)}，一并转入");
        return names.Count;
    }
}

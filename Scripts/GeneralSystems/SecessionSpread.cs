using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 自立按法理成片(用户 2026-10-27 嫌版图太碎，问答四条全选)：一座城起事、或欠饷撤守自立时，同一个王国法理里挨着它的、
// 原主手里的城，人心也已经散了的(开着财政看实际稳定度不到起事门槛，不开看忠诚为负)，一并跟着走——至多 MaxCities 座，
// 不带原主的都城。分封诸侯国里的小乱(PettyRebellion)本来就只一城一邑，不跟。
public static class SecessionSpread
{
    private const int MaxCities = 4;

    // rebellion：起事(跟着走的城按原版起事那样易主)；欠饷撤守自立为 false
    public static int Spread(Kingdom rebel, City seed, Kingdom former, bool rebellion = true)
    {
        if (rebel?.data == null || rebel.isRekt() || seed?.data == null || former?.data == null || former.isRekt() ||
            rebel == former || PettyRebellion.IsPetty(rebel)) return 0;
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
}

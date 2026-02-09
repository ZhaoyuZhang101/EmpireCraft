using System;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Regimes;

namespace EmpireCraft.Scripts.AI.KingdomAI;

public class EmpireCraftKingdomBehCheckReligionKingdom: GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Kingdom pKingdom)
    {
        var ked = KingdomExtension.GetOrCreate(pKingdom);
        if (ked != null && ked.last_religion_check_ts > 0)
        {
            if (Date.getMonthsSince(ked.last_religion_check_ts) < 1) return BehResult.Continue;
        }
        var regime = pKingdom.GetRegime();
        if (regime == null) return BehResult.Continue;
        if (pKingdom.GetKingdomType() != KingdomType.Feudalism_papal_state) return BehResult.Continue;
        var saintCity = pKingdom.religion?.GetCity();
        if (saintCity == null)
        {
            regime.SetReligionLevel(ReligionLevel.Medium);
            if (ked != null) ked.last_religion_check_ts = World.world.getCurWorldTime();
            return BehResult.Continue;
        }

        if (pKingdom.cities.Contains(saintCity))
        {
            if (!saintCity.isCapitalCity())
            {
                pKingdom.setCapital(saintCity);
            }
        }
        else
        {
            regime.SetReligionLevel(ReligionLevel.Medium);
        }
        if (ked != null) ked.last_religion_check_ts = World.world.getCurWorldTime();
        return BehResult.Continue;
    }
}

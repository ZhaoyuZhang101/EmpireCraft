using System.Collections.Generic;

namespace EmpireCraft.Scripts.GeneralSystems;

// Reuse one population snapshot across all factions in a monthly agenda pass.
public sealed class PartyOpeningPressure
{
    public PartyIdeology? Challenger;
    public long Population;
    public long GovernmentPeople;
    public long ChallengerPeople;
    public bool HasChallenge => Challenger.HasValue && ChallengerPeople > GovernmentPeople;
    public float LeadShare => HasChallenge && Population > 0
        ? (float)(ChallengerPeople - GovernmentPeople) / Population : 0f;
}

public static class PartyOpeningRules
{
    public static PartyOpeningPressure Measure(IReadOnlyDictionary<PartyIdeology, int> counts,
        PartyIdeology government)
    {
        var result = new PartyOpeningPressure();
        if (counts == null) return result;
        foreach (var pair in counts)
        {
            if (pair.Value <= 0) continue;
            result.Population += pair.Value;
            if (pair.Key == government) result.GovernmentPeople = pair.Value;
            else if (pair.Value > result.ChallengerPeople ||
                     pair.Value == result.ChallengerPeople && (int)pair.Key < (int)result.Challenger.GetValueOrDefault())
            {
                result.Challenger = pair.Key;
                result.ChallengerPeople = pair.Value;
            }
        }
        return result;
    }
}

using System;

namespace EmpireCraft.Scripts.GeneralSystems;

// 值类型快照：不包含城市、单位、人口组或 Unity 对象。
public readonly struct CityStabilityInput : IEquatable<CityStabilityInput>
{
    public readonly int Population, Households, Loyalty, Legitimacy, GovernancePolicy, GarrisonPolicy;
    public readonly float Corruption, Sentiment, Landless, Grievance;
    public readonly bool Famine, ArmyEnabled;
    public readonly float PeoplePerFiscalUnit;

    public CityStabilityInput(int population, int households, int loyalty, int legitimacy,
        float corruption, float sentiment, float landless, float grievance, bool famine,
        bool armyEnabled, int governancePolicy, int garrisonPolicy, float peoplePerFiscalUnit = 1f)
    {
        Population = population; Households = households; Loyalty = loyalty; Legitimacy = legitimacy;
        Corruption = corruption; Sentiment = sentiment; Landless = landless; Grievance = grievance;
        Famine = famine; ArmyEnabled = armyEnabled; GovernancePolicy = governancePolicy; GarrisonPolicy = garrisonPolicy;
        PeoplePerFiscalUnit = float.IsNaN(peoplePerFiscalUnit) || float.IsInfinity(peoplePerFiscalUnit)
            ? 1f : Math.Max(1f, peoplePerFiscalUnit);
    }

    public bool Equals(CityStabilityInput other) => Population == other.Population && Households == other.Households &&
        Loyalty == other.Loyalty && Legitimacy == other.Legitimacy && Corruption == other.Corruption &&
        Sentiment == other.Sentiment && Landless == other.Landless && Grievance == other.Grievance &&
        Famine == other.Famine && ArmyEnabled == other.ArmyEnabled && GovernancePolicy == other.GovernancePolicy &&
        GarrisonPolicy == other.GarrisonPolicy && PeoplePerFiscalUnit == other.PeoplePerFiscalUnit;
}

public readonly struct CityStabilityBudget
{
    public readonly float Natural, Governance;
    public readonly int Guards;
    public readonly double GovernanceAnnual, GuardMonthly;
    public CityStabilityBudget(float natural, float governance, int guards, double annual, double guardMonthly)
    { Natural = natural; Governance = governance; Guards = guards; GovernanceAnnual = annual; GuardMonthly = guardMonthly; }
}

public static class CityStabilityMath
{
    private static float Policy(int policy, float automatic) => policy < 0 ? automatic :
        policy == 0 ? 0f : policy == 1 ? 1f : 1.5f;

    public static CityStabilityBudget Compute(CityStabilityInput input)
    {
        float natural = CityStabilityRules.Natural(input.Loyalty, input.Legitimacy, input.Corruption,
            input.Sentiment, input.Landless, input.Famine, input.Grievance);
        float governance = Policy(input.GovernancePolicy, natural < 55f ? 1.5f : 1f);
        int guards = CityStabilityRules.RequiredGarrison(input.Population, natural,
            input.ArmyEnabled ? Policy(input.GarrisonPolicy, 1f) : 0f);
        return new CityStabilityBudget(natural, governance, guards,
            CityStabilityRules.GovernanceAnnual(input.Households, natural, input.Corruption, governance),
            CityStabilityRules.GarrisonAnnual(guards, natural) / input.PeoplePerFiscalUnit / 12d);
    }

    public static CityStabilityBudget[] Compute(CityStabilityInput[] inputs)
    {
        var results = new CityStabilityBudget[inputs.Length];
        for (int i = 0; i < inputs.Length; i++) results[i] = Compute(inputs[i]);
        return results;
    }
}

namespace EmpireCraft.Scripts.Data;

public sealed class CityStabilityData
{
    public long owner_id = -1L;
    public double last_update = -1d;
    public float stability = 50f, natural_stability = 50f, governance_quality;
    public float last_change, suppression, garrison_funding;
    public int governance_policy = -1, garrison_policy = -1;
    public int garrison_required, garrison_actual, garrison_assigned, funded_slots;
    public int governance_due, governance_paid, garrison_due, garrison_paid, military_due, military_paid;
    public double governance_carry, garrison_carry, military_carry;
    public int unfunded_months;
    public long arrears;
    public long governance_arrears, garrison_arrears, military_arrears;
    public bool withdrawn;
    public string pressure_detail = "";
    public double last_massacre = -1d;
    public float fear;
}

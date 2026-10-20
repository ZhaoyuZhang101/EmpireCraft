namespace EmpireCraft.Scripts.Data;

// 多京(见 MultiCapitalSystem)的一京，存在 EmpireData.jing：京城、所据的帝国法理、相对主京的方位(主京为空)、设京的时间
public sealed class JingRecord
{
    public long city_id = -1L;
    public long core_id = -1L;
    public string direction = "";
    public double since = -1d;
}

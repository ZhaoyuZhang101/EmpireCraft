namespace EmpireCraft.Scripts.Data;

// 起事前的快照：离开帝国、拆城或改名之后仍能查到当时的原因。
public sealed class RebellionCauseData
{
    public string reason_key = "";
    public string detail = "";
    public string origin_name = "";
    public string city_name = "";
    public long origin_empire_id = -1L;
    public int legitimacy = -1;
    public int? loyalty;
    public float? stability;
    public double timestamp = -1d;
}

namespace EmpireCraft.Scripts.Data;

// 科技革新(见 TechDriveSystem)：一个帝国同时办一项，由一派倡议，占一个研究位、国库每年另拨经费直接推进度
public sealed class TechDriveState
{
    public string tech_id = "";
    public string sponsor_faction_id = "";
    public string sponsor_name = "";
    public double started = -1d;
    public double last_funded = -1d;
    // 累计拨的经费(两)、累计直接推进的进度(占费用的比例)、拨不出钱的年数
    public int invested;
    public float added;
    public int unfunded_years;
    public bool forced;
}

// 办完的一项：done 研成 / abandoned 久无成效或倡议的一派散了 / stopped 神力喊停
public sealed class TechDriveRecord
{
    public string tech_id = "";
    public string sponsor_name = "";
    public string outcome = "";
    public double started = -1d;
    public double closed = -1d;
    public int invested;
}

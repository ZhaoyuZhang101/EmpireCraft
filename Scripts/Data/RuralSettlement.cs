namespace EmpireCraft.Scripts.Data;

// 城郊的一处镇或村(见 RuralSettlementSystem)：钉在 (x, y) 那一格上，归落在谁的地上的城
public sealed class RuralSettlement
{
    public int id;
    public int x, y;
    public bool town;
    // 镇的大小(格)、有没有矮墙；村不用
    public int size;
    public bool walled;
    public string name = "";
    public double founded = -1d;
    // 毁于兵火的时间(-1 = 完好)
    public double ruined_at = -1d;
    // 彻底抹去了(见 RuralSettlementSystem.Erase)：不画、不算数、不重建，只留着占住原处，城郊不再在这里另建
    public bool cleared;
}

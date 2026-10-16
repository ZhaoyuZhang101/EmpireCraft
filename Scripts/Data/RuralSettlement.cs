namespace EmpireCraft.Scripts.Data;

// 城郊的一处镇或村(见 RuralSettlementSystem)：钉在 (x, y) 那一格上，归落在谁的地上的城
public sealed class RuralSettlement
{
    public int id;
    public int x, y;
    public bool town;
    // 镇的大小(格)、有没有矮墙；村不用；关城的等级(1~3)
    public int size;
    // 关城、军城(用户 2026-10-17 "城=关城/军城"：只有城墙、驻军，不住百姓，见 RuralSettlementSystem.FortFactor)
    public bool fort;
    public bool walled;
    public string name = "";
    public double founded = -1d;
    // 毁于兵火的时间(-1 = 完好)
    public double ruined_at = -1d;
    // 布局种子(0 = 按位置；「换个样子」换一个，见 SettlementWindow)
    public long seed;
    // 彻底抹去了(见 RuralSettlementSystem.Erase)：不画、不算数、不重建，只留着占住原处，城郊不再在这里另建
    public bool cleared;
}

namespace EmpireCraft.Scripts.Data;

// 一座工厂(见 FactorySystem)：钉在 (x, y) 那一格上(厂区的中心)，归落在谁的地上的城
public sealed class FactorySite
{
    public int id;
    public int x, y;
    // 大小：1 小厂、2 中厂、3 大厂(厂区边长 4、6、8 格)
    public int size = 1;
    public double founded = -1d;
}

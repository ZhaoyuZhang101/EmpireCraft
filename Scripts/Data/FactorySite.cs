using System.Collections.Generic;

namespace EmpireCraft.Scripts.Data;

// 一片工厂区(见 FactorySystem)：钉在 (x, y) 那一格上(厂区的中心)，归落在谁的地上的城
public sealed class FactorySite
{
    public int id;
    public int x, y;
    // 大小：1 小厂区、2 中厂区、3 大厂区(边长 4、6、8 格，至多 2、4、6 座厂)
    public int size = 1;
    public double founded = -1d;
    public string name = "";
    // 布局种子(「换个样子」换一个)
    public long seed;
    // 官营(国库出钱建的，见 EnterpriseSystem.Fund)；民营为 false
    public bool state;
    // 神力手动放的(营地里也不拆，见 FactorySystem.Grow)
    public bool manual;
    // 区里的厂
    public List<FactoryUnit> units = new();
}

// 一座厂：门类(火药厂、钢铁厂……，见 FactorySystem.Types)与等级(1~3)
public sealed class FactoryUnit
{
    public string type = "";
    public int level = 1;
}

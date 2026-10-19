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
    // 毡帐村镇(游牧营地的城立的，见 RuralSettlementSystem.YurtGround)：只立在旱地上、只放牧、画成一圈圈毡帐；
    // 随地易主后照旧是毡帐(钉在地上的是这些人家，不随新主人改样)
    public bool yurt;
    public string name = "";
    // 名字是手改的(改名后不再按文化重起)；按文化起名的版本(0 = 旧版写死的词库，读档时改按文化重起，见 SettlementNaming)
    public bool named_by_hand;
    public int naming_version;
    public double founded = -1d;
    // 毁于兵火的时间(-1 = 完好)
    public double ruined_at = -1d;
    // 布局种子(0 = 按位置；「换个样子」换一个，见 SettlementWindow)
    public long seed;
    // 彻底抹去了(见 RuralSettlementSystem.Erase)：不画、不算数、不重建，只留着占住原处，城郊不再在这里另建
    public bool cleared;
    // 生计(见 LivelihoodSystem：0 农、1 林、2 矿、3 牧、4 渔；-1 = 还没定，按四周地形补定)
    public int trade = -1;
    // 生计是在窗口里手动改的(之后不再按规则自动改)；按第几版规则定的(旧档 0，见 LivelihoodSystem.RulesVersion)
    public bool trade_manual;
    public int trade_rules;
    // 官营(国库出钱建的，见 EnterpriseSystem.Fund)；民营为 false
    public bool state;
}

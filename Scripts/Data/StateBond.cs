namespace EmpireCraft.Scripts.Data;

// 一笔国债：向某城百姓(民间现金)借的钱。债权人是那座城的百姓，城易主也照付；城没了算违约(见 StateDebtSystem)
public sealed class StateBond
{
    public long city_id = -1L;
    public long principal;
    public float rate;
    public double since = -1d;
    // 月息不足一枚的零头
    public double interest_carry;
}

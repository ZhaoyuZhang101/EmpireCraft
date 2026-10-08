using System.Collections.Generic;

namespace EmpireCraft.Scripts.Data;

// 只保存有界月度汇总，旧档不追补历史收支。
public sealed class TreasuryData
{
    public double started = -1d;
    public List<TreasuryPeriod> periods = new();
}

public sealed class TreasuryPeriod
{
    public double timestamp;
    public Dictionary<string, long> income = new();
    public Dictionary<string, long> expense = new();
}

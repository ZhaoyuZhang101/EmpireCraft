using System.Collections.Generic;

namespace EmpireCraft.Scripts.Data;

// 借道的存档(见 MilitaryAccessSystem)：主国许借道国借道，单向各记一条——起止时间、长期还是短期、是不是神力定的
public sealed class MilitaryAccessData
{
    public List<MilitaryAccessGrant> grants = new();
}

public sealed class MilitaryAccessGrant
{
    public long host = -1L, guest = -1L;
    public double since = -1d, until = -1d;
    public bool long_term, by_god;
}

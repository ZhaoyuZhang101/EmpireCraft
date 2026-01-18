using System.Collections.Generic;
using EmpireCraft.Scripts.HelperFunc;

namespace EmpireCraft.Scripts.Layer;

public class CoreEmpireTitle
{
    public long CoreTitleId;
    public string Name;
    public List<long> Titles = new();

    public void Init(Empire pEmpire)
    {
        Name = pEmpire.name;
        CoreTitleId = OverallHelperFunc.IdGenerator.NextId();
        Titles.Clear();
    }
}
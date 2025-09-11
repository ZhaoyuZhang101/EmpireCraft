using System;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Regimes;

namespace EmpireCraft.Scripts.AI.CityAI;

public class EmpireCraftCityBehCheckOffice:GameAICityBase
{
    public override Type OriginalBeh => GetType();
    public override BehResult execute(City pCity)
    {
        if (pCity.hasKingdom())
        {
            
        }
        return BehResult.Continue;
    }

    public void UpdateOffice(City pCity)
    {
        if (!pCity.hasKingdom()) return;
        Kingdom pKingdom = pCity.kingdom;
        if (pCity.kingdom.GetRegime()==null) return;
        var regime = pKingdom.GetRegime();
        OfficeObject officeObject = new OfficeObject();
        switch (regime.type)
        {
            case RegimeType.Arabic:
                break;
            case RegimeType.Feudalism:
                break;
            case RegimeType.LvLing:
                break;
            case RegimeType.Republic:
                break;
            case RegimeType.ZhouFeudalism:
                break;
        }
    }
}
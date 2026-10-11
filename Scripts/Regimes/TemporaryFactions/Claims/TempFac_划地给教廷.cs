using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 划地给教廷：神权派请皇帝把与教宗国接壤的一座城(非都城)献给同一信仰的教宗国，换取教廷的支持(天命 +5)。
public class TempFac_划地给教廷 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_划地给教廷();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override void Init(FixedFaction faction)
    {
        base.Init(faction);
        base.Hide = true;
    }

    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        City city = GetCityTarget();
        Kingdom papacy = city == null ? null : FindPapacy(GetEmpire(), city);
        if (city != null && papacy != null)
        {
            string cityName = city.GetCityName();
            city.joinAnotherKingdom(papacy);
            GetEmpire().AddMandate(5);
            CompletionOutcome = string.Format(LM.Get("claim_outcome_划地给教廷_city"), cityName, papacy.GetKingdomFullName());
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        Kingdom core = empire?.CoreKingdom;
        if (core?.cities == null || empire.Religion == null || empire.Religion.isRekt()) return false;
        City city = core.cities.Where(candidate => candidate?.data != null && !candidate.isRekt() &&
                                                   candidate != core.capital && !candidate.isCapitalCity() &&
                                                   FindPapacy(empire, candidate) != null)
            .OrderBy(candidate => candidate.getPopulationPeople()).FirstOrDefault();
        if (city == null) return false;
        SetCityTarget(city);
        return true;
    }

    // 与该城接壤、信同一宗教的教宗国
    private static Kingdom FindPapacy(Empire empire, City city) =>
        city.neighbours_cities?.Select(other => other?.kingdom)
            .FirstOrDefault(kingdom => kingdom != null && !kingdom.isRekt() && kingdom != city.kingdom &&
                                       kingdom.GetKingdomType() == Regimes.KingdomType.Feudalism_papal_state &&
                                       kingdom.religion == empire.Religion);
}

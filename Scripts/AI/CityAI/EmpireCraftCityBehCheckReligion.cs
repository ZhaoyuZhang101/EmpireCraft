using System;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.GeneralSystems;

namespace EmpireCraft.Scripts.AI.CityAI;

public class EmpireCraftCityBehCheckReligion: GameAICityBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(City pCity)
    {
        if (!pCity.hasKingdom()) return BehResult.Continue;
        if (pCity.kingdom.IsInEmpire())  return BehResult.Continue;
        var culture = CultureService.GetMainCulture(pCity);
        if (!CultureService.IsValidCulture(culture)) culture = "Western";
        // 圣城神权自立：罗马一线(教会与王权分立的传统)；以前查文化配置的政体是不是封建，西方(古典城邦起步)漏了
        if (InstitutionSystem.GetCultureLine(culture) != "Roma") return BehResult.Continue;
        if (pCity.hasReligion())
        {
            Religion pReligion = pCity.getReligion();
            // 没有教阶的宗教、地方教会(同天下的本土小教)没有教廷，圣城不神权自立(见 ReligionFaith、ReligionSphere)
            if (pReligion.GetCity() == pCity && ReligionFaith.HasHierarchy(pReligion) && !ReligionSphere.IsLocalChurch(pReligion))
            {
                if (pCity.kingdom?.getReligion() != pReligion) return BehResult.Continue;
                // 圣城本就是一城之国的都城(创教之城多是城邦都城)：这国就是教廷所在，整国政教合一，封建化后即为教宗国。
                // 以前都城一律不管，圣城之国一直是世俗的城邦、公国，教宗国从来出不来；多城之国的都城不动，等本文化施行教权至上
                if (pCity.isCapitalCity())
                {
                    Regime seat = pCity.kingdom.GetRegime();
                    if (pCity.kingdom.cities.Count == 1 && seat?.options != null &&
                        seat.options.ContainsKey("option_religion_type") && seat.GetReligionLevel() < ReligionLevel.High)
                        seat.SetReligionLevel(ReligionLevel.High);
                    return BehResult.Continue;
                }
                // 圣城人心不稳(稳定低于起事线)才由神职自立，免得世俗国家无端丢城
                if (CityStabilitySystem.Effective(pCity) >= CityStabilityRules.RebellionThreshold) return BehResult.Continue;
                // 由城主出面自立。以前取城里第一个实体：无小人模式下可能是船或路过的要人，城里没有实体时直接抛异常
                Actor theocrat = Theocrat(pCity);
                if (theocrat == null) return BehResult.Continue;
                var kingdom = pCity.makeOwnKingdom(theocrat);
                if (kingdom == null) return BehResult.Continue;
                Regime regime = kingdom.GetRegime();
                regime.SetReligionLevel(ReligionLevel.High);
                if (pCity.hasTitle())
                {
                    var cTitle = pCity.GetTitle();
                    if (cTitle.getCities().Count() == 1)
                    {
                        return BehResult.Continue;
                    }
                }
                KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER.newKingdomTitle(pCity);
                if (title == null) return BehResult.Continue;
                pCity.kingdom?.SetMainTitle(title);
                pCity.kingdom?.king?.AddOwnedTitle(title);
            }
        }
        return BehResult.Continue;
    }

    private static Actor Theocrat(City city)
    {
        static bool Fit(Actor actor) => actor?.data != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() &&
                                        !actor.isKing() && !CityPopulationSystem.IsVehicle(actor);
        if (Fit(city.leader)) return city.leader;
        return city.units?.FirstOrDefault(Fit);
    }
}

using System.Linq;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_宗教同化 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_宗教同化();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        var kingdom = GetKingdomTarget();
        Empire empire = GetEmpire();
        if (kingdom?.data != null && !kingdom.isRekt() && empire?.Religion != null &&
            !empire.Religion.isRekt() && !CheckRebelling(kingdom))
        {
            kingdom.setReligion(empire.Religion);
            kingdom.units?.ForEach(u =>
            {
                if (u?.data != null && !u.isRekt()) u.setReligion(empire.Religion);
            });
            // 城一并改宗(以前只改国，城个个成了异教之城)：都城、无教之城、与帝国国教同天下的城当场改；
            // 外天下的城留给国教推行(用户 2026-11-06 "古典时代全世界都信基督")
            int converted = 0, spared = 0;
            foreach (City city in kingdom.cities.ToList())
            {
                if (city?.data == null || city.isRekt() || city.religion == empire.Religion) continue;
                if (city != kingdom.capital && city.hasReligion() &&
                    !ReligionTenets.AtHome(empire.Religion, ReligionSphere.SphereOfCity(city)))
                {
                    spared++;
                    continue;
                }
                BackgroundReligionSystem.Convert(city, empire.Religion, city.religion);
                converted++;
            }
            ReligionDiagnostics.Add(ReligionDiagnostics.Assimilated, converted);
            ReligionDiagnostics.Add(ReligionDiagnostics.AssimilationSpared, spared);
        }
        End();
    }

    // 目标：不奉帝国国教的成员(连同朝廷自己)。规则在 ClaimRules.json 的 target(见 ClaimTargetRules)
    public override bool CheckCondition() => TargetReady() && PickRuleTarget();

    protected override bool TargetReady()
    {
        Empire empire = GetEmpire();
        return empire != null && !empire.IsArchived() && !empire.isRekt() &&
               empire.Religion != null && !empire.Religion.isRekt() && empire.kingdoms_list != null;
    }
}

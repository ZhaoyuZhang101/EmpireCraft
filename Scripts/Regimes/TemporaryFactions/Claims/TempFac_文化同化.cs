using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 原名"汉化"：这个决议本来就不是专指汉化，而是把帝国官方文化（不管具体是哪个）
// 推广到境内的其他文化人口，所以改名成跟"宗教同化"对称的"文化同化"。
//
// 这条决议发布一项有明确目标文化的长期任务。行政推动每城每年最多一次；人口达到
// 城市文化转变门槛后停止硬推，转入自然巩固，最后仍由城市主流文化 Plot 正式承认。
public class TempFac_文化同化 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_文化同化();
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
        Empire empire = GetEmpire();
        string targetCulture = CultureService.GetEmpireDefaultCulture(empire);
        Kingdom administration = GetKingdomTarget();
        if (administration?.data != null && !administration.isRekt())
        {
            if (CultureService.AssignCulturalAssimilationDuty(administration, empire, targetCulture))
                TranslateHelper.LogCulturalAssimilationDutyAssigned(administration, targetCulture);
        }
        else
        {
            City city = GetCityTarget();
            if (city?.data != null && !city.isRekt())
            {
                if (CultureService.AssignCulturalAssimilationDuty(city, empire, targetCulture))
                    TranslateHelper.LogCulturalAssimilationDutyAssignedCity(city, targetCulture);
            }
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (!CultureService.IsAssimilationEnabled() || empire == null || empire.IsArchived() || empire.isRekt())
            return false;
        string sourceCulture = CultureService.GetEmpireDefaultCulture(empire);
        if (!CultureService.IsValidCulture(sourceCulture)) return false;

        Kingdom kingdomTarget = GetCandidateAdministrations(empire, sourceCulture).FirstOrDefault();
        if (kingdomTarget != null)
        {
            SetKingdomTarget(kingdomTarget);
            return true;
        }

        City cityTarget = GetCandidateDirectCities(empire, sourceCulture).FirstOrDefault();
        if (cityTarget != null)
        {
            SetCityTarget(cityTarget);
            return true;
        }
        return false;
    }

    private static IEnumerable<Kingdom> GetCandidateAdministrations(Empire empire, string targetCulture)
    {
        IEnumerable<Kingdom> members = (empire.kingdoms_list ?? new List<Kingdom>())
            .Where(member => member?.data != null && !member.isRekt() && !member.IsEmpire())
            .OrderBy(member => member.id);
        foreach (Kingdom member in members)
        {
            CultureService.UpdateCulturalAssimilationDuty(member);
            if (member.GetOrCreate().cultural_assimilation_duty) continue;
            if (member.GetAdministrativeTitle() == null) continue;
            if (!CultureService.AdministrationNeedsCulturalAssimilation(member, targetCulture)) continue;
            yield return member;
        }
    }

    // 帝国直辖：没有下放给任何行政区、由帝国核心王国自己统治的城市。这类城市
    // 没有"行政区长官"这一层，所以只要城市本身还没被指定过、且主流文化尚未
    // 变成任务目标，就可以被这条决议直接点名，交给城主
    // （或没有城主时的皇帝本人）自己负责。
    private static IEnumerable<City> GetCandidateDirectCities(Empire empire, string sourceCulture)
    {
        Kingdom core = empire.CoreKingdom;
        if (core?.cities == null) yield break;
        CultureService.UpdateCulturalAssimilationDuty(core);
        foreach (City city in core.cities.OrderBy(c => c.id))
        {
            if (city?.data == null || city.isRekt()) continue;
            if (city.GetOrCreate().cultural_assimilation_duty) continue;
            if (!CultureService.CityNeedsCulturalAssimilation(city, sourceCulture)) continue;
            yield return city;
        }
    }
}

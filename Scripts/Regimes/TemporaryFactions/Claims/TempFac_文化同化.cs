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

    public override bool CheckCondition() => TargetReady() && PickRuleTarget();

    // 谁能当目标、朝廷自己怎么挑写在 ClaimRules.json 的 target(见 ClaimTargetRules，政体编辑器的决议页可改)；这里只留决议自己的前提
    protected override bool TargetReady()
    {
        Empire empire = GetEmpire();
        return CultureService.IsAssimilationEnabled() && empire != null && !empire.IsArchived() && !empire.isRekt() &&
               CultureService.IsValidCulture(CultureService.GetEmpireDefaultCulture(empire));
    }
}

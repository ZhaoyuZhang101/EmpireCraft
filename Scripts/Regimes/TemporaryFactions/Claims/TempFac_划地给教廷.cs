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
        Kingdom papacy = city == null ? null : GeneralSystems.ClaimTargetRules.PapacyNear(GetEmpire(), city);
        if (city != null && papacy != null)
        {
            string cityName = city.GetCityName();
            city.joinAnotherKingdom(papacy);
            GetEmpire().AddMandate(5);
            CompletionOutcome = string.Format(LM.Get("claim_outcome_划地给教廷_city"), cityName, papacy.GetKingdomFullName());
        }
        End();
    }

    public override bool CheckCondition() => TargetReady() && PickRuleTarget();

    // 谁能当目标、朝廷自己怎么挑写在 ClaimRules.json 的 target(见 ClaimTargetRules，政体编辑器的决议页可改)；这里只留决议自己的前提
    protected override bool TargetReady()
    {
        Empire empire = GetEmpire();
        return empire?.CoreKingdom?.cities != null && empire.Religion != null && !empire.Religion.isRekt();
    }
}

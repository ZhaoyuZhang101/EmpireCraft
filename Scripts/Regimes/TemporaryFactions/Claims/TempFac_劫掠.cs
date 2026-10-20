using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 单国劫掠(朝堂诉求)：并进游牧联盟劫掠的规矩(见 NomadRaidSystem)——草原压力够(维京这类非游牧只算国库与粮食)才提，
// 挑一家比自己弱、过得了开战闸门、五年内没遭劫的接壤定居之国；开的是同一种劫掠之战(只抢不占，抢一城即退、满一年退兵、
// 可被买和)，只是没有盟友。出过兵三年内不再出兵(与每年的结盟劫掠共用)；诉求本身五年一次
public class TempFac_劫掠 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_劫掠();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }
    
    public override void Execute()
    {
        Empire empire = GetEmpire();
        Kingdom target = GetKingdomTarget();
        if (target != null && empire?.CoreKingdom != null) NomadRaidSystem.RaidFromClaim(empire.CoreKingdom, target);
        CountDown = 5;
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire?.CoreKingdom == null || empire.CoreKingdom.isRekt()) return false;
        // 有劫掠传统的文化才劫(草原、维京；阿兹特克、林地部族不劫，见 ClaimRules.json)
        if (!ClaimRules.HasTrait(empire, EmpireCraft.Scripts.Data.OnomasticsRule.RaiderTrait)) return false;
        Kingdom target = NomadRaidSystem.ClaimTarget(empire.CoreKingdom);
        if (target == null) return false;
        SetKingdomTarget(target);
        return true;
    }
}

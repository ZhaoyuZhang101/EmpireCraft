using EmpireCraft.Scripts.AI;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;
using EmpireCraft.Scripts.Layer;
namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

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
        if (target != null && empire?.CoreKingdom != null)
        {
            var war = DiplomacyHelpers.wars.newWar(empire.CoreKingdom, target, WarTypeLibrary.normal);
            war.SetEmpireWarType(EmpireWarType.劫掠);
        }
        // 同游牧扩张：五年一次
        CountDown = 5;
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire?.CoreKingdom == null || empire.CoreKingdom.isRekt()) return false;
        // 有劫掠传统的文化才劫(草原、维京；阿兹特克、林地部族不劫，见 ClaimRules.json)
        if (!GeneralSystems.ClaimRules.HasTrait(empire, EmpireCraft.Scripts.Data.OnomasticsRule.RaiderTrait)) return false;
        if (empire.CoreKingdom.hasEnemies()) return false;
        // 打仗太多、发不出军饷不劫；要比对方强，过开战闸门(停战、岁币、朝贡、同盟，见 WarGate)
        if (GeneralSystems.WarSituation.TooBusyForWar(empire.CoreKingdom) || GeneralSystems.CorpsSystem.CannotFundWar(empire.CoreKingdom)) return false;
        double power = GeneralSystems.WarSituation.Power(empire);
        var neighbours = empire.GetKingdomNeighbours();
        if (!neighbours.Any()) return false;
        // 劫异族：按文化不按政体(以前看政体不同——同族换了政体的也劫，别族同政体的反倒不劫)
        string own = GeneralSystems.CultureService.GetRealmCulture(empire.CoreKingdom);
        // 五年内遭劫过的不再劫(用户 2026-10-17 选"劫掠只拿三成国库"：同一家几年内不再劫)
        var target = neighbours.Find(k => k?.data != null && !k.isRekt() &&
                                          GeneralSystems.CultureService.GetRealmCulture(k) != own &&
                                          (k.GetOrCreate().raided_at < 0d || Date.getYearsSince(k.GetOrCreate().raided_at) >= 5) &&
                                          GeneralSystems.WarSituation.Power(k) < power &&
                                          GeneralSystems.WarGate.CanDeclare(empire.CoreKingdom, k));
        if (target!= null)
        {
            SetKingdomTarget(target);
            return true;
        }
        return false;
    }
}

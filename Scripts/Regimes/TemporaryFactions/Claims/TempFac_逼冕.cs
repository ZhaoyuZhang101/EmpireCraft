using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 逼冕(西方，见 RealmNamingSystem.Universal)：联合天下里未得普世之名、教廷好感不正的帝国，朝中兴兵逼教宗加冕。
// 打下教廷都城或圣城、或战胜，由和约加冕、授普世之名；打输正统 −5、十年不再逼冕。
// 只挂在联合天下的西方帝国的派系上(见 RealmNamingSystem.SyncCoercionClaim)
public class TempFac_逼冕 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_逼冕();
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
        Kingdom papacy = GetKingdomTarget();
        if (empire?.CoreKingdom != null && papacy != null && !papacy.isRekt() &&
            RealmNamingSystem.WantsCoercion(empire, out Kingdom current) && current == papacy)
        {
            War war = DiplomacyHelpers.wars.newWar(empire.CoreKingdom, papacy, WarTypeLibrary.normal);
            war?.SetEmpireWarType(EmpireWarType.逼冕);
        }
        CountDown = 5;
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (!RealmNamingSystem.WantsCoercion(empire, out Kingdom papacy)) return false;
        SetKingdomTarget(papacy);
        return true;
    }
}

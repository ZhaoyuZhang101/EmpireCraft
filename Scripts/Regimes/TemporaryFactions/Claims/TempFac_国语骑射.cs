using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 国语骑射(本俗反扑，见 CompositeEmpireService.ProclaimNativism)：统治族群游牧、尚未融完的征服王朝，融合度到 50 以上
// 或已走上从俗之路时，攘夷、同化派请重申本族语言、骑射——融合度 −10，部族正统 +10、属地正统 −10，二十年内不从俗；
// 三十年一次。廷议时攘夷、同化派赞成，融入、绥靖派反对。挂在哪些派系上见 SynchronizeMilitaryTraditionClaims
public class TempFac_国语骑射 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_国语骑射();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override int CooldownYears => 30;

    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        Empire empire = GetEmpire();
        string outcome = CompositeEmpireService.ProclaimNativism(empire);
        if (!string.IsNullOrEmpty(outcome)) CompletionOutcome = outcome;
        End();
    }

    public override bool CheckCondition() => CompositeEmpireService.CanProclaimNativism(GetEmpire()) && OffCooldown();

    public override float TargetStance(FixedFaction faction) => faction?.Type switch
    {
        FactionType.攘夷 or FactionType.同化 => 15f,
        FactionType.融入 or FactionType.绥靖 => -10f,
        _ => 0f
    };
}

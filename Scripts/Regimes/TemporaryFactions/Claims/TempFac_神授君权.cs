using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 神授君权：求教廷授"神圣"之名(见 PapacySystem.GrantHoly，教廷页按钮同用)。
// 以前要教宗国就在本帝国里(多数时候教宗国是独立的)，又读空的帝国国教，几乎从不触发；现在皇帝由教宗亲自加冕过
// (不是自行加冕)，或教廷就在本帝国手里(成员、附庸、教廷之囚)，即可提出(本文化还要施行教权至上，见 ClaimRules.json)
public class TempFac_神授君权 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_神授君权();
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
        if (empire?.data != null && !PapacySystem.HasHolyName(empire) && PapacySystem.HolyBlocker(empire) == null)
            PapacySystem.GrantHoly(empire);
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire?.data == null || PapacySystem.HasHolyName(empire) || PapacySystem.HolyBlocker(empire) != null) return false;
        Kingdom papacy = PapacySystem.Papacy(PapacySystem.ReligionOf(empire));
        if (papacy == null) return false;
        var state = PapacySystem.State(empire);
        bool crownedByPope = PapacySystem.IsCrowned(empire) && state != null && !state.self_crowned;
        if (!crownedByPope && !RealmNamingSystem.ControlsPapacy(empire, papacy)) return false;
        SetKingdomTarget(papacy);
        return true;
    }
}

using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 神授君权：帝国自称"神圣"(见 PapacySystem.GrantHoly，教廷页按钮同用)。
// 用户 2026-11-08 按史实改：神圣是帝国在与教廷争权时自称的(1157 巴巴罗萨)，不是教宗授的——持普世之名满百年、
// 与教廷正面相争过(叙任权之争、遭绝罚)才提(条件都在 PapacySystem.HolyBlocker)，不再要求皇帝由教宗加冕或握着教廷。
// 本文化要施行教权至上、政教协约或世俗等级会议之一(见 ClaimRules.json)
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
        // 目标挂教廷(争的是它)
        Kingdom papacy = PapacySystem.Papacy(PapacySystem.ReligionOf(empire));
        if (papacy == null) return false;
        SetKingdomTarget(papacy);
        return true;
    }
}

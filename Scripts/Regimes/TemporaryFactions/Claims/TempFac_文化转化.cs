using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using NeoModLoader.General;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

/// <summary>
/// "文治"取向的融入派系发起的决议：当某个（非当前）文化已经是法理首都的主流文化，
/// 并且至少是法理辖下三分之二城市的主流文化时，可以把它正式定为法理的默认文化。
/// 取代了以前 CultureService 里"占比够高就自动转换"的旧逻辑——现在只能通过这条决议来改变。
/// </summary>
public class TempFac_文化转化 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_文化转化();
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
        KingdomTitle title = GetTitleTarget();
        if (title != null && CultureService.FindTitleCultureConversionCandidate(title, out string candidate))
        {
            CultureService.ConvertTitleCulture(title, candidate);
        }
        End();
    }

    public override bool CheckCondition() => TargetReady() && PickRuleTarget();

    protected override bool TargetReady() => GetEmpire()?.kingdoms_list != null;

    // 候选：各成员国的主头衔里，法理首都与三分之二辖城的主流文化已经换了的(见 CultureService.FindTitleCultureConversionCandidate)
    public override List<ClaimTargetOption> BuiltinCandidates()
    {
        var result = new List<ClaimTargetOption>();
        var seen = new HashSet<KingdomTitle>();
        Empire empire = GetEmpire();
        if (empire?.kingdoms_list == null) return result;
        foreach (Kingdom kingdom in empire.kingdoms_list)
        {
            if (kingdom == null || kingdom.isRekt()) continue;
            KingdomTitle title = kingdom.GetMainTitle();
            if (title?.data == null || title.isRekt() || !seen.Add(title) ||
                !CultureService.FindTitleCultureConversionCandidate(title, out string candidate)) continue;
            ClaimTargetOption option = ClaimTargetOption.Of(title,
                string.Format(LM.Get("claim_target_tag_convert_to"), candidate.GetCultureTranslate()));
            option.Figure = string.Format(LM.Get("claim_target_cities"), title.city_list?.Count ?? 0);
            result.Add(option);
        }
        return result;
    }
}

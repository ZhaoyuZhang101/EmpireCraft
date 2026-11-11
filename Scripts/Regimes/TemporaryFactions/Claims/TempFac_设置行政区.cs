using System.Collections.Generic;
using EmpireCraft.Scripts.GeneralSystems;
using NeoModLoader.General;
using System.Linq;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;
using EmpireCraft.Scripts.Layer;
namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_设置行政区 : TemporaryFaction
{
    // 设治经费：每座城两份(见 ClaimTuning)
    public override int Budget => EmpireCraft.Scripts.GeneralSystems.ClaimTuning.Cost(GetEmpire(), type) *
                                  global::System.Math.Max(1, GetTitleTarget()?.city_list?.Count() ?? 1);

    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_设置行政区();
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
        KingdomTitle title = GetTitleTarget();
        if (title != null&&title.title_capital!=empire.CoreKingdom.capital)
        {
            Kingdom k = null;
            try
            {
                var king = empire.getUnits().ToList().FindAll(a => !a.isKing() && a.isAdult() && a.isUnitFitToRule())
                    .First();
                if (king != null)
                {
                    k = title.title_capital.makeOwnKingdom(king); 
                    empire.AddMandate(10);
                }
            }
            catch
            {
                LogService.LogInfo("设置行政区失败");
                End();
                return;
            }

            if (k != null)
            {
                k.SetRegimeType(empire.CoreKingdom?.GetRegime()?.type??RegimeType.LvLing);
                k.LoadRegime();
                Regime regime = k.GetRegime();
                regime.SetAllowDiplomacy(false);
                regime.SetLeaderSelectMethod(LeaderSelectMethod.Exam);
                foreach (var c in title.city_list)
                {
                    if (c==title.title_capital) continue;
                    c.joinAnotherKingdom(k);
                }
                empire.join(k, pForce:true);
                k.RemoveMainTitle();
                k.SetAdministrativeTitle(title);
                EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(k);
            }
        }
        CountDown = 2;
        End();
    }

    public override bool CheckCondition()
    {
        if (!TargetReady() || !PickRuleTarget()) return false;
        Acc = 30;
        return true;
    }

    protected override bool TargetReady()
    {
        Empire empire = GetEmpire();
        return empire?.CoreKingdom != null && empire.Emperor != null;
    }

    // 候选：皇帝名下、朝廷自己管着、首府不是京师的法理头衔
    public override List<ClaimTargetOption> BuiltinCandidates()
    {
        var result = new List<ClaimTargetOption>();
        Empire empire = GetEmpire();
        if (empire?.CoreKingdom == null || empire.Emperor == null) return result;
        foreach (long id in empire.Emperor.GetOwnedTitle())
        {
            KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER.get(id);
            if (title?.data == null || title.isRekt() || title.title_capital == null || title.title_capital.isRekt() ||
                title.control_kingdom != empire.CoreKingdom || title.title_capital == empire.CoreKingdom.capital) continue;
            ClaimTargetOption option = ClaimTargetOption.Of(title);
            option.Figure = string.Format(LM.Get("claim_target_cities"), title.city_list?.Count ?? 0);
            result.Add(option);
        }
        return result;
    }
}

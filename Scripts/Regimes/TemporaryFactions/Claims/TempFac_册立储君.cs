using System.Linq;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 册立储君(公共议题)：皇帝有两个以上可继位的子嗣时，派系请立本派属意的皇子为储君。
// 属意的皇子：本派的皇子；没有本派皇子而现储君属于别派时，改立没有派系的年长皇子。
// 廷议时皇子所属派系支持、现储君所属派系反对(见 TargetStance)。立下后越过继承法次序(见 EmpireCraftKingdomBehCheckHeir)。
public class TempFac_册立储君 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_册立储君();
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
        Actor heir = GetActorTarget();
        Kingdom core = empire?.CoreKingdom;
        if (core != null && heir != null && EmpireCraftKingdomBehCheckHeir.HeirCandidates(core).Contains(heir))
        {
            core.GetOrCreate().designated_heir_id = heir.getID();
            core.SetHeir(heir);
            CompletionOutcome = string.Format(LM.Get("claim_outcome_册立储君_named"), heir.getName());
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        Kingdom core = empire?.CoreKingdom;
        Regime regime = core?.GetRegime();
        FixedFaction faction = GetFaction();
        if (core == null || regime == null || faction == null || empire.Emperor == null ||
            regime.GetLeaderSelectMethod() != LeaderSelectMethod.Succession ||
            regime.GetSuccessionLaw() == SuccessionLawType.强者继承法) return false;
        var candidates = EmpireCraftKingdomBehCheckHeir.HeirCandidates(core);
        if (candidates.Count < 2) return false;
        Actor current = core.GetHeir();
        Actor pick = candidates.Where(actor => actor != current && actor.GetFaction() == faction)
            .OrderByDescending(actor => actor.getAge()).FirstOrDefault();
        FixedFaction currentFaction = current?.GetFaction();
        if (pick == null && currentFaction != null && currentFaction != faction)
            pick = candidates.Where(actor => actor != current && actor.GetFaction() == null)
                .OrderByDescending(actor => actor.getAge()).FirstOrDefault();
        if (pick == null || core.GetOrCreate().designated_heir_id == pick.getID()) return false;
        SetActorTarget(pick);
        return true;
    }

    // 立的是哪一派的皇子：皇子所属派系 +15，被换下的储君所属派系 -12
    public override float TargetStance(FixedFaction faction)
    {
        Actor heir = GetActorTarget();
        if (heir == null || faction == null) return 0f;
        float stance = heir.GetFaction() == faction ? 15f : 0f;
        Actor current = GetEmpire()?.CoreKingdom?.GetHeir();
        if (current != null && current != heir && current.GetFaction() == faction) stance -= 12f;
        return stance;
    }
}

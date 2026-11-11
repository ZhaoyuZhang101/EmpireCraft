using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 岁输金帛以求和(澶渊之盟式)：比割地温和，打不赢、又不愿再耗下去时才提。条件(见 WarSituation)：
//   · 战争已打满一年，国库有钱可付；
//   · 战局不利或久拖不决：这场仗丢了城且敌强于我(兵力 ≥ 1.2 倍)，或敌兵力 ≥ 1.8 倍，
//     或敌兵力 ≥ 1.5 倍且已打满三年(相持太久，劳民伤财)；旧存档开打的战争只看兵力与年数。
// 打赢了、或敌我相当时不会提出；开国雄主在位不纳贡求和(雄主志在一统，见 RulerTraitSystem)。
public class TempFac_提供岁币 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_提供岁币();
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
        Kingdom kingdom = GetKingdomTarget();
        if (kingdom != null)
        {
            Empire empire = GetEmpire();
            kingdom.JoinGivenAlliance(empire);
            kingdom.EndWarWith(empire.CoreKingdom);
            // 准奏时从国库支出的经费就是岁币，交给对方
            if (PaidBudget > 0)
            {
                kingdom.AddMoney(PaidBudget, TreasuryCategory.Tribute);
                CompletionOutcome = string.Format(GeneralSystems.CultureDoctrines.Text("claim_outcome_提供岁币_paid", GetEmpire()), PaidBudget,
                    kingdom.GetKingdomFullName());
            }
            EventRecorder.Record(empire, string.Format(LM.Get("tribute_peace_history"), empire.GetEmpireFullName(),
                kingdom.GetKingdomFullName()));
        }
        End();
    }

    public override bool CheckCondition() => TargetReady() && PickRuleTarget();

    // 谁能当目标、朝廷自己怎么挑写在 ClaimRules.json 的 target(见 ClaimTargetRules，政体编辑器的决议页可改)；这里只留决议自己的前提
    protected override bool TargetReady()
    {
        Kingdom core = GetEmpire()?.CoreKingdom;
        return core != null && !core.HasGivenAlliance() && core.GetMoney() > 0 && !RulerTraitSystem.FounderReigns(core);
    }
}

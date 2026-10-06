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
// 打赢了、或敌我相当时不会提出。
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
            EventRecorder.Record(empire, string.Format(LM.Get("tribute_peace_history"), empire.GetEmpireFullName(),
                kingdom.GetKingdomFullName()));
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        Kingdom core = empire?.CoreKingdom;
        if (core == null || core.HasGivenAlliance() || core.GetMoney() <= 0) return false;
        foreach (Kingdom enemy in core.getEnemiesKingdoms())
        {
            if (enemy == null || enemy.isRekt() || empire.given_Kingdoms.Contains(enemy) ||
                enemy.IsInSameEmpire(core) || enemy.HasGivenAlliance()) continue;
            Kingdom target = enemy.IsInEmpire() ? enemy.GetEmpire().CoreKingdom : enemy;
            if (target == null || !IsLosing(core, enemy)) continue;
            SetKingdomTarget(target);
            return true;
        }
        return false;
    }

    private static bool IsLosing(Kingdom core, Kingdom enemy)
    {
        War war = WarSituation.FindWar(core, enemy);
        if (war == null) return false;
        float years = WarSituation.WarYears(war);
        if (years < 1f) return false;
        int lost = WarSituation.CitiesLost(war, core);
        float ratio = WarSituation.StrengthRatio(core, enemy);
        return lost >= 1 && ratio >= 1.2f || ratio >= 1.8f || ratio >= 1.5f && years >= 3f;
    }
}

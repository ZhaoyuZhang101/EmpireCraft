using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 割地求和(城下之盟)：只有真打输了才会割地，而不是一开战就让。条件(对每个接壤的交战国，见 WarSituation)：
//   · 战争已打满一年(不会一开战就求和)，本国至少还有三座城；
//   · 战局不利：这场仗已丢 2 城以上且敌强于我(兵力 ≥ 1.2 倍)，或丢了城且敌兵力 ≥ 1.8 倍，
//     或敌兵力 ≥ 3 倍(兵败如山倒)；旧存档开打、没有丢城记录的战争要敌兵力 ≥ 2.5 倍且打满两年；
//   · 国库充裕时宁可岁输金帛也不割地(交给"提供岁币")。
// 割让的是与敌国接壤、人口最少的非都城城市，割让后停战。
public class TempFac_割让城池 : TemporaryFaction
{
    private const int RichTreasury = 500;

    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_割让城池();
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
        Kingdom enemy = GetKingdomTarget();
        Empire empire = GetEmpire();
        if (enemy != null && !enemy.isRekt() && empire != null)
        {
            City city = ClaimTargetRules.CedeCity(empire, enemy);
            if (city != null)
            {
                string cityName = city.GetCityName();
                city.joinAnotherKingdom(enemy);
                EventRecorder.Record(empire, string.Format(LM.Get("cede_city_history"), empire.GetEmpireFullName(),
                    cityName, enemy.GetKingdomFullName()));
            }
            if (enemy.isInWarWith(empire.CoreKingdom)) enemy.EndWarWith(empire.CoreKingdom);
        }
        End();
    }

    // 目标：接壤、把本朝打得危急、又有城可割的交战国。规则在 ClaimRules.json 的 target
    // (门槛见 ClaimTargetRules.LosingWarBadly，割哪座城见 ClaimTargetRules.CedeCity)
    public override bool CheckCondition() => TargetReady() && PickRuleTarget();

    protected override bool TargetReady()
    {
        Empire empire = GetEmpire();
        Kingdom core = empire?.CoreKingdom;
        if (core == null || !core.hasEnemies() || empire.countCities() < 3) return false;
        // 国库充裕：宁可岁输金帛，也不割地
        return core.GetMoney() < RichTreasury || core.HasGivenAlliance();
    }
}

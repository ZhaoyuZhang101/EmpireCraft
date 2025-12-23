using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;
using EmpireCraft.Scripts.Layer;
namespace EmpireCraft.Scripts.Regimes.TemporaryFactions;

public class TempFac_设置行政区 : TemporaryFaction
{
    public override int Budget => GetTitleTarget()?.city_list?.ToList()?.Sum(c => c.units.Count)??0;

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
            }
        }
        CountDown = 2;
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        var titles = empire.Emperor.GetOwnedTitle();
        if (titles.Any())
        {
            foreach (var title in titles)
            {
                var kt = ModClass.KINGDOM_TITLE_MANAGER.get(title);
                if (kt==null) continue;
                if (kt.title_capital.isRekt()) continue;
                if (kt.control_kingdom!=empire.CoreKingdom) continue;
                if (kt.title_capital != empire.CoreKingdom.capital)
                {
                    Acc = 30;
                    SetTitleTarget(kt);
                    return true;
                }
            }
        }
        return false;
    }
}

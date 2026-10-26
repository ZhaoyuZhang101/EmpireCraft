using System.Collections.Generic;
using System.Linq;
using NeoModLoader.services;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_宗教融入 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_宗教融入();
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
        Religion religion = GetReligionTarget();
        if (religion != null)
        {
            empire.CoreKingdom.setReligion(religion);
            empire.CoreKingdom.units.ForEach(a=>a.setReligion(religion));
            // 帝国国教一并改(宗教同化、神授君权、恢复圣地都看它；以前留着旧教，宗教同化反把成员推回核心弃掉的旧教)
            empire.Religion = religion;
        }
        End();
    }

    // 帝国治下六成以上的人信同一个宗教、又不是核心王国的国教时，核心王国改奉此教。
    // 信众按各城算：城里的实体各按自己的宗教，背景人口按城的宗教(与 NoCommonersPatch 的信众统计同一口径)；
    // 以前只数帝国的实体小人，无小人模式下只剩君主、城主、官员，几个人的信仰就定了国教
    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire?.CoreKingdom == null) return false;
        var believers = new Dictionary<Religion, double>();
        double total = 0d;
        foreach (City city in empire.getCities())
        {
            if (city?.data == null || city.isRekt()) continue;
            if (city.units != null)
                foreach (Actor actor in city.units)
                {
                    if (actor?.data == null || actor.isRekt() || !actor.isAlive() || CityPopulationSystem.IsVehicle(actor)) continue;
                    total++;
                    if (actor.hasReligion()) Add(believers, actor.religion, 1d);
                }
            int background = CityPopulationSystem.BackgroundCount(city);
            total += background;
            if (background > 0 && city.hasReligion()) Add(believers, city.religion, background);
        }
        if (total <= 0d || believers.Count == 0) return false;
        KeyValuePair<Religion, double> mostPopular = believers.OrderByDescending(pair => pair.Value).First();
        if (mostPopular.Value / total > 0.6d && mostPopular.Key != empire.CoreKingdom.religion)
        {
            SetReligionTarget(mostPopular.Key);
            return true;
        }

        return false;
    }

    private static void Add(Dictionary<Religion, double> believers, Religion religion, double amount)
    {
        believers.TryGetValue(religion, out double current);
        believers[religion] = current + amount;
    }
}

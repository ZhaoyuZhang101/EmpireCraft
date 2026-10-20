using System.Linq;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_恢复圣地 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_恢复圣地();
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
        var city = GetCityTarget();
        Empire empire = GetEmpire();
        if (city?.data != null && !city.isRekt() && empire != null)
        {
            // 圣城本是某个成员国的都城：扶这个国为教宗国；否则把圣城分出来另立一国
            Kingdom kingdom = city.isCapitalCity() ? city.kingdom : SplitHolyCity(empire, city);
            if (kingdom?.data != null && !kingdom.isRekt())
            {
                kingdom.GetRegime()?.SetReligionLevel(ReligionLevel.High);
                // 立刻按教宗国重判类别(见 Feudalism 配置的 holy_city)，免得月度刷新之前又提一次
                EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(kingdom);
            }
        }
        End();
    }

    private static Kingdom SplitHolyCity(Empire empire, City city)
    {
        // 由城主出面立国；无小人模式下城里可能没有合适的实体，以前直接拿第一个单位(可能是船，或者没有)
        static bool Fit(Actor actor) => actor?.data != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() &&
                                        !actor.isKing() && !CityPopulationSystem.IsVehicle(actor);
        Actor founder = Fit(city.leader) ? city.leader : city.units?.FirstOrDefault(Fit);
        if (founder == null) return null;
        var kingdom = city.makeOwnKingdom(founder);
        if (kingdom == null) return null;
        empire.join(kingdom, pForce:true);
        KingdomTitle title = city.GetTitle();
        if (title != null)
        {
            kingdom.SetMainTitle(title);
            kingdom.king?.AddOwnedTitle(title);
            kingdom.ReconcileMainTitle(new[] { title });
        }
        empire.SynchronizeLandedLegalTitles(kingdom);
        return kingdom;
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire == null) return false;
        // 西方封建：教宗国要帝国施行了教权至上(或已进到神权国家)才能建立；须先确立国教(下面的 empire.Religion)
        if (empire.CoreKingdom?.GetRegime()?.type == RegimeType.Feudalism && !PapacySystem.PapalRule(empire))
            return false;
        var religion = empire.Religion;
        if (religion == null || religion.isRekt()) return false;
        var religionCity = religion.GetCity();
        if (religionCity?.kingdom == null || religionCity.isRekt()) return false;
        Kingdom holder = religionCity.kingdom;
        // 只把本帝国里的圣城立为教宗国(以前任何帝国里的圣城都能分出来并进自己帝国，等于白拿别国的城)
        if (holder.GetEmpire() != empire) return false;
        // 圣城是成员国的都城：以前一律不提，圣城之主就永远立不成教宗国；现在扶它为教宗国。
        // 皇帝自家的都城立不了，已是教宗国的、改奉别的宗教的不再提
        if (religionCity.isCapitalCity() &&
            (holder == empire.CoreKingdom || holder.religion != religion ||
             holder.GetKingdomType() == KingdomType.Feudalism_papal_state)) return false;
        SetCityTarget(religionCity);
        return true;
    }
}

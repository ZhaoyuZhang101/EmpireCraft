using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;

namespace EmpireCraft.Scripts.GodPowers;

// 册封名将、册封名臣(见 FamousTalentSystem)：点一个成年人物，直接封为名将/名臣——不看功业门槛、
// 数量上限与时势；已成名的再点一次晋一档(良才 → 名 → 国柱封顶)。
// 无小人模式地图上没有人可点(用户 2026-10-17："我都是无小人系统了这个册封按钮没用")：点城就册封城里的人——
// 名将挑统兵的(带兵的、军人)名望最高者，没有就挑名望最高的在任官员；名臣挑名望最高的在任官员，没有就是城主；
// 城里一个人物都没有时从本城人口里出一位官员来封。同一座城再点一次，是给同一个人晋一档。
public static class FamousTalentPowers
{
    public const string BestowGeneral = "bestow_famous_general", BestowMinister = "bestow_famous_minister";

    public static void init()
    {
        PowerLibrary powerLib = AssetManager.powers;
        powerLib.add(new GodPower
        {
            id = BestowGeneral,
            name = BestowGeneral,
            click_action = (tile, power) => Bestow(tile, TalentKind.General)
        });
        powerLib.add(new GodPower
        {
            id = BestowMinister,
            name = BestowMinister,
            click_action = (tile, power) => Bestow(tile, TalentKind.Minister)
        });
    }

    private static bool Bestow(WorldTile tile, TalentKind kind)
    {
        Actor actor = tile != null ? ActorCreateKingdom.getActorFromTile(tile) : World.world.getActorNearCursor();
        actor ??= FromCity(tile?.zone?.city, kind);
        if (actor == null)
        {
            ActionLibrary.showWhisperTip("bestow_talent_no_one");
            return false;
        }
        return FamousTalentSystem.Bestow(actor, kind);
    }

    private static Actor FromCity(City city, TalentKind kind)
    {
        if (city?.data == null || city.isRekt() || city.kingdom == null || city.kingdom.wild) return null;
        List<Actor> people = city.units?.Where(unit => unit?.data != null && !unit.isRekt() && unit.isAlive() && unit.isAdult()).ToList()
                             ?? new List<Actor>();
        Actor pick = null;
        if (kind == TalentKind.General)
            pick = people.Where(unit => unit.is_army_captain || unit.army != null || unit.isWarrior())
                .OrderByDescending(unit => unit.renown).FirstOrDefault();
        pick ??= people.Where(unit => unit.IsOnOffice()).OrderByDescending(unit => unit.renown).FirstOrDefault();
        pick ??= city.leader != null && !city.leader.isRekt() && city.leader.isAlive() ? city.leader : null;
        pick ??= people.OrderByDescending(unit => unit.renown).FirstOrDefault();
        return pick ?? CityPopulationSystem.SpawnOfficial(city, city.kingdom);
    }
}

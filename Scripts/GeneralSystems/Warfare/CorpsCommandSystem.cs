using System.Collections.Generic;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 军令(用户转玩家 2026-10-17 "可以对国家发动军事命令"，用户选"军令神力")：神力先点一座城选中它的国家，再点目标城——
//   · 本国的城：集中固守。分兵守城时这座城排第一，至少拿全国能上阵兵力的六成(攻势更大就按攻势)，余下的照常分守别处、出征；
//   · 交战敌国的城：集中攻打。出征的军都投这一段(城在敌境深处、还不是战线的，投离它最近的一段)，守城的兵照常先留够；
//   · 再点同一座城撤销；军令管到这场仗打完(本国不再有交战之敌、或攻打的城已不归敌国)。军令不存档。
// 见 FrontSystem.Assign、CorpsCommandPower。
public static class CorpsCommandSystem
{
    public const float HoldShare = 0.6f;
    private static readonly Dictionary<long, long> Holds = new(), Attacks = new();

    public static void ResetWorldState()
    {
        Holds.Clear();
        Attacks.Clear();
    }

    public static City HoldCity(Kingdom kingdom)
    {
        if (kingdom == null || !Holds.TryGetValue(kingdom.id, out long id)) return null;
        City city = World.world.cities.get(id);
        if (city?.data != null && !city.isRekt() && city.kingdom == kingdom && kingdom.hasEnemies()) return city;
        Holds.Remove(kingdom.id);
        return null;
    }

    public static City AttackCity(Kingdom kingdom)
    {
        if (kingdom == null || !Attacks.TryGetValue(kingdom.id, out long id)) return null;
        City city = World.world.cities.get(id);
        if (city?.kingdom != null && !city.isRekt() && kingdom.isInWarWith(city.kingdom)) return city;
        Attacks.Remove(kingdom.id);
        return null;
    }

    // 下令或撤销，返回提示
    public static string Command(Kingdom kingdom, City city)
    {
        if (kingdom?.data == null || kingdom.isRekt() || city?.kingdom == null) return LM.Get("force_vassal_no_kingdom");
        if (city.kingdom == kingdom)
        {
            if (HoldCity(kingdom) == city)
            {
                Holds.Remove(kingdom.id);
                return string.Format(LM.Get("corps_command_hold_cancelled"), kingdom.GetKingdomName(), city.GetCityName());
            }
            if (!kingdom.hasEnemies()) return string.Format(LM.Get("corps_command_no_war"), kingdom.GetKingdomName());
            Holds[kingdom.id] = city.data.id;
            return string.Format(LM.Get("corps_command_hold"), kingdom.GetKingdomName(), city.GetCityName());
        }
        if (!kingdom.isInWarWith(city.kingdom))
            return string.Format(LM.Get("corps_command_not_enemy"), kingdom.GetKingdomName(), city.kingdom.GetKingdomName());
        if (AttackCity(kingdom) == city)
        {
            Attacks.Remove(kingdom.id);
            return string.Format(LM.Get("corps_command_attack_cancelled"), kingdom.GetKingdomName(), city.GetCityName());
        }
        Attacks[kingdom.id] = city.data.id;
        return string.Format(LM.Get("corps_command_attack"), kingdom.GetKingdomName(), city.GetCityName());
    }

    // 军事页：这支军是不是正在奉令(固守或攻打军令所指的城)
    public static bool Ordered(Kingdom kingdom, CorpsData corps) =>
        corps != null && corps.sector_city_id >= 0 &&
        (corps.task == "defend" && HoldCity(kingdom)?.data?.id == corps.sector_city_id ||
         corps.task == "attack" && (AttackCity(kingdom) is City target &&
                                    (target.data.id == corps.sector_city_id || FrontSystem.FocusSector(kingdom) == corps.sector_city_id)));
}

using System;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 战争中的平民与屠戮(平民保护见 GamePatches/CivilianProtectionPatch)：
//   · 交战时士兵不攻击敌国平民，弱小民族不会因为打了败仗就被杀光；
//   · 仇外的军队仍会屠戮异族平民。被屠戮的城市记下仇恨：屠城者(或其所在帝国)统治这座城时，
//     城市忠诚按死难人数下降(每人 -LoyaltyPerVictim，最多 -MaxLoyaltyPenalty)，三十年内逐渐淡去，
//     忠诚跌破零就会叛乱——屠戮换来的只是更多叛乱。
public static class MassacreSystem
{
    public const int MemoryYears = 30;
    private const int LoyaltyPerVictim = 2;
    private const int MaxLoyaltyPenalty = 40;
    private const int NoticeVictims = 5;

    private static bool IsCivilian(Actor actor) => actor?.profession_asset?.is_civilian == true;

    // 交战双方之间、至少一方是平民的交锋
    public static bool IsWartimeCivilianEncounter(Actor attacker, Actor target)
    {
        if (attacker == null || target == null) return false;
        Kingdom attackerKingdom = attacker.kingdom;
        Kingdom targetKingdom = target.kingdom;
        if (attackerKingdom == null || targetKingdom == null || attackerKingdom == targetKingdom) return false;
        if (!attacker.isKingdomCiv() || !target.isKingdomCiv()) return false;
        if (!IsCivilian(attacker) && !IsCivilian(target)) return false;
        return attackerKingdom.isEnemy(targetKingdom);
    }

    // 仇外的士兵对异族(物种或文化不同)平民下手
    public static bool WillMassacre(Actor attacker, Actor target) =>
        !IsCivilian(attacker) && IsCivilian(target) && attacker.hasXenophobic() &&
        !(attacker.culture == target.culture && attacker.asset == target.asset);

    public static void OnCivilianKilled(Actor victim, Actor killer)
    {
        if (World.world == null || !IsCivilian(victim) || killer == null || IsCivilian(killer)) return;
        Kingdom killerKingdom = killer.kingdom;
        if (killerKingdom == null || victim.kingdom == null || !killer.isKingdomCiv() || !victim.isKingdomCiv() ||
            !killerKingdom.isEnemy(victim.kingdom)) return;
        City city = victim.city ?? victim.current_tile?.zone_city;
        if (city == null || city.isRekt()) return;
        CityExtension.CityExtraData data = city.GetOrCreate();
        double now = World.world.getCurWorldTime();
        if (data.massacre_by_kingdom_id != killerKingdom.id ||
            data.massacre_last < 0d || Date.getYearsSince(data.massacre_last) >= MemoryYears)
        {
            data.massacre_by_kingdom_id = killerKingdom.id;
            data.massacre_victims = 0;
            data.massacre_noticed = false;
        }
        data.massacre_victims++;
        data.massacre_last = now;
        if (data.massacre_noticed || data.massacre_victims < NoticeVictims) return;
        data.massacre_noticed = true;
        TranslateHelper.LogEventMessage(string.Format(LM.Get("massacre_event"), killerKingdom.GetKingdomName(),
            city.GetCityName()), killerKingdom);
    }

    // 屠城之恨：城市现在归屠城者(或与之同一帝国)统治时的忠诚惩罚
    public static int LoyaltyPenalty(City city)
    {
        if (city?.kingdom == null || World.world == null) return 0;
        CityExtension.CityExtraData data = city.GetOrCreate();
        if (data.massacre_victims <= 0 || data.massacre_last < 0d) return 0;
        int years = Date.getYearsSince(data.massacre_last);
        if (years >= MemoryYears) return 0;
        Kingdom perpetrator = World.world.kingdoms.get(data.massacre_by_kingdom_id);
        bool ruledByPerpetrator = city.kingdom.id == data.massacre_by_kingdom_id ||
                                  perpetrator != null && perpetrator.GetEmpire() != null &&
                                  perpetrator.GetEmpire() == city.kingdom.GetEmpire();
        if (!ruledByPerpetrator) return 0;
        float fade = 1f - years / (float)MemoryYears;
        return -Mathf.RoundToInt(Math.Min(MaxLoyaltyPenalty, data.massacre_victims * LoyaltyPerVictim) * fade);
    }
}

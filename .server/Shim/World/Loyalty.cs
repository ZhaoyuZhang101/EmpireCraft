// 城市忠诚：游戏里是原版二十几条加上模组自己的几条(正统、苛政、屠城之恨……)逐条加总。服务器里原版那些条没有，
// 只剩模组的几条、加起来在零上下，每座城都像要叛离：不上缴、动辄起事。这里补几条底子(写法是服务器自己的，量级照原版：
// 每条正负十几到三十)：都城、本国之城、离都城远近、文化、宗教、新近打下、君主在位久暂、有没有城主。模组的几条照旧叠在上面。
//# hand City: getLoyalty
using System;
using System.Runtime.CompilerServices;
using UnityEngine;

public partial class City
{
    private int _shimLoyalty;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public int getLoyalty(bool pForceRecalc = false)
    {
        MapBox world = World.world;
        if (kingdom == null || kingdom.wild) return _shimLoyalty = 0;
        // 同原版：三秒(世界时间)算一回
        if (pForceRecalc || world.getWorldTimeElapsedSince(_loyalty_last_time) > 3f || world.getCurWorldTime() < _loyalty_last_time)
        {
            int total = 0;
            foreach (LoyaltyAsset entry in AssetManager.loyalty_library.list)
            {
                if (entry?.calc == null) continue;
                try
                {
                    total += entry.calc(this);
                }
                catch (Exception)
                {
                    // 一条算不出来不耽误别的
                }
            }
            _shimLoyalty = total;
            _loyalty_last_time = world.getCurWorldTime();
        }
        return _shimLoyalty;
    }
}

internal static class ShimLoyalty
{
    internal static void Init()
    {
        Add("server_capital", city => city.kingdom != null && city.kingdom.capital == city ? 100 : 0);
        Add("server_part_of_kingdom", city => 10);
        // 离都城越远越不服管：五十格以内 +10，再远每四十格 -5，至多 -30
        Add("server_distance", city =>
        {
            City capital = city.kingdom?.capital;
            if (capital == null || capital == city) return 0;
            float distance = Vector2.Distance(city.city_center, capital.city_center);
            return distance <= 50f ? 10 : -Mathf.Min(30, Mathf.FloorToInt((distance - 50f) / 40f) * 5);
        });
        Add("server_culture", city => city.culture == null || city.kingdom?.culture == null ? 0 : city.culture == city.kingdom.culture ? 15 : -15);
        Add("server_religion", city => city.religion == null || city.kingdom?.religion == null ? 0 : city.religion == city.kingdom.religion ? 10 : -10);
        // 新近打下的城：十年里从 -20 慢慢回到 0
        Add("server_new_conquest", city =>
        {
            if (city.data == null || city.data.timestamp_kingdom <= 0d) return 0;
            int years = Date.getYearsSince(city.data.timestamp_kingdom);
            return years >= 10 ? 0 : -(20 - years * 2);
        });
        // 君主在位五年以上 +5；国无君 -10
        Add("server_king_rule", city =>
        {
            Kingdom realm = city.kingdom;
            if (realm == null) return 0;
            if (!realm.hasKing()) return -10;
            return realm.data.timestamp_king_rule > 0d && Date.getYearsSince(realm.data.timestamp_king_rule) >= 5 ? 5 : 0;
        });
        Add("server_leader", city => city.hasLeader() ? 5 : -5);
    }

    private static void Add(string id, LoyaltyDelegateCalc calc)
    {
        if (!AssetManager.loyalty_library.dict.ContainsKey(id))
            AssetManager.loyalty_library.add(new LoyaltyAsset { id = id, translation_key = id, calc = calc });
    }
}

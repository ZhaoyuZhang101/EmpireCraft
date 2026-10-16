using System.Collections.Generic;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 奴隶制(用户 2026-10-16："奴隶制度""奴隶主阶级")：施行了"奴隶制"(华夏世袭王权以后，见 Huaxia.json)的国家——
//   · 来源：攻克城池，俘其民(贵族、官僚以外)一成二为奴，押回都城(见 OnCityCaptured)；罪人、负债者没为奴隶，
//     各城奴隶至少占百分之四，闹饥荒的城每年再有百分之一的人口卖身为奴(见 Settle)；
//   · 奴隶不随阶层流动(见 CityPopulationSystem.DriftClasses)，世代为奴；每年百分之二赎身、逃亡为平民；
//     不施行奴隶制的国家，境内奴隶每年放免两成；
//   · 奴隶劳作：那一份产值按一倍半算，归奴隶主、不计税(见 PopulationEconomySystem)；不征为兵(见 CorpsSystem)；
//   · 奴隶主：有奴隶的城按奴隶多少出一批奴隶主(见 CityPopulationSystem.TargetClassShares)；
//   · 奴隶终年劳苦，怨气常高(见 SocialCrisisSystem.For)，聚众起事就是阶层起义里的奴隶起义。
// 奴隶只在背景人口里(无小人模式)；实体单位不分奴隶。
public static class SlaverySystem
{
    public const string Feature = "slavery";
    // 奴隶那一份劳作产值的倍数
    public const float OutputFactor = 1.5f;
    private const float CaptiveRate = 0.12f, BaseShare = 0.04f, MaxShare = 0.4f, BondageCatchUp = 0.2f, DebtRate = 0.01f,
        ManumitRate = 0.02f, FreeRate = 0.2f, OwnersPerSlave = 0.1f, OwnerMin = 0.003f, OwnerMax = 0.03f;
    private const int NoticePeople = 50;

    public static bool Practices(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild) return false;
        string culture = CultureService.GetRealmCulture(kingdom);
        return CultureService.IsValidCulture(culture) && InstitutionSystem.GetFeature(culture, Feature) > 0f;
    }

    public static float Slaves(City city) => CityPopulationSystem.BackgroundOfClass(city, SocialClass.Slave);

    // 不能征为兵的人(奴隶；野人大半不征，见 GuoyeSystem)
    public static float Unfree(City city) => Slaves(city) + GuoyeSystem.Exempt(city);

    // 奴隶主在本城自由民里应占的比例：奴隶越多奴隶主越多
    public static float OwnerShare(City city)
    {
        if (!Practices(city?.kingdom)) return 0f;
        float slaves = Slaves(city);
        if (slaves < 1f) return 0f;
        float free = Mathf.Max(1f, CityPopulationSystem.GetBackgroundTotal(city) - slaves);
        return Mathf.Clamp(slaves / free * OwnersPerSlave, OwnerMin, OwnerMax);
    }

    // 劳动力里奴隶占几成
    public static float WorkforceShare(Dictionary<SocialClass, float> workforce)
    {
        if (workforce == null || !workforce.TryGetValue(SocialClass.Slave, out float slaves) || slaves <= 0f) return 0f;
        float total = 0f;
        foreach (float amount in workforce.Values) total += amount;
        return total > 0f ? Mathf.Clamp01(slaves / total) : 0f;
    }

    // 每年(背景人口自然增减时)：赎身、逃亡、放免；罪人负债者没为奴隶、饥荒卖身
    public static void Settle(City city, CityPopulationData data, float years)
    {
        if (data?.groups == null || years <= 0f) return;
        float slaves = 0f, total = 0f;
        foreach (PopGroup group in data.groups)
        {
            total += group.Background;
            if (group.social_class == SocialClass.Slave) slaves += group.Background;
        }
        if (total < 1f) return;
        bool practices = Practices(city.kingdom);
        if (slaves > 0f)
            slaves -= Convert(city, data, SocialClass.Slave, GuoyeSystem.CommonerOf(city),
                slaves * Mathf.Clamp01((practices ? ManumitRate : FreeRate) * years));
        if (!practices) return;
        float bonded = Mathf.Max(0f, total * BaseShare - slaves) * Mathf.Clamp01(BondageCatchUp * years);
        if (data.last_food_shortage > 0f) bonded += total * DebtRate * years;
        bonded = Mathf.Min(bonded, total * MaxShare - slaves);
        if (bonded > 0f) Convert(city, data, GuoyeSystem.CommonerOf(city), SocialClass.Slave, bonded);
    }

    // 城破(ScorchedEarthSystem.OnCityCaptured)：施行奴隶制的一方俘其民为奴，押回都城
    public static void OnCityCaptured(City city, Kingdom conqueror)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || city?.data == null || !Practices(conqueror)) return;
        CityPopulationData data = CityPopulationSystem.Get(city);
        if (data?.groups == null) return;
        City seat = conqueror.capital != null && !conqueror.capital.isRekt() && conqueror.capital != city ? conqueror.capital : city;
        var moves = new List<(PopGroup group, float amount)>();
        foreach (PopGroup group in data.groups)
            if (group.social_class is not (SocialClass.Noble or SocialClass.Officer or SocialClass.SlaveOwner or SocialClass.Slave) &&
                group.Background > 0f)
                moves.Add((group, group.Background * CaptiveRate));
        float captives = 0f;
        foreach ((PopGroup group, float amount) in moves)
        {
            float removed = CityPopulationSystem.RemoveBackground(group, amount);
            if (removed <= 0f) continue;
            CityPopulationSystem.AddBackground(seat, SocialClass.Slave, group.culture, group.species, group.ideology, removed);
            captives += removed;
        }
        if (captives < NoticePeople) return;
        string content = string.Format(LM.Get("slavery_captives_history"), conqueror.GetKingdomName(), city.GetCityName(),
            Mathf.RoundToInt(captives), seat.GetCityName());
        LogService.LogInfo("[EmpireCraft][奴隶制] " + content);
        Empire empire = conqueror.GetEmpire();
        if (empire != null && !empire.isRekt()) empire.RecordHistory(directContent: content, kingdomId: conqueror.id);
        else TranslateHelper.LogEventMessage(content, conqueror);
    }

    // 把某阶层的一批背景人口改为另一阶层(按各组人数摊，文化、物种、理念不变)，返回实际改过去的人数
    public static float Convert(City city, CityPopulationData data, SocialClass from, SocialClass to, float amount)
    {
        if (data?.groups == null || amount <= 0f) return 0f;
        float have = 0f;
        foreach (PopGroup group in data.groups)
            if (group.social_class == from) have += group.Background;
        if (have <= 0f) return 0f;
        float ratio = Mathf.Clamp01(amount / have);
        var moves = new List<(PopGroup group, float amount)>();
        foreach (PopGroup group in data.groups)
            if (group.social_class == from && group.Background > 0f) moves.Add((group, group.Background * ratio));
        float moved = 0f;
        foreach ((PopGroup group, float share) in moves)
        {
            float removed = CityPopulationSystem.RemoveBackground(group, share);
            if (removed <= 0f) continue;
            CityPopulationSystem.AddBackground(city, to, group.culture, group.species, group.ideology, removed);
            moved += removed;
        }
        return moved;
    }
}

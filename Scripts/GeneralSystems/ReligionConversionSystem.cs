using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 统治者皈依(用户 2026-10-26 选"统治者皈依"：克洛维、弗拉基米尔受洗，汗国皈依伊斯兰、佛教)。以前君主从不主动改宗，
// 国教只会被原版"国王改国教"决策顺手带走(已拦，见 ReligionPatch)：
//   · 每年，独立王国与帝国中央看周边：接壤(帝国按全体成员)、不在交战的国家所奉的世界性宗教(普世教会、伊斯兰、法教、三教，
//     见 ReligionFaith.IsWorldFaith)，按对方城数 / 本国城数(至多 MaxStrengthRatio)加权；境内奉该教的城按占比 ×2 加权；
//   · 皈依几率 = BaseChance × 权重 × 意愿(多神、萨满、无教 1，三教、法教、神道 0.15，一神教 0.03；握着本教圣地的再 ×0.1)；
//     皈依后 CooldownYears 年不再皈依；
//   · 皈依：君主、国家、都城改奉；帝国中央皈依的，帝国国教同改，成员国君主每年 MemberFollowChance 随之改宗；
//     此后 ConversionPushYears 年国教势能加倍(无小人模式见 BackgroundReligionSystem.StateFactor；有小人时每年 ManualCityChance 逐城改宗)。
//   · 入主从俗(用户 2026-10-27 选"做"，伊儿汗国改宗伊斯兰、元崇佛)：君主奉的是民族宗教(不是教会、伊斯兰、法教)，治下过半人口
//     奉另一个天下的本土大教，每年 AdoptSubjectsChance 改奉治下的大教，不看皈依冷却；握着本教圣地的不改。
public static class ReligionConversionSystem
{
    private const float AdoptSubjectsChance = 0.1f;
    public const int ConversionPushYears = 40;
    private const int CooldownYears = 50;
    private const float BaseChance = 0.02f;
    private const float MaxStrengthRatio = 3f;
    private const float MinorityWeight = 2f;
    private const float HolyCityReluctance = 0.1f;
    private const float MemberFollowChance = 0.1f;
    private const float ManualCityChance = 0.05f;

    private static bool Alive(Kingdom kingdom) => kingdom?.data != null && !kingdom.isRekt() && !kingdom.wild;

    private static Kingdom Realm(Kingdom kingdom) => kingdom?.GetEmpire()?.CoreKingdom ?? kingdom;

    private static bool Within(double at, int years) =>
        at >= 0d && World.world.getCurWorldTime() >= at && Date.getYearsSince(at) < years;

    // 本国(帝国按中央)近年皈依、而且本国仍奉中央皈依的宗教
    public static bool RecentlyConverted(Kingdom kingdom)
    {
        Kingdom realm = Realm(kingdom);
        return Alive(realm) && kingdom.hasReligion() && kingdom.religion == realm.religion &&
               Within(realm.GetOrCreate().religion_converted_at, ConversionPushYears);
    }

    private static List<Kingdom> Members(Kingdom realm)
    {
        Empire empire = realm.GetEmpire();
        return empire != null && empire.CoreKingdom == realm
            ? empire.kingdoms_list.Where(Alive).ToList()
            : new List<Kingdom> { realm };
    }

    private static int Cities(Kingdom realm) => Members(realm).Sum(member => member.cities.Count);

    // 每年一次(挂在 ReligiousTension 的年度扫描上)
    public static void YearlyPass()
    {
        if (World.world?.kingdoms == null || ModClass.IS_CLEAR) return;
        List<Kingdom> realms = World.world.kingdoms.list.Where(kingdom => Alive(kingdom) && Realm(kingdom) == kingdom &&
            !EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(kingdom)).ToList();
        foreach (Kingdom realm in realms)
        {
            try
            {
                if (!TryAdoptSubjectsFaith(realm)) TryConvert(realm, realms);
                FollowRealm(realm);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][宗教] {realm.data?.name} 皈依结算失败: {exception.Message}");
            }
        }
        if (!BackgroundReligionSystem.Active) ManualCityConversion(realms);
    }

    private static void TryConvert(Kingdom realm, List<Kingdom> realms)
    {
        Actor king = realm.king;
        if (king?.data == null || !king.isAlive() || Within(realm.GetOrCreate().religion_converted_at, CooldownYears)) return;
        Religion current = realm.hasReligion() ? realm.religion : null;
        float receptive = ReligionFaith.Receptiveness(current);
        if (PapacySystem.HoldsHolyCity(realm)) receptive *= HolyCityReluctance;
        List<Kingdom> members = Members(realm);
        int own = Mathf.Max(1, members.Sum(member => member.cities.Count));
        var weights = new Dictionary<Religion, float>();
        foreach (Kingdom other in realms)
        {
            if (other == realm || !other.hasReligion() || other.religion == current || !Candidate(other.religion) ||
                realm.isInWarWith(other)) continue;
            // 帝国中央的 IsNeighbourWith 已按全体成员的城算
            if (!Members(other).Any(realm.IsNeighbourWith)) continue;
            Add(weights, other.religion, Mathf.Min(MaxStrengthRatio, Cities(other) / (float)own));
        }
        // 境内的少数派
        var minorities = new Dictionary<Religion, int>();
        foreach (Kingdom member in members)
        foreach (City city in member.cities)
            if (city?.data != null && city.hasReligion() && city.religion != current && Candidate(city.religion))
            {
                minorities.TryGetValue(city.religion, out int count);
                minorities[city.religion] = count + 1;
            }
        foreach (KeyValuePair<Religion, int> pair in minorities) Add(weights, pair.Key, MinorityWeight * pair.Value / own);
        if (weights.Count == 0) return;
        KeyValuePair<Religion, float> best = weights.OrderByDescending(pair => pair.Value).First();
        if (UnityEngine.Random.value >= BaseChance * best.Value * receptive) return;
        Convert(realm, best.Key, current);
    }

    // 入主从俗：民族宗教的君主治下过半人口奉另一个天下的本土大教
    private static bool TryAdoptSubjectsFaith(Kingdom realm)
    {
        Actor king = realm.king;
        Religion current = realm.hasReligion() ? realm.religion : null;
        if (king?.data == null || !king.isAlive() || current == null || ReligionFaith.IsUniversal(current) ||
            PapacySystem.HoldsHolyCity(realm)) return false;
        var people = new Dictionary<Religion, float>();
        float total = 0f;
        foreach (Kingdom member in Members(realm))
        foreach (City city in member.cities)
        {
            if (city?.data == null || city.isRekt()) continue;
            float count = Mathf.Max(1, city.CountLivingPopulation());
            total += count;
            if (!city.hasReligion()) continue;
            people.TryGetValue(city.religion, out float sum);
            people[city.religion] = sum + count;
        }
        if (people.Count == 0) return false;
        KeyValuePair<Religion, float> top = people.OrderByDescending(pair => pair.Value).First();
        Religion faith = top.Key;
        if (faith == current || top.Value * 2f < total || !ReligionSphere.IsNative(faith) || ReligionSphere.IsLocalChurch(faith) ||
            ReligionSphere.SphereOf(faith) == ReligionSphere.SphereOf(current)) return false;
        if (UnityEngine.Random.value >= AdoptSubjectsChance) return false;
        Convert(realm, faith, current, "ruler_adopts_subjects_log");
        return true;
    }

    private static bool Candidate(Religion religion) =>
        religion?.data != null && !religion.isRekt() && ReligionFaith.IsWorldFaith(religion) && !ReligionSphere.IsLocalChurch(religion);

    private static void Add(Dictionary<Religion, float> weights, Religion religion, float weight)
    {
        weights.TryGetValue(religion, out float current);
        weights[religion] = current + weight;
    }

    // 君主率国皈依(异文明帝国复归本土大教也走这里，见 ReligionSchismSystem.TryNativeTurns)
    internal static void Convert(Kingdom realm, Religion faith, Religion old, string textKey = "ruler_conversion_log")
    {
        Actor king = realm.king;
        king.setReligion(faith);
        realm.setReligion(faith);
        if (realm.capital?.data != null && !realm.capital.isRekt() && realm.capital.religion != faith)
            BackgroundReligionSystem.Convert(realm.capital, faith, realm.capital.religion);
        Empire empire = realm.GetEmpire();
        if (empire != null && empire.CoreKingdom == realm) empire.Religion = faith;
        realm.GetOrCreate().religion_converted_at = World.world.getCurWorldTime();
        string text = string.Format(LM.Get(textKey), king.getName(), realm.GetKingdomName(), faith.name);
        TranslateHelper.LogEventMessage(text, realm);
        king.RecordPersonalHistory(text);
        ReligionChronicle.Record(faith, old, ReligionChronicle.KindConversion, text);
        LogService.LogInfo($"[EmpireCraft][宗教] {realm.GetKingdomName()} 由{old?.name ?? "无教"}皈依{faith.name}");
    }

    // 中央皈依后，成员国君主陆续随之改宗
    private static void FollowRealm(Kingdom realm)
    {
        if (!Within(realm.GetOrCreate().religion_converted_at, ConversionPushYears) || !realm.hasReligion()) return;
        Empire empire = realm.GetEmpire();
        if (empire == null || empire.CoreKingdom != realm) return;
        foreach (Kingdom member in empire.kingdoms_list.ToList())
        {
            if (!Alive(member) || member == realm || member.religion == realm.religion ||
                UnityEngine.Random.value >= MemberFollowChance) continue;
            member.setReligion(realm.religion);
            if (member.king?.data != null && member.king.isAlive()) member.king.setReligion(realm.religion);
            if (member.capital?.data != null && !member.capital.isRekt() && member.capital.religion != realm.religion)
                BackgroundReligionSystem.Convert(member.capital, realm.religion, member.capital.religion);
        }
    }

    // 有小人时(没有背景人口传教)：皈依后的国家每年逐城改宗
    private static void ManualCityConversion(List<Kingdom> realms)
    {
        foreach (Kingdom realm in realms)
        {
            if (!Within(realm.GetOrCreate().religion_converted_at, ConversionPushYears)) continue;
            foreach (Kingdom member in Members(realm))
            {
                if (!member.hasReligion() || member.religion != realm.religion) continue;
                foreach (City city in member.cities.ToList())
                {
                    if (city?.data == null || city.isRekt() || city.religion == member.religion ||
                        city.hasReligion() && city.religion.GetCity() == city || UnityEngine.Random.value >= ManualCityChance) continue;
                    BackgroundReligionSystem.Convert(city, member.religion, city.religion);
                }
            }
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 迫害与驱逐(用户 2026-10-26 选"迫害与驱逐"：灭佛没收寺产、驱逐异教徒、强迫改宗，换钱换统一，代价是稳定、人口外逃和起事)。
// 国家每年自行决定，不经派系提议：
//   · 谁：独立王国与帝国中央(帝国按全体成员的城)，境内异教之城至少 MinCities 座、或占城数 MinShare 以上；
//     每年几率：不宽容 IntolerantChance，收人头税 PollTaxChance，宽容的只在国库吃紧(不到半年经常收入)时 PurgeChance(灭佛)；
//     冷却 CooldownYears 年；
//   · 对象：境内奉的城最多的异教；
//   · 驱逐(国教排他、不宽容时一半机会，见 ReligionFaith.IsExclusive)：该教各城 ExpelShare 的背景人口出走，送到接壤的同教外国城，
//     没有就到接壤的外国城做难民(MigrationRelations：难民城五年稳定 −6)；城改奉国教；奉该教的国家十年内对本国好感 −40；
//   · 迫害：没收寺产(每户 ConfiscatePerHousehold 入国库)；一半的城当场改奉国教，其余十年稳定少 PersecutionStability、
//     当年各有 RevoltChance 机会举教起事(仍要稳定低于起事线，见 ReligiousTension)。
public static class ReligiousPersecution
{
    private const int MinCities = 2;
    private const float MinShare = 0.15f;
    private const float IntolerantChance = 0.03f, PollTaxChance = 0.01f, PurgeChance = 0.01f;
    private const int CooldownYears = 30;
    private const float ExpelShare = 0.2f;
    private const int ConfiscatePerHousehold = 2;
    private const float ConvertShare = 0.5f;
    private const float RevoltChance = 0.25f;
    public const float PersecutionStability = 8f;
    public const int PersecutionYears = 10;

    public static float StabilityLoss(City city)
    {
        if (city?.data == null) return 0f;
        double at = city.GetOrCreate().persecuted_at;
        return at >= 0d && World.world.getCurWorldTime() >= at && Date.getYearsSince(at) < PersecutionYears
            ? PersecutionStability : 0f;
    }

    private static bool Alive(Kingdom kingdom) => kingdom?.data != null && !kingdom.isRekt() && !kingdom.wild;

    private static Kingdom Realm(Kingdom kingdom) => kingdom?.GetEmpire()?.CoreKingdom ?? kingdom;

    public static void YearlyPass()
    {
        if (World.world?.kingdoms == null || ModClass.IS_CLEAR) return;
        foreach (Kingdom realm in World.world.kingdoms.list.ToList())
        {
            if (!Alive(realm) || Realm(realm) != realm || !realm.hasReligion() ||
                EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(realm)) continue;
            var data = realm.GetOrCreate();
            if (data.persecution_at >= 0d && World.world.getCurWorldTime() >= data.persecution_at &&
                Date.getYearsSince(data.persecution_at) < CooldownYears) continue;
            List<City> all = Cities(realm);
            List<City> heterodox = all.Where(ReligiousTension.IsHeterodox).ToList();
            if (heterodox.Count == 0) continue;
            IGrouping<Religion, City> target = heterodox.GroupBy(city => city.religion).OrderByDescending(group => group.Count()).First();
            if (target.Count() < MinCities && target.Count() < all.Count * MinShare) continue;
            ReligiousTension.Policy policy = ReligiousTension.PolicyOf(realm);
            float chance = policy switch
            {
                ReligiousTension.Policy.Intolerant => IntolerantChance,
                ReligiousTension.Policy.PollTax => PollTaxChance,
                _ => Strapped(realm) ? PurgeChance : 0f
            };
            if (UnityEngine.Random.value >= chance) continue;
            data.persecution_at = World.world.getCurWorldTime();
            bool expel = policy == ReligiousTension.Policy.Intolerant && ReligionFaith.IsExclusive(realm.religion) &&
                         UnityEngine.Random.value < 0.5f;
            if (expel) Expel(realm, target.Key, target.ToList());
            else Persecute(realm, target.Key, target.ToList());
        }
    }

    private static List<City> Cities(Kingdom realm)
    {
        Empire empire = realm.GetEmpire();
        IEnumerable<Kingdom> members = empire != null && empire.CoreKingdom == realm
            ? empire.kingdoms_list.Where(Alive)
            : new[] { realm };
        return members.SelectMany(member => member.cities).Where(city => city?.data != null && !city.isRekt()).ToList();
    }

    // 国库吃紧：不到半年经常收入
    private static bool Strapped(Kingdom realm) => realm.GetMoney() < StateDebtSystem.AnnualStableIncome(realm) / 2;

    // 迫害：没收寺产、一半的城改宗，其余不稳、可能起事
    private static void Persecute(Kingdom realm, Religion faith, List<City> cities)
    {
        long seized = 0;
        int converted = 0;
        double now = World.world.getCurWorldTime();
        foreach (City city in cities)
        {
            seized += (long)CityPopulationSystem.FiscalHouseholds(city) * ConfiscatePerHousehold;
            Religion state = city.kingdom.religion;
            if (UnityEngine.Random.value < ConvertShare && state != null)
            {
                BackgroundReligionSystem.Convert(city, state, faith);
                converted++;
                continue;
            }
            city.GetOrCreate().persecuted_at = now;
            ReligiousTension.TryPersecutionRevolt(city, RevoltChance);
        }
        if (seized > 0) realm.AddMoney((int)Mathf.Min(int.MaxValue, seized), TreasuryCategory.RecoveredFunds);
        string text = string.Format(LM.Get("persecution_log"), realm.GetKingdomName(), faith.name, MoneyDisplay.Format(seized), converted);
        TranslateHelper.LogEventMessage(text, realm);
        ReligionChronicle.Record(faith, realm.religion, ReligionChronicle.KindConversion, text);
        LogService.LogInfo($"[EmpireCraft][宗教] {realm.GetKingdomName()} 迫害{faith.name}：{cities.Count} 城，没收 {seized}，改宗 {converted} 城");
    }

    // 驱逐：各城两成人口出走到同教邻国(没有就做难民)，城改奉国教，同教之国结怨
    private static void Expel(Kingdom realm, Religion faith, List<City> cities)
    {
        bool moved = false;
        Empire empire = realm.GetEmpire();
        foreach (City city in cities)
        {
            if (CityPopulationSystem.AbstractPopulationEnabled) moved |= Emigrate(city, faith, realm, empire);
            Religion state = city.kingdom.religion;
            if (state != null) BackgroundReligionSystem.Convert(city, state, faith);
        }
        if (moved) CityPopulationSystem.InvalidateHouseholdCaches();
        foreach (Kingdom other in World.world.kingdoms.list.ToList())
            if (Alive(other) && other != realm && Realm(other) == other && other.religion == faith)
                MigrationRelations.Expelled(other, realm);
        string text = string.Format(LM.Get("expulsion_log"), realm.GetKingdomName(), faith.name, cities.Count);
        TranslateHelper.LogEventMessage(text, realm);
        ReligionChronicle.Record(faith, realm.religion, ReligionChronicle.KindConversion, text);
        LogService.LogInfo($"[EmpireCraft][宗教] {realm.GetKingdomName()} 驱逐{faith.name}信众：{cities.Count} 城");
    }

    private static bool Emigrate(City city, Religion faith, Kingdom realm, Empire empire)
    {
        CityPopulationData data = CityPopulationSystem.Get(city);
        if (data?.groups == null) return false;
        List<City> abroad = city.neighbours_cities?.Where(other => other?.data != null && !other.isRekt() && other.kingdom != null &&
            !other.kingdom.wild && Realm(other.kingdom) != realm).ToList() ?? new List<City>();
        City home = abroad.FirstOrDefault(other => other.religion == faith);
        City refuge = home ?? abroad.FirstOrDefault();
        PopulationParallelSystem.Discard(city);
        if (refuge != null) PopulationParallelSystem.Discard(refuge);
        bool moved = false;
        foreach (PopGroup group in data.groups.Where(group => group != null && group.Background > 0f &&
                     group.social_class != SocialClass.Army && group.social_class != SocialClass.Officer).ToList())
        {
            float removed = CityPopulationSystem.RemoveBackground(group, group.Background * ExpelShare);
            if (removed <= 0f) continue;
            moved = true;
            if (refuge != null)
                CityPopulationSystem.AddBackground(refuge, group.social_class, group.culture, group.species, group.ideology, removed);
        }
        if (moved && refuge != null && home == null) MigrationRelations.TakeRefugees(refuge);
        return moved;
    }
}

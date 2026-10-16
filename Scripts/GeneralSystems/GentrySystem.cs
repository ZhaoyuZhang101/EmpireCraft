using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 世家(用户 2026-10-16："我可能还需要一个世家系统"；选了门第郡望、仕途垄断、庄园部曲、兴衰站队四样，只给华夏)。
// 第一步——门第与郡望：
//   · 每年给每个在世的华夏宗族(在位皇帝的宗室除外)算一回"门第"目标(0~100)：
//       族人做官(在任官员每人 6，城主 4，诸侯王 12；虚拟族人记着官职的 3)、爵位与领有的头衔(每个 5，荣誉爵位 6，藩王 6)，
//       这两项合计至多 60；郡望之城的田产(每占一分地 1.5，至多 15)；名望最高的三人(每 30 名望 1，至多 10)；
//       族大(在世族人每人 0.4，至多 10)；年深(立族每 15 年 1，至多 10)；与世家、宗室联姻(每对 2，至多 8)；
//     门第向目标靠拢——升得快(每年一成五)、跌得慢(每年半成)：家道中落要好些年；
//   · 门第到三十列为世家，跌到二十二以下不再是世家；七十以上甲等、五十五乙等、四十丙等，其余丁等；
//   · 郡望：田产最多的城，没有田产按祖籍，再没有按族人最多的城；称"某城某氏"；
//   · 头一次算(旧存档、新开局)直接到位、不记史；此后列为世家、门第衰微记入郡望所在帝国的史册(每国每年至多三条)。
// 宗族成千上万，按帧分批算(见 Tick)。
public static class GentrySystem
{
    public const float EnterAt = 30f, LeaveAt = 22f;
    private const float RiseRate = 0.15f, FallRate = 0.05f, OfficeCap = 60f;
    private const int MaxRecordsPerEmpire = 3;

    private static readonly FrameBudgetQueue<long> Queue = new(1.5d, Assess, "世家门第");
    private static double _lastPass = -1d;
    private static readonly Dictionary<long, (float share, long city)> Land = new();
    private static readonly HashSet<SpecificClan> Ruling = new();
    private static readonly Dictionary<long, int> Recorded = new();

    public static void ResetWorldState()
    {
        Queue.Cancel();
        _lastPass = -1d;
        Land.Clear();
        Ruling.Clear();
        Recorded.Clear();
    }

    #region 查询

    public static bool IsGentry(SpecificClan clan) => clan != null && clan.gentry;

    public static bool IsGentry(Actor actor) => IsGentry(actor?.GetSpecificClan());

    public static float Pedigree(SpecificClan clan) => clan?.gentry_pedigree ?? 0f;

    // 门第等第：1 甲、2 乙、3 丙、4 丁
    public static int Grade(float pedigree) => pedigree >= 70f ? 1 : pedigree >= 55f ? 2 : pedigree >= 40f ? 3 : 4;

    public static string GradeName(SpecificClan clan) => LM.Get("gentry_grade_" + Grade(Pedigree(clan)));

    public static City Seat(SpecificClan clan)
    {
        long id = clan?.gentry_seat_city_id ?? -1L;
        City city = id > 0 ? World.world.cities.get(id) : null;
        return city?.data == null || city.isRekt() ? null : city;
    }

    // 郡望："某城某氏"
    public static string JunWang(SpecificClan clan)
    {
        if (clan == null) return "";
        string surname = !string.IsNullOrEmpty(clan.shi) ? clan.shi.TrimEnd('氏') : clan.name ?? "";
        City seat = Seat(clan);
        return string.Format(LM.Get("gentry_junwang"), seat?.GetCityName() ?? "", surname);
    }

    // 郡望在本帝国的世家，门第从高到低
    public static List<SpecificClan> Of(Empire empire)
    {
        if (empire?.data == null) return new List<SpecificClan>();
        return SpecificClanManager._specificClans
            .Where(clan => clan != null && clan.gentry && RealmOf(Seat(clan)) == empire)
            .OrderByDescending(clan => clan.gentry_pedigree).ToList();
    }

    private static Empire RealmOf(City city)
    {
        Kingdom kingdom = city?.kingdom;
        if (kingdom == null || kingdom.isRekt()) return null;
        return kingdom.IsEmpire() || kingdom.IsInEmpire() ? kingdom.GetEmpire() : null;
    }

    private static bool Huaxia(City city)
    {
        if (city == null) return false;
        try
        {
            return InstitutionSystem.GetCultureLine(CultureService.GetMainCulture(city, initialize: false)) == "Huaxia";
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region 每年分批结算

    // 每帧(CityPopulationSystem 里跟着虚拟族谱一起推进)：一年一轮
    public static void Tick()
    {
        if (World.world == null) return;
        if (Queue.Active)
        {
            Queue.Tick();
            return;
        }
        double now = World.world.getCurWorldTime();
        if (_lastPass >= 0d && now >= _lastPass && Date.getYearsSince(_lastPass) < 1) return;
        if (SpecificClanManager._specificClans.Count == 0) return;
        _lastPass = now;
        BuildLand();
        BuildRuling();
        Recorded.Clear();
        Queue.Start(SpecificClanManager._specificClans.Where(clan => clan != null && clan.extinct_since < 0d)
            .Select(clan => clan.id).ToList());
        Queue.Tick();
    }

    // 各宗族的私田：合计占地(按城的分数加总)、占地最多的城
    private static void BuildLand()
    {
        Land.Clear();
        foreach (City city in World.world.cities.list)
        {
            if (city?.data == null || city.isRekt()) continue;
            Dictionary<string, float> shares = city.GetOrCreate().household_land_shares;
            if (shares == null) continue;
            foreach (KeyValuePair<string, float> pair in shares)
            {
                if (pair.Value <= 0f || !pair.Key.StartsWith("s:", StringComparison.Ordinal) ||
                    !long.TryParse(pair.Key.Substring(2), out long clanId)) continue;
                Land.TryGetValue(clanId, out var held);
                float best = held.city > 0 && World.world.cities.get(held.city) is City bestCity &&
                             bestCity.GetOrCreate().household_land_shares.TryGetValue(pair.Key, out float bestShare)
                    ? bestShare : 0f;
                Land[clanId] = (held.share + pair.Value, pair.Value > best ? city.data.id : held.city);
            }
        }
    }

    // 在位皇帝的宗室(连同分出去的各支)：是皇族，不算世家
    private static void BuildRuling()
    {
        Ruling.Clear();
        if (ModClass.EMPIRE_MANAGER == null) return;
        foreach (Empire empire in ModClass.EMPIRE_MANAGER)
        {
            if (empire?.data == null || empire.isRekt() || empire.IsArchived() || empire.EmpireSpecificClan == null) continue;
            foreach (SpecificClan clan in SpecificClanManager.LineageClans(empire.EmpireSpecificClan)) Ruling.Add(clan);
        }
    }

    private static void Assess(long id)
    {
        SpecificClan clan = SpecificClanManager.Get(id);
        if (clan == null) return;
        List<PersonalClanIdentity> living = clan.LivingPeople;
        City seat = living.Count == 0 ? null : FindSeat(clan, living);
        if (living.Count == 0 || Ruling.Contains(clan) || !Huaxia(seat))
        {
            if (clan.gentry && living.Count == 0) Decline(clan, extinct: true);
            else if (clan.gentry) clan.gentry = false;
            return;
        }
        clan.gentry_seat_city_id = seat.data.id;
        float target = Target(clan, living);
        if (!clan.gentry_assessed)
        {
            // 头一次直接到位、不记史
            clan.gentry_assessed = true;
            clan.gentry_pedigree = target;
            clan.gentry = target >= EnterAt;
            if (clan.gentry) clan.gentry_since = World.world.getCurWorldTime();
            return;
        }
        float pedigree = clan.gentry_pedigree;
        pedigree += (target - pedigree) * (target > pedigree ? RiseRate : FallRate);
        clan.gentry_pedigree = Mathf.Clamp(pedigree, 0f, 100f);
        if (!clan.gentry && clan.gentry_pedigree >= EnterAt)
        {
            clan.gentry = true;
            clan.gentry_since = World.world.getCurWorldTime();
            Record(seat, string.Format(LM.Get("gentry_rise_history"), JunWang(clan), GradeName(clan)));
        }
        else if (clan.gentry && clan.gentry_pedigree < LeaveAt) Decline(clan, extinct: false);
    }

    private static void Decline(SpecificClan clan, bool extinct)
    {
        string name = JunWang(clan);
        City seat = Seat(clan);
        clan.gentry = false;
        Record(seat, string.Format(LM.Get(extinct ? "gentry_extinct_history" : "gentry_decline_history"), name));
    }

    // 郡望：田产最多的城；没有田产按祖籍；再没有按族人最多的城
    private static City FindSeat(SpecificClan clan, List<PersonalClanIdentity> living)
    {
        if (Land.TryGetValue(clan.id, out var held) && World.world.cities.get(held.city) is City landed && landed.data != null &&
            !landed.isRekt()) return landed;
        City ancestral = clan.ancestral_city_id > 0 ? World.world.cities.get(clan.ancestral_city_id) : null;
        if (ancestral?.data != null && !ancestral.isRekt()) return ancestral;
        long cityId = living.Select(person => person.is_virtual ? person.virtual_city_id : person._actor?.city?.data?.id ?? -1L)
            .Where(cityIdOf => cityIdOf > 0).GroupBy(cityIdOf => cityIdOf).OrderByDescending(group => group.Count())
            .Select(group => group.Key).FirstOrDefault();
        City common = cityId > 0 ? World.world.cities.get(cityId) : null;
        return common?.data != null && !common.isRekt() ? common : null;
    }

    private static float Target(SpecificClan clan, List<PersonalClanIdentity> living)
    {
        float office = 0f, marriage = 0f;
        var renown = new List<int>();
        foreach (PersonalClanIdentity person in living)
        {
            Actor actor = person.is_virtual ? null : person._actor;
            if (actor?.data != null && !actor.isRekt() && actor.isAlive())
            {
                if (actor.isKing()) office += 12f;
                else if (actor.IsOnOffice()) office += 6f;
                else if (actor.isCityLeader()) office += 4f;
                if (actor.HasHonoraryPeerage()) office += 6f;
                office += 5f * (actor.GetOwnedTitle()?.Count ?? 0);
                renown.Add(actor.renown);
            }
            else
            {
                if (!string.IsNullOrEmpty(person.officeName)) office += 3f;
                office += 5f * (person.ownedTitleNames?.Count ?? 0);
            }
            if (!string.IsNullOrEmpty(person.prince_title)) office += 6f;
            SpecificClan spouse = person.lover.identity > 0 ? SpecificClanManager.getPerson(person.lover.identity)?._specificClan : null;
            if (spouse != null && spouse != clan && (spouse.gentry || Ruling.Contains(spouse))) marriage += 2f;
        }
        float score = Mathf.Min(OfficeCap, office);
        if (Land.TryGetValue(clan.id, out var held)) score += Mathf.Min(15f, held.share * 1.5f);
        score += Mathf.Min(10f, renown.OrderByDescending(value => value).Take(3).Sum() / 30f);
        score += Mathf.Min(10f, living.Count * 0.4f);
        if (clan.established_timestamp > 0d) score += Mathf.Min(10f, Date.getYearsSince(clan.established_timestamp) / 15f);
        score += Mathf.Min(8f, marriage);
        return Mathf.Clamp(score, 0f, 100f);
    }

    private static void Record(City seat, string content)
    {
        LogService.LogInfo("[EmpireCraft][世家] " + content);
        Empire empire = RealmOf(seat);
        if (empire == null) return;
        Recorded.TryGetValue(empire.data.id, out int count);
        if (count >= MaxRecordsPerEmpire) return;
        Recorded[empire.data.id] = count + 1;
        empire.RecordHistory(directContent: content);
    }

    #endregion
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;
using Newtonsoft.Json;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 归化民(穿越政权)：身份与文化、职业、受教育程度、元老资格分开记，讲同样的话不算归化，归化了也不是元老。
//   · 台账：每座城一份(CityPopulationData.naturalization)，只是给城里已有的人贴标签：同一个人同一时间只占一种状态，
//     总数不超过这座城的背景人口，所以怎么算都不会多出人来。阶层、文化、理念之间的转移都在一座城里，不动台账；
//     迁徙、开拓按比例带走(见 Carry)；饥荒、战乱净减的人口按比例摊到各状态(见 Reconcile)。
//   · 流程：收容登记 → 检疫安置 → 基础教育 → 待分配，一年走一步。登记看干部、检疫看护理、教育看教师，另有元老亲自抓的底子；
//     每步花钱，缺钱、缺人手、闹饥荒就停在那一步，停在哪、为什么都记下来(见 NaturalizationReport)。
//   · 岗位：结业的人按缺口分给护理、教师、干部、军人，其余做工；这些是岗位不是世袭，人手还缺时工人里抽一小部分转岗。
//     培养重点可以手动定，默认按需。干部不够的城从富余的城调人，人是真搬过去的。
//   · 地主、贵族、官吏、军人不进流程；一座城受训的人至多占 max_share，其余是留在本地社会的居民。
// 数值在 Scripts/Regimes/Configs/Transmigrant/Naturalization.json，说明见同目录上一级的 README。
public enum NaturalizationState
{
    Registered, Quarantine, Schooling, Idle, Worker, Soldier, Cadre, Teacher, Nurse
}

public sealed class NaturalizationConfig
{
    public float max_share = 0.75f;
    public float registry_per_cadre = 10f, quarantine_per_nurse = 10f, school_per_teacher = 10f;
    public float base_registry = 100f, base_quarantine = 60f, base_school = 60f;
    public float cost_registry = 0.1f, cost_quarantine = 0.2f, cost_school = 0.3f;
    public float budget_share = 0.5f, school_years = 2f;
    public float worker_floor = 0.3f, retrain_share = 0.05f, focus_share = 0.5f, soldier_reserve = 0.2f;
    public float cadre_per_people = 400f, cadre_base = 5f, cadre_min_yield = 0.6f, dispatch_share = 0.5f;
    public float workers_per_factory = 100f, staffing_floor = 0.25f;
    public float culture_shift = 0.08f, assimilation = 4f;
    public float landing_worker = 0.44f, landing_soldier = 0.34f, landing_cadre = 0.08f, landing_teacher = 0.08f, landing_nurse = 0.06f;
    public float migrate_worker = 0.7f, migrate_soldier = 0.15f, migrate_cadre = 0.05f, migrate_teacher = 0.05f, migrate_nurse = 0.05f;
}

public sealed class NaturalizationFlow
{
    public float moved;
    public int cost;
    // 没走完的原因(语言键)；都走完了或没人等是空
    public string block;
}

public sealed class NaturalizationReport
{
    public readonly NaturalizationFlow Intake = new(), Quarantine = new(), School = new();
    public float graduates, dispatched, shifted;
}

public sealed class NaturalizationSnapshot
{
    public readonly float[] totals = new float[NaturalizationSystem.StateCount];
    public float untrained, registryCapacity, quarantineCapacity, schoolCapacity;
    public float workerWant, soldierWant, cadreWant, teacherWant, nurseWant;
    public NaturalizationReport last;
    public readonly List<(City city, float have, float need)> gaps = new();

    public float Count(NaturalizationState state) => totals[(int)state];
    public float InProcess => Count(NaturalizationState.Registered) + Count(NaturalizationState.Quarantine) + Count(NaturalizationState.Schooling);
    public float Completed => Count(NaturalizationState.Idle) + Count(NaturalizationState.Worker) + Count(NaturalizationState.Soldier) +
                              Count(NaturalizationState.Cadre) + Count(NaturalizationState.Teacher) + Count(NaturalizationState.Nurse);
}

public static class NaturalizationSystem
{
    public const int StateCount = 9;
    private static NaturalizationConfig _config;
    private static readonly Dictionary<long, NaturalizationReport> Reports = new();
    private static object _world;

    public static NaturalizationConfig Config
    {
        get
        {
            if (_config != null) return _config;
            try
            {
                string path = Path.Combine(ModClass._declare.FolderPath, "Scripts", "Regimes", "Configs", "Transmigrant", "Naturalization.json");
                _config = JsonConvert.DeserializeObject<NaturalizationConfig>(File.ReadAllText(path)) ?? new NaturalizationConfig();
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][归化] Naturalization.json 读取失败，按默认数值：{exception.Message}");
                _config = new NaturalizationConfig();
            }
            return _config;
        }
    }

    #region 台账

    private static CityPopulationData Data(City city) => city?.data == null || city.isRekt() ? null : CityPopulationSystem.Get(city);

    public static bool HasLedger(City city) => Data(city)?.naturalization != null;

    private static float[] Ledger(CityPopulationData data)
    {
        if (data.naturalization == null) data.naturalization = new float[StateCount];
        else if (data.naturalization.Length < StateCount) Array.Resize(ref data.naturalization, StateCount);
        return data.naturalization;
    }

    public static float Count(City city, NaturalizationState state)
    {
        float[] ledger = Data(city)?.naturalization;
        return ledger == null || (int)state >= ledger.Length ? 0f : ledger[(int)state];
    }

    private static float Sum(float[] ledger, int from, int to)
    {
        float total = 0f;
        for (int i = from; i <= to && i < ledger.Length; i++) total += ledger[i];
        return total;
    }

    // 已归化(走完流程的)
    public static float Completed(City city)
    {
        float[] ledger = Data(city)?.naturalization;
        return ledger == null ? 0f : Sum(ledger, (int)NaturalizationState.Idle, StateCount - 1);
    }

    // 进了台账的(流程中的连同已归化的)
    public static float Identity(City city)
    {
        float[] ledger = Data(city)?.naturalization;
        return ledger == null ? 0f : Sum(ledger, 0, StateCount - 1);
    }

    private static bool EligibleClass(SocialClass socialClass) =>
        socialClass is not (SocialClass.Noble or SocialClass.Landlord or SocialClass.SlaveOwner or SocialClass.Army or SocialClass.Officer);

    private static float EligibleBackground(CityPopulationData data)
    {
        float total = 0f;
        if (data?.groups != null)
            foreach (PopGroup group in data.groups)
                if (group != null && EligibleClass(group.social_class)) total += group.Background;
        return total;
    }

    // 还没进台账的居民
    public static float Untrained(City city)
    {
        CityPopulationData data = Data(city);
        return data == null ? 0f : Mathf.Max(0f, EligibleBackground(data) - Identity(city));
    }

    // 对上实际人口：比上次少了就按比例摊到各状态，无论如何不超过背景人口。只标人，不产生人
    private static void Reconcile(City city)
    {
        CityPopulationData data = Data(city);
        if (data?.naturalization == null) return;
        float[] ledger = Ledger(data);
        float background = CityPopulationSystem.GetBackgroundTotal(city);
        float last = data.naturalization_background;
        float factor = last > 0f && background < last ? background / last : 1f;
        float sum = Sum(ledger, 0, StateCount - 1);
        if (sum * factor > background && sum > 0f) factor = background / sum;
        if (factor < 1f)
            for (int i = 0; i < ledger.Length; i++) ledger[i] *= factor;
        data.naturalization_background = background;
    }

    // 迁徙、开拓(CityPopulationSystem.TransferBackground 等)：走的人按比例带走标签。背景人口账一起记，年底不算损失
    public static void Carry(City from, City to, float moved, float totalBefore)
    {
        CityPopulationData source = Data(from);
        if (moved <= 0f || source == null) return;
        if (source.naturalization != null && totalBefore > 0f)
        {
            float share = Mathf.Clamp01(moved / totalBefore);
            float[] ledger = Ledger(source);
            CityPopulationData target = Data(to);
            float[] arriving = target == null ? null : Ledger(target);
            for (int i = 0; i < ledger.Length; i++)
            {
                float people = ledger[i] * share;
                ledger[i] -= people;
                if (arriving != null) arriving[i] += people;
            }
            if (target != null) target.naturalization_background += moved;
            source.naturalization_background = Mathf.Max(0f, source.naturalization_background - moved);
            return;
        }
        // 没有台账的城迁来：这里的人多了，不算别人的
        CityPopulationData destination = Data(to);
        if (destination?.naturalization != null) destination.naturalization_background += moved;
    }

    // 征走的人(见 CorpsSystem.Reinforce)；trained：是合格军人，从军人岗位上扣
    public static void Drafted(City city, float people, bool trained)
    {
        CityPopulationData data = Data(city);
        if (data?.naturalization == null || people <= 0f) return;
        data.naturalization_background = Mathf.Max(0f, data.naturalization_background - people);
        if (!trained) return;
        float[] ledger = Ledger(data);
        ledger[(int)NaturalizationState.Soldier] = Mathf.Max(0f, ledger[(int)NaturalizationState.Soldier] - people);
    }

    // 复员回来的人(见 CorpsSystem.Demobilize)；trainedShare：其中受过训的比例，回到军人岗位
    public static void Returned(City city, float people, float trainedShare)
    {
        CityPopulationData data = Data(city);
        if (data == null || people <= 0f) return;
        if (data.naturalization == null && trainedShare <= 0f) return;
        float[] ledger = Ledger(data);
        ledger[(int)NaturalizationState.Soldier] += people * Mathf.Clamp01(trainedShare);
        data.naturalization_background += people;
    }

    // 干部不够的城赋税、征兵收不齐(见 AdministrativeControlSystem.Yield)：打下来不等于管得了，调干部过去就好。
    // 这条很热，先看有没有算过；城落到别人手里就不再算
    public static float YieldFactor(City city)
    {
        CityPopulationData data = Data(city);
        if (data == null || data.naturalization_cover < 0f) return 1f;
        if (TransmigrantSystem.SenateOf(city.kingdom) == null && !TransmigrantSystem.Mobilizes(city.kingdom)) return 1f;
        float floor = Config.cadre_min_yield;
        return floor + (1f - floor) * data.naturalization_cover;
    }

    // 事件里的死伤(见 TransmigrantSystem.Events)：从背景人口里扣，干部、教师首当其冲，年底对账不再算一遍
    public static float Casualties(City city, float people)
    {
        CityPopulationData data = Data(city);
        if (data?.groups == null || people <= 0f) return 0f;
        float dead = 0f;
        for (int attempt = 0; attempt < 8 && dead < people - 0.01f; attempt++)
        {
            PopGroup group = CityPopulationSystem.DrawBackground(city, candidate => EligibleClass(candidate.social_class));
            if (group == null) break;
            float removed = CityPopulationSystem.RemoveBackground(group, people - dead);
            if (removed <= 0f) break;
            dead += removed;
        }
        if (dead <= 0f) return 0f;
        if (data.naturalization != null)
        {
            float[] ledger = Ledger(data);
            ledger[(int)NaturalizationState.Cadre] = Mathf.Max(0f, ledger[(int)NaturalizationState.Cadre] - dead * 0.5f);
            ledger[(int)NaturalizationState.Teacher] = Mathf.Max(0f, ledger[(int)NaturalizationState.Teacher] - dead * 0.5f);
            data.naturalization_background = Mathf.Max(0f, data.naturalization_background - dead);
        }
        CityPopulationSystem.InvalidateHouseholdCaches();
        return dead;
    }

    // 这座城能出的合格军人
    public static float Available(City city) => Count(city, NaturalizationState.Soldier);

    #endregion

    #region 开局与旧档

    // 降临带来的人：都已归化，按配置分到各岗位
    public static void SeedLanding(City city, TransmigrantState state, float settlers)
    {
        CityPopulationData data = Data(city);
        if (data == null || state == null) return;
        NaturalizationConfig c = Config;
        float[] ledger = Ledger(data);
        ledger[(int)NaturalizationState.Worker] += settlers * c.landing_worker;
        ledger[(int)NaturalizationState.Soldier] += settlers * c.landing_soldier;
        ledger[(int)NaturalizationState.Cadre] += settlers * c.landing_cadre;
        ledger[(int)NaturalizationState.Teacher] += settlers * c.landing_teacher;
        ledger[(int)NaturalizationState.Nurse] += settlers * c.landing_nurse;
        data.naturalization_background = CityPopulationSystem.GetBackgroundTotal(city);
        state.naturalization_seeded = true;
    }

    // 旧档：以前"澳洲人文化 = 归化民"(元老除外)，这些人算已归化，按配置分岗；只做一次
    private static void Seed(TransmigrantState state, List<City> cities)
    {
        if (state.naturalization_seeded) return;
        state.naturalization_seeded = true;
        NaturalizationConfig c = Config;
        foreach (City city in cities)
        {
            CityPopulationData data = Data(city);
            if (data?.groups == null) continue;
            float people = 0f;
            foreach (PopGroup group in data.groups)
                if (group != null && group.culture == TransmigrantSystem.Culture && group.social_class != SocialClass.Noble)
                    people += group.Background;
            float[] ledger = Ledger(data);
            ledger[(int)NaturalizationState.Worker] += people * c.migrate_worker;
            ledger[(int)NaturalizationState.Soldier] += people * c.migrate_soldier;
            ledger[(int)NaturalizationState.Cadre] += people * c.migrate_cadre;
            ledger[(int)NaturalizationState.Teacher] += people * c.migrate_teacher;
            ledger[(int)NaturalizationState.Nurse] += people * c.migrate_nurse;
            data.naturalization_background = CityPopulationSystem.GetBackgroundTotal(city);
        }
    }

    #endregion

    #region 年度流程

    // 本国、殖民地、帝国里的大区
    private static List<City> Homes(Kingdom kingdom)
    {
        var seen = new HashSet<City>();
        var realms = new List<Kingdom> { kingdom };
        realms.AddRange(ColonySystem.ColoniesOf(kingdom));
        List<Kingdom> members = kingdom.GetEmpire()?.kingdoms_list;
        if (members != null) realms.AddRange(members);
        foreach (Kingdom realm in realms)
        {
            if (realm?.cities == null || realm.isRekt()) continue;
            foreach (City city in realm.cities)
                if (city?.data != null && !city.isRekt()) seen.Add(city);
        }
        return seen.ToList();
    }

    private static float[] Totals(List<City> cities)
    {
        var totals = new float[StateCount];
        foreach (City city in cities)
        {
            float[] ledger = Data(city)?.naturalization;
            if (ledger == null) continue;
            for (int i = 0; i < StateCount && i < ledger.Length; i++) totals[i] += ledger[i];
        }
        return totals;
    }

    private static float ProcessRate() => InstitutionSystem.IsEnacted(TransmigrantSystem.Culture, "aosong_first_committee") ? 1f : 0.5f;

    // 容量倍数：全体大会、全岛平定加大，看守内阁期间打折(见 TransmigrantSystem.Events)
    private static float CapacityRate(TransmigrantState state)
    {
        float rate = ProcessRate();
        if (state == null) return rate;
        if (state.naturalization_boost > 0f) rate *= state.naturalization_boost;
        if (state.caretaker_until >= 0 && Date.getCurrentYear() < state.caretaker_until) rate *= CaretakerFactor;
        return rate;
    }

    private const float CaretakerFactor = 0.75f;

    private static float RegistryCapacity(float[] totals, float rate) =>
        (Config.base_registry + totals[(int)NaturalizationState.Cadre] * Config.registry_per_cadre) * rate;

    private static float QuarantineCapacity(float[] totals, float rate) =>
        (Config.base_quarantine + totals[(int)NaturalizationState.Nurse] * Config.quarantine_per_nurse) * rate;

    private static float SchoolCapacity(float[] totals, float rate) =>
        (Config.base_school + totals[(int)NaturalizationState.Teacher] * Config.school_per_teacher) * rate;

    private static float CadreNeed(City city)
    {
        float people = CityPopulationSystem.GetBackgroundTotal(city);
        return people <= 0f ? 0f : Config.cadre_base + people / Config.cadre_per_people;
    }

    private static bool Hungry(City city) => Data(city)?.last_food_shortage > 0f;

    // 按比例把 from 状态的人挪到 to 状态
    private static void Advance(IEnumerable<City> cities, NaturalizationState from, NaturalizationState to, float share)
    {
        if (share <= 0f) return;
        foreach (City city in cities)
        {
            float[] ledger = Data(city)?.naturalization;
            if (ledger == null) continue;
            float people = ledger[(int)from] * share;
            ledger[(int)from] -= people;
            ledger[(int)to] += people;
        }
    }

    // 花得起几个人：funds 不够时只走一部分
    private static float Afford(float people, float perHead, ref long funds, out int cost)
    {
        cost = 0;
        if (people <= 0f) return 0f;
        if (perHead <= 0f) return people;
        float can = Mathf.Min(people, funds / perHead);
        cost = Mathf.CeilToInt(can * perHead);
        funds = Math.Max(0L, funds - cost);
        return can;
    }

    // 一年：倒着走(先结业再收人)，同一个人一年只走一步。返回今年结业的人数
    public static float Yearly(Kingdom kingdom, TransmigrantState state)
    {
        if (kingdom?.data == null || state == null) return 0f;
        if (!ReferenceEquals(_world, World.world))
        {
            Reports.Clear();
            _world = World.world;
        }
        List<City> cities = Homes(kingdom);
        Seed(state, cities);
        foreach (City city in cities) Reconcile(city);
        NaturalizationConfig c = Config;
        var report = new NaturalizationReport();
        Reports[kingdom.id] = report;
        float rate = ProcessRate();
        float capacityRate = CapacityRate(state);
        // 闹饥荒的城这一年什么都不动
        List<City> open = cities.Where(city => !Hungry(city)).ToList();
        bool famine = open.Count < cities.Count;
        float[] all = Totals(cities), active = Totals(open);
        long funds = (long)(Mathf.Max(0, StateSettlementSystem.DiscretionaryFunds(kingdom)) * c.budget_share);
        int spent = 0;

        // 基础教育：先交学费，交得起的才结业
        float students = active[(int)NaturalizationState.Schooling];
        float paid = Afford(students, c.cost_school, ref funds, out int tuition);
        float paidShare = students > 0f ? paid / students : 1f;
        spent += tuition;
        report.School.cost = tuition;
        float graduates = students * paidShare / Mathf.Max(1f, c.school_years);
        if (students > 0f) Advance(open, NaturalizationState.Schooling, NaturalizationState.Idle, graduates / students);
        report.graduates = graduates;

        // 检疫完进学校：看教师
        float inSchool = all[(int)NaturalizationState.Schooling] - graduates;
        float seats = Mathf.Max(0f, SchoolCapacity(all, capacityRate) - inSchool);
        float quarantined = active[(int)NaturalizationState.Quarantine];
        float entrants = Mathf.Min(quarantined, seats);
        // 入学先交第一年学费：没钱的留在检疫营，不白进学校
        float entering = Afford(entrants, c.cost_school, ref funds, out int entryCost);
        spent += entryCost;
        report.School.cost += entryCost;
        if (quarantined > 0f) Advance(open, NaturalizationState.Quarantine, NaturalizationState.Schooling, entering / quarantined);
        report.School.moved = entering;
        if (paidShare < 0.999f || entering < entrants - 0.5f) report.School.block = "naturalization_block_no_money";
        else if (entrants < quarantined - 0.5f) report.School.block = "naturalization_block_no_teacher";

        // 登记完进检疫：看护理
        float stillQuarantined = all[(int)NaturalizationState.Quarantine] - entering;
        seats = Mathf.Max(0f, QuarantineCapacity(all, capacityRate) - stillQuarantined);
        float registered = active[(int)NaturalizationState.Registered];
        float room = Mathf.Min(registered, seats);
        float admitted = Afford(room, c.cost_quarantine, ref funds, out int quarantineCost);
        spent += quarantineCost;
        report.Quarantine.cost = quarantineCost;
        report.Quarantine.moved = admitted;
        if (registered > 0f) Advance(open, NaturalizationState.Registered, NaturalizationState.Quarantine, admitted / registered);
        if (admitted < room - 0.5f) report.Quarantine.block = "naturalization_block_no_money";
        else if (room < registered - 0.5f) report.Quarantine.block = "naturalization_block_no_nurse";

        // 收容登记：看干部；一城受训的至多 max_share，其余留在本地社会
        float stillRegistered = all[(int)NaturalizationState.Registered] - admitted;
        seats = Mathf.Max(0f, RegistryCapacity(all, capacityRate) - stillRegistered);
        var rooms = new List<(City city, float room)>();
        float wanting = 0f;
        foreach (City city in open)
        {
            CityPopulationData data = Data(city);
            if (data == null) continue;
            float cityRoom = Mathf.Max(0f, EligibleBackground(data) * c.max_share - Identity(city));
            if (cityRoom <= 0f) continue;
            rooms.Add((city, cityRoom));
            wanting += cityRoom;
        }
        float allowed = Mathf.Min(wanting, seats);
        float intake = Afford(allowed, c.cost_registry, ref funds, out int registryCost);
        spent += registryCost;
        report.Intake.cost = registryCost;
        report.Intake.moved = intake;
        if (wanting > 0f)
            foreach ((City city, float cityRoom) in rooms)
            {
                CityPopulationData data = Data(city);
                Ledger(data)[(int)NaturalizationState.Registered] += cityRoom / wanting * intake;
                if (data.naturalization_background <= 0f) data.naturalization_background = CityPopulationSystem.GetBackgroundTotal(city);
            }
        if (intake < allowed - 0.5f) report.Intake.block = "naturalization_block_no_money";
        else if (allowed < wanting - 0.5f) report.Intake.block = "naturalization_block_no_cadre";
        if (famine)
        {
            report.Intake.block ??= "naturalization_block_famine";
            report.Quarantine.block ??= "naturalization_block_famine";
            report.School.block ??= "naturalization_block_famine";
        }
        if (spent > 0) kingdom.SubMoney(spent, TreasuryCategory.Welfare);

        Assign(kingdom, state, cities);
        report.dispatched = DispatchCadres(cities);
        report.shifted = ShiftCulture(cities, rate);
        UpdateEffects(cities);
        LogYear(cities, report, spent);
        return graduates;
    }

    // 每年一行，查节奏和卡点(只记录)
    private static void LogYear(List<City> cities, NaturalizationReport report, int spent)
    {
        float[] t = Totals(cities);
        float untrained = 0f;
        foreach (City city in cities) untrained += Untrained(city);
        string Why(NaturalizationFlow flow) => string.IsNullOrEmpty(flow.block) ? "无" : flow.block.Replace("naturalization_block_", "");
        LogService.LogInfo($"[EmpireCraft][归化] 登记{t[(int)NaturalizationState.Registered]:0}(进{report.Intake.moved:0} 卡{Why(report.Intake)}) " +
                           $"检疫{t[(int)NaturalizationState.Quarantine]:0}(进{report.Quarantine.moved:0} 卡{Why(report.Quarantine)}) " +
                           $"上学{t[(int)NaturalizationState.Schooling]:0}(进{report.School.moved:0} 卡{Why(report.School)}) 结业{report.graduates:0} " +
                           $"工人{t[(int)NaturalizationState.Worker]:0} 军人{t[(int)NaturalizationState.Soldier]:0} 干部{t[(int)NaturalizationState.Cadre]:0} " +
                           $"教师{t[(int)NaturalizationState.Teacher]:0} 护理{t[(int)NaturalizationState.Nurse]:0} 未受训{untrained:0} 调干部{report.dispatched:0} " +
                           $"改文化{report.shifted:0} 花费{spent}");
    }

    // 各岗位还缺多少(不算工人)
    private static void Shortages(Kingdom kingdom, float[] totals, List<City> cities, float[] shortage)
    {
        NaturalizationConfig c = Config;
        shortage[(int)NaturalizationState.Nurse] = Mathf.Max(0f, (totals[(int)NaturalizationState.Registered] +
            totals[(int)NaturalizationState.Quarantine]) / c.quarantine_per_nurse - totals[(int)NaturalizationState.Nurse]);
        shortage[(int)NaturalizationState.Teacher] = Mathf.Max(0f, (totals[(int)NaturalizationState.Quarantine] +
            totals[(int)NaturalizationState.Schooling]) / c.school_per_teacher - totals[(int)NaturalizationState.Teacher]);
        shortage[(int)NaturalizationState.Soldier] = Mathf.Max(0f, TransmigrantSystem.SoldierDemand(kingdom) - totals[(int)NaturalizationState.Soldier]);
        float cadres = 0f;
        foreach (City city in cities) cadres += CadreNeed(city);
        shortage[(int)NaturalizationState.Cadre] = Mathf.Max(0f, cadres - totals[(int)NaturalizationState.Cadre]);
    }

    private static readonly NaturalizationState[] Posts =
    {
        NaturalizationState.Nurse, NaturalizationState.Teacher, NaturalizationState.Soldier, NaturalizationState.Cadre
    };

    // people 个人按缺口比例分到各岗位；shortage 随之减少。返回分出去的人数
    private static float Distribute(float[] ledger, float people, float[] shortage)
    {
        float total = 0f;
        foreach (NaturalizationState post in Posts) total += shortage[(int)post];
        if (total <= 0f || people <= 0f) return 0f;
        float share = Mathf.Min(1f, people / total);
        float given = 0f;
        foreach (NaturalizationState post in Posts)
        {
            float x = shortage[(int)post] * share;
            ledger[(int)post] += x;
            shortage[(int)post] -= x;
            given += x;
        }
        return given;
    }

    // 待分配的人进岗位：先留够工人，有培养重点的先给重点，其余按缺口，剩下的都去做工；还缺人就从工人里转一小部分
    private static void Assign(Kingdom kingdom, TransmigrantState state, List<City> cities)
    {
        NaturalizationConfig c = Config;
        var shortage = new float[StateCount];
        Shortages(kingdom, Totals(cities), cities, shortage);
        int focus = state.naturalization_focus;
        foreach (City city in cities)
        {
            float[] ledger = Data(city)?.naturalization;
            if (ledger == null) continue;
            float idle = ledger[(int)NaturalizationState.Idle];
            if (idle <= 0f) continue;
            ledger[(int)NaturalizationState.Idle] = 0f;
            float rest = idle * (1f - Mathf.Clamp01(c.worker_floor));
            float workers = idle - rest;
            if (focus >= (int)NaturalizationState.Worker && focus < StateCount)
            {
                float toFocus = rest * Mathf.Clamp01(c.focus_share);
                ledger[focus] += toFocus;
                shortage[focus] = Mathf.Max(0f, shortage[focus] - toFocus);
                rest -= toFocus;
            }
            rest -= Distribute(ledger, rest, shortage);
            ledger[(int)NaturalizationState.Worker] += workers + rest;
        }
        foreach (City city in cities)
        {
            float[] ledger = Data(city)?.naturalization;
            if (ledger == null) continue;
            float pool = ledger[(int)NaturalizationState.Worker] * Mathf.Clamp01(c.retrain_share);
            ledger[(int)NaturalizationState.Worker] -= Distribute(ledger, pool, shortage);
        }
    }

    // 干部不够的城从富余的城调人：人真搬过去
    private static float DispatchCadres(List<City> cities)
    {
        NaturalizationConfig c = Config;
        var needy = new List<(City city, float gap)>();
        var donors = new List<(City city, float spare)>();
        foreach (City city in cities)
        {
            float gap = CadreNeed(city) - Count(city, NaturalizationState.Cadre);
            if (gap >= 1f) needy.Add((city, gap));
            else if (gap <= -1f) donors.Add((city, -gap * Mathf.Clamp01(c.dispatch_share)));
        }
        if (needy.Count == 0 || donors.Count == 0) return 0f;
        needy.Sort((a, b) => b.gap.CompareTo(a.gap));
        float total = 0f;
        int d = 0;
        foreach ((City city, float gap) in needy)
        {
            float left = gap;
            while (left >= 1f && d < donors.Count)
            {
                (City donor, float spare) = donors[d];
                float moved = MovePeople(donor, city, NaturalizationState.Cadre, Mathf.Min(left, spare));
                left -= moved;
                total += moved;
                spare -= moved;
                donors[d] = (donor, spare);
                if (spare < 1f || moved <= 0f) d++;
            }
        }
        return total;
    }

    // 把 from 城 state 状态的 amount 个人真搬到 to 城：背景人口、台账一起动
    private static float MovePeople(City from, City to, NaturalizationState state, float amount)
    {
        CityPopulationData source = Data(from), target = Data(to);
        if (source == null || target == null || amount <= 0f) return 0f;
        float[] ledger = Ledger(source);
        amount = Mathf.Min(amount, ledger[(int)state]);
        float moved = 0f;
        for (int attempt = 0; attempt < 8 && moved < amount - 0.01f; attempt++)
        {
            PopGroup group = CityPopulationSystem.DrawBackground(from, candidate => EligibleClass(candidate.social_class));
            if (group == null) break;
            float removed = CityPopulationSystem.RemoveBackground(group, amount - moved);
            if (removed <= 0f) break;
            CityPopulationSystem.AddBackground(to, group.social_class, group.culture, group.species, group.ideology, removed);
            moved += removed;
        }
        if (moved <= 0f) return 0f;
        ledger[(int)state] -= moved;
        Ledger(target)[(int)state] += moved;
        source.naturalization_background = Mathf.Max(0f, source.naturalization_background - moved);
        target.naturalization_background += moved;
        CityPopulationSystem.InvalidateHouseholdCaches();
        return moved;
    }

    // 文化慢慢跟上：已归化的人里每年一小部分改说澳洲话、过澳洲日子。身份不看这个，反过来也不算
    private static float ShiftCulture(List<City> cities, float rate)
    {
        NaturalizationConfig c = Config;
        float total = 0f;
        foreach (City city in cities)
        {
            CityPopulationData data = Data(city);
            if (data?.groups == null) continue;
            float completed = Completed(city), background = CityPopulationSystem.GetBackgroundTotal(city);
            if (completed <= 0f || background <= 0f) continue;
            float want = completed * c.culture_shift * rate;
            float others = 0f;
            foreach (PopGroup group in data.groups)
                if (group != null && group.culture != TransmigrantSystem.Culture && EligibleClass(group.social_class)) others += group.Background;
            if (others > 0f && want > 0f)
            {
                float share = Mathf.Min(1f, want / others);
                foreach (PopGroup group in data.groups.Where(group => group != null && group.culture != TransmigrantSystem.Culture &&
                                                                      EligibleClass(group.social_class) && group.Background > 0f).ToList())
                {
                    float removed = CityPopulationSystem.RemoveBackground(group, group.Background * share);
                    if (removed <= 0f) continue;
                    CityPopulationSystem.AddBackground(city, group.social_class, TransmigrantSystem.Culture, group.species, group.ideology, removed);
                    total += removed;
                }
            }
            CultureService.ApplyAssimilation(city, TransmigrantSystem.Culture, c.assimilation * rate * Mathf.Clamp01(completed / background));
        }
        if (total >= 1f) CityPopulationSystem.InvalidateHouseholdCaches();
        return total;
    }

    // 干部覆盖、工厂配员：一年算一次，读的地方很热
    private static void UpdateEffects(List<City> cities)
    {
        NaturalizationConfig c = Config;
        foreach (City city in cities)
        {
            CityPopulationData data = Data(city);
            if (data == null) continue;
            float need = CadreNeed(city);
            data.naturalization_cover = need <= 0f ? 1f : Mathf.Clamp01(Count(city, NaturalizationState.Cadre) / need);
            float wanted = FactorySystem.Count(city) * c.workers_per_factory;
            data.naturalization_staffing = wanted <= 0f ? 1f
                : Mathf.Clamp(Count(city, NaturalizationState.Worker) / wanted, Mathf.Clamp01(c.staffing_floor), 1f);
        }
    }

    public static void SetFocus(TransmigrantState state, NaturalizationState? focus)
    {
        if (state != null) state.naturalization_focus = focus.HasValue ? (int)focus.Value : -1;
    }

    #endregion

    #region 查询

    // 已归化的总人数(见 TransmigrantSystem.NaturalizedPopulation)
    public static float CompletedPopulation(Kingdom kingdom)
    {
        float total = 0f;
        foreach (City city in Homes(kingdom)) total += Completed(city);
        return total;
    }

    public static NaturalizationSnapshot Snapshot(Kingdom kingdom)
    {
        var snapshot = new NaturalizationSnapshot();
        if (kingdom?.data == null) return snapshot;
        NaturalizationConfig c = Config;
        List<City> cities = Homes(kingdom);
        float[] totals = Totals(cities);
        Array.Copy(totals, snapshot.totals, StateCount);
        float rate = CapacityRate(TransmigrantSystem.Of(kingdom));
        snapshot.registryCapacity = RegistryCapacity(totals, rate);
        snapshot.quarantineCapacity = QuarantineCapacity(totals, rate);
        snapshot.schoolCapacity = SchoolCapacity(totals, rate);
        snapshot.nurseWant = (totals[(int)NaturalizationState.Registered] + totals[(int)NaturalizationState.Quarantine]) / c.quarantine_per_nurse;
        snapshot.teacherWant = (totals[(int)NaturalizationState.Quarantine] + totals[(int)NaturalizationState.Schooling]) / c.school_per_teacher;
        snapshot.soldierWant = TransmigrantSystem.SoldierDemand(kingdom);
        foreach (City city in cities)
        {
            snapshot.untrained += Untrained(city);
            float need = CadreNeed(city), have = Count(city, NaturalizationState.Cadre);
            snapshot.cadreWant += need;
            snapshot.workerWant += FactorySystem.Count(city) * c.workers_per_factory;
            if (need - have >= 1f) snapshot.gaps.Add((city, have, need));
        }
        snapshot.gaps.Sort((a, b) => (b.need - b.have).CompareTo(a.need - a.have));
        Reports.TryGetValue(kingdom.id, out snapshot.last);
        return snapshot;
    }

    #endregion
}

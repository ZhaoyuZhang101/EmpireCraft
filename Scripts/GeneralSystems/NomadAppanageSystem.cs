using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 诸子分帐(用户 2026-10-27 截图草原上的"领地"——喀尔喀、郭尔罗斯、阿史德、马尔哈拉……："这些游牧领地从来没有分裂过"
// "一点都不符合游牧传统")：汗死分兀鲁思只给游牧政体的汗国(帝国，见 EnfeoffmentHelper.UlusPartition)，独立的游牧国
// (兀鲁思自立、水土封建化后成了"领地")换君从不分家，存档里几国各换了二十来代君主一次没分过。现在：
//   · 不在帝国里的真游牧国(KingdomExtension.IsTrueNomad：游牧、汗国分封政体，本族游牧或水土游牧化)换了君主，
//     至少 MinCities 城、没在打仗、不是开国雄主新即位、本国 CooldownYears 年内没分过；
//   · 机会 BaseChance + PerCity × (折算城数 − MinCities)，至多 MaxChance；草原一城三五百个地块，折算城数 = 城数 + 地块数 / ZonesPerCity；
//   · 新汗的兄弟(先汗诸子：成年、不是君主、在本国)各领一帐，没有兄弟的由当地首领领一帐，至多 MaxSplits 帐：
//     离汗帐最远的非都城为帐，连成一片的本国城一并带走，每帐至多分到 1/(帐数+1)，汗帐至少留一半的城；
//     分出的帐独立、政体同汗帐；史书记一句。
public static class NomadAppanageSystem
{
    private const int MinCities = 2, MaxSplits = 3, ZonesPerCity = 200;
    private const float BaseChance = 0.3f, PerCity = 0.1f, MaxChance = 0.7f, CooldownYears = 15f;

    // 换了君主的国 → 新汗；setKing 补丁里不当场立国(原版可能正遍历国家列表)，下一帧由 Tick 分
    private static readonly Dictionary<long, long> Pending = new();
    private static object _world;

    private static bool Alive(Kingdom kingdom) => kingdom?.data != null && !kingdom.isRekt();

    // 不是帝国的国家换了君主(KingdomPatch)
    public static void OnChieftainSucceeded(Kingdom kingdom, Actor khan)
    {
        if (kingdom?.data != null && khan?.data != null) Pending[kingdom.id] = khan.id;
    }

    // 每帧(EmpireCraftStrategicScheduler)
    public static void Tick()
    {
        if (!ReferenceEquals(_world, World.world))
        {
            _world = World.world;
            Pending.Clear();
        }
        if (Pending.Count == 0 || World.world == null) return;
        var due = Pending.ToList();
        Pending.Clear();
        foreach ((long kingdomId, long khanId) in due)
        {
            Kingdom kingdom = World.world.kingdoms.get(kingdomId);
            Actor khan = World.world.units.get(khanId);
            try
            {
                if (khan == null || kingdom?.king != khan || !Due(kingdom, khan)) continue;
                Partition(kingdom, khan);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][诸子分帐] {kingdom?.GetKingdomName()} 结算失败: {exception.Message}");
            }
        }
    }

    private static bool Due(Kingdom kingdom, Actor khan)
    {
        if (!Alive(kingdom) || khan?.data == null || World.world == null || kingdom.IsInEmpire() || kingdom.capital == null ||
            !KingdomExtension.IsTrueNomad(kingdom) || Compatibility.AncientWarfareCompatibility.Owns(kingdom)) return false;
        int cities = kingdom.cities?.Count ?? 0;
        if (cities < MinCities || kingdom.getWars().Any(war => war != null && !war.hasEnded()) ||
            RulerTraitSystem.FounderShield(kingdom)) return false;
        double last = kingdom.GetOrCreate().nomad_split_at;
        if (last >= 0d && World.world.getCurWorldTime() >= last && Date.getYearsSince(last) < CooldownYears) return false;
        float scale = cities + kingdom.cities.Sum(city => city?.zones?.Count ?? 0) / (float)ZonesPerCity;
        return UnityEngine.Random.value < Mathf.Min(MaxChance, BaseChance + PerCity * (scale - MinCities));
    }

    private static void Partition(Kingdom kingdom, Actor khan)
    {
        int total = kingdom.cities.Count;
        int keep = (total + 1) / 2;
        Queue<Actor> brothers = new(Brothers(kingdom, khan));
        int splits = Mathf.Min(MaxSplits, Mathf.Max(1, brothers.Count), total - keep);
        if (splits <= 0) return;
        int share = Mathf.Max(1, total / (splits + 1));
        City seat = kingdom.capital;
        Vector2 home = seat.getTile()?.posV ?? Vector2.zero;
        RegimeType? regime = kingdom.GetRegime()?.type;
        var made = new List<Kingdom>();
        for (int i = 0; i < splits && kingdom.cities.Count > keep; i++)
        {
            City seed = kingdom.cities.Where(city => city?.data != null && !city.isRekt() && city != seat)
                .OrderByDescending(city => Vector2.Distance(home, city.getTile()?.posV ?? home)).FirstOrDefault();
            if (seed == null) break;
            int room = Mathf.Min(share, kingdom.cities.Count - keep);
            List<City> cluster = Cluster(kingdom, seed, seat, room);
            Actor brother = brothers.Count > 0 ? brothers.Dequeue() : null;
            Kingdom tent = brother != null ? Found(kingdom, cluster, brother)
                : AdministrativeControlSystem.ReleaseToLocal(kingdom, cluster, _ => "independent", out _);
            if (tent == null) break;
            if (regime.HasValue && tent.GetRegime()?.type != regime.Value)
            {
                tent.SetRegimeType(regime.Value);
                tent.LoadRegime();
            }
            EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(tent);
            made.Add(tent);
        }
        if (made.Count == 0) return;
        kingdom.GetOrCreate().nomad_split_at = World.world.getCurWorldTime();
        string text = string.Format(LM.Get("nomad_appanage_history"), kingdom.GetKingdomName(), khan.getName(),
            string.Join(LM.Get("schism_name_separator"), made.Select(tent => tent.GetKingdomName())));
        EventRecorder.Record(kingdom, text, khan);
        TranslateHelper.LogEventMessage(text, kingdom);
        LogService.LogInfo($"[EmpireCraft][诸子分帐] {kingdom.GetKingdomName()} 新汗 {khan.getName()} 即位，分出 " +
                           $"{string.Join("、", made.Select(tent => $"{tent.GetKingdomName()}({tent.cities?.Count ?? 0}城)"))}，汗帐剩 {kingdom.cities.Count}/{total} 城");
    }

    // 新汗的兄弟(先汗诸子)：成年、不是君主、在本国，年长的在前
    private static List<Actor> Brothers(Kingdom kingdom, Actor khan)
    {
        PersonalClanIdentity identity = khan.GetPersonalIdentity();
        if (identity == null) return new List<Actor>();
        return SpecificClanManager.GetSiblingsWithRelation(identity)
            .Select(item => item.Item2?._actor)
            .Where(actor => actor?.data != null && !actor.isRekt() && actor.isAlive() && !actor.isKing() && actor.kingdom == kingdom &&
                            !RealisticAgeSystem.IsMinor(actor) && actor.isUnitFitToRule())
            .Distinct().OrderByDescending(actor => actor.getAge()).ToList();
    }

    // 帐：seed 连同挨着的本国城，至多 room 座，不带汗帐的都城
    private static List<City> Cluster(Kingdom kingdom, City seed, City seat, int room)
    {
        var cluster = new List<City> { seed };
        var seen = new HashSet<City> { seed, seat };
        for (int i = 0; i < cluster.Count && cluster.Count < room; i++)
            foreach (City next in (IEnumerable<City>)cluster[i].neighbours_cities ?? Array.Empty<City>())
            {
                if (cluster.Count >= room) break;
                if (next?.data == null || next.isRekt() || !seen.Add(next) || next.kingdom != kingdom) continue;
                cluster.Add(next);
            }
        return cluster;
    }

    // 兄弟领帐：帐的头一座城由他自立为国，其余并入
    private static Kingdom Found(Kingdom kingdom, List<City> cluster, Actor brother)
    {
        City seed = cluster[0];
        Kingdom tent;
        try
        {
            if (brother.city != seed) brother.joinCity(seed);
            tent = seed.makeOwnKingdom(brother);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][诸子分帐] {seed.data.name} 立帐失败: {exception.Message}");
            return null;
        }
        if (!Alive(tent) || seed.kingdom != tent) return null;
        foreach (City city in cluster.Skip(1))
            if (city?.data != null && !city.isRekt() && city.kingdom == kingdom) city.joinAnotherKingdom(tent);
        return tent;
    }
}

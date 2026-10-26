using System;
using System.Linq;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 徭役、大兴土木与好大喜功(民怨的增长来源，用户 2026-10-26"好大喜功类君主、大兴土木、重赋税、重徭役")。
// 以前修路、建城只扣钱，封禅只给农民怨气 +3，没有征发民力的代价。现在政权每发起一项国家工程，记一笔"工役"：
//   · 修路(RoadBuildingSystem)：每月实际施工的地形单位 × 0.004(寻常路政，大国小国都只 +1~3)；
//   · 兴修水利(议题)：每座开渠的城 0.3；
//   · 官办移民建城(StateSettlementSystem，自发移民不算)：每座新城 1；
//   · 改建城池(决议，CityStyleSystem)：每座改建的城 0.4；
//   · 封禅(议题)：全境每城 0.15；
//   · 迁都(还于旧都也算，宫城随迁)：3。
// 后四项是大工程，另记"大兴土木"。工役存在政权上(帝国记在 ConstitutionalEconomyState，独立王国记在 KingdomExtraData)，
// 按经过的月数衰减：工役每年 ×0.7，大兴土木每年 ×0.4(只看近两三年扎堆)。近代国家多雇工，不再征发徭役，只按三成记。
// 苛政(HarshRuleSystem.Breakdown)按每城摊到的工役折算：
//   · 重徭役 harsh_corvee = 每城工役 × 25，最多 +20；
//   · 大兴土木 harsh_construction = (每城大工程 − 0.15) × 40，最多 +15(一两项单独的工程不算，扎堆才算)；
//   · 好大喜功 harsh_ambition：君主有野心勃勃(ambitious)特质，在位时 +5；他另多开一项修路工程，朝议更爱封禅、兴修水利。
// 战时征兵仍算"兵役"(harsh_war)，这里只算力役，不重复。农民(国人、野人)的怨气目标值按每城工役加，最多 +10(见 InstitutionSystem)。
public static class CorveeSystem
{
    public const string AmbitiousTrait = "ambitious";

    // 各项工程的工役
    public const float RoadPerTerrain = 0.004f;
    public const float IrrigationPerCity = 0.3f;
    public const float NewCity = 1f;
    public const float RestylePerCity = 0.4f;
    public const float FengshanPerCity = 0.15f;
    public const float CapitalMove = 3f;

    private const float CorveeDecay = 0.7f;
    private const float GrandDecay = 0.4f;
    private const float ModernShare = 0.3f;
    // 摊工役的城数至少按 4 座算(一两座城的小国不至于一修路就满额)
    private const int MinCities = 4;
    private const float CorveePerIntensity = 25f, CorveeCap = 20f;
    private const float GrandThreshold = 0.15f, GrandPerIntensity = 40f, GrandCap = 15f;
    private const float AmbitionHarsh = 5f;
    private const float PeasantPerIntensity = 12.5f, PeasantCap = 10f;

    // 记一笔工役(grand：大兴土木的大工程)；帝国成员记到帝国上
    public static void Add(Kingdom kingdom, float labor, bool grand)
    {
        if (labor <= 0f || kingdom?.data == null || kingdom.isRekt() || World.world == null ||
            AncientWarfareCompatibility.Owns(kingdom)) return;
        if (ModernStability.IsModern(kingdom)) labor *= ModernShare;
        Empire empire = kingdom.IsInEmpire() ? kingdom.GetEmpire() : null;
        if (empire?.data != null)
        {
            ConstitutionalEconomyState state = empire.data.constitutional_economy ??= new ConstitutionalEconomyState();
            Settle(ref state.corvee_load, ref state.grand_works, ref state.corvee_updated);
            state.corvee_load += labor;
            if (grand) state.grand_works += labor;
            return;
        }
        KingdomExtension.KingdomExtraData data = kingdom.GetOrCreate();
        if (data == null) return;
        Settle(ref data.corvee_load, ref data.grand_works, ref data.corvee_updated);
        data.corvee_load += labor;
        if (grand) data.grand_works += labor;
    }

    // 全境性的工程(封禅等)：按政权的城数记
    public static void AddPerCity(Kingdom kingdom, float perCity, bool grand) =>
        Add(kingdom, perCity * Mathf.Max(1, RealmCities(kingdom)), grand);

    // 苛政：重徭役
    public static float CorveeBurden(Kingdom core, int cities) =>
        Mathf.Min(CorveeCap, Intensity(core, cities).corvee * CorveePerIntensity);

    // 苛政：大兴土木
    public static float ConstructionBurden(Kingdom core, int cities) =>
        Mathf.Clamp((Intensity(core, cities).grand - GrandThreshold) * GrandPerIntensity, 0f, GrandCap);

    // 苛政：好大喜功之君在位
    public static float AmbitionBurden(Actor ruler) => Ambitious(ruler) ? AmbitionHarsh : 0f;

    public static bool Ambitious(Actor ruler) =>
        ruler?.data != null && !ruler.isRekt() && ruler.isAlive() && ruler.hasTrait(AmbitiousTrait);

    // 好大喜功之君多开一项修路工程(见 RoadBuildingSystem)
    public static int ExtraRoadProjects(Kingdom kingdom) => Ambitious(kingdom?.king) ? 1 : 0;

    // 农民(国人、野人)怨气目标值的徭役压力(InstitutionSystem.UpdateSocialUnrest 用)
    public static float PeasantPressure(Empire empire)
    {
        Kingdom core = empire?.CoreKingdom;
        if (core == null || core.isRekt()) return 0f;
        int cities = empire.kingdoms_list?.Where(kingdom => kingdom != null && !kingdom.isRekt())
            .Sum(kingdom => kingdom.cities?.Count ?? 0) ?? 0;
        return Mathf.Min(PeasantCap, Intensity(core, cities).corvee * PeasantPerIntensity);
    }

    // 每城摊到的工役、大工程(已按经过的时间衰减，只读不写)
    public static (float corvee, float grand) Intensity(Kingdom core, int cities)
    {
        if (core?.data == null || core.isRekt() || World.world == null) return (0f, 0f);
        float load, grand;
        double updated;
        Empire empire = core.IsInEmpire() ? core.GetEmpire() : null;
        if (empire?.data != null)
        {
            ConstitutionalEconomyState state = empire.data.constitutional_economy;
            if (state == null) return (0f, 0f);
            (load, grand, updated) = (state.corvee_load, state.grand_works, state.corvee_updated);
        }
        else
        {
            KingdomExtension.KingdomExtraData data = core.GetOrCreate();
            if (data == null) return (0f, 0f);
            (load, grand, updated) = (data.corvee_load, data.grand_works, data.corvee_updated);
        }
        if (load <= 0f && grand <= 0f) return (0f, 0f);
        int months = Months(updated);
        float share = 1f / Mathf.Max(MinCities, cities);
        return (Decay(load, CorveeDecay, months) * share, Decay(grand, GrandDecay, months) * share);
    }

    private static int RealmCities(Kingdom kingdom)
    {
        Empire empire = kingdom.IsInEmpire() ? kingdom.GetEmpire() : null;
        return empire?.kingdoms_list?.Where(member => member != null && !member.isRekt())
                   .Sum(member => member.cities?.Count ?? 0) ?? kingdom.cities?.Count ?? 0;
    }

    // 把衰减结算进存档；不满一个月的不动时间戳(修路每月记一笔，零头不至于丢太多)
    private static void Settle(ref float load, ref float grand, ref double updated)
    {
        double now = World.world.getCurWorldTime();
        if (updated < 0d || now < updated)
        {
            updated = now;
            return;
        }
        int months = Months(updated);
        if (months <= 0) return;
        load = Decay(load, CorveeDecay, months);
        grand = Decay(grand, GrandDecay, months);
        updated = now;
    }

    private static int Months(double since)
    {
        if (since < 0d || World.world == null || World.world.getCurWorldTime() < since) return 0;
        return Math.Max(0, Date.getMonthsSince(since));
    }

    private static float Decay(float value, float annual, int months)
    {
        if (value <= 0.001f) return 0f;
        return months <= 0 ? value : value * Mathf.Pow(annual, months / 12f);
    }
}

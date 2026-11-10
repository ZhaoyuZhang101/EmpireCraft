using System.Collections.Generic;
using EmpireCraft.Scripts.Data;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 时令(借鉴钢铁雄心的天气，用户 2026-11-02 "先做第一批")：打仗要看季节。按地形定，地形已经说明了气候，所以不分南北半球：
//   · 严冬(腊月到二月)：雪原、高山；雨季(五到七月)：丛林、沼泽、河网；酷暑(六到八月)：沙漠；
//   · 原版纪元：冰封、绝望纪元全年严冬，其余地形(沙漠除外)也苦寒；泪水纪元全年雨季；
//   · 效果：攻方战力打折、双方多掉组织度、每月少推进(见 Apply，接在交战的地形效果上)；雪原严冬行军减半(见 SlowsMarch)；
//     在外打仗的军冻馁、染疫、缺水减员(见 AttritionRate，接在后勤结算上；预备、溃退、船上的不算，防守的减半)；
//   · 惯了的不怕：游牧、阿拉伯不怕沙漠，游牧、斯拉夫不怕雪原，打折与减员减半；电气时代以后的军队减半(两者可叠)；
//   · AI 挑战线时避开正当严酷时令的地形(见 Harshness)。
public static class WarWeather
{
    public enum Kind { Clear, Frost, Rains, Heat, Bitter }

    private readonly struct Hardship
    {
        public readonly float Attack, ExtraOrg, Attrition;
        public readonly int AdvanceCut;
        public readonly bool Slow;

        public Hardship(float attack, float extraOrg, int advanceCut, float attrition, bool slow)
        {
            Attack = attack;
            ExtraOrg = extraOrg;
            AdvanceCut = advanceCut;
            Attrition = attrition;
            Slow = slow;
        }
    }

    private static readonly Hardship None = new(1f, 0f, 0, 0f, false);
    private static readonly Hardship SnowFrost = new(0.75f, 6f, 1, 0.02f, true);
    private static readonly Hardship MountainFrost = new(0.85f, 3f, 0, 0.01f, false);
    private static readonly Hardship BitterCold = new(0.9f, 2f, 0, 0.005f, false);
    private static readonly Hardship WetJungle = new(0.85f, 3f, 1, 0.015f, false);
    private static readonly Hardship HighWater = new(0.85f, 2f, 0, 0f, false);
    private static readonly Hardship DesertHeat = new(0.9f, 4f, 0, 0.015f, false);

    private const int ModernTier = 8;
    private const float Softened = 0.5f;

    private static bool FrostMonth(int month) => month is 12 or 1 or 2;
    private static bool RainMonth(int month) => month is 5 or 6 or 7;
    private static bool HeatMonth(int month) => month is 6 or 7 or 8;

    private static bool IceAge => World.world_era?.flag_winter == true;
    private static bool RainAge => World.world_era?.overlay_rain == true;

    public static Kind KindOf(FrontTerrain terrain)
    {
        if (World.world == null) return Kind.Clear;
        int month = Date.getCurrentMonth();
        bool ice = IceAge;
        switch (terrain)
        {
            case FrontTerrain.Snow:
            case FrontTerrain.Mountain:
            // 高原冬天同山地一样冻
            case FrontTerrain.Plateau:
                return ice || FrostMonth(month) ? Kind.Frost : Kind.Clear;
            case FrontTerrain.Jungle:
            case FrontTerrain.Swamp:
            case FrontTerrain.River:
                if (ice) return Kind.Bitter;
                return RainAge || RainMonth(month) ? Kind.Rains : Kind.Clear;
            case FrontTerrain.Desert:
                return !ice && HeatMonth(month) ? Kind.Heat : Kind.Clear;
            case FrontTerrain.Urban:
                return Kind.Clear;
            default:
                return ice ? Kind.Bitter : Kind.Clear;
        }
    }

    private static Hardship HardshipOf(FrontTerrain terrain) => KindOf(terrain) switch
    {
        Kind.Frost => terrain == FrontTerrain.Snow ? SnowFrost : MountainFrost,
        Kind.Rains => terrain == FrontTerrain.River ? HighWater : WetJungle,
        Kind.Heat => DesertHeat,
        Kind.Bitter => BitterCold,
        _ => None
    };

    // 打折与减员还剩几成：惯了这种地的减半，电气时代以后的军队减半
    private static float Exposure(Kingdom kingdom, FrontTerrain terrain)
    {
        if (kingdom?.data == null) return 1f;
        float exposure = 1f;
        string culture = CultureService.GetRealmCulture(kingdom);
        bool used = terrain switch
        {
            FrontTerrain.Desert => culture == "Arab" || AnimalHusbandrySystem.IsNomadic(kingdom),
            FrontTerrain.Snow => culture == "Slavonic" || AnimalHusbandrySystem.IsNomadic(kingdom),
            // 高原：青藏文化与游牧惯了(高寒高原的牧人)
            FrontTerrain.Plateau => culture == "Xizang" || AnimalHusbandrySystem.IsNomadic(kingdom),
            _ => false
        };
        if (used) exposure *= Softened;
        if (!string.IsNullOrEmpty(culture) && TechnologySystem.GetEraTier(culture) >= ModernTier) exposure *= Softened;
        return exposure;
    }

    // 交战地形效果叠上时令
    public static TerrainEffect Apply(TerrainEffect ground, FrontTerrain terrain, Kingdom attacker)
    {
        Hardship hardship = HardshipOf(terrain);
        if (hardship.Attack >= 1f) return ground;
        float exposure = Exposure(attacker, terrain);
        int advance = ground.MaxAdvance <= 0 ? 0 : Mathf.Max(1, ground.MaxAdvance - hardship.AdvanceCut);
        return new TerrainEffect(ground.Attack * (1f - (1f - hardship.Attack) * exposure), ground.AttackerOrg + hardship.ExtraOrg * exposure,
            ground.DefenderOrg + hardship.ExtraOrg * 0.5f, advance, ground.SlowMarch);
    }

    public static bool SlowsMarch(TileZone zone) => zone != null && HardshipOf(TerrainSystem.Of(zone)).Slow;

    // 这种地形眼下有多难打(0~1)：AI 挑战线时打折
    public static float Harshness(FrontTerrain terrain) => Mathf.Clamp01((1f - HardshipOf(terrain).Attack) * 4f);

    // 这支军这个月的非战斗减员比例
    public static float AttritionRate(Kingdom kingdom, CorpsData corps)
    {
        if (corps == null || corps.embarked || corps.task is not ("attack" or "defend" or "march")) return 0f;
        TileZone zone = CorpsSystem.Zone(corps);
        if (zone == null) return 0f;
        FrontTerrain terrain = TerrainSystem.Of(zone);
        float rate = HardshipOf(terrain).Attrition;
        if (rate <= 0f) return 0f;
        return rate * Exposure(kingdom, terrain) * (corps.task == "defend" ? 0.5f : 1f);
    }

    public static string Name(Kind kind) => kind == Kind.Clear ? null : LM.Get("war_weather_" + kind.ToString().ToLowerInvariant());

    public static string Name(FrontTerrain terrain) => Name(KindOf(terrain));

    // 地形名后面带上时令：雪原 · 严冬
    public static string Tag(FrontTerrain terrain)
    {
        string name = Name(terrain);
        return name == null ? TerrainSystem.Name(terrain) : $"{TerrainSystem.Name(terrain)} · {name}";
    }

    // 眼下各地的时令(军事页一行)；没有就返回 null
    public static string SeasonLine()
    {
        if (World.world == null) return null;
        var parts = new List<string>();
        foreach (FrontTerrain terrain in new[] { FrontTerrain.Snow, FrontTerrain.Jungle, FrontTerrain.Desert, FrontTerrain.Plains })
        {
            Kind kind = KindOf(terrain);
            if (kind == Kind.Clear) continue;
            string line = LM.Get("war_weather_line_" + kind.ToString().ToLowerInvariant());
            if (!parts.Contains(line)) parts.Add(line);
        }
        return parts.Count == 0 ? null : string.Join("；", parts);
    }
}

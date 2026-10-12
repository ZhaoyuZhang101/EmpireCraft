using System.Collections.Generic;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 战线地形(借鉴钢铁雄心)：每块地块按格子构成定一种地形(地形极少变化，一年重判一次)。
// 交战时看攻方要推进的那块地：攻方战力打折、双方额外掉组织度(补给难)、每月最多推进几块；
// 山地、沼泽、丛林行军慢一半。城中心那块地算城池。
// 原版最高的地形(山地、山巅是阻挡格，地面单位走不了)占到四分之三的地块不可通行：军团绕行，战线不从这里推进
public enum FrontTerrain { Plains, Forest, Jungle, Hills, Mountain, River, Swamp, Desert, Snow, Urban }

public readonly struct TerrainEffect
{
    public readonly float Attack, AttackerOrg, DefenderOrg;
    public readonly int MaxAdvance;
    public readonly bool SlowMarch;

    public TerrainEffect(float attack, float attackerOrg, float defenderOrg, int maxAdvance, bool slowMarch)
    {
        Attack = attack;
        AttackerOrg = attackerOrg;
        DefenderOrg = defenderOrg;
        MaxAdvance = maxAdvance;
        SlowMarch = slowMarch;
    }
}

public static class TerrainSystem
{
    // 攻方战力倍率、攻方/守方每月额外掉的组织度、每月最多推进几块、行军是否减半
    private static readonly Dictionary<FrontTerrain, TerrainEffect> Effects = new()
    {
        [FrontTerrain.Plains] = new TerrainEffect(1f, 0f, 0f, 4, false),
        [FrontTerrain.Forest] = new TerrainEffect(0.85f, 0f, 0f, 3, false),
        [FrontTerrain.Jungle] = new TerrainEffect(0.75f, 3f, 2f, 2, true),
        [FrontTerrain.Hills] = new TerrainEffect(0.8f, 0f, 0f, 3, false),
        [FrontTerrain.Mountain] = new TerrainEffect(0.6f, 4f, 0f, 2, true),
        [FrontTerrain.River] = new TerrainEffect(0.75f, 0f, 0f, 3, false),
        [FrontTerrain.Swamp] = new TerrainEffect(0.7f, 4f, 4f, 2, true),
        [FrontTerrain.Desert] = new TerrainEffect(0.95f, 5f, 5f, 4, false),
        [FrontTerrain.Snow] = new TerrainEffect(0.85f, 5f, 5f, 3, false),
        [FrontTerrain.Urban] = new TerrainEffect(0.7f, 0f, 0f, 2, false)
    };

    private static readonly Dictionary<int, (FrontTerrain terrain, bool impassable, double at)> Cache = new();

    public static void ResetWorldState() => Cache.Clear();

    public static TerrainEffect Effect(FrontTerrain terrain) =>
        Effects.TryGetValue(terrain, out TerrainEffect effect) ? effect : Effects[FrontTerrain.Plains];

    public static FrontTerrain Of(TileZone zone) => Lookup(zone).terrain;

    // 原版最高的地形：山地、山巅(阻挡格)占到四分之三的地块，军团绕行、战线不从这里推进
    public static bool Impassable(TileZone zone) => zone != null && Lookup(zone).impassable;

    private static (FrontTerrain terrain, bool impassable) Lookup(TileZone zone)
    {
        if (zone?.tiles == null || World.world == null) return (FrontTerrain.Plains, false);
        double now = World.world.getCurWorldTime();
        if (Cache.TryGetValue(zone.id, out var hit) && now >= hit.at && Date.getMonthsSince(hit.at) < 12)
            return (hit.terrain, hit.impassable);
        (FrontTerrain terrain, bool impassable) = Classify(zone);
        Cache[zone.id] = (terrain, impassable, now);
        return (terrain, impassable);
    }

    // 交战地形：城中心那块地是城池
    public static FrontTerrain OfBattle(City city, TileZone zone) =>
        zone != null && city?.getTile()?.zone == zone ? FrontTerrain.Urban : Of(zone);

    public static bool SlowsMarch(TileZone zone) => zone != null && Effect(Of(zone)).SlowMarch;

    public static string Name(FrontTerrain terrain) => LM.Get("terrain_" + terrain.ToString().ToLowerInvariant());

    // 地形效果的一句话说明
    public static string Describe(FrontTerrain terrain)
    {
        TerrainEffect effect = Effect(terrain);
        var parts = new List<string>();
        if (effect.Attack < 0.999f) parts.Add(string.Format(LM.Get("terrain_effect_attack"), effect.Attack.ToString("0.##")));
        if (effect.AttackerOrg > 0f && effect.AttackerOrg == effect.DefenderOrg)
            parts.Add(string.Format(LM.Get("terrain_effect_org_both"), effect.AttackerOrg));
        else
        {
            if (effect.AttackerOrg > 0f) parts.Add(string.Format(LM.Get("terrain_effect_org_attacker"), effect.AttackerOrg));
            if (effect.DefenderOrg > 0f) parts.Add(string.Format(LM.Get("terrain_effect_org_defender"), effect.DefenderOrg));
        }
        if (effect.MaxAdvance < 4) parts.Add(string.Format(LM.Get("terrain_effect_advance"), effect.MaxAdvance));
        if (effect.SlowMarch) parts.Add(LM.Get("terrain_effect_march"));
        return parts.Count == 0 ? LM.Get("terrain_effect_none") : string.Join(LM.Get("terrain_effect_separator"), parts);
    }

    // 按地块里 64 格的构成判定：山多为山地，山与丘陵多为丘陵，其次沼泽、水网(河湖)、丛林、森林、沙漠、雪原，都不够为平原。
    // 门槛按本地大地图实测：城市地块里平原约四成，丛林、雪原、水网、山地、丘陵各占一到两成(视地图而定)
    private static (FrontTerrain terrain, bool impassable) Classify(TileZone zone)
    {
        int total = 0, water = 0, mountain = 0, hills = 0, trees = 0, woods = 0, swamp = 0, jungle = 0, desert = 0, snow = 0,
            blocked = 0;
        foreach (WorldTile tile in zone.tiles)
        {
            TileTypeBase type = tile?.Type;
            if (type == null) continue;
            total++;
            if (type.liquid && !type.lava)
            {
                water++;
                continue;
            }
            TileTypeBase main = tile.main_type;
            string id = type.id ?? "";
            if (type.block || main != null && main.block) blocked++;
            if (type.mountains || type.summit || main != null && (main.mountains || main.summit)) mountain++;
            else if (id == "hills" || id == "snow_hills" || main?.id == "hills") hills++;
            switch (type.biome_id)
            {
                case "biome_swamp": swamp++; break;
                case "biome_jungle": jungle++; break;
                case "biome_birch":
                case "biome_maple": woods++; break;
                case "biome_desert":
                case "biome_sand": desert++; break;
            }
            if (type.biome_id == "biome_permafrost" || id.StartsWith("snow") || id.StartsWith("frozen")) snow++;
            BuildingAsset flora = tile.building?.asset;
            if (flora != null && flora.flora && flora.flora_type == FloraType.Tree) trees++;
        }
        if (total == 0) return (FrontTerrain.Plains, false);
        bool impassable = blocked >= total * 0.75f;
        float Share(int count) => count / (float)total;
        if (Share(mountain) >= 0.25f) return (FrontTerrain.Mountain, impassable);
        if (Share(mountain + hills) >= 0.3f) return (FrontTerrain.Hills, impassable);
        if (Share(swamp) >= 0.3f) return (FrontTerrain.Swamp, impassable);
        if (Share(water) >= 0.15f) return (FrontTerrain.River, impassable);
        if (Share(jungle) >= 0.3f) return (FrontTerrain.Jungle, impassable);
        // 城市周边的树大多被砍过，一块地里有四棵树(约 6%)就算林地；桦树林、枫树林等林地生物群系按面积算
        if (Share(trees) >= 0.06f || Share(woods) >= 0.3f) return (FrontTerrain.Forest, impassable);
        if (Share(desert) >= 0.4f) return (FrontTerrain.Desert, impassable);
        if (Share(snow) >= 0.4f) return (FrontTerrain.Snow, impassable);
        return (FrontTerrain.Plains, impassable);
    }
}

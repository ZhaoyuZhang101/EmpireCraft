using System;
using System.Collections.Generic;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 战线地形(借鉴钢铁雄心)：每块地块按格子构成定一种地形(格子改了当场重判，否则 4~6 年重判一次，见 Lookup)。
// 交战时看攻方要推进的那块地：攻方战力打折、双方额外掉组织度(补给难)、每月最多推进几块；
// 山地、沼泽、丛林行军慢一半。城中心那块地算城池。
// 原版最高的地形(山地、山巅是阻挡格，地面单位走不了)占到四分之三的地块不可通行：军团绕行，战线不从这里推进。
// 同一份缓存还记着地块里的陆地格数与旱地格数(水土用，见 ShuiTuSystem、DryCounts)：旱地按 LandKinds.Dry(荒漠、稀树草原、荒原)，
// 陆地不含水、岩浆——同毡帐村镇的口径(RuralSettlementSystem.YurtGround)；这里的"沙漠"地形连海滩沙地也算，水土不用它
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

    // 地块地形缓存(按地块编号)：以前一律一年过期，又和行政控制力的年度结算同步，到期那一刻一国的地块一起重判。
    // 现在格子改了(设地面格、顶层格，见 MapWallPatch)当场作废；没改的只为树木长、砍重判，4~6 年一次，按地块编号错开到期
    private struct Entry
    {
        public double At;
        public FrontTerrain Terrain;
        public bool Impassable, Known;
        // 陆地格数、旱地格数(一块地 64 格)；宜居格(按地面加权 × LivableScale)、可耕格
        public byte Land, Dry, Livable, Arable;
    }

    private const int RefreshMonths = 48, RefreshSpreadMonths = 24;
    private static Entry[] _cache = new Entry[0];

    public static void ResetWorldState()
    {
        _cache = new Entry[0];
        CityLivable.Clear();
        _marks = new int[0];
        _markTypes = new TileTypeBase[0];
    }

    // 地块里有格子改了：下次用到时重判
    public static void Invalidate(TileZone zone)
    {
        if (zone != null && zone.id >= 0 && zone.id < _cache.Length) _cache[zone.id].Known = false;
    }

    public static TerrainEffect Effect(FrontTerrain terrain) =>
        Effects.TryGetValue(terrain, out TerrainEffect effect) ? effect : Effects[FrontTerrain.Plains];

    public static FrontTerrain Of(TileZone zone) => Lookup(zone).Terrain;

    // 地形与能否通行一次取出(地形阻力两样都要，免得查两遍缓存)
    public static FrontTerrain Of(TileZone zone, out bool impassable)
    {
        Entry entry = Lookup(zone);
        impassable = zone != null && entry.Impassable;
        return entry.Terrain;
    }

    // 原版最高的地形：山地、山巅(阻挡格)占到四分之三的地块，军团绕行、战线不从这里推进
    public static bool Impassable(TileZone zone) => zone != null && Lookup(zone).Impassable;

    // 地块里的陆地格数与旱地格数(水土，见 ShuiTuSystem)
    public static (int land, int dry) DryCounts(TileZone zone)
    {
        if (zone == null) return (0, 0);
        Entry entry = Lookup(zone);
        return (entry.Land, entry.Dry);
    }

    // 旱地地块：陆地里一半以上是旱地
    public static bool IsDry(TileZone zone)
    {
        (int land, int dry) = DryCounts(zone);
        return land > 0 && dry * 2 > land;
    }

    // 宜居度(0~1，用户 2026-10-22 见荒漠帐比中原城人多："设置的地块大是因为他是沙漠和荒原，实际居住面积应该很小")：
    // 一块地 64 格按地面加权(见 LivableWeight)。民居上限按它折算(见 NoCommonersPatch.AfterRecalculateMaxHouses)
    public static float Livability(TileZone zone) =>
        zone == null ? 0f : Math.Min(1f, Lookup(zone).Livable / (64f * LivableScale));

    // 陆地格数与其中的可耕格数(城郊收成按可耕比例折算，见 FarmlandSystem)
    public static (int land, int arable) ArableCounts(TileZone zone)
    {
        if (zone == null) return (0, 0);
        Entry entry = Lookup(zone);
        return (entry.Land, entry.Arable);
    }

    private const float LivableScale = 3f;
    // 荒地格：一格宜居度低于这个(沙漠、荒原、山石、沙地、地狱)
    public const float BarrenWeight = 0.3f;

    // 一座城的地块宜居度合计(几块"整块草地")：盖房上限、民居选址都要用，按城缓存几秒(真实时间)
    private static readonly Dictionary<City, (float value, float at)> CityLivable = new();

    public static float CityLivability(City city)
    {
        if (city?.zones == null) return 0f;
        float now = UnityEngine.Time.realtimeSinceStartup;
        if (CityLivable.TryGetValue(city, out var hit) && now - hit.at < 5f && now >= hit.at) return hit.value;
        float sum = 0f;
        foreach (TileZone zone in city.zones) sum += Livability(zone);
        if (CityLivable.Count > 4096) CityLivable.Clear();
        CityLivable[city] = (sum, now);
        return sum;
    }

    // 一格地能住多少人(相对草地)：草地、可耕地 1，林地 0.8，丛林 0.6，稀树草原 0.5，沼泽 0.4，冻土 0.3，
    // 丘陵山石、沙地 0.2，荒原 0.2，沙漠 0.15，地狱 0.05，水、岩浆 0
    public static float LivableWeight(TileTypeBase type)
    {
        if (type == null) return 0f;
        int kinds = LandKinds.Of(type);
        if ((kinds & (LandKinds.Water | LandKinds.Lava)) != 0) return 0f;
        if ((kinds & LandKinds.Infernal) != 0) return 0.05f;
        if ((kinds & LandKinds.Rough) != 0) return 0.2f;
        switch (type.biome_id)
        {
            case "biome_desert": return 0.15f;
            case "biome_wasteland": return 0.2f;
            case "biome_savanna": return 0.5f;
            case "biome_permafrost": return 0.3f;
        }
        if ((kinds & LandKinds.Sand) != 0) return 0.2f;
        if ((kinds & LandKinds.Forest) != 0) return (kinds & LandKinds.Wet) != 0 ? 0.6f : 0.8f;
        if ((kinds & LandKinds.Wet) != 0) return 0.4f;
        if ((kinds & LandKinds.Woodland) != 0) return 0.8f;
        if ((kinds & (LandKinds.Grass | LandKinds.Arable)) != 0) return 1f;
        return 0.5f;
    }

    private static Entry Lookup(TileZone zone)
    {
        if (zone?.tiles == null || World.world == null) return new Entry { Terrain = FrontTerrain.Plains };
        int id = zone.id;
        if (id < 0) return Classify(zone);
        if (id >= _cache.Length)
            Array.Resize(ref _cache, Math.Max(id + 1, World.world.zone_calculator?.zones?.Count ?? 0));
        double now = World.world.getCurWorldTime();
        ref Entry hit = ref _cache[id];
        if (hit.Known && now >= hit.At && Date.getMonthsSince(hit.At) < RefreshMonths + id % RefreshSpreadMonths)
            return hit;
        Entry fresh = Classify(zone);
        fresh.At = now;
        fresh.Known = true;
        hit = fresh;
        return fresh;
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

    // 格子种类的地形标记(按 TileTypeBase.index_id：原版地面格与顶层格共用一套编号)。判地形每块地 64 格，
    // 以前逐格比 id、生物群系的字符串；现在每种格子第一次见到时算一次标记，逐格只取标记
    private const int MarkWater = 1, MarkBlock = 2, MarkMountain = 4, MarkHills = 8, MarkSnowHills = 16, MarkSwamp = 32,
        MarkJungle = 64, MarkWoods = 128, MarkDesert = 256, MarkSnow = 512;
    private static int[] _marks = new int[0];
    private static TileTypeBase[] _markTypes = new TileTypeBase[0];

    private static int Marks(TileTypeBase type)
    {
        int index = type.index_id;
        if (index < 0) return ComputeMarks(type);
        if (index >= _marks.Length)
        {
            int size = Math.Max(index + 1, 256);
            Array.Resize(ref _marks, size);
            Array.Resize(ref _markTypes, size);
        }
        // 编号撞上别的格子(模板格不分编号)就照算，不串用
        if (ReferenceEquals(_markTypes[index], type)) return _marks[index];
        int marks = ComputeMarks(type);
        _marks[index] = marks;
        _markTypes[index] = type;
        return marks;
    }

    private static int ComputeMarks(TileTypeBase type)
    {
        int marks = 0;
        if (type.liquid && !type.lava) marks |= MarkWater;
        if (type.block) marks |= MarkBlock;
        if (type.mountains || type.summit) marks |= MarkMountain;
        string id = type.id ?? "";
        if (id == "hills") marks |= MarkHills;
        else if (id == "snow_hills") marks |= MarkSnowHills;
        switch (type.biome_id)
        {
            case "biome_swamp": marks |= MarkSwamp; break;
            case "biome_jungle": marks |= MarkJungle; break;
            case "biome_birch":
            case "biome_maple": marks |= MarkWoods; break;
            case "biome_desert":
            case "biome_sand": marks |= MarkDesert; break;
        }
        if (type.biome_id == "biome_permafrost" || id.StartsWith("snow", StringComparison.Ordinal) ||
            id.StartsWith("frozen", StringComparison.Ordinal)) marks |= MarkSnow;
        return marks;
    }

    // 按地块里 64 格的构成判定：山多为山地，山与丘陵多为丘陵，其次沼泽、水网(河湖)、丛林、森林、沙漠、雪原，都不够为平原。
    // 门槛按本地大地图实测：城市地块里平原约四成，丛林、雪原、水网、山地、丘陵各占一到两成(视地图而定)
    private static Entry Classify(TileZone zone)
    {
        (FrontTerrain terrain, bool impassable, int land, int dry, int livable, int arable) = ClassifyTiles(zone);
        return new Entry
        {
            Terrain = terrain, Impassable = impassable,
            Land = (byte)Math.Min(byte.MaxValue, land), Dry = (byte)Math.Min(byte.MaxValue, dry),
            Livable = (byte)Math.Min(byte.MaxValue, livable), Arable = (byte)Math.Min(byte.MaxValue, arable)
        };
    }

    private static (FrontTerrain terrain, bool impassable, int land, int dry, int livable, int arable) ClassifyTiles(TileZone zone)
    {
        int total = 0, water = 0, mountain = 0, hills = 0, trees = 0, woods = 0, swamp = 0, jungle = 0, desert = 0, snow = 0,
            blocked = 0, land = 0, dry = 0, arable = 0;
        float livableSum = 0f;
        foreach (WorldTile tile in zone.tiles)
        {
            TileTypeBase type = tile?.Type;
            if (type == null) continue;
            total++;
            // 水土：陆地(不含水、岩浆)与其中的旱地
            int kinds = LandKinds.Of(type);
            if ((kinds & (LandKinds.Water | LandKinds.Lava)) == 0)
            {
                land++;
                if ((kinds & LandKinds.Dry) != 0) dry++;
                if ((kinds & LandKinds.Arable) != 0) arable++;
                livableSum += LivableWeight(type);
            }
            int marks = Marks(type);
            if ((marks & MarkWater) != 0)
            {
                water++;
                continue;
            }
            // 底下的地面格只看阻挡、山地，丘陵只认 hills
            TileTypeBase main = tile.main_type;
            int under = main != null ? Marks(main) : 0;
            if (((marks | under) & MarkBlock) != 0) blocked++;
            if (((marks | under) & MarkMountain) != 0) mountain++;
            else if ((marks & (MarkHills | MarkSnowHills)) != 0 || (under & MarkHills) != 0) hills++;
            if ((marks & MarkSwamp) != 0) swamp++;
            else if ((marks & MarkJungle) != 0) jungle++;
            else if ((marks & MarkWoods) != 0) woods++;
            else if ((marks & MarkDesert) != 0) desert++;
            if ((marks & MarkSnow) != 0) snow++;
            BuildingAsset flora = tile.building?.asset;
            if (flora != null && flora.flora && flora.flora_type == FloraType.Tree) trees++;
        }
        int livable = (int)Math.Round(livableSum * LivableScale);
        if (total == 0) return (FrontTerrain.Plains, false, land, dry, livable, arable);
        bool impassable = blocked >= total * 0.75f;
        float Share(int count) => count / (float)total;
        if (Share(mountain) >= 0.25f) return (FrontTerrain.Mountain, impassable, land, dry, livable, arable);
        if (Share(mountain + hills) >= 0.3f) return (FrontTerrain.Hills, impassable, land, dry, livable, arable);
        if (Share(swamp) >= 0.3f) return (FrontTerrain.Swamp, impassable, land, dry, livable, arable);
        if (Share(water) >= 0.15f) return (FrontTerrain.River, impassable, land, dry, livable, arable);
        if (Share(jungle) >= 0.3f) return (FrontTerrain.Jungle, impassable, land, dry, livable, arable);
        // 城市周边的树大多被砍过，一块地里有四棵树(约 6%)就算林地；桦树林、枫树林等林地生物群系按面积算
        if (Share(trees) >= 0.06f || Share(woods) >= 0.3f) return (FrontTerrain.Forest, impassable, land, dry, livable, arable);
        if (Share(desert) >= 0.4f) return (FrontTerrain.Desert, impassable, land, dry, livable, arable);
        if (Share(snow) >= 0.4f) return (FrontTerrain.Snow, impassable, land, dry, livable, arable);
        return (FrontTerrain.Plains, impassable, land, dry, livable, arable);
    }
}

namespace EmpireCraft.Scripts.GeneralSystems;

// 地形归类：村镇生计(LivelihoodSystem)、矿藏分布(MineralResourceSystem)、牧场(AnimalHusbandrySystem)共用一套，
// 按原版格子种类(地面格、顶层格)判定，每种格子第一次见到时算一次(按 TileTypeBase.index_id，地面格顶层格共用编号)。
//   · 水：液体(海、湖、冰面)。岩浆不算水、不算海岸，算地狱地貌；
//   · 山石：山地、山巅、丘陵(连同冰封丘陵)、岩地；
//   · 密林：桦树林、枫树林、魔法林、丛林；疏林：草地、冻土的高处(原版长橡树、松树)；
//   · 草地：草地、冻土的低处(苔原)，花甸、三叶草地；
//   · 旱地：荒漠、稀树草原、荒原；海滩沙地(连同雪滩)另记，算海岸，不放牧；
//   · 湿地：沼泽、丛林；地狱：地狱地貌与岩浆；
//   · 宜农：能耕的格子里除去山石、旱地、沙地。
// 纯净地图没有树，林地只能看生物群系。战线地形(TerrainSystem)另有一套，不走这里
public static class LandKinds
{
    public const int Water = 1, Lava = 2, Rough = 4, Forest = 8, Woodland = 16, Grass = 32, Dry = 64, Sand = 128, Wet = 256,
        Infernal = 512, Arable = 1024;

    private static int[] _kinds = new int[0];
    private static TileTypeBase[] _types = new TileTypeBase[0];

    public static int Of(TileTypeBase type)
    {
        if (type == null) return 0;
        int index = type.index_id;
        if (index < 0) return Compute(type);
        if (index >= _kinds.Length)
        {
            int size = global::System.Math.Max(index + 1, 256);
            global::System.Array.Resize(ref _kinds, size);
            global::System.Array.Resize(ref _types, size);
        }
        // 编号撞上别的格子(模板格不分编号)就照算，不串用
        if (ReferenceEquals(_types[index], type)) return _kinds[index];
        int kinds = Compute(type);
        _kinds[index] = kinds;
        _types[index] = type;
        return kinds;
    }

    public static bool Is(TileTypeBase type, int kind) => (Of(type) & kind) != 0;

    private static int Compute(TileTypeBase type)
    {
        if (type.lava) return Lava | Infernal;
        if (type.liquid) return Water;
        string id = type.id ?? "";
        string biome = type.biome_id ?? "";
        int kinds = 0;
        if (type.rocks || type.mountains || type.summit || id is "hills" or "snow_hills" ||
            biome is "biome_hill" or "biome_rocklands")
            kinds |= Rough;
        bool high = type.biome_asset != null && id == type.biome_asset.tile_high;
        switch (biome)
        {
            case "biome_birch":
            case "biome_maple":
            case "biome_enchanted":
                kinds |= Forest;
                break;
            case "biome_jungle":
                kinds |= Forest | Wet;
                break;
            case "biome_swamp":
                kinds |= Wet;
                break;
            case "biome_grass":
            case "biome_permafrost":
                if (id == "snow_sand") kinds |= Sand;
                else if ((kinds & Rough) == 0) kinds |= high ? Woodland : Grass;
                break;
            case "biome_flower":
            case "biome_clover":
                kinds |= Grass;
                break;
            case "biome_desert":
            case "biome_savanna":
            case "biome_wasteland":
                kinds |= Dry;
                break;
            case "biome_sand":
                kinds |= Sand;
                break;
            case "biome_infernal":
                kinds |= Infernal;
                break;
        }
        if ((type.can_be_farm || type.farm_field) && (kinds & (Rough | Dry | Sand)) == 0) kinds |= Arable;
        return kinds;
    }
}

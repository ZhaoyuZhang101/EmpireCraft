using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 城池全图的规划编辑(用户 2026-10-28 要一个全屏的"城池"界面，"玩家可以随意改城的画法"；界面见 UI/Windows/CityPlanScreen)：
// 城区地块并入/划出、宫城挪位、城墙/护城河开关、中轴与四门挪位、坊市疏密、换个样子(换布局种子)、撤销、交还规矩。
//   · 改过的记作"手动规划"，存在城里(CityExtraData.plan_*)，城市以后长大、改建不覆盖，直到交还规矩；
//   · 只改平面图的画法(怎么套用见 PureMapPatch.ApplyManual)，城市实际的领地、原版建筑都不动；城区只能画在本城自己的地上；
//   · 每次改动前记一份快照，每城至多 MaxUndo 步(只在内存里，不进存档)；改完本城平面图重算重画，Revision 加一(界面据此重画预览)。
// 返回的提示文本都已本地化；拒绝时也返回提示(什么都没改)，改没改看 Revision
public static class CityPlanEditing
{
    private const int MaxUndo = 30;
    // 预览：本城地块外再放宽几格；最大边长(格)，再大就以城心为中截一块
    private const int PreviewMargin = 4, MaxPreviewTiles = 512;
    // 预览：本城领地外压暗多少；领地边界虚线的颜色、不透明度、一段多长(像素)
    private const float OutsideDim = 0.86f, BorderAlpha = 0.85f;
    private const int DashLength = 3;
    private static readonly Color32 BorderRed = new(222, 58, 46, 255), NeutralTint = new(160, 160, 160, 255);

    // 每次改动加一
    public static int Revision { get; private set; }
    // 最近一次预览是不是截过(城太大，只画城心附近)，截过时附一句本地化的说明，没截为 null
    public static bool PreviewCropped { get; private set; }
    public static string PreviewNote { get; private set; }

    #region 撤销

    private sealed class Snapshot
    {
        private List<int> _in, _out;
        private int _palace, _walls, _moat, _axis, _axisX, _axisY, _blocks;
        private long _seed;

        public static Snapshot Of(CityExtension.CityExtraData d) => new()
        {
            _in = d.plan_zones_in != null ? new List<int>(d.plan_zones_in) : null,
            _out = d.plan_zones_out != null ? new List<int>(d.plan_zones_out) : null,
            _palace = d.plan_palace_zone, _walls = d.plan_walls, _moat = d.plan_moat, _axis = d.plan_axis,
            _axisX = d.plan_axis_x, _axisY = d.plan_axis_y, _blocks = d.plan_blocks, _seed = d.plan_seed_offset
        };

        public void Restore(CityExtension.CityExtraData d)
        {
            d.plan_zones_in = _in != null ? new List<int>(_in) : null;
            d.plan_zones_out = _out != null ? new List<int>(_out) : null;
            d.plan_palace_zone = _palace;
            d.plan_walls = _walls;
            d.plan_moat = _moat;
            d.plan_axis = _axis;
            d.plan_axis_x = _axisX;
            d.plan_axis_y = _axisY;
            d.plan_blocks = _blocks;
            d.plan_seed_offset = _seed;
        }
    }

    // 各城的撤销步(记着是哪个城对象：读档换了世界，同号的城不认)
    private static readonly Dictionary<long, (City city, List<Snapshot> steps)> UndoSteps = new();

    // 一笔(界面里按住拖着刷地块，见 BeginStroke)：整笔只记一步撤销
    private static City _stroke;
    private static bool _strokePushed;

    // 按住拖着刷地块：BeginStroke 到 EndStroke 之间本城的改动合成一步撤销(不然刷一笔就把 MaxUndo 步用完)
    public static void BeginStroke(City city)
    {
        _stroke = city;
        _strokePushed = false;
    }

    public static void EndStroke()
    {
        _stroke = null;
        _strokePushed = false;
    }

    private static void Push(City city, CityExtension.CityExtraData extra)
    {
        if (_stroke != null && ReferenceEquals(_stroke, city))
        {
            if (_strokePushed) return;
            _strokePushed = true;
        }
        long id = city.data.id;
        if (!UndoSteps.TryGetValue(id, out var entry) || !ReferenceEquals(entry.city, city))
        {
            if (UndoSteps.Count > 64)
                foreach (long stale in new List<long>(UndoSteps.Keys))
                    if (UndoSteps[stale].city?.data == null || UndoSteps[stale].city.isRekt()) UndoSteps.Remove(stale);
            entry = (city, new List<Snapshot>());
            UndoSteps[id] = entry;
        }
        entry.steps.Add(Snapshot.Of(extra));
        if (entry.steps.Count > MaxUndo) entry.steps.RemoveAt(0);
    }

    public static bool Undo(City city)
    {
        if (city?.data == null || !UndoSteps.TryGetValue(city.data.id, out var entry) || !ReferenceEquals(entry.city, city) ||
            entry.steps.Count == 0)
            return false;
        Snapshot last = entry.steps[entry.steps.Count - 1];
        entry.steps.RemoveAt(entry.steps.Count - 1);
        last.Restore(city.GetOrCreate());
        Changed(city);
        return true;
    }

    #endregion

    #region 状态

    // 有没有手动改过(见 PureMapPatch.ApplyManual)
    internal static bool HasOverrides(CityExtension.CityExtraData extra) =>
        extra != null && (extra.plan_zones_in?.Count > 0 || extra.plan_zones_out?.Count > 0 || extra.plan_palace_zone >= 0 ||
                          extra.plan_walls >= 0 || extra.plan_moat >= 0 || extra.plan_axis >= 0 ||
                          extra.plan_axis_x != int.MinValue || extra.plan_blocks != 0 || extra.plan_seed_offset != 0L);

    private static bool Alive(City city) => city?.data != null && !city.isRekt() && city.zones != null && city.zones.Count > 0;

    public static bool Editable(City city, out string reason)
    {
        if (!Alive(city))
        {
            reason = LM.Get("city_plan_city_gone");
            return false;
        }
        // 平面图只在纯净地图下按轮重算、画出来；没开时不现算(重算会顺带添村镇、出钱，见 UrbanFabric.PlaceCountryside)
        if (!PureMapMode.Enabled)
        {
            reason = LM.Get("city_plan_need_pure_map");
            return false;
        }
        reason = null;
        return true;
    }

    public static bool IsManual(City city) => city?.data != null && HasOverrides(city.GetOrCreate());

    // 眼下的平面图要点(纯净地图没开时不现算，为 null)
    private static PureMapPatch.PlanFacts Facts(City city) => Alive(city) && PureMapMode.Enabled ? PureMapPatch.FactsOf(city) : null;

    public static string Summary(City city)
    {
        if (!Alive(city)) return "";
        CityExtension.CityExtraData extra = city.GetOrCreate();
        if (!HasOverrides(extra)) return LM.Get("city_plan_summary_auto");
        var parts = new List<string>();
        PureMapPatch.PlanFacts facts = Facts(city);
        if (facts != null)
        {
            parts.Add(string.Format(LM.Get("city_plan_part_zones"), facts.TownZones));
            if (facts.Walled) parts.Add(LM.Get("city_plan_part_walls"));
            if (facts.Moat) parts.Add(LM.Get("city_plan_part_moat"));
            if (facts.Axis) parts.Add(LM.Get("city_plan_part_axis"));
            if (facts.PalaceZone >= 0) parts.Add(LM.Get("city_plan_part_palace"));
        }
        else
        {
            // 纯净地图没开：只照记下的改动说
            if (extra.plan_walls == 1) parts.Add(LM.Get("city_plan_part_walls"));
            if (extra.plan_moat == 1) parts.Add(LM.Get("city_plan_part_moat"));
            if (extra.plan_axis == 1) parts.Add(LM.Get("city_plan_part_axis"));
            if (extra.plan_palace_zone >= 0) parts.Add(LM.Get("city_plan_part_palace"));
        }
        if (extra.plan_blocks == 1) parts.Add(LM.Get("city_plan_part_dense"));
        else if (extra.plan_blocks == 2) parts.Add(LM.Get("city_plan_part_sparse"));
        return string.Format(LM.Get("city_plan_summary_manual"), string.Join(LM.Get("city_plan_part_separator"), parts));
    }

    // 这块地能不能并入/划出：本城的地、不是城心
    public static bool ZoneEditable(City city, TileZone zone) =>
        Alive(city) && PureMapMode.Enabled && zone != null && zone.city == city && zone != city.getTile()?.zone;

    // 这块地眼下画成城区
    public static bool InTown(City city, TileZone zone) => Alive(city) && PureMapMode.Enabled && PureMapPatch.ZoneInTown(city, zone);

    // 平面图的指纹(见 PureMapPatch.Stamp)：城市自己长大、改建，平面图按轮重算变了样也会变，界面可据此重画预览(没有为 0)
    public static int PlanKey(City city) => Alive(city) && PureMapMode.Enabled ? PureMapPatch.StampOf(city) : 0;

    // axisX、axisY：城门中线的交点(中轴开着时就是中轴交点；没有为 int.MinValue)；palaceZone：宫城地块(-1 没有)；
    // blocks：坊市疏密(0 照规矩，1 密，2 疏)
    public static (bool walls, bool moat, bool axis, int axisX, int axisY, int palaceZone, int blocks) State(City city)
    {
        int blocks = city?.data != null ? city.GetOrCreate().plan_blocks : 0;
        PureMapPatch.PlanFacts facts = Facts(city);
        if (facts == null) return (false, false, false, int.MinValue, int.MinValue, -1, blocks);
        return (facts.Walled, facts.Moat, facts.Axis, facts.GateX, facts.GateY, facts.PalaceZone, blocks);
    }

    #endregion

    #region 改动

    // 改完：本城平面图丢掉重算(城市平面图层、精细层、标注、夜景灯光都按平面图指纹跟着重画，见 PureMapPatch.Redraw、Stamp)
    private static void Changed(City city)
    {
        Revision++;
        PureMapPatch.Redraw(city);
    }

    private static string NotOwn() => LM.Get("city_plan_zone_not_own");

    public static string ToggleZone(City city, TileZone zone)
    {
        if (!Editable(city, out string reason)) return reason;
        if (zone == null || zone.city != city) return NotOwn();
        if (zone == city.getTile()?.zone) return LM.Get("city_plan_zone_center");
        CityExtension.CityExtraData extra = city.GetOrCreate();
        if (PureMapPatch.ZoneInTown(city, zone))
        {
            if (PureMapPatch.LandmarkZone(city, zone)) return LM.Get("city_plan_zone_palace");
            // 手动挪过的中轴交点在这块地上：先挪中轴
            if (extra.plan_axis_x != int.MinValue && Facts(city) is PureMapPatch.PlanFacts facts && facts.GateX == extra.plan_axis_x &&
                facts.GateY == extra.plan_axis_y && World.world.GetTile(facts.GateX, facts.GateY)?.zone == zone)
                return LM.Get("city_plan_zone_axis");
            Push(city, extra);
            extra.plan_zones_in?.Remove(zone.id);
            extra.plan_zones_out ??= new List<int>();
            if (!extra.plan_zones_out.Contains(zone.id)) extra.plan_zones_out.Add(zone.id);
            Changed(city);
            return LM.Get("city_plan_zone_out");
        }
        Push(city, extra);
        // 划出过的先撤掉划出(本来就画成城区的照原样画回去)，还不是城区再整块并入
        if (extra.plan_zones_out?.Remove(zone.id) == true)
        {
            PureMapPatch.Redraw(city);
            if (PureMapPatch.ZoneInTown(city, zone))
            {
                Changed(city);
                return LM.Get("city_plan_zone_in");
            }
        }
        extra.plan_zones_in ??= new List<int>();
        if (!extra.plan_zones_in.Contains(zone.id)) extra.plan_zones_in.Add(zone.id);
        Changed(city);
        return LM.Get("city_plan_zone_in");
    }

    // 宫城挪到这块地(并入城区；不是都城也照画)；zone 为 null：宫城交还规矩
    public static string MovePalace(City city, TileZone zone)
    {
        if (!Editable(city, out string reason)) return reason;
        CityExtension.CityExtraData extra = city.GetOrCreate();
        if (zone == null)
        {
            if (extra.plan_palace_zone < 0) return null;
            Push(city, extra);
            extra.plan_palace_zone = -1;
            Changed(city);
            return LM.Get("city_plan_palace_auto");
        }
        if (zone.city != city) return NotOwn();
        PureMapPatch.PlanFacts facts = Facts(city);
        if (facts == null) return LM.Get("city_plan_city_gone");
        if (facts.Tribal) return LM.Get("city_plan_tribal");
        if (facts.PalaceZone == zone.id) return LM.Get("city_plan_palace_same");
        Push(city, extra);
        extra.plan_palace_zone = zone.id;
        extra.plan_zones_out?.Remove(zone.id);
        Changed(city);
        return LM.Get("city_plan_palace_moved");
    }

    public static string ToggleWalls(City city)
    {
        if (!Editable(city, out string reason)) return reason;
        PureMapPatch.PlanFacts facts = Facts(city);
        if (facts == null) return LM.Get("city_plan_city_gone");
        if (facts.Tribal) return LM.Get("city_plan_tribal");
        CityExtension.CityExtraData extra = city.GetOrCreate();
        if (facts.Walled)
        {
            Push(city, extra);
            extra.plan_walls = 0;
            Changed(city);
            return LM.Get("city_plan_walls_off");
        }
        if (!facts.CanWall) return string.Format(LM.Get("city_plan_walls_none"), CityStyleSystem.StyleName(facts.Family));
        Push(city, extra);
        extra.plan_walls = 1;
        Changed(city);
        return LM.Get(facts.WallsDown ? "city_plan_walls_ring" : "city_plan_walls_on");
    }

    public static string ToggleMoat(City city)
    {
        if (!Editable(city, out string reason)) return reason;
        PureMapPatch.PlanFacts facts = Facts(city);
        if (facts == null) return LM.Get("city_plan_city_gone");
        if (facts.Tribal) return LM.Get("city_plan_tribal");
        CityExtension.CityExtraData extra = city.GetOrCreate();
        if (facts.Moat)
        {
            Push(city, extra);
            extra.plan_moat = 0;
            Changed(city);
            return LM.Get("city_plan_moat_off");
        }
        if (!facts.Walled) return LM.Get("city_plan_moat_need_walls");
        Push(city, extra);
        extra.plan_moat = 1;
        Changed(city);
        return LM.Get("city_plan_moat_on");
    }

    public static string ToggleAxis(City city)
    {
        if (!Editable(city, out string reason)) return reason;
        PureMapPatch.PlanFacts facts = Facts(city);
        if (facts == null) return LM.Get("city_plan_city_gone");
        if (facts.Tribal) return LM.Get("city_plan_tribal");
        if (facts.Camp) return LM.Get("city_plan_axis_camp");
        if (facts.GateX == int.MinValue) return LM.Get("city_plan_axis_outside");
        CityExtension.CityExtraData extra = city.GetOrCreate();
        Push(city, extra);
        extra.plan_axis = facts.Axis ? 0 : 1;
        Changed(city);
        return LM.Get(facts.Axis ? "city_plan_axis_off" : "city_plan_axis_on");
    }

    // 中轴交点挪到这一格(并打开中轴)，四门跟着挪：要落在本城画成城区的格上
    public static string MoveAxis(City city, int tileX, int tileY)
    {
        if (!Editable(city, out string reason)) return reason;
        PureMapPatch.PlanFacts facts = Facts(city);
        if (facts == null) return LM.Get("city_plan_city_gone");
        if (facts.Tribal) return LM.Get("city_plan_tribal");
        if (facts.Camp) return LM.Get("city_plan_axis_camp");
        WorldTile tile = World.world.GetTile(tileX, tileY);
        if (tile?.zone == null || tile.zone.city != city) return NotOwn();
        if (!PureMapPatch.TileInTown(city, tile)) return LM.Get("city_plan_axis_outside");
        CityExtension.CityExtraData extra = city.GetOrCreate();
        if (extra.plan_axis == 1 && extra.plan_axis_x == tileX && extra.plan_axis_y == tileY) return null;
        Push(city, extra);
        extra.plan_axis_x = tileX;
        extra.plan_axis_y = tileY;
        extra.plan_axis = 1;
        Changed(city);
        return LM.Get("city_plan_axis_moved");
    }

    // 坊市疏密轮换：照规矩 → 密 → 疏 → 照规矩(只在近处的精细图里看得出，见 PureMapPatch.Detail 的 Subdivide、StreetLine)
    public static string CycleBlocks(City city)
    {
        if (!Editable(city, out string reason)) return reason;
        PureMapPatch.PlanFacts facts = Facts(city);
        if (facts == null) return LM.Get("city_plan_city_gone");
        if (facts.Tribal || facts.Camp) return LM.Get("city_plan_blocks_none");
        CityExtension.CityExtraData extra = city.GetOrCreate();
        Push(city, extra);
        extra.plan_blocks = extra.plan_blocks is 1 ? 2 : extra.plan_blocks is 2 ? 0 : 1;
        Changed(city);
        return LM.Get(extra.plan_blocks switch { 1 => "city_plan_blocks_dense", 2 => "city_plan_blocks_sparse", _ => "city_plan_blocks_auto" });
    }

    // 换个样子：布局种子错开一份(城的轮廓、干道、街巷、宅基地、城外新添村镇的落点都随它；已有的村镇不挪，见 UrbanFabric.SeedOf)
    public static string Reroll(City city)
    {
        if (!Editable(city, out string reason)) return reason;
        CityExtension.CityExtraData extra = city.GetOrCreate();
        Push(city, extra);
        extra.plan_seed_offset++;
        Changed(city);
        return string.Format(LM.Get("city_plan_reroll"), extra.plan_seed_offset + 1);
    }

    // 交还规矩：清掉全部改动(可以撤销)
    public static void Reset(City city)
    {
        if (city?.data == null || city.isRekt()) return;
        CityExtension.CityExtraData extra = city.GetOrCreate();
        if (!HasOverrides(extra)) return;
        Push(city, extra);
        extra.plan_zones_in = null;
        extra.plan_zones_out = null;
        extra.plan_palace_zone = -1;
        extra.plan_walls = extra.plan_moat = extra.plan_axis = -1;
        extra.plan_axis_x = extra.plan_axis_y = int.MinValue;
        extra.plan_blocks = 0;
        extra.plan_seed_offset = 0L;
        Changed(city);
    }

    #endregion

    #region 预览

    // 整座城的预览：本城全部地块外加一圈 PreviewMargin 格，连同城外村镇田路的精细图(同地图上近看的那张，见 PureMapPatch.BuildDetail)。
    //   · 地形打底：每格原版地形的本色(见 PureMapPatch.TerrainColor)铺满，再把精细图按不透明度盖上去；国色、国界不画；
    //   · 精细图的底色(地图上是国色)取本城陆地地形的平均色：城郊过渡带、田块淡回去的是地形色，不在城外套一圈国色；
    //   · 本城领地以外稍压暗，领地外沿画一圈红色虚线；
    //   · 宽或高超过 MaxPreviewTiles 格的城以城心为中截一块(PreviewCropped、PreviewNote)。
    // x0、y0：纹理左下角的世界格；纹理第 0 行是最下面一格；每格 scale×scale 像素。调用方负责 Destroy 旧纹理。
    // 一次的工作量约是地图上生成一张精细图(几十毫秒，大城上百毫秒)加铺地形(每格十六像素)
    public static Texture2D RenderPreview(City city, out int x0, out int y0, out int widthTiles, out int heightTiles, out int scale)
    {
        x0 = y0 = widthTiles = heightTiles = 0;
        scale = PureMapPatch.DetailScale;
        PreviewCropped = false;
        PreviewNote = null;
        if (!Editable(city, out _)) return null;
        int size = Config.CITY_ZONE_SIZE;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (TileZone zone in city.zones)
        {
            if (zone == null) continue;
            minX = Mathf.Min(minX, zone.x * size);
            minY = Mathf.Min(minY, zone.y * size);
            maxX = Mathf.Max(maxX, zone.x * size + size - 1);
            maxY = Mathf.Max(maxY, zone.y * size + size - 1);
        }
        if (minX == int.MaxValue) return null;
        minX -= PreviewMargin;
        minY -= PreviewMargin;
        maxX += PreviewMargin;
        maxY += PreviewMargin;
        // 太大：以城心为中截一块
        WorldTile center = city.getTile();
        int cx = center?.x ?? (minX + maxX) / 2, cy = center?.y ?? (minY + maxY) / 2;
        bool cropped = false;
        if (maxX - minX + 1 > MaxPreviewTiles)
        {
            minX = Mathf.Clamp(cx - MaxPreviewTiles / 2, minX, maxX - MaxPreviewTiles + 1);
            maxX = minX + MaxPreviewTiles - 1;
            cropped = true;
        }
        if (maxY - minY + 1 > MaxPreviewTiles)
        {
            minY = Mathf.Clamp(cy - MaxPreviewTiles / 2, minY, maxY - MaxPreviewTiles + 1);
            maxY = minY + MaxPreviewTiles - 1;
            cropped = true;
        }
        minX = Mathf.Max(0, minX);
        minY = Mathf.Max(0, minY);
        maxX = Mathf.Min(MapBox.width - 1, maxX);
        maxY = Mathf.Min(MapBox.height - 1, maxY);
        if (maxX < minX || maxY < minY) return null;
        int w = maxX - minX + 1, h = maxY - minY + 1, pw = w * scale, ph = h * scale;
        var pixels = new Color32[pw * ph];
        var own = new bool[w * h];
        // ① 地形打底；顺带记下本城陆地的平均地形色
        long sumR = 0, sumG = 0, sumB = 0, land = 0;
        for (int ty = 0; ty < h; ty++)
        for (int tx = 0; tx < w; tx++)
        {
            WorldTile tile = World.world.GetTile(minX + tx, minY + ty);
            Color32 color = PureMapPatch.TerrainColor(tile);
            bool mine = tile?.zone != null && tile.zone.city == city;
            own[ty * w + tx] = mine;
            if (mine && tile.Type != null && !tile.Type.liquid)
            {
                sumR += color.r;
                sumG += color.g;
                sumB += color.b;
                land++;
            }
            for (int sy = 0; sy < scale; sy++)
            {
                int row = (ty * scale + sy) * pw + tx * scale;
                for (int sx = 0; sx < scale; sx++) pixels[row + sx] = color;
            }
        }
        Color32 tint = land > 0 ? new Color32((byte)(sumR / land), (byte)(sumG / land), (byte)(sumB / land), 255) : NeutralTint;
        // ② 精细图按不透明度盖上去(精细图自己的范围按地图上的算法，只放宽截取的上限，与地图上近看的一样)
        PureMapPatch.DetailImage image = PureMapPatch.BuildDetail(city, tint, MaxPreviewTiles);
        if (image?.Pixels != null)
        {
            int iw = image.W * scale, ih = image.H * scale;
            int ox = (image.X0 - minX) * scale, oy = (image.Y0 - minY) * scale;
            for (int iy = 0; iy < ih; iy++)
            {
                int py = oy + iy;
                if (py < 0 || py >= ph) continue;
                for (int ix = 0; ix < iw; ix++)
                {
                    int px = ox + ix;
                    if (px < 0 || px >= pw) continue;
                    Color32 over = image.Pixels[iy * iw + ix];
                    if (over.a == 0) continue;
                    int i = py * pw + px;
                    if (over.a == 255)
                    {
                        pixels[i] = over;
                        continue;
                    }
                    Color32 under = pixels[i];
                    float t = over.a / 255f;
                    pixels[i] = new Color32((byte)(under.r + (over.r - under.r) * t), (byte)(under.g + (over.g - under.g) * t),
                        (byte)(under.b + (over.b - under.b) * t), 255);
                }
            }
        }
        // ③ 本城领地以外稍压暗；领地外沿(四邻不归本城的那一边)画红色虚线
        for (int ty = 0; ty < h; ty++)
        for (int tx = 0; tx < w; tx++)
        {
            if (own[ty * w + tx])
            {
                bool up = ty + 1 >= h || !own[(ty + 1) * w + tx], down = ty == 0 || !own[(ty - 1) * w + tx];
                bool left = tx == 0 || !own[ty * w + tx - 1], right = tx + 1 >= w || !own[ty * w + tx + 1];
                if (!up && !down && !left && !right) continue;
                for (int k = 0; k < scale; k++)
                {
                    int px = tx * scale + k, py = ty * scale + k;
                    if (up) Dash(pixels, pw, px, ty * scale + scale - 1, px);
                    if (down) Dash(pixels, pw, px, ty * scale, px);
                    if (left) Dash(pixels, pw, tx * scale, py, py);
                    if (right) Dash(pixels, pw, tx * scale + scale - 1, py, py);
                }
                continue;
            }
            for (int sy = 0; sy < scale; sy++)
            {
                int row = (ty * scale + sy) * pw + tx * scale;
                for (int sx = 0; sx < scale; sx++)
                {
                    Color32 c = pixels[row + sx];
                    pixels[row + sx] = new Color32((byte)(c.r * OutsideDim), (byte)(c.g * OutsideDim), (byte)(c.b * OutsideDim), 255);
                }
            }
        }
        var texture = new Texture2D(pw, ph, TextureFormat.RGBA32, false)
        {
            name = "EmpireCraftCityPlanPreview", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
        };
        texture.SetPixels32(pixels);
        texture.Apply(false);
        x0 = minX;
        y0 = minY;
        widthTiles = w;
        heightTiles = h;
        PreviewCropped = cropped;
        if (cropped) PreviewNote = string.Format(LM.Get("city_plan_preview_cropped"), w, h);
        return texture;
    }

    // 虚线的一点：沿边每 DashLength 像素画一段、空一段
    private static void Dash(Color32[] pixels, int pw, int px, int py, int along)
    {
        if (along / DashLength % 2 != 0) return;
        int i = py * pw + px;
        if (i < 0 || i >= pixels.Length) return;
        Color32 under = pixels[i];
        pixels[i] = new Color32((byte)(under.r + (BorderRed.r - under.r) * BorderAlpha), (byte)(under.g + (BorderRed.g - under.g) * BorderAlpha),
            (byte)(under.b + (BorderRed.b - under.b) * BorderAlpha), 255);
    }

    #endregion
}

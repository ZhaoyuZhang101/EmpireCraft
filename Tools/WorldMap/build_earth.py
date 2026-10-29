"""把公开地球数据烘成模组自带的地表网格 WorldMaps/earth.bytes。

数据来源(先下到临时目录，原始文件不进仓库)：
  - 高程(二选一)：
      · NOAA NCEI ETOPO 2022 60 角秒地表高程，THREDDS OPeNDAP 取经度 28°W~158°E，每 3 格取 1 格(0.05°)。
          北段  .../ETOPO_2022_v1_60s_N90W180_surface.nc.dods?z.z[4800:3:10200][9120:3:20280]  (纬度 10°S~80°N)
          南段  .../ETOPO_2022_v1_60s_N90W180_surface.nc.dods?z.z[3120:3:4797][9120:3:20280]   (纬度 38°S~10°S)
        只有格点高程，格内最高最低按格点算；
      · AWS 公开地形瓦片 Terrarium(https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{z}/{x}/{y}.png，
        高程 = R*256 + G + B/256 - 32768 米，Web 墨卡托)：z6 覆盖地图范围约 1100 张(70 MB)，逐张下载、逐张按经纬度
        重采样进 0.05° 网格(格内均值、最高、最低)，存成 terrarium_z6.npz，瓦片本身不落盘(见 fetch_terrarium)。
  - 海陆、湖泊：Natural Earth 1:10m ne_10m_land / ne_10m_minor_islands / ne_10m_lakes。
      里海、死海、吐鲁番这种低于海平面的地方光看高程会被当成海，海陆以它为准。
  - 河流：Natural Earth 1:10m ne_10m_rivers_lake_centerlines，只用来在旱区沿大河画出可耕的河谷绿洲
      (两河、尼罗河、印度河、阿姆河、塔里木、河套)，不画成水面——一格十来公里，画成水会把大陆切成岛。
  - 山脉、高原、平原、盆地的名字与范围：Natural Earth 1:10m ne_10m_geography_regions_polys(可缺)，只拿来辅助：
      带名字的山脉一带门槛放宽一点、低矮的山脊(阿拉瓦利、温迪亚、萨特普拉)按局部高出补成丘陵带，
      华北、恒河、东北、匈牙利这类有名的平原盆地里门槛收紧一点；判不判山仍以高程为准。
  - 气候(二选一)：
      · Kottek 等 2006 柯本-盖格气候分区 0.5° 网格 Koeppen-Geiger-ASCII.txt；
      · 旧的 earth.bytes：每格的生物群系照旧(柯本分出来的沙漠草原雨林冻土、草原带、旱区河谷绿洲都在里面)，
        海陆、湖泊、近海深浅也照旧(岸线一格不动)；旧网格里山、丘陵格的生物群系丢了，按同一海拔带里最近的平地补。
      Natural Earth、柯本、NOAA 站点下不到时用后一种(Natural Earth 有 GitHub 镜像
      https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/10m_physical/)。

用法：
  python -I build_earth.py <数据目录> <输出 earth.bytes> [预览.png]
      原来的路子：数据目录里要有 etopo_z.dods、etopo_south.dods、ne/ne_10m_*.shp(.dbf)、kg/Koeppen-Geiger-ASCII.txt；
      顺带重出 rivers.bytes。
  python -I build_earth.py fetch-terrarium <数据目录>
      下 Terrarium z6 瓦片，重采样成 <数据目录>/terrarium_z6.npz。
  python -I build_earth.py terrarium <数据目录> <旧 earth.bytes> <输出 earth.bytes> [预览.png]
      数据目录里要有 terrarium_z6.npz、ne/ne_10m_rivers_lake_centerlines.shp(.dbf)，ne/ne_10m_geography_regions_polys.shp(.dbf)
      可缺；海陆湖泊与气候取旧网格。河道数据没变，rivers.bytes 不重出。

网格每格存一个地块编号(编号表写在文件头里，是原版存档用的地块写法，如 "soil_low:grass_low")，
游戏里按选的范围和大小逐格就近取样(见 Scripts/GeneralSystems/WorldMap/EarthTerrain.cs)。

山、丘陵按多尺度起伏判(见 classify_relief)，不只看海拔(用户 2026-10-29："给欧洲那边的山也弄得精细点, 这看着全是平原"，
"你很多地方的地区复杂性都没考虑到""地形"，"还有亚洲那边的山也要精细")：
  - 山：比周围(约 55 公里内的低处)高出 700 米以上、且约 28 公里内起伏 900 米以上，处在局部起伏的上半截(谷底不算)；
    欧洲的比利牛斯、喀尔巴阡、迪纳拉、巴尔干、品都斯、亚平宁、斯堪的纳维亚、高加索，亚洲的太行燕山秦岭大巴武夷南岭、
    天山阿尔泰祁连昆仑、扎格罗斯厄尔布尔士、西高止、长山、日本脊梁都按实际走向成带；
  - 丘陵(本模组的崎岖地、不能耕)：成片的起伏(高出 350 米、起伏 600 米)，或比周围明显更崎岖的山脊；平谷底不算。
    华南、东南亚、印度的低丘(水田梯田的地方)门槛提高，江南、岭南的低山丘陵大多仍算平地，只有真正破碎陡峭的才算；
  - 山间谷地、山口留出来：山里的谷底不算山；名关要道(ROUTES)与被山围住的谷地盆地沿最低处开出三格宽的山口(丘陵)；
  - 高地平原(soil_high)：周围平均海拔 450 米以上，四周高山围着的低盆地(四川、关中、汉中、费尔干纳)仍算低地；
  - 青藏 3800 米以上的高原面是雪丘；雪线随纬度降(赤道 5200 米、阿尔卑斯约 3000 米、斯堪的纳维亚约 1300 米)，
    雪线以上的山顶、丘陵盖雪。
"""
import io
import math
import struct
import sys
import time
import zlib

import numpy as np
from scipy import ndimage

STEP = 0.05
LON0 = -179.991666667 + 9120 / 60.0   # 第 0 列的格心经度
LAT0 = -89.991666667 + 3120 / 60.0    # 第 0 行(最南)的格心纬度
COLS = 3721
ROWS_SOUTH = 560
ROWS_NORTH = 1801
ROWS = ROWS_SOUTH + ROWS_NORTH
LATS = LAT0 + np.arange(ROWS) * STEP
LONS = LON0 + np.arange(COLS) * STEP


def read_dods(path, rows, cols):
    raw = open(path, "rb").read()
    at = raw.find(b"Data:\n") + 6
    n1, n2 = struct.unpack(">ii", raw[at:at + 8])
    assert n1 == n2 == rows * cols, (n1, rows, cols)
    return np.frombuffer(raw, dtype=">f4", count=n1, offset=at + 8).reshape(rows, cols).astype(np.float32)


def read_shapes(path, keep_empty=False):
    """最小的 .shp 读法：只认多边形(类型 5)，返回每个记录的环列表。keep_empty 时别的类型记空表，与 .dbf 的行对齐。"""
    data = open(path, "rb").read()
    pos = 100
    records = []
    while pos < len(data):
        _, length = struct.unpack(">ii", data[pos:pos + 8])
        content = data[pos + 8:pos + 8 + length * 2]
        pos += 8 + length * 2
        shape_type = struct.unpack("<i", content[:4])[0]
        if shape_type != 5:
            if keep_empty:
                records.append([])
            continue
        num_parts, num_points = struct.unpack("<ii", content[36:44])
        parts = struct.unpack("<%di" % num_parts, content[44:44 + 4 * num_parts])
        pts = np.frombuffer(content, dtype="<f8", count=num_points * 2, offset=44 + 4 * num_parts).reshape(-1, 2)
        rings = []
        for i, start in enumerate(parts):
            end = parts[i + 1] if i + 1 < num_parts else num_points
            rings.append(pts[start:end])
        records.append(rings)
    return records


def read_dbf_field(path, field):
    """.dbf 里一个字段的全部取值(字符串)。"""
    dbf = open(path, "rb").read()
    count, header_len, record_len = struct.unpack("<IHH", dbf[4:12])
    offset = 1
    for i in range(32, header_len - 1, 32):
        name = dbf[i:i + 11].split(b"\0")[0].decode()
        length = dbf[i + 16]
        if name == field:
            return [dbf[header_len + r * record_len + offset:header_len + r * record_len + offset + length]
                    .decode("utf-8", "replace").strip() for r in range(count)]
        offset += length
    raise KeyError(field)


def read_polylines(shp_path, dbf_path, max_rank):
    """折线(类型 3)，只取 scalerank 不超过 max_rank 的大河。返回 [(scalerank, [点阵, ...])]。"""
    ranks = [int(text) if text else 99 for text in read_dbf_field(dbf_path, "scalerank")]
    data = open(shp_path, "rb").read()
    pos, index, lines = 100, 0, []
    while pos < len(data):
        _, length = struct.unpack(">ii", data[pos:pos + 8])
        content = data[pos + 8:pos + 8 + length * 2]
        pos += 8 + length * 2
        rank = ranks[index] if index < len(ranks) else 99
        index += 1
        if struct.unpack("<i", content[:4])[0] != 3 or rank > max_rank:
            continue
        num_parts, num_points = struct.unpack("<ii", content[36:44])
        parts = struct.unpack("<%di" % num_parts, content[44:44 + 4 * num_parts])
        pts = np.frombuffer(content, dtype="<f8", count=num_points * 2, offset=44 + 4 * num_parts).reshape(-1, 2)
        segs = [pts[start:(parts[i + 1] if i + 1 < num_parts else num_points)] for i, start in enumerate(parts)]
        lines.append((rank, segs))
    return lines


def rasterize_lines(lines):
    """大河按等级画宽：scalerank 0~2 宽 5 格、3~4 宽 3 格、其余 2 格。返回行 0 = 最南的布尔网格。"""
    from PIL import Image, ImageDraw
    img = Image.new("L", (COLS, ROWS), 0)
    draw = ImageDraw.Draw(img)
    lat_top = LAT0 + (ROWS - 1) * STEP + STEP / 2
    lon_left = LON0 - STEP / 2
    for rank, segs in lines:
        width = 5 if rank <= 2 else 3 if rank <= 4 else 2
        for seg in segs:
            xs = (seg[:, 0] - lon_left) / STEP
            ys = (lat_top - seg[:, 1]) / STEP
            draw.line(list(zip(xs.tolist(), ys.tolist())), fill=255, width=width)
    return (np.asarray(img) >= 128)[::-1]


def signed_area(ring):
    x, y = ring[:, 0], ring[:, 1]
    return 0.5 * float(np.sum(x[:-1] * y[1:] - x[1:] * y[:-1]))


def rasterize(records, value_mask=None):
    """按网格画多边形：外环(顺时针)填 1，洞(逆时针)填 0。返回自北向南的布尔网格。"""
    from PIL import Image, ImageDraw
    scale = 2  # 两倍细画再缩，岸线少锯齿
    w, h = COLS * scale, ROWS * scale
    img = Image.new("L", (w, h), 0)
    draw = ImageDraw.Draw(img)
    lat_top = LAT0 + (ROWS - 1) * STEP + STEP / 2
    lon_left = LON0 - STEP / 2

    def to_px(ring):
        xs = (ring[:, 0] - lon_left) / STEP * scale
        ys = (lat_top - ring[:, 1]) / STEP * scale
        return list(zip(xs.tolist(), ys.tolist()))

    for rings in records:
        for ring in rings:
            if len(ring) < 3:
                continue
            if ring[:, 0].max() < lon_left - 1 or ring[:, 0].min() > lon_left + COLS * STEP + 1:
                continue
            if ring[:, 1].max() < LAT0 - 1 or ring[:, 1].min() > lat_top + 1:
                continue
            hole = signed_area(ring) > 0
            draw.polygon(to_px(ring), fill=0 if hole else 255)
    small = img.resize((COLS, ROWS), Image.BOX)
    return np.asarray(small) >= 128


def read_koppen(path):
    """0.5° 网格 → 本网格(就近)，海上没有分区的格子取最近的陆上分区。"""
    classes = {}
    kg = {}
    with open(path, encoding="latin-1") as f:
        next(f)
        for line in f:
            parts = line.split()
            if len(parts) != 3:
                continue
            lat, lon, cls = float(parts[0]), float(parts[1]), parts[2]
            if cls not in classes:
                classes[cls] = len(classes) + 1
            kg[(round(lat * 4), round(lon * 4))] = classes[cls]
    # 0.5° 粗格
    lat_c = np.arange(-89.75, 90, 0.5)
    lon_c = np.arange(-179.75, 180, 0.5)
    coarse = np.zeros((len(lat_c), len(lon_c)), dtype=np.int16)
    for i, la in enumerate(lat_c):
        for j, lo in enumerate(lon_c):
            coarse[i, j] = kg.get((round(la * 4), round(lo * 4)), 0)
    # 海上补最近陆格
    missing = coarse == 0
    _, (ii, jj) = ndimage.distance_transform_edt(missing, return_indices=True)
    coarse = coarse[ii, jj]
    # 边界抖一抖，免得 0.5° 的方块直接露出来
    rng = np.random.default_rng(7)

    def noise(sigma):
        n = ndimage.gaussian_filter(rng.normal(0, 1, (ROWS, COLS)), sigma)
        return n / n.std()

    # 两层噪声叠加(大块起伏 + 细碎边)，分区边界抖开约 0.3°
    jitter_lat = (noise(8) * 0.7 + noise(2) * 0.3) * 0.3
    jitter_lon = (noise(8) * 0.7 + noise(2) * 0.3) * 0.3
    li = np.clip(np.round((LATS[:, None] + jitter_lat + 89.75) / 0.5), 0, len(lat_c) - 1).astype(int)
    lj = np.clip(np.round((LONS[None, :] + jitter_lon + 179.75) / 0.5), 0, len(lon_c) - 1).astype(int)
    names = {v: k for k, v in classes.items()}
    return coarse[li, lj], names


TYPES = [
    "deep_ocean", "close_ocean", "shallow_waters",
    "sand", "mountains", "mountains:snow_block", "hills", "hills:snow_hills",
]
BIOMES = ["grass", "savanna", "jungle", "desert", "permafrost", "birch", "maple", "swamp"]
for b in BIOMES:
    TYPES.append("soil_low:%s_low" % b)
    TYPES.append("soil_high:%s_high" % b)
CODE = {t: i for i, t in enumerate(TYPES)}
# 平地的地表：沙地(沙丘、旱区海滩)另记一种，其余按生物群系分高低
SURFACES = ["sand"] + BIOMES
SURFACE = {s: i for i, s in enumerate(SURFACES)}


# 欧亚草原带(经纬度多边形，北缘为森林草原线，南缘让给沙漠)
STEPPE_BELTS = [
    [(27, 48), (30, 49.5), (35, 50.5), (40, 51.5), (45, 52.5), (50, 53.5), (55, 54), (60, 54.8), (65, 55), (70, 55),
     (75, 54.5), (80, 53.5), (84, 52.5), (88, 50.5), (95, 50.5), (100, 50), (105, 50.5), (110, 50.5), (115, 51),
     (120, 50.5), (123, 48.5), (124.5, 46), (123.5, 43.5), (121, 42), (117, 41.5), (112, 41), (108, 41.5),
     (100, 42), (95, 43.5), (90, 45), (85, 46.5), (80, 45.5), (75, 44), (70, 44), (65, 46), (60, 46.5), (55, 46),
     (50, 45.5), (47, 44.5), (44, 45), (40, 45.5), (37, 46.5), (34, 45.5), (31, 46.5), (29, 46), (27, 47)],
    [(19, 47.8), (21.5, 48.3), (22.6, 47.0), (21.5, 45.5), (19.4, 46.0)],
]


def steppe_mask(lon2, lat2):
    from PIL import Image, ImageDraw
    img = Image.new("L", (COLS, ROWS), 0)
    draw = ImageDraw.Draw(img)
    for poly in STEPPE_BELTS:
        draw.polygon([((x - LON0) / STEP, (y - LAT0) / STEP) for x, y in poly], fill=255)
    return np.asarray(img) >= 128


def simplify(points, tolerance):
    """Douglas-Peucker 抽稀。"""
    if len(points) < 3:
        return points
    keep = np.zeros(len(points), bool)
    keep[0] = keep[-1] = True
    stack = [(0, len(points) - 1)]
    while stack:
        a, b = stack.pop()
        if b <= a + 1:
            continue
        pa, pb = points[a], points[b]
        seg = pb - pa
        length = np.hypot(*seg) or 1e-9
        rel = points[a + 1:b] - pa
        d = np.abs(seg[0] * rel[:, 1] - seg[1] * rel[:, 0]) / length
        i = int(np.argmax(d))
        if d[i] > tolerance:
            k = a + 1 + i
            keep[k] = True
            stack.append((a, k))
            stack.append((k, b))
    return points[keep]


def export_rivers(lines, path):
    """大河折线：ECRIVER1、条数，每条 等级(byte)、点数(ushort)、点(经纬度 ×100 的 int16)。只留范围内、等级 0~5 的。"""
    west, east, south, north = LON0, LON0 + COLS * STEP, LAT0, LAT0 + ROWS * STEP
    records = []
    for rank, segs in lines:
        if rank > 5:
            continue
        for seg in segs:
            inside = (seg[:, 0] > west) & (seg[:, 0] < east) & (seg[:, 1] > south) & (seg[:, 1] < north)
            if inside.sum() < 2:
                continue
            pts = simplify(seg[inside], 0.02)
            if len(pts) >= 2:
                records.append((rank, pts))
    with open(path, "wb") as f:
        f.write(b"ECRIVER1")
        f.write(struct.pack("<i", len(records)))
        for rank, pts in records:
            f.write(struct.pack("<BH", rank, len(pts)))
            f.write(np.round(pts * 100).astype("<i2").tobytes())
    print("rivers", path, len(records), "lines", sum(len(p) for _, p in records), "points")


def biome_of(cls_name, lon):
    if cls_name in ("Af", "Am"):
        return "jungle"
    if cls_name in ("Aw", "As"):
        # 本模组把稀树草原算旱地、不能耕：印度、中南半岛的季风平原要能种地，按草地；非洲照旧稀树草原
        return "grass" if lon > 55 else "savanna"
    if cls_name in ("BWh", "BWk"):
        return "desert"
    if cls_name in ("BSh", "BSk"):
        return "savanna"
    if cls_name in ("ET", "EF"):
        return "permafrost"
    if cls_name in ("Dfc", "Dfd", "Dwc", "Dwd", "Dsc", "Dsd"):
        return "birch"
    if cls_name in ("Cfb", "Cfc"):
        return "maple"
    return "grass"


# ---------------- 多尺度起伏：山、丘陵、高地 ----------------

RELIEF = dict(
    # 山：高出周围(约 55 公里内低处，平滑过)PEAK_RISE 米、28 公里内起伏 PEAK_RELIEF 米，且在局部起伏的上 PEAK_UPPER 以上；
    # 山脉核心两侧起伏够 PEAK_FLANK 成的格子并进来(山带宽窄照实)
    PEAK_RISE=700.0, PEAK_RELIEF=900.0, PEAK_UPPER=0.3, PEAK_FLANK=0.75,
    # 五千米以上的高原上，高出高原面 ROOF_RISE、在起伏上 ROOF_UPPER 以上的也算山(青藏腹地约三成是山岭，其余是雪丘高原面)
    ROOF=5000.0, ROOF_RISE=250.0, ROOF_UPPER=0.45,
    # 山里的谷底：局部起伏的下 VALLEY 以下，紧挨着的下 VALLEY_SIDE 以下也算(谷地至少两三格宽，取样后不断)；
    # 起伏超过 DEEP_RELIEF 起逐渐收到 DEEP 倍，喜马拉雅、喀喇昆仑这种三四千米深的峡谷只留主谷，山体不被切碎
    VALLEY=0.3, VALLEY_SIDE=0.42, DEEP_RELIEF=1800.0, DEEP=0.55,
    # 丘陵：成片的起伏(高出 HILL_RISE、起伏 HILL_RELIEF)；或比周围(约 65 公里)平均起伏崎岖 RUGGED 倍以上的地方
    # (起伏至少 RUGGED_RELIEF、高出至少 RUGGED_RISE)
    HILL_RISE=350.0, HILL_RELIEF=600.0, RUGGED=2.0, RUGGED_RELIEF=300.0, RUGGED_RISE=150.0,
    # 带名字的山脉(Natural Earth)附近 RIDGE_NEAR 格内：低矮山脊局部(约 30 公里)高出 RIDGE_RISE、起伏 RIDGE_RELIEF、
    # 比周围崎岖 RIDGE_RUGGED 倍也算丘陵(阿拉瓦利、温迪亚、萨特普拉、东高止、哈尔茨、孚日)
    RIDGE_NEAR=10, RIDGE_RISE=150.0, RIDGE_RELIEF=200.0, RIDGE_RUGGED=1.3,
    # 平谷底(局部起伏下 FLOOR 以下、格内起伏不到 FLOOR_RELIEF 米)不算丘陵：安纳托利亚、巴尔干、亚平宁的河谷盆地，
    # 汉中、佛罗伦萨、大理、斯巴达这类夹在山间的谷地照样能耕
    FLOOR=0.3, FLOOR_RELIEF=250.0,
    # 带名字的山脉里门槛乘 NAMED_RANGE；有名的平原盆地里山的门槛乘 NAMED_PLAIN_PEAK、丘陵乘 NAMED_PLAIN_HILL
    NAMED_RANGE=0.85, NAMED_PLAIN_PEAK=1.1, NAMED_PLAIN_HILL=1.3,
    # 季风水田区的低丘(江南、岭南、东南亚，印度打六折)：丘陵门槛最多提高 MONSOON，海拔 600 米以下全额、1000 米以上不加
    MONSOON=0.45,
    # 两千八百米以上起伏 HIGH_RELIEF 米就算丘陵(高寒，青海、帕米尔、藏东)
    HIGH=2800.0, HIGH_RELIEF=250.0,
    # 高原面雪丘
    PLATEAU=3800.0,
    # 高地平原：周围(σ约 17 公里)平均海拔 SOIL_HIGH 以上；周围(σ约 80 公里)比它高 BASIN 米、本身不到 BASIN_TOP 的是盆地，算低地
    SOIL_HIGH=450.0, BASIN=350.0, BASIN_TOP=900.0,
    # 被山围住的谷地盆地至少 POCKET 格、谷底中位海拔低于 POCKET_TOP 才开山口连出去
    POCKET=12, POCKET_TOP=3000.0,
)

# 雪线(米)随纬度：赤道 5200，喜马拉雅北坡约 5000，阿尔卑斯约 3000，斯堪的纳维亚约 1300
SNOWLINE = ([0, 30, 38, 42, 46, 55, 65, 80], [5200, 5000, 4400, 3700, 3000, 1900, 1250, 900])


def lat_filter(func, arr, ns, band=4.0):
    """按纬度分带滤波：南北向 ns 格，东西向按 1/cos(纬度) 放宽，窗口在地面上大致方正(网格是等经纬度的)。"""
    out = np.empty_like(arr)
    done = {}
    for lo in np.arange(np.floor(LATS[0] / band) * band, LATS[-1] + band, band):
        rows = np.nonzero((LATS >= lo) & (LATS < lo + band))[0]
        if len(rows) == 0:
            continue
        ew = ns / max(0.3, math.cos(math.radians(min(abs(lo), abs(lo + band)))))
        key = round(ew * 2) / 2
        if key not in done:
            done[key] = func(arr, ns, key)
        out[rows] = done[key][rows]
    return out


def _max(a, ns, ew):
    return ndimage.maximum_filter(a, size=(int(ns), int(round(ew)) | 1))


def _min(a, ns, ew):
    return ndimage.minimum_filter(a, size=(int(ns), int(round(ew)) | 1))


def _gauss(a, ns, ew):
    return ndimage.gaussian_filter(a, (ns, ew))


def relief_features(elev, emax, emin, water):
    """各尺度的高出、起伏。水面取最近陆格的高度：高原湖(青藏一带四五千米)周围不会因为湖面算 0 而凭空起一圈山。"""
    _, (wi, wj) = ndimage.distance_transform_edt(water, return_indices=True)

    def landed(a):
        return np.where(water, 0, np.maximum(a, 0))[wi, wj].astype(np.float32)

    e, top, bottom = landed(elev), landed(emax), landed(emin)
    f = {"elev": e, "cell": top - bottom}
    f["s1"] = lat_filter(_gauss, e, 1.0)
    f["s2"] = lat_filter(_gauss, e, 1.5)
    f["s3"] = lat_filter(_gauss, e, 3.0)
    f["around"] = lat_filter(_gauss, e, 15.0)
    # 高出：比周围低处(先取最低再平滑)高多少，约 30 公里与 55 公里两档
    f["rise5"] = e - lat_filter(_gauss, lat_filter(_min, f["s1"], 11), 2.5)
    f["rise10"] = e - lat_filter(_gauss, lat_filter(_min, f["s1"], 21), 5.0)
    # 起伏：约 28 公里内格内最高与最低之差
    f["relief"] = lat_filter(_max, top, 5) - lat_filter(_min, bottom, 5)
    land = (~water).astype(np.float32)
    f["relief_around"] = (lat_filter(_gauss, np.where(water, 0, f["relief"]).astype(np.float32), 12.0) /
                          np.maximum(lat_filter(_gauss, land, 12.0), 0.05))
    # 在局部起伏里的位置(0 谷底、1 山顶)，约 39 公里窗口
    s07 = lat_filter(_gauss, e, 0.7)
    low, high = lat_filter(_min, s07, 7), lat_filter(_max, s07, 7)
    f["upper"] = (s07 - low) / (high - low + 50)
    return f


def region_polygons(data_dir):
    """Natural Earth 的地理分区：带名字的山脉、平原盆地低地两张布尔网格(行 0 = 最南)；没有文件就都是空的。"""
    base = data_dir + "/ne/ne_10m_geography_regions_polys"
    try:
        kinds = read_dbf_field(base + ".dbf", "FEATURECLA")
        records = read_shapes(base + ".shp", keep_empty=True)
    except OSError:
        print("没有 ne_10m_geography_regions_polys，山脉平原不作辅助")
        empty = np.zeros((ROWS, COLS), bool)
        return empty, empty
    ranges = rasterize([r for k, r in zip(kinds, records) if k == "Range/mtn" and r])[::-1]
    plains = rasterize([r for k, r in zip(kinds, records) if k in ("Plain", "Basin", "Lowland") and r])[::-1]
    return ranges, plains


def remove_small(mask, cells):
    """去掉不到 cells 格的零星小块(斜着相连也算一块，细长的山脊不拆)。"""
    lab, n = ndimage.label(mask, structure=np.ones((3, 3), bool))
    if n == 0:
        return mask
    keep = np.bincount(lab.ravel()) >= cells
    keep[0] = False
    return keep[lab]


def classify_relief(f, water, ranges, plains):
    """返回 (山, 丘陵) 两张布尔网格。"""
    p = RELIEF
    land = ~water
    lon2 = np.broadcast_to(LONS[None, :], (ROWS, COLS))
    lat2 = np.broadcast_to(LATS[:, None], (ROWS, COLS))
    e, rise5, rise10, relief, upper = f["elev"], f["rise5"], f["rise10"], f["relief"], f["upper"]
    # 门槛因地而异：带名字的山脉放宽、有名的平原收紧；季风水田区的低丘(用户 2026-10-29 前定：丘陵是崎岖地、不能耕，
    # 江南、岭南的低山丘陵大多仍算平地)门槛提高，海拔越高加得越少(云贵、掸邦的高原照常)
    k_peak = np.ones((ROWS, COLS), np.float32)
    k_hill = np.ones((ROWS, COLS), np.float32)
    k_peak[ranges] *= p["NAMED_RANGE"]
    k_hill[ranges] *= p["NAMED_RANGE"]
    k_peak[plains] *= p["NAMED_PLAIN_PEAK"]
    k_hill[plains] *= p["NAMED_PLAIN_HILL"]
    paddy = ndimage.gaussian_filter(((lon2 >= 97) & (lat2 <= 33.5)).astype(np.float32), 10)
    india = ndimage.gaussian_filter(((lon2 >= 68) & (lon2 < 97) & (lat2 <= 30)).astype(np.float32), 10)
    taper = np.clip((1000 - f["s3"]) / 400, 0, 1)
    k_hill *= 1 + p["MONSOON"] * np.maximum(paddy, india * 0.6) * taper

    # 山：高出与起伏都够、在起伏的上半截(起伏极大的地方放宽到下半截)；核心补缝去渣，两侧够七成半的并进来；谷底挖掉
    peak = np.minimum(rise10 / (p["PEAK_RISE"] * k_peak), relief / (p["PEAK_RELIEF"] * k_peak))
    deep = 1 - (1 - p["DEEP"]) * np.clip((relief - p["DEEP_RELIEF"]) / p["DEEP_RELIEF"], 0, 1)
    core = land & (peak >= 1) & (upper >= p["PEAK_UPPER"] * deep)
    # 高原上的山岭(羌塘、可可西里)：本身五千米以上，比高原面高出一截、在起伏上半截
    core |= land & (e >= p["ROOF"]) & (rise10 >= p["ROOF_RISE"]) & (upper >= p["ROOF_UPPER"])
    core = remove_small(ndimage.binary_closing(core) & land, 4)
    mountains = core | (ndimage.binary_dilation(core) & (peak >= p["PEAK_FLANK"]) & land)
    valley = upper < p["VALLEY"] * deep
    valley |= ndimage.binary_dilation(valley) & (upper < p["VALLEY_SIDE"] * deep)
    mountains &= ~valley

    # 丘陵：成片起伏；比周围明显崎岖；带名字的山脉附近的低矮山脊(华南水田区不补)。平谷底不算
    hill = np.minimum(rise10 / (p["HILL_RISE"] * k_hill), relief / (p["HILL_RELIEF"] * k_hill))
    k_named = np.where(ranges, p["NAMED_RANGE"], 1.0)
    around = np.maximum(f["relief_around"], 1.0)
    rugged = np.minimum.reduce([relief / (p["RUGGED"] * around * k_named), relief / (p["RUGGED_RELIEF"] * k_named),
                                rise10 / (p["RUGGED_RISE"] * k_named)])
    near = ndimage.distance_transform_edt(~ranges) <= p["RIDGE_NEAR"]
    ridge = near & (paddy < 0.5) & (rise5 >= p["RIDGE_RISE"]) & (relief >= p["RIDGE_RELIEF"]) & \
        (relief >= p["RIDGE_RUGGED"] * around)
    floor = (upper < p["FLOOR"]) & (f["cell"] < p["FLOOR_RELIEF"])
    hills = land & ((hill >= 1) | (rugged >= 1) | ridge)
    hills = remove_small(ndimage.binary_closing(hills) & land & ~floor, 6)
    hills |= land & (f["s1"] >= p["HIGH"]) & (relief >= p["HIGH_RELIEF"])
    hills &= ~mountains
    return mountains, hills


# 名关要道：两头(经度, 纬度)之间按最低处开出山口(两头的城不翻山走得通)，窗口在连线外放宽若干度
ROUTES = [
    # 阿尔卑斯、比利牛斯、喀尔巴阡、巴尔干、迪纳拉、亚平宁、品都斯、斯堪的纳维亚
    ("布伦纳山口", (11.39, 47.27), (11.35, 46.50), 0.5),
    ("圣哥达山口", (8.64, 46.88), (9.02, 46.19), 0.5),
    ("辛普朗山口", (7.99, 46.31), (8.29, 46.11), 0.4),
    ("大圣伯纳德山口", (7.07, 46.10), (7.32, 45.74), 0.4),
    ("塞尼山口", (6.35, 45.28), (7.05, 45.14), 0.4),
    ("塔尔维西奥", (13.85, 46.61), (13.23, 46.06), 0.5),
    ("塞默灵", (16.37, 48.20), (15.44, 47.07), 0.6),
    ("龙塞斯瓦列斯", (-1.64, 42.81), (-1.47, 43.49), 0.4),
    ("松波特山口", (-0.55, 42.57), (-0.60, 43.19), 0.4),
    ("佩尔蒂斯", (2.96, 42.27), (2.89, 42.69), 0.4),
    ("杜克拉山口", (21.77, 49.69), (21.57, 49.08), 0.5),
    ("维雷茨基山口", (22.70, 48.44), (24.03, 49.84), 0.6),
    ("普雷代亚尔山口", (25.59, 45.66), (26.02, 44.94), 0.5),
    ("红塔山口", (24.15, 45.79), (24.37, 45.10), 0.4),
    ("铁门", (22.40, 44.72), (22.87, 43.99), 0.5),
    ("希普卡山口", (25.32, 42.87), (25.39, 42.62), 0.4),
    ("图拉真门", (23.32, 42.70), (24.75, 42.15), 0.5),
    ("摩拉瓦河谷", (21.90, 43.32), (21.43, 42.00), 0.5),
    ("瓦尔达尔河谷", (21.43, 42.00), (22.94, 40.64), 0.5),
    ("内雷特瓦河谷", (17.81, 43.34), (18.41, 43.86), 0.4),
    ("亚平宁(博洛尼亚—佛罗伦萨)", (11.34, 44.49), (11.25, 43.77), 0.4),
    ("弗拉米尼亚大道", (13.02, 43.84), (12.70, 42.95), 0.4),
    ("瓦莱里亚大道", (12.50, 41.90), (14.21, 42.46), 0.5),
    ("阿庇亚大道", (14.25, 40.85), (15.55, 41.46), 0.5),
    ("卡塔拉山口", (20.85, 39.66), (21.77, 39.55), 0.4),
    ("特隆赫姆—厄斯特松德", (10.40, 63.43), (14.64, 63.18), 0.8),
    ("奥斯陆—卑尔根", (10.75, 59.91), (5.32, 60.39), 1.0),
    # 安纳托利亚、高加索、伊朗、黎凡特
    ("西里西亚门", (35.48, 38.73), (34.89, 36.92), 0.6),
    ("济加纳山口", (41.27, 39.90), (39.72, 41.00), 0.6),
    ("达里亚尔峡谷", (44.68, 43.02), (44.79, 41.72), 0.5),
    ("第比利斯—埃里温", (44.79, 41.72), (44.51, 40.18), 0.5),
    ("摩苏尔—迪亚巴克尔", (43.13, 36.34), (40.22, 37.91), 0.6),
    ("扎格罗斯(巴格达—哈马丹)", (44.36, 33.31), (48.51, 34.80), 0.8),
    ("设拉子—布什尔", (52.53, 29.60), (50.84, 28.97), 0.6),
    ("厄尔布尔士(德黑兰—萨里)", (51.39, 35.69), (53.06, 36.56), 0.6),
    ("加兹温—拉什特", (50.00, 36.27), (49.58, 37.28), 0.5),
    ("大马士革—贝鲁特", (36.29, 33.51), (35.50, 33.89), 0.4),
    ("阿勒颇—安条克", (37.16, 36.20), (36.16, 36.20), 0.4),
    # 中亚、南亚
    ("开伯尔山口", (71.57, 34.01), (69.17, 34.53), 0.6),
    ("兴都库什(喀布尔—马扎里沙里夫)", (69.17, 34.53), (67.11, 36.71), 0.8),
    ("喀布尔—巴米扬", (69.17, 34.53), (67.83, 34.82), 0.5),
    ("博兰山口", (67.00, 30.18), (67.88, 29.54), 0.5),
    ("撒马尔罕—费尔干纳", (66.97, 39.65), (71.78, 40.39), 0.8),
    ("伊尔克什坦(喀什—奥什)", (75.99, 39.47), (72.80, 40.53), 0.8),
    ("加德满都—比尔根杰", (85.32, 27.70), (84.88, 27.01), 0.5),
    ("博尔山口(浦那—孟买)", (73.86, 18.52), (72.88, 19.08), 0.4),
    ("帕尔卡德山口", (76.96, 11.00), (76.65, 10.78), 0.4),
    ("若开山(谬乌—敏巫)", (93.19, 20.59), (94.88, 20.18), 0.6),
    # 中国
    ("居庸关", (116.40, 39.90), (115.05, 40.60), 0.5),
    ("古北口", (116.40, 39.90), (117.95, 40.97), 0.5),
    ("山海关", (121.13, 41.10), (116.40, 39.90), 0.6),
    ("雁门关", (112.55, 37.87), (113.30, 40.08), 0.6),
    ("井陉", (114.51, 38.04), (112.55, 37.87), 0.5),
    ("滏口陉", (114.49, 36.61), (113.12, 36.20), 0.5),
    ("太行陉", (113.90, 35.30), (112.85, 35.49), 0.4),
    ("潼关", (112.45, 34.62), (108.94, 34.34), 0.4),
    ("武关", (108.94, 34.34), (112.53, 33.00), 0.6),
    ("子午道", (108.94, 34.34), (107.03, 33.07), 0.8),
    ("剑门蜀道", (107.03, 33.07), (104.07, 30.67), 0.8),
    ("三峡", (106.55, 29.56), (111.29, 30.70), 0.6),
    ("萧关", (108.94, 34.34), (106.28, 36.00), 0.6),
    ("陇山(西安—兰州)", (108.94, 34.34), (103.83, 36.06), 0.8),
    ("乌鞘岭", (103.83, 36.06), (102.64, 37.93), 0.6),
    ("河西走廊", (102.64, 37.93), (98.49, 39.73), 0.6),
    ("湟水(西宁—兰州)", (101.78, 36.62), (103.83, 36.06), 0.5),
    ("贺兰山三关口", (106.23, 38.49), (105.67, 38.83), 0.4),
    ("武胜关", (114.07, 32.13), (114.30, 30.60), 0.5),
    ("梅关", (114.31, 25.12), (114.93, 25.85), 0.5),
    ("骑田岭", (113.00, 25.80), (113.60, 24.80), 0.5),
    ("湘桂走廊", (112.57, 26.89), (110.29, 25.27), 0.6),
    ("分水关", (118.03, 27.75), (117.97, 28.45), 0.4),
    ("仙霞关", (118.60, 28.75), (118.53, 27.92), 0.4),
    ("凭祥", (108.37, 22.82), (106.76, 21.85), 0.6),
    ("滇中(昆明—大理)", (102.71, 25.04), (100.27, 25.60), 0.6),
    ("大兴安岭(海拉尔—齐齐哈尔)", (119.70, 49.20), (123.90, 47.30), 0.8),
    ("天山北路(乌鲁木齐—伊宁)", (87.60, 43.80), (81.30, 43.90), 1.2),
    ("达坂城", (89.19, 42.95), (87.60, 43.80), 0.6),
    ("吐鲁番—库尔勒", (89.19, 42.95), (86.15, 41.73), 0.8),
    # 东南亚、朝鲜、日本、北非
    ("长山(荣市—他曲)", (105.68, 18.67), (104.80, 17.40), 0.6),
    ("马来半岛(吉隆坡—关丹)", (101.69, 3.14), (103.33, 3.82), 0.6),
    ("大关岭(汉城—江陵)", (126.98, 37.57), (128.90, 37.75), 0.5),
    ("鸟岭(汉城—大邱)", (126.98, 37.57), (128.60, 35.87), 0.6),
    ("中山道(京都—名古屋)", (135.77, 35.01), (136.91, 35.18), 0.5),
    ("东海道(名古屋—江户)", (136.91, 35.18), (139.69, 35.69), 0.6),
    ("提兹恩提什卡山口", (-7.99, 31.63), (-6.90, 30.92), 0.5),
    ("塔扎走廊", (-5.00, 34.03), (-1.91, 34.68), 0.6),
]


def _window_graph(cost):
    """窗口里的八邻图，边权为两格代价均值乘步长；水(无穷大)不连。"""
    from scipy.sparse import coo_matrix
    h, w = cost.shape
    idx = np.arange(h * w).reshape(h, w)
    rows, cols, vals = [], [], []
    for dy, dx in ((-1, -1), (-1, 0), (-1, 1), (0, 1)):
        ys0, ys1 = max(0, -dy), h - max(0, dy)
        xs0, xs1 = max(0, -dx), w - max(0, dx)
        a = idx[ys0:ys1, xs0:xs1]
        b = idx[ys0 + dy:ys1 + dy, xs0 + dx:xs1 + dx]
        weight = (cost[ys0:ys1, xs0:xs1] + cost[ys0 + dy:ys1 + dy, xs0 + dx:xs1 + dx]) * (0.7071 if dy and dx else 0.5)
        ok = np.isfinite(weight)
        rows.append(a[ok])
        cols.append(b[ok])
        vals.append(weight[ok])
    r, c, v = np.concatenate(rows), np.concatenate(cols), np.concatenate(vals)
    return coo_matrix((v, (r, c)), shape=(h * w, h * w)).tocsr()


def _cheapest(cost, src, dst):
    """窗口里从 src 任一格到 dst 任一格最省的一条路(布尔网格)；走不通返回 None。"""
    from scipy.sparse.csgraph import dijkstra
    h, w = cost.shape
    dist, pred, _ = dijkstra(_window_graph(cost), directed=False, indices=np.flatnonzero(src), min_only=True,
                             return_predecessors=True)
    targets = np.flatnonzero(dst)
    if len(targets) == 0:
        return None
    node = targets[np.argmin(dist[targets])]
    if not np.isfinite(dist[node]):
        return None
    path = np.zeros(h * w, bool)
    while node >= 0 and not path[node]:
        path[node] = True
        node = pred[node]
    return path.reshape(h, w)


def open_passes(f, water, mountains):
    """山口：名关要道两头之间、被山围住的谷地盆地与外面之间，沿最低处开三格宽的口子(山改丘陵)。返回开出的格。"""
    land = ~water
    _, (li, lj) = ndimage.distance_transform_edt(water, return_indices=True)
    # 走山里一格的代价随海拔指数涨(1400 米约 30、2500 米约 140，平地 1)：路顺着谷地走、从最低的垭口翻过去
    climb = 4.0 * np.exp(np.clip(f["s2"], 0, 6000) / 700.0)
    opened = np.zeros((ROWS, COLS), bool)
    square = np.ones((3, 3), bool)

    def cost(y0, y1, x0, x1):
        steep = mountains[y0:y1, x0:x1] & ~opened[y0:y1, x0:x1]
        c = np.where(steep, climb[y0:y1, x0:x1], 1.0)
        return np.where(land[y0:y1, x0:x1], c, np.inf)

    def carve(path, y0, y1, x0, x1):
        full = np.zeros((ROWS, COLS), bool)
        full[y0:y1, x0:x1] = path
        opened[:] |= ndimage.binary_dilation(full, structure=square) & mountains

    def cell(lon, lat):
        y = min(max(int(round((lat - LAT0) / STEP)), 0), ROWS - 1)
        x = min(max(int(round((lon - LON0) / STEP)), 0), COLS - 1)
        return li[y, x], lj[y, x]   # 落在水里(海港)的挪到最近的陆格

    missed = []
    for name, a, b, margin in ROUTES:
        (ya, xa), (yb, xb) = cell(*a), cell(*b)
        m = int(margin / STEP) + 4
        y0, y1 = max(0, min(ya, yb) - m), min(ROWS, max(ya, yb) + m + 1)
        x0, x1 = max(0, min(xa, xb) - m), min(COLS, max(xa, xb) + m + 1)
        src = np.zeros((y1 - y0, x1 - x0), bool)
        dst = np.zeros_like(src)
        src[ya - y0, xa - x0] = True
        dst[yb - y0, xb - x0] = True
        path = _cheapest(cost(y0, y1, x0, x1), src, dst)
        if path is None:
            missed.append(name)
            continue
        carve(path, y0, y1, x0, x1)
    print("名关要道 %d 条，开出 %d 格%s" % (len(ROUTES), opened.sum(), ("，走不通：" + "、".join(missed)) if missed else ""))

    # 被山围住的谷地盆地：每块陆地上最大的一片平地丘陵为主，别的够大的(谷底不太高)按最省的路连到比它大的一片；两轮
    landmass, _ = ndimage.label(land, structure=square)
    for round_ in range(2):
        lab, n = ndimage.label(land & ~(mountains & ~opened))
        sizes = np.bincount(lab.ravel())
        sizes[0] = 0
        boxes = ndimage.find_objects(lab)
        mains = {}
        for c in np.argsort(-sizes):
            if c == 0 or sizes[c] == 0:
                continue
            sl = boxes[c - 1]
            ys, xs = np.nonzero(lab[sl] == c)
            mains.setdefault(landmass[sl][ys[0], xs[0]], c)
        main_set = set(mains.values())
        joined = 0
        for c in np.argsort(-sizes):
            if c == 0 or c in main_set or sizes[c] < RELIEF["POCKET"]:
                continue
            sl = boxes[c - 1]
            mine = lab[sl] == c
            if np.median(f["s1"][sl][mine]) >= RELIEF["POCKET_TOP"]:
                continue
            m = 30
            y0, y1 = max(0, sl[0].start - m), min(ROWS, sl[0].stop + m)
            x0, x1 = max(0, sl[1].start - m), min(COLS, sl[1].stop + m)
            sub = lab[y0:y1, x0:x1]
            src = sub == c
            dst = (sub > 0) & (sub != c) & (sizes[sub] > sizes[c])
            if not dst.any():
                dst = (sub > 0) & (sub != c)
            path = _cheapest(cost(y0, y1, x0, x1), src, dst) if dst.any() else None
            if path is not None:
                carve(path, y0, y1, x0, x1)
                joined += 1
        print("山间谷地第 %d 轮：%d 片连出去，累计开出 %d 格" % (round_ + 1, joined, opened.sum()))
    return opened


def compose(water_codes, water, surface, mountains, hills, f):
    """拼出网格：水照 water_codes；平地按地表(沙地或生物群系)分高低；丘陵、高原面、山盖上去，雪线以上盖雪。"""
    p = RELIEF
    land = ~water
    lat2 = np.broadcast_to(LATS[:, None], (ROWS, COLS))
    snowy = f["elev"] >= np.interp(np.abs(lat2), *SNOWLINE)
    plateau = land & (f["s1"] > p["PLATEAU"]) & ~mountains
    basin = (f["around"] - f["s3"] >= p["BASIN"]) & (f["s3"] < p["BASIN_TOP"])
    high = (f["s3"] >= p["SOIL_HIGH"]) & ~basin
    out = np.where(water, water_codes, 0).astype(np.uint8)
    flat = land & ~mountains & ~hills & ~plateau
    for i, name in enumerate(SURFACES):
        mask = flat & (surface == i)
        if name == "sand":
            out[mask] = CODE["sand"]
            continue
        out[mask & high] = CODE["soil_high:%s_high" % name]
        out[mask & ~high] = CODE["soil_low:%s_low" % name]
    out[land & (hills | plateau)] = CODE["hills"]
    out[land & ((hills & snowy) | plateau)] = CODE["hills:snow_hills"]
    out[mountains] = CODE["mountains"]
    out[mountains & snowy] = CODE["mountains:snow_block"]
    return out


def dune_noise():
    rng = np.random.default_rng(11)
    noise = ndimage.gaussian_filter(rng.normal(0, 1, (ROWS, COLS)), 5)
    return noise / noise.std()


def local_relief(f):
    """格子高出周围(约 30 公里内)最低处多少：沙丘只铺在平坦的大漠腹地。"""
    return f["elev"] - ndimage.minimum_filter(f["elev"], size=7)


def build(data_dir, out_path, preview_path=None):
    north = read_dods(data_dir + "/etopo_z.dods", ROWS_NORTH, COLS)
    south = read_dods(data_dir + "/etopo_south.dods", ROWS_SOUTH, COLS)
    elev = np.vstack([south, north])  # 行 0 = 最南
    land = rasterize(read_shapes(data_dir + "/ne/ne_10m_land.shp"))
    land |= rasterize(read_shapes(data_dir + "/ne/ne_10m_minor_islands.shp"))
    lakes = rasterize(read_shapes(data_dir + "/ne/ne_10m_lakes.shp"))
    land = land[::-1]   # 自北向南 → 行 0 = 最南
    lakes = lakes[::-1]
    # 大陆多边形极大，PIL 偶尔在某条扫描线上横着多填一整行(阿拉伯海 21°N 一条)：深于 200 米的一律按海
    # (卡塔拉洼地 -133 米、吐鲁番 -154 米仍是陆地)
    land &= ~(elev < -200)
    lakes &= land
    # 太小的湖(不到 3 格)填平：一格十来公里，小湖只会把陆地戳出窟窿
    lab, n = ndimage.label(lakes)
    sizes = ndimage.sum(lakes, lab, range(1, n + 1))
    keep = np.zeros(n + 1, bool)
    keep[1:] = sizes >= 3
    lakes = keep[lab]
    water = ~land | lakes
    kg, kg_names = read_koppen(data_dir + "/kg/Koeppen-Geiger-ASCII.txt")

    # 水：近岸两格内或浅于 150 米为浅水，深于 1500 米为深海
    water_codes = np.zeros((ROWS, COLS), dtype=np.uint8)
    coast_dist = ndimage.distance_transform_edt(water)
    depth = -elev
    sea = water & ~lakes
    water_codes[sea] = CODE["close_ocean"]
    water_codes[sea & (depth > 1500) & (coast_dist > 4)] = CODE["deep_ocean"]
    water_codes[sea & ((depth < 150) | (coast_dist <= 2))] = CODE["shallow_waters"]
    water_codes[lakes] = CODE["shallow_waters"]
    water_codes[lakes & (coast_dist > 4)] = CODE["close_ocean"]

    lon2 = np.broadcast_to(LONS[None, :], (ROWS, COLS))
    lat2 = np.broadcast_to(LATS[:, None], (ROWS, COLS))
    east = lon2 > 55
    names = np.empty((ROWS, COLS), dtype=object)
    for code, name in kg_names.items():
        names[kg == code] = name
    land_only = land & ~lakes
    biome = np.full((ROWS, COLS), "grass", dtype=object)
    for code, name in kg_names.items():
        mask = (kg == code) & land_only
        biome[mask & east] = biome_of(name, 100)
        biome[mask & ~east] = biome_of(name, 0)
    # 边界起伏：两层噪声把下面几条经纬度界线抖开约 1°，免得冻土、草原带切成方块、透镜
    rng_b = np.random.default_rng(23)

    def wobble(sigma):
        n = ndimage.gaussian_filter(rng_b.normal(0, 1, (ROWS, COLS)), sigma)
        return n / n.std()

    wob_x = (wobble(14) * 0.7 + wobble(4) * 0.3) * 1.0
    wob_y = (wobble(14) * 0.7 + wobble(4) * 0.3) * 1.0
    # 西伯利亚冻土：极寒副极地(Dfd/Dwd)全是；其余副极地按"越往东界线越往南"——乌拉尔一带北纬 61°，
    # 叶尼塞约 57°，勒拿约 54°，远东约 51°；乌拉尔以西(斯堪的纳维亚、俄罗斯北部)仍是针叶林
    subarctic = np.isin(names, ["Dfc", "Dwc", "Dsc"])
    extreme = np.isin(names, ["Dfd", "Dwd", "Dsd"])
    frost_line = lat2 + wob_y * 1.5 + np.maximum(0, lon2 - 60) * 0.12
    frozen = land_only & (extreme | (subarctic & (lon2 + wob_x > 55) & (frost_line > 61)))
    biome[frozen] = "permafrost"
    # 欧亚草原带：匈牙利平原、黑海北岸、哈萨克、蒙古、呼伦贝尔与科尔沁——柯本里多算湿润大陆性，植被其实是草原；
    # 带内非沙漠、非冻土的一律按草原(本模组旱地，游牧的水土)
    belt_grid = steppe_mask(lon2, lat2)
    bi = np.clip(np.round((lat2 + wob_y - LAT0) / STEP), 0, ROWS - 1).astype(int)
    bj = np.clip(np.round((lon2 + wob_x - LON0) / STEP), 0, COLS - 1).astype(int)
    belt = belt_grid[bi, bj] & land_only & ~np.isin(names, ["BWh", "BWk"]) & ~frozen
    biome[belt] = "savanna"
    surface = np.zeros((ROWS, COLS), np.int8)
    for b in BIOMES:
        surface[biome == b] = SURFACE[b]

    f = relief_features(elev, elev, elev, water)
    ranges, plains = region_polygons(data_dir)
    mountains, hills = classify_relief(f, water, ranges, plains)
    opened = open_passes(f, water, mountains)
    mountains &= ~opened
    hills |= opened
    rough = mountains | hills
    # 大漠腹地(撒哈拉、阿拉伯、塔克拉玛干、戈壁)：起伏小、离海远的沙漠里成片沙丘；旱区海岸是沙滩
    desert = land_only & np.isin(names, ["BWh", "BWk"])
    coast_far = ndimage.distance_transform_edt(~sea) > 8
    surface[desert & (local_relief(f) < 250) & coast_far & (dune_noise() > 0.2)] = SURFACE["sand"]
    surface[land_only & ndimage.binary_dilation(sea, iterations=1) & desert] = SURFACE["sand"]
    # 旱区大河河谷：沙漠、草原里沿河画出可耕草地(两河、尼罗河、印度河、阿姆河、塔里木、河套)
    arid = land_only & np.isin(names, ["BWh", "BWk", "BSh", "BSk"])
    river_lines = read_polylines(data_dir + "/ne/ne_10m_rivers_lake_centerlines.shp",
                                 data_dir + "/ne/ne_10m_rivers_lake_centerlines.dbf", 6)
    surface[oasis_mask(river_lines, f) & arid & ~rough] = SURFACE["grass"]
    # 河道本身不烘进网格(一格宽的线按地图大小取样会断)：折线另存，游戏里按地图大小画水道、留渡口
    export_rivers(river_lines, out_path.replace("earth.bytes", "rivers.bytes"))
    finish(compose(water_codes, water, surface, mountains, hills, f), out_path, preview_path)


def oasis_mask(river_lines, f):
    """河道两侧；低平的冲积平原(海拔 150 米以下：两河之间、尼罗河三角洲、印度河下游、花剌子模)沿河再放宽约 30 公里。"""
    rivers = rasterize_lines(river_lines)
    return rivers | (ndimage.binary_dilation(rivers, iterations=6) & (f["s1"] < 150))


def read_earth(path):
    """读回 earth.bytes：(网格, 地块写法表)，行 0 = 最南。"""
    data = open(path, "rb").read()
    assert data[:8] == b"ECEARTH1", path
    cols, rows = struct.unpack("<ii", data[8:16])
    count = struct.unpack("<i", data[28:32])[0]
    pos, types = 32, []
    for _ in range(count):
        n = data[pos]
        types.append(data[pos + 1:pos + 1 + n].decode("utf-8"))
        pos += 1 + n
    length = struct.unpack("<i", data[pos:pos + 4])[0]
    grid = np.frombuffer(zlib.decompress(data[pos + 4:pos + 4 + length], -15), dtype=np.uint8).reshape(rows, cols)
    return grid, types


def fetch_terrarium(data_dir, zoom=6):
    """逐张下 Terrarium 瓦片，按每个像素的经纬度落进 0.05° 网格，累计格内均值、最高、最低；瓦片只在内存里过一遍。"""
    import urllib.request
    from concurrent.futures import ThreadPoolExecutor
    from PIL import Image
    tiles = 1 << zoom
    url = "https://s3.amazonaws.com/elevation-tiles-prod/terrarium/%d/%d/%d.png"

    def tile_x(lon):
        return int(math.floor((lon + 180) / 360 * tiles))

    def tile_y(lat):
        r = math.radians(lat)
        return int(math.floor((1 - math.log(math.tan(r) + 1 / math.cos(r)) / math.pi) / 2 * tiles))

    xs = range(tile_x(LON0 - STEP / 2), tile_x(LON0 + (COLS - 0.5) * STEP) + 1)
    ys = range(tile_y(LAT0 + (ROWS - 0.5) * STEP), tile_y(LAT0 - STEP / 2) + 1)

    def get(xy):
        for attempt in range(5):
            try:
                return xy, urllib.request.urlopen(url % (zoom, xy[0], xy[1]), timeout=60).read()
            except OSError:
                time.sleep(2 + attempt * 3)
        raise RuntimeError("瓦片下不来 %s" % (xy,))

    total = np.zeros((ROWS, COLS))
    count = np.zeros((ROWS, COLS), np.int32)
    top = np.full((ROWS, COLS), -1e9, np.float32)
    bottom = np.full((ROWS, COLS), 1e9, np.float32)
    got = 0
    pix = np.arange(256) + 0.5
    with ThreadPoolExecutor(8) as pool:
        for (x, y), data in pool.map(get, [(x, y) for y in ys for x in xs]):
            got += len(data)
            rgb = np.asarray(Image.open(io.BytesIO(data)).convert("RGB")).astype(np.float64)
            height = (rgb[..., 0] * 256 + rgb[..., 1] + rgb[..., 2] / 256 - 32768)[::-1]   # 行改成自南向北
            lon = (x * 256 + pix) / (256 * tiles) * 360 - 180
            lat = np.degrees(np.arctan(np.sinh(np.pi * (1 - 2 * (y * 256 + pix) / (256 * tiles)))))[::-1]
            cj = np.rint((lon - LON0) / STEP).astype(int)
            ci = np.rint((lat - LAT0) / STEP).astype(int)
            vc, vr = (cj >= 0) & (cj < COLS), (ci >= 0) & (ci < ROWS)
            if not vc.any() or not vr.any():
                continue
            h = height[np.ix_(vr, vc)]
            cj, ci = cj[vc], ci[vr]
            # 像素按经纬度单调落格：同一格的像素连成一段，按段归并
            sc = np.r_[0, np.nonzero(np.diff(cj))[0] + 1]
            sr = np.r_[0, np.nonzero(np.diff(ci))[0] + 1]
            at = np.ix_(ci[sr], cj[sc])
            total[at] += np.add.reduceat(np.add.reduceat(h, sc, axis=1), sr, axis=0)
            count[at] += np.diff(np.r_[sr, len(ci)])[:, None] * np.diff(np.r_[sc, len(cj)])[None, :]
            top[at] = np.maximum(top[at], np.maximum.reduceat(np.maximum.reduceat(h, sc, axis=1), sr, axis=0))
            bottom[at] = np.minimum(bottom[at], np.minimum.reduceat(np.minimum.reduceat(h, sc, axis=1), sr, axis=0))
    assert count.min() > 0, "有格子没落到像素"
    mean = (total / count).astype(np.float32)
    np.savez_compressed(data_dir + "/terrarium_z6.npz", mean=mean, max=top, min=bottom)
    print("Terrarium z%d：%d×%d 张、%.1f MB，存 %s/terrarium_z6.npz" % (zoom, len(xs), len(ys), got / 1e6, data_dir))


def build_terrarium(data_dir, old_path, out_path, preview_path=None):
    old, types = read_earth(old_path)
    assert types == TYPES and old.shape == (ROWS, COLS), "旧网格的编号表或大小对不上"
    dem = np.load(data_dir + "/terrarium_z6.npz")
    # 海陆、湖泊、近海深浅照旧(岸线一格不动)
    water = np.isin(old, [CODE["deep_ocean"], CODE["close_ocean"], CODE["shallow_waters"]])
    land = ~water
    water_codes = np.where(water, old, 0).astype(np.uint8)
    # 气候：旧网格平地的地表(沙地或生物群系)照旧；山、丘陵格没有，按同一海拔带里最近的平地补(约 220 公里内同带的优先，
    # 埃塞俄比亚、也门、亚美尼亚高原不会补成低处的稀树草原)
    surface_of = np.full(len(TYPES), -1, np.int8)
    surface_of[CODE["sand"]] = SURFACE["sand"]
    for b in BIOMES:
        surface_of[CODE["soil_low:%s_low" % b]] = surface_of[CODE["soil_high:%s_high" % b]] = SURFACE[b]
    surface = surface_of[old]
    known = land & (surface >= 0)
    lost = land & ~known
    f = relief_features(dem["mean"], dem["max"], dem["min"], water)
    donor = np.where(surface == SURFACE["sand"], SURFACE["desert"], surface)
    dist_any, (ai, aj) = ndimage.distance_transform_edt(~known, return_indices=True)
    filled = donor[ai, aj]
    for lo, hi in ((0, 600), (600, 1300), (1300, 2200), (2200, 3200), (3200, 1e9)):
        band = (f["s1"] >= lo) & (f["s1"] < hi)
        if not (known & band).any():
            continue
        dist, (bi, bj) = ndimage.distance_transform_edt(~(known & band), return_indices=True)
        use = lost & band & (dist <= dist_any + 40)
        filled[use] = donor[bi, bj][use]
    surface = np.where(known, surface, filled).astype(np.int8)

    ranges, plains = region_polygons(data_dir)
    mountains, hills = classify_relief(f, water, ranges, plains)
    opened = open_passes(f, water, mountains)
    mountains &= ~opened
    hills |= opened
    rough = mountains | hills
    # 以前是山丘、现在成了平地的格：大漠腹地补沙丘、旱区海岸补沙滩、旱区大河沿岸补可耕草地(同原来的路子)
    desert = lost & (surface == SURFACE["desert"])
    # 旧网格分不出海与湖：连成五千格以上的水面算海(大洋、里海、黑海)，咸海这类湖不算
    lab, _ = ndimage.label(water)
    big = np.bincount(lab.ravel()) >= 5000
    big[0] = False
    sea = big[lab]
    coast_far = ndimage.distance_transform_edt(~sea) > 8
    surface[desert & (local_relief(f) < 250) & coast_far & (dune_noise() > 0.2)] = SURFACE["sand"]
    surface[desert & ndimage.binary_dilation(sea, iterations=1)] = SURFACE["sand"]
    arid = lost & np.isin(surface, [SURFACE["desert"], SURFACE["savanna"], SURFACE["sand"]])
    river_lines = read_polylines(data_dir + "/ne/ne_10m_rivers_lake_centerlines.shp",
                                 data_dir + "/ne/ne_10m_rivers_lake_centerlines.dbf", 6)
    surface[oasis_mask(river_lines, f) & arid & ~rough] = SURFACE["grass"]
    out = compose(water_codes, water, surface, mountains, hills, f)
    assert (out[water] == old[water]).all()
    finish(out, out_path, preview_path)


def finish(out, out_path, preview_path):
    write(out, out_path)
    if preview_path:
        render_preview(out, preview_path)
    counts = np.bincount(out.ravel(), minlength=len(TYPES))
    for t, c in zip(TYPES, counts):
        if c:
            print("%-28s %8d" % (t, c))


def write(grid, path):
    payload = zlib.compressobj(9, zlib.DEFLATED, -15)
    body = payload.compress(grid.tobytes()) + payload.flush()
    with open(path, "wb") as f:
        f.write(b"ECEARTH1")
        f.write(struct.pack("<iifff", COLS, ROWS, LON0, LAT0, STEP))
        f.write(struct.pack("<i", len(TYPES)))
        for t in TYPES:
            b = t.encode("utf-8")
            f.write(struct.pack("<B", len(b)))
            f.write(b)
        f.write(struct.pack("<i", len(body)))
        f.write(body)
    print("wrote", path, len(body), "bytes compressed")


PREVIEW = {
    "deep_ocean": (40, 90, 190), "close_ocean": (55, 120, 220), "shallow_waters": (80, 170, 240),
    "sand": (240, 220, 150), "mountains": (90, 90, 90), "mountains:snow_block": (245, 245, 250),
    "hills": (130, 125, 110), "hills:snow_hills": (215, 220, 230),
    "grass": ((96, 160, 60), (70, 130, 45)), "savanna": ((200, 190, 90), (180, 160, 70)),
    "jungle": ((40, 120, 40), (30, 100, 30)), "desert": ((245, 225, 150), (225, 195, 110)),
    "permafrost": ((200, 215, 225), (180, 195, 210)), "birch": ((110, 150, 90), (90, 130, 75)),
    "maple": ((130, 160, 70), (110, 140, 60)), "swamp": ((90, 110, 60), (80, 100, 50)),
}


def preview_colors():
    lut = np.zeros((len(TYPES), 3), dtype=np.uint8)
    for i, t in enumerate(TYPES):
        if t in PREVIEW:
            lut[i] = PREVIEW[t]
        else:
            b = t.split(":")[1].rsplit("_", 1)
            lut[i] = PREVIEW[b[0]][0 if b[1] == "low" else 1]
    return lut


def render_preview(grid, path, box=None):
    """预览图；box=(西, 南, 东, 北) 时只画这一块(烘前烘后对比用)。"""
    from PIL import Image
    if box:
        w, s, e, n = box
        grid = grid[int(round((s - LAT0) / STEP)):int(round((n - LAT0) / STEP)),
                    int(round((w - LON0) / STEP)):int(round((e - LON0) / STEP))]
    img = Image.fromarray(preview_colors()[grid[::-1]])
    img.save(path)
    print("preview", path)


if __name__ == "__main__":
    args = sys.argv[1:]
    if args and args[0] == "fetch-terrarium":
        fetch_terrarium(args[1])
    elif args and args[0] == "terrarium":
        build_terrarium(args[1], args[2], args[3], args[4] if len(args) > 4 else None)
    elif len(args) >= 2:
        build(args[0], args[1], args[2] if len(args) > 2 else None)
    else:
        print(__doc__)

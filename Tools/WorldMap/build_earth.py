"""把公开地球数据烘成模组自带的地表网格 WorldMaps/earth.bytes。

数据来源(先下到临时目录，原始文件不进仓库)：
  - 高程：NOAA NCEI ETOPO 2022 60 角秒地表高程，THREDDS OPeNDAP 取经度 28°W~158°E，每 3 格取 1 格(0.05°)。
      北段  .../ETOPO_2022_v1_60s_N90W180_surface.nc.dods?z.z[4800:3:10200][9120:3:20280]  (纬度 10°S~80°N)
      南段  .../ETOPO_2022_v1_60s_N90W180_surface.nc.dods?z.z[3120:3:4797][9120:3:20280]   (纬度 38°S~10°S)
  - 海陆、湖泊：Natural Earth 1:10m ne_10m_land / ne_10m_minor_islands / ne_10m_lakes。
      里海、死海、吐鲁番这种低于海平面的地方光看高程会被当成海，海陆以它为准。
  - 河流：Natural Earth 1:10m ne_10m_rivers_lake_centerlines，只用来在旱区沿大河画出可耕的河谷绿洲
      (两河、尼罗河、印度河、阿姆河、塔里木、河套)，不画成水面——一格十来公里，画成水会把大陆切成岛。
  - 气候：Kottek 等 2006 柯本-盖格气候分区 0.5° 网格 Koeppen-Geiger-ASCII.txt。

用法：
  python -I build_earth.py <数据目录> <输出 earth.bytes> [预览.png]
数据目录里要有 etopo_z.dods、etopo_south.dods、ne/ne_10m_*.shp、kg/Koeppen-Geiger-ASCII.txt。

网格每格存一个地块编号(编号表写在文件头里，是原版存档用的地块写法，如 "soil_low:grass_low")，
游戏里按选的范围和大小逐格就近取样(见 Scripts/GeneralSystems/WorldMap/EarthTerrain.cs)。
"""
import struct
import sys
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


def read_dods(path, rows, cols):
    raw = open(path, "rb").read()
    at = raw.find(b"Data:\n") + 6
    n1, n2 = struct.unpack(">ii", raw[at:at + 8])
    assert n1 == n2 == rows * cols, (n1, rows, cols)
    return np.frombuffer(raw, dtype=">f4", count=n1, offset=at + 8).reshape(rows, cols).astype(np.float32)


def read_shapes(path):
    """最小的 .shp 读法：只认多边形(类型 5)，返回每个记录的环列表。"""
    data = open(path, "rb").read()
    pos = 100
    records = []
    while pos < len(data):
        _, length = struct.unpack(">ii", data[pos:pos + 8])
        content = data[pos + 8:pos + 8 + length * 2]
        pos += 8 + length * 2
        shape_type = struct.unpack("<i", content[:4])[0]
        if shape_type != 5:
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


def read_polylines(shp_path, dbf_path, max_rank):
    """折线(类型 3)，只取 scalerank 不超过 max_rank 的大河。返回 [(scalerank, [点阵, ...])]。"""
    dbf = open(dbf_path, "rb").read()
    count, header_len, record_len = struct.unpack("<IHH", dbf[4:12])
    fields, offset = [], 1
    for i in range(32, header_len - 1, 32):
        name = dbf[i:i + 11].split(b"\0")[0].decode()
        length = dbf[i + 16]
        fields.append((name, offset, length))
        offset += length
    rank_field = next(f for f in fields if f[0] == "scalerank")
    ranks = []
    for r in range(count):
        at = header_len + r * record_len + rank_field[1]
        text = dbf[at:at + rank_field[2]].strip()
        ranks.append(int(text) if text else 99)
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
    lat_n = int(round((LAT0 + ROWS * STEP - (LAT0)) / 0.5)) + 4
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
    lats = LAT0 + np.arange(ROWS) * STEP
    lons = LON0 + np.arange(COLS) * STEP
    # 边界抖一抖，免得 0.5° 的方块直接露出来
    rng = np.random.default_rng(7)

    def noise(sigma):
        n = ndimage.gaussian_filter(rng.normal(0, 1, (ROWS, COLS)), sigma)
        return n / n.std()

    # 两层噪声叠加(大块起伏 + 细碎边)，分区边界抖开约 0.3°
    jitter_lat = (noise(8) * 0.7 + noise(2) * 0.3) * 0.3
    jitter_lon = (noise(8) * 0.7 + noise(2) * 0.3) * 0.3
    li = np.clip(np.round((lats[:, None] + jitter_lat + 89.75) / 0.5), 0, len(lat_c) - 1).astype(int)
    lj = np.clip(np.round((lons[None, :] + jitter_lon + 179.75) / 0.5), 0, len(lon_c) - 1).astype(int)
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
    lats = LAT0 + np.arange(ROWS) * STEP

    # 局部起伏：格子高出周围(约 30 公里内)最低处多少；高原平坦处不算山
    land_elev = np.where(water, 0, np.maximum(elev, 0))
    # 水面取最近陆格的高度：高原湖(青藏一带四五千米)周围不会因为湖面算 0 而凭空起一圈山
    _, (wi, wj) = ndimage.distance_transform_edt(water, return_indices=True)
    land_elev = land_elev[wi, wj]
    local_min = ndimage.minimum_filter(land_elev, size=7)
    local_max = ndimage.maximum_filter(land_elev, size=5)
    relief = land_elev - local_min
    smooth = ndimage.gaussian_filter(land_elev, 1.0)

    out = np.zeros((ROWS, COLS), dtype=np.uint8)
    # 水：近岸两格内或浅于 150 米为浅水，深于 1500 米为深海
    coast_dist = ndimage.distance_transform_edt(water)
    depth = -elev
    lake_dist = coast_dist
    sea = water & ~lakes
    out[sea] = CODE["close_ocean"]
    out[sea & (depth > 1500) & (coast_dist > 4)] = CODE["deep_ocean"]
    out[sea & ((depth < 150) | (coast_dist <= 2))] = CODE["shallow_waters"]
    out[lakes] = CODE["shallow_waters"]
    out[lakes & (lake_dist > 4)] = CODE["close_ocean"]

    lons = LON0 + np.arange(COLS) * STEP
    lon2 = np.broadcast_to(lons[None, :], (ROWS, COLS))
    lat2 = np.broadcast_to(lats[:, None], (ROWS, COLS))
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
    for b in BIOMES:
        mask = land_only & (biome == b)
        out[mask & (smooth < 400)] = CODE["soil_low:%s_low" % b]
        out[mask & (smooth >= 400)] = CODE["soil_high:%s_high" % b]
    # 大漠腹地(撒哈拉、阿拉伯、塔克拉玛干、戈壁)：起伏小、离海远的沙漠里成片沙丘
    desert = land_only & np.isin(names, ["BWh", "BWk"])
    rng = np.random.default_rng(11)
    dunes_noise = ndimage.gaussian_filter(rng.normal(0, 1, (ROWS, COLS)), 5)
    dunes_noise /= dunes_noise.std()
    coast_far = ndimage.distance_transform_edt(~sea) > 8
    dunes = desert & (relief < 250) & coast_far & (dunes_noise > 0.2)
    out[dunes] = CODE["sand"]
    # 丘陵、山：看起伏，不只看海拔。本模组丘陵算崎岖地、不能耕，江南、岭南的低山丘陵大多仍算平地；两千米以上的高地都算丘陵
    hills = land_only & (((relief > 500) & (land_elev > 800)) | ((relief > 800) & (land_elev > 300)) | (land_elev > 2000))
    # 山脉：喜马拉雅、昆仑、天山、长白山、秦岭大巴山与龙门山(四川盆地四周)、阿尔卑斯、比利牛斯、喀尔巴阡、巴尔干、
    # 高加索、扎格罗斯、托罗斯……连成带：高处起伏大的格子算山，沿山脊加粗一格(只在 1200 米以上加粗)；
    # 谷地起伏小，仍是丘陵，留出山口
    peak = land_only & (((land_elev > 1400) & (relief > 750)) | ((land_elev > 2500) & (relief > 450)) |
                        ((land_elev > 5000) & (relief > 250)))
    mountains = land_only & (peak | (ndimage.binary_dilation(peak, iterations=1) & (land_elev > 1200)))
    snowy = (land_elev > 4500) | ((np.abs(lats[:, None]) > 50) & (land_elev > 1500))
    out[hills] = CODE["hills"]
    out[hills & snowy] = CODE["hills:snow_hills"]
    # 青藏高原面：3800 米以上一片雪丘(高寒、住不了人)，拉萨河谷、雅鲁藏布河谷低于此仍是原来的地表
    plateau = land_only & (smooth > 3800) & ~mountains
    out[plateau] = CODE["hills:snow_hills"]
    out[mountains] = CODE["mountains"]
    out[mountains & snowy] = CODE["mountains:snow_block"]
    # 海岸沙滩只在旱区：撒哈拉、阿拉伯沿岸
    coast_land = land_only & ndimage.binary_dilation(sea, iterations=1)
    out[coast_land & desert & ~hills & ~mountains] = CODE["sand"]
    # 旱区大河河谷：沙漠、草原里沿河画出可耕草地(两河、尼罗河、印度河、阿姆河、塔里木、河套)
    arid = land_only & np.isin(names, ["BWh", "BWk", "BSh", "BSk"])
    river_lines = read_polylines(data_dir + "/ne/ne_10m_rivers_lake_centerlines.shp",
                                 data_dir + "/ne/ne_10m_rivers_lake_centerlines.dbf", 6)
    rivers = rasterize_lines(river_lines)
    # 低平的冲积平原(海拔 150 米以下：两河之间、尼罗河三角洲、印度河下游、花剌子模)沿河再放宽约 30 公里
    wide = ndimage.binary_dilation(rivers, iterations=6) & (smooth < 150)
    oasis = (rivers | wide) & arid & ~mountains & ~hills
    out[oasis & (smooth < 400)] = CODE["soil_low:grass_low"]
    out[oasis & (smooth >= 400)] = CODE["soil_high:grass_high"]
    # 河道本身不烘进网格(一格宽的线按地图大小取样会断)：折线另存，游戏里按地图大小画水道、留渡口
    export_rivers(river_lines, out_path.replace("earth.bytes", "rivers.bytes"))

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


def render_preview(grid, path):
    from PIL import Image
    lut = np.zeros((len(TYPES), 3), dtype=np.uint8)
    for i, t in enumerate(TYPES):
        if t in PREVIEW:
            lut[i] = PREVIEW[t]
        else:
            b = t.split(":")[1].rsplit("_", 1)
            lut[i] = PREVIEW[b[0]][0 if b[1] == "low" else 1]
    img = Image.fromarray(lut[grid[::-1]])
    img.save(path)
    print("preview", path)


if __name__ == "__main__":
    build(sys.argv[1], sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else None)

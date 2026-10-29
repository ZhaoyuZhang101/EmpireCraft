"""世界地图无人区预演：照游戏里的取样、归类、连片，配上 WorldMaps/wilderness.json 的真实地名，画成 PNG 看一眼。

用法：python -I preview_wilderness.py <模组目录> <输出目录> [--frame eurasia|oldworld|all] [--size 0|1|2] [--era 时代id] [--list]
  - 取样照 Scripts/GeneralSystems/WorldMap/EarthTerrain.cs：按范围与大小逐格就近取 earth.bytes，再按 rivers.bytes 画大河水道(留渡口)；
  - 归类、连片照 Scripts/GeneralSystems/WildernessSystem.cs(阈值常量照抄，改了那边这里也要改)：
    一块地 8×8 格，水面过四分之一、陆地不到 16 格的不算；山脉、沙漠冻原、沼泽丛林的门槛同 Classify；
    同类型四邻相连成核心，再按 Regroup 的外沿放宽往外收(大的先长，这一类地形占陆地过半的地块并进来，长到同类的小片整片并进来)，
    最后至少 4 块成一片；图上外沿收进来的地块淡一些、打斜线；
  - 起名照 Scripts/GeneralSystems/WorldMap/WorldMapWilderness.cs：地块换成经纬度，找同类型、与范围(锚点半径或经纬框)
    重合得最多的地理单元(盖住的块数占两者并集的比例，盖住区域过半的优先)；同一单元被拆成几片的，盖得最多的那片用原名，别的加方位(东段/东部、Eastern)，再撞名加"余脉""边缘"；
  - 给了 --era：剧本的城心那块留给城(同 Regroup 的 anchored，近似为城址所在那块)，名字按本时代年份取旧称、按最近城的文化取文化叫法，
    区域旁注史实归属(时代文件的 wilderness 一节)；不给则按现代名；
  - --list 只列区域(类型、块数、中心经纬度、配上的单元)，不画图。
中文字体用系统里的文泉驿(WenQuanYi)，找不到就用 PIL 默认字体(中文显示成方块)。
"""
import json
import math
import os
import struct
import sys
import zlib

import numpy as np

# ---- 地图范围(照 EarthTerrain.Frames) ----
FRAMES = {
    "eurasia": dict(west=-25.0, east=153.0, top=77.5, center=None, sizes=[(16, 7), (32, 15), (40, 18)]),
    "oldworld": dict(west=-26.0, east=155.0, top=None, center=21.0, sizes=[(14, 9), (28, 18), (34, 22)]),
}

# ---- 归类阈值(照抄 WildernessSystem 的常量) ----
MAX_WATER = 0.25
MIN_LAND = 16
MOUNTAIN_CORE, MOUNTAIN_FLANK, MOUNTAIN_RUGGED = 0.5, 0.35, 0.75
BARREN_SHARE, SWAMP_SHARE, JUNGLE_SHARE = 0.85, 0.75, 0.85
MIN_ZONES = 4
EDGE_SHARE = 0.5   # 外沿放宽：这一类地形占陆地过半的地块往外收(WildernessSystem.EdgeShare)
MOUNTAIN_EDGE_SHARE = 0.6   # 山脉高一档(WildernessSystem.MountainEdgeShare)
ZONE = 8

# ---- 拆片命名、匹配(照抄 WorldMapWilderness 的常量) ----
SNAP_DEGREES = 2.5
MAJOR_SHARE = 0.5
DEFAULT_RADIUS = 3.0

KINDS = {1: "mountain", 2: "desert", 3: "tundra", 4: "jungle", 5: "swamp"}
KIND_ZH = {1: "山脉", 2: "沙漠", 3: "冻原", 4: "雨林", 5: "大泽"}

# 地块写法 → 标记(照 ComputeMarks：水、山、丘陵、冻土、沙、丛林、沼泽)；"主:顶"的顶层看自己，主地块看 under
WATER, ROCK, HILLS, FROZEN, SAND, JUNGLE, SWAMP = 1, 2, 4, 8, 16, 32, 64


def type_marks(name):
    if name in ("deep_ocean", "close_ocean", "shallow_waters"):
        return WATER
    if name == "mountains":
        return ROCK
    if name == "hills":
        return HILLS
    if name == "sand":
        return SAND
    if name.startswith("snow"):
        return FROZEN
    biome = name.rsplit("_", 1)[0]
    return {"desert": SAND, "permafrost": FROZEN, "jungle": JUNGLE, "swamp": SWAMP}.get(biome, 0)


def read_earth(path):
    data = open(path, "rb").read()
    assert data[:8] == b"ECEARTH1"
    cols, rows = struct.unpack("<ii", data[8:16])
    lon0, lat0, step = struct.unpack("<fff", data[16:28])
    count = struct.unpack("<i", data[28:32])[0]
    pos, types = 32, []
    for _ in range(count):
        n = data[pos]
        types.append(data[pos + 1:pos + 1 + n].decode())
        pos += 1 + n
    length = struct.unpack("<i", data[pos:pos + 4])[0]
    grid = np.frombuffer(zlib.decompress(data[pos + 4:pos + 4 + length], -15), dtype=np.uint8).reshape(rows, cols)
    return grid, types, float(lon0), float(lat0), float(step)


def read_rivers(path):
    data = open(path, "rb").read()
    assert data[:8] == b"ECRIVER1"
    count = struct.unpack("<i", data[8:12])[0]
    pos, rivers = 12, []
    for _ in range(count):
        rank, n = struct.unpack("<BH", data[pos:pos + 3])
        pos += 3
        pts = np.frombuffer(data, dtype="<i2", count=n * 2, offset=pos).reshape(-1, 2) / 100.0
        pos += n * 4
        rivers.append((rank, pts))
    return rivers


def bounds(frame, size):
    w, h = size[0] * 64, size[1] * 64
    dpt = (frame["east"] - frame["west"]) / w
    span = h * dpt
    north = frame["center"] + span / 2 if frame["top"] is None else frame["top"]
    return w, h, dpt, north - span


def sample(root, frame, size):
    """返回 (格子的地块编号网格[y, x]，y=0 最南, 地块写法表, dpt, bottom)。"""
    grid, types, lon0, lat0, step = read_earth(os.path.join(root, "WorldMaps", "earth.bytes"))
    w, h, dpt, bottom = bounds(frame, size)
    xs = frame["west"] + (np.arange(w) + 0.5) * dpt
    ys = bottom + (np.arange(h) + 0.5) * dpt
    cols = np.rint((xs - lon0) / step).astype(int)
    rows = np.rint((ys - lat0) / step).astype(int)
    deep = types.index("deep_ocean")
    out = np.full((h, w), deep, dtype=np.uint8)
    cin = (cols >= 0) & (cols < grid.shape[1])
    rin = (rows >= 0) & (rows < grid.shape[0])
    sub = grid[np.ix_(rows[rin], cols[cin])]
    out[np.ix_(np.where(rin)[0], np.where(cin)[0])] = sub
    # 大河水道(照 EarthTerrain.DrawRivers：等级 0~3 两格宽，每走 14 格留 2 格渡口)
    shallow = types.index("shallow_waters")
    liquid = np.array([type_marks(t.split(":")[-1]) & WATER != 0 for t in types])
    for rank, pts in read_rivers(os.path.join(root, "WorldMaps", "rivers.bytes")):
        thickness = 2 if rank <= 3 else 1
        walked = 0
        for i in range(1, len(pts)):
            x0 = math.floor((pts[i - 1][0] - frame["west"]) / dpt)
            y0 = math.floor((pts[i - 1][1] - bottom) / dpt)
            x1 = math.floor((pts[i][0] - frame["west"]) / dpt)
            y1 = math.floor((pts[i][1] - bottom) / dpt)
            dx, dy = abs(x1 - x0), -abs(y1 - y0)
            sx, sy = (1 if x0 < x1 else -1), (1 if y0 < y1 else -1)
            err = dx + dy
            while True:
                if walked % 14 >= 2:
                    for t in range(thickness):
                        x, y = x0 + (0 if dy == 0 else t), y0 + (t if dy == 0 else 0)
                        if 1 <= x < w - 1 and 1 <= y < h - 1 and not liquid[out[y, x]]:
                            out[y, x] = shallow
                walked += 1
                if x0 == x1 and y0 == y1:
                    break
                e2 = 2 * err
                if e2 >= dy:
                    err += dy
                    x0 += sx
                if e2 <= dx:
                    err += dx
                    y0 += sy
    return out, types, dpt, bottom


def zone_counts(tiles, types):
    """逐块数格子(照 WildernessSystem.Count)：返回 {水, 山, 丘, 冻, 沙, 丛林, 沼泽, 陆地} 各一张 [zy, zx] 网格。"""
    top = np.array([type_marks(t.split(":")[-1]) for t in types])
    under = np.array([type_marks(t.split(":")[0]) if ":" in t else 0 for t in types])
    h, w = tiles.shape
    zh, zw = h // ZONE, w // ZONE
    m = top[tiles[:zh * ZONE, :zw * ZONE]]
    u = under[tiles[:zh * ZONE, :zw * ZONE]]
    both = m | u

    def count(mask):
        return mask.reshape(zh, ZONE, zw, ZONE).sum(axis=(1, 3))

    water = (both & WATER) != 0
    rock = ~water & ((both & ROCK) != 0)
    hills = ~water & ~rock & ((both & HILLS) != 0)
    frozen = ~water & ((m & FROZEN) != 0)
    sand = ~water & ~frozen & ((m & SAND) != 0)
    jungle = ~water & ~frozen & ~sand & ((m & JUNGLE) != 0)
    swamp = ~water & ~frozen & ~sand & ~jungle & ((m & SWAMP) != 0)
    c = {k: count(a) for k, a in (("water", water), ("rock", rock), ("hills", hills), ("frozen", frozen), ("sand", sand),
                                  ("jungle", jungle), ("swamp", swamp))}
    c["land"] = ZONE * ZONE - c["water"]
    return c


def classify(tiles, types, c=None):
    """逐块归类(照 WildernessSystem.Classify，开局没有上回的类型)：返回 [zy, zx] 的类型编号(0 不是)。"""
    c = c or zone_counts(tiles, types)
    nw, nr, nh, nf, ns, nj, nsw, land = (c[k] for k in ("water", "rock", "hills", "frozen", "sand", "jungle", "swamp", "land"))
    total = ZONE * ZONE
    kind = np.zeros(land.shape, dtype=np.int8)
    ok = (land >= MIN_LAND) & (nw <= total * MAX_WATER)
    mountain = ok & ((nr >= land * MOUNTAIN_CORE) | ((nr >= land * MOUNTAIN_FLANK) & (nr + nh >= land * MOUNTAIN_RUGGED)))
    barren = ok & ~mountain & (nf + ns >= land * BARREN_SHARE)
    swampy = ok & ~mountain & ~barren & (nsw >= land * SWAMP_SHARE)
    junglish = ok & ~mountain & ~barren & ~swampy & (nj + nsw >= land * JUNGLE_SHARE)
    kind[mountain] = 1
    kind[barren & (nf > ns)] = 3
    kind[barren & (nf <= ns)] = 2
    kind[swampy] = 5
    kind[junglish & (nj >= nsw)] = 4
    kind[junglish & (nj < nsw)] = 5
    return kind


def regions_of(kind, anchored, c):
    """照 WildernessSystem.Regroup(开局，没有旧区域)：同类型四邻连成核心；大的先长，往四邻收没被别片占、不留给城、
    陆地够 MIN_LAND、这一类地形占陆地过半(山脉算山+丘，沙漠冻原算沙+冻，雨林大泽算丛林+沼泽；水面不设上限)的地块，
    收到收不动；长到同类、还没长过的核心整片并进来；最后去掉不到 MIN_ZONES 块的。
    返回 ([(类型, [(zx, zy), ...])], [外沿收进来的块集合])，大的在前。"""
    zh, zw = kind.shape
    piece = -np.ones((zh, zw), dtype=np.int32)
    groups = []
    for sy in range(zh):
        for sx in range(zw):
            k = kind[sy, sx]
            if k == 0 or piece[sy, sx] >= 0 or anchored[sy, sx]:
                continue
            mark = len(groups)
            stack, cells = [(sx, sy)], []
            piece[sy, sx] = mark
            while stack:
                x, y = stack.pop()
                cells.append((x, y))
                for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                    if 0 <= nx < zw and 0 <= ny < zh and piece[ny, nx] < 0 and kind[ny, nx] == k and not anchored[ny, nx]:
                        piece[ny, nx] = mark
                        stack.append((nx, ny))
            groups.append([int(k), cells, set()])
    share = {1: c["rock"] + c["hills"], 2: c["sand"] + c["frozen"], 3: c["sand"] + c["frozen"],
             4: c["jungle"] + c["swamp"], 5: c["jungle"] + c["swamp"]}
    grown = [False] * len(groups)
    from collections import deque
    for g in sorted(range(len(groups)), key=lambda i: -len(groups[i][1])):
        k, cells, edge = groups[g]
        if not cells:
            continue
        grown[g] = True
        queue = deque(cells)
        while queue:
            x, y = queue.popleft()
            for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                if not (0 <= nx < zw and 0 <= ny < zh):
                    continue
                other = piece[ny, nx]
                if other >= 0:
                    if other == g or grown[other] or groups[other][0] != k:
                        continue
                    for cell in groups[other][1]:
                        piece[cell[1], cell[0]] = g
                        cells.append(cell)
                        queue.append(cell)
                    edge |= groups[other][2]
                    groups[other][1] = []
                    grown[other] = True
                    continue
                if anchored[ny, nx] or c["land"][ny, nx] < MIN_LAND or share[k][ny, nx] < c["land"][ny, nx] * (MOUNTAIN_EDGE_SHARE if k == 1 else EDGE_SHARE):
                    continue
                piece[ny, nx] = g
                cells.append((nx, ny))
                edge.add((nx, ny))
                queue.append((nx, ny))
    result = sorted((g for g in groups if len(g[1]) >= MIN_ZONES), key=lambda r: -len(r[1]))
    return [(k, cells) for k, cells, _ in result], [edge for _, _, edge in result]


# ---------------- 地理单元：范围、匹配、起名(照 WorldMapWilderness) ----------------

def load_units(root):
    with open(os.path.join(root, "WorldMaps", "wilderness.json"), encoding="utf-8") as f:
        units = json.load(f)["units"]
    for u in units:
        u.setdefault("box", [])
        u.setdefault("pts", [])
        u.setdefault("r", DEFAULT_RADIUS)
        u.setdefault("alt", [])
        u.setdefault("c", {})
        west = south = float("inf")
        east = north = float("-inf")
        area, slon, slat, parts = 0.0, 0.0, 0.0, 0
        for b in u["box"]:
            west, south, east, north = min(west, b[0]), min(south, b[1]), max(east, b[2]), max(north, b[3])
            area += (b[2] - b[0]) * (b[3] - b[1])
            slon += (b[0] + b[2]) / 2
            slat += (b[1] + b[3]) / 2
            parts += 1
        r = u["r"]
        for p in u["pts"]:
            pad = r / max(0.15, math.cos(math.radians(min(89.0, abs(p[1]) + r))))
            west, east = min(west, p[0] - pad), max(east, p[0] + pad)
            south, north = min(south, p[1] - r), max(north, p[1] + r)
            area += math.pi * r * r / max(0.15, math.cos(math.radians(abs(p[1]))))
            slon += p[0]
            slat += p[1]
            parts += 1
        u["_ext"] = (west, south, east, north)
        u["_area"] = area
        u["_center"] = (slon / max(1, parts), slat / max(1, parts))
    return units


def covers(u, lon, lat):
    w, s, e, n = u["_ext"]
    if lon < w or lon > e or lat < s or lat > n:
        return False
    for b in u["box"]:
        if b[0] <= lon <= b[2] and b[1] <= lat <= b[3]:
            return True
    squeeze, r2 = math.cos(math.radians(lat)), u["r"] * u["r"]
    for p in u["pts"]:
        dx, dy = (lon - p[0]) * squeeze, lat - p[1]
        if dx * dx + dy * dy <= r2:
            return True
    return False


def distance_to(u, lon, lat):
    squeeze, best = math.cos(math.radians(lat)), float("inf")
    for b in u["box"]:
        dx = max(0.0, b[0] - lon, lon - b[2]) * squeeze
        dy = max(0.0, b[1] - lat, lat - b[3])
        best = min(best, math.hypot(dx, dy))
    for p in u["pts"]:
        best = min(best, max(0.0, math.hypot((lon - p[0]) * squeeze, lat - p[1]) - u["r"]))
    return best


def in_years(spec, year):
    spec = spec.strip()
    if not spec:
        return True
    if "~" not in spec:
        return year <= int(spec)
    a, b = spec.split("~", 1)
    return (not a.strip() or year >= int(a)) and (not b.strip() or year <= int(b))


def entry_name(entry, year):
    parts = entry.split("|")
    if len(parts) == 3:
        return parts[0]
    if len(parts) >= 4 and in_years(parts[0], year):
        return parts[1]
    return None


def names_of(u, culture, year):
    names = []
    for key, entries in u["c"].items():
        if culture and culture in [k.strip() for k in key.split(",")]:
            names += [entry_name(e, year) for e in entries]
    names += [entry_name(e, year) for e in u["alt"]]
    names.append(u["zh"])
    out = []
    for n in names:
        if n and n not in out:
            out.append(n)
    return out


DIRECTION = {("east", True): "{0}东段", ("west", True): "{0}西段", ("north", True): "{0}北段", ("south", True): "{0}南段",
             ("east", False): "{0}东部", ("west", False): "{0}西部", ("north", False): "{0}北部", ("south", False): "{0}南部"}


def direction(dlon, dlat, lat):
    dx = dlon * math.cos(math.radians(lat))
    if abs(dx) < 0.3 and abs(dlat) < 0.3:
        return None
    if abs(dx) >= abs(dlat):
        return "east" if dx > 0 else "west"
    return "north" if dlat > 0 else "south"


def match_regions(regions, units, lonlat, zone_deg):
    """每片区域对上的单元：[(单元, 盖住块数, 距离, 中心经, 中心纬)]，以及各单元盖得最多的那片的下标。"""
    matches, main = [], {}
    for idx, (k, cells) in enumerate(regions):
        pts = [lonlat(x, y) for x, y in cells]
        clon = sum(p[0] for p in pts) / len(pts)
        clat = sum(p[1] for p in pts) / len(pts)
        cand = [u for u in units if KINDS[k] in u["k"]]
        best, best_count, best_score, best_dist = None, 0, 0.0, float("inf")
        counts = [sum(1 for p in pts if covers(u, p[0], p[1])) for u in cand]
        # 有单元盖住区域过半的，只在这些里挑
        major = any(c >= len(pts) * MAJOR_SHARE for c in counts)
        for u, count in zip(cand, counts):
            if count <= 0 or major and count < len(pts) * MAJOR_SHARE:
                continue
            # 重合度：盖住的块数 / (区域块数 + 单元范围折成的块数 - 盖住的块数)
            score = count / max(1.0, len(pts) + u["_area"] / (zone_deg * zone_deg) - count)
            d = distance_to(u, clon, clat)
            if best is not None and (score < best_score or score == best_score and (u["_area"] > best["_area"] or
                                                                                    u["_area"] == best["_area"] and d >= best_dist)):
                continue
            best, best_count, best_score, best_dist = u, count, score, d
        if best is None:
            stride = max(1, len(pts) // 64)
            for u in cand:
                d = min(distance_to(u, p[0], p[1]) for p in pts[::stride])
                if d <= SNAP_DEGREES and d < best_dist:
                    best, best_dist = u, d
        if best is None:
            matches.append(None)
            continue
        m = (best, best_count, 0.0 if best_count else best_dist, clon, clat, len(cells), idx)
        matches.append(m)
        old = main.get(best["id"])
        if old is None or (m[1], -m[2], m[5], -m[6]) > (old[1], -old[2], old[5], -old[6]):
            main[best["id"]] = m
    return matches, main


def name_regions(regions, matches, main, culture_of, year):
    """照 Regroup 的次序(大的先)起名；没对上的标"未配"。"""
    used, names = set(), [None] * len(regions)
    order = sorted(range(len(regions)), key=lambda i: (-len(regions[i][1]), i))
    for i in order:
        m = matches[i]
        if m is None:
            names[i] = None
            continue
        u = m[0]
        cands = names_of(u, culture_of(i), year)
        chosen = None
        if main.get(u["id"]) is m:
            chosen = next((n for n in cands if n not in used), None)
        if chosen is None:
            first = cands[0]
            ref = u["_center"]
            mm = main.get(u["id"])
            if mm is not None and mm is not m:
                ref = (mm[3], mm[4])
            else:
                for j, other in enumerate(names):
                    if other == first and j != i and matches[j] is not None:
                        ref = (matches[j][3], matches[j][4])
                        break
            mountain = regions[i][0] == 1
            d = direction(m[3] - ref[0], m[4] - ref[1], m[4])
            if d is not None and DIRECTION[(d, mountain)].format(first) not in used:
                chosen = DIRECTION[(d, mountain)].format(first)
            elif (first + ("余脉" if mountain else "边缘")) not in used:
                chosen = first + ("余脉" if mountain else "边缘")
        names[i] = chosen
        if chosen:
            used.add(chosen)
    return names


# ---------------- 开局整块荒漠冻土(照 WorldMapZones.Classify)：留空却没划成无人区的数一数 ----------------

def zones_barren(tiles, types):
    h, w = tiles.shape
    zh, zw = h // ZONE, w // ZONE
    tops = [t.split(":")[-1] for t in types]
    mains = [t.split(":")[0] for t in types]
    liquid = np.array([type_marks(t) & WATER != 0 for t in tops]) | np.array([type_marks(t) & WATER != 0 for t in mains])
    mountain = np.array([m == "mountains" for m in mains])
    frozen = np.array([t in ("snow_hills", "snow_block") or t.startswith("permafrost") for t in tops])
    desert = np.array([t == "sand" or t.startswith("desert") for t in tops])

    def count(lut):
        return lut[tiles].reshape(zh, ZONE, zw, ZONE).sum(axis=(1, 3))

    land = ZONE * ZONE - count(liquid)
    nm, nf, nd = count(mountain & ~liquid), count(frozen & ~liquid), count(desert & ~liquid)
    barren = (land >= 32) & (nd + nf > 0) & (nd + nf >= 0.85 * (land - nm))
    mountainous = (land >= 32) & (nm >= 0.2 * land)
    return barren & ~mountainous


# ---------------- 剧本(--era)：城心、文化、史实归属 ----------------

def load_era(root, era_id, frame, size, size_index):
    with open(os.path.join(root, "WorldMaps", "cities.json"), encoding="utf-8") as f:
        base = json.load(f)
    with open(os.path.join(root, "WorldMaps", "eras", era_id + ".json"), encoding="utf-8") as f:
        era = json.load(f)
    w, h, dpt, bottom = bounds(frame, size)
    year = era["year"]
    regions = {r["id"]: r for r in base["regions"]}
    ranks = era.get("ranks", {})
    seats = []
    for c in base["cities"]:
        if not (c.get("from", -99999) <= year <= c.get("to", 99999)) or c["r"] not in regions:
            continue
        rank = ranks.get(c["id"], c.get("k", 2))
        if size_index == 0 and rank > 2:
            continue
        x, y = math.floor((c["x"] - frame["west"]) / dpt), math.floor((c["y"] - bottom) / dpt)
        if x < 4 or y < 4 or x >= w - 4 or y >= h - 4:
            continue
        culture = era.get("culture", {}).get(c["r"], regions[c["r"]].get("c", ""))
        seats.append((x, y, culture, c["id"]))
    polity_name = {p["id"]: p["zh"] for p in era["polities"]}
    owners = {u: polity_name.get(p, p) for u, p in era.get("wilderness", {}).items()}
    return era, year, seats, owners


def render(tiles, types, kind, regions, edges, names, matches, owners_of, seats, path, title):
    from PIL import Image, ImageDraw, ImageFont
    base = {
        "deep_ocean": (40, 90, 190), "close_ocean": (55, 120, 220), "shallow_waters": (80, 170, 240),
        "sand": (240, 220, 150), "mountains": (90, 90, 90), "mountains:snow_block": (245, 245, 250),
        "hills": (130, 125, 110), "hills:snow_hills": (215, 220, 230),
    }
    biome = {"grass": (96, 160, 60), "savanna": (200, 190, 90), "jungle": (40, 120, 40), "desert": (235, 215, 140),
             "permafrost": (200, 215, 225), "birch": (110, 150, 90), "maple": (130, 160, 70), "swamp": (90, 110, 60)}
    lut = np.zeros((len(types), 3), dtype=np.float32)
    for i, t in enumerate(types):
        lut[i] = base.get(t) or biome.get(t.split(":")[-1].rsplit("_", 1)[0], (128, 128, 128))
    img = lut[tiles]
    tint = {1: (128, 112, 96), 2: (214, 170, 100), 3: (180, 200, 235), 4: (30, 90, 40), 5: (80, 100, 60)}
    zh, zw = kind.shape
    region_of = -np.ones((zh, zw), dtype=np.int32)
    for i, (k, cells) in enumerate(regions):
        for x, y in cells:
            region_of[y, x] = i
    up = np.kron(region_of, np.ones((ZONE, ZONE), dtype=np.int32))
    # 外沿收进来的地块淡一些、打斜线(看收得对不对)
    grown = np.zeros((zh, zw), dtype=bool)
    for cells in edges:
        for x, y in cells:
            grown[y, x] = True
    grown_up = np.kron(grown, np.ones((ZONE, ZONE), dtype=bool))
    yy, xx = np.indices(up.shape)
    hatch = grown_up & ((xx + yy) % 4 == 0)
    for i, (k, cells) in enumerate(regions):
        mask = up == i
        color = np.array(tint[k], dtype=np.float32) if names[i] else np.array((220, 40, 40), dtype=np.float32)
        core = mask & ~grown_up
        img[core] = img[core] * 0.35 + color * 0.65
        outer = mask & grown_up
        img[outer] = img[outer] * 0.65 + color * 0.35
        img[mask & hatch] = img[mask & hatch] * 0.45
    edge = np.zeros_like(up, dtype=bool)
    edge[:-1, :] |= up[:-1, :] != up[1:, :]
    edge[:, :-1] |= up[:, :-1] != up[:, 1:]
    edge &= (up >= 0) | np.roll(up >= 0, -1, axis=0) | np.roll(up >= 0, -1, axis=1)
    img[edge] = img[edge] * 0.4
    picture = Image.fromarray(np.clip(img[::-1], 0, 255).astype(np.uint8))
    draw = ImageDraw.Draw(picture)
    font_path = next((p for p in ("/usr/share/fonts/truetype/wqy/wqy-zenhei.ttc", "/usr/share/fonts/truetype/wqy/wqy-microhei.ttc")
                      if os.path.exists(p)), None)

    def font(size):
        return ImageFont.truetype(font_path, size) if font_path else ImageFont.load_default()

    h = tiles.shape[0]
    for x, y, _, _ in seats:
        draw.ellipse((x - 2, h - 1 - y - 2, x + 2, h - 1 - y + 2), fill=(200, 0, 0))
    for i, (k, cells) in enumerate(regions):
        cx = sum(c[0] for c in cells) / len(cells) * ZONE + ZONE / 2
        cy = sum(c[1] for c in cells) / len(cells) * ZONE + ZONE / 2
        best = min(cells, key=lambda c: (c[0] * ZONE + 4 - cx) ** 2 + (c[1] * ZONE + 4 - cy) ** 2)
        px, py = best[0] * ZONE + 4, h - 1 - (best[1] * ZONE + 4)
        label = names[i] or ("未配·" + KIND_ZH[k])
        owner = owners_of(i)
        if owner:
            label += "→" + owner
        size = 22 if len(cells) >= 300 else 16 if len(cells) >= 40 else 12
        f = font(size)
        width = draw.textlength(label, font=f)
        draw.text((px - width / 2, py - size / 2), label, font=f, fill=(20, 20, 20), stroke_width=2, stroke_fill=(255, 255, 255))
    f = font(26)
    draw.text((12, 10), title, font=f, fill=(0, 0, 0), stroke_width=3, stroke_fill=(255, 255, 255))
    picture.save(path)
    print("预演图", path)


def preview(root, frame_id, size_index, era_id, out_path, list_only):
    frame = FRAMES[frame_id]
    size = frame["sizes"][size_index]
    tiles, types, dpt, bottom = sample(root, frame, size)
    counts = zone_counts(tiles, types)
    kind = classify(tiles, types, counts)
    zh, zw = kind.shape
    anchored = np.zeros_like(kind, dtype=bool)
    year, seats, owners, era = 2000, [], {}, None
    if era_id:
        era, year, seats, owners = load_era(root, era_id, frame, size, size_index)
        for x, y, _, _ in seats:
            anchored[y // ZONE, x // ZONE] = True
    regions, edges = regions_of(kind, anchored, counts)
    units = load_units(root)
    unit_by_id = {u["id"]: u for u in units}

    def lonlat(zx, zy):
        return frame["west"] + (zx * ZONE + 4.5) * dpt, bottom + (zy * ZONE + 4.5) * dpt

    def culture_of(i):
        if not seats:
            return ""
        cells = regions[i][1]
        cx = sum(c[0] for c in cells) / len(cells) * ZONE + 4
        cy = sum(c[1] for c in cells) / len(cells) * ZONE + 4
        return min(seats, key=lambda s: (s[0] - cx) ** 2 + (s[1] - cy) ** 2)[2]

    def owners_of(i):
        if not owners:
            return ""
        k = regions[i][0]
        got = {}
        for zx, zy in regions[i][1]:
            lon, lat = lonlat(zx, zy)
            best = None
            for uid in owners:
                u = unit_by_id.get(uid)
                if u is None or KINDS[k] not in u["k"] or not covers(u, lon, lat):
                    continue
                if best is None or u["_area"] < best["_area"]:
                    best = u
            if best is not None:
                got[owners[best["id"]]] = got.get(owners[best["id"]], 0) + 1
        return "/".join(name for name, _ in sorted(got.items(), key=lambda kv: -kv[1]))

    matches, main = match_regions(regions, units, lonlat, ZONE * dpt)
    names = name_regions(regions, matches, main, culture_of, year)
    barren = zones_barren(tiles, types)
    wild = np.zeros_like(kind, dtype=bool)
    for _, cells in regions:
        for x, y in cells:
            wild[y, x] = True
    holes = int((barren & ~wild & ~anchored).sum())
    total = sum(len(c) for _, c in regions)
    print("== %s %s %s：%d 片 %d 块无人区(外沿收进 %d 块)，对上地名 %d 片，没对上 %d 片；开局整块荒漠冻土留空却没成无人区 %d 块" % (
        frame_id, size, era_id or "现代", len(regions), total, sum(len(e) for e in edges), sum(1 for n in names if n),
        sum(1 for n in names if not n), holes))
    for i, (k, cells) in enumerate(regions):
        lon, lat = (matches[i][3], matches[i][4]) if matches[i] else lonlat(*cells[0])
        unit = matches[i][0]["id"] if matches[i] else "-"
        print("  %s %5d 块(外沿 %4d) (%6.1f,%5.1f) %-22s %s %s" % (KIND_ZH[k], len(cells), len(edges[i]), lon, lat, unit,
                                                              names[i] or "(未配)", ("→" + owners_of(i)) if owners else ""))
    if not list_only:
        title = "无人区预演 · %s %d×%d · %s" % (frame_id, size[0], size[1], (era["zh"] + " %d" % year) if era else "现代地名")
        render(tiles, types, kind, regions, edges, names, matches, owners_of, seats, out_path, title)


def main():
    args = sys.argv[1:]
    if len(args) < 2:
        print(__doc__)
        return
    root, out = args[0], args[1]
    frame_ids, size_index, era_id, list_only = list(FRAMES), 1, None, False
    i = 2
    while i < len(args):
        if args[i] == "--frame":
            frame_ids = list(FRAMES) if args[i + 1] == "all" else [args[i + 1]]
            i += 2
        elif args[i] == "--size":
            size_index = int(args[i + 1])
            i += 2
        elif args[i] == "--era":
            era_id = args[i + 1]
            i += 2
        elif args[i] == "--list":
            list_only = True
            i += 1
        else:
            raise SystemExit("不认识的参数 " + args[i])
    for frame_id in frame_ids:
        path = out
        if len(frame_ids) > 1 or era_id:
            stem, ext = os.path.splitext(out)
            path = "%s-%s%s%s" % (stem, frame_id, ("-" + era_id) if era_id else "", ext or ".png")
        preview(root, frame_id, size_index, era_id, path, list_only)


if __name__ == "__main__":
    main()

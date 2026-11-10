"""重出 WorldMaps/rivers.bytes：Natural Earth 1:10m 河道中线里 scalerank 0~5 的大河(build_earth 原来的口径)，
再加一张史上常作分界、影响版图的名河(等级 6~9，原口径里没有)。用户 2026-11-11："东北的河流没了""剧本模式几个能影响领土形状的河流"
"不这样子分我的雄鸡地图怎么自然形成"——黑龙江有了，乌苏里江、松花江、嫩江、辽河、鸭绿江、图们江都没有，淮河、汉水、渭河、汾河、
湘江、赣江也没有；欧洲的波河、罗讷河、泰晤士河、顿河、德涅斯特河、台伯河、威悉河等同样缺。

0~5 级照现有 rivers.bytes 原样保留(build_earth 当初从 shapefile 出的)，名河存成原等级(6~9)追加在后面，重跑不会重复。
名河按名字加经纬框认(同名的汉江/韩国汉江、岷江/闽江、西布格/南布格、俄罗斯两条奥卡河靠框分开)；整条要，不按框裁。
数据(只用来生成，原文件不进仓库)：
  https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_10m_rivers_lake_centerlines.geojson
用法：python rivers_source.py ROOT ne_10m_rivers_lake_centerlines.geojson [--list]
  --list 只列出名河各认到几段，不写文件
"""
import json
import re
import struct
import sys

import numpy as np

# 与 build_earth 的地表网格同一范围(export_rivers 只留网格范围内的段)
STEP = 0.05
LON0 = -179.991666667 + 9120 / 60.0
LAT0 = -89.991666667 + 3120 / 60.0
COLS = 3721
ROWS = 560 + 1801
MAX_RANK = 5

# (名字或正则, 西, 南, 东, 北, 中文)
NAMED = [
    # 东北、蒙古
    ("Sungari", 123, 44, 133, 48.5, "松花江"),
    (r"Di.er Songhua|Songhua", 124, 41, 128, 45.5, "第二松花江"),
    ("Nen", 121, 45, 127, 51.5, "嫩江"),
    ("Liao", 119, 40, 125, 44, "辽河"),
    ("Xiliao", 117, 42, 124, 44.5, "西辽河"),
    ("Xar Moron", 116, 42, 121, 44.5, "西拉木伦河"),
    ("Yalu", 124, 39.5, 128.5, 42.2, "鸭绿江"),
    ("Tumen", 128, 41.5, 131, 43.5, "图们江"),
    ("Ussuri|Wusuli", 131, 44, 135.5, 48.6, "乌苏里江"),
    ("Shilka", 113, 50, 122, 54, "石勒喀河"),
    ("Onon", 107, 47, 116, 51.5, "鄂嫩河"),
    ("Kerulen", 107, 46, 118, 49.5, "克鲁伦河"),
    ("Orhon", 100, 46, 107, 51, "鄂尔浑河"),
    # 华北、关中
    ("Huai", 112, 31.5, 120, 34.5, "淮河"),
    ("Han", 106, 29.5, 115, 34, "汉江"),
    ("Wei", 103.5, 33.5, 110.5, 36, "渭河"),
    ("Jing", 106, 34, 109, 37, "泾河"),
    ("Fen", 110.5, 35, 113, 39, "汾河"),
    ("Hai", 116, 38.5, 118, 39.5, "海河"),
    ("Yongding", 113, 39, 117, 41, "永定河"),
    ("Sanggan", 112, 39, 115.5, 41, "桑干河"),
    ("Luan", 115, 39, 119.5, 42.5, "滦河"),
    # 江南、西南、岭南
    ("Gan", 113.5, 24.5, 117, 29.8, "赣江"),
    ("Xiang", 110, 24.5, 114, 29.5, "湘江"),
    ("Yuan", 107, 25.5, 112.5, 29.5, "沅江"),
    ("Jialing", 104, 29.5, 108, 34.5, "嘉陵江"),
    ("Min", 102.5, 28.5, 104.5, 33.5, "岷江"),
    ("Min", 116, 25, 120, 28.5, "闽江"),
    ("Wu", 104, 26, 109, 30, "乌江"),
    ("Dadu", 100.5, 28.5, 103.5, 33, "大渡河"),
    ("Yalong", 97, 26, 102.5, 34, "雅砻江"),
    ("Bei", 112, 22.5, 114.5, 25.5, "北江"),
    ("Dong", 113.5, 22.5, 116, 25, "东江"),
    ("Yu", 107.5, 22.5, 111.5, 24, "郁江"),
    ("You", 105.5, 22.5, 108.5, 24.5, "右江"),
    ("Zuo", 106, 21.5, 108.5, 23, "左江"),
    ("Fuchun", 118, 29, 121, 30.5, "富春江"),
    ("Red", 100, 20, 107, 25, "红河"),
    # 朝鲜、日本
    ("Han", 126, 37, 129.5, 38.3, "汉江(朝鲜)"),
    ("Namhan", 127, 36.5, 129.5, 37.6, "南汉江"),
    ("Nakdong", 127.5, 34.9, 129.5, 37.2, "洛东江"),
    ("Tone", 138.5, 35.5, 141, 37, "利根川"),
    ("Ishikari", 141, 42.8, 143.5, 44, "石狩川"),
    # 南亚(五河、恒河支流、德干)
    ("Sutlej", 70, 28, 79, 33, "萨特莱杰河"),
    ("Chenab", 70, 29, 77, 34, "杰纳布河"),
    ("Jhelum", 71, 31, 76, 35, "杰赫勒姆河"),
    ("Beas", 74.5, 31, 77.5, 32.5, "比亚斯河"),
    ("Yamuna", 76, 24.5, 82, 31.5, "亚穆纳河"),
    (r"Gh.gh?ara", 79.5, 25, 85, 30.5, "格格拉河"),
    ("Chambal", 74, 22.5, 79.5, 27, "昌巴尔河"),
    ("Narmada", 72.5, 21, 82, 23.5, "讷尔默达河"),
    ("Tapi", 72.5, 20.5, 78.5, 22.5, "达布蒂河"),
    ("Krishna", 73, 15.5, 81.5, 18.5, "克里希纳河"),
    ("Tungabhadra", 75, 14.5, 78.5, 16.5, "栋格珀德拉河"),
    ("Cauvery", 75.5, 10.5, 80, 13, "高韦里河"),
    ("Helmand", 61, 29.5, 69, 35, "赫尔曼德河"),
    ("Harirud", 60, 34, 66, 36.8, "哈里河"),
    # 中南半岛
    (r"(Chao )?Phraya", 99.5, 13, 101, 16, "湄南河"),
    ("Ping", 98, 15.5, 100.5, 19.5, "滨河"),
    ("Chindwin", 93.5, 21.5, 96.5, 27, "钦敦江"),
    ("Sittong", 95.5, 16.5, 97, 21, "锡当河"),
    # 西亚
    ("Jordan", 35.3, 31, 36, 33.5, "约旦河"),
    (r"K.z.l.rmak", 32.5, 38.5, 37.5, 42, "克孜勒河"),
    ("Sakarya", 29, 39, 32.5, 41.3, "萨卡里亚河"),
    ("Aras", 41, 38.5, 49.5, 41, "阿拉斯河"),
    ("Kura", 43, 39.5, 50, 42.5, "库拉河"),
    ("Karkheh", 46.5, 31, 49, 34.5, "卡尔黑河"),
    ("Atrak", 53.5, 37, 59, 38.5, "阿特雷克河"),
    # 欧洲
    ("Po", 6.5, 44.5, 13, 46, "波河"),
    ("Adige", 10.5, 45, 12.5, 47, "阿迪杰河"),
    ("Tiber", 11.5, 41.5, 13, 43.9, "台伯河"),
    (r"Rh.?ne", 3.5, 43, 8.5, 47, "罗讷河"),
    ("Garonne", -1.5, 42.5, 2, 45.2, "加龙河"),
    ("Duero", -9, 40.5, -2, 42.2, "杜罗河"),
    ("Guadiana", -8, 37, -2, 39.5, "瓜迪亚纳河"),
    ("Guadalquivir", -7, 36.5, -2.5, 38.5, "瓜达尔基维尔河"),
    ("Thames", -2.5, 51, 1, 52.2, "泰晤士河"),
    ("Severn", -4, 51.3, -1.5, 53, "塞文河"),
    ("Shannon", -10, 52, -7.5, 54.2, "香农河"),
    ("Weser", 7.5, 51, 10, 54, "威悉河"),
    ("Maas", 3.5, 48, 6.5, 52, "默兹河"),
    ("Schelde", 2.5, 49.8, 4.5, 51.5, "斯海尔德河"),
    ("Mosel", 5.5, 48, 7.5, 50.5, "摩泽尔河"),
    ("Main", 7.5, 49.5, 12, 50.5, "美因河"),
    ("Inn", 9.5, 47, 14, 49, "因河"),
    ("Sava", 13.5, 44, 20.5, 46.5, "萨瓦河"),
    ("Drau|Drava", 12.5, 45.5, 19, 47, "德拉瓦河"),
    ("Tisa|Tisza", 19, 44.8, 24.5, 48.5, "蒂萨河"),
    ("Morava", 20, 42.5, 22.8, 44.8, "大摩拉瓦河"),
    ("Drina", 18.5, 42.5, 19.7, 45, "德里纳河"),
    ("Maritsa", 24, 40.5, 27, 42.5, "马里查河"),
    ("Prut", 26, 45, 28.5, 48.5, "普鲁特河"),
    ("Dniester", 22.5, 45.5, 30.5, 50, "德涅斯特河"),
    ("Southern Bug", 26, 46.5, 32.5, 50, "南布格河"),
    ("Bug", 21, 50, 26, 53.5, "西布格河"),
    ("Pripyat", 23.5, 51, 30.5, 52.5, "普里皮亚季河"),
    ("Desna", 30, 50.5, 35.5, 54.5, "杰斯纳河"),
    ("Don", 37, 46.5, 45, 54, "顿河"),
    ("Donets", 35, 47, 41, 51, "北顿涅茨河"),
    ("Oka", 33, 53, 44, 56.5, "奥卡河"),
    ("Kuban", 37, 44, 42.5, 46, "库班河"),
    ("Nemunas", 21, 53.5, 26, 55.5, "涅曼河"),
    ("Narva", 27, 57.5, 28.5, 59.5, "纳尔瓦河"),
]


def simplify(points, tolerance):
    """Douglas-Peucker 抽稀(同 build_earth.simplify)。"""
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


def segments(feature):
    geom = feature.get("geometry") or {}
    if geom.get("type") == "LineString":
        return [np.asarray(geom["coordinates"], float)[:, :2]]
    if geom.get("type") == "MultiLineString":
        return [np.asarray(line, float)[:, :2] for line in geom["coordinates"] if len(line) >= 2]
    return []


def named_entry(feature, segs):
    p = feature["properties"]
    names = {p.get("name") or "", p.get("name_en") or ""} - {""}
    for index, (pattern, w, s, e, n, _) in enumerate(NAMED):
        if not any(re.fullmatch(pattern, name) for name in names):
            continue
        if any(((seg[:, 0] >= w) & (seg[:, 0] <= e) & (seg[:, 1] >= s) & (seg[:, 1] <= n)).any() for seg in segs):
            return index
    return None


def read_records(path):
    data = open(path, "rb").read()
    assert data[:8] == b"ECRIVER1", path
    count, pos, records = struct.unpack_from("<i", data, 8)[0], 12, []
    for _ in range(count):
        rank, n = data[pos], struct.unpack_from("<H", data, pos + 1)[0]
        pts = np.frombuffer(data, dtype="<i2", count=n * 2, offset=pos + 3).reshape(-1, 2) / 100.0
        records.append((rank, pts))
        pos += 3 + 4 * n
    return records


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    root, source = sys.argv[1], sys.argv[2]
    listing = "--list" in sys.argv[3:]
    path = root + "/WorldMaps/rivers.bytes"
    features = json.load(open(source, encoding="utf-8"))["features"]
    west, east, south, north = LON0, LON0 + COLS * STEP, LAT0, LAT0 + ROWS * STEP
    # 0~5 级照现有文件原样留着(build_earth 从 shapefile 出的；GeoJSON 版本略有出入，不动已有河道)，只重加名河
    records = [r for r in read_records(path) if r[0] <= MAX_RANK]
    hits = [0] * len(NAMED)
    for feature in features:
        segs = segments(feature)
        if not segs:
            continue
        rank = int(feature["properties"].get("scalerank") or 99)
        if rank <= MAX_RANK:
            continue
        index = named_entry(feature, segs)
        if index is None:
            continue
        hits[index] += 1
        for seg in segs:
            inside = (seg[:, 0] > west) & (seg[:, 0] < east) & (seg[:, 1] > south) & (seg[:, 1] < north)
            if inside.sum() < 2:
                continue
            pts = simplify(seg[inside], 0.02)
            if len(pts) >= 2:
                records.append((min(rank, 255), pts))
    missing = [NAMED[i][5] for i, h in enumerate(hits) if h == 0]
    for (pattern, *_ , zh), h in zip(NAMED, hits):
        if listing:
            print(zh, pattern, h)
    if missing:
        print("没认到的名河", missing)
    if listing:
        return
    with open(path, "wb") as f:
        f.write(b"ECRIVER1")
        f.write(struct.pack("<i", len(records)))
        for rank, pts in records:
            f.write(struct.pack("<BH", rank, len(pts)))
            f.write(np.round(pts * 100).astype("<i2").tobytes())
    print("rivers", path, len(records), "lines", sum(len(p) for _, p in records), "points",
          "named", sum(1 for h in hits if h), "/", len(NAMED))


if __name__ == "__main__":
    main()

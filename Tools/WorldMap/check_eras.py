"""核对 WorldMaps/cities.json 与 eras/*.json，并按游戏里同样的分城规则出每个时代的格局预览图。

用法：python -I check_eras.py <模组目录> [earth 预览 png] [输出目录]
分城规则同 Scripts/GeneralSystems/WorldMap/WorldMapScenario.cs：
  国家点名的城先归它；其余的城归"认领了该地区、都城最近"的国家；谁都不认领的城各自独立。
  游戏里"最近"按区块地形代价算(翻山、过漠、跨海更远，见 WorldMapZones)，这里预览按直线近似。
"""
import json
import math
import os
import sys


def load(mod_dir):
    with open(os.path.join(mod_dir, "WorldMaps", "cities.json"), encoding="utf-8") as f:
        base = json.load(f)
    eras = []
    era_dir = os.path.join(mod_dir, "WorldMaps", "eras")
    for name in sorted(os.listdir(era_dir)):
        if name.endswith(".json"):
            with open(os.path.join(era_dir, name), encoding="utf-8") as f:
                eras.append(json.load(f))
    return base, eras


def active(city, year):
    return city.get("from", -99999) <= year <= city.get("to", 99999)


def name_at(city, year):
    for entry in city.get("alt", []):
        until, zh = entry.split("|")[:2]
        if year <= int(until):
            return zh
    return city["zh"]


def dist(a, b):
    lat = math.radians((a["y"] + b["y"]) / 2)
    return math.hypot((a["x"] - b["x"]) * math.cos(lat), a["y"] - b["y"])


def plan(base, era):
    year = era["year"]
    regions = {r["id"]: r for r in base["regions"]}
    cities = {c["id"]: c for c in base["cities"] if active(c, year)}
    owner = {}
    problems = []
    polities = era["polities"]
    ids = {p["id"] for p in polities}
    for p in polities:
        if p["cap"] not in cities:
            problems.append("%s 都城 %s 不存在或该年不在" % (p["id"], p["cap"]))
        for cid in p.get("cities", []):
            if cid not in cities:
                problems.append("%s 点名的城 %s 不存在或该年不在" % (p["id"], cid))
            elif cid in owner and owner[cid] != p["id"]:
                problems.append("城 %s 被 %s 和 %s 同时点名" % (cid, owner[cid], p["id"]))
            else:
                owner[cid] = p["id"]
        for rid in p.get("r", []):
            if rid not in regions:
                problems.append("%s 认领的地区 %s 不存在" % (p["id"], rid))
        if p.get("of") and p["of"] not in ids:
            problems.append("%s 的宗主 %s 不存在" % (p["id"], p["of"]))
        if p["cap"] in cities and p["cap"] not in owner:
            owner[p["cap"]] = p["id"]
        elif p["cap"] in owner and owner[p["cap"]] != p["id"]:
            problems.append("%s 的都城 %s 归了 %s" % (p["id"], p["cap"], owner[p["cap"]]))
    for core in era.get("cores", []):
        for rid in core["r"]:
            if rid not in regions:
                problems.append("核心 %s 的地区 %s 不存在" % (core["id"], rid))
        if core["cap"] not in cities:
            problems.append("核心 %s 的首府 %s 不存在" % (core["id"], core["cap"]))
    for rid in era.get("culture", {}):
        if rid not in regions:
            problems.append("文化改写的地区 %s 不存在" % rid)
    for cid, city in cities.items():
        if cid in owner:
            continue
        best, best_d = None, 1e9
        for p in polities:
            if city["r"] in p.get("r", []) and p["cap"] in cities:
                d = dist(city, cities[p["cap"]])
                if d < best_d:
                    best, best_d = p["id"], d
        if best:
            owner[cid] = best
    return cities, owner, problems


def render(base, era, cities, owner, preview, out_path):
    from PIL import Image, ImageDraw, ImageFont
    img = Image.open(preview).convert("RGB")
    # 预览图网格：经度 -27.99 起每格 0.05°，纬度自北 80.0 起
    lon0, lat_top, step = -27.991666667, -37.991666667 + 2360 * 0.05, 0.05
    w, h = img.size
    img = img.crop((0, 0, w, int((lat_top + 10) / step)))
    img = img.resize((img.width // 2, img.height // 2))
    draw = ImageDraw.Draw(img)
    try:
        font = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 11)
    except OSError:
        font = None
    import random
    colors = {}
    pol = {p["id"]: p for p in era["polities"]}

    def color(pid):
        if pid not in colors:
            rnd = random.Random(hash(pid) & 0xffff)
            colors[pid] = (rnd.randint(30, 255), rnd.randint(30, 255), rnd.randint(30, 255))
        return colors[pid]

    for cid, city in cities.items():
        px = (city["x"] - lon0) / step / 2
        py = (lat_top - city["y"]) / step / 2
        pid = owner.get(cid)
        c = color(pid) if pid else (255, 255, 255)
        r = 5 if city.get("k", 2) == 1 else 3
        draw.ellipse([px - r, py - r, px + r, py + r], fill=c, outline=(0, 0, 0))
    for p in era["polities"]:
        cap = cities.get(p["cap"])
        if not cap:
            continue
        px = (cap["x"] - lon0) / step / 2
        py = (lat_top - cap["y"]) / step / 2
        draw.text((px + 6, py - 6), p["zh"], fill=(0, 0, 0), font=font)
    img.save(out_path)


def main():
    mod_dir = sys.argv[1]
    preview = sys.argv[2] if len(sys.argv) > 2 else None
    out_dir = sys.argv[3] if len(sys.argv) > 3 else None
    base, eras = load(mod_dir)
    ids = [c["id"] for c in base["cities"]]
    dup = {i for i in ids if ids.count(i) > 1}
    if dup:
        print("重复的城 id:", dup)
    regions = {r["id"] for r in base["regions"]}
    for c in base["cities"]:
        if c["r"] not in regions:
            print("城 %s 的地区 %s 不存在" % (c["id"], c["r"]))
    for era in eras:
        cities, owner, problems = plan(base, era)
        free = [name_at(cities[c], era["year"]) for c in cities if c not in owner]
        print("== %s %s：%d 城，%d 国，独立 %d 城" % (era["id"], era["year"], len(cities), len(era["polities"]), len(free)))
        for p in problems:
            print("   !", p)
        if free:
            print("   独立：", " ".join(free))
        if preview and out_dir:
            render(base, era, cities, owner, preview, os.path.join(out_dir, "era_%s.png" % era["id"]))


if __name__ == "__main__":
    main()

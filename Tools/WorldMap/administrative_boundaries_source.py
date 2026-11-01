"""Import the dated DARMC Roman provincial polygons, preserving islands and holes.

Only the matching 117 CE preset is updated. Other eras continue to use their own
historical seats and terrain; these polygons are never reused as later borders.
Usage: python administrative_boundaries_source.py ROOT [--input downloaded.geojson]
"""
import argparse
import hashlib
import json
import pathlib
import urllib.request

SERVICE = ("https://services1.arcgis.com/qN3V93cYGMKQCOxL/arcgis/rest/services/"
           "DARMC_Roman_World/FeatureServer/14")
QUERY = SERVICE + "/query?where=1%3D1&outFields=*&outSR=4326&f=geojson&maxAllowableOffset=0.04"
# Labels match the preset's administrative names, not the shared kingdom-level titles.
NAMES = {
    "ITALIA": "_core", "ACHAIA": "亚该亚", "AEGYPTUS": "埃及", "AFRICA PROCONSULARIS": "阿非利加",
    "AQUITANIA": "阿基坦高卢", "ARABIA": "阿拉伯", "ASIA": "亚细亚", "BAETICA": "贝提卡",
    "BELGICA": "比利时高卢", "BITHYNIA ET PONTUS": "比提尼亚与本都", "BRITANNIA": "不列颠尼亚",
    "CILICIA": "西里西亚", "CRETA ET CYRENE": "克里特与昔兰尼加", "CYPRUS": "塞浦路斯",
    "DACIA": "达契亚", "DALMATIA": "达尔马提亚", "GERMANIA INFERIOR": "下日耳曼尼亚",
    "GERMANIA SUPERIOR": "上日耳曼尼亚", "IUDAEA": "犹太", "LUGDUNENSIS": "卢格敦高卢",
    "LUSITANIA": "卢西塔尼亚", "MACEDONIA": "马其顿", "MAURETANIA CAESARIENSIS": "凯撒里亚毛里塔尼亚",
    "MAURETANIA TINGITANA": "丁吉塔纳毛里塔尼亚", "MOESIA INFERIOR": "默西亚", "MOESIA SUPERIOR": "默西亚",
    "NARBONENSIS": "纳博讷高卢", "NORGIUM": "诺里库姆", "NUMIDIA": "努米底亚",
    "PANNONIA INFERIOR": "潘诺尼亚", "PANNONIA SUPERIOR": "潘诺尼亚", "RAETIA": "雷蒂亚",
    "SARDINIA ET CORSICA": "撒丁与科西嘉", "SICILIA": "西西里", "SYRIA": "叙利亚",
    "TARRACONENSIS": "塔拉科西班牙", "THRACIA": "色雷斯",
}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=pathlib.Path)
    parser.add_argument("--input", type=pathlib.Path)
    args = parser.parse_args()
    raw = args.input.read_bytes() if args.input else urllib.request.urlopen(QUERY, timeout=45).read()
    data = json.loads(raw)
    if data.get("type") != "FeatureCollection" or data.get("exceededTransferLimit"):
        raise SystemExit("Incomplete or invalid source GeoJSON")
    era = json.loads((args.root / "WorldMaps/eras/roman_empire.json").read_text(encoding="utf-8"))
    rome = next(p for p in era["polities"] if p["id"] == "rome")
    valid = {d["name"].split("|")[0] for d in rome.get("divisions", [])} | {"_core"}
    groups = {}
    for feature in data["features"]:
        name = " ".join(feature["properties"]["PROV_NAME"].split())
        division = NAMES.get(name)
        if division is None:
            continue  # Combined source units are not invented subdivisions of the preset.
        if division not in valid:
            raise SystemExit("Preset has no division: " + division)
        geometry = feature["geometry"]
        polygons = geometry["coordinates"] if geometry["type"] == "MultiPolygon" else [geometry["coordinates"]]
        groups.setdefault(division, []).extend(polygons)
    rows = []
    for division, polygons in sorted(groups.items()):
        rounded = [[[[round(float(v), 5) for v in point[:2]] for point in ring] for ring in polygon] for polygon in polygons]
        rows.append(dict(era="roman_empire", polity="rome", division=division, year=117,
                         precision="approximate", source=SERVICE, polygons=rounded))
    output = dict(
        description="Dated historical administrative areas. Uncovered presets use seats and terrain; no cross-era substitution.",
        attribution="Matthew Polk / DARMC, Harvard University; Richard J. A. Talbert (ed.), Barrington Atlas of the Greek and Roman World (2000).",
        license="CC BY-NC-SA 4.0 (DARMC / Mapping Past Societies)",
        source_url=QUERY, source_sha256=hashlib.sha256(raw).hexdigest(),
        simplification_degrees=0.04, boundaries=rows)
    path = args.root / "WorldMaps/administrative_boundaries.json"
    path.write_text(json.dumps(output, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")
    print(f"Wrote {len(rows)} dated administrative areas, {path.stat().st_size} bytes")


if __name__ == "__main__":
    main()

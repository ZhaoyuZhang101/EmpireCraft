"""cities.json 各城坐标对维基数据(CC0，2026-10-29 用它校过一遍：偏得远的多是同名异地，真改了萨那、提婆、毗耶陀补罗、东古拉)。
只用 SPARQL(搜索接口容易被限频)。按英文名、英文旧称、中文名精确匹配标签与别名，
取有坐标(P625)的候选里离我们坐标最近的。
用法：python -I -X utf8 check_coords.py <模组目录> <输出.json>；输出：{id: {zh, en, ours, qid, label, wd, km}}"""
import json
import math
import sys
import time
import urllib.parse
import urllib.request

root, out = sys.argv[1], sys.argv[2]
UA = "EmpireCraftMod-worldmap-check/1.2 (coordinate verification against Wikidata, CC0)"


def sparql(query):
    data = urllib.parse.urlencode({"query": query, "format": "json"}).encode()
    for attempt in range(6):
        try:
            req = urllib.request.Request("https://query.wikidata.org/sparql", data=data,
                                         headers={"User-Agent": UA, "Accept": "application/sparql-results+json"})
            with urllib.request.urlopen(req, timeout=90) as resp:
                return json.loads(resp.read().decode("utf-8")).get("results", {}).get("bindings", [])
        except Exception as e:
            print("retry", attempt, e, flush=True)
            time.sleep(8 + attempt * 6)
    return []


def km(a, b):
    lon1, lat1, lon2, lat2 = map(math.radians, (a[0], a[1], b[0], b[1]))
    d = math.sin((lat2 - lat1) / 2) ** 2 + math.cos(lat1) * math.cos(lat2) * math.sin((lon2 - lon1) / 2) ** 2
    return 6371 * 2 * math.asin(math.sqrt(min(1, d)))


def lit(text, lang):
    return '"%s"@%s' % (text.replace("\\", "\\\\").replace('"', '\\"'), lang)


data = json.load(open(root + "/WorldMaps/cities.json", encoding="utf-8"))
names = {}  # (text, lang) → [city ids]
for c in data["cities"]:
    keys = [(c["en"], "en"), (c["zh"], "zh")]
    for alt in c.get("alt", []):
        parts = alt.split("|")
        if len(parts) >= 3:
            keys += [(parts[2], "en"), (parts[1], "zh")]
    for k in keys:
        if k[0]:
            names.setdefault(k, []).append(c["id"])
keys = list(names)
points = {}  # (text, lang) → [(lon, lat, qid, label)]
for i in range(0, len(keys), 40):
    chunk = keys[i:i + 40]
    values = " ".join(lit(t, l) for t, l in chunk)
    query = """SELECT ?name ?item ?coord WHERE {
      VALUES ?name { %s }
      { ?item rdfs:label ?name } UNION { ?item skos:altLabel ?name }
      ?item wdt:P625 ?coord .
    }""" % values
    for b in sparql(query):
        name = b["name"]["value"]
        lang = b["name"].get("xml:lang", "")
        wkt = b["coord"]["value"]
        try:
            lon, lat = map(float, wkt[wkt.index("(") + 1:wkt.index(")")].split())
        except ValueError:
            continue
        qid = b["item"]["value"].rsplit("/", 1)[1]
        points.setdefault((name, lang), []).append((lon, lat, qid))
    print("batch", i // 40, len(points), flush=True)
    time.sleep(1.5)

result = {}
for c in data["cities"]:
    ours = [c["x"], c["y"]]
    best = None
    keys = [(c["en"], "en"), (c["zh"], "zh")]
    for alt in c.get("alt", []):
        parts = alt.split("|")
        if len(parts) >= 3:
            keys += [(parts[2], "en"), (parts[1], "zh")]
    for k in keys:
        for lon, lat, qid in points.get(k, []):
            d = km(ours, [lon, lat])
            if best is None or d < best[0]:
                best = (d, qid, k[0], [lon, lat])
    result[c["id"]] = {"zh": c["zh"], "en": c["en"], "ours": ours, "qid": best[1] if best else None,
                       "label": best[2] if best else None, "wd": best[3] if best else None,
                       "km": round(best[0], 1) if best else None}
json.dump(result, open(out, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
far = sum(1 for r in result.values() if r["km"] is not None and r["km"] > 60)
none = sum(1 for r in result.values() if r["km"] is None)
print("done", len(result), "far", far, "none", none)

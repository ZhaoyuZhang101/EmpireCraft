"""近代剧本(1937 日军侵华)的共用省界：中国按现行省级区划，苏联按 1983 年的加盟共和国与俄罗斯境内的边疆区、州、自治共和国。

用户 2026-11-10 给了审图号 GS(2016)1600 的中国地图与 1983 年苏联行政区划图："剧本地图的行政区划请严格遵照这个来"。
省界是硬边界：一省里的地只归这一省里的城(哪一方的城都算)，省内照离哪座城近分(见 WorldMapScenario.ConformSharedBorders)；
本国都城所在的那一省以外，近代国家在哪一省有城就在哪一省设行政区(见 WorldMapScenario.SharedDivisions)。

数据(只用来生成，不随模组发布原文件)：
  · 中国：阿里云 DataV.GeoAtlas 全国省级 https://geo.datav.aliyun.com/areas_v3/bound/100000_full.json
  · 苏联：Natural Earth ne_10m_admin_1_states_provinces.geojson(公有领域)
    https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_10m_admin_1_states_provinces.geojson
    俄罗斯的自治州、自治区照 1983 年并回所属的边疆区、州(哈卡斯并入克拉斯诺亚尔斯克，犹太自治州并入哈巴罗夫斯克……)，
    车臣、印古什合为车臣-印古什自治共和国，克里米亚、塞瓦斯托波尔归乌克兰；别的加盟共和国整个一块。名称照 1983 年(高尔基州、古比雪夫州、加里宁州)。

用法：python modern_administration_source.py ROOT --china 100000_full.json --ne ne_10m_admin_1_states_provinces.geojson
需要 shapely；游戏只读生成的 JSON。
"""
import argparse
import json
import pathlib

from shapely.geometry import MultiPolygon, Polygon, mapping, shape
from shapely.ops import unary_union

from preset_tools import T

ERA, YEAR = "sino_japanese", 1937

CHINA_EN = {
    "北京市": "Beijing", "天津市": "Tianjin", "河北省": "Hebei", "山西省": "Shanxi", "内蒙古自治区": "Inner Mongolia",
    "辽宁省": "Liaoning", "吉林省": "Jilin", "黑龙江省": "Heilongjiang", "上海市": "Shanghai", "江苏省": "Jiangsu",
    "浙江省": "Zhejiang", "安徽省": "Anhui", "福建省": "Fujian", "江西省": "Jiangxi", "山东省": "Shandong", "河南省": "Henan",
    "湖北省": "Hubei", "湖南省": "Hunan", "广东省": "Guangdong", "广西壮族自治区": "Guangxi", "海南省": "Hainan",
    "重庆市": "Chongqing", "四川省": "Sichuan", "贵州省": "Guizhou", "云南省": "Yunnan", "西藏自治区": "Tibet",
    "陕西省": "Shaanxi", "甘肃省": "Gansu", "青海省": "Qinghai", "宁夏回族自治区": "Ningxia", "新疆维吾尔自治区": "Xinjiang",
    "台湾省": "Taiwan", "香港特别行政区": "Hong Kong", "澳门特别行政区": "Macau",
}

# 俄罗斯：Natural Earth 名 → (1983 年中文名, 英文名)；字符串值 = 并入那一项
RUSSIA = {
    "Adygey": "Krasnodar", "Gorno-Altay": "Altay", "Ingush": "Chechnya", "Karachay-Cherkess": "Stavropol'", "Khakass": "Krasnoyarsk",
    "Yevrey": "Khabarovsk", "Chukchi Autonomous Okrug": "Maga Buryatdan", "Nenets": "Arkhangel'sk", "Khanty-Mansiy": "Tyumen'",
    "Yamal-Nenets": "Tyumen'", "Moskva": "Moskovskaya", "City of St. Petersburg": "Leningrad",
    "Altay": ("阿尔泰边疆区", "Altai Krai"), "Amur": ("阿穆尔州", "Amur Oblast"),
    "Arkhangel'sk": ("阿尔汉格尔斯克州", "Arkhangelsk Oblast"), "Astrakhan'": ("阿斯特拉罕州", "Astrakhan Oblast"),
    "Bashkortostan": ("巴什基尔自治共和国", "Bashkir ASSR"), "Belgorod": ("别尔哥罗德州", "Belgorod Oblast"),
    "Bryansk": ("布良斯克州", "Bryansk Oblast"), "Buryat": ("布里亚特自治共和国", "Buryat ASSR"),
    "Chechnya": ("车臣-印古什自治共和国", "Chechen-Ingush ASSR"), "Chelyabinsk": ("车里雅宾斯克州", "Chelyabinsk Oblast"),
    "Chita": ("赤塔州", "Chita Oblast"), "Chuvash": ("楚瓦什自治共和国", "Chuvash ASSR"),
    "Dagestan": ("达吉斯坦自治共和国", "Dagestan ASSR"), "Irkutsk": ("伊尔库茨克州", "Irkutsk Oblast"),
    "Ivanovo": ("伊万诺沃州", "Ivanovo Oblast"), "Kabardin-Balkar": ("卡巴尔达-巴尔卡尔自治共和国", "Kabardino-Balkar ASSR"),
    "Kaliningrad": ("加里宁格勒州", "Kaliningrad Oblast"), "Kalmyk": ("卡尔梅克自治共和国", "Kalmyk ASSR"),
    "Kaluga": ("卡卢加州", "Kaluga Oblast"), "Kamchatka": ("堪察加州", "Kamchatka Oblast"),
    "Karelia": ("卡累利阿自治共和国", "Karelian ASSR"), "Kemerovo": ("克麦罗沃州", "Kemerovo Oblast"),
    "Khabarovsk": ("哈巴罗夫斯克边疆区", "Khabarovsk Krai"), "Kirov": ("基洛夫州", "Kirov Oblast"),
    "Komi": ("科米自治共和国", "Komi ASSR"), "Kostroma": ("科斯特罗马州", "Kostroma Oblast"),
    "Krasnodar": ("克拉斯诺达尔边疆区", "Krasnodar Krai"), "Krasnoyarsk": ("克拉斯诺亚尔斯克边疆区", "Krasnoyarsk Krai"),
    "Kurgan": ("库尔干州", "Kurgan Oblast"), "Kursk": ("库尔斯克州", "Kursk Oblast"),
    "Leningrad": ("列宁格勒州", "Leningrad Oblast"), "Lipetsk": ("利佩茨克州", "Lipetsk Oblast"),
    "Maga Buryatdan": ("马加丹州", "Magadan Oblast"), "Mariy-El": ("马里自治共和国", "Mari ASSR"),
    "Mordovia": ("莫尔多瓦自治共和国", "Mordovian ASSR"), "Moskovskaya": ("莫斯科州", "Moscow Oblast"),
    "Murmansk": ("摩尔曼斯克州", "Murmansk Oblast"), "Nizhegorod": ("高尔基州", "Gorky Oblast"),
    "North Ossetia": ("北奥塞梯自治共和国", "North Ossetian ASSR"), "Novgorod": ("诺夫哥罗德州", "Novgorod Oblast"),
    "Novosibirsk": ("新西伯利亚州", "Novosibirsk Oblast"), "Omsk": ("鄂木斯克州", "Omsk Oblast"),
    "Orel": ("奥廖尔州", "Oryol Oblast"), "Orenburg": ("奥伦堡州", "Orenburg Oblast"), "Penza": ("奔萨州", "Penza Oblast"),
    "Perm'": ("彼尔姆州", "Perm Oblast"), "Primor'ye": ("滨海边疆区", "Primorsky Krai"), "Pskov": ("普斯科夫州", "Pskov Oblast"),
    "Rostov": ("罗斯托夫州", "Rostov Oblast"), "Ryazan'": ("梁赞州", "Ryazan Oblast"),
    "Sakha (Yakutia)": ("雅库特自治共和国", "Yakut ASSR"), "Sakhalin": ("萨哈林州", "Sakhalin Oblast"),
    "Samara": ("古比雪夫州", "Kuybyshev Oblast"), "Saratov": ("萨拉托夫州", "Saratov Oblast"),
    "Smolensk": ("斯摩棱斯克州", "Smolensk Oblast"), "Stavropol'": ("斯塔夫罗波尔边疆区", "Stavropol Krai"),
    "Sverdlovsk": ("斯维尔德洛夫斯克州", "Sverdlovsk Oblast"), "Tambov": ("坦波夫州", "Tambov Oblast"),
    "Tatarstan": ("鞑靼自治共和国", "Tatar ASSR"), "Tomsk": ("托木斯克州", "Tomsk Oblast"), "Tula": ("图拉州", "Tula Oblast"),
    "Tuva": ("图瓦自治共和国", "Tuva ASSR"), "Tver'": ("加里宁州", "Kalinin Oblast"), "Tyumen'": ("秋明州", "Tyumen Oblast"),
    "Udmurt": ("乌德穆尔特自治共和国", "Udmurt ASSR"), "Ul'yanovsk": ("乌里扬诺夫斯克州", "Ulyanovsk Oblast"),
    "Vladimir": ("弗拉基米尔州", "Vladimir Oblast"), "Volgograd": ("伏尔加格勒州", "Volgograd Oblast"),
    "Vologda": ("沃洛格达州", "Vologda Oblast"), "Voronezh": ("沃罗涅日州", "Voronezh Oblast"),
    "Yaroslavl'": ("雅罗斯拉夫尔州", "Yaroslavl Oblast"),
}
# 归乌克兰的(1954 年起)
TO_UKRAINE = {"Crimea", "Sevastopol"}

REPUBLICS = {
    "UKR": ("乌克兰加盟共和国", "Ukrainian SSR"), "BLR": ("白俄罗斯加盟共和国", "Byelorussian SSR"),
    "KAZ": ("哈萨克加盟共和国", "Kazakh SSR"), "UZB": ("乌兹别克加盟共和国", "Uzbek SSR"),
    "TKM": ("土库曼加盟共和国", "Turkmen SSR"), "KGZ": ("吉尔吉斯加盟共和国", "Kirghiz SSR"),
    "TJK": ("塔吉克加盟共和国", "Tajik SSR"), "GEO": ("格鲁吉亚加盟共和国", "Georgian SSR"),
    "ARM": ("亚美尼亚加盟共和国", "Armenian SSR"), "AZE": ("阿塞拜疆加盟共和国", "Azerbaijan SSR"),
    "MDA": ("摩尔达维亚加盟共和国", "Moldavian SSR"), "LTU": ("立陶宛加盟共和国", "Lithuanian SSR"),
    "LVA": ("拉脱维亚加盟共和国", "Latvian SSR"), "EST": ("爱沙尼亚加盟共和国", "Estonian SSR"),
}

CHINA_TOLERANCE, SOVIET_TOLERANCE = 0.04, 0.08
MIN_PART = 0.0005  # 平方度：更小的岛不要(最大的一块总留着，香港、澳门本身就小)


def tidy(geom, tolerance):
    geom = geom.buffer(0)
    parts = list(geom.geoms) if isinstance(geom, MultiPolygon) else [geom]
    parts = [p for p in parts if not p.is_empty]
    if not parts:
        return None
    largest = max(parts, key=lambda p: p.area)
    kept = [p for p in parts if p is largest or p.area >= MIN_PART]
    out = []
    for part in kept:
        simple = part.simplify(tolerance, preserve_topology=True)
        if simple.is_empty or simple.area <= 0:
            simple = part
        out.extend(simple.geoms if isinstance(simple, MultiPolygon) else [simple])
    polygons = []
    for poly in out:
        rings = [[[round(x, 3), round(y, 3)] for x, y in poly.exterior.coords]]
        rings += [[[round(x, 3), round(y, 3)] for x, y in hole.coords] for hole in poly.interiors]
        polygons.append(rings)
    return polygons


def entry(name, realm, polygons, source):
    return {"era": ERA, "year": YEAR, "polity": "*", "division": name, "realm": realm,
            "precision": "simplified", "source": source, "polygons": polygons}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=pathlib.Path)
    parser.add_argument("--china", type=pathlib.Path, required=True)
    parser.add_argument("--ne", type=pathlib.Path, required=True)
    args = parser.parse_args()

    out = []
    china = json.loads(args.china.read_text(encoding="utf-8"))
    for feature in china["features"]:
        zh = feature["properties"].get("name") or ""
        if zh not in CHINA_EN:
            continue  # 九段线等不是省
        polygons = tidy(shape(feature["geometry"]), CHINA_TOLERANCE)
        if polygons:
            out.append(entry(T(zh, CHINA_EN[zh]), "china", polygons, "DataV.GeoAtlas 100000_full"))

    ne = json.loads(args.ne.read_text(encoding="utf-8"))
    russia, republics = {}, {}
    for feature in ne["features"]:
        props = feature["properties"]
        country, name = props.get("adm0_a3"), props.get("name")
        geom = shape(feature["geometry"])
        if country == "RUS":
            if name in TO_UKRAINE:
                republics.setdefault("UKR", []).append(geom)
                continue
            if name not in RUSSIA:
                continue
            target = RUSSIA[name]
            key = target if isinstance(target, str) else name
            while isinstance(RUSSIA[key], str):
                key = RUSSIA[key]
            russia.setdefault(key, []).append(geom)
        elif country in REPUBLICS:
            republics.setdefault(country, []).append(geom)
    for key, geoms in russia.items():
        zh, en = RUSSIA[key]
        polygons = tidy(unary_union(geoms), SOVIET_TOLERANCE)
        if polygons:
            out.append(entry(T(zh, en), "ussr", polygons, "Natural Earth admin-1, 1983 oblast level"))
    for code, geoms in republics.items():
        zh, en = REPUBLICS[code]
        polygons = tidy(unary_union(geoms), SOVIET_TOLERANCE)
        if polygons:
            out.append(entry(T(zh, en), "ussr", polygons, "Natural Earth admin-1, union republic"))

    data = {
        "description": "1937 sino_japanese shared administrative borders: PRC provinces (GS(2016)1600 reference) and Soviet "
                       "union republics / RSFSR oblasts as of 1983. Province borders are hard edges; land inside goes to the "
                       "nearest city inside the same province, whoever owns it.",
        "attribution": "China: Aliyun DataV.GeoAtlas; USSR: Natural Earth (public domain)",
        "boundaries": out,
    }
    path = args.root / "WorldMaps" / "administrative_boundaries_modern.json"
    path.write_text(json.dumps(data, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(path.name, len(out), "块", "中国", sum(1 for e in out if e["realm"] == "china"), "苏联",
          sum(1 for e in out if e["realm"] == "ussr"), path.stat().st_size // 1024, "KB")


if __name__ == "__main__":
    main()

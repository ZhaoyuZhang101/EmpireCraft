"""各时代剧本的长城、边墙与关隘(照史实，经纬度取自通行地图的走向，按山脊走的事由 WorldMapWalls 在两点之间找路)：
写进 WorldMaps/eras/*.json 的 "walls"([{name, pts: [[经, 纬]…]}])与 "passes"([{name, at: [经, 纬]}])。
name 是 "简体|英文|繁体"；关名英文不超过八个字(村镇名的长度上限)。

用法：python -I -X utf8 walls_source.py <模组目录>
"""
import json
import os
import sys


def W(name, pts):
    return {"name": name, "pts": pts}


def P(name, lon, lat):
    return {"name": name, "at": [lon, lat]}


QIN_ZHAOWANG = W("秦昭王长城|Wall of King Zhao of Qin|秦昭王長城",
                 [[103.86, 35.38], [104.6, 35.55], [105.6, 35.85], [106.3, 36.05], [107.0, 36.4], [107.6, 36.8],
                  [108.2, 37.15], [109.0, 37.65], [109.8, 38.3], [110.5, 38.9]])
ZHAO_NORTH = W("赵长城|Zhao Wall|趙長城",
               [[107.4, 41.0], [108.6, 40.9], [109.5, 40.85], [110.5, 40.9], [111.6, 41.0], [112.5, 41.0],
                [113.5, 40.95], [114.9, 40.9]])
YAN_NORTH = W("燕长城|Yan Wall|燕長城",
              [[114.9, 40.9], [116.3, 41.5], [117.6, 42.0], [118.9, 42.3], [120.3, 42.2], [121.7, 42.0],
               [122.6, 41.6], [123.2, 41.3]])
QI_WALL = W("齐长城|Qi Wall|齊長城",
            [[116.45, 36.28], [117.1, 36.32], [117.7, 36.25], [118.2, 36.15], [118.6, 36.0], [119.3, 35.9],
             [120.1, 35.95]])
CHU_FANGCHENG = W("楚方城|Chu Fangcheng|楚方城",
                  [[113.6, 33.4], [113.2, 33.6], [112.8, 33.65], [112.4, 33.5], [112.0, 33.3], [111.8, 33.0]])
HEXI = W("汉长城·河西|Han Wall (Hexi)|漢長城·河西",
         [[103.3, 36.7], [102.6, 37.9], [101.5, 38.4], [100.45, 38.93], [99.4, 39.3], [98.5, 39.73], [97.4, 40.2],
          [96.0, 40.4], [94.7, 40.4], [93.86, 40.35]])
YINSHAN = W("秦汉长城·阴山|Qin-Han Wall (Yinshan)|秦漢長城·陰山", ZHAO_NORTH["pts"])
YAN_LIAO = W("秦汉长城·燕辽|Qin-Han Wall (Yan-Liao)|秦漢長城·燕遼", YAN_NORTH["pts"])
QIN_OLD = W("秦长城|Qin Wall|秦長城", QIN_ZHAOWANG["pts"])
LIMES = W("日耳曼边墙|Limes Germanicus|日耳曼邊牆",
          [[7.33, 50.5], [7.8, 50.37], [8.3, 50.3], [8.65, 50.42], [8.98, 50.08], [9.1, 49.6], [9.3, 49.3],
           [9.5, 49.0], [9.69, 48.8], [10.3, 48.95], [11.0, 49.0], [11.78, 48.85]])
HADRIAN = W("哈德良长城|Hadrian's Wall|哈德良長城",
            [[-1.53, 54.99], [-1.61, 54.97], [-2.14, 55.03], [-2.33, 55.01], [-2.6, 54.99], [-2.93, 54.89],
             [-3.21, 54.95]])
DANEVIRKE = W("丹麦边墙|Danevirke|丹麥邊牆", [[9.38, 54.45], [9.48, 54.47], [9.56, 54.49], [9.65, 54.5]])
JIN_TRENCH = W("金界壕|Jin Border Trench|金界壕",
               [[124.3, 48.6], [122.7, 47.9], [122.0, 46.6], [121.0, 45.9], [119.6, 45.5], [117.6, 44.6],
                [116.2, 43.5], [115.3, 42.6], [113.8, 42.0], [112.5, 41.8], [111.3, 41.7], [110.3, 41.7]])
MING_MAIN = W("明长城|Ming Great Wall|明長城",
              [[98.22, 39.8], [99.0, 39.5], [99.9, 39.15], [100.45, 38.95], [101.1, 38.8], [101.9, 38.3],
               [102.6, 37.95], [103.4, 37.6], [104.1, 37.2], [105.2, 37.5], [105.8, 38.1], [106.0, 38.6],
               [106.6, 38.2], [107.4, 37.8], [108.2, 37.6], [108.8, 37.65], [109.75, 38.3], [110.5, 38.85],
               [111.2, 39.3], [111.5, 39.45], [112.4, 39.9], [113.3, 40.15], [114.2, 40.5], [114.9, 40.8],
               [115.6, 40.7], [116.07, 40.45], [116.6, 40.5], [117.15, 40.69], [117.8, 40.45], [118.4, 40.43],
               [119.2, 40.2], [119.78, 40.0]])
MING_INNER = W("明内长城|Ming Inner Wall|明內長城",
               [[111.5, 39.45], [112.3, 39.05], [112.88, 39.18], [113.9, 39.3], [114.6, 39.45], [115.17, 39.43],
                [115.7, 39.95], [116.07, 40.29]])
LIAODONG = W("辽东边墙|Liaodong Wall|遼東邊牆",
             [[119.78, 40.0], [120.6, 40.6], [121.1, 41.3], [121.8, 41.6], [122.7, 42.1], [123.5, 42.6],
              [124.0, 42.55], [123.95, 41.9], [124.3, 41.3], [124.8, 40.7], [124.4, 40.13]])

HANGU_QIN = P("函谷关|Hangu|函谷關", 110.9, 34.63)
HANGU_HAN = P("函谷关|Hangu|函谷關", 112.15, 34.73)
WUGUAN = P("武关|Wuguan|武關", 110.65, 33.67)
XIAOGUAN = P("萧关|Xiaoguan|蕭關", 106.25, 36.0)
YANMEN = P("雁门关|Yanmen|雁門關", 112.88, 39.18)
JUYONG = P("居庸关|Juyong|居庸關", 116.07, 40.29)
YUMEN = P("玉门关|Yumen|玉門關", 93.86, 40.35)
YUMEN_TANG = P("玉门关|Yumen|玉門關", 95.78, 40.53)
YANGGUAN = P("阳关|Yangguan|陽關", 94.06, 39.93)
TONGGUAN = P("潼关|Tongguan|潼關", 110.25, 34.6)
JIANMEN = P("剑门关|Jianmen|劍門關", 105.55, 32.2)
YANGPING = P("阳平关|Yangping|陽平關", 106.67, 33.15)
DASAN = P("大散关|Dasan|大散關", 106.95, 34.25)
SAALBURG = P("萨尔堡|Saalburg|薩爾堡", 8.57, 50.27)
CHESTERS = P("切斯特斯堡|Chesters|切斯特斯堡", -2.14, 55.03)
HEDEBY = P("海塔布|Hedeby|海塔布", 9.57, 54.49)
SHANHAI = P("山海关|Shanhai|山海關", 119.78, 40.0)
JIAYU = P("嘉峪关|Jiayu|嘉峪關", 98.22, 39.8)
ZIJING = P("紫荆关|Zijing|紫荊關", 115.17, 39.43)
GUBEIKOU = P("古北口|Gubeikou|古北口", 117.15, 40.69)
XIFENG = P("喜峰口|Xifeng|喜峰口", 118.4, 40.43)
PIANGUAN = P("偏关|Pianguan|偏關", 111.5, 39.44)
NINGWU = P("宁武关|Ningwu|寧武關", 112.3, 39.0)

ERAS = {
    # 战国：秦昭王长城、赵武灵王的北长城、燕北长城、齐长城、楚方城
    "warring_states": ([QIN_ZHAOWANG, ZHAO_NORTH, YAN_NORTH, QI_WALL, CHU_FANGCHENG],
                       [HANGU_QIN, WUGUAN, XIAOGUAN, YANMEN, JUYONG]),
    # 图拉真：汉长城河西段与沿用的秦长城；日耳曼—雷蒂亚边墙(哈德良长城 122 年才修)
    "roman_empire": ([HEXI, QIN_OLD, YINSHAN, YAN_LIAO, LIMES],
                     [YUMEN, YANGGUAN, HANGU_HAN, XIAOGUAN, YANMEN, JUYONG, SAALBURG]),
    "three_kingdoms": ([HEXI, QIN_OLD, YINSHAN, YAN_LIAO, LIMES, HADRIAN],
                       [YUMEN, YANGGUAN, TONGGUAN, JIANMEN, YANGPING, YANMEN, JUYONG, SAALBURG, CHESTERS]),
    # 唐不修长城，秦汉旧墙尚在；玉门关已东移瓜州
    "high_tang": ([HEXI, QIN_OLD, YINSHAN, YAN_LIAO],
                  [YUMEN_TANG, YANGGUAN, TONGGUAN, JIANMEN, DASAN, XIAOGUAN, YANMEN, JUYONG]),
    "charlemagne": ([HEXI, QIN_OLD, YINSHAN, YAN_LIAO, DANEVIRKE],
                    [YUMEN_TANG, YANGGUAN, TONGGUAN, JIANMEN, DASAN, XIAOGUAN, YANMEN, JUYONG, HEDEBY]),
    # 金界壕(1123 年起修)；瓦尔德马尔增修的丹麦边墙
    "crusades": ([JIN_TRENCH, DANEVIRKE],
                 [JUYONG, YANMEN, TONGGUAN, JIANMEN, DASAN, HEDEBY]),
    # 明长城：嘉峪关到山海关，内长城、辽东边墙
    "lingao": ([MING_MAIN, MING_INNER, LIAODONG],
               [SHANHAI, JUYONG, JIAYU, YANMEN, ZIJING, GUBEIKOU, XIFENG, PIANGUAN, NINGWU, TONGGUAN, JIANMEN]),
}


def main():
    root = sys.argv[1]
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from add_traditional import dump_records  # noqa: E402
    for era_id, (walls, passes) in ERAS.items():
        for p in passes:
            if len(p["name"].split("|")[1]) > 8:
                raise SystemExit("%s：关名英文过长 %s" % (era_id, p["name"]))
        path = os.path.join(root, "WorldMaps", "eras", era_id + ".json")
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        data["walls"] = walls
        data["passes"] = passes
        head = {k: v for k, v in data.items() if k not in ("cores", "polities")}
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write("{\n")
            for key, value in head.items():
                f.write('  "%s": %s,\n' % (key, json.dumps(value, ensure_ascii=False)))
            dump_records(f, "cores", data.get("cores", []), False)
            dump_records(f, "polities", data.get("polities", []), True)
            f.write("}\n")
        print(era_id, len(walls), "道墙", len(passes), "关")


if __name__ == "__main__":
    main()

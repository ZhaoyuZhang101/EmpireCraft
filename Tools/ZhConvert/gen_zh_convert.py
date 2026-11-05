# 生成 RegimeEditor/zh-convert.js(编辑器里简体⇄繁体自动补全)。
# 用法：python gen_zh_convert.py <OpenCC 的 dictionary 目录>
#   字典文件可用 pip install opencc-python-reimplemented 后在 site-packages/opencc/dictionary 找到
#   (STCharacters、STPhrases、TWVariants、TSCharacters、TSPhrases、TWVariantsRev、TWVariantsRevPhrases)。
# 字典数据来自 OpenCC(https://github.com/BYVoid/OpenCC)，Apache License 2.0。
# custom_s2t.json 是本模组繁体文案的惯用写法(預設、模組設定、劃進、煉鋼、徵斂、后黨……)，优先于 OpenCC 词组。
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent


def load(folder, name):
    table = {}
    for line in (folder / name).read_text(encoding="utf-8").splitlines():
        if "\t" not in line:
            continue
        key, value = line.split("\t", 1)
        table[key] = value.split(" ")[0]
    return table


def main():
    folder = Path(sys.argv[1])
    stc = load(folder, "STCharacters.txt")
    stp = load(folder, "STPhrases.txt")
    twv = load(folder, "TWVariants.txt")
    tsc = load(folder, "TSCharacters.txt")
    tsp = load(folder, "TSPhrases.txt")
    twvr = load(folder, "TWVariantsRev.txt")
    twvrp = load(folder, "TWVariantsRevPhrases.txt")
    custom = json.loads((HERE / "custom_s2t.json").read_text(encoding="utf-8"))

    # 本模组的单字习惯：划一律作劃(划船等例外在词表里)；佈、賬、燬、譭、唸按台湾常用写作布、帳、毀、毀、念
    stc["划"] = "劃"
    s2t_phrases = dict(stp)
    s2t_phrases.update(custom)
    twv.update({"佈": "布", "賬": "帳", "燬": "毀", "譭": "毀", "唸": "念"})

    def chars(text, table):
        return "".join(table.get(c, c) for c in text)

    t2s_phrases = {k: v for k, v in tsp.items() if chars(k, tsc) != v}
    t2s_phrases.update(twvrp)
    t2s_phrases.update({v: k for k, v in custom.items()})

    data = {"sc": stc, "sp": s2t_phrases, "tv": twv, "tc": tsc, "tp": t2s_phrases, "tvr": twvr}
    template = (HERE / "template.js").read_text(encoding="utf-8")
    out = template.replace("/*DATA*/", json.dumps(data, ensure_ascii=False, separators=(",", ":")))
    target = ROOT / "RegimeEditor" / "zh-convert.js"
    target.write_text(out, encoding="utf-8")
    print(f"wrote {target} ({len(out) // 1024} KB)")


if __name__ == "__main__":
    main()

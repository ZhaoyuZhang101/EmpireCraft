"""给字形包配一张"缺字换字表"：字体里没有、简繁转换后也没有的字，换成字体里有的同音字。

用法：python3 build_homophones.py <字形包.glyphs> <输出.txt>
需要 pypinyin(pip install pypinyin，只在生成时用，不随模组分发)。

要处理的字：GB2312 的全部汉字与 Big5 的常用、次常用汉字(国号、城名、人名里会出现的字)。
每个字先按模组的简繁规则查(本字、简体对应的繁体、繁体对应的简体)，查得到就不进表；
查不到的按读音换：先找主读音、声调都相同的字，再找主读音相同声调不同的，最后才看多音字的其它读音；
同一档里取最常用的(GB2312 一级字 > 二级字 > Big5 常用字 > 其余)。表里每行"原字<TAB>换成的字"。
"""
import os
import struct
import sys

from pypinyin import Style, pinyin

HERE = os.path.dirname(os.path.abspath(__file__))
OPENCC = os.path.join(HERE, '..', '..', 'Assets', 'Fonts', 'OpenCC', 'STCharacters.txt')


def pack_codepoints(path):
    data = open(path, 'rb').read()
    assert data[:4] == b'ECSG', '不是字形包'
    count = struct.unpack('<i', data[20:24])[0]
    return {struct.unpack('<i', data[24 + i * 22:28 + i * 22])[0] for i in range(count)}


def gb2312(level):
    rows = range(0xB0, 0xD8) if level == 1 else range(0xD8, 0xF8)
    for hi in rows:
        for lo in range(0xA1, 0xFF):
            try:
                yield bytes([hi, lo]).decode('gb2312')
            except UnicodeDecodeError:
                pass


def big5(level):
    rows = range(0xA4, 0xC7) if level == 1 else range(0xC9, 0xFA)
    for hi in rows:
        for lo in list(range(0x40, 0x7F)) + list(range(0xA1, 0xFF)):
            try:
                c = bytes([hi, lo]).decode('big5')
            except UnicodeDecodeError:
                continue
            if '一' <= c <= '鿿':
                yield c


def opencc():
    to_traditional, to_simplified = {}, {}
    for line in open(OPENCC, encoding='utf-8'):
        if line.startswith('#') or '\t' not in line:
            continue
        simple, variants = line.rstrip('\n').split('\t')
        to_traditional[simple] = [v for v in variants.split(' ') if len(v) == 1]
        for v in to_traditional[simple]:
            if v != simple:
                to_simplified.setdefault(v, simple)
    return to_traditional, to_simplified


def main():
    pack, out = sys.argv[1], sys.argv[2]
    have = pack_codepoints(pack)
    to_traditional, to_simplified = opencc()

    def shown(c):
        candidates = [c] + to_traditional.get(c, [])
        simple = to_simplified.get(c)
        if simple:
            candidates += [simple] + to_traditional.get(simple, [])
        return any(ord(x) in have for x in candidates)

    # 常用度排序：先出现的更常用
    order = {}
    for group in (gb2312(1), gb2312(2), big5(1), big5(2)):
        for c in group:
            order.setdefault(c, len(order))
    universe = list(order)
    # 字体里有的字按读音归类：主读音一套，多音字的全部读音一套
    main_tone, main_sound, any_tone, any_sound = {}, {}, {}, {}
    for c in sorted((chr(cp) for cp in have if 0x4E00 <= cp <= 0x9FFF), key=lambda c: order.get(c, 10 ** 6)):
        primary = pinyin(c, style=Style.TONE3)[0][0]
        main_tone.setdefault(primary, []).append(c)
        main_sound.setdefault(primary.rstrip('12345'), []).append(c)
        for reading in pinyin(c, style=Style.TONE3, heteronym=True)[0]:
            any_tone.setdefault(reading, []).append(c)
            any_sound.setdefault(reading.rstrip('12345'), []).append(c)
    table = []
    for c in universe:
        if shown(c):
            continue
        reading = pinyin(c, style=Style.TONE3)[0][0]
        sound = reading.rstrip('12345')
        pick = next((group[0] for group in (main_tone.get(reading), main_sound.get(sound), any_tone.get(reading),
                                              any_sound.get(sound)) if group), None)
        if pick:
            table.append((c, pick))
    with open(out, 'w', encoding='utf-8', newline='\n') as f:
        f.write('# 缺字换字表(同音字)：Tools/FontBake/build_homophones.py 生成\n')
        for c, pick in table:
            f.write(f'{c}\t{pick}\n')
    missing = sum(1 for c in universe if not shown(c))
    print(f'{len(universe)} 字里字体与简繁转换显示不了 {missing} 个，同音换字 {len(table)} 个')


if __name__ == '__main__':
    main()

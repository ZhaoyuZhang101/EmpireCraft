# -*- coding: utf-8 -*-
"""把小图集、立绘说明与高清分片索引编进手机终端网页。
高清图层由浏览器按需读取，不计入首页的回复大小。

图集与说明由 portrait_forge.py 生成；改了立绘先跑它，再跑这个：

    python Tools/IconForge/portrait_forge.py
    python Tools/IconForge/terminal_portraits.py
"""
import base64, hashlib, io, json, os, re
from PIL import Image, ImageChops
from portrait_templates import DIRECTORY as TEMPLATE_DIRECTORY, validate_marks

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ATLAS = os.path.join(ROOT, 'Terminal', 'portraits.png')
PARTS = os.path.join(ROOT, 'Tools', 'IconForge', 'portrait_parts.json')
PAGE = os.path.join(ROOT, 'Terminal', 'index.html')
HD = os.path.join(ROOT, 'Terminal', 'portraits-hd.json')
ALIGNMENT = os.path.join(ROOT, 'Tools', 'IconForge', 'portrait_alignment.json')
BEGIN, END = '/*PORTRAIT-BEGIN*/', '/*PORTRAIT-END*/'
MAX_PAGE_BYTES = 900 * 1024  # TerminalRelay.MaxReply
GARMENT_NECK = (44, 37)
GARMENT_SOLE = 134


def garment_soles(parts, outfit):
    hd = parts.get('hd')
    if not hd:
        raise ValueError('衣身对位需要高清图集')
    w, h = (v * hd['scale'] for v in parts['cell'])
    columns = hd['columns']
    page, slot = divmod(parts['tiles'][outfit], columns * hd['rows'])
    x, y = slot % columns * w, slot // columns * h
    with Image.open(os.path.join(os.path.dirname(HD), hd['pages'][page])) as image:
        alpha = image.crop((x, y, x + w, y + h)).getchannel('A')
    box = alpha.point([0] * 128 + [255] * 128).getbbox()
    if not box:
        raise ValueError('衣身图层为空 ' + outfit)
    return {(box[3] - 1) // hd['scale']}


def validate_alignment(data, parts):
    if not isinstance(data, dict):
        raise ValueError('对位配置必须是对象')
    tiles = parts['tiles']
    width, height = parts['cell']
    result = {}
    for outfit, entries in data.items():
        if outfit not in tiles or not outfit.startswith(('body:', 'base:')) or not isinstance(entries, dict):
            raise ValueError('无效的衣装 ' + outfit)
        offsets = {}
        for key, offset in entries.items():
            if key not in ('@head', '@neck') and (key not in tiles or key.startswith(('body:', 'base:')) and key != outfit):
                raise ValueError('无效的图层 ' + key)
            if (not isinstance(offset, list) or len(offset) != 2
                    or any(type(v) is not int or abs(v) > limit for v, limit in zip(offset, (width, height)))):
                raise ValueError('无效的图层坐标 ' + key)
            if key == '@neck':
                if not (0 <= offset[0] < width and 0 <= offset[1] < height):
                    raise ValueError('颈根标点超出画布 ' + outfit)
                offsets[key] = offset
            elif any(offset):
                offsets[key] = offset
        neck = offsets.get('@neck')
        body_offset = offsets.get(outfit, [0, 0])
        if neck is None and any(body_offset):
            raise ValueError('移动衣身前须标记模板颈根 ' + outfit)
        if neck is not None:
            aligned = [target - origin for target, origin in zip(GARMENT_NECK, neck)]
            if aligned != body_offset:
                raise ValueError('衣身颈根没有对齐模板 ' + outfit)
            if garment_soles(parts, outfit) != {GARMENT_SOLE - aligned[1]}:
                raise ValueError('这件衣身的颈根和脚底不能同时对齐模板 ' + outfit)
        if offsets:
            result[outfit] = offsets
    return result


def read_alignment(parts):
    if not os.path.isfile(ALIGNMENT):
        return {}
    with open(ALIGNMENT, encoding='utf-8') as source:
        return validate_alignment(json.load(source), parts)


def portrait_payload(parts):
    return 'var PORTRAIT=' + json.dumps(parts, ensure_ascii=False, separators=(',', ':')) + ';'


def check_garment_templates(parts):
    baseline = TEMPLATE_DIRECTORY / 'legacy_tiles.json'
    existing = set(json.loads(baseline.read_text(encoding='utf-8')))
    current = {key for key in parts['tiles'] if key.startswith('body:')}
    index = json.loads((TEMPLATE_DIRECTORY / 'index.json').read_text(encoding='utf-8'))
    for key in sorted(current):
        _, name, sex = key.split(':')
        path = TEMPLATE_DIRECTORY / 'garments' / (name + '-' + sex + '.json')
        if key in existing and not path.exists():
            continue
        if not path.is_file():
            raise SystemExit('新增衣装缺少模板标点 ' + str(path))
        try:
            data = json.loads(path.read_text(encoding='utf-8-sig'))
            template = validate_marks(data, index)
            if template['id'] != 'terminal_' + sex:
                raise ValueError('现用终端图集须使用相应男女版的终端现用模板')
        except ValueError as error:
            raise SystemExit(str(path) + ' ' + str(error)) from error


def check_art_fit(image, tiles, cell, columns, scale):
    """Check shared head and upper-body landmarks on the portrait grid."""
    width, height = (axis * scale for axis in cell)
    for key, slot in tiles:
        layer = key.split(':', 1)[0]
        if layer not in ('head', 'hair', 'hat', 'body', 'base'):
            continue
        x, y = slot % columns * width, slot // columns * height
        alpha = image.crop((x, y, x + width, y + height)).getchannel('A')
        solid = alpha.point([0] * 128 + [255] * 128)
        bounds = solid.getbbox()
        if not bounds:
            raise SystemExit('空的立绘部件 ' + key)
        left, top, right, bottom = bounds
        span = (right - left) / scale
        if layer == 'head':
            off_center = abs(left + right - width) > scale * 2
            if off_center or not 11.5 <= span <= 13.5 or not 14 <= top / scale <= 16:
                raise SystemExit('脸宽或颅顶偏离统一立绘标准 ' + key)
        elif layer == 'hair' and span > 19:
            raise SystemExit('前发超出统一立绘宽度 ' + key)
        elif layer == 'hat':
            limit = {'hat:mianguan': 25, 'hat:wushamao': 32}.get(key, 20)
            if span > limit:
                raise SystemExit('冠帽超出统一立绘宽度 ' + key)
        elif layer in ('body', 'base'):
            if not 134 * scale <= bottom - 1 < 135 * scale:
                raise SystemExit('脚底偏离统一立绘标准 ' + key)
            # The neckline must overlap the neutral neck. Sleeve volume and
            # collar depth vary with the cut of the garment.
            center = width // 2
            join = solid.crop((center - scale, 33 * scale, center + scale, 39 * scale))
            pixels = join.tobytes()
            if not any(pixels.count(255, y, y + join.width) >= scale
                       for y in range(0, len(pixels), join.width)):
                raise SystemExit('衣身未接到立绘颈根 ' + key)


def high_resolution(parts):
    with open(HD, encoding='utf-8') as f:
        hd = json.load(f)
    if hd.pop('tiles') != parts['tiles']:
        raise SystemExit('高清图层序号与 portrait_parts.json 不一致')
    scale, columns, rows = (hd[k] for k in ('scale', 'columns', 'rows'))
    width, height = parts['cell']
    capacity = columns * rows * len(hd['pages'])
    profiles = hd.pop('profiles', {})
    sample_tiles = [i for p in profiles.values() for i in p['tiles'].values()]
    indices = list(parts['tiles'].values()) + sample_tiles
    if len(set(indices)) != len(indices) or min(indices) < 0 or max(indices) >= capacity:
        raise SystemExit('高清图层序号重复或超出图集范围')
    slots_per_page = columns * rows
    for i, name in enumerate(hd['pages']):
        if not re.fullmatch(r'portraits-hd-\d{2}-[0-9a-f]{12}\.webp', name) or int(name[13:15]) != i:
            raise SystemExit('无效的高清立绘分片名 ' + name)
        with open(os.path.join(os.path.dirname(HD), name), 'rb') as f:
            data = f.read()
        if len(data) > MAX_PAGE_BYTES or hashlib.sha256(data).hexdigest()[:12] != name[16:28]:
            raise SystemExit('高清立绘分片超限或校验失败 ' + name)
        with Image.open(io.BytesIO(data)) as image:
            if image.size != (width * scale * columns, height * scale * rows) or image.mode != 'RGBA':
                raise SystemExit('高清立绘分片尺寸或透明通道错误 ' + name)
            tiles = ((key, index % slots_per_page) for key, index in parts['tiles'].items()
                     if index // slots_per_page == i)
            check_art_fit(image, tiles, parts['cell'], columns, scale)
    # Keep sample art in the source manifest. The terminal uses the same
    # registered layer for each part ID across every outfit.
    hd['skinBases'] = {key: value for key, value in hd['skinBases'].items() if key in parts['tiles']}
    hd['pages'] = hd['pages'][:max(parts['tiles'].values()) // slots_per_page + 1]
    return hd


def main():
    parts = json.load(io.open(PARTS, encoding='utf-8'))
    for key in ('cell', 'thumb', 'columns', 'tiles', 'order', 'faces', 'palettes', 'skinBases',
                'hairStyle', 'clip', 'wardrobe', 'presets'):
        if key not in parts:
            raise SystemExit('portrait_parts.json is missing ' + key)
    wardrobe = parts['wardrobe']
    check_garment_templates(parts)
    if os.path.isfile(HD):
        parts['hd'] = high_resolution(parts)
    parts['alignment'] = read_alignment(parts)
    text = io.open(PAGE, encoding='utf-8', newline='').read()
    if BEGIN not in text or END not in text:
        raise SystemExit('markers not found in index.html')
    head, rest = text.split(BEGIN, 1)
    tail = rest.split(END, 1)[1]
    # 优先无损。图集较大时仅压缩颜色，保留用于换肤色、瞳色的 Alpha 标记。
    with Image.open(ATLAS) as atlas:
        for options in ({'lossless': True}, {'quality': 96}):
            encoded = io.BytesIO()
            atlas.save(encoded, format='WEBP', method=6, **options)
            encoded.seek(0)
            with Image.open(encoded) as decoded:
                if ImageChops.difference(atlas.getchannel('A'), decoded.getchannel('A')).getbbox():
                    raise SystemExit('The compressed atlas altered alpha markers')
            data = encoded.getvalue()
            parts['atlas'] = 'data:image/webp;base64,' + base64.b64encode(data).decode('ascii')
            text = head + BEGIN + portrait_payload(parts) + END + tail
            if len(text.encode('utf-8')) <= MAX_PAGE_BYTES:
                break
        else:
            raise SystemExit('Terminal/index.html exceeds TerminalRelay.MaxReply')
    io.open(PAGE, 'w', encoding='utf-8', newline='').write(text)
    # 游戏服务端读取这份清单来开衣铺并计价。
    io.open(os.path.join(ROOT, 'Terminal', 'wardrobe.json'), 'w', encoding='utf-8', newline='\n').write(
        json.dumps(wardrobe, ensure_ascii=False, separators=(',', ':')))
    print('%d presets, atlas %d bytes' % (len(parts['presets']), len(data)))


if __name__ == '__main__':
    main()

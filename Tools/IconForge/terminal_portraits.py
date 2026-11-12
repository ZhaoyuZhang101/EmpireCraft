# -*- coding: utf-8 -*-
"""把人物立绘的部件图集编进手机终端的网页：Terminal/portraits.png(图集) + Tools/IconForge/portrait_parts.json(说明)
写进 Terminal/index.html 的 /*PORTRAIT-BEGIN*/ … /*PORTRAIT-END*/ 之间。内嵌而不另开文件：游戏里的终端是旧版、走外网线路时也照样显示
(同 terminal_page_icons.py)。

图集与说明由 portrait_forge.py 生成；改了立绘先跑它，再跑这个：

    python Tools/IconForge/portrait_forge.py
    python Tools/IconForge/terminal_portraits.py
"""
import base64, io, json, os
from PIL import Image, ImageChops

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ATLAS = os.path.join(ROOT, 'Terminal', 'portraits.png')
PARTS = os.path.join(ROOT, 'Tools', 'IconForge', 'portrait_parts.json')
PAGE = os.path.join(ROOT, 'Terminal', 'index.html')
BEGIN, END = '/*PORTRAIT-BEGIN*/', '/*PORTRAIT-END*/'
MAX_PAGE_BYTES = 900 * 1024  # TerminalRelay.MaxReply


def main():
    parts = json.load(io.open(PARTS, encoding='utf-8'))
    for key in ('cell', 'thumb', 'columns', 'tiles', 'order', 'faces', 'palettes', 'skinBases',
                'hairStyle', 'clip', 'wardrobe', 'presets'):
        if key not in parts:
            raise SystemExit('portrait_parts.json is missing ' + key)
    wardrobe = parts['wardrobe']
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
            text = head + BEGIN + 'var PORTRAIT=' + json.dumps(parts, ensure_ascii=False, separators=(',', ':')) + ';' + END + tail
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

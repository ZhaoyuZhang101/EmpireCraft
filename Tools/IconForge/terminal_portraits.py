# -*- coding: utf-8 -*-
"""把人物立绘的部件图集编进手机终端的网页：Terminal/portraits.png(图集) + Tools/IconForge/portrait_parts.json(说明)
写进 Terminal/index.html 的 /*PORTRAIT-BEGIN*/ … /*PORTRAIT-END*/ 之间。内嵌而不另开文件：游戏里的终端是旧版、走外网线路时也照样显示
(同 terminal_page_icons.py)。

图集与说明由 portrait_forge.py 生成；改了立绘先跑它，再跑这个：

    python Tools/IconForge/portrait_forge.py
    python Tools/IconForge/terminal_portraits.py
"""
import base64, io, json, os
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ATLAS = os.path.join(ROOT, 'Terminal', 'portraits.png')
PARTS = os.path.join(ROOT, 'Tools', 'IconForge', 'portrait_parts.json')
PAGE = os.path.join(ROOT, 'Terminal', 'index.html')
BEGIN, END = '/*PORTRAIT-BEGIN*/', '/*PORTRAIT-END*/'
MAX_PAGE_BYTES = 900 * 1024  # TerminalRelay.MaxReply


def main():
    parts = json.load(io.open(PARTS, encoding='utf-8'))
    for key in ('cell', 'thumb', 'columns', 'offsets', 'order', 'rows', 'faces', 'palettes', 'wardrobe', 'presets'):
        if key not in parts:
            raise SystemExit('portrait_parts.json is missing ' + key)
    wardrobe = parts['wardrobe']
    # 网页经中继也要能打开，源图保留 PNG，内嵌版本用 WebP 压缩。
    with Image.open(ATLAS) as atlas, io.BytesIO() as encoded:
        atlas.save(encoded, format='WEBP', lossless=True, method=6)
        data = encoded.getvalue()
    parts['atlas'] = 'data:image/webp;base64,' + base64.b64encode(data).decode('ascii')
    text = io.open(PAGE, encoding='utf-8', newline='').read()
    if BEGIN not in text or END not in text:
        raise SystemExit('markers not found in index.html')
    head, rest = text.split(BEGIN, 1)
    tail = rest.split(END, 1)[1]
    text = head + BEGIN + 'var PORTRAIT=' + json.dumps(parts, ensure_ascii=False, separators=(',', ':')) + ';' + END + tail
    if len(text.encode('utf-8')) > MAX_PAGE_BYTES:
        raise SystemExit('Terminal/index.html exceeds TerminalRelay.MaxReply')
    io.open(PAGE, 'w', encoding='utf-8', newline='').write(text)
    # 游戏服务端读取这份清单来开衣铺并计价。
    io.open(os.path.join(ROOT, 'Terminal', 'wardrobe.json'), 'w', encoding='utf-8', newline='\n').write(
        json.dumps(wardrobe, ensure_ascii=False, separators=(',', ':')))
    print('%d presets, atlas %d bytes' % (len(parts['presets']), len(data)))


if __name__ == '__main__':
    main()

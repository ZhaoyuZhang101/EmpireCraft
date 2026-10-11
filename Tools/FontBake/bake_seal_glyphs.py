"""把篆书字体预先画成字形包(.glyphs)，游戏里直接拼图显示，不依赖 Unity 读字体文件。

用法：python3 bake_seal_glyphs.py <字体.ttf> <输出.glyphs> [字号=64]

格式(小端)：
  'ECSG' int版本=1 int字号 int上伸 int下伸 int字数
  每字：int码位 short步进 short左偏 short顶高(基线以上，向上为正) short宽 short高 int数据偏移 int数据长度
  数据：每字一段 raw deflate，解开是 宽×高 字节的灰度(0 透明~255 实心)，从上到下逐行
"""
import struct
import sys
import zlib

from PIL import Image, ImageDraw, ImageFont


def codepoints(path):
    data = open(path, 'rb').read()
    u16 = lambda a: (data[a] << 8) | data[a + 1]
    i16 = lambda a: u16(a) - 65536 if u16(a) >= 32768 else u16(a)
    u32 = lambda a: int.from_bytes(data[a:a + 4], 'big')
    cmap = next(u32(12 + i * 16 + 8) for i in range(u16(4)) if data[12 + i * 16:16 + i * 16] == b'cmap')
    best, score = -1, -1
    for i in range(u16(cmap + 2)):
        r = cmap + 4 + i * 8
        platform, encoding, offset = u16(r), u16(r + 2), cmap + u32(r + 4)
        fmt = u16(offset)
        s = 3 if fmt == 12 and (platform == 3 and encoding == 10 or platform == 0) else \
            2 if fmt == 4 and (platform == 3 and encoding == 1 or platform == 0) else -1
        if s > score:
            best, score = offset, s
    result = set()
    if u16(best) == 12:
        for g in range(u32(best + 12)):
            a = best + 16 + g * 12
            result.update(range(u32(a), u32(a + 4) + 1))
        return sorted(result)
    seg = u16(best + 6) // 2
    ends = best + 14
    starts = ends + seg * 2 + 2
    deltas = starts + seg * 2
    ranges = deltas + seg * 2
    for k in range(seg):
        end, start = u16(ends + k * 2), u16(starts + k * 2)
        delta, at = i16(deltas + k * 2), ranges + k * 2
        rng = u16(at)
        if start == 0xFFFF:
            continue
        for c in range(start, end + 1):
            if rng == 0:
                glyph = (c + delta) & 0xFFFF
            else:
                raw = u16(at + rng + (c - start) * 2)
                glyph = (raw + delta) & 0xFFFF if raw else 0
            if glyph:
                result.add(c)
    return sorted(result)


def main():
    src, dst = sys.argv[1], sys.argv[2]
    size = int(sys.argv[3]) if len(sys.argv) > 3 else 64
    font = ImageFont.truetype(src, size)
    ascent, descent = font.getmetrics()
    entries, blobs, offset = [], [], 0
    for cp in codepoints(src):
        ch = chr(cp)
        if ch.isspace():
            continue
        advance = int(round(font.getlength(ch)))
        x0, y0, x1, y1 = font.getbbox(ch, anchor='ls')
        w, h = x1 - x0, y1 - y0
        if w <= 0 or h <= 0:
            continue
        image = Image.new('L', (w, h), 0)
        ImageDraw.Draw(image).text((-x0, -y0), ch, font=font, fill=255, anchor='ls')
        pixels = image.tobytes()
        if max(pixels) == 0:
            continue
        compressor = zlib.compressobj(9, zlib.DEFLATED, -15)
        blob = compressor.compress(pixels) + compressor.flush()
        entries.append((cp, advance, x0, -y0, w, h, offset, len(blob)))
        blobs.append(blob)
        offset += len(blob)
    with open(dst, 'wb') as out:
        out.write(b'ECSG')
        out.write(struct.pack('<iiiii', 1, size, ascent, descent, len(entries)))
        for e in entries:
            out.write(struct.pack('<ihhhhhii', *e))
        for blob in blobs:
            out.write(blob)
    print(f'{len(entries)} glyphs, {offset / 1e6:.1f} MB data, ascent={ascent} descent={descent}')


if __name__ == '__main__':
    main()

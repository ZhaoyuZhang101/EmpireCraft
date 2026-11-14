"""Generate the transparent wardrobe drawing guides and their joint coordinates."""
import argparse
import json
from math import isfinite
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

DIRECTORY = Path(__file__).resolve().parent / 'portrait_templates'
CELL = (352, 544)
FLOOR = 537
LANDMARKS = {
    'chin': '下巴', 'neck': '颈根', 'shoulder_l': '左肩', 'shoulder_r': '右肩',
    'waist': '腰', 'hip_l': '左胯', 'hip_r': '右胯', 'crotch': '胯下',
    'knee_l': '左膝', 'knee_r': '右膝', 'foot_l': '左脚底', 'foot_r': '右脚底',
}
# Nominal drawing heights in cm, head counts, and vertical joint fractions
# from crown to sole. Children have a longer trunk relative to leg length.
PROFILES = (
    ('child', '幼年', (110, 109), 5.4, (.46, .54, .59, .79)),
    ('school', '学龄', (135, 134), 6.15, (.43, .51, .56, .77)),
    ('teen', '少年', (153, 150), 6.6, (.41, .49, .54, .76)),
    ('youth', '青年', (167, 157), 6.85, (.39, .48, .53, .75)),
    ('short', '成年矮档', (158, 148), 6.65, (.40, .49, .54, .76)),
    ('medium', '成年中档', (172, 160), 7.0, (.39, .47, .52, .75)),
    ('tall', '成年高档', (186, 174), 7.0, (.38, .46, .51, .74)),
)
# Fixed terminal drawing anchors at the native raster size.
# Left and right are from the viewer's perspective.
TERMINAL_MARKS = {
    'm': {
        'chin': (176, 128), 'neck': (176, 148),
        'shoulder_l': (123, 156), 'shoulder_r': (229, 156), 'waist': (176, 244),
        'hip_l': (136, 270), 'hip_r': (216, 270), 'crotch': (176, 280),
        'knee_l': (152, 408), 'knee_r': (200, 408),
        'foot_l': (150, 537), 'foot_r': (202, 537),
    },
    'f': {
        'chin': (176, 128), 'neck': (176, 148),
        'shoulder_l': (128, 156), 'shoulder_r': (224, 156), 'waist': (176, 244),
        'hip_l': (131, 270), 'hip_r': (221, 270), 'crotch': (176, 280),
        'knee_l': (155, 408), 'knee_r': (197, 408),
        'foot_l': (156, 537), 'foot_r': (196, 537),
    },
}
TERMINAL_ARMS = {
    'm': (((132, 251), (131, 312)), ((221, 251), (221, 312))),
    'f': (((132, 245), (165, 281)), ((219, 245), (186, 281))),
}


def make_template(name, title, sex, cm, ratio, stations):
    runtime = name == 'terminal'
    if runtime:
        return dict(id=f'terminal_{sex}', label=title + (' · 男版' if sex == 'm' else ' · 女版'),
                    sex=sex, cm=cm, heads=7.01, crown=60, marks=TERMINAL_MARKS[sex],
                    arms=TERMINAL_ARMS[sex])
    height = round(cm * 477 / 172)
    top = FLOOR - height
    head = round(height / ratio)
    chin = top + head
    neck = chin + round(height * .038)
    shoulders = neck + round(height * .019)
    waist, hips, crotch, knees = [top + round(height * fraction) for fraction in stations]
    center = CELL[0] // 2
    shoulders_w = round(height * (.112 if sex == 'm' else .100))
    hips_w = round(height * (.083 if sex == 'm' else .094))
    knees_w = round(height * (.050 if sex == 'm' else .044))
    feet_w = round(height * (.054 if sex == 'm' else .042))
    marks = {'chin': (center, chin), 'neck': (center, neck), 'waist': (center, waist),
             'crotch': (center, crotch)}
    for joint, y, half in [('shoulder', shoulders, shoulders_w), ('hip', hips, hips_w),
                           ('knee', knees, knees_w), ('foot', FLOOR, feet_w)]:
        marks[joint + '_l'] = (center - half, y)
        marks[joint + '_r'] = (center + half, y)
    return dict(id=f'{name}_{sex}', label=title + (' · 男版' if sex == 'm' else ' · 女版'),
                sex=sex, cm=cm, heads=round(height / (chin - top), 2),
                crown=top, marks=marks)


def guide(template):
    im = Image.new('RGBA', CELL)
    d = ImageDraw.Draw(im)
    m = template['marks']
    c = CELL[0] // 2
    top = template['crown']
    chin = m['chin'][1]
    head_radius = round((chin - top) * .36)
    d.ellipse((c - head_radius, top, c + head_radius, chin), outline=(67, 194, 216, 215), width=2)
    labels = {'chin': '下巴', 'neck': '颈根', 'shoulder_l': '双肩', 'waist': '腰',
              'hip_l': '双胯', 'crotch': '胯下', 'knee_l': '双膝', 'foot_l': '脚底'}
    rows = sorted([(top, '颅顶'), *((m[k][1], value) for k, value in labels.items())])
    try:
        font = ImageFont.truetype('msyh.ttc', 10)
    except OSError:
        font = None
    last_label = -12
    for y, label in rows:
        for x in range(24, 328, 8):
            d.line((x, y, x + 3, y), fill=(52, 186, 204, 120))
        label_y = max(y - 5, last_label + 11)
        last_label = label_y
        d.text((8, label_y), str(y), fill=(76, 209, 227, 250))
        if font:
            d.line((313, y, 318, label_y + 5), fill=(76, 209, 227, 150))
            d.text((319, label_y), label, font=font, fill=(76, 209, 227, 250))
    d.line((c, top, c, m['foot_l'][1]), fill=(61, 203, 223, 165), width=1)
    waist_span = round(abs(m['hip_l'][0] - c) * .77)
    for side, sign in [('l', -1), ('r', 1)]:
        shoulder, hip = m['shoulder_' + side], m['hip_' + side]
        waist = (c + sign * waist_span, m['waist'][1])
        knee, foot = m['knee_' + side], m['foot_' + side]
        color = (75, 205, 219, 205)
        d.line((m['neck'], shoulder), fill=color, width=2)
        d.line((m['waist'], waist, hip), fill=color, width=1)
        d.line((hip, knee, foot), fill=color, width=1)
        arm_x = shoulder[0] + sign * 16
        wrist_y = m['crotch'][1]
        if template['sex'] == 'f':
            wrist = (c + sign * 10, m['waist'][1] + 14)
        else:
            wrist = (arm_x + sign * 4, wrist_y)
        arm = template['arms'][0 if side == 'l' else 1] if 'arms' in template else ((arm_x, m['waist'][1]), wrist)
        d.line((shoulder, *arm), fill=color, width=1)
    for x, y in m.values():
        d.ellipse((x - 3, y - 3, x + 3, y + 3), fill=(255, 197, 105, 255))
    return im


def generate():
    DIRECTORY.mkdir(exist_ok=True)
    templates = [make_template('terminal', '终端现用', sex, 172, 7.0, (.39, .47, .52, .75))
                 for sex in ('m', 'f')]
    for name, title, heights, ratio, stations in PROFILES:
        templates.extend(make_template(name, title, sex, cm, ratio, stations)
                         for sex, cm in zip(('m', 'f'), heights))
    for entry in templates:
        guide(entry).save(DIRECTORY / (entry['id'] + '.png'))
    data = dict(cell=CELL, landmarks=LANDMARKS, templates=templates)
    (DIRECTORY / 'index.json').write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'{len(templates)} wardrobe templates')


def validate_marks(data, index):
    if not isinstance(data, dict):
        raise ValueError('标点文件必须为对象')
    template = next((t for t in index['templates'] if t['id'] == data.get('template')), None)
    if not template or data.get('canvas') != list(CELL):
        raise ValueError('模板 ID 或标点画布尺寸不符')
    points = data.get('landmarks', {})
    if not isinstance(points, dict):
        raise ValueError('标点列表无效')
    bad = []
    for key, target in template['marks'].items():
        point = points.get(key)
        if (not isinstance(point, list) or len(point) != 2
                or any(type(v) not in (int, float) or not isfinite(v) or abs(v - t) > 2
                       for v, t in zip(point, target))):
            bad.append(LANDMARKS[key])
    if bad:
        raise ValueError('标点缺失或偏离模板超过 2 像素：' + '、'.join(bad))
    return template


def check(image_path, marks_path):
    index = json.loads((DIRECTORY / 'index.json').read_text(encoding='utf-8'))
    data = json.loads(Path(marks_path).read_text(encoding='utf-8-sig'))
    template = validate_marks(data, index)
    with Image.open(image_path) as image:
        if image.format != 'PNG' or image.size != CELL or 'A' not in image.getbands():
            raise ValueError('衣装须为 352×544 带透明通道的 PNG')
    print(template['label'] + ' · 标点全部通过')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', nargs=2, metavar=('PNG', 'JSON'))
    args = parser.parse_args()
    if args.check:
        try:
            check(*args.check)
        except (OSError, ValueError, AttributeError) as error:
            raise SystemExit(str(error)) from error
    else:
        generate()


if __name__ == '__main__':
    main()

"""Apply the sourced 2026-11-02 preset corrections without reformatting unrelated data.

Run after the other WorldMap data generators; --check verifies without writing.
Sources, snapshot choices and unresolved boundaries are recorded in
Docs/preset-historical-audit-2026-11-02.md.
"""
import argparse
import copy
import json
import pathlib
import re


def items(text, key):
    match = re.search(r'^  "' + re.escape(key) + r'"\s*:\s*\[', text, re.M)
    if not match:
        raise ValueError("Missing root array " + key)
    decoder = json.JSONDecoder()
    pos = match.end()
    result = []
    while True:
        while text[pos].isspace() or text[pos] == ',':
            pos += 1
        if text[pos] == ']':
            return result
        value, end = decoder.raw_decode(text, pos)
        result.append((pos, end, value))
        pos = end


def patch_array(text, key, desired):
    spans = items(text, key)
    wanted = {item['id']: item for item in desired}
    for start, end, old in reversed(spans):
        if old['id'] in wanted and old != wanted[old['id']]:
            text = text[:start] + json.dumps(wanted[old['id']], ensure_ascii=False) + text[end:]
    existing = {item['id'] for _, _, item in spans}
    additions = [item for item in desired if item['id'] not in existing]
    if additions:
        spans = items(text, key)
        end = spans[-1][1]
        extra = ''.join(',\n    ' + json.dumps(item, ensure_ascii=False) for item in additions)
        text = text[:end] + extra + text[end:]
    return text


def corrections(era, cities=None):
    result = copy.deepcopy(era)
    realms = {realm['id']: realm for realm in result['polities']}
    if era['id'] == 'roman_empire':
        rome = realms['rome']
        rome['ruler'] = dict(age=63, fam='乌尔皮乌斯|Ulpius|烏爾皮烏斯', giv='图拉真|Trajan|圖拉真')
        rome['kin'] = [dict(fam='', giv='庞培娅·普洛蒂娜|Pompeia Plotina|龐培婭·普洛蒂娜', age=47, rel='wife')]
        # Assyria's short-lived provincial status is disputed; do not call
        # Ctesiphon, Elymais and Characene one securely attested Roman province.
        for division in rome.get('divisions', []):
            if division['name'].split('|')[0] == '美索不达米亚' and 'ctesiphon' not in division['cities']:
                division['cities'].append('ctesiphon')
        rome['divisions'] = [d for d in rome['divisions'] if d['name'].split('|')[0] != '亚述']
        realms['characene']['cities'] = [c for c in realms['characene']['cities'] if c != 'susa']
        if 'susa' not in realms['parthia'].setdefault('cities', []):
            realms['parthia']['cities'].append('susa')
        realms['han']['r'] = [r for r in realms['han']['r'] if r != 'xiyu']
        for pid, zh, en, tw, capital in [
            ('shule', '疏勒', 'Shule', '疏勒', 'kashgar'),
            ('yutian', '于阗', 'Khotan', '于闐', 'khotan'),
            ('jushi', '车师', 'Jushi', '車師', 'turpan'),
        ]:
            if pid not in realms:
                result['polities'].append(dict(id=pid, zh=zh, en=en, tw=tw, c='Persepolis', cap=capital, cities=[capital]))
    elif era['id'] == 'high_tang':
        realms['balhae']['cap'] = 'hyeondeok'
    elif era['id'] == 'crusades':
        # 天眷新制以后金用三省六部，国号称大金朝
        realms['jin']['court'] = 'Huaxia'
        result['dzh'] = '1190年春，第三次十字军东征。腓特烈一世尚在远征途中，阿卡仍由阿尤布守军控制，十字军正围城；塞浦路斯由伊萨克自立。东方金与南宋隔淮对峙，蒙古诸部尚在草原混战。'
        result['dtw'] = '1190年春，第三次十字軍東征。腓特烈一世尚在遠征途中，阿卡仍由阿尤布守軍控制，十字軍正圍城；塞浦路斯由伊薩克自立。東方金與南宋隔淮對峙，蒙古諸部尚在草原混戰。'
        result['den'] = 'Spring AD 1190, during the Third Crusade. Frederick I is still campaigning; Acre remains under its Ayyubid garrison and under crusader siege. Isaac rules Cyprus independently. In the east, Jin and Southern Song face each other across the Huai while the Mongol tribes remain divided.'
        realms['hre']['ruler'] = dict(age=68, fam='霍亨施陶芬|Hohenstaufen|霍亨施陶芬', giv='腓特烈一世|Frederick I|腓特烈一世')
        realms['hre']['kin'] = [
            dict(fam='霍亨施陶芬|Hohenstaufen|霍亨施陶芬', giv='亨利|Henry|亨利', age=25, rel='son', heir=True),
            dict(fam='', giv='西西里的康斯坦丝|Constance of Sicily|西西里的康斯坦絲', age=36, rel='wife', of=0, house='sicily'),
        ]
        realms['jerusalem']['cap'] = 'tyre'
        realms['jerusalem']['cities'] = ['tyre']
        if 'acre' not in realms['ayyubid'].setdefault('cities', []):
            realms['ayyubid']['cities'].append('acre')
        realms['byzantium']['r'] = [r for r in realms['byzantium']['r'] if r != 'cyprus']
        realms['byzantium']['divisions'] = [d for d in realms['byzantium'].get('divisions', []) if 'cyprus' not in d.get('cities', [])]
        if 'cyprus' not in realms:
            result['polities'].append(dict(id='cyprus', zh='塞浦路斯', en='Cyprus', tw='塞浦路斯', c='Roma', cap='cyprus', r=['cyprus'],
                                          ruler=dict(fam='科穆宁|Komnenos|科穆寧', giv='伊萨克|Isaac|伊薩克')))
    elif era['id'] == 'lingao':
        realms['cambodia']['cap'] = 'oudong'
    # Respect the existing dated city table; stale province lists otherwise
    # advertise seats that the opening-world planner silently skips.
    if cities is not None:
        for polity in result['polities']:
            for division in polity.get('divisions', []):
                division['cities'] = [cid for cid in division.get('cities', []) if cid in cities and
                                      cities[cid].get('from', -99999) <= result['year'] <= cities[cid].get('to', 99999)]
    return result


NEW_CITIES = [
    dict(id='hyeondeok', zh='中京显德府', en='Junggyeong Hyeondeok', tw='中京顯德府',
         x=129.14, y=42.71, r='manchuria', k=2, **{'from': 742, 'to': 926}),
    dict(id='oudong', zh='乌栋', en='Oudong', tw='烏棟', x=104.7425, y=11.8239, r='khmer', k=2, **{'from': 1618}),
]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('root', type=pathlib.Path)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    changes = []
    city_path = args.root / 'WorldMaps/cities.json'
    city_data = {c['id']: c for c in json.loads(city_path.read_text(encoding='utf-8-sig'))['cities']}
    city_data.update({c['id']: c for c in NEW_CITIES})
    paths = [city_path] + sorted((args.root / 'WorldMaps/eras').glob('*.json'))
    for path in paths:
        raw = path.read_bytes()
        text = raw.decode('utf-8').replace('\r\n', '\n')
        before = json.loads(text)
        if path == city_path:
            after = copy.deepcopy(before)
            known = {c['id'] for c in after['cities']}
            after['cities'].extend(c for c in NEW_CITIES if c['id'] not in known)
            output = patch_array(text, 'cities', after['cities'])
        else:
            after = corrections(before, city_data)
            output = patch_array(text, 'polities', after['polities'])
            for key in ('dzh', 'dtw', 'den'):
                if after.get(key) != before.get(key):
                    match = re.search(r'^  "' + key + r'"\s*:\s*', output, re.M)
                    if match is None:
                        raise ValueError('Missing preset description ' + key)
                    _, length = json.JSONDecoder().raw_decode(output[match.end():])
                    output = output[:match.end()] + json.dumps(after[key], ensure_ascii=False) + output[match.end()+length:]
        assert json.loads(output) == after, path
        if before == after:
            continue
        changes.append(str(path))
        if not args.check:
            path.write_bytes(output.replace('\n', '\r\n' if b'\r\n' in raw else '\n').encode('utf-8'))
    if args.check and changes:
        raise SystemExit('Corrections missing: ' + ', '.join(changes))
    print('ok: historical preset corrections' if args.check else 'updated: ' + ', '.join(changes))


if __name__ == '__main__':
    main()

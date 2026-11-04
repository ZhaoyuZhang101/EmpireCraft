"""Add conservative local-title defaults without changing historical polity claims.

Only use existing geographical units and legal regions whose dated cities all
belong to the declared polity. This is coarse administrative default data, not
a new set of surveyed historical borders. See Docs/wilderness-refinement-2026-11-02.md.
Run after the other historical/default generators; --check does not write.
"""
import argparse
import json
import pathlib
import re

import check_eras


# Interior areas of the project's existing broad regions. Border-spanning units
# (Qinling, Qilian, Taihang, Alps, Himalaya, etc.) deliberately remain subdividable.
REGIONS = {
    'luliang': 'hedong',
    'taebaek': 'korea_s',
    'negev': 'palestine',
    'judaean_desert': 'palestine',
    'simien': 'aksum',
    'mesopotamian_marshes': 'mesopotamia',
    'scottish_highlands': 'britain_n',
    'cantabrian': 'hispania_n',
    'sierra_nevada_es': 'hispania_s',
    'massif_central': 'gaul_s',
}
YEARS = {'warring_states': -260, 'roman_empire': 117, 'three_kingdoms': 230,
         'high_tang': 750, 'charlemagne': 800, 'crusades': 1190, 'song_jin': 1140, 'lingao': 1628, 'ming_qing': 1638}


def defaults(base, era):
    if YEARS.get(era['id']) != era['year']:
        raise ValueError('Unexpected historical snapshot: ' + era['id'])
    cities, owners, problems = check_eras.plan(base, era)
    if problems:
        raise ValueError('; '.join(problems))
    result = {}
    for unit, region in REGIONS.items():
        polity = era.get('wilderness', {}).get(unit)
        local = [city for city in cities.values() if city['r'] == region]
        if polity and local and all(owners.get(city['id']) == polity for city in local) and any(
                era.get('ranks', {}).get(city['id'], city.get('k', 2)) <= 2 for city in local):
            result[unit] = region
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('root', type=pathlib.Path)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    base, eras = check_eras.load(str(args.root))
    units = {u['id'] for u in json.loads((args.root/'WorldMaps/wilderness.json').read_text(encoding='utf-8-sig'))['units']}
    assert set(REGIONS) <= units
    missing, total = [], 0
    for era in eras:
        desired = defaults(base, era)
        total += len(desired)
        if era.get('wilderness_regions', {}) == desired:
            continue
        missing.append(era['id'])
        if args.check:
            continue
        path = args.root/'WorldMaps/eras'/(era['id']+'.json')
        raw = path.read_bytes()
        text = raw.decode('utf-8-sig')
        value = json.dumps(desired, ensure_ascii=False)
        match = re.search(r'(?m)^  "wilderness_regions"\s*:\s*', text)
        if match:
            _, length = json.JSONDecoder().raw_decode(text[match.end():])
            updated = text[:match.end()] + value + text[match.end()+length:]
        else:
            at = re.search(r'(?m)^  "wilderness"\s*:', text)
            if at is None:
                raise ValueError('Missing wilderness defaults: ' + era['id'])
            newline = '\r\n' if b'\r\n' in raw else '\n'
            updated = text[:at.start()] + '  "wilderness_regions": ' + value + ',' + newline + text[at.start():]
        era['wilderness_regions'] = desired
        assert json.loads(updated) == era
        path.write_bytes(updated.encode('utf-8-sig' if raw.startswith(b'\xef\xbb\xbf') else 'utf-8'))
    if args.check and missing:
        raise SystemExit('Missing regional wilderness defaults: ' + ', '.join(missing))
    print(f'ok: {total} local wilderness title defaults across {len(eras)} presets')


if __name__ == '__main__':
    main()

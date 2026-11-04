"""Keep preset capitals and one seat per division visible using default rank data.

Adds rank=2 overrides for dated capitals, and where every listed, dated
non-capital division seat would be filtered from the smallest map.
Does not change city filtering or map rendering.
"""
import argparse
import json
import pathlib
import re


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('root', type=pathlib.Path)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    base = json.loads((args.root/'WorldMaps/cities.json').read_text(encoding='utf-8-sig'))
    cities = {c['id']: c for c in base['cities']}
    total = 0
    for path in sorted((args.root/'WorldMaps/eras').glob('*.json')):
        raw = path.read_bytes()
        text = raw.decode('utf-8-sig')
        era = json.loads(text)
        ranks = dict(era.get('ranks', {}))
        additions = {}
        for polity in era['polities']:
            capital = cities.get(polity.get('cap'))
            if capital is not None and capital.get('from', -99999) <= era['year'] <= capital.get('to', 99999) and \
                    ranks.get(capital['id'], capital.get('k', 2)) > 2:
                ranks[capital['id']] = additions[capital['id']] = 2
            for division in polity.get('divisions', []):
                candidates = [cities[cid] for cid in division.get('cities', []) if cid in cities and cid != polity.get('cap')
                              and cities[cid].get('from', -99999) <= era['year'] <= cities[cid].get('to', 99999)]
                if candidates and all(ranks.get(c['id'], c.get('k', 2)) > 2 for c in candidates):
                    seat = min(candidates, key=lambda c: (ranks.get(c['id'], c.get('k', 2)), c['id']))
                    ranks[seat['id']] = additions[seat['id']] = 2
        total += len(additions)
        if not additions:
            continue
        if args.check:
            print(path.stem, 'missing default visible seats:', ', '.join(additions))
            continue
        match = re.search(r'(?m)^  "ranks"\s*:\s*', text)
        value = json.dumps(ranks, ensure_ascii=False, separators=(', ', ': '))
        if match:
            _, length = json.JSONDecoder().raw_decode(text[match.end():])
            updated = text[:match.end()] + value + text[match.end()+length:]
        else:
            at = re.search(r'(?m)^  "polities"', text)
            if at is None:
                raise SystemExit('Cannot locate preset polities: '+str(path))
            nl = '\r\n' if '\r\n' in text else '\n'
            updated = text[:at.start()]+'  "ranks": '+value+','+nl+text[at.start():]
        era['ranks'] = ranks
        if json.loads(updated) != era:
            raise SystemExit('Unexpected unrelated preset change: '+str(path))
        path.write_bytes(updated.encode('utf-8-sig' if raw.startswith(b'\xef\xbb\xbf') else 'utf-8'))
        print(path.stem, 'default visible seats:', ', '.join(additions))
    if args.check and total:
        raise SystemExit(1)
    print('ok: default administrative seats' if not total else f'updated {total} default city ranks')


if __name__ == '__main__':
    main()

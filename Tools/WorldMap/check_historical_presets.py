"""Read-only checks for sourced historical snapshot corrections and known cities."""
import json
import pathlib
import sys

import check_eras


def main():
    root = pathlib.Path(sys.argv[1])
    base, eras = check_eras.load(str(root))
    by_era = {era['id']: era for era in eras}
    checks = 0

    def check(condition, message):
        nonlocal checks
        checks += 1
        if not condition:
            raise SystemExit(message)

    owners = {}
    for era in eras:
        cities, owner, problems = check_eras.plan(base, era)
        check(not problems, f"{era['id']}: " + '; '.join(problems))
        for polity in era['polities']:
            check(polity['cap'] in cities, f"{era['id']}: unavailable capital {polity['cap']}")
            check(owner.get(polity['cap']) == polity['id'], f"{era['id']}: stolen capital {polity['cap']}")
            rank = era.get('ranks', {}).get(polity['cap'], cities[polity['cap']].get('k', 2))
            check(rank <= 2, f"{era['id']}: capital absent on smallest map {polity['cap']}")
        owners[era['id']] = owner

    rome = next(p for p in by_era['roman_empire']['polities'] if p['id'] == 'rome')
    check('Trajan' in rome['ruler']['giv'], '117 preset no longer uses selected Trajan snapshot')
    check(any('Plotina' in p.get('giv', '') for p in rome['kin']), 'Trajan has wrong consort')
    check(not any('Sabina' in p.get('giv', '') for p in rome['kin']), 'Hadrian consort leaked into Trajan snapshot')
    check(not any(d['name'].split('|')[0] == '亚述' for d in rome['divisions']), 'Disputed Assyria treated as securely attested province')
    for city, polity in [('kashgar', 'shule'), ('khotan', 'yutian'), ('turpan', 'jushi'), ('susa', 'parthia'), ('ctesiphon', 'rome')]:
        check(owners['roman_empire'].get(city) == polity, f'117: {city} wrongly assigned')
    for city, polity in [('acre', 'ayyubid'), ('tyre', 'jerusalem'), ('cyprus', 'cyprus')]:
        check(owners['crusades'].get(city) == polity, f'1190: {city} wrongly assigned')
    hre = next(p for p in by_era['crusades']['polities'] if p['id'] == 'hre')
    check('Frederick I' in hre['ruler']['giv'], 'Spring 1190 emperor incorrectly replaced before June')
    check(hre['kin'][0].get('heir') and hre['kin'][1].get('of') == 0, 'Henry or Constance has wrong family role')
    check(next(p for p in by_era['high_tang']['polities'] if p['id'] == 'balhae')['cap'] == 'hyeondeok', '750 Balhae uses later capital')
    check(next(p for p in by_era['lingao']['polities'] if p['id'] == 'cambodia')['cap'] == 'oudong', '1628 Cambodia uses earlier capital')
    cities = {city['id']: city for city in base['cities']}
    check(742 <= cities['hyeondeok']['from'] <= 750, 'Hyeondeok absent in 750')
    check(1618 <= cities['oudong']['from'] <= 1628, 'Oudong absent in 1628')
    check(not check_eras.active(cities['hyeondeok'], 230) and not check_eras.active(cities['oudong'], 1190), 'New capitals appear before their era')
    print(f'ok: {checks} historical ownership, capital, date and smallest-map checks across seven presets')


if __name__ == '__main__':
    main()

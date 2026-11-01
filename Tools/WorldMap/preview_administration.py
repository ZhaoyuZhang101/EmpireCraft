"""Offline default-land preview using sampled earth and the original C# fallback partition.

City ownership uses check_eras' approximate distance assignment. Walls use the
scenario polylines, not the game's final terrain-routed wall construction. Thus
these images validate the division algorithm, not the complete game simulation.
Requires PowerShell 7, numpy, matplotlib, Pillow and tests/ScenarioAdministration.Tests.ps1.
"""
import argparse
import collections
import colorsys
import json
import math
import os
import pathlib
import subprocess

import numpy as np
from matplotlib.path import Path
from PIL import Image, ImageDraw, ImageFont

import check_eras
import preview_wilderness as earth


def polygon_mask(polygons, points):
    inside = np.zeros(len(points), dtype=bool)
    for polygon in polygons:
        if not polygon:
            continue
        hit = Path(polygon[0]).contains_points(points, radius=1e-9)
        for hole in polygon[1:]:
            hit &= ~Path(hole).contains_points(points, radius=1e-9)
        inside |= hit
    return inside


def fixture(root, era_id, frame, size, size_index, areas):
    tiles, types, degrees, bottom = earth.sample(str(root), frame, size)
    counts = earth.zone_counts(tiles, types)
    kind = earth.classify(tiles, types, counts)
    land = counts['land']
    h, w = land.shape
    travel = np.ones((h, w), dtype=float)
    share = lambda name: counts[name] / np.maximum(1, land)
    travel[share('hills') >= .25] = 1.6
    travel[(share('sand') >= .5) | (share('frozen') >= .5)] = 2
    travel[(share('jungle') + share('swamp')) >= .5] = 2.5
    travel[(share('hills') + share('rock')) >= .45] = 3
    travel[(kind == 2) | (kind == 3)] = 3
    travel[share('rock') >= .2] = 6
    base = json.loads((root / 'WorldMaps/cities.json').read_text(encoding='utf-8'))
    era = json.loads((root / 'WorldMaps/eras' / (era_id + '.json')).read_text(encoding='utf-8'))
    cities, owners, problems = check_eras.plan(base, era)
    polities = {p['id']: p for p in era['polities']}
    groups, labels, members, seeds = [], {}, collections.defaultdict(list), []

    def label_of(polity, division):
        key = (polity, division)
        if key not in labels:
            labels[key] = len(groups)
            groups.append(key)
        return labels[key]

    def position(lon, lat):
        x = math.floor((lon-frame['west'])/degrees)
        y = math.floor((lat-bottom)/degrees)
        return max(0, min(w-1, x//8)) + max(0, min(h-1, y//8))*w

    city_positions = {}
    for cid, city in cities.items():
        rank = era.get('ranks', {}).get(cid, city.get('k', 2))
        if size_index == 0 and rank > 2:
            continue
        x, y = (city['x']-frame['west'])/degrees, (city['y']-bottom)/degrees
        if not (4 <= x < w*8-4 and 4 <= y < h*8-4):
            continue
        polity_id = owners.get(cid, '@'+cid)
        polity = polities.get(polity_id, {})
        division = '_core'
        if cid != polity.get('cap'):
            division = next((d['name'].split('|')[0] for d in polity.get('divisions', []) if cid in d.get('cities', [])), '_core')
        label = label_of(polity_id, division)
        at = position(city['x'], city['y'])
        seeds.append((at, label, 24))
        members[label].append(cid)
        city_positions[cid] = at
        # The game opens the city centre before partitioning.
        land.flat[at], travel.flat[at] = 64, 1
    for pid, polity in polities.items():
        if not any(k[0] == pid for k in groups):
            continue
        for division, seats in [('_core', polity.get('seats', []))] + [(d['name'].split('|')[0], d.get('seats', [])) for d in polity.get('divisions', [])]:
            label = label_of(pid, division)
            seeds.extend((position(*point), label, 24) for point in seats)
    yy, xx = np.indices((h, w))
    points = np.column_stack((frame['west']+(xx.ravel()*8+4)*degrees, bottom+(yy.ravel()*8+4)*degrees))
    boundary = np.full(w*h, -1, dtype=int)
    bounded = []
    for area in areas:
        if area['era'] != era_id or area['year'] != era['year']:
            continue
        key = (area['polity'], area['division'])
        if key not in labels:
            continue
        label = labels[key]
        mask = polygon_mask(area['polygons'], points)
        boundary[(boundary < 0) & mask] = label
        bounded.append(label)
    # Same segment-crossing test as WorldMapZones; source polylines are an offline approximation.
    def side(o, p, q):
        return (p[0]-o[0])*(q[1]-o[1])-(p[1]-o[1])*(q[0]-o[0])
    blocked, protected = set(), {s[0] for s in seeds}
    for wall in era.get('walls', []):
        pts = wall.get('pts', [])
        for c, d in zip(pts, pts[1:]):
            x0 = max(0, int(((min(c[0], d[0])-frame['west'])/degrees-4)/8)-1)
            x1 = min(w-1, int(((max(c[0], d[0])-frame['west'])/degrees-4)/8)+1)
            y0 = max(0, int(((min(c[1], d[1])-bottom)/degrees-4)/8)-1)
            y1 = min(h-1, int(((max(c[1], d[1])-bottom)/degrees-4)/8)+1)
            for y in range(y0, y1+1):
                for x in range(x0, x1+1):
                    aidx = y*w+x
                    for dx, dy in [(1,0),(0,1),(1,1),(1,-1)]:
                        nx, ny = x+dx, y+dy
                        if not (0 <= nx < w and 0 <= ny < h):
                            continue
                        bidx = ny*w+nx
                        a, b = points[aidx], points[bidx]
                        if (side(c,d,a)>0) != (side(c,d,b)>0) and (side(a,b,c)>0) != (side(a,b,d)>0):
                            blocked.add((min(aidx,bidx), max(aidx,bidx)))
                            protected.update((aidx,bidx))
    f = dict(width=w, height=h, travel=travel.ravel().tolist(), land=land.ravel().tolist(),
             indexes=[s[0] for s in seeds], labels=[s[1] for s in seeds], budgets=[s[2] for s in seeds],
             boundaries=boundary.tolist(), bounded=bounded, blocked=sorted(blocked), protected=sorted(protected))
    meta = dict(groups=groups, degrees=degrees, bottom=bottom, frame=frame, land=land, kind=kind,
                members=members, city_positions=city_positions, name=era['zh'], problems=problems)
    return f, meta


def render(labels, meta, path, era):
    land = meta['land']
    h, w = land.shape
    labels = np.asarray(labels).reshape(h, w)
    rgb = np.full((h, w, 3), [30, 48, 65], dtype=np.uint8)
    rgb[land >= 16] = [91, 96, 91]
    for label, (polity, division) in enumerate(meta['groups']):
        hue = ((label * .61803398875) % 1)
        color = np.array(colorsys.hsv_to_rgb(hue, .43, .84))*255
        rgb[labels == label] = color.astype(np.uint8)
    # Fine boundaries: one stable administrative edge, regardless of city/wilderness terrain type.
    edge = np.zeros((h, w), dtype=bool)
    edge[:,1:] |= (labels[:,1:] != labels[:,:-1]) & (land[:,1:] >= 16) & (land[:,:-1] >= 16)
    edge[1:,:] |= (labels[1:,:] != labels[:-1,:]) & (land[1:,:] >= 16) & (land[:-1,:] >= 16)
    rgb[edge] = (rgb[edge]*.5).astype(np.uint8)
    # Terrain remains a light texture, with no extra administrative outline.
    rgb[meta['kind'] > 0] = (rgb[meta['kind'] > 0]*.85).astype(np.uint8)
    crop = (-13,19,45,59) if era == 'roman_empire' else (78,16,137,54)
    frame, degrees, bottom = meta['frame'], meta['degrees'], meta['bottom']
    x0 = max(0,int((crop[0]-frame['west'])/(degrees*8)));x1=min(w,int((crop[2]-frame['west'])/(degrees*8)))
    y0 = max(0,int((crop[1]-bottom)/(degrees*8)));y1=min(h,int((crop[3]-bottom)/(degrees*8)))
    zoom = 9
    map_image=Image.fromarray(rgb[y0:y1,x0:x1][::-1]).resize(((x1-x0)*zoom,(y1-y0)*zoom),Image.Resampling.NEAREST)
    img=Image.new('RGB',(map_image.width,map_image.height+76),(22,29,35));img.paste(map_image,(0,76))
    draw=ImageDraw.Draw(img)
    font_path='C:/Windows/Fonts/msyh.ttc'
    font=ImageFont.truetype(font_path,18)
    small=ImageFont.truetype(font_path,12)
    draw.text((12,9),meta['name']+' · 默认地块数据预览',font=font,fill='white')
    draw.text((12,39),'原有无人区推算 + 历史默认范围覆盖；非游戏平滑显示截图',font=small,fill='#b8c1c8')
    for label, (polity, division) in enumerate(meta['groups']):
        ys,xs=np.where(labels[y0:y1,x0:x1]==label)
        if len(xs)<8 or division == '_core' or polity.startswith('@'):
            continue
        # Place text inside the largest component rather than between separate islands.
        from scipy.ndimage import label as connected
        components, num=connected(labels[y0:y1,x0:x1]==label)
        if not num:
            continue
        largest=max(range(1,num+1),key=lambda n:np.count_nonzero(components==n))
        ys,xs=np.where(components==largest)
        cx,cy=int(np.median(xs)),int(np.median(ys))
        x,y=(cx+.5)*zoom,(y1-y0-cy-.5)*zoom+76
        box=draw.textbbox((x,y),division,font=small,anchor='mm')
        draw.rectangle((box[0]-2,box[1]-1,box[2]+2,box[3]+1),fill='#252b30')
        draw.text((x,y),division,font=small,fill='white',anchor='mm')
    img.save(path)


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('root',type=pathlib.Path);p.add_argument('--out',type=pathlib.Path,required=True)
    p.add_argument('--era',action='append');p.add_argument('--size',type=int,choices=[0,1,2],default=1)
    args=p.parse_args();root=args.root.resolve();args.out.mkdir(parents=True,exist_ok=True)
    areas=json.loads((root/'WorldMaps/administrative_boundaries.json').read_text(encoding='utf-8'))['boundaries']
    frame=earth.FRAMES['eurasia'];size=frame['sizes'][args.size]
    eras=args.era or [f.stem for f in sorted((root/'WorldMaps/eras').glob('*.json'))]
    report=[]
    for era in eras:
        f,meta=fixture(root,era,frame,size,args.size,areas)
        path=args.out/(era+f'-{args.size}.fixture.json');path.write_text(json.dumps(f,separators=(',',':')),encoding='utf-8')
        env=os.environ.copy();env['EMPIRECRAFT_ADMIN_FIXTURE']=str(path.resolve())
        result=subprocess.run(['pwsh','-NoProfile','-File',str(root/'tests/ScenarioAdministration.Tests.ps1')],env=env,cwd=root,text=True,capture_output=True)
        if result.returncode:
            raise SystemExit(result.stdout+result.stderr)
        labels=json.loads(path.with_suffix(path.suffix+'.labels.json').read_text(encoding='utf-8-sig'))
        render(labels,meta,args.out/(era+f'-{args.size}.png'),era)
        row=dict(era=era,size=args.size,seeds=len(f['indexes']),cells=sum(x>=0 for x in labels),historical_cells=sum(x>=0 for x in f['boundaries']))
        report.append(row);print(json.dumps(row),flush=True)
    (args.out/f'report-{args.size}.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')


if __name__=='__main__':
    main()

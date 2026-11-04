"""Build schematic, shared-edge Ming core administrative defaults for Lingao 1628.

Manually reconstructed against the user's Ming provincial/巡抚 reference maps
(2026-11-05), using the preset's actual cities for georeferencing. These are
approximate administrative defaults, not survey data or a 1433 political claim.
Shared edges avoid independent nearest-seat islands. Only Lingao is emitted;
Han/Tang/Warring States retain their own dated jurisdictions.

Usage: python ming_administration_source.py ROOT [--preview output.png]
Generation/QA requires shapely; the game reads only the generated JSON.
"""
import argparse
import json
from pathlib import Path
from shapely.geometry import LineString, Point, Polygon, mapping
from shapely.ops import polygonize, unary_union

# Clockwise core envelope; sea portions are clipped by the game's actual land.
# Tibet is not included. The western extension is the existing Hexi/Xining
# administrative corridor, not a nearest-city claim over the plateau.
OUTLINE = [
    (94.0,40.3),(97.0,40.3),(99.3,40.3),(100.8,39.4),(103.0,39.2),
    (104.9,38.9),(106.7,39.3),(108.5,39.3),(111.0,39.6),(112.2,40.3),
    (114.4,40.5),(116.7,40.7),(119.8,40.2),(120.4,39.7),(119.4,39.0),
    (118.7,38.7),(118.5,38.1),(117.9,38.1),(119.4,37.9),(120.6,38.0),
    (122.8,37.8),(122.8,36.5),(121.0,35.5),(119.8,35.2),(120.7,33.7),
    (121.8,32.0),(122.2,31.0),(121.3,30.7),(122.6,30.4),(122.3,28.9),
    (121.2,27.6),(120.7,27.3),(119.7,25.5),(118.2,24.0),(117.2,23.5),
    (115.4,22.5),(113.6,21.8),(112.0,21.3),(111.0,20.2),(109.5,20.2),
    (109.5,21.1),(109.7,21.4),(108.0,21.0),(107.3,21.5),(106.7,22.6),
    (105.8,22.7),(104.8,22.5),(103.9,22.6),(102.5,22.3),(101.5,21.2),
    (100.4,21.4),(99.3,22.1),(98.5,24.0),(97.5,24.5),(97.5,26.0),
    (98.3,27.7),(99.1,28.8),(98.1,29.5),(98.7,31.0),(100.5,32.0),
    (101.8,32.6),(103.3,33.8),(102.0,35.0),(101.0,36.3),(99.5,37.3),
    (97.8,38.2),(96.3,38.7),(94.0,39.0),(94.0,40.3),
]

# Junctions and shared river/mountain-aligned administrative edges.
A=(110.3,34.6); B=(113.5,36.2); C=(115.7,36.2); D=(116.4,34.6)
E=(115.6,31.5); F=(110.3,32.6); G=(108.7,28.5); H=(116.7,29.9)
I=(113.2,25.2); J=(118.3,29.4); K=(118.5,28.3); L=(116.3,24.7)
M=(111.6,24.7); N=(109.3,26.3); P=(104.4,27.9); Q=(105.3,24.8)
EDGES = [
    [(111.0,39.6),(111.0,38.9),(110.5,38.0),(110.8,37.4),(110.5,36.7),(110.4,35.8),(110.2,35.2),A],
    [(114.4,40.5),(114.3,39.6),(114.6,39.0),(113.6,38.0),(113.6,37.2),B],
    [A,(111.0,34.9),(111.3,35.3),(112.0,35.1),(112.6,35.3),(113.1,35.3),B],
    [B,(114.6,36.3),C],
    [C,(116.2,37.0),(116.6,37.4),(117.2,37.5),(117.9,38.1)],
    [C,(115.9,35.6),(115.5,35.0),D],
    [D,(117.0,34.8),(117.5,34.5),(118.1,34.6),(118.9,35.0),(119.8,35.2)],
    [D,(115.7,34.2),(115.5,33.6),(115.8,32.5),E],
    [A,(110.8,34.1),(110.5,33.7),(110.8,33.1),F],
    [F,(111.4,32.6),(112.2,32.3),(113.0,32.1),(113.7,31.7),(114.7,31.7),E],
    [(103.3,33.8),(104.5,33.1),(105.7,32.8),(106.4,32.6),(107.5,32.6),(108.1,32.2),(109.0,32.4),F],
    [F,(110.0,31.8),(109.5,31.4),(109.0,30.9),(109.2,30.1),(108.6,29.5),G],
    [E,(116.2,31.1),(116.6,30.7),(116.5,30.2),H],
    [H,(116.0,29.5),(115.2,29.8),(114.8,29.4),(114.3,28.8),(113.9,27.7),(113.7,26.6),(113.7,25.5),I],
    [H,(117.0,29.8),(117.7,29.8),J],
    [J,(118.8,30.1),(119.3,31.1),(120.3,31.0),(120.9,31.0),(121.3,30.7)],
    [J,(118.2,28.9),K],
    [K,(119.1,27.9),(119.6,27.6),(120.7,27.3)],
    [K,(117.8,27.6),(117.2,27.4),(116.8,26.6),(116.0,25.8),(116.1,25.1),L],
    [L,(116.9,24.0),(117.2,23.5)],
    [I,(114.3,24.8),(115.2,24.6),L],
    [I,(112.5,25.2),(111.9,24.9),M],
    [G,(109.0,28.0),(109.2,27.6),(109.5,27.0),N],
    [N,(110.2,26.0),(110.8,25.4),(111.1,25.1),M],
    [G,(108.0,28.3),(107.3,28.0),(107.2,27.4),(106.6,27.1),(106.1,27.6),(105.4,27.9),P],
    [P,(103.9,28.2),(103.1,28.3),(102.8,27.9),(102.2,28.1),(101.6,27.7),(101.1,28.2),(100.3,28.0),(99.1,28.8)],
    [P,(104.8,27.0),(104.7,26.3),(104.6,25.6),Q],
    [Q,(106.0,24.7),(106.8,25.3),(107.3,25.4),(108.0,25.5),(108.8,25.9),N],
    [Q,(105.7,24.2),(105.4,23.5),(106.0,22.9),(106.7,22.6)],
    [M,(111.8,24.0),(111.6,23.5),(111.3,23.3),(110.7,22.4),(110.3,22.2),(109.7,21.4)],
]
SEEDS = {
    '_core':(116.0,39.0), '山西':(112.2,37.7), '陕西':(108.0,35.0),
    '山东':(118.0,36.5), '河南':(113.5,33.5), '南京':(118.0,32.0),
    '四川':(104.5,30.5), '湖广':(112.0,28.5), '江西':(115.5,27.5),
    '浙江':(120.0,29.0), '福建':(118.0,26.0), '广东':(113.5,23.0),
    '广西':(108.0,23.5), '贵州':(106.5,26.5), '云南':(101.5,25.0),
}


def build():
    outline = Polygon(OUTLINE)
    if not outline.is_valid:
        raise ValueError('Invalid core envelope')
    faces = list(polygonize(unary_union([LineString(OUTLINE), *map(LineString, EDGES)])))
    areas = {}
    for face in faces:
        if not outline.covers(face.representative_point()):
            continue
        names = [name for name, point in SEEDS.items() if face.covers(Point(point))]
        if len(names) != 1:
            raise ValueError(f'Face has {names}: {face.bounds}')
        if names[0] in areas:
            raise ValueError('Repeated region '+names[0])
        areas[names[0]] = face
    if set(areas) != set(SEEDS):
        raise ValueError('Missing regions: '+str(set(SEEDS)-set(areas)))
    if abs(sum(p.area for p in areas.values())-outline.area) > 1e-6:
        raise ValueError('Overlapping or missing administrative area')
    # Guangdong's Hainan is a real island component, not a spurious enclave.
    hainan = Polygon([(108.5,19.7),(109.2,18.1),(110.2,18.0),(111.2,19.5),(110.7,20.3),(109.6,20.4),(108.5,19.7)])
    return areas, hainan


def main():
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('root',type=Path);ap.add_argument('--preview',type=Path)
    args=ap.parse_args();areas,hainan=build()
    era=json.loads((args.root/'WorldMaps/eras/lingao.json').read_text(encoding='utf-8-sig'))
    ming=next(p for p in era['polities'] if p['id']=='ming')
    valid={'_core'} | {d['name'].split('|')[0] for d in ming['divisions']}
    if set(areas) != valid:raise ValueError('Preset names differ from boundary defaults')
    rows=[]
    for name,area in areas.items():
        polygons=[mapping(area)['coordinates']]
        if name=='广东':polygons.append(mapping(hainan)['coordinates'])
        rows.append(dict(era='lingao',polity='ming',division=name,year=1628,
            precision='schematic-reference',source='User-supplied Ming administrative / 巡抚 reference maps, 2026-11-05; manually reconstructed shared edges',polygons=polygons))
    out=args.root/'WorldMaps/administrative_boundaries_ming.json'
    out.write_text(json.dumps(dict(description='Ming core administrative defaults; approximate shared-edge reconstruction for 1628, not 1433 territorial extent.',boundaries=rows),ensure_ascii=False,separators=(',',':'))+'\n',encoding='utf-8')
    print(f'Wrote {len(rows)} Ming core regions, no overlapping interior faces; Hainan retained')
    if args.preview:
        import matplotlib
        matplotlib.use('Agg')
        import matplotlib.pyplot as plt
        plt.rcParams['font.sans-serif']=['Microsoft YaHei','SimHei','DejaVu Sans']
        fig,ax=plt.subplots(figsize=(11,10))
        for n,(name,p) in enumerate(areas.items()):
            x,y=p.exterior.xy;ax.fill(x,y,facecolor=plt.cm.Set3(n%12),edgecolor='#525252',linewidth=.8)
            point=p.representative_point();ax.text(point.x,point.y,'北直隶' if name=='_core' else '南直隶' if name=='南京' else name,ha='center',fontsize=11)
        ax.fill(*hainan.exterior.xy,facecolor=plt.cm.Set3(list(areas).index('广东')%12),edgecolor='#525252',linewidth=.8)
        cs=json.loads((args.root/'WorldMaps/cities.json').read_text(encoding='utf-8-sig'))['cities']
        lookup={c['id']:c for c in cs};miss=[]
        for division in ming['divisions']:
            name=division['name'].split('|')[0]
            for cid in division['cities']:
                c=lookup[cid];point=Point(c['x'],c['y'])
                if not (areas[name].covers(point) or name=='广东' and hainan.covers(point)):miss.append((cid,name))
                ax.plot(c['x'],c['y'],'.',color='#333',markersize=3)
        if miss:raise ValueError('Administrative cities outside reference bounds: '+str(miss))
        ax.set(xlim=(93,124),ylim=(17,42),xlabel='Longitude',ylabel='Latitude',title='临高1628 · 明核心行政辖境（示意重建，非测绘边界）')
        ax.set_aspect(1.13);ax.grid(alpha=.15);fig.tight_layout();args.preview.parent.mkdir(parents=True,exist_ok=True)
        fig.savefig(args.preview,dpi=130);plt.close(fig)
        print('All listed Ming administrative cities lie within their intended default regions; preview:',args.preview)


if __name__=='__main__':main()

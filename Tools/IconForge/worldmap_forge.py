"""「世界地图」模板按钮的图标：原版新世界窗口的模板图标是 50×50 的小地图缩略图，这里同样画一张旧大陆缩略图。

直接从 WorldMaps/earth.bytes(地球地表网格，见 Tools/WorldMap/build_earth.py)取样：经度 20°W~150°E、纬度 35°S~78°N，
按块取众数(海陆各算各的，陆地占三成以上就画陆地)，配色取原版地块的近似色，外圈 1 像素黑框，与原版模板图标一致。

输出：GameResources/ui/new_world_templates_icons/template_empirecraft_world_map.png(50×50)
用法: python Tools/IconForge/worldmap_forge.py [--preview [输出路径]]
"""
import os
import struct
import sys
import zlib

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
SIZE = 50
WEST, EAST, SOUTH, NORTH = -20.0, 150.0, -35.0, 78.0

COLORS = {
    "deep_ocean": (38, 86, 178), "close_ocean": (52, 112, 206), "shallow_waters": (78, 160, 232),
    "sand": (232, 214, 140), "mountains": (92, 88, 84), "mountains:snow_block": (236, 240, 246),
    "hills": (122, 118, 98), "hills:snow_hills": (206, 212, 222),
    "grass": (84, 156, 54), "savanna": (196, 182, 84), "jungle": (34, 112, 40), "desert": (238, 214, 136),
    "permafrost": (196, 210, 222), "birch": (96, 140, 76), "maple": (118, 152, 62), "swamp": (82, 102, 56),
}


def load_grid(path):
    with open(path, "rb") as f:
        assert f.read(8) == b"ECEARTH1"
        cols, rows, lon0, lat0, step = struct.unpack("<iifff", f.read(20))
        count = struct.unpack("<i", f.read(4))[0]
        types = [f.read(f.read(1)[0]).decode("utf-8") for _ in range(count)]
        length = struct.unpack("<i", f.read(4))[0]
        grid = np.frombuffer(zlib.decompress(f.read(length), -15), np.uint8).reshape(rows, cols)
    return grid, types, lon0, lat0, step


def color_of(name):
    if name in COLORS:
        return COLORS[name]
    biome = name.split(":")[1].rsplit("_", 1)[0]
    base = COLORS.get(biome, COLORS["grass"])
    return tuple(int(c * 0.88) for c in base) if name.endswith("_high") else base


def make_icon():
    grid, types, lon0, lat0, step = load_grid(os.path.join(ROOT, "WorldMaps", "earth.bytes"))
    water = {i for i, t in enumerate(types) if t in ("deep_ocean", "close_ocean", "shallow_waters")}
    img = np.zeros((SIZE, SIZE, 4), np.uint8)
    for py in range(SIZE):
        lat_hi = NORTH - (NORTH - SOUTH) * py / SIZE
        lat_lo = NORTH - (NORTH - SOUTH) * (py + 1) / SIZE
        r0, r1 = int((lat_lo - lat0) / step), int((lat_hi - lat0) / step)
        for px in range(SIZE):
            lon_lo = WEST + (EAST - WEST) * px / SIZE
            lon_hi = WEST + (EAST - WEST) * (px + 1) / SIZE
            c0, c1 = int((lon_lo - lon0) / step), int((lon_hi - lon0) / step)
            block = grid[max(r0, 0):max(r1, 1), max(c0, 0):max(c1, 1)].ravel()
            land = block[~np.isin(block, list(water))]
            pick = block if len(land) < 0.3 * len(block) else land
            code = np.bincount(pick).argmax()
            img[py, px, :3] = color_of(types[code])
            img[py, px, 3] = 255
    img[0, :, :3] = img[-1, :, :3] = img[:, 0, :3] = img[:, -1, :3] = 0
    return Image.fromarray(img, "RGBA")


def build():
    return {"GameResources/ui/new_world_templates_icons/template_empirecraft_world_map.png": make_icon()}


if __name__ == "__main__":
    icons = build()
    for path, im in icons.items():
        full = os.path.join(ROOT, path)
        os.makedirs(os.path.dirname(full), exist_ok=True)
        im.save(full)
    if "--preview" in sys.argv:
        idx = sys.argv.index("--preview")
        out = sys.argv[idx + 1] if len(sys.argv) > idx + 1 else os.path.join(HERE, "worldmap_preview.png")
        im = next(iter(icons.values()))
        im.resize((SIZE * 6, SIZE * 6), Image.NEAREST).save(out)
        print("preview", out)

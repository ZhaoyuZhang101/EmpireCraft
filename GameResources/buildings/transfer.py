from pathlib import Path
from PIL import Image
import numpy as np
import shutil

# 是否备份原图：main_0.png -> main_0.png.bak
MAKE_BACKUP = True

# 是否递归处理所有子目录
RECURSIVE = True

# 处理的图片格式
IMAGE_EXTS = {".png"}

# 你的固定粉色调色板
# 对应：
# color_magenta_0 = #FF00FF
# color_magenta_1 = #DE00DE
# color_magenta_2 = #A700A7
# color_magenta_3 = #7F007F
# color_magenta_4 = #580058
# EVERYTHING_MAGIC_COLOR32 = #DF7FFF
MAGENTA_PALETTE = np.array([
    [0xFF, 0x00, 0xFF],  # 亮粉
    [0xDE, 0x00, 0xDE],  # 主粉
    [0xA7, 0x00, 0xA7],  # 暗粉
    [0x7F, 0x00, 0x7F],  # 深粉
    [0x58, 0x00, 0x58],  # 极暗轮廓粉
    [0xDF, 0x7F, 0xFF],  # 高光/偏淡粉紫
], dtype=np.int16)

def is_pink_mask(r, g, b, a):
    """
    识别需要被国家颜色替换的粉色区域。
    适配：
    - #FF00FF
    - #DE00DE
    - #A700A7
    - #7F007F
    - #580058
    - #E71DDE 这类带少量 G 的粉
    - #DF7FFF 这类高光粉紫

    尽量避免误伤红门、橙色装饰、黄色窗户、灰墙、绿树。
    """
    max_rb = np.maximum(r, b)
    min_rb = np.minimum(r, b)

    return (
            (a > 0) &

            # R/B 是主导色
            (r >= 70) &
            (b >= 70) &

            # R 和 B 相对接近，避免把纯红/橙色算进去
            (np.abs(r.astype(np.int16) - b.astype(np.int16)) <= 95) &

            # G 不能太强，除非是 #DF7FFF 这种官方高光色
            (
                    (g <= 125) |
                    (
                            (r >= 180) &
                            (b >= 200) &
                            (g <= 150)
                    )
            ) &

            # G 明显低于 R/B，粉色特征
            ((max_rb.astype(np.int16) - g.astype(np.int16)) >= 45) &

            # 排除太灰的颜色
            ((max_rb.astype(np.int16) - min_rb.astype(np.int16)) <= 110)
    )

def map_to_nearest_palette(rgb_pixels):
    """
    把粉色像素映射到最近的固定调色板颜色。
    使用 RGB 欧氏距离。
    """
    pixels = rgb_pixels.astype(np.int16)

    # shape:
    # pixels: N, 3
    # palette: 6, 3
    # diff: N, 6, 3
    diff = pixels[:, None, :] - MAGENTA_PALETTE[None, :, :]
    dist = np.sum(diff * diff, axis=2)

    indices = np.argmin(dist, axis=1)
    return MAGENTA_PALETTE[indices].astype(np.uint8)

def process_image(path: Path):
    img = Image.open(path).convert("RGBA")
    arr = np.array(img)

    r = arr[:, :, 0].astype(np.int16)
    g = arr[:, :, 1].astype(np.int16)
    b = arr[:, :, 2].astype(np.int16)
    a = arr[:, :, 3].astype(np.int16)

    mask = is_pink_mask(r, g, b, a)
    changed_pixels = int(mask.sum())

    if changed_pixels == 0:
        return 0

    if MAKE_BACKUP:
        backup_path = path.with_suffix(path.suffix + ".bak")
        if not backup_path.exists():
            shutil.copy2(path, backup_path)

    rgb_pixels = arr[:, :, :3][mask]
    mapped = map_to_nearest_palette(rgb_pixels)

    arr[:, :, :3][mask] = mapped

    Image.fromarray(arr, "RGBA").save(path)
    return changed_pixels

def main():
    root = Path(__file__).resolve().parent

    if RECURSIVE:
        files = [
            p for p in root.rglob("*")
            if p.suffix.lower() in IMAGE_EXTS
               and not p.name.endswith(".bak")
        ]
    else:
        files = [
            p for p in root.iterdir()
            if p.suffix.lower() in IMAGE_EXTS
               and not p.name.endswith(".bak")
        ]

    total_files = 0
    total_pixels = 0

    for path in files:
        changed = process_image(path)

        if changed > 0:
            total_files += 1
            total_pixels += changed
            print(f"已处理: {path.relative_to(root)} | 粉色像素映射: {changed}")

    print()
    print(f"完成。共处理图片: {total_files}")
    print(f"共映射粉色像素: {total_pixels}")
    print("粉色已固定为：#FF00FF / #DE00DE / #A700A7 / #7F007F / #580058 / #DF7FFF")

if __name__ == "__main__":
    main()
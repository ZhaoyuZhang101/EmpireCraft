"""EmpireCraft 文明乐加工：把下载来的 CC0 曲子统一成游戏里用的循环 OGG。

八系文明各配一两首(出处、授权见 Docs/CREDITS.md)，输出到 Music/civ/<文化系>_<n>.ogg：
  huaxia 华夏、japan 日本、western 西方、norse 北欧、arab 阿拉伯、steppe 草原、india 印度、america 美洲。
游戏里怎么放见 Scripts/GeneralSystems/CivMusic.cs。

不放 GameResources：NML 会把那里除图片、wav 以外的文件全当文本读进内存。

加工：
  · 解码(soundfile；zip 只在内存里读要的那一个成员，不解压、不执行任何东西)，转成立体声 44.1 kHz
    (原曲只有 16 kHz 的保持原采样率，免得白占码率)；
  · 统一响度：近似 K 加权 + 400 ms 门限的整体响度(仿 BS.1770)对到 TARGET_LUFS，峰值超过 -1 dBFS 的部分软限幅；
  · 循环：作者给了循环版、或注明能无缝循环的直接用，重采样时首尾环接，不起边缘效应；
    其余裁掉首尾静音，把结尾 XFADE 秒等功率交叉淡化到开头；
  · 编码：OGG Vorbis，compression_level 0.8(约 80 kbps，中等音质)。
同样的输入重跑结果一致(没有随机数)。下载来的文件当不可信数据处理：只解码音频。

用法(每首一个参数，键名见 TRACKS)：
  python -I Tools/SoundForge/civ_music_forge.py --huaxia_1 "Chinese Game Music v1.0.zip" --japan_1 hot_spring_town.ogg ...
  缺的那首跳过(已有的输出文件不动)。解码要 soundfile(pip install --user soundfile)。
"""
import argparse
import io
import math
import os
import site
import sys
import zipfile

import numpy as np
from scipy.signal import lfilter, resample_poly, sosfilt, butter

sys.path.append(site.getusersitepackages())
import soundfile as sf  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
OUT = os.path.join(ROOT, "Music", "civ")

RATE = 44100
TARGET_LUFS = -18.0
CEILING = 10 ** (-1.0 / 20)
XFADE = 4.0
COMPRESSION = 0.8
MAX_MEMBER = 200 * 1024 * 1024

# 输出名 → (zip 里取哪个成员或 None, 循环方式 seamless/xfade, 曲名, 作者)
TRACKS = {
    "huaxia_1": ("Lotus Pond - Loop.wav", "seamless", "Lotus Pond (Loop)", "BiteMe Games"),
    "japan_1": (None, "xfade", "Hot Springs Town", "Kistol"),
    "japan_2": (None, "xfade", "Oriental Somber", "Tozan"),
    "western_1": (None, "xfade", "Medieval: The Bard's Tale", "RandomMind"),
    "western_2": (None, "xfade", "Chant", "christislord"),
    "norse_1": (None, "xfade", "Viking March", "nightm4re"),
    "arab_1": (None, "xfade", "Desert theme", "yd"),
    "arab_2": (None, "xfade", "Frozen Desert", "Dizzy Crow"),
    "steppe_1": (None, "xfade", "Fantasy - Grasslands (Remastered)", "Doge"),
    "india_1": (None, "xfade", "Relics of Past", "Centurion_of_war"),
    "america_1": ("Kokopelli's Labyrinth Theme ~ Main Loop.wav", "seamless", "Kokopelli's Labyrinth Theme (Main Loop)",
                  "Cleyton Kauffman"),
    "america_2": (None, "seamless", "Krakatoa", "Kistol"),
}


def decode(path, member):
    """读一首：普通音频文件直接解码；zip 只在内存里读名字结尾对得上的那个成员。"""
    if member is None:
        data, rate = sf.read(path, always_2d=True)
        return data, rate
    with zipfile.ZipFile(path) as z:
        for info in z.infolist():
            if info.is_dir() or not info.filename.replace("\\", "/").endswith(member):
                continue
            if info.file_size > MAX_MEMBER:
                raise SystemExit(f"{member} 太大({info.file_size} 字节)")
            data, rate = sf.read(io.BytesIO(z.read(info)), always_2d=True)
            return data, rate
    raise SystemExit(f"{path} 里没有 {member}")


def stereo(x):
    if x.shape[1] == 1:
        return np.repeat(x, 2, axis=1)
    return x[:, :2]


def resample(x, src, dst, loop):
    """改采样率；循环曲首尾各接上对面一段再做，接缝处不起边缘效应。"""
    if src == dst:
        return x
    g = math.gcd(src, dst)
    up, down = dst // g, src // g
    if not loop:
        return resample_poly(x, up, down, axis=0)
    pad = down * max(1, int(0.2 * src) // down)
    y = resample_poly(np.concatenate([x[-pad:], x, x[:pad]]), up, down, axis=0)
    start = pad * up // down
    return y[start:start + len(x) * up // down]


def trim_silence(x, rate, thresh_db=-50.0):
    a = np.max(np.abs(x), axis=1)
    live = np.nonzero(a > a.max() * 10 ** (thresh_db / 20))[0]
    if len(live) == 0:
        return x
    return x[max(0, live[0] - int(0.01 * rate)):min(len(x), live[-1] + int(0.05 * rate))]


def loop_xfade(x, rate, seconds):
    """把结尾 seconds 秒等功率交叉淡化到开头，结果首尾相接无缝。"""
    n = int(seconds * rate)
    body = x[:len(x) - n].copy()
    u = np.linspace(0, 1, n)[:, None]
    body[:n] = x[:n] * np.sin(u * np.pi / 2) + x[len(x) - n:] * np.cos(u * np.pi / 2)
    return body


def k_weight(x, rate):
    """近似 K 加权：约 38 Hz 二阶高通 + 约 1.5 kHz 以上 +4 dB 的高架(RBJ 公式)。"""
    y = sosfilt(butter(2, 38 / (rate / 2), "highpass", output="sos"), x, axis=0)
    f0, gain_db, slope = 1500.0, 4.0, 1.0
    a = 10 ** (gain_db / 40)
    w0 = 2 * np.pi * f0 / rate
    alpha = np.sin(w0) / 2 * np.sqrt((a + 1 / a) * (1 / slope - 1) + 2)
    cos = np.cos(w0)
    b = [a * ((a + 1) + (a - 1) * cos + 2 * np.sqrt(a) * alpha), -2 * a * ((a - 1) + (a + 1) * cos),
         a * ((a + 1) + (a - 1) * cos - 2 * np.sqrt(a) * alpha)]
    den = [(a + 1) - (a - 1) * cos + 2 * np.sqrt(a) * alpha, 2 * ((a - 1) - (a + 1) * cos),
           (a + 1) - (a - 1) * cos - 2 * np.sqrt(a) * alpha]
    return lfilter(np.array(b) / den[0], np.array(den) / den[0], y, axis=0)


def loudness(x, rate):
    """整体响度(LUFS 近似)：400 ms 块、75% 重叠，-70 绝对门限 + 相对 -10 门限。"""
    y = k_weight(x, rate)
    block, hop = int(0.4 * rate), int(0.1 * rate)
    ms = np.array([np.sum(np.mean(y[i:i + block] ** 2, axis=0)) for i in range(0, len(y) - block, hop)])
    lk = -0.691 + 10 * np.log10(ms + 1e-12)
    gated = ms[lk > -70]
    if len(gated) == 0:
        return -70.0
    rel = -0.691 + 10 * np.log10(np.mean(gated)) - 10
    gated = ms[lk > rel]
    return float(-0.691 + 10 * np.log10(np.mean(gated)))


def soft_limit(x, ceiling=CEILING):
    """只压超过 ceiling 的峰：膝点 0.8×ceiling 以上平滑压到 ceiling 以内。"""
    knee = 0.8 * ceiling
    a = np.abs(x)
    over = a > knee
    y = x.copy()
    y[over] = np.sign(x[over]) * (knee + (ceiling - knee) * np.tanh((a[over] - knee) / (ceiling - knee)))
    return y


def write_ogg(path, x, rate):
    # libsndfile 1.2 一次写入大段 Vorbis 会崩，按块写
    os.makedirs(os.path.dirname(path), exist_ok=True)
    y = np.clip(x, -1, 1).astype(np.float32)
    with sf.SoundFile(path, "w", rate, y.shape[1], format="OGG", subtype="VORBIS", compression_level=COMPRESSION) as f:
        for i in range(0, len(y), 16384):
            f.write(y[i:i + 16384])


def wrap_jump(x):
    """循环接缝处的跳变 / 平常相邻样本差的 99 分位(≲1 算接得上)。"""
    d = np.abs(np.diff(x, axis=0)).max(axis=1)
    return float(np.max(np.abs(x[0] - x[-1])) / (np.percentile(d, 99) + 1e-9))


def process(name, path):
    member, mode, title, author = TRACKS[name]
    data, src = decode(path, member)
    x = stereo(np.asarray(data, dtype=np.float64))
    rate = src if src < 32000 else RATE
    seamless = mode == "seamless"
    if not seamless:
        x = trim_silence(x, src)
    x = resample(x, src, rate, loop=seamless)
    if not seamless:
        x = loop_xfade(x, rate, XFADE)
    x = x * 10 ** ((TARGET_LUFS - loudness(x, rate)) / 20)
    x = soft_limit(x)
    out = os.path.join(OUT, name + ".ogg")
    write_ogg(out, x, rate)
    check, _ = sf.read(out, always_2d=True)
    print(f"{name:10s} {len(x) / rate:6.1f}s {rate:5d}Hz  {loudness(check, rate):6.1f} LUFS  "
          f"peak {20 * np.log10(np.max(np.abs(check)) + 1e-12):5.1f} dBFS  seam {wrap_jump(check):4.2f}  "
          f"{os.path.getsize(out) / 1024:6.0f} KiB  {title} - {author}")
    return os.path.getsize(out)


def main():
    ap = argparse.ArgumentParser()
    for name, (member, _, title, author) in TRACKS.items():
        ap.add_argument("--" + name, default="", help=f"{title}({author})" + (f"，zip 里取 {member}" if member else ""))
    args = ap.parse_args()
    total = 0
    for name in TRACKS:
        path = getattr(args, name)
        if path:
            total += process(name, path)
    files = [f for f in os.listdir(OUT)] if os.path.isdir(OUT) else []
    size = sum(os.path.getsize(os.path.join(OUT, f)) for f in files)
    print(f"written {total / 1024 / 1024:.2f} MiB; Music/civ now {len(files)} files, {size / 1024 / 1024:.2f} MiB")


if __name__ == "__main__":
    main()

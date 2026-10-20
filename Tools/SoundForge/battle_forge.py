"""EmpireCraft 战声锻造器 —— 纯代码合成战场音效，不用任何外部素材。

时代(文件名前缀)：
  cold   冷兵器：刀剑相击、喊杀、箭雨、马蹄
  fire   火器：火绳枪齐射、前装炮
  modern 近代：步枪连射、机枪、炮击
  naval  海战：海浪、舰炮(闷远的炮声 + 水面回声)、弹着水柱

输出：
  GameResources/sounds/battle/<时代>_amb.wav          环境声，8 秒，首尾交叉淡化可无缝循环
  GameResources/sounds/battle/<时代>_<种类>_<n>.wav   单发变体
  Tools/SoundForge/preview/preview_<时代>.wav         每个时代 14 秒试听混音(环境声 + 按游戏里的配比随机插单发；不进游戏)

格式：16 位单声道 PCM；以中低频为主的(炮、舰炮、炸点、马蹄、远处的环境声)存 16000 Hz，其余 22050 Hz，省体积。
游戏里的读法与配比见 Scripts/GeneralSystems/Warfare/BattleSound.cs。

合成手法(都在下面各函数里)：
  · 金属：噪声短促激励一组非谐波二阶共振器(模态合成)，每个模成对略失谐出拍频，高次模衰减快；
  · 人声：声门脉冲(带抖动、颤音的谐波叠加 + 气声) → 四个共振峰滤波，元音从 u 滑到 a("呼——啊")，
    多人错开起声、分远中近三层各自压暗，不是滤过的白噪声，不会像风；
  · 火器：药池引燃的嘶声 + 爆裂 + 低频冲击，按远近低通、减弱，齐射按伽马分布参差排开，再加地形回声与混响；
  · 远近：越远越闷越轻(低通 + 衰减)；混响 = 指数衰减噪声脉冲响应(前段亮、后段暗) + 几道早期反射。
随机数种子固定(按文件名派生)，重跑结果逐字节一致。

用法：python Tools/SoundForge/battle_forge.py [--only 名字前缀] [--spectrogram 输出目录]
"""
import argparse
import os
import wave
import zlib

import numpy as np
from scipy.signal import butter, fftconvolve, iirpeak, lfilter, resample_poly, sosfilt

SR = 22050
NYQ = SR / 2
SEED = 20261021

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
OUT = os.path.join(ROOT, "GameResources", "sounds", "battle")
PREVIEW = os.path.join(HERE, "preview")

# 存盘采样率(按文件名前缀，先匹配先用)：低频为主的降到 16 kHz
SAVE_RATES = (("naval_splash", 22050), ("naval_", 16000), ("fire_cannon", 16000), ("fire_amb", 16000),
              ("modern_shell", 16000), ("modern_amb", 16000), ("cold_hooves", 16000))
# 单发最长几秒(混响尾巴再长也在这里淡出)
MAX_LEN = {"fire_cannon": 2.6, "naval_gun": 3.2, "naval_broadside": 4.2, "modern_shell": 3.4}

# 各种类的播放音量，与 BattleSound.cs 里的 KindGain 一致(试听混音按这个配比)
GAIN = {
    "amb": 0.5,
    "clash": 0.55, "warcry": 0.5, "arrows": 0.55, "hooves": 0.5,
    "volley": 0.7, "cannon": 0.8,
    "rifle": 0.65, "mg": 0.65, "shell": 0.8,
    "gun": 0.8, "broadside": 0.8, "splash": 0.45,
}


# ═════════════ 基础 ═════════════
def n_(sec):
    return max(1, int(round(sec * SR)))


def tvec(n):
    return np.arange(n) / SR


def rng_for(name):
    return np.random.default_rng([SEED, zlib.crc32(name.encode("utf-8"))])


def _sos(kind, f, order):
    if kind == "bandpass":
        lo, hi = f
        lo, hi = max(lo, 20.0), min(hi, NYQ * 0.95)
        return butter(order, [lo / NYQ, hi / NYQ], "bandpass", output="sos")
    f = min(max(f, 20.0), NYQ * 0.95)
    return butter(order, f / NYQ, kind, output="sos")


def lp(x, f, order=2):
    return sosfilt(_sos("lowpass", f, order), x)


def hp(x, f, order=2):
    return sosfilt(_sos("highpass", f, order), x)


def bp(x, lo, hi, order=2):
    return sosfilt(_sos("bandpass", (lo, hi), order), x)


def peak_filter(x, f, q):
    b, a = iirpeak(min(f, NYQ * 0.9), q, fs=SR)
    return lfilter(b, a, x)


def resonator(x, f, tau):
    """二阶共振器：被激励后以 tau 秒指数衰减地振荡(模态合成里的一个模)，输出按峰值归一。"""
    r = np.exp(-1.0 / (tau * SR))
    w = 2 * np.pi * min(f, NYQ * 0.95) / SR
    y = lfilter([1.0], [1.0, -2 * r * np.cos(w), r * r], x)
    m = np.max(np.abs(y))
    return y / m if m > 0 else y


def tv_filter(x, design, block=256):
    """时变滤波：按块换系数、状态接着往下走(参数缓变时听不出接缝)。design(秒) → sos，阶数要固定。"""
    y = np.zeros_like(x)
    zi = None
    for s in range(0, len(x), block):
        sos = design((s + block / 2) / SR)
        if zi is None:
            zi = np.zeros((sos.shape[0], 2))
        y[s:s + block], zi = sosfilt(sos, x[s:s + block], zi=zi)
    return y


def white(rng, n):
    return rng.standard_normal(n)


def pink(rng, n):
    spec = np.fft.rfft(rng.standard_normal(n))
    f = np.fft.rfftfreq(n, 1 / SR)
    f[0] = f[1]
    y = np.fft.irfft(spec / np.sqrt(f), n)
    return y / (np.std(y) + 1e-12)


def brown(rng, n):
    y = hp(np.cumsum(rng.standard_normal(n)), 18)
    return y / (np.std(y) + 1e-12)


def decay(n, tau, attack=0.001):
    """指数衰减包络，前面带一小段起音(避免硬咔哒)。"""
    e = np.exp(-tvec(n) / tau)
    a = min(n, n_(attack))
    if a > 1:
        e[:a] *= np.linspace(0, 1, a) ** 1.5
    return e


def bell(n, center, width):
    t = np.linspace(0, 1, n)
    return np.exp(-((t - center) / width) ** 2)


def place(dst, src, at, gain=1.0):
    i = int(round(at * SR))
    if i >= len(dst) or gain == 0:
        return
    if i < 0:
        src, i = src[-i:], 0
    m = min(len(src), len(dst) - i)
    if m > 0:
        dst[i:i + m] += gain * src[:m]


def norm_peak(x, peak=0.89):
    m = np.max(np.abs(x))
    return x * (peak / m) if m > 0 else x


def soft_limit(x, ceiling=0.95):
    return ceiling * np.tanh(x / ceiling)


def norm_rms(x, rms_db=-20.0, ceiling=0.95):
    rms = np.sqrt(np.mean(x ** 2))
    return soft_limit(x * (10 ** (rms_db / 20) / max(rms, 1e-12)), ceiling)


def taper(x, sec=0.04):
    """末尾淡出到零(事件撒到数组尽头时别硬切出咔哒声)。"""
    y = x.copy()
    f = min(len(y), n_(sec))
    y[-f:] *= np.linspace(1, 0, f) ** 2
    return y


def trim_tail(x, thresh_db=-40.0, fade_s=0.3):
    """切掉听不见的尾巴(按 20 ms 滑动最大值判)，末尾淡出。"""
    a = np.abs(x)
    w = n_(0.02)
    env = np.maximum.reduceat(a, np.arange(0, len(a), w))
    thr = env.max() * 10 ** (thresh_db / 20)
    live = np.nonzero(env > thr)[0]
    end = min(len(x), (live[-1] + 2) * w) if len(live) else len(x)
    y = x[:end].copy()
    f = min(n_(fade_s), end)
    y[-f:] *= np.linspace(1, 0, f) ** 2
    return y


def distant(x, d):
    """远近：d=0 就在跟前、1 很远——越远越闷越轻。"""
    fc = 9500 * (0.085 ** d)
    return lp(x, fc, 2) / (1 + 2.6 * d)


def room(rng, rt60, bright=5000.0, dark=900.0, early=6, er_span=0.07):
    """混响脉冲响应：指数衰减的噪声，前段亮后段暗，前面撒几道早期反射；按能量归一。"""
    L = n_(rt60 * 1.15)
    t = tvec(L)
    env = 10 ** (-3 * t / rt60)
    nz = rng.standard_normal(L)
    w = 1 - np.exp(-t / (rt60 * 0.16))
    ir = (lp(nz, bright) * (1 - w) + lp(nz, dark) * w * 1.7) * env
    onset = n_(0.006)
    ir[:onset] *= np.linspace(0, 1, onset)
    top = np.max(np.abs(ir))
    for _ in range(early):
        k = n_(rng.uniform(0.005, er_span))
        ir[k] += rng.uniform(1.5, 3.0) * top * rng.choice([-1.0, 1.0])
    return ir / np.sqrt(np.sum(ir ** 2))


def reverb(x, ir, mix, dry=1.0):
    x = taper(x)
    y = fftconvolve(x, ir) * mix
    y[:len(x)] += x * dry
    return y


def echoes(x, taps):
    """离散回声(山林、水面反射)：taps = [(延迟秒, 增益, 低通 Hz)]。"""
    x = taper(x)
    out = np.zeros(len(x) + n_(max(d for d, _, _ in taps)) + 1)
    out[:len(x)] += x
    for d, g, fc in taps:
        place(out, lp(x, fc), d, g)
    return out


def finish_oneshot(x, peak=0.89):
    x = hp(x, 25)
    return norm_peak(trim_tail(x), peak)


def make_loop(x, length, xfade):
    """x 长 length+xfade：把尾巴 xfade 与开头等功率交叉淡化，结果长 length、循环时无缝。"""
    L, X = n_(length), n_(xfade)
    y = x[:L].copy()
    u = np.linspace(0, 1, X)
    y[:X] = x[:X] * np.sin(u * np.pi / 2) + x[L:L + X] * np.cos(u * np.pi / 2)
    return y


# ═════════════ 金属、木头 ═════════════
METAL_RATIOS = np.array([1.0, 2.43, 3.95, 5.41, 6.82, 8.37, 9.9, 11.6])


def metal_ring(rng, exc, f0, ring, bright=0.85, modes=7, beat=0.55):
    """模态合成：激励信号推动一组非谐波共振器；每个模成对略失谐出拍频(金属的颤音)，高次模衰减快、更轻。"""
    y = np.zeros(len(exc))
    for k in range(modes):
        f = f0 * METAL_RATIOS[k] * rng.uniform(0.97, 1.03)
        if f > NYQ * 0.9:
            break
        tau = ring / (1 + 0.55 * k) * rng.uniform(0.75, 1.25)
        g = rng.uniform(0.55, 1.0) * bright ** k / (1 + 0.3 * k)
        y += g * resonator(exc, f, tau)
        if beat:
            y += beat * g * resonator(exc, f * (1 + rng.uniform(0.002, 0.007)), tau * 0.85)
    return y


def impulse_exc(rng, total, length=0.004, hp_hz=600):
    exc = np.zeros(n_(total))
    burst = hp(white(rng, n_(length)), hp_hz) * decay(n_(length), length / 3, 0.0002)
    exc[:len(burst)] = burst
    return exc


def blade_clash(rng, f0=None, ring=None):
    """两刃相击：两把刀剑各响各的(音高、余音不同)，接触处一声脆响，再带一点刃身的闷击。"""
    f0 = f0 or rng.uniform(950, 1900)
    ring = ring or rng.uniform(0.12, 0.32)
    total = ring * 4 + 0.05
    a = metal_ring(rng, impulse_exc(rng, total), f0, ring, bright=rng.uniform(0.78, 0.95))
    b = metal_ring(rng, impulse_exc(rng, total), f0 * rng.uniform(0.7, 1.35), ring * rng.uniform(0.5, 1.0))
    y = a / np.max(np.abs(a)) + 0.7 * np.roll(b / np.max(np.abs(b)), n_(rng.uniform(0.0, 0.004)))
    n = len(y)
    click = np.zeros(n)
    c = hp(white(rng, n_(0.012)), 2500) * decay(n_(0.012), 0.0025, 0.0002)
    click[:len(c)] = c
    thunk = np.zeros(n)
    th = bp(white(rng, n_(0.07)), 220, 800) * decay(n_(0.07), 0.014, 0.0008)
    thunk[:len(th)] = th
    return y * 0.55 + click * 0.9 + thunk * 0.9


def blade_scrape(rng, dur=None):
    """刃口相磨：抖动的高频摩擦噪声，同时激起刃身的鸣响("锵——")。"""
    dur = dur or rng.uniform(0.08, 0.2)
    total = dur + 0.5
    n = n_(total)
    exc = np.zeros(n)
    m = n_(dur)
    flutter = 1 + 0.6 * np.sin(2 * np.pi * rng.uniform(30, 80) * tvec(m)) * rng.uniform(0.5, 1.0)
    exc[:m] = bp(white(rng, m), 1800, 7000) * flutter * bell(m, 0.4, 0.35)
    ring = metal_ring(rng, exc, rng.uniform(1100, 2000), rng.uniform(0.15, 0.3), beat=0.4)
    return exc * 0.35 + ring / np.max(np.abs(ring)) * 0.5


def wood_knock(rng, size=1.0):
    """木盾、木杆的闷击：几只低频短共振器 + 一声干裂。"""
    total = 0.18
    exc = np.zeros(n_(total))
    e = lp(white(rng, n_(0.006)), 3000) * decay(n_(0.006), 0.002, 0.0002)
    exc[:len(e)] = e
    y = np.zeros(len(exc))
    for f, tau, g in ((rng.uniform(150, 230) / size, 0.045, 1.0), (rng.uniform(320, 480) / size, 0.03, 0.6),
                      (rng.uniform(700, 1100), 0.012, 0.35)):
        y += g * resonator(exc, f, tau * rng.uniform(0.8, 1.2))
    crack = np.zeros(len(exc))
    c = bp(white(rng, n_(0.01)), 1200, 4500) * decay(n_(0.01), 0.002, 0.0002)
    crack[:len(c)] = c
    return y + crack * 0.6


def shield_bash(rng):
    """盾击：木盾闷响 + 铜盾钉一点轻响。"""
    w = wood_knock(rng, size=rng.uniform(0.9, 1.2))
    boss = metal_ring(rng, impulse_exc(rng, 0.3), rng.uniform(450, 800), 0.07, modes=4)
    y = np.zeros(max(len(w), len(boss)))
    y[:len(w)] += w
    y[:len(boss)] += 0.25 * boss / np.max(np.abs(boss))
    return y


# ═════════════ 人声 ═════════════
VOWELS = {"a": (800, 1250, 2600, 3300), "o": (560, 940, 2500, 3200), "e": (620, 1750, 2550, 3350),
          "u": (420, 820, 2400, 3200), "ae": (740, 1650, 2500, 3300)}
FORMANT_GAIN = (1.0, 0.62, 0.3, 0.16)
FORMANT_BW = (95, 115, 170, 230)


def voice(rng, dur, f0, v1="u", v2="a", glide=0.2, effort=1.0):
    """一个人喊一声：声门脉冲(谐波叠加，带抖动与颤音) + 气声，过四个共振峰；元音 v1→v2 滑过去。"""
    n = n_(dur)
    t = tvec(n)
    rise = np.clip(t / (0.12 * dur), 0, 1)
    contour = (0.86 + 0.18 * rise) * (1 - 0.2 * np.clip((t - 0.3 * dur) / (0.7 * dur), 0, 1))
    jit = lp(white(rng, n), 7)
    jit = jit / (np.std(jit) + 1e-9) * 0.018
    vib = 0.012 * np.sin(2 * np.pi * rng.uniform(4.5, 7) * t + rng.uniform(0, 6.3))
    f = f0 * contour * (1 + jit + vib)
    ph = 2 * np.pi * np.cumsum(f) / SR + rng.uniform(0, 6.3)
    k_max = max(4, int(4600 / (f0 * 1.12)))
    tilt = 1.15 - 0.3 * effort
    src = np.zeros(n)
    for k in range(1, k_max + 1):
        src += np.sin(k * ph) / k ** tilt
    # 嗓子喊劈了的粗糙感：部分人带一点次谐波
    if rng.random() < 0.35:
        src += 0.25 * np.sin(0.5 * ph)
    asp = hp(white(rng, n), 1000) * (0.6 + 0.4 * np.sin(ph))
    src = src / (np.std(src) + 1e-9) + asp * rng.uniform(0.15, 0.3)
    scale = rng.uniform(0.92, 1.08)

    def formant(x, vowel):
        out = np.zeros(n)
        for fi, gi, bwi in zip(VOWELS[vowel], FORMANT_GAIN, FORMANT_BW):
            fi = fi * scale * (1.12 if fi < 1000 else 1.0) * (1 + 0.08 * effort)
            out += gi * peak_filter(x, fi, fi / bwi)
        return out

    a = formant(src, v1)
    b = formant(src, v2) if v2 != v1 else a
    w = np.clip((t - 0.04) / max(0.04, glide), 0, 1)
    y = a * (1 - w) + b * w
    att = min(n, n_(rng.uniform(0.04, 0.12)))
    rel = min(n, n_(rng.uniform(0.12, 0.3)))
    env = np.ones(n)
    env[:att] = np.linspace(0, 1, att) ** 1.5
    env[-rel:] *= np.linspace(1, 0, rel) ** 1.3
    env *= 1 + 0.12 * np.sin(2 * np.pi * rng.uniform(2, 5) * t)
    return y * env


def crowd(rng, length, voices, onset_span=None, dur_range=(0.4, 1.2), f0_range=(100, 240), effort=(0.6, 1.0),
          d_layers=((0.1, 0.25), (0.35, 0.3), (0.65, 0.45)), vowels=None, onset_shape=None):
    """一群人喊：每人随机音高、元音、起声时刻；按远近分三层，各层合起来再压暗(省得每人滤一次)。"""
    vowels = vowels or [("u", "a"), ("a", "a"), ("o", "a"), ("e", "a"), ("a", "o"), ("ae", "a"), ("u", "o")]
    total = np.zeros(n_(length))
    span = onset_span if onset_span is not None else length
    weights = np.array([w for _, w in d_layers])
    weights = weights / weights.sum()
    layers = [np.zeros(n_(length)) for _ in d_layers]
    for _ in range(voices):
        li = rng.choice(len(d_layers), p=weights)
        dur = rng.uniform(*dur_range)
        at = onset_shape(rng) if onset_shape else rng.uniform(-dur * 0.5, span)
        v1, v2 = vowels[rng.integers(len(vowels))]
        f0 = rng.uniform(*f0_range) if rng.random() > 0.15 else rng.uniform(170, 300)
        v = voice(rng, dur, f0, v1, v2, glide=rng.uniform(0.08, 0.3), effort=rng.uniform(*effort))
        place(layers[li], v, at, rng.uniform(0.5, 1.0))
    for (d, _), layer in zip(d_layers, layers):
        total += distant(layer, d)
    return total


# ═════════════ 弓箭 ═════════════
def bow_twang(rng):
    """弓弦回弹：Karplus-Strong 拨弦(低沉短促) + 一声弦拍弓臂的闷响。"""
    f = rng.uniform(85, 150)
    period = int(SR / f)
    n = n_(0.28)
    buf = list(rng.uniform(-1, 1, period))
    out = np.zeros(n)
    damp = rng.uniform(0.975, 0.99)
    for i in range(n):
        j = i % period
        nxt = buf[(j + 1) % period]
        out[i] = buf[j]
        buf[j] = damp * 0.5 * (buf[j] + nxt)
    out *= decay(n, 0.08, 0.001)
    slap = lp(white(rng, n_(0.03)), 1400) * decay(n_(0.03), 0.006, 0.0005)
    out[:len(slap)] += slap * 0.8
    return lp(out, 2500)


def arrow_whoosh(rng, dur=None):
    """一支箭飞过："嗖"——带通噪声，中心频率随多普勒从高滑到低，箭羽颤动调幅。"""
    dur = dur or rng.uniform(0.3, 0.7)
    n = n_(dur)
    f_hi, f_lo = rng.uniform(2600, 4300), rng.uniform(900, 1700)
    t_pass = rng.uniform(0.45, 0.65)

    def design(sec):
        u = sec / dur
        fc = f_lo + (f_hi - f_lo) / (1 + np.exp((u - t_pass) * 12))
        return _sos("bandpass", (fc * 0.8, fc * 1.25), 2)

    y = tv_filter(white(rng, n), design, block=128)
    flutter = 1 + 0.3 * np.sin(2 * np.pi * rng.uniform(25, 70) * tvec(n))
    return y * bell(n, t_pass, 0.22) * flutter


def arrow_impact(rng):
    """箭落：多半扎进木盾、地面，少数叮在甲上。"""
    r = rng.random()
    if r < 0.55:
        y = wood_knock(rng, size=rng.uniform(0.6, 0.9)) * 0.7
    elif r < 0.85:
        y = lp(white(rng, n_(0.08)), rng.uniform(300, 600)) * decay(n_(0.08), 0.012, 0.0008) * 1.3
    else:
        y = metal_ring(rng, impulse_exc(rng, 0.25, 0.002, 1500), rng.uniform(2200, 3600), rng.uniform(0.03, 0.07),
                       modes=4)
        y = y / np.max(np.abs(y)) * 0.35
    return y


def arrow_volley(rng, with_release=True, arrows=55):
    length = 3.0
    y = np.zeros(n_(length))
    t0 = 0.0
    if with_release:
        for _ in range(rng.integers(16, 32)):
            place(y, distant(bow_twang(rng), rng.uniform(0.15, 0.5)), rng.gamma(2.0, 0.07), rng.uniform(0.4, 0.8))
        t0 = 0.25
    for _ in range(arrows):
        d = rng.uniform(0.0, 0.7)
        place(y, distant(arrow_whoosh(rng), d), t0 + rng.gamma(2.5, 0.12), rng.uniform(0.3, 0.9))
    hit0 = t0 + rng.uniform(0.75, 0.95)
    for _ in range(rng.integers(30, 55)):
        place(y, distant(arrow_impact(rng), rng.uniform(0.1, 0.75)), hit0 + rng.gamma(2.0, 0.22), rng.uniform(0.3, 1.0))
    return reverb(y, room(rng, 1.3, 4500, 900), 0.28)


# ═════════════ 马蹄 ═════════════
def hoof_pool(rng, count=24):
    pool = []
    for _ in range(count):
        n = n_(0.14)
        thud = lp(white(rng, n), rng.uniform(220, 420), 2) * decay(n, rng.uniform(0.016, 0.03), 0.001) * 1.4
        clop = bp(white(rng, n), 700, 2400) * decay(n, 0.006, 0.0004) * rng.uniform(0.15, 0.55)
        exc = np.zeros(n)
        exc[0] = 1.0
        sub = resonator(exc, rng.uniform(50, 80), 0.035) * 0.5
        pool.append(thud + clop + sub)
    return pool


def cavalry(rng, length=3.2, horses=18, approach=True):
    """骑兵冲过：每匹马四拍跑步节奏(哒哒-哒哒)，相位、步幅各异；整体由远及近再远去，远时更闷。"""
    pool = hoof_pool(rng)
    y = np.zeros(n_(length))
    for _ in range(horses):
        d = rng.uniform(0.05, 0.6)
        period = rng.uniform(0.36, 0.45)
        offs = np.array([0.0, 0.065, 0.165, 0.215]) * (period / 0.4)
        s = -rng.uniform(0, period)
        g = rng.uniform(0.4, 1.0) / (1 + 2 * d)
        while s < length:
            for o in offs:
                place(y, pool[rng.integers(len(pool))], s + o + rng.normal(0, 0.006), g * rng.uniform(0.7, 1.0))
            s += period * rng.uniform(0.97, 1.03)
    n = len(y)
    env = bell(n, 0.55, 0.32) if approach else np.linspace(1, 0.2, n)
    dull = lp(y, 600)
    y = dull * (1 - env) + y * env
    y *= 0.25 + 0.75 * env
    y += lp(brown(rng, n), 90) * env * 0.35
    # 马具叮当
    for _ in range(rng.integers(6, 12)):
        jingle = metal_ring(rng, impulse_exc(rng, 0.08, 0.002, 2000), rng.uniform(2200, 3600), 0.012, modes=5)
        at = rng.uniform(0.1, length - 0.2)
        place(y, jingle / np.max(np.abs(jingle)), at, 0.035 * (0.3 + env[min(n - 1, n_(at))]))
    return reverb(y, room(rng, 1.0, 4000, 800), 0.2)


# ═════════════ 火器 ═════════════
def musket_shot(rng, d=0.0):
    """火绳枪/燧发枪一响：药池先"嘶"地一闪，随后爆裂(钝、闷、低频足)；越远越闷越轻。"""
    n = n_(0.65)
    y = np.zeros(n)
    pre = rng.uniform(0.025, 0.06)
    if d < 0.45:
        click = metal_ring(rng, impulse_exc(rng, 0.06, 0.002, 1500), rng.uniform(1800, 3000), 0.012, modes=3)
        place(y, click / np.max(np.abs(click)), 0.0, 0.08)
        m = n_(pre)
        fizz = hp(white(rng, m), 1800) * np.sin(np.linspace(0, np.pi, m)) ** 0.7
        place(y, fizz, 0.003, 0.07)
    m = n_(0.5)
    blast = white(rng, m) * decay(m, rng.uniform(0.04, 0.075), 0.0006)
    body = lp(blast, rng.uniform(1600, 2800)) * 0.9 + bp(blast, 110, 420) * 1.9
    thump = lp(white(rng, m), 120, 4) * decay(m, rng.uniform(0.05, 0.09), 0.002) * 3.2
    crack = hp(white(rng, n_(0.006)), 1300) * decay(n_(0.006), 0.0015, 0.0002) * 0.8
    place(y, body + thump, pre)
    place(y, crack, pre)
    return distant(y, d)


def volley(rng, shots=40, spread=0.45, d_range=(0.1, 0.6), stragglers=4, rt=1.8):
    """一排齐射：前排几乎同时、后面零零落落(伽马分布)，外加几声迟发；再加山林回声与旷野混响。"""
    pool = [(d, musket_shot(rng, d)) for d in np.linspace(d_range[0], d_range[1], 6) for _ in range(4)]
    length = spread * 2 + 2.6
    y = np.zeros(n_(length))
    onsets = list(np.clip(rng.gamma(2.2, spread / 4.4, shots), 0, spread * 1.8))
    onsets += list(rng.uniform(spread, spread * 2.2, stragglers))
    for at in onsets:
        _, shot = pool[rng.integers(len(pool))]
        place(y, shot, 0.05 + at, rng.uniform(0.55, 1.0))
    y = echoes(y, [(rng.uniform(0.32, 0.45), 0.2, 1300), (rng.uniform(0.62, 0.85), 0.11, 800)])
    return reverb(y, room(rng, rt, 4200, 750), 0.42)


def cannon_core(rng, size=1.0):
    """前装炮的炮口暴音：一声裂响 + 闷雷般的中低频爆 + 很沉的次低频冲击。"""
    n = n_(1.6)
    crack = np.zeros(n)
    c = hp(white(rng, n_(0.012)), 600) * decay(n_(0.012), 0.003, 0.0003)
    crack[:len(c)] = c
    blast = white(rng, n) * decay(n, 0.12 * size, 0.0012)
    body = lp(blast, rng.uniform(750, 1100)) + bp(blast, 55, 210) * 2.2
    sub = lp(white(rng, n), 55, 4) * decay(n, 0.26 * size, 0.004) * 7.0
    exc = np.zeros(n)
    exc[0] = 1.0
    boom = resonator(exc, rng.uniform(42, 62), 0.09 * size) * 0.45
    return crack * 0.5 + body + sub + boom


def cannon(rng, d=0.0, size=1.0, rt=2.8):
    y = distant(cannon_core(rng, size), d)
    y = echoes(y, [(rng.uniform(0.35, 0.55), 0.34, 700), (rng.uniform(0.8, 1.1), 0.22, 520),
                   (rng.uniform(1.3, 1.75), 0.12, 400)])
    return reverb(y, room(rng, rt, 2600, 480), 0.55)


# ═════════════ 近代 ═════════════
def rifle_shot(rng, d=0.0, crack=0.0):
    """步枪一响：子弹从近处掠过时先有超音速 N 波的"啪"，随后枪口暴音——比火绳枪干脆、短促。"""
    n = n_(0.4)
    y = np.zeros(n)
    lag = 0.0
    if crack > 0:
        m = int(rng.integers(8, 15))
        nw = hp(np.linspace(1, -1, m), 900)
        place(y, nw, 0.0, crack)
        lag = 0.02 + d * 0.12
    m = n_(0.3)
    blast = white(rng, m) * decay(m, rng.uniform(0.016, 0.03), 0.0004)
    body = lp(blast, rng.uniform(3800, 5500)) * 0.8 + bp(blast, 250, 1500) * 1.5
    thump = lp(white(rng, m), 150, 4) * decay(m, rng.uniform(0.025, 0.04), 0.0015) * 1.6
    place(y, body + thump, lag)
    return distant(y, d)


def fusillade(rng, length=2.6, near_shooters=3, far_shots=10):
    """步枪对射：几名近处射手各自拉栓射击(间隔一两秒)，远处零星枪声。"""
    y = np.zeros(n_(length + 1.5))
    for _ in range(near_shooters):
        d = rng.uniform(0.12, 0.45)
        t = rng.uniform(0, 0.8)
        while t < length:
            place(y, rifle_shot(rng, d, crack=0.25 if rng.random() < 0.35 else 0.0), t, rng.uniform(0.7, 1.0))
            t += rng.uniform(0.9, 1.8)
    for _ in range(far_shots):
        place(y, rifle_shot(rng, rng.uniform(0.55, 0.95)), rng.uniform(0, length), rng.uniform(0.5, 1.0))
    y = echoes(y, [(rng.uniform(0.18, 0.3), 0.25, 2500), (rng.uniform(0.45, 0.7), 0.13, 1500)])
    return reverb(y, room(rng, 1.5, 4500, 900), 0.32)


def mg_burst(rng, d, rounds, rate):
    """机枪一个点射：射速稳定、略有抖动，每发之间带一点机件的金属咔嗒。"""
    pool = [rifle_shot(rng, d) for _ in range(4)]
    clack = metal_ring(rng, impulse_exc(rng, 0.05, 0.002, 2000), rng.uniform(2400, 3400), 0.01, modes=3)
    clack = distant(clack / np.max(np.abs(clack)), d)
    period = 1.0 / rate
    y = np.zeros(n_(rounds * period + 0.6))
    for i in range(rounds):
        t = i * period + rng.normal(0, 0.003)
        place(y, pool[i % 4], max(0.0, t), rng.uniform(0.82, 1.0))
        place(y, clack, max(0.0, t + period * 0.5), 0.05)
    return y


def mg_sample(rng, bursts):
    """bursts = [(开始秒, 远近, 发数, 射速)]。"""
    end = max(at + rounds / rate for at, _, rounds, rate in bursts)
    y = np.zeros(n_(end + 1.5))
    for at, d, rounds, rate in bursts:
        place(y, mg_burst(rng, d, rounds, rate), at)
    y = echoes(y, [(rng.uniform(0.2, 0.32), 0.24, 2200), (rng.uniform(0.5, 0.75), 0.12, 1200)])
    return reverb(y, room(rng, 1.6, 4500, 850), 0.32)


def explosion(rng, d=0.0, size=1.0):
    """炮弹炸开：裂响、闷爆、很沉的次低频，随后碎石土块零星落下(越来越稀)、尘土沙沙。"""
    n = n_(2.4)
    y = np.zeros(n)
    c = hp(white(rng, n_(0.006)), 800) * decay(n_(0.006), 0.0015, 0.0002)
    place(y, c, 0.0, 0.7)
    blast = white(rng, n) * decay(n, 0.15 * size, 0.0008)
    y += lp(blast, rng.uniform(1500, 2200)) + bp(blast, 70, 300) * 1.9
    y += lp(white(rng, n), 60, 4) * decay(n, 0.42 * size, 0.003) * 8.0
    for _ in range(rng.integers(60, 120)):
        at = 0.06 + rng.exponential(0.45)
        if at > 2.2:
            continue
        click = hp(white(rng, n_(0.004)), 1500) * decay(n_(0.004), 0.001, 0.0001)
        place(y, lp(click, 3800), at, rng.uniform(0.05, 0.3) * np.exp(-at / 0.9))
    m = n_(2.0)
    dirt = hp(white(rng, m), 1800) * np.clip(tvec(m) / 0.15, 0, 1) * np.exp(-tvec(m) / 0.5) * 0.12
    place(y, taper(dirt, 0.6), 0.05)
    return distant(y, d)


def incoming(rng, dur=None):
    """炮弹来袭的尖啸：窄带噪声，中心频率由高往低滑，越来越响。"""
    dur = dur or rng.uniform(0.6, 0.9)
    n = n_(dur)
    f_hi, f_lo = rng.uniform(2000, 2600), rng.uniform(600, 850)

    def design(sec):
        fc = f_hi * (f_lo / f_hi) ** (sec / dur)
        return _sos("bandpass", (fc * 0.88, fc * 1.12), 2)

    y = tv_filter(white(rng, n), design, block=128)
    return y * (tvec(n) / dur) ** 2.2


def shelling(rng, events):
    """events = [(秒, 种类, 远近)]，种类：boom 炮声、burst 炸点、whistle 尖啸后炸开。"""
    y = np.zeros(n_(max(at for at, _, _ in events) + 3.5))
    for at, kind, d in events:
        if kind == "boom":
            place(y, distant(cannon_core(rng, 1.1), d), at)
        elif kind == "burst":
            place(y, explosion(rng, d), at)
        else:
            w = incoming(rng)
            place(y, distant(w, max(0.0, d - 0.2)), at, 0.35)
            place(y, explosion(rng, d), at + len(w) / SR)
    y = echoes(y, [(rng.uniform(0.35, 0.55), 0.3, 800), (rng.uniform(0.85, 1.2), 0.18, 550)])
    return reverb(y, room(rng, 3.0, 2800, 500), 0.5)


# ═════════════ 海战 ═════════════
WATER_TAPS = ((0.22, 0.45, 950), (0.47, 0.3, 750), (0.78, 0.2, 600), (1.15, 0.12, 480), (1.6, 0.07, 380))


def naval_gun(rng, d=0.6, size=1.25):
    """舰炮：更闷、更远的炮声，贴着水面一连串滚过去的回声。"""
    y = distant(cannon_core(rng, size), d)
    y = echoes(y, [(a * rng.uniform(0.85, 1.15), g, f) for a, g, f in WATER_TAPS])
    return y


def splash(rng, d=0.3, size=1.0):
    """弹着水柱：水被炸起的一声闷"嘭"，水花哗啦落下，夹着气泡咕嘟声。"""
    n = n_(2.2)
    y = np.zeros(n)
    hit = white(rng, n) * decay(n, 0.09 * size, 0.008)
    y += bp(hit, 120, 900) * 1.6 + bp(hit, 900, 3500) * 0.5
    m = n_(1.8)
    spray = lp(hp(white(rng, m), 1200), 5500) * np.clip(tvec(m) / 0.2, 0, 1) * np.exp(-tvec(m) / 0.32)
    place(y, taper(spray, 0.5), 0.08, 0.2)
    for _ in range(rng.integers(20, 40)):
        bl = n_(rng.uniform(0.01, 0.03))
        f = rng.uniform(300, 1100) * np.linspace(1, rng.uniform(1.3, 2.0), bl)
        bloop = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.sin(np.linspace(0, np.pi, bl))
        place(y, bloop, rng.uniform(0.15, 1.4), rng.uniform(0.03, 0.12))
    return distant(y, d)


def broadside(rng, guns=7, span=1.6, d_range=(0.45, 0.65)):
    y = np.zeros(n_(span + 4.0))
    for at in np.sort(rng.uniform(0, span, guns)):
        place(y, naval_gun(rng, rng.uniform(*d_range), rng.uniform(1.0, 1.3)), at, rng.uniform(0.6, 1.0))
    return reverb(y, room(rng, 3.6, 2200, 400), 0.55)


def sea(rng, n):
    """海浪：低沉的水体涌动随涌浪大起大落，浪头翻起时才有哗哗声与一点水沫嘶声，浪拍船帮的闷响。"""
    t = tvec(n)
    swell = (np.sin(2 * np.pi * t / 2.0 + rng.uniform(0, 6.3)) * 0.5 + np.sin(2 * np.pi * t / 2.667 + 1.3) * 0.3
             + np.sin(2 * np.pi * t / 4.0 + 0.4) * 0.2)
    env = 0.12 + 0.88 * np.clip(swell * 0.5 + 0.5, 0, 1) ** 2.2
    y = (lp(brown(rng, n), 450) * 0.9 + lp(pink(rng, n), 1000) * 0.5) * env
    y += bp(white(rng, n), 600, 2500) * env ** 3 * 0.22
    y += hp(white(rng, n), 3000) * env ** 5 * 0.06
    for _ in range(int(n / SR * 3)):
        m = n_(rng.uniform(0.1, 0.3))
        lap = lp(bp(white(rng, m), 160, 900), 900) * np.sin(np.linspace(0, np.pi, m)) ** 2
        place(y, lap, rng.uniform(0, n / SR), rng.uniform(0.5, 1.1))
    return y


def creak(rng, dur=None):
    """木船吱呀：缓变速率的脉冲串过几只木头共振器。"""
    dur = dur or rng.uniform(0.4, 0.9)
    n = n_(dur)
    rate = np.linspace(rng.uniform(25, 45), rng.uniform(45, 80), n)
    ph = np.cumsum(rate) / SR
    pulses = (np.diff(np.floor(ph), prepend=0) > 0).astype(float) * (1 + 0.3 * rng.standard_normal(n))
    y = sum(g * resonator(pulses, f, 0.02) for f, g in ((rng.uniform(320, 420), 1.0), (rng.uniform(700, 900), 0.6),
                                                       (rng.uniform(1300, 1700), 0.3)))
    return y * np.sin(np.linspace(0, np.pi, n)) ** 0.8


# ═════════════ 成品：单发 ═════════════
def build_oneshots():
    S = {}

    def put(name, fn):
        x = finish_oneshot(fn(rng_for(name)))
        cap = next((v for k, v in MAX_LEN.items() if name.startswith(k)), None)
        if cap and len(x) > n_(cap):
            x = x[:n_(cap)]
            f = n_(0.6)
            x[-f:] *= np.linspace(1, 0, f) ** 2
        S[name] = x

    # 冷兵器
    def clash_1(rng):
        y = np.zeros(n_(1.4))
        place(y, blade_clash(rng), 0.02)
        place(y, blade_clash(rng) * 0.5, rng.uniform(0.25, 0.4))
        return reverb(y, room(rng, 0.9, 5000, 1000), 0.18)

    def clash_2(rng):
        y = np.zeros(n_(1.8))
        t = 0.02
        for _ in range(rng.integers(3, 5)):
            place(y, blade_clash(rng), t, rng.uniform(0.6, 1.0))
            if rng.random() < 0.5:
                place(y, blade_scrape(rng), t + 0.03, 0.5)
            t += rng.uniform(0.12, 0.26)
        return reverb(y, room(rng, 0.9, 5000, 1000), 0.2)

    def clash_3(rng):
        y = np.zeros(n_(1.5))
        place(y, shield_bash(rng), 0.02, 1.0)
        place(y, blade_clash(rng), rng.uniform(0.15, 0.25), 0.8)
        place(y, shield_bash(rng), rng.uniform(0.35, 0.5), 0.7)
        return reverb(y, room(rng, 1.0, 4500, 900), 0.22)

    def clash_4(rng):
        y = np.zeros(n_(2.2))
        for _ in range(rng.integers(9, 14)):
            d = rng.uniform(0.1, 0.7)
            src = blade_clash(rng) if rng.random() < 0.65 else shield_bash(rng)
            place(y, distant(src, d), rng.uniform(0, 1.5), rng.uniform(0.5, 1.0))
        return reverb(y, room(rng, 1.2, 4500, 900), 0.26)

    for i, fn in enumerate((clash_1, clash_2, clash_3, clash_4), 1):
        put(f"cold_clash_{i}", fn)

    def warcry(rng, voices, f0_range):
        y = crowd(rng, 2.6, voices, dur_range=(0.7, 1.6), f0_range=f0_range, effort=(0.8, 1.0),
                  onset_shape=lambda r: r.gamma(2.0, 0.12))
        return reverb(y, room(rng, 1.4, 4200, 900), 0.32)

    put("cold_warcry_1", lambda r: warcry(r, 60, (105, 220)))
    put("cold_warcry_2", lambda r: warcry(r, 90, (95, 200)))
    put("cold_arrows_1", lambda r: arrow_volley(r, True, 55))
    put("cold_arrows_2", lambda r: arrow_volley(r, False, 70))
    put("cold_hooves_1", lambda r: cavalry(r, 3.2, 18, True))
    put("cold_hooves_2", lambda r: cavalry(r, 2.8, 26, False))

    # 火器
    put("fire_volley_1", lambda r: volley(r, 36, 0.35, (0.1, 0.5)))
    put("fire_volley_2", lambda r: volley(r, 50, 0.6, (0.2, 0.7)))
    put("fire_volley_3", lambda r: volley(r, 24, 0.25, (0.05, 0.35), stragglers=2))
    put("fire_volley_4", lambda r: volley(r, 60, 0.9, (0.35, 0.85), stragglers=8, rt=2.2))
    put("fire_cannon_1", lambda r: cannon(r, 0.15, 1.0))
    put("fire_cannon_2", lambda r: cannon(r, 0.55, 1.1, rt=3.2))

    def cannon_pair(rng):
        y = np.zeros(n_(5.0))
        place(y, cannon(rng, 0.3, 1.0), 0.0)
        place(y, cannon(rng, 0.45, 0.9), rng.uniform(0.5, 0.8), 0.8)
        return y

    put("fire_cannon_3", cannon_pair)

    # 近代
    put("modern_rifle_1", lambda r: fusillade(r, 2.6, 3, 10))
    put("modern_rifle_2", lambda r: fusillade(r, 2.4, 5, 6))
    put("modern_rifle_3", lambda r: fusillade(r, 2.8, 2, 18))
    put("modern_mg_1", lambda r: mg_sample(r, [(0.0, 0.25, 7, 9.5), (1.25, 0.25, 11, 9.5)]))
    put("modern_mg_2", lambda r: mg_sample(r, [(0.0, 0.4, 16, 11.0), (0.9, 0.75, 8, 8.0)]))
    put("modern_mg_3", lambda r: mg_sample(r, [(0.0, 0.35, 14, 18.0), (1.3, 0.35, 6, 18.0)]))
    put("modern_shell_1", lambda r: shelling(r, [(0.0, "whistle", 0.2)]))
    put("modern_shell_2", lambda r: shelling(r, [(0.0, "boom", 0.85), (1.2, "burst", 0.45)]))
    put("modern_shell_3", lambda r: shelling(r, [(0.0, "burst", 0.35), (0.55, "burst", 0.6)]))

    # 海战
    def gun_alone(rng, d, size):
        return reverb(naval_gun(rng, d, size), room(rng, 3.6, 2200, 400), 0.55)

    put("naval_gun_1", lambda r: gun_alone(r, 0.55, 1.3))

    def gun_ranging(rng):
        y = np.zeros(n_(5.5))
        place(y, naval_gun(rng, 0.75, 1.2), 0.0)
        place(y, splash(rng, 0.3), 1.4, 0.7)
        return reverb(y, room(rng, 3.6, 2200, 400), 0.5)

    put("naval_gun_2", gun_ranging)

    def gun_twin(rng):
        y = np.zeros(n_(5.0))
        place(y, naval_gun(rng, 0.6, 1.2), 0.0)
        place(y, naval_gun(rng, 0.62, 1.2), rng.uniform(0.2, 0.3), 0.85)
        return reverb(y, room(rng, 3.6, 2200, 400), 0.55)

    put("naval_gun_3", gun_twin)
    put("naval_broadside_1", lambda r: broadside(r, 7, 1.6, (0.45, 0.65)))

    def broadside_far(rng):
        y = broadside(rng, 10, 2.4, (0.65, 0.85))
        for _ in range(3):
            place(y, splash(rng, rng.uniform(0.5, 0.75)), rng.uniform(1.0, 3.0), 0.5)
        return y

    put("naval_broadside_2", broadside_far)
    put("naval_splash_1", lambda r: reverb(splash(r, 0.25, 1.1), room(r, 1.6, 3000, 600), 0.3))

    def splash_pair(rng):
        y = np.zeros(n_(3.5))
        place(y, splash(rng, 0.2, 1.2), 0.0)
        place(y, splash(rng, 0.55, 0.9), rng.uniform(0.4, 0.7), 0.8)
        return reverb(y, room(rng, 1.8, 3000, 600), 0.3)

    put("naval_splash_2", splash_pair)
    return S


# ═════════════ 成品：环境声 ═════════════
LOOP, XFADE, PREROLL = 8.0, 1.0, 2.5


def ambient(name, body):
    """在 PREROLL + LOOP + XFADE 的时间轴上撒事件，过混响，切掉开头预卷，再做成无缝循环。"""
    rng = rng_for(name)
    total = PREROLL + LOOP + XFADE + 1.0
    y = body(rng, total)
    y = y[n_(PREROLL):n_(PREROLL) + n_(LOOP + XFADE)]
    y = make_loop(hp(y, 25), LOOP, XFADE)
    return norm_rms(y, -20.0)


def amb_cold(rng, total):
    n = n_(total)
    y = crowd(rng, total, int(total * 13), dur_range=(0.25, 1.0), effort=(0.4, 0.9),
              d_layers=((0.3, 0.2), (0.55, 0.4), (0.8, 0.4))) * 0.9
    hits = np.zeros(n)
    for _ in range(int(total * 7)):
        d = rng.uniform(0.25, 0.9)
        src = blade_clash(rng) if rng.random() < 0.6 else shield_bash(rng)
        place(hits, distant(src, d), rng.uniform(0, total), rng.uniform(0.3, 0.8))
    y += hits * 0.55
    stomp = np.zeros(n)
    pool = hoof_pool(rng, 12)
    for _ in range(int(total * 14)):
        place(stomp, pool[rng.integers(len(pool))], rng.uniform(0, total), rng.uniform(0.2, 0.6))
    y += lp(stomp, 500) * 0.4 + lp(brown(rng, n), 140) * 0.35
    return reverb(y, room(rng, 1.6, 4000, 800), 0.35)[:n]


def amb_fire(rng, total):
    n = n_(total)
    y = crowd(rng, total, int(total * 6), dur_range=(0.3, 1.0), effort=(0.4, 0.8),
              d_layers=((0.55, 0.4), (0.85, 0.6))) * 0.6
    shots = np.zeros(n)
    pool = [musket_shot(rng, d) for d in np.linspace(0.5, 0.95, 5) for _ in range(4)]
    t = 0.0
    while t < total:
        if rng.random() < 0.12:
            for _ in range(rng.integers(5, 12)):
                place(shots, pool[rng.integers(len(pool))], t + rng.gamma(2.0, 0.08), rng.uniform(0.5, 1.0))
            t += rng.uniform(0.4, 0.8)
        else:
            place(shots, pool[rng.integers(len(pool))], t, rng.uniform(0.4, 1.0))
            t += rng.exponential(0.11)
    y += shots * 1.1
    for _ in range(max(2, int(total / 4))):
        place(y, distant(cannon_core(rng, 1.0), rng.uniform(0.75, 0.95)), rng.uniform(0, total), rng.uniform(0.5, 0.9))
    y += lp(brown(rng, n), 120) * 0.3
    y = echoes(y, [(0.42, 0.18, 900)])[:n]
    return reverb(y, room(rng, 2.2, 3500, 650), 0.45)[:n]


def amb_modern(rng, total):
    n = n_(total)
    y = np.zeros(n)
    pool = [rifle_shot(rng, d) for d in np.linspace(0.5, 0.95, 5) for _ in range(4)]
    t = 0.0
    while t < total:
        place(y, pool[rng.integers(len(pool))], t, rng.uniform(0.35, 1.0))
        t += rng.exponential(0.07)
    for _ in range(max(1, int(total / 5))):
        place(y, mg_burst(rng, rng.uniform(0.7, 0.9), int(rng.integers(6, 14)), rng.uniform(8, 12)),
              rng.uniform(0, total), 0.7)
    for _ in range(max(2, int(total / 3.5))):
        src = cannon_core(rng, 1.2) if rng.random() < 0.5 else explosion(rng, 0.0, 1.2)
        place(y, distant(src, rng.uniform(0.75, 0.95)), rng.uniform(0, total), rng.uniform(0.5, 0.9))
    tt = tvec(n)
    drone = sum(np.sin(2 * np.pi * f * tt * (1 + 0.004 * np.sin(2 * np.pi * 0.3 * tt))) / k
                for k, f in enumerate((38.0, 76.0, 114.0, 152.0), 1))
    y += lp(drone * (0.6 + 0.4 * np.sin(2 * np.pi * tt / 4.0)) + white(rng, n) * 0.3, 300) * 0.05
    y += lp(brown(rng, n), 110) * 0.3
    y = echoes(y, [(0.3, 0.2, 1400)])[:n]
    return reverb(y, room(rng, 2.0, 4000, 700), 0.4)[:n]


def amb_naval(rng, total):
    n = n_(total)
    y = sea(rng, n) * 1.2
    for _ in range(2):
        place(y, creak(rng), rng.uniform(0, total), 0.08)
    for _ in range(2):
        place(y, distant(splash(rng, 0.0, 0.8), rng.uniform(0.6, 0.8)), rng.uniform(0, total), 0.3)
    return reverb(y, room(rng, 1.8, 3000, 600), 0.25)[:n]


AMBIENTS = {"cold_amb": amb_cold, "fire_amb": amb_fire, "modern_amb": amb_modern, "naval_amb": amb_naval}


# ═════════════ 试听混音 ═════════════
# 各时代按游戏里的配比插单发(BattleSound.cs 的 Palette)：种类 → 权重
PREVIEW_PLAN = {
    "cold": ("cold_amb", {"clash": 5.0, "warcry": 1.2, "arrows": 1.6, "hooves": 1.2}, 1.5),
    "fire": ("fire_amb", {"volley": 5.0, "cannon": 2.0, "clash": 1.0, "warcry": 0.8}, 1.2),
    "modern": ("modern_amb", {"rifle": 4.0, "mg": 2.5, "shell": 2.2}, 1.5),
    "naval": ("naval_amb", {"gun": 4.0, "broadside": 2.0, "splash": 1.5}, 1.0),
}
PREVIEW_LEN = 14.0


def pitch(x, factor):
    """变速变调(与 FMOD setPitch 一样)：factor>1 更高更短。"""
    n = int(len(x) / factor)
    return np.interp(np.arange(n) * factor, np.arange(len(x)), x)


def build_preview(era, files):
    amb_name, palette, rate = PREVIEW_PLAN[era]
    rng = rng_for("preview_" + era)
    n = n_(PREVIEW_LEN)
    amb = files[amb_name]
    y = np.tile(amb, int(np.ceil(n / len(amb))) + 1)[:n] * GAIN["amb"]
    kinds = list(palette)
    weights = np.array([palette[k] for k in kinds])
    weights /= weights.sum()
    t, last = rng.uniform(0.3, 0.8), None
    while t < PREVIEW_LEN - 1.5:
        kind = kinds[rng.choice(len(kinds), p=weights)]
        prefix = ("cold" if kind in ("clash", "warcry", "arrows", "hooves") else
                  "fire" if kind in ("volley", "cannon") else
                  "modern" if kind in ("rifle", "mg", "shell") else "naval")
        names = [k for k in files if k.startswith(f"{prefix}_{kind}_") and k != last]
        name = names[rng.integers(len(names))]
        last = name
        shot = pitch(files[name], rng.uniform(0.94, 1.06))
        place(y, shot, t, GAIN[kind] * rng.uniform(0.75, 1.0))
        t += max(0.3, rng.exponential(1.0 / rate))
    f = n_(1.0)
    y[:n_(0.4)] *= np.linspace(0, 1, n_(0.4))
    y[-f:] *= np.linspace(1, 0, f)
    return norm_peak(y, 0.89)


# ═════════════ 输出 ═════════════
def save_rate(name):
    return next((r for k, r in SAVE_RATES if name.startswith(k)), SR)


def to_rate(x, rate, loop=False):
    """22050 → rate(只用到 16000)；返回值仍是浮点。循环的环境声首尾各接上对面一段再重采样，接缝处不起边缘效应。"""
    if rate == SR:
        return x
    if not loop:
        return resample_poly(x, 320, 441)
    pad = 4410
    y = resample_poly(np.concatenate([x[-pad:], x, x[:pad]]), 320, 441)
    return y[3200:3200 + len(x) * 320 // 441]


def from_rate(x, rate):
    return x if rate == SR else resample_poly(x, 441, 320)


def save_wav(path, x, rate=SR):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    pcm = np.clip(np.round(x * 32767), -32768, 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(rate)
        w.writeframes(pcm.tobytes())


def spectrogram(path, x, title):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    fig, ax = plt.subplots(2, 1, figsize=(12, 5), sharex=True)
    t = tvec(len(x))
    ax[0].plot(t, x, lw=0.3)
    ax[0].set_ylim(-1, 1)
    ax[0].set_title(title)
    ax[1].specgram(x, NFFT=1024, Fs=SR, noverlap=768, cmap="magma", vmin=-120)
    ax[1].set_ylim(0, NYQ)
    fig.tight_layout()
    fig.savefig(path, dpi=80)
    plt.close(fig)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="", help="只重做名字以此开头的文件(试听混音照常出)")
    ap.add_argument("--spectrogram", default="", help="把每个文件的波形与频谱图画到这个目录")
    args = ap.parse_args()

    files = build_oneshots()
    for name, body in AMBIENTS.items():
        files[name] = ambient(name, body)
    total = 0
    for name, x in sorted(files.items()):
        rate = save_rate(name)
        stored = to_rate(x, rate, loop=name.endswith("_amb"))
        # 试听混音用存盘后的版本(降了采样率的再升回来)，听到的就是游戏里的
        files[name] = from_rate(np.clip(np.round(stored * 32767), -32768, 32767) / 32767, rate)
        if args.only and not name.startswith(args.only):
            continue
        path = os.path.join(OUT, name + ".wav")
        save_wav(path, stored, rate)
        total += os.path.getsize(path)
        print(f"{name:22s} {len(x) / SR:5.2f}s {rate:5d}Hz  peak {np.max(np.abs(x)):.2f}  "
              f"rms {20 * np.log10(np.sqrt(np.mean(x ** 2)) + 1e-12):6.1f} dB")
    print(f"game sounds: {len(files)} files, {total / 1024:.0f} KiB")
    for era in PREVIEW_PLAN:
        y = build_preview(era, files)
        save_wav(os.path.join(PREVIEW, f"preview_{era}.wav"), y)
        if args.spectrogram:
            os.makedirs(args.spectrogram, exist_ok=True)
            spectrogram(os.path.join(args.spectrogram, f"preview_{era}.png"), y, f"preview_{era}")
    if args.spectrogram:
        for name, x in sorted(files.items()):
            spectrogram(os.path.join(args.spectrogram, name + ".png"), x, name)
    print("previews:", ", ".join(os.path.join(PREVIEW, f"preview_{e}.wav") for e in PREVIEW_PLAN))


if __name__ == "__main__":
    main()

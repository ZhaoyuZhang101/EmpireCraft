"""EmpireCraft 战声锻造器 —— 战场音效：冷兵器的刀剑与喊杀、火器的枪炮用 CC0 录音素材加工，其余纯代码合成。

时代(文件名前缀)：
  cold   冷兵器：刀剑相击、喊杀、环境声(录音素材)；箭雨、马蹄(合成)
  fire   火器：排枪齐射、前装炮、环境声(录音素材)
  modern 近代：步枪连射、机枪、炮击(合成)
  naval  海战：海浪、舰炮(闷远的炮声 + 水面回声)、弹着水柱(合成)

输出：
  GameResources/sounds/battle/<时代>_amb.wav          环境声，8 秒，首尾交叉淡化可无缝循环
  GameResources/sounds/battle/<时代>_<种类>_<n>.wav   单发变体
  Tools/SoundForge/preview/preview_<时代>.wav         每个时代 14 秒试听混音(环境声 + 按游戏里的配比随机插单发；不进游戏)

格式：16 位单声道 PCM；以中低频为主的(炮、舰炮、炸点、马蹄、火器与近代的环境声)存 16000 Hz，其余 22050 Hz，省体积。
游戏里的读法与配比见 Scripts/GeneralSystems/Warfare/BattleSound.cs。素材出处与授权见 Docs/CREDITS.md。

录音素材的加工(build_from_assets)：
  · 刀剑：十段相击各自裁静音、轻微变调做单响；挥砍带出相击做对砍；远近错落叠成混战；
  · 喊杀与环境声：一段人声氛围错开起点、轻微变调叠两三层(像更多人)，喊杀加起势与渐落，环境声里撒远处的相击；
  · 齐射：一段六响的黑火药录音按起振切成单发，错开叠成一排(前排几乎同时、后面零落)，按远近压暗；
  · 炮：原声、压低音高放远、两门先后开火；火器环境声是远处零星枪声 + 喊杀，切掉 140 Hz 以下。
合成手法(都在下面各函数里)：
  · 金属：噪声短促激励一组非谐波二阶共振器(模态合成)，用在箭落叮甲、马具、机枪机件；
  · 枪炮：枪口暴音 + 低频冲击，按远近低通、减弱，再加地形或水面回声与混响；
  · 远近：越远越闷越轻(低通 + 衰减)；混响 = 指数衰减噪声脉冲响应(前段亮、后段暗) + 几道早期反射。
随机数种子固定(按文件名派生)，同样的素材重跑结果逐字节一致。

用法：
  python -I Tools/SoundForge/battle_forge.py --clash 刀剑相击.zip --swing 挥砍.zip --crowd 人声.ogg       --powder 黑火药.wav --cannon 炮.ogg [--spectrogram 输出目录]
  不给素材时只重做合成的那些，素材做的沿用 GameResources 里现有的文件(试听混音照常出)。
  解码 ogg 要 soundfile(pip install --user soundfile)。
"""
import argparse
import io
import math
import os
import wave
import zipfile
import zlib

import numpy as np
from scipy.signal import butter, fftconvolve, lfilter, resample_poly, sosfilt

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
MAX_LEN = {"fire_cannon": 3.0, "naval_gun": 3.2, "naval_broadside": 4.2, "modern_shell": 3.4}

# 各种类的播放音量，与 BattleSound.cs 里的 KindGain 一致(试听混音按这个配比)
GAIN = {
    "amb": 0.5,
    "clash": 0.55, "warcry": 0.5, "arrows": 0.4, "hooves": 0.35,
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


def pitch(x, factor):
    """变速变调(与 FMOD setPitch 一样)：factor>1 更高更短。"""
    n = int(len(x) / factor)
    return np.interp(np.arange(n) * factor, np.arange(len(x)), x)


def make_loop(x, length, xfade):
    """x 长 length+xfade：把尾巴 xfade 与开头等功率交叉淡化，结果长 length、循环时无缝。"""
    L, X = n_(length), n_(xfade)
    y = x[:L].copy()
    u = np.linspace(0, 1, X)
    y[:X] = x[:X] * np.sin(u * np.pi / 2) + x[L:L + X] * np.cos(u * np.pi / 2)
    return y


# ═════════════ 金属、木头(箭落、马具、机件) ═════════════
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


# ═════════════ 炮声(近代炮击、舰炮共用) ═════════════
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


# ═════════════ 录音素材(冷兵器的刀剑与喊杀、火器的枪炮；CC0，出处见 Docs/CREDITS.md) ═════════════
# 下载来的文件当不可信数据：只用 soundfile 解码音频；zip 只在内存里读 .ogg/.wav 成员，不解压到磁盘、不执行任何东西。
# 命令行参数名 → 说明
ASSETS = (("clash", "刀剑相击 zip：StarNinjas《20 Sword Sound Effects》的 sword_clash.1~10.ogg"),
          ("swing", "挥砍 zip：同上的 sword.1~10.ogg"),
          ("crowd", "人声喊杀氛围：StarNinjas《Crowd Shouting/Speaking Ambience》crowd_shouting_0.ogg"),
          ("powder", "黑火药枪声：kurt《Gunshots》Black Powder.wav(一段六响)"),
          ("cannon", "炮声：Thimras《Cannon Fire》cannon_fire_0.ogg"))
MAX_MEMBER = 20 * 1024 * 1024
# 素材做成的单发按 50 ms 短时最大响度统一到这么多 dBFS(峰值另限 -1 dBFS)，与合成的那几类相当
ASSET_LOUDNESS = {"cold_clash": -13.0, "cold_warcry": -13.0, "fire_volley": -11.0, "fire_cannon": -9.0}


def _soundfile():
    """soundfile 装在用户 site-packages 里(python -I 不会自动带上)。"""
    import site
    import sys
    sys.path.append(site.getusersitepackages())
    import soundfile
    return soundfile


def _mono(data, rate):
    """解码结果 → 单声道、SR 采样率的浮点数组。"""
    x = np.asarray(data, dtype=np.float64)
    if x.ndim == 2:
        x = x.mean(axis=1)
    if int(rate) != SR:
        g = math.gcd(int(rate), SR)
        x = resample_poly(x, SR // g, int(rate) // g)
    return x


def read_audio(path):
    data, rate = _soundfile().read(path, always_2d=True)
    return _mono(data, rate)


def read_zip(path):
    """zip 里的 .ogg/.wav 成员按文件名里的序号排好，逐个在内存里解码。"""
    sf = _soundfile()
    found = []
    with zipfile.ZipFile(path) as z:
        for info in z.infolist():
            name = info.filename.replace("\\", "/").split("/")[-1]
            if info.is_dir() or not name.lower().endswith((".ogg", ".wav")) or info.file_size > MAX_MEMBER:
                continue
            data, rate = sf.read(io.BytesIO(z.read(info)), always_2d=True)
            digits = "".join(ch for ch in name if ch.isdigit())
            found.append((int(digits) if digits else 0, name, _mono(data, rate)))
    found.sort(key=lambda item: (item[0], item[1]))
    return [x for _, _, x in found]


def trim_head(x, thresh_db=-40.0, pre=0.004):
    """裁掉开头的静音(留几毫秒再淡入)。"""
    a = np.abs(x)
    i = int(np.argmax(a > a.max() * 10 ** (thresh_db / 20)))
    y = x[max(0, i - n_(pre)):].copy()
    f = min(len(y), n_(pre))
    y[:f] *= np.linspace(0, 1, f)
    return y


def short_term_max(x, win=0.05):
    w = n_(win)
    r = np.sqrt(np.convolve(x ** 2, np.ones(w) / w, "valid")) if len(x) > w else np.sqrt(np.mean(x ** 2))
    return float(np.max(r))


def finish_asset(name, x):
    """素材做成的单发：去直流、裁首尾静音(录音的余音自然，留到 -52 dB)，按短时最大响度统一，峰值不过 -1 dBFS。"""
    x = trim_tail(trim_head(hp(x, 25)), -52.0, 0.2)
    target = next((v for k, v in ASSET_LOUDNESS.items() if name.startswith(k)), -12.0)
    gain = min(10 ** (target / 20) / max(short_term_max(x), 1e-9), 0.89 / max(np.max(np.abs(x)), 1e-9))
    return cap_length(name, x * gain)


def split_shots(x, gap=0.25, thresh_db=-20.0):
    """一段多响的录音按起振切成单发：包络静下去 gap 秒以上再越过阈值算新的一响，切到下一响之前。"""
    w = n_(0.005)
    env = np.convolve(np.abs(x), np.ones(w) / w, "same")
    above = np.nonzero(env > env.max() * 10 ** (thresh_db / 20))[0]
    onsets, prev = [], -n_(10)
    for k in above:
        if k - prev > n_(gap):
            onsets.append(int(k))
        prev = k
    shots = []
    for i, start in enumerate(onsets):
        end = onsets[i + 1] - n_(0.01) if i + 1 < len(onsets) else len(x)
        shot = trim_tail(x[max(0, start - n_(0.003)):end], -45.0, 0.1)
        shots.append(norm_peak(shot, 1.0))
    return shots


def load_assets(args):
    A = {
        "clash": [norm_peak(trim_head(c), 1.0) for c in read_zip(args.clash)],
        "swing": [norm_peak(trim_head(c), 1.0) for c in read_zip(args.swing)],
        "crowd": read_audio(args.crowd),
        "shots": split_shots(read_audio(args.powder)),
        "cannon": norm_peak(trim_head(read_audio(args.cannon)), 1.0),
    }
    crowd = A["crowd"]
    A["crowd"] = crowd[n_(0.6):] / (np.max(np.abs(crowd)) + 1e-9)
    print(f"assets: {len(A['clash'])} clashes, {len(A['swing'])} swings, crowd {len(A['crowd']) / SR:.1f}s, "
          f"{len(A['shots'])} black-powder shots, cannon {len(A['cannon']) / SR:.1f}s")
    if len(A["clash"]) < 4 or len(A["swing"]) < 2 or len(A["shots"]) < 3 or len(A["crowd"]) < n_(22):
        raise SystemExit("素材不全：刀剑相击至少 4 段、挥砍 2 段、枪声至少 3 响、人声氛围至少 22 秒")
    return A


def crowd_layers(crowd, total, layers):
    """同一段人声错开起点、轻微变调叠几层，听着像更多人；layers = [(起点秒, 变调, 增益)]。"""
    y = np.zeros(n_(total))
    for start, factor, g in layers:
        seg = pitch(crowd[n_(start):n_(start + total * factor + 0.1)], factor)[:n_(total)]
        y[:len(seg)] += g * seg
    return y


def build_from_assets(A):
    """用录音素材做的：冷兵器的刀剑相击、喊杀、环境声，火器的齐射、炮声、环境声。"""
    S = {}
    clashes, swings, crowd, shots, cannon = A["clash"], A["swing"], A["crowd"], A["shots"], A["cannon"]

    def put(name, fn):
        S[name] = finish_asset(name, fn(rng_for(name)))

    def any_clash(rng, swing_chance=0.0):
        pool = swings if rng.random() < swing_chance else clashes
        return pitch(pool[rng.integers(len(pool))], rng.uniform(0.95, 1.05))

    # 刀剑：前六段是单独一响(挑不同的录音、轻微变调)，七八是一来一回的对砍(挥砍带出相击)，九十是远近错落的混战
    order = rng_for("cold_clash_order").permutation(len(clashes))
    for i in range(6):
        source = clashes[order[i % len(clashes)]]
        put(f"cold_clash_{i + 1}",
            lambda r, s=source: reverb(pitch(s, r.uniform(0.96, 1.04)), room(r, 0.8, 5000, 1000), 0.12))

    def duel(rng):
        y = np.zeros(n_(2.4))
        t = 0.0
        for _ in range(rng.integers(2, 4)):
            place(y, pitch(swings[rng.integers(len(swings))], rng.uniform(0.95, 1.05)), t, rng.uniform(0.5, 0.8))
            t += rng.uniform(0.12, 0.22)
            place(y, any_clash(rng), t, rng.uniform(0.75, 1.0))
            t += rng.uniform(0.25, 0.45)
        return reverb(y, room(rng, 0.9, 5000, 1000), 0.15)

    def melee(rng):
        y = np.zeros(n_(3.0))
        for _ in range(rng.integers(8, 13)):
            place(y, distant(any_clash(rng, 0.3), rng.uniform(0.05, 0.7)), rng.uniform(0, 1.8), rng.uniform(0.5, 1.0))
        return reverb(y, room(rng, 1.2, 4500, 900), 0.22)

    put("cold_clash_7", duel)
    put("cold_clash_8", duel)
    put("cold_clash_9", melee)
    put("cold_clash_10", melee)

    # 喊杀：人声氛围错开叠两层(一层略低沉)，起势、喊满、渐落，起势时声音由闷转亮
    def warcry(rng, layers):
        length = 3.0
        y = crowd_layers(crowd, length, layers)
        t = tvec(len(y))
        env = np.clip(0.3 + 0.7 * t / 0.5, 0, 1) * np.clip((length - t) / 1.1, 0, 1) ** 1.5
        y = lp(y, 1800) * (1 - env) + y * env
        return reverb(y * env, room(rng, 1.2, 4500, 900), 0.18)

    put("cold_warcry_1", lambda r: warcry(r, [(3.0, 1.0, 1.0), (15.5, 0.95, 0.8)]))
    put("cold_warcry_2", lambda r: warcry(r, [(8.5, 1.03, 1.0), (20.0, 0.97, 0.8)]))

    # 冷兵器环境声：人声氛围三层(大军)，略压暗，远处稀疏的刀剑相击
    def amb_cold(rng, total):
        y = lp(crowd_layers(crowd, total, [(0.0, 1.0, 1.0), (6.5, 0.96, 0.8), (13.5, 1.04, 0.65)]), 6000)
        hits = np.zeros(len(y))
        for _ in range(int(total * 1.2)):
            place(hits, distant(any_clash(rng, 0.25), rng.uniform(0.45, 0.85)), rng.uniform(0, total),
                  rng.uniform(0.25, 0.5))
        return reverb(y + hits * 0.6, room(rng, 1.4, 4000, 800), 0.2)[:len(y)]

    S["cold_amb"] = ambient("cold_amb", amb_cold)

    # 齐射：六响黑火药单发错开叠起来，前排几乎同时、后面零零落落(伽马分布)，外加几声迟发；按远近压暗、轻微变调
    def volley(rng, count, spread, d_range, stragglers=4):
        y = np.zeros(n_(spread * 2.2 + 1.6))
        onsets = list(np.clip(rng.gamma(2.2, spread / 4.4, count), 0, spread * 1.8))
        onsets += list(rng.uniform(spread, spread * 2.2, stragglers))
        for at in onsets:
            shot = pitch(shots[rng.integers(len(shots))], rng.uniform(0.93, 1.07))
            place(y, distant(shot, rng.uniform(*d_range)), 0.02 + at, rng.uniform(0.55, 1.0))
        return reverb(y, room(rng, 1.4, 4500, 900), 0.18)

    put("fire_volley_1", lambda r: volley(r, 36, 0.35, (0.1, 0.5)))
    put("fire_volley_2", lambda r: volley(r, 50, 0.6, (0.2, 0.7)))
    put("fire_volley_3", lambda r: volley(r, 24, 0.25, (0.05, 0.35), stragglers=2))
    put("fire_volley_4", lambda r: volley(r, 60, 0.9, (0.35, 0.85), stragglers=8))

    # 炮：原声近处一门；压低音高放远(闷而长)；两门先后开火
    put("fire_cannon_1", lambda r: reverb(cannon, room(r, 2.0, 3000, 600), 0.1))
    put("fire_cannon_2", lambda r: reverb(distant(pitch(cannon, 0.86), 0.45), room(r, 2.8, 2500, 500), 0.35))

    def cannon_pair(rng):
        y = np.zeros(n_(4.2))
        place(y, distant(cannon, 0.15), 0.0)
        place(y, distant(pitch(cannon, 1.07), 0.35), rng.uniform(0.5, 0.7), 0.8)
        return reverb(y, room(rng, 2.4, 2800, 550), 0.2)

    put("fire_cannon_3", cannon_pair)

    # 火器环境声：远处零星的枪声(偶尔一小排)，加远处的喊杀；全程切掉 140 Hz 以下，不要成排的低频闷响
    def amb_fire(rng, total):
        n = n_(total)
        y = lp(crowd_layers(crowd, total, [(4.0, 0.98, 1.0), (14.0, 1.02, 0.7)]), 2600) * 0.4
        fire = np.zeros(n)
        t = rng.uniform(0, 0.3)
        while t < total:
            if rng.random() < 0.15:
                for _ in range(rng.integers(3, 7)):
                    shot = pitch(shots[rng.integers(len(shots))], rng.uniform(0.92, 1.08))
                    place(fire, distant(shot, rng.uniform(0.5, 0.85)), t + rng.gamma(2.0, 0.08), rng.uniform(0.4, 0.9))
                t += rng.uniform(0.6, 1.1)
            else:
                shot = pitch(shots[rng.integers(len(shots))], rng.uniform(0.92, 1.08))
                place(fire, distant(shot, rng.uniform(0.45, 0.9)), t, rng.uniform(0.35, 1.0))
                t += rng.exponential(0.4)
        y = hp(y + fire * 2.4, 140)
        return reverb(y, room(rng, 1.8, 3500, 700), 0.22)[:n]

    S["fire_amb"] = ambient("fire_amb", amb_fire)
    return S


# ═════════════ 成品：单发 ═════════════
def cap_length(name, x):
    cap = next((v for k, v in MAX_LEN.items() if name.startswith(k)), None)
    if cap and len(x) > n_(cap):
        x = x[:n_(cap)]
        f = n_(0.6)
        x[-f:] *= np.linspace(1, 0, f) ** 2
    return x


def build_oneshots():
    """合成的单发：箭雨、马蹄、近代、海战。"""
    S = {}

    def put(name, fn):
        S[name] = cap_length(name, finish_oneshot(fn(rng_for(name))))

    # 冷兵器：箭雨、马蹄(刀剑、喊杀换成了录音素材，见 build_from_assets)
    put("cold_arrows_1", lambda r: arrow_volley(r, True, 55))
    put("cold_arrows_2", lambda r: arrow_volley(r, False, 70))
    put("cold_hooves_1", lambda r: cavalry(r, 3.2, 18, True))
    put("cold_hooves_2", lambda r: cavalry(r, 2.8, 26, False))

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


# 合成的环境声(冷兵器、火器的换成了录音素材，见 build_from_assets)
AMBIENTS = {"modern_amb": amb_modern, "naval_amb": amb_naval}


# ═════════════ 试听混音 ═════════════
# 各时代按游戏里的配比插单发(BattleSound.cs 的 Palette)：种类 → 权重
PREVIEW_PLAN = {
    "cold": ("cold_amb", {"clash": 5.0, "warcry": 1.2, "arrows": 1.6, "hooves": 1.2}, 1.5),
    "fire": ("fire_amb", {"volley": 5.0, "cannon": 2.0, "clash": 1.0, "warcry": 0.8}, 1.2),
    "modern": ("modern_amb", {"rifle": 4.0, "mg": 2.5, "shell": 2.2}, 1.5),
    "naval": ("naval_amb", {"gun": 4.0, "broadside": 2.0, "splash": 1.5}, 1.0),
}
PREVIEW_LEN = 14.0


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
        # 按序号排(素材做的是读盘还是现做，挑中的都一样)
        names = sorted((k for k in files if k.startswith(f"{prefix}_{kind}_") and k != last),
                       key=lambda k: int(k.rsplit("_", 1)[1]))
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


def read_wav(path):
    """读现有的游戏音频(16 位单声道)，升回 SR。"""
    with wave.open(path, "rb") as w:
        rate = w.getframerate()
        x = np.frombuffer(w.readframes(w.getnframes()), "<i2") / 32767
    return from_rate(x, rate)


def main():
    ap = argparse.ArgumentParser()
    for key, text in ASSETS:
        ap.add_argument("--" + key, default="", help=text)
    ap.add_argument("--spectrogram", default="", help="把每个文件的波形与频谱图画到这个目录")
    args = ap.parse_args()
    given = [key for key, _ in ASSETS if getattr(args, key)]
    if 0 < len(given) < len(ASSETS):
        ap.error("五个素材要一起给(或者都不给，只重做合成的)")

    files = build_oneshots()
    for name, body in AMBIENTS.items():
        files[name] = ambient(name, body)
    if given:
        files.update(build_from_assets(load_assets(args)))
    made = set(files)
    total = 0
    for name, x in sorted(files.items()):
        rate = save_rate(name)
        stored = to_rate(x, rate, loop=name.endswith("_amb"))
        # 试听混音用存盘后的版本(降了采样率的再升回来)，听到的就是游戏里的
        files[name] = from_rate(np.clip(np.round(stored * 32767), -32768, 32767) / 32767, rate)
        path = os.path.join(OUT, name + ".wav")
        save_wav(path, stored, rate)
        total += os.path.getsize(path)
        print(f"{name:22s} {len(x) / SR:5.2f}s {rate:5d}Hz  peak {np.max(np.abs(x)):.2f}  "
              f"st-max {20 * np.log10(short_term_max(x) + 1e-12):6.1f} dB")
    print(f"written: {len(made)} files, {total / 1024:.0f} KiB")
    for f in sorted(os.listdir(OUT)):
        name, ext = os.path.splitext(f)
        if ext.lower() != ".wav" or name in made:
            continue
        if given:
            # 全部重做时，不再产出的旧文件(被替换掉的合成版、变体数变少了的)删掉
            os.remove(os.path.join(OUT, f))
            print("removed stale", f)
        else:
            files[name] = read_wav(os.path.join(OUT, f))
    sizes = sum(os.path.getsize(os.path.join(OUT, f)) for f in os.listdir(OUT) if f.lower().endswith(".wav"))
    print(f"game sounds now: {sum(1 for f in os.listdir(OUT) if f.lower().endswith('.wav'))} files, {sizes / 1024:.0f} KiB")
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

using System;
using System.Collections.Generic;
using System.Text;

namespace EmpireCraft.Scripts.Terminal;

// 终端看到的不是真账，是"统治者拿得到的消息"(用户 2026-11-11 "手机终端模拟的是现实统治者的这个身份")。三样各管一头：
//   · 眼界(科技)：古代只知道接壤的国；有了商路、航海，使节到得了的也知道，再远的只有传闻；有了电报，天下都看得见、消息不再迟；
//   · 粗细(政令)："皇权也不下乡"——行政控制力低的城只有约数，再低的不详；
//   · 真假(吏治)："越廉洁信息越透明"——腐败越重，数字虚报越多，奏报越迟，小事干脆报不上来。
// 只改送到终端的那一份，游戏里的数不动。这里全是算术，不碰游戏和 Unity(好拿出来单测)；同一个数同一年里报的不变(按种子算)
public static class TerminalIntel
{
    public enum Sight { Borders = 0, Reach = 1, World = 2 }

    // 铁器及以前只知接壤(时代档见 Technology/TechTree.json 的 eras：5 = 中世纪)
    public const int ReachEraTier = 5;
    // 地图格的记号(国的下标用 0~249)
    public const byte Wild = 252, Water = 253, Fog = 254;
    public const int MaxRealms = 250;
    // 虚报至多四成；腐败低于这个数的当真账
    private const double MaxError = 0.4d, HonestBelow = 0.08d;
    public const float ClearControl = 0.6f, RoughControl = 0.3f;

    public static Sight SightOf(int eraTier, bool telegraph) =>
        telegraph ? Sight.World : eraTier >= ReachEraTier ? Sight.Reach : Sight.Borders;

    public static uint Hash(long a, long b, long c)
    {
        unchecked
        {
            ulong h = 1469598103934665603UL;
            foreach (long value in new[] { a, b, c })
            {
                ulong v = (ulong)value;
                for (int i = 0; i < 8; i++)
                {
                    h ^= v & 0xFF;
                    h *= 1099511628211UL;
                    v >>= 8;
                }
            }
            return (uint)(h ^ (h >> 32));
        }
    }

    public static double Unit(uint hash) => hash / 4294967296d;

    // 奏报上的数。bias：+1 往多里报(库银、兵额)，-1 往少里报(丁口，隐户)，0 两头都可能。vague 为真的是约数
    public static long Reported(long truth, double corruption, int bias, uint seed, out bool vague)
    {
        corruption = Math.Max(0d, Math.Min(1d, corruption));
        vague = corruption >= HonestBelow && truth != 0;
        if (!vague) return truth;
        double error = corruption * MaxError, roll = Unit(seed);
        double delta = bias > 0 ? roll * error : bias < 0 ? -roll * error : (roll * 2d - 1d) * error;
        return RoundSig((long)Math.Round(truth * (1d + delta)), 2);
    }

    // 留几位有效数字
    public static long RoundSig(long value, int digits)
    {
        if (value == 0) return 0;
        long sign = value < 0 ? -1 : 1, abs = Math.Abs(value), scale = 1;
        while (abs / scale >= Pow10(digits)) scale *= 10;
        return sign * (long)Math.Round(abs / (double)scale) * scale;
    }

    private static long Pow10(int digits)
    {
        long value = 1;
        for (int i = 0; i < digits; i++) value *= 10;
        return value;
    }

    // 政令到得了几分：0 清楚、1 只有约数、2 不详
    public static int Clarity(float control) => control >= ClearControl ? 0 : control >= RoughControl ? 1 : 2;

    // 吏治的可信：0 可信、1 参差、2 不足信
    public static int Trust(double corruption) => corruption < 0.2d ? 0 : corruption < 0.5d ? 1 : 2;

    // 这件事迟几个月才报到。own 本国的事；major 军国大事(加急)
    public static int DelayMonths(double corruption, bool own, bool major, Sight sight, uint seed)
    {
        if (sight == Sight.World) return 0;
        corruption = Math.Max(0d, Math.Min(1d, corruption));
        double months = (own ? 1d : 3d) + corruption * 8d * (0.5d + Unit(seed));
        if (major) months *= 0.5d;
        return (int)Math.Round(months);
    }

    // 小事报不上来
    public static bool Lost(double corruption, bool major, Sight sight, uint seed) =>
        !major && sight != Sight.World && corruption > 0.3d && Unit(seed ^ 0x9E3779B9u) < (corruption - 0.3d) * 0.6d;

    // 迷雾：seed 为真的格(本国、已知之国)四周 radius 格以内看得见，其余盖成 Fog；keep 为真的格(传闻之国)不盖
    public static void ApplyFog(byte[] cells, bool[] seed, bool[] keep, int width, int height, int radius)
    {
        var distance = new int[cells.Length];
        var queue = new Queue<int>();
        for (int i = 0; i < cells.Length; i++)
        {
            distance[i] = seed[i] ? 0 : -1;
            if (seed[i]) queue.Enqueue(i);
        }
        while (queue.Count > 0)
        {
            int at = queue.Dequeue(), d = distance[at];
            if (d >= radius) continue;
            int x = at % width, y = at / width;
            if (x > 0) Visit(at - 1);
            if (x + 1 < width) Visit(at + 1);
            if (y > 0) Visit(at - width);
            if (y + 1 < height) Visit(at + width);

            void Visit(int next)
            {
                if (distance[next] >= 0) return;
                distance[next] = d + 1;
                queue.Enqueue(next);
            }
        }
        for (int i = 0; i < cells.Length; i++)
            if (distance[i] < 0 && !keep[i]) cells[i] = Fog;
    }

    // 行程压缩：(值, 连着几格) 一对一对，一段至多 255 格
    public static byte[] Rle(byte[] cells)
    {
        var packed = new List<byte>(cells.Length / 4 + 16);
        int i = 0;
        while (i < cells.Length)
        {
            byte value = cells[i];
            int run = 1;
            while (i + run < cells.Length && run < 255 && cells[i + run] == value) run++;
            packed.Add(value);
            packed.Add((byte)run);
            i += run;
        }
        return packed.ToArray();
    }

    // 去掉游戏文字里的 <color=...>、<b> 这类标记
    public static string Strip(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('<') < 0) return text ?? "";
        var plain = new StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '<')
            {
                int close = text.IndexOf('>', i + 1);
                if (close > i && close - i <= 40 && LooksLikeTag(text, i + 1, close))
                {
                    i = close + 1;
                    continue;
                }
            }
            plain.Append(c);
            i++;
        }
        return plain.ToString();
    }

    private static bool LooksLikeTag(string text, int from, int to)
    {
        if (from >= to) return false;
        int at = text[from] == '/' ? from + 1 : from;
        if (at >= to || !char.IsLetter(text[at])) return false;
        string name = text.Substring(at, to - at);
        if (name.IndexOf('<') >= 0) return false;
        int cut = name.IndexOfAny(new[] { '=', ' ' });
        if (cut < 0) return name is "color" or "b" or "i" or "size" or "material" or "quad";
        // 带参数的只有这几种(<b 1> 这样的是正文)
        name = name.Substring(0, cut);
        return name is "color" or "size" or "material" or "quad";
    }
}

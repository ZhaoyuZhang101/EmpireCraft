using System;
using System.Collections.Generic;
using System.Text;

namespace EmpireCraft.Scripts.Terminal;

// 二维码(用户 2026-11-11 "开启后倒是告诉我手机怎么连啊，不行给我生成个二维码我扫描也成")：把终端的地址连同配对码编成码，
// 手机一扫就进。不引库，自己编：字节模式、纠错 L 级、版本 1~5(都只有一个纠错块，至多 106 个字节，一条地址绰绰有余)。
// 掩码八种各试一遍取罚分最低的(连排、成块、黑白失衡三项；像定位图案的那一项没算，不影响能不能扫)。不碰游戏和 Unity
public static class TerminalQr
{
    // 版本 1~5 在 L 级的数据码字数、纠错码字数
    private static readonly int[] DataWords = { 19, 34, 55, 80, 108 }, EccWords = { 7, 10, 15, 20, 26 };

    // [行, 列]，真为黑；行 0 在上。太长编不下返回 null
    public static bool[,] Encode(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text ?? "");
        int version = 0;
        for (int v = 1; v <= DataWords.Length && version == 0; v++)
            if (bytes.Length * 8 + 12 <= DataWords[v - 1] * 8) version = v;
        if (version == 0) return null;
        int dataCount = DataWords[version - 1], capacity = dataCount * 8;

        var bits = new List<bool>(capacity);
        Append(bits, 4, 4);
        Append(bits, bytes.Length, 8);
        foreach (byte value in bytes) Append(bits, value, 8);
        Append(bits, 0, Math.Min(4, capacity - bits.Count));
        while (bits.Count % 8 != 0) bits.Add(false);
        for (int pad = 0xEC; bits.Count < capacity; pad ^= 0xEC ^ 0x11) Append(bits, pad, 8);
        var words = new byte[dataCount + EccWords[version - 1]];
        for (int i = 0; i < bits.Count; i++)
            if (bits[i]) words[i >> 3] |= (byte)(0x80 >> (i & 7));
        byte[] data = new byte[dataCount];
        Array.Copy(words, data, dataCount);
        byte[] ecc = Remainder(data, Divisor(EccWords[version - 1]));
        Array.Copy(ecc, 0, words, dataCount, ecc.Length);

        int size = 17 + 4 * version;
        var modules = new bool[size, size];
        var fixedCells = new bool[size, size];
        for (int i = 0; i < size; i++)
        {
            Fix(modules, fixedCells, 6, i, i % 2 == 0);
            Fix(modules, fixedCells, i, 6, i % 2 == 0);
        }
        Finder(modules, fixedCells, size, 3, 3);
        Finder(modules, fixedCells, size, size - 4, 3);
        Finder(modules, fixedCells, size, 3, size - 4);
        if (version >= 2)
            for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
                Fix(modules, fixedCells, size - 7 + dx, size - 7 + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
        Format(modules, fixedCells, size, 0);

        // 数据：从右下起两列一组，上下来回走
        int at = 0;
        for (int right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (int vert = 0; vert < size; vert++)
            for (int j = 0; j < 2; j++)
            {
                int x = right - j;
                bool upward = ((right + 1) & 2) == 0;
                int y = upward ? size - 1 - vert : vert;
                if (fixedCells[y, x] || at >= words.Length * 8) continue;
                modules[y, x] = ((words[at >> 3] >> (7 - (at & 7))) & 1) != 0;
                at++;
            }
        }

        int best = 0, bestPenalty = int.MaxValue;
        for (int mask = 0; mask < 8; mask++)
        {
            Mask(modules, fixedCells, size, mask);
            Format(modules, fixedCells, size, mask);
            int penalty = Penalty(modules, size);
            if (penalty < bestPenalty)
            {
                bestPenalty = penalty;
                best = mask;
            }
            Mask(modules, fixedCells, size, mask);
        }
        Mask(modules, fixedCells, size, best);
        Format(modules, fixedCells, size, best);
        return modules;
    }

    private static void Append(List<bool> bits, int value, int length)
    {
        for (int i = length - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0);
    }

    private static void Fix(bool[,] modules, bool[,] fixedCells, int x, int y, bool dark)
    {
        modules[y, x] = dark;
        fixedCells[y, x] = true;
    }

    private static void Finder(bool[,] modules, bool[,] fixedCells, int size, int cx, int cy)
    {
        for (int dy = -4; dy <= 4; dy++)
        for (int dx = -4; dx <= 4; dx++)
        {
            int x = cx + dx, y = cy + dy, ring = Math.Max(Math.Abs(dx), Math.Abs(dy));
            if (x >= 0 && x < size && y >= 0 && y < size) Fix(modules, fixedCells, x, y, ring != 2 && ring != 4);
        }
    }

    // 格式信息(纠错级别 L + 掩码号，十五位，两处各放一份)，外加左下那一格恒黑
    private static void Format(bool[,] modules, bool[,] fixedCells, int size, int mask)
    {
        int data = (1 << 3) | mask, rem = data;
        for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        int bits = ((data << 10) | rem) ^ 0x5412;
        bool Bit(int i) => ((bits >> i) & 1) != 0;
        for (int i = 0; i <= 5; i++) Fix(modules, fixedCells, 8, i, Bit(i));
        Fix(modules, fixedCells, 8, 7, Bit(6));
        Fix(modules, fixedCells, 8, 8, Bit(7));
        Fix(modules, fixedCells, 7, 8, Bit(8));
        for (int i = 9; i < 15; i++) Fix(modules, fixedCells, 14 - i, 8, Bit(i));
        for (int i = 0; i < 8; i++) Fix(modules, fixedCells, size - 1 - i, 8, Bit(i));
        for (int i = 8; i < 15; i++) Fix(modules, fixedCells, 8, size - 15 + i, Bit(i));
        Fix(modules, fixedCells, 8, size - 8, true);
    }

    private static void Mask(bool[,] modules, bool[,] fixedCells, int size, int mask)
    {
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            if (fixedCells[y, x]) continue;
            bool flip = mask switch
            {
                0 => (x + y) % 2 == 0,
                1 => y % 2 == 0,
                2 => x % 3 == 0,
                3 => (x + y) % 3 == 0,
                4 => (x / 3 + y / 2) % 2 == 0,
                5 => x * y % 2 + x * y % 3 == 0,
                6 => (x * y % 2 + x * y % 3) % 2 == 0,
                _ => ((x + y) % 2 + x * y % 3) % 2 == 0
            };
            if (flip) modules[y, x] = !modules[y, x];
        }
    }

    private static int Penalty(bool[,] modules, int size)
    {
        int penalty = 0, dark = 0;
        for (int a = 0; a < size; a++)
        {
            int runRow = 0, runColumn = 0;
            bool lastRow = false, lastColumn = false;
            for (int b = 0; b < size; b++)
            {
                bool row = modules[a, b], column = modules[b, a];
                if (row) dark++;
                runRow = b > 0 && row == lastRow ? runRow + 1 : 1;
                runColumn = b > 0 && column == lastColumn ? runColumn + 1 : 1;
                if (runRow == 5) penalty += 3;
                else if (runRow > 5) penalty++;
                if (runColumn == 5) penalty += 3;
                else if (runColumn > 5) penalty++;
                lastRow = row;
                lastColumn = column;
            }
        }
        for (int y = 0; y + 1 < size; y++)
        for (int x = 0; x + 1 < size; x++)
        {
            bool colour = modules[y, x];
            if (colour == modules[y, x + 1] && colour == modules[y + 1, x] && colour == modules[y + 1, x + 1]) penalty += 3;
        }
        int total = size * size;
        int off = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
        return penalty + Math.Max(0, off) * 10;
    }

    // 里德-所罗门纠错：GF(2^8)，本原多项式 0x11D
    private static byte[] Divisor(int degree)
    {
        var result = new byte[degree];
        result[degree - 1] = 1;
        int root = 1;
        for (int i = 0; i < degree; i++)
        {
            for (int j = 0; j < degree; j++)
            {
                result[j] = (byte)Multiply(result[j], root);
                if (j + 1 < degree) result[j] ^= result[j + 1];
            }
            root = Multiply(root, 2);
        }
        return result;
    }

    private static byte[] Remainder(byte[] data, byte[] divisor)
    {
        var result = new byte[divisor.Length];
        foreach (byte value in data)
        {
            int factor = value ^ result[0];
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[result.Length - 1] = 0;
            for (int i = 0; i < result.Length; i++) result[i] ^= (byte)Multiply(divisor[i], factor);
        }
        return result;
    }

    private static int Multiply(int x, int y)
    {
        int z = 0;
        for (int i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }
        return z & 0xFF;
    }
}

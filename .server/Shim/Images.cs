// 图片：服务器不画图，但导入地图的剧本要把一张 PNG 读成像素(每个像素定一格地形)。这里是一块只放像素的"贴图"和一个 PNG 读取器。
//# hand UnityEngine.Texture2D: ctor Apply GetPixel GetPixels32 SetPixels32
//# hand UnityEngine.ImageConversion: LoadImage
//# hand WorldTip: showNow
using System;
using System.IO;
using System.IO.Compression;

namespace UnityEngine
{
    public partial class Texture2D
    {
        // 像素按行存，头一行是图的最底下一行(同 Unity 的约定，模组照这个取)
        internal Color32[] Pixels = Array.Empty<Color32>();

        public Texture2D()
        {
        }

        public Texture2D(int width, int height) : this(width, height, default, false)
        {
        }

        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain)
        {
            Resize(width, height);
        }

        internal void Resize(int newWidth, int newHeight)
        {
            width = Math.Max(0, newWidth);
            height = Math.Max(0, newHeight);
            Pixels = new Color32[width * height];
        }

        public void Apply()
        {
        }

        public void Apply(bool updateMipmaps)
        {
        }

        public Color32[] GetPixels32() => Pixels;
        public Color GetPixel(int x, int y) => x < 0 || y < 0 || x >= width || y >= height ? default(Color) : (Color)Pixels[y * width + x];

        public void SetPixels32(Color32[] colors)
        {
            if (colors != null && colors.Length == Pixels.Length) Array.Copy(colors, Pixels, colors.Length);
        }
    }

    public static partial class ImageConversion
    {
        // 读 PNG(八位或十六位，灰度、真彩、调色板，带不带透明都行；不认隔行存的)。读不出回假
        public static bool LoadImage(this Texture2D tex, byte[] data)
        {
            if (tex == null || data == null) return false;
            try
            {
                if (!EmpireCraft.Server.PngReader.Read(data, out int width, out int height, out Color32[] pixels)) return false;
                tex.Resize(width, height);
                Array.Copy(pixels, tex.Pixels, pixels.Length);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}

namespace EmpireCraft.Server
{
    internal static class PngReader
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        internal static bool Read(byte[] data, out int width, out int height, out UnityEngine.Color32[] pixels)
        {
            width = height = 0;
            pixels = null;
            if (data.Length < 33) return false;
            for (int i = 0; i < 8; i++)
                if (data[i] != Signature[i]) return false;
            int depth = 0, kind = 0, interlace = 0;
            byte[] palette = null, alpha = null;
            using var packed = new MemoryStream();
            for (int at = 8; at + 12 <= data.Length;)
            {
                int length = data[at] << 24 | data[at + 1] << 16 | data[at + 2] << 8 | data[at + 3];
                string type = "" + (char)data[at + 4] + (char)data[at + 5] + (char)data[at + 6] + (char)data[at + 7];
                int body = at + 8;
                if (length < 0 || body + length > data.Length) return false;
                if (type == "IHDR")
                {
                    width = data[body] << 24 | data[body + 1] << 16 | data[body + 2] << 8 | data[body + 3];
                    height = data[body + 4] << 24 | data[body + 5] << 16 | data[body + 6] << 8 | data[body + 7];
                    depth = data[body + 8];
                    kind = data[body + 9];
                    interlace = data[body + 12];
                }
                else if (type == "PLTE") palette = data.AsSpan(body, length).ToArray();
                else if (type == "tRNS") alpha = data.AsSpan(body, length).ToArray();
                else if (type == "IDAT") packed.Write(data, body, length);
                else if (type == "IEND") break;
                at = body + length + 4;
            }
            if (width <= 0 || height <= 0 || width > 16384 || height > 16384 || interlace != 0) return false;
            int channels = kind switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
            if (channels == 0 || depth is not (1 or 2 or 4 or 8 or 16) || kind == 3 && palette == null) return false;
            int bitsPerPixel = channels * depth, stride = (width * bitsPerPixel + 7) / 8, step = Math.Max(1, bitsPerPixel / 8);
            var raw = new byte[(stride + 1) * height];
            packed.Position = 0;
            using (var inflate = new ZLibStream(packed, CompressionMode.Decompress))
            {
                int have = 0;
                while (have < raw.Length)
                {
                    int read = inflate.Read(raw, have, raw.Length - have);
                    if (read <= 0) break;
                    have += read;
                }
                if (have < raw.Length) return false;
            }
            // 逐行还原(每行头一个字节说这一行是照哪种办法存的差值)
            var line = new byte[stride];
            var above = new byte[stride];
            pixels = new UnityEngine.Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                int start = y * (stride + 1);
                byte filter = raw[start];
                for (int x = 0; x < stride; x++)
                {
                    int value = raw[start + 1 + x], left = x >= step ? line[x - step] : 0, up = above[x], corner = x >= step ? above[x - step] : 0;
                    switch (filter)
                    {
                        case 1: value += left; break;
                        case 2: value += up; break;
                        case 3: value += (left + up) / 2; break;
                        case 4:
                            int guess = left + up - corner, dl = Math.Abs(guess - left), du = Math.Abs(guess - up), dc = Math.Abs(guess - corner);
                            value += dl <= du && dl <= dc ? left : du <= dc ? up : corner;
                            break;
                    }
                    line[x] = (byte)value;
                }
                int row = (height - 1 - y) * width;
                for (int x = 0; x < width; x++) pixels[row + x] = Pixel(line, x, kind, depth, palette, alpha);
                (line, above) = (above, line);
            }
            return true;
        }

        private static int Sample(byte[] line, int index, int depth)
        {
            if (depth == 8) return line[index];
            if (depth == 16) return line[index * 2];
            int bit = index * depth, value = line[bit / 8] >> (8 - depth - bit % 8) & ((1 << depth) - 1);
            return value;
        }

        private static UnityEngine.Color32 Pixel(byte[] line, int x, int kind, int depth, byte[] palette, byte[] alpha)
        {
            switch (kind)
            {
                case 0:
                {
                    int gray = Sample(line, x, depth);
                    if (depth < 8) gray = gray * 255 / ((1 << depth) - 1);
                    return new UnityEngine.Color32((byte)gray, (byte)gray, (byte)gray, 255);
                }
                case 2:
                    return new UnityEngine.Color32((byte)Sample(line, x * 3, depth), (byte)Sample(line, x * 3 + 1, depth), (byte)Sample(line, x * 3 + 2, depth), 255);
                case 3:
                {
                    int index = Sample(line, x, depth);
                    if (index * 3 + 2 >= palette.Length) return new UnityEngine.Color32(0, 0, 0, 255);
                    return new UnityEngine.Color32(palette[index * 3], palette[index * 3 + 1], palette[index * 3 + 2], alpha != null && index < alpha.Length ? alpha[index] : (byte)255);
                }
                case 4:
                {
                    byte gray = (byte)Sample(line, x * 2, depth);
                    return new UnityEngine.Color32(gray, gray, gray, (byte)Sample(line, x * 2 + 1, depth));
                }
                default:
                    return new UnityEngine.Color32((byte)Sample(line, x * 4, depth), (byte)Sample(line, x * 4 + 1, depth), (byte)Sample(line, x * 4 + 2, depth), (byte)Sample(line, x * 4 + 3, depth));
            }
        }
    }
}

// 屏幕上的提示：服务器没有屏幕，只把最近一条记下(开局没成时拿它当原因报给上传的人)
public partial class WorldTip
{
    internal static string ShimLast = "";

    public static void showNow(string pText, bool pTranslate = true, string pPosition = "center", float pTime = 3f, string pColor = "#F3961F")
    {
        ShimLast = pTranslate ? NeoModLoader.General.LM.Get(pText) : pText ?? "";
    }
}

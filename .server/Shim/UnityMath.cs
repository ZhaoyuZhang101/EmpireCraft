// Unity 里纯算术的那几样：模组核心到处用 Mathf、Vector2、Color32 这些名字，这里照同样的名字和算法自己写一份。
//# hand T:UnityEngine.ColorUtility
using System;
using System.Diagnostics;
using System.Globalization;

namespace UnityEngine
{
    public static class Mathf
    {
        public const float PI = 3.14159274f, Infinity = float.PositiveInfinity, NegativeInfinity = float.NegativeInfinity;
        public const float Deg2Rad = 0.0174532924f, Rad2Deg = 57.29578f, Epsilon = float.Epsilon;

        public static float Max(float a, float b) => a > b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;

        public static float Max(params float[] values)
        {
            if (values.Length == 0) return 0f;
            float best = values[0];
            for (int i = 1; i < values.Length; i++) if (values[i] > best) best = values[i];
            return best;
        }

        public static int Max(params int[] values)
        {
            if (values.Length == 0) return 0;
            int best = values[0];
            for (int i = 1; i < values.Length; i++) if (values[i] > best) best = values[i];
            return best;
        }

        public static float Min(params float[] values)
        {
            if (values.Length == 0) return 0f;
            float best = values[0];
            for (int i = 1; i < values.Length; i++) if (values[i] < best) best = values[i];
            return best;
        }

        public static int Min(params int[] values)
        {
            if (values.Length == 0) return 0;
            int best = values[0];
            for (int i = 1; i < values.Length; i++) if (values[i] < best) best = values[i];
            return best;
        }

        public static int RoundToInt(float f) => (int)Math.Round(f);
        public static int FloorToInt(float f) => (int)Math.Floor(f);
        public static int CeilToInt(float f) => (int)Math.Ceiling(f);
        public static float Round(float f) => (float)Math.Round(f);
        public static float Floor(float f) => (float)Math.Floor(f);
        public static float Ceil(float f) => (float)Math.Ceiling(f);
        public static float Abs(float f) => Math.Abs(f);
        public static int Abs(int value) => Math.Abs(value);
        public static float Sqrt(float f) => (float)Math.Sqrt(f);
        public static float Pow(float f, float p) => (float)Math.Pow(f, p);
        public static float Exp(float power) => (float)Math.Exp(power);
        public static float Log(float f) => (float)Math.Log(f);
        public static float Log(float f, float p) => (float)Math.Log(f, p);
        public static float Log10(float f) => (float)Math.Log10(f);
        public static float Sin(float f) => (float)Math.Sin(f);
        public static float Cos(float f) => (float)Math.Cos(f);
        public static float Tan(float f) => (float)Math.Tan(f);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Sign(float f) => f >= 0f ? 1f : -1f;
        public static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
        public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
        public static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float LerpUnclamped(float a, float b, float t) => a + (b - a) * t;
        public static float InverseLerp(float a, float b, float value) => a != b ? Clamp01((value - a) / (b - a)) : 0f;
        public static float MoveTowards(float current, float target, float maxDelta) =>
            Abs(target - current) <= maxDelta ? target : current + Sign(target - current) * maxDelta;
        public static float Repeat(float t, float length) => Clamp(t - Floor(t / length) * length, 0f, length);

        public static float DeltaAngle(float current, float target)
        {
            float delta = Repeat(target - current, 360f);
            return delta > 180f ? delta - 360f : delta;
        }

        public static bool Approximately(float a, float b) =>
            Abs(b - a) < Max(1E-06f * Max(Abs(a), Abs(b)), Epsilon * 8f);
    }

    public struct Vector2 : IEquatable<Vector2>
    {
        public float x, y;

        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public static Vector2 zero => new(0f, 0f);
        public static Vector2 one => new(1f, 1f);
        public static Vector2 right => new(1f, 0f);
        public static Vector2 left => new(-1f, 0f);
        public static Vector2 up => new(0f, 1f);
        public static Vector2 down => new(0f, -1f);
        public float sqrMagnitude => x * x + y * y;
        public float magnitude => (float)Math.Sqrt(x * x + y * y);

        public Vector2 normalized
        {
            get
            {
                float length = magnitude;
                return length > 1E-05f ? new Vector2(x / length, y / length) : zero;
            }
        }

        public static float Distance(Vector2 a, Vector2 b) => (a - b).magnitude;
        public static float SqrMagnitude(Vector2 a) => a.sqrMagnitude;
        public static float Dot(Vector2 lhs, Vector2 rhs) => lhs.x * rhs.x + lhs.y * rhs.y;

        public static Vector2 Lerp(Vector2 a, Vector2 b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Vector2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);
        }

        public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);
        public static Vector2 operator -(Vector2 a) => new(-a.x, -a.y);
        public static Vector2 operator *(Vector2 a, float d) => new(a.x * d, a.y * d);
        public static Vector2 operator *(float d, Vector2 a) => new(a.x * d, a.y * d);
        public static Vector2 operator /(Vector2 a, float d) => new(a.x / d, a.y / d);
        public static bool operator ==(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 9.99999944E-11f;
        public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);
        public static implicit operator Vector3(Vector2 v) => new(v.x, v.y, 0f);
        public static implicit operator Vector2(Vector3 v) => new(v.x, v.y);
        public bool Equals(Vector2 other) => x == other.x && y == other.y;
        public override bool Equals(object other) => other is Vector2 vector && Equals(vector);
        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2);
        public override string ToString() => "(" + x.ToString("F2") + ", " + y.ToString("F2") + ")";
    }

    public struct Vector3 : IEquatable<Vector3>
    {
        public float x, y, z;

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public Vector3(float x, float y)
        {
            this.x = x;
            this.y = y;
            z = 0f;
        }

        public static Vector3 zero => new(0f, 0f, 0f);
        public static Vector3 one => new(1f, 1f, 1f);
        public float sqrMagnitude => x * x + y * y + z * z;
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;

        public static Vector3 Lerp(Vector3 a, Vector3 b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
        }

        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float d) => new(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator *(float d, Vector3 a) => new(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator /(Vector3 a, float d) => new(a.x / d, a.y / d, a.z / d);
        public static bool operator ==(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 9.99999944E-11f;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
        public bool Equals(Vector3 other) => x == other.x && y == other.y && z == other.z;
        public override bool Equals(object other) => other is Vector3 vector && Equals(vector);
        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2);
    }

    public struct Vector2Int : IEquatable<Vector2Int>
    {
        public int x { get; set; }
        public int y { get; set; }

        public Vector2Int(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        public static Vector2Int zero => new(0, 0);
        public static Vector2Int one => new(1, 1);
        public static Vector2Int up => new(0, 1);
        public static Vector2Int down => new(0, -1);
        public static Vector2Int left => new(-1, 0);
        public static Vector2Int right => new(1, 0);
        public int sqrMagnitude => x * x + y * y;
        public float magnitude => (float)Math.Sqrt(x * x + y * y);
        public static float Distance(Vector2Int a, Vector2Int b) => (a - b).magnitude;
        public static Vector2Int RoundToInt(Vector2 v) => new(Mathf.RoundToInt(v.x), Mathf.RoundToInt(v.y));
        public static Vector2Int FloorToInt(Vector2 v) => new(Mathf.FloorToInt(v.x), Mathf.FloorToInt(v.y));
        public static Vector2Int operator +(Vector2Int a, Vector2Int b) => new(a.x + b.x, a.y + b.y);
        public static Vector2Int operator -(Vector2Int a, Vector2Int b) => new(a.x - b.x, a.y - b.y);
        public static Vector2Int operator *(Vector2Int a, int b) => new(a.x * b, a.y * b);
        public static bool operator ==(Vector2Int lhs, Vector2Int rhs) => lhs.x == rhs.x && lhs.y == rhs.y;
        public static bool operator !=(Vector2Int lhs, Vector2Int rhs) => !(lhs == rhs);
        public static implicit operator Vector2(Vector2Int v) => new(v.x, v.y);
        public bool Equals(Vector2Int other) => x == other.x && y == other.y;
        public override bool Equals(object other) => other is Vector2Int vector && Equals(vector);
        public override int GetHashCode() => x * 73856093 ^ y * 19349663;
        public override string ToString() => "(" + x + ", " + y + ")";
    }

    public struct Color : IEquatable<Color>
    {
        public float r, g, b, a;

        public Color(float r, float g, float b, float a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public Color(float r, float g, float b) : this(r, g, b, 1f)
        {
        }

        public static Color white => new(1f, 1f, 1f, 1f);
        public static Color black => new(0f, 0f, 0f, 1f);
        public static Color red => new(1f, 0f, 0f, 1f);
        public static Color green => new(0f, 1f, 0f, 1f);
        public static Color blue => new(0f, 0f, 1f, 1f);
        public static Color yellow => new(1f, 0.921568632f, 0.0156862754f, 1f);
        public static Color cyan => new(0f, 1f, 1f, 1f);
        public static Color magenta => new(1f, 0f, 1f, 1f);
        public static Color gray => new(0.5f, 0.5f, 0.5f, 1f);
        public static Color grey => gray;
        public static Color clear => new(0f, 0f, 0f, 0f);

        public static Color Lerp(Color a, Color b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);
        }

        public static void RGBToHSV(Color rgbColor, out float H, out float S, out float V)
        {
            float max = Math.Max(rgbColor.r, Math.Max(rgbColor.g, rgbColor.b)), min = Math.Min(rgbColor.r, Math.Min(rgbColor.g, rgbColor.b));
            float delta = max - min;
            V = max;
            S = max <= 0f ? 0f : delta / max;
            if (delta <= 0f) H = 0f;
            else if (max == rgbColor.r) H = ((rgbColor.g - rgbColor.b) / delta + (rgbColor.g < rgbColor.b ? 6f : 0f)) / 6f;
            else if (max == rgbColor.g) H = ((rgbColor.b - rgbColor.r) / delta + 2f) / 6f;
            else H = ((rgbColor.r - rgbColor.g) / delta + 4f) / 6f;
        }

        public static Color HSVToRGB(float H, float S, float V)
        {
            if (S <= 0f) return new Color(V, V, V, 1f);
            float h = (H - (float)Math.Floor(H)) * 6f;
            int sector = (int)Math.Floor(h);
            float f = h - sector, p = V * (1f - S), q = V * (1f - S * f), t = V * (1f - S * (1f - f));
            return sector switch
            {
                0 => new Color(V, t, p, 1f), 1 => new Color(q, V, p, 1f), 2 => new Color(p, V, t, 1f),
                3 => new Color(p, q, V, 1f), 4 => new Color(t, p, V, 1f), _ => new Color(V, p, q, 1f)
            };
        }

        public static Color operator *(Color a, float b) => new(a.r * b, a.g * b, a.b * b, a.a * b);
        public static Color operator *(Color a, Color b) => new(a.r * b.r, a.g * b.g, a.b * b.b, a.a * b.a);
        public static Color operator +(Color a, Color b) => new(a.r + b.r, a.g + b.g, a.b + b.b, a.a + b.a);
        public static bool operator ==(Color a, Color b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        public static bool operator !=(Color a, Color b) => !(a == b);
        public bool Equals(Color other) => this == other;
        public override bool Equals(object other) => other is Color color && this == color;
        public override int GetHashCode() => HashCode.Combine(r, g, b, a);
    }

    public struct Color32 : IEquatable<Color32>
    {
        public byte r, g, b, a;

        public Color32(byte r, byte g, byte b, byte a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public static implicit operator Color(Color32 c) => new(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);

        public static implicit operator Color32(Color c) => new(
            (byte)Math.Round(Mathf.Clamp01(c.r) * 255f), (byte)Math.Round(Mathf.Clamp01(c.g) * 255f),
            (byte)Math.Round(Mathf.Clamp01(c.b) * 255f), (byte)Math.Round(Mathf.Clamp01(c.a) * 255f));

        public static Color32 Lerp(Color32 a, Color32 b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Color32((byte)(a.r + (b.r - a.r) * t), (byte)(a.g + (b.g - a.g) * t), (byte)(a.b + (b.b - a.b) * t), (byte)(a.a + (b.a - a.a) * t));
        }

        public bool Equals(Color32 other) => r == other.r && g == other.g && b == other.b && a == other.a;
        public override bool Equals(object other) => other is Color32 color && Equals(color);
        public override int GetHashCode() => r | g << 8 | b << 16 | a << 24;
    }

    public static class ColorUtility
    {
        public static string ToHtmlStringRGB(Color color)
        {
            Color32 c = color;
            return c.r.ToString("X2") + c.g.ToString("X2") + c.b.ToString("X2");
        }

        public static string ToHtmlStringRGBA(Color color)
        {
            Color32 c = color;
            return c.r.ToString("X2") + c.g.ToString("X2") + c.b.ToString("X2") + c.a.ToString("X2");
        }

        // 认 #RGB、#RRGGBB、#RRGGBBAA
        public static bool TryParseHtmlString(string htmlString, out Color color)
        {
            color = default;
            if (string.IsNullOrEmpty(htmlString)) return false;
            string hex = htmlString.Trim().TrimStart('#');
            if (hex.Length == 3) hex = "" + hex[0] + hex[0] + hex[1] + hex[1] + hex[2] + hex[2];
            if (hex.Length == 6) hex += "FF";
            if (hex.Length != 8 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value)) return false;
            color = new Color32((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
            return true;
        }
    }

    public struct Rect
    {
        public float x, y, width, height;

        public Rect(float x, float y, float width, float height)
        {
            this.x = x;
            this.y = y;
            this.width = width;
            this.height = height;
        }

        public static Rect MinMaxRect(float xmin, float ymin, float xmax, float ymax) => new(xmin, ymin, xmax - xmin, ymax - ymin);

        public float xMin
        {
            get => x;
            set
            {
                float right = xMax;
                x = value;
                width = right - x;
            }
        }

        public float yMin
        {
            get => y;
            set
            {
                float top = yMax;
                y = value;
                height = top - y;
            }
        }

        public float xMax
        {
            get => x + width;
            set => width = value - x;
        }

        public float yMax
        {
            get => y + height;
            set => height = value - y;
        }

        public Vector2 center => new(x + width / 2f, y + height / 2f);
        public Vector2 size => new(width, height);
        public bool Contains(Vector2 point) => point.x >= xMin && point.x < xMax && point.y >= yMin && point.y < yMax;
    }

    public static class Random
    {
        public static float value => System.Random.Shared.NextSingle();
        public static int Range(int minInclusive, int maxExclusive) => maxExclusive <= minInclusive ? minInclusive : System.Random.Shared.Next(minInclusive, maxExclusive);
        public static float Range(float minInclusive, float maxInclusive) => minInclusive + (maxInclusive - minInclusive) * System.Random.Shared.NextSingle();
    }

    // 时间：服务器的主循环每走一步把这几样往前拨(见 ServerLoop)
    public static class Time
    {
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        public static float realtimeSinceStartup => (float)Clock.Elapsed.TotalSeconds;
        public static float unscaledTime { get; set; }
        public static float time { get; set; }
        public static int frameCount { get; set; }
        public static float unscaledDeltaTime { get; set; }
        public static float deltaTime { get; set; }
        public static float smoothDeltaTime { get; set; }
        public static float timeScale { get; set; } = 1f;
    }

    public static class Debug
    {
        public static void Log(object message) => Console.WriteLine(message);
        public static void LogWarning(object message) => Console.WriteLine("[warn] " + message);
        public static void LogError(object message) => Console.Error.WriteLine("[error] " + message);
        public static void LogException(Exception exception) => Console.Error.WriteLine("[error] " + exception);
    }

    // Unity 对象的根：销毁过的对象同 null 比较算相等，模组里到处靠这个判断"还在不在"
    public partial class Object
    {
        public string name { get; set; } = "";
        public HideFlags hideFlags { get; set; }
        internal bool Destroyed;
        private static int _nextId;
        private readonly int _id = System.Threading.Interlocked.Increment(ref _nextId);

        public int GetInstanceID() => _id;

        public static void Destroy(Object obj)
        {
            if (!ReferenceEquals(obj, null)) obj.Destroyed = true;
        }

        public static void Destroy(Object obj, float t) => Destroy(obj);
        public static void DestroyImmediate(Object obj) => Destroy(obj);

        public static void DontDestroyOnLoad(Object target)
        {
        }

        // 界面预制件的复制：服务器没有界面，复制出来的就是原件(多半是空)
        public static T Instantiate<T>(T original) where T : Object => original;
        public static T Instantiate<T>(T original, Transform parent) where T : Object => original;
        public static T Instantiate<T>(T original, Transform parent, bool worldPositionStays) where T : Object => original;
        public static T[] FindObjectsOfType<T>() where T : Object => Array.Empty<T>();
        public static T FindObjectOfType<T>() where T : Object => null;
        private static bool Gone(Object obj) => ReferenceEquals(obj, null) || obj.Destroyed;
        public static bool operator ==(Object x, Object y) => Gone(x) ? Gone(y) : ReferenceEquals(x, y);
        public static bool operator !=(Object x, Object y) => !(x == y);
        public static implicit operator bool(Object exists) => !Gone(exists);
        public override bool Equals(object other) => other is Object obj ? this == obj : other == null && Destroyed;
        public override int GetHashCode() => _id;
        public override string ToString() => name;
    }
}

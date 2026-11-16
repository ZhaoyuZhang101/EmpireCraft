using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.services;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace EmpireCraft.Scripts.Data;

// 模组存档多核写(用户 2026-11-17 要多核)：存档时世界是停着的(主线程在等)，上百兆的数据只读不改，
// 正好分给几个核同时转成文字——大的列表(族谱、各国各城各人的扩展数据)切成片、每片一个任务，其余每项一个任务；
// 主线程一批一批收回来按原来的先后写盘。写出来的文件同一个核从头写到尾的一字不差(Verify 开着时两样都写、逐字节比)。
// 一批只做"核数的两倍"那么多片，写完就放掉：内存里同时只有一批的文字，小内存的服务器上不会把整份存档攒在内存里。
// 只在独立核心开(Enabled，见 .server/Program.cs)；游戏里照旧一个核写。出了岔子(设置不是默认的、哪一片转不出来)退回老办法重写一遍。
public static class ParallelSaveWriter
{
    public static bool Enabled;
    // 排查用(独立核心的 --verify-save)：多核写完再照老办法写一份到旁边，逐字节比
    public static bool Verify;
    public static int Written, FellBack, Verified, Mismatched;
    public static double LastMilliseconds;
    private const int ChunkItems = 256, MinChunkedItems = 512;

    private sealed class Piece
    {
        public object Value;
        public IList List;
        public int From, To;
        public string Before, After, Text;
    }

    public static void Write(string path, SaveData data)
    {
        var clock = global::System.Diagnostics.Stopwatch.StartNew();
        if (Enabled && ParallelWork.MaxWorkers > 1)
        {
            try
            {
                WriteChunked(path, data);
                Written++;
                LastMilliseconds = clock.Elapsed.TotalMilliseconds;
                if (Verify) VerifyAgainstPlain(path, data);
                return;
            }
            catch (Exception exception)
            {
                FellBack++;
                LogService.LogWarning($"[EmpireCraft][存档] 多核写没成，改回一个核写: {exception.Message}");
            }
        }
        WritePlain(path, data);
        LastMilliseconds = clock.Elapsed.TotalMilliseconds;
    }

    private static void WritePlain(string path, SaveData data)
    {
        // 流式序列化直接写盘：不先攒出一个大字符串
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        JsonSerializer.CreateDefault().Serialize(writer, data);
    }

    private static void WriteChunked(string path, SaveData data)
    {
        JsonSerializer probe = JsonSerializer.CreateDefault();
        // 切片各自转文字要同整份一起转是一个结果：只认默认的设置(不带类型名、不记引用、不省空值与默认值、不排版)
        if (probe.NullValueHandling != NullValueHandling.Include || probe.DefaultValueHandling != DefaultValueHandling.Include ||
            probe.PreserveReferencesHandling != PreserveReferencesHandling.None || probe.TypeNameHandling != TypeNameHandling.None ||
            probe.Formatting != Formatting.None)
            throw new NotSupportedException("序列化设置不是默认的");
        if (probe.ContractResolver.ResolveContract(typeof(SaveData)) is not JsonObjectContract contract)
            throw new NotSupportedException("存档对象不是按字段逐项写的");
        var pieces = new List<Piece>();
        bool firstProperty = true;
        foreach (JsonProperty property in contract.Properties)
        {
            if (property.Ignored || !property.Readable) continue;
            if (property.ShouldSerialize != null && !property.ShouldSerialize(data)) continue;
            if (property.NullValueHandling.HasValue || property.DefaultValueHandling.HasValue || property.Converter != null ||
                property.ItemConverter != null || property.IsReference == true || property.TypeNameHandling.HasValue)
                throw new NotSupportedException("字段 " + property.PropertyName + " 有单独的序列化设置");
            object value = property.ValueProvider.GetValue(data);
            string head = (firstProperty ? "" : ",") + JsonConvert.ToString(property.PropertyName) + ":";
            firstProperty = false;
            Type type = value?.GetType();
            if (value is IList list && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) && list.Count >= MinChunkedItems)
            {
                for (int from = 0; from < list.Count; from += ChunkItems)
                {
                    int to = Math.Min(list.Count, from + ChunkItems);
                    pieces.Add(new Piece
                    {
                        List = list, From = from, To = to, Before = from == 0 ? head + "[" : ",", After = to == list.Count ? "]" : ""
                    });
                }
            }
            else pieces.Add(new Piece { Value = value, Before = head, After = "" });
        }

        int batch = Math.Max(2, ParallelWork.MaxWorkers * 2);
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false), 1 << 16);
        writer.Write('{');
        for (int start = 0; start < pieces.Count; start += batch)
        {
            int count = Math.Min(batch, pieces.Count - start);
            int offset = start;
            Exception failed = null;
            ParallelWork.For(count, index =>
            {
                try
                {
                    Render(pieces[offset + index]);
                }
                catch (Exception exception)
                {
                    failed = exception;
                }
            });
            if (failed != null) throw failed;
            for (int index = 0; index < count; index++)
            {
                Piece piece = pieces[offset + index];
                writer.Write(piece.Before);
                writer.Write(piece.Text);
                writer.Write(piece.After);
                piece.Text = null;
            }
        }
        writer.Write('}');
    }

    // 工作线程：一片转成文字。只读，不碰别的片
    private static void Render(Piece piece)
    {
        JsonSerializer serializer = JsonSerializer.CreateDefault();
        var text = new StringBuilder(1 << 14);
        using var output = new StringWriter(text, CultureInfo.InvariantCulture);
        if (piece.List == null)
        {
            using var json = new JsonTextWriter(output) { CloseOutput = false };
            serializer.Serialize(json, piece.Value);
        }
        else
            for (int index = piece.From; index < piece.To; index++)
            {
                if (index > piece.From) output.Write(',');
                using var json = new JsonTextWriter(output) { CloseOutput = false };
                serializer.Serialize(json, piece.List[index]);
            }
        output.Flush();
        piece.Text = text.ToString();
    }

    private static void VerifyAgainstPlain(string path, SaveData data)
    {
        string plain = path + ".verify";
        try
        {
            WritePlain(plain, data);
            bool same = SameBytes(path, plain);
            if (same)
            {
                Verified++;
                File.Delete(plain);
            }
            else
            {
                Mismatched++;
                LogService.LogWarning($"[EmpireCraft][存档] 多核写出来的同一个核写的不一样，两份都留着: {path} / {plain}");
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][存档] 核对没做成: {exception.Message}");
        }
    }

    private static bool SameBytes(string first, string second)
    {
        using FileStream a = File.OpenRead(first), b = File.OpenRead(second);
        if (a.Length != b.Length) return false;
        byte[] left = new byte[1 << 16], right = new byte[1 << 16];
        while (true)
        {
            int read = a.Read(left, 0, left.Length);
            if (read <= 0) return true;
            int other = 0;
            while (other < read)
            {
                int got = b.Read(right, other, read - other);
                if (got <= 0) return false;
                other += got;
            }
            for (int i = 0; i < read; i++)
                if (left[i] != right[i]) return false;
        }
    }
}

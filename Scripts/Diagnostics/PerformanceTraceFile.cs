using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace EmpireCraft.Scripts.Diagnostics;

// 独立于 Unity/加载器日志设置；低频数值摘要由一个后台写入者落盘。
public static class PerformanceTraceFile
{
    public const int MaximumPending = 64;
    public const long MaximumBytes = 1024 * 1024;
    private static readonly ConcurrentQueue<string> Lines = new();
    private static string _path;
    private static int _pending, _writing;
    private static long _dropped, _failures;
    public static int PendingCount => Volatile.Read(ref _pending);
    public static bool IsWriting => Volatile.Read(ref _writing) != 0;
    public static long DroppedLines => Interlocked.Read(ref _dropped);
    public static long FailedWrites => Interlocked.Read(ref _failures);
    public static int LastWriteThreadId { get; private set; }

    public static void Configure(string folder)
    {
        if (!string.IsNullOrEmpty(folder))
            Interlocked.CompareExchange(ref _path, Path.Combine(folder, "EmpireCraft.performance.log"), null);
    }

    public static void Record(string message)
    {
        if (_path == null || string.IsNullOrEmpty(message)) return;
        if (Interlocked.Increment(ref _pending) > MaximumPending)
        {
            Interlocked.Decrement(ref _pending);
            Interlocked.Increment(ref _dropped);
            return;
        }
        string bounded = message.Length > 8192 ? message.Substring(0, 8192) : message;
        Lines.Enqueue(DateTime.UtcNow.ToString("O") + " " + bounded + Environment.NewLine);
        Schedule();
    }

    private static void Schedule()
    {
        if (Interlocked.CompareExchange(ref _writing, 1, 0) == 0)
            ThreadPool.QueueUserWorkItem(_ => Drain());
    }

    private static void Drain()
    {
        try
        {
            while (Lines.TryDequeue(out string line))
            {
                Interlocked.Decrement(ref _pending);
                try
                {
                    LastWriteThreadId = Thread.CurrentThread.ManagedThreadId;
                    string path = _path;
                    if (File.Exists(path) && new FileInfo(path).Length >= MaximumBytes)
                    {
                        File.Copy(path, path + ".previous", true);
                        File.WriteAllText(path, string.Empty, Encoding.UTF8);
                    }
                    File.AppendAllText(path, line, Encoding.UTF8);
                }
                catch { Interlocked.Increment(ref _failures); }
            }
        }
        finally
        {
            Volatile.Write(ref _writing, 0);
            if (!Lines.IsEmpty) Schedule();
        }
    }
}

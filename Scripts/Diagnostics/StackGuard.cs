using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Diagnostics;

// 栈快用完时(深递归，多半是存档数据里有环)：把调用链记一次，再抛异常把这次调用退回去，免得整个游戏原生崩溃。
// 栈溢出时 Unity 连崩溃日志都写不出，系统事件里只留下 worldbox.exe 在 ntdll.dll 访问冲突(0xc0000005)，
// 本地 10-08 起反复出现。探针放在模组取扩展数据的公共入口(几乎所有模组逻辑每层都会经过)与计时段里，每 4 次查一次栈(查得太疏，触发时剩的栈不够抛异常与写日志，Unity 自己会崩)
public static class StackGuard
{
    private static int _counter;
    private static bool _reported;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Probe()
    {
        if ((++_counter & 3) == 0) Check();
    }

    private static void Check()
    {
        try
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();
        }
        catch (InsufficientExecutionStackException)
        {
            if (!_reported)
            {
                _reported = true;
                Report();
            }
            throw;
        }
    }

    // 只列最里面的 60 层与出现最多的方法(递归的那几个会重复成千上万次)
    private static void Report()
    {
        try
        {
            var frames = new StackTrace(1, false).GetFrames() ?? Array.Empty<StackFrame>();
            string Name(StackFrame frame)
            {
                var method = frame.GetMethod();
                return method == null ? "?" : $"{method.DeclaringType?.FullName}.{method.Name}";
            }
            var text = new StringBuilder();
            text.Append($"[EmpireCraft][栈将溢出] 共 {frames.Length} 层，已中止这次调用以免游戏崩溃。重复最多的方法：");
            foreach (var group in frames.Select(Name).GroupBy(name => name).OrderByDescending(group => group.Count()).Take(12))
                text.Append($"\n  {group.Count()} 次 {group.Key}");
            text.Append("\n最里面 60 层：");
            foreach (StackFrame frame in frames.Take(60)) text.Append("\n  ").Append(Name(frame));
            string report = text.ToString();
            LogService.LogError(report);
            File.WriteAllText(Path.Combine(UnityEngine.Application.persistentDataPath, "EmpireCraftStackOverflow.txt"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n" + report);
        }
        catch
        {
            // 栈太紧时记不下来也照样中止
        }
    }
}

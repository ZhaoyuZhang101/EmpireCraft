using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

// 原版 LogHandler.ProcessLog 把每条普通日志都拼进静态字符串 LogHandler.log，只有报错时才清空，报错满 100 条后连清空也不走了。
// 这个串只有"发邮件报错"的按钮读末尾 4000 字，却越拼越长，每条 Debug.Log 都要重新分配一个整串、拷一遍。
// 11-02 那局开局一分钟字体报错就占满 100 条，此后串一路涨到 49 MB：十秒 127 条日志就分配 6 GB、回收 6 次 2 代，
// 帧越跑越慢；最后连续 49 MB 再也分配不到，275 次 OutOfMemoryException 全抛在这一处。只留末尾，按钮读到的内容不变
public class LogHandlerPatch : GamePatch
{
    private const int TrimAtChars = 8000, KeepChars = 4000;

    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(TrimLog)).Patch(
            AccessTools.Method(typeof(LogHandler), "ProcessLog"),
            prefix: new HarmonyMethod(GetType(), nameof(TrimLog))
        );
    }

    public static void TrimLog()
    {
        string log = LogHandler.log;
        if (log != null && log.Length > TrimAtChars) LogHandler.log = log.Substring(log.Length - KeepChars);
    }
}

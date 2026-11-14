using System;
using System.Threading;

namespace EmpireCraft.Server;

// 主线程的同步上下文(同 Unity 的)：模组里 async 方法 await 之后的接续、async void 里没接住的异常，都排回主循环办。
// 没有它，async void 里一抛异常整个进程就退了(Unity 只记一行日志)
internal sealed class MainThreadContext : SynchronizationContext
{
    private static int _reported;

    public override void Post(SendOrPostCallback callback, object state) => ServerRoutes.MainThread.Enqueue(() => Run(callback, state));

    public override void Send(SendOrPostCallback callback, object state) => Run(callback, state);

    public override SynchronizationContext CreateCopy() => this;

    private static void Run(SendOrPostCallback callback, object state)
    {
        try
        {
            callback(state);
        }
        catch (Exception exception)
        {
            if (Interlocked.Increment(ref _reported) <= 50)
                Console.Error.WriteLine("[warn] 异步接续出错(已接住): " + exception.GetType().Name + ": " + exception.Message +
                                        (Shim.Verbose ? "\n" + exception.StackTrace : ""));
        }
    }
}

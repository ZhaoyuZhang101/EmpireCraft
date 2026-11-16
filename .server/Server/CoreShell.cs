using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Web.WebView2.Core;

namespace EmpireCraft.Server;

// 核心自己的窗口和托盘图标(只在 Windows 上；用户 2026-11-16 "核心得给简易的gui吧""可以挂后台""不然我怎么知道开了还是没开"
// "不是主机面板网页, 是想终端一样的可以挂后台的gui")：
//   · 窗口里嵌主机面板网页(Terminal/host.html，没有就嵌终端首页)，用系统自带的 WebView2 运行时；地址带 #admin=管理员码或一次性票，面板自己登入管理员；
//   · 关窗口＝收进托盘，核心照跑；托盘提示一行状态(第几年、几部手机连着)；左键/双击再把窗口叫出来；
//     右键菜单：打开面板、打开管理页(本机浏览器)、停下核心(脱手的是"回到游戏")；
//   · WebView2 起不来(没有运行时等)：面板改用系统 Edge/Chrome 的 --app 独立窗口开，再不行用默认浏览器。
// 自己一个 STA 线程跑消息循环；网页那边的异步回调经 ShellContext 排回这个线程。只用 Win32 与 WebView2 的 Core 库，不引 WinForms
internal static class CoreShell
{
    private const string Title = "帝国盒子 · 服务器核心";
    private const string ClassName = "EmpireCraftCoreShell";

    private const uint WM_DESTROY = 0x0002, WM_SIZE = 0x0005, WM_CLOSE = 0x0010, WM_NULL = 0x0000, WM_TIMER = 0x0113,
        WM_LBUTTONUP = 0x0202, WM_LBUTTONDBLCLK = 0x0203, WM_RBUTTONUP = 0x0205, WM_DPICHANGED = 0x02E0, WM_GETMINMAXINFO = 0x0024, WM_APP = 0x8000;
    private const uint WM_TRAY = WM_APP + 1, WM_RUN = WM_APP + 2, WM_SHOW = WM_APP + 3, WM_QUIT_SHELL = WM_APP + 4;
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_INFO = 0x10, NIIF_INFO = 1;
    private const uint MenuPanel = 1, MenuAdmin = 2, MenuStop = 3;

    private static Thread _thread;
    private static IntPtr _hwnd, _iconBig, _iconSmall, _background;
    private static readonly ManualResetEventSlim Created = new();
    private static WndProc _proc; // 留着引用，别被回收
    private static CoreWebView2Controller _controller;
    private static bool _webFailed, _hiddenOnce, _showWanted;
    private static int _dpi = 96;
    private static DateTime _pageStamp;
    private static string _lastTip = "";

    internal static bool Active => _hwnd != IntPtr.Zero;
    // 托盘提示上的那行状态(主循环每两秒写一次；托盘提示最多 127 个字)
    internal static volatile string Status = "帝国盒子核心 · 启动中";

    // 起窗口和托盘(已起就只按 show 把窗口叫出来)。show = 起来就把窗口亮出来
    internal static void Start(bool show)
    {
        if (!OperatingSystem.IsWindows()) return;
        lock (Created)
        {
            if (_thread != null)
            {
                if (show) Show();
                return;
            }
            _showWanted = show;
            _thread = new Thread(Run) { IsBackground = true, Name = "core-shell" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }
        Created.Wait(5000);
    }

    internal static void Show()
    {
        if (_hwnd != IntPtr.Zero) PostMessageW(_hwnd, WM_SHOW, IntPtr.Zero, IntPtr.Zero);
    }

    // 核心退出前：撤托盘图标(不撤的话任务栏里留一个点了没反应的图标)、关窗口
    internal static void Stop()
    {
        if (_thread == null || _hwnd == IntPtr.Zero) return;
        PostMessageW(_hwnd, WM_QUIT_SHELL, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(3000);
    }

    private static void Run()
    {
        try
        {
            SetProcessDpiAwarenessContext(new IntPtr(-4)); // 按显示器的缩放(Per-Monitor V2)，网页不糊
        }
        catch (Exception)
        {
            // 老系统没有这个接口
        }
        try
        {
            IntPtr instance = GetModuleHandleW(null);
            LoadIcons();
            // 深色底(#161916)：窗口刚起、网页还没画出来时不闪白(用户 2026-11-16 "这个核心的小窗口感觉不好看")
            _background = CreateSolidBrush(0x00161916);
            _proc = WindowProc;
            var cls = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(), lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc), hInstance = instance,
                hIcon = _iconBig, hIconSm = _iconSmall, hCursor = LoadCursorW(IntPtr.Zero, new IntPtr(32512)), hbrBackground = _background, lpszClassName = ClassName
            };
            RegisterClassExW(ref cls);
            uint dpi = 96;
            try
            {
                dpi = GetDpiForSystem();
            }
            catch (Exception)
            {
                // 老系统按 96
            }
            _dpi = (int)dpi;
            int width = 900 * _dpi / 96, height = 600 * _dpi / 96;
            int x = Math.Max(0, (GetSystemMetrics(0) - width) / 2), y = Math.Max(0, (GetSystemMetrics(1) - height) / 2);
            const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
            _hwnd = CreateWindowExW(0, ClassName, Title, WS_OVERLAPPEDWINDOW, x, y, width, height, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("窗口建不起来(" + Marshal.GetLastWin32Error() + ")");
            DarkTitle(_hwnd);
            Tray(NIM_ADD);
            SetTimer(_hwnd, new IntPtr(1), 2000, IntPtr.Zero);
            SynchronizationContext.SetSynchronizationContext(new ShellContext());
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("[error] 核心窗口起不来: " + exception.Message);
            _hwnd = IntPtr.Zero;
            Created.Set();
            return;
        }
        Created.Set();
        StartWebView();
        if (_showWanted) Show();
        while (GetMessageW(out MSG message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }
    }

    private static async void StartWebView()
    {
        try
        {
            string data = Path.Combine(WorldSave.Root ?? Path.GetTempPath(), "webview");
            Directory.CreateDirectory(data);
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, data);
            _controller = await environment.CreateCoreWebView2ControllerAsync(_hwnd);
            _controller.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 0x16, 0x19, 0x16);
            CoreWebView2 web = _controller.CoreWebView2;
            web.Settings.AreDevToolsEnabled = false;
            web.Settings.IsStatusBarEnabled = false;
            web.Settings.IsZoomControlEnabled = false;
            // 面板里要另开的(管理页之类)交给本机浏览器
            web.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                OpenExternal(args.Uri);
            };
            Fit();
            _pageStamp = PageStamp();
            web.Navigate(Program.PanelLink());
        }
        catch (Exception exception)
        {
            _webFailed = true;
            Console.Error.WriteLine("[warn] 核心窗口里嵌不了网页(WebView2 运行时没装或起不来)，面板改用浏览器的独立窗口开: " + exception.Message);
            ShowWindow(_hwnd, 0);
            if (_showWanted) OpenAppWindow();
        }
    }

    private static IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (message)
            {
                case WM_SIZE:
                    Fit();
                    return IntPtr.Zero;
                case WM_GETMINMAXINFO:
                    // 最小 760×520(按缩放)：再小面板就挤了
                    var limits = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                    limits.ptMinTrackSize = new POINT { X = 760 * _dpi / 96, Y = 520 * _dpi / 96 };
                    Marshal.StructureToPtr(limits, lParam, false);
                    return IntPtr.Zero;
                case WM_DPICHANGED:
                    _dpi = (int)(wParam.ToInt64() & 0xFFFF);
                    var suggested = Marshal.PtrToStructure<RECT>(lParam);
                    SetWindowPos(hwnd, IntPtr.Zero, suggested.Left, suggested.Top, suggested.Right - suggested.Left, suggested.Bottom - suggested.Top, 0x0014);
                    return IntPtr.Zero;
                case WM_CLOSE:
                    // 关窗口只是收进托盘，核心照跑；头一回收起时冒个气泡说一声
                    ShowWindow(hwnd, 0);
                    if (!_hiddenOnce)
                    {
                        _hiddenOnce = true;
                        Balloon("核心还在后台跑", "点托盘里的图标再打开面板；要停下核心，在图标上点右键。");
                    }
                    return IntPtr.Zero;
                case WM_TIMER:
                    string tip = Status ?? "";
                    if (tip != _lastTip)
                    {
                        _lastTip = tip;
                        Tray(NIM_MODIFY);
                    }
                    return IntPtr.Zero;
                case WM_TRAY:
                    uint mouse = (uint)(lParam.ToInt64() & 0xFFFF);
                    if (mouse == WM_LBUTTONUP || mouse == WM_LBUTTONDBLCLK) ShowPanel();
                    else if (mouse == WM_RBUTTONUP) Menu();
                    return IntPtr.Zero;
                case WM_SHOW:
                    ShowPanel();
                    return IntPtr.Zero;
                case WM_RUN:
                    ShellContext.Drain();
                    return IntPtr.Zero;
                case WM_QUIT_SHELL:
                    Tray(NIM_DELETE);
                    try
                    {
                        _controller?.Close();
                    }
                    catch (Exception)
                    {
                        // 退出时网页那头已经没了也不要紧
                    }
                    DestroyWindow(hwnd);
                    return IntPtr.Zero;
                case WM_DESTROY:
                    _hwnd = IntPtr.Zero;
                    PostQuitMessage(0);
                    return IntPtr.Zero;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("[error] 核心窗口: " + exception.Message);
        }
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private static void ShowPanel()
    {
        if (_webFailed)
        {
            OpenAppWindow();
            return;
        }
        // 面板网页改过(换了新版面)：叫出窗口时重新载入一回，不必停了核心再开。登入的令牌在网页自己的存储里，重载不用再登
        try
        {
            DateTime stamp = PageStamp();
            if (_controller?.CoreWebView2 != null && stamp != _pageStamp)
            {
                _pageStamp = stamp;
                _controller.CoreWebView2.Reload();
            }
        }
        catch (Exception)
        {
            // 重载不了就照旧显示
        }
        ShowWindow(_hwnd, IsIconic(_hwnd) ? 9 : 5);
        SetForegroundWindow(_hwnd);
    }

    private static DateTime PageStamp()
    {
        try
        {
            string page = Path.Combine(Program.DataRoot ?? "", "Terminal", "host.html");
            return File.Exists(page) ? File.GetLastWriteTimeUtc(page) : DateTime.MinValue;
        }
        catch (Exception)
        {
            return DateTime.MinValue;
        }
    }

    // 深色标题栏(同面板的深色调)：Win10 20H1 起是 20 号属性，更早的 Win10 是 19 号；都不认就照系统的
    private static void DarkTitle(IntPtr hwnd)
    {
        int on = 1;
        try
        {
            if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0) DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
        }
        catch (Exception)
        {
            // 没有 dwmapi(不会发生在 Win10 以上)
        }
    }

    private static void Fit()
    {
        if (_controller == null || _hwnd == IntPtr.Zero) return;
        GetClientRect(_hwnd, out RECT rect);
        _controller.Bounds = new System.Drawing.Rectangle(0, 0, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    private static void Menu()
    {
        IntPtr menu = CreatePopupMenu();
        AppendMenuW(menu, 0, new UIntPtr(MenuPanel), "打开面板");
        AppendMenuW(menu, 0, new UIntPtr(MenuAdmin), "打开管理页(浏览器)");
        AppendMenuW(menu, 0x800, UIntPtr.Zero, null);
        AppendMenuW(menu, 0, new UIntPtr(MenuStop), Program.Detached ? "停下核心，回到游戏" : "停下核心");
        GetCursorPos(out POINT at);
        SetForegroundWindow(_hwnd);
        uint picked = TrackPopupMenu(menu, 0x0100 | 0x0002, at.X, at.Y, 0, _hwnd, IntPtr.Zero);
        PostMessageW(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(menu);
        switch (picked)
        {
            case MenuPanel:
                ShowPanel();
                break;
            case MenuAdmin:
                OpenAdmin();
                break;
            case MenuStop:
                string ask = Program.Detached ? "存好档、停下核心，再把游戏开回来？" : "存好档、停下核心？手机上的玩家都会断开。";
                if (MessageBoxW(_hwnd, ask, Title, 0x1 | 0x20) == 1 && Program.RequestReturn())
                    Balloon("核心正在停下", Program.Detached ? "存完档就把游戏开回来。" : "存完档就退出。");
                break;
        }
    }

    // 退路：WebView2 起不来时，面板用系统 Edge/Chrome 的 --app 独立窗口开(没有地址栏和标签)；都找不到用默认浏览器
    private static void OpenAppWindow()
    {
        string link = Program.PanelLink();
        foreach (string browser in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe")
                 })
        {
            if (!File.Exists(browser)) continue;
            try
            {
                var start = new ProcessStartInfo(browser) { UseShellExecute = false };
                start.ArgumentList.Add("--app=" + link);
                start.ArgumentList.Add("--window-size=900,600");
                Process.Start(start)?.Dispose();
                return;
            }
            catch (Exception)
            {
                // 换下一个
            }
        }
        OpenExternal(link);
    }

    // 网页管理页在本机默认浏览器里开(地址带码或票，开了就是管理员；见 Program.AdminLink)。托盘菜单和面板上的按钮(/api/admin/open)都走这里
    internal static bool OpenAdmin() => OpenExternal(Program.AdminLink());

    private static bool OpenExternal(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("[warn] 浏览器打不开: " + exception.Message);
            return false;
        }
    }

    private static void Tray(uint action)
    {
        var data = NewTrayData(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        Shell_NotifyIconW(action, ref data);
    }

    private static void Balloon(string title, string text)
    {
        var data = NewTrayData(NIF_INFO);
        data.szInfoTitle = title;
        data.szInfo = text;
        data.dwInfoFlags = NIIF_INFO;
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    private static NOTIFYICONDATAW NewTrayData(uint flags)
    {
        string tip = Status ?? "";
        if (tip.Length > 127) tip = tip.Substring(0, 127);
        return new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(), hWnd = _hwnd, uID = 1, uFlags = flags, uCallbackMessage = WM_TRAY, hIcon = _iconSmall,
            szTip = tip, szInfo = "", szInfoTitle = ""
        };
    }

    // 图标用终端网页的那张(Terminal/icon-192.png)，PNG 直接交给系统缩放；读不到用系统默认的程序图标
    private static void LoadIcons()
    {
        string png = Path.Combine(Program.DataRoot ?? "", "Terminal", "icon-192.png");
        if (File.Exists(png))
        {
            byte[] bytes = File.ReadAllBytes(png);
            _iconBig = CreateIconFromResourceEx(bytes, (uint)bytes.Length, true, 0x00030000, GetSystemMetrics(11), GetSystemMetrics(12), 0);
            _iconSmall = CreateIconFromResourceEx(bytes, (uint)bytes.Length, true, 0x00030000, GetSystemMetrics(49), GetSystemMetrics(50), 0);
        }
        IntPtr fallback = LoadIconW(IntPtr.Zero, new IntPtr(32512));
        if (_iconBig == IntPtr.Zero) _iconBig = fallback;
        if (_iconSmall == IntPtr.Zero) _iconSmall = fallback;
    }

    // 网页那边的异步回调排回窗口线程跑(WebView2 只许在建它的线程上动)
    private sealed class ShellContext : SynchronizationContext
    {
        private static readonly ConcurrentQueue<(SendOrPostCallback, object)> Queue = new();

        public override void Post(SendOrPostCallback callback, object state)
        {
            Queue.Enqueue((callback, state));
            PostMessageW(_hwnd, WM_RUN, IntPtr.Zero, IntPtr.Zero);
        }

        public override void Send(SendOrPostCallback callback, object state)
        {
            if (Thread.CurrentThread == _thread) callback(state);
            else
            {
                using var done = new ManualResetEventSlim();
                Post(_ =>
                {
                    callback(state);
                    done.Set();
                }, null);
                done.Wait();
            }
        }

        public override SynchronizationContext CreateCopy() => this;

        internal static void Drain()
        {
            while (Queue.TryDequeue(out var item))
            {
                try
                {
                    item.Item1(item.Item2);
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine("[error] 核心窗口: " + exception.Message);
                }
            }
        }
    }

    #region Win32

    private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string lpszMenuName, lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam, lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID, uFlags, uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern ushort RegisterClassExW(ref WNDCLASSEXW cls);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu,
        IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetMessageW(out MSG message, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessageW(ref MSG message);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr SetTimer(IntPtr hwnd, IntPtr id, uint milliseconds, IntPtr callback);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern IntPtr LoadCursorW(IntPtr instance, IntPtr name);
    [DllImport("user32.dll")] private static extern IntPtr LoadIconW(IntPtr instance, IntPtr name);
    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconFromResourceEx(byte[] bits, uint size, bool icon, uint version, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenuW(IntPtr menu, uint flags, UIntPtr id, string text);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int MessageBoxW(IntPtr hwnd, string text, string caption, uint type);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string name);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);

    #endregion
}

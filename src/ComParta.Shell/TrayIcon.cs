using System.Runtime.InteropServices;

namespace ComParta.Shell;

/// <summary>
/// The notification-area icon (AD-14): a click opens the window, the menu offers "Open ComParta"
/// and "Quit". Plain Win32 on its own thread, so it works while the window is hidden and needs no
/// extra package. The callbacks run on that thread; the caller moves to its UI thread.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const uint WmApp = 0x8000;
    private const uint CallbackMessage = WmApp + 1;
    private const uint WmClose = 0x0010;
    private const uint WmDestroy = 0x0002;
    private const uint WmNull = 0x0000;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmContextMenu = 0x007B;
    private const uint NimAdd = 0, NimDelete = 2;
    private const uint NifMessage = 0x1, NifIcon = 0x2, NifTip = 0x4;
    private const uint MfString = 0x0;
    private const uint TpmReturnCmd = 0x0100, TpmRightButton = 0x0002;
    private const int OpenCommand = 1, QuitCommand = 2;
    private const int IdiApplication = 32512;

    private readonly string _tooltip;
    private readonly string _iconPath;
    private readonly Func<(string Open, string Quit)> _labels;
    private readonly Action _open;
    private readonly Action _quit;
    private readonly Action<Exception> _log;
    private readonly WndProc _proc;
    private readonly ManualResetEventSlim _ready = new();
    private IntPtr _window;
    private IntPtr _icon;
    private uint _taskbarCreated;

    /// <param name="labels">The menu's words, read each time it opens so they follow the language.</param>
    public TrayIcon(string tooltip, string iconPath, Func<(string Open, string Quit)> labels, Action open, Action quit, Action<Exception> log)
    {
        _tooltip = tooltip;
        _iconPath = iconPath;
        _labels = labels;
        _open = open;
        _quit = quit;
        _log = log;
        _proc = Handle;
        if (!OperatingSystem.IsWindows())
            return;
        var thread = new Thread(Run) { IsBackground = true, Name = "ComParta icon" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        if (_window != IntPtr.Zero)
            PostMessageW(_window, WmClose, IntPtr.Zero, IntPtr.Zero);
    }

    private void Run()
    {
        try
        {
            var className = "ComPartaIcon" + Environment.ProcessId;
            var windowClass = new WndClassEx
            {
                Size = (uint)Marshal.SizeOf<WndClassEx>(),
                Proc = Marshal.GetFunctionPointerForDelegate(_proc),
                Instance = GetModuleHandleW(null),
                ClassName = className,
            };
            if (RegisterClassExW(ref windowClass) == 0)
                throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastWin32Error()}");
            // A hidden top-level window: the menu needs one it can bring to the front.
            _window = CreateWindowExW(0, className, "ComParta", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, windowClass.Instance, IntPtr.Zero);
            if (_window == IntPtr.Zero)
                throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
            _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
            _icon = ExtractIconW(windowClass.Instance, _iconPath, 0);
            if (_icon == IntPtr.Zero || _icon == new IntPtr(1))
                _icon = LoadIconW(IntPtr.Zero, new IntPtr(IdiApplication));
            Add();
        }
        catch (Exception e)
        {
            _log(e);
            return;
        }
        finally
        {
            _ready.Set();
        }

        while (GetMessageW(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }
    }

    private void Add()
    {
        var data = Data();
        data.Flags = NifMessage | NifIcon | NifTip;
        data.CallbackMessage = CallbackMessage;
        data.Icon = _icon;
        data.Tip = _tooltip;
        Shell_NotifyIconW(NimAdd, ref data);
    }

    private NotifyIconData Data() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(),
        Window = _window,
        Id = 1,
        Tip = "",
        Info = "",
        InfoTitle = "",
    };

    private IntPtr Handle(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (message == CallbackMessage)
            {
                var mouse = (uint)(lParam.ToInt64() & 0xFFFF);
                if (mouse == WmLButtonUp)
                    _open();
                else if (mouse is WmRButtonUp or WmContextMenu)
                    ShowMenu(window);
                return IntPtr.Zero;
            }
            if (message == _taskbarCreated && _taskbarCreated != 0)
            {
                // Explorer restarted and forgot every icon.
                Add();
                return IntPtr.Zero;
            }
            if (message == WmClose)
            {
                DestroyWindow(window);
                return IntPtr.Zero;
            }
            if (message == WmDestroy)
            {
                var data = Data();
                Shell_NotifyIconW(NimDelete, ref data);
                PostQuitMessage(0);
                return IntPtr.Zero;
            }
        }
        catch (Exception e)
        {
            _log(e);
        }
        return DefWindowProcW(window, message, wParam, lParam);
    }

    private void ShowMenu(IntPtr window)
    {
        var (open, quit) = _labels();
        var menu = CreatePopupMenu();
        try
        {
            AppendMenuW(menu, MfString, OpenCommand, open);
            AppendMenuW(menu, MfString, QuitCommand, quit);
            GetCursorPos(out var point);
            // Without this the menu does not close when the person clicks elsewhere.
            SetForegroundWindow(window);
            var chosen = TrackPopupMenu(menu, TpmReturnCmd | TpmRightButton, point.X, point.Y, 0, window, IntPtr.Zero);
            PostMessageW(window, WmNull, IntPtr.Zero, IntPtr.Zero);
            if (chosen == OpenCommand)
                _open();
            else if (chosen == QuitCommand)
                _quit();
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private delegate IntPtr WndProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public IntPtr Proc;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;
        public uint InfoFlags;
        public Guid Item;
        public IntPtr BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Window;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Point;
        public uint Private;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WndClassEx windowClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(
        uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out Msg message, IntPtr window, uint min, uint max);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref Msg message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref Msg message);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string name);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(IntPtr menu, uint flags, nint id, string text);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr window, IntPtr rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIconW(IntPtr instance, IntPtr name);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? name);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractIconW(IntPtr instance, string file, uint index);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(uint message, ref NotifyIconData data);
}

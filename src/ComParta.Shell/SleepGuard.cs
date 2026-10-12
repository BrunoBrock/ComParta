using System.Runtime.InteropServices;

namespace ComParta.Shell;

/// <summary>
/// Asks Windows not to sleep while files are moving (AD-14). The display may still turn off. It is
/// a power request, so <c>powercfg /requests</c> lists ComParta with its reason while it holds.
/// </summary>
public sealed class SleepGuard : IDisposable
{
    private const uint PowerRequestContextVersion = 0;
    private const uint PowerRequestContextSimpleString = 0x1;
    private const int PowerRequestSystemRequired = 1;

    private readonly object _lock = new();
    private IntPtr _request = IntPtr.Zero;
    private bool _held;

    /// <summary>Holds or releases the request. Does nothing on other systems or when Windows refuses.</summary>
    public void Hold(bool awake)
    {
        if (!OperatingSystem.IsWindows())
            return;
        lock (_lock)
        {
            if (awake == _held)
                return;
            if (_request == IntPtr.Zero)
            {
                var reason = new ReasonContext
                {
                    Version = PowerRequestContextVersion,
                    Flags = PowerRequestContextSimpleString,
                    SimpleReasonString = "ComParta is sending or receiving files",
                };
                _request = PowerCreateRequest(ref reason);
                if (_request == IntPtr.Zero || _request == new IntPtr(-1))
                {
                    _request = IntPtr.Zero;
                    return;
                }
            }
            var done = awake
                ? PowerSetRequest(_request, PowerRequestSystemRequired)
                : PowerClearRequest(_request, PowerRequestSystemRequired);
            if (done)
                _held = awake;
        }
    }

    public void Dispose()
    {
        Hold(false);
        lock (_lock)
        {
            if (_request != IntPtr.Zero)
                CloseHandle(_request);
            _request = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ReasonContext
    {
        public uint Version;
        public uint Flags;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string SimpleReasonString;
        // The rest of the union, unused with a simple string, so the size matches Windows' own.
        private IntPtr _detailed1;
        private IntPtr _detailed2;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr PowerCreateRequest(ref ReasonContext context);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerSetRequest(IntPtr request, int type);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerClearRequest(IntPtr request, int type);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}

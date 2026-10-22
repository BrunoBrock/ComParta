using ComParta.Core.Ports;
using ComParta.Shell;

namespace ComParta.App;

/// <summary>
/// The Windows side of staying present (AD-14): the notification-area icon, the sleep guard and
/// the start-up entry. Showing and hiding the window and quitting are the window's own business, so
/// they are handed in.
/// </summary>
public sealed class WindowsPresence : IBackgroundPresence, IDisposable
{
	private readonly string _executable;
	private readonly SleepGuard _sleep = new();
	private readonly Action _quit;
	private readonly Action _hide;
	private readonly Action<Exception> _log;
	private TrayIcon? _icon;

	public WindowsPresence(string executable, Action quit, Action hide, Action<Exception> log)
	{
		_executable = executable;
		_quit = quit;
		_hide = hide;
		_log = log;
	}

	/// <summary>Puts the icon in the notification area. It stays while the app runs.</summary>
	/// <param name="labels">The menu's words in the person's language.</param>
	/// <param name="open">Shows the window.</param>
	/// <param name="quit">The menu's Quit, which checks first whether files are moving.</param>
	public void ShowIcon(Func<(string Open, string Quit)> labels, Action open, Action quit) =>
		_icon = new TrayIcon("ComParta", _executable, labels, open, quit, _log);

	public void KeepAwake(bool awake) => _sleep.Hold(awake);

	public void StartWithSystem(bool on)
	{
		if (!StartupEntry.Set(_executable, on))
			_log(new InvalidOperationException($"Could not {(on ? "add" : "remove")} the start-up entry."));
	}

	public void Quit() => _quit();

	public void Hide() => _hide();

	public void Dispose()
	{
		_icon?.Dispose();
		_sleep.Dispose();
	}
}

using ComParta.Core.Ports;

namespace ComParta.App;

/// <summary>Whether the window is the one in front, from the window's own activation events.</summary>
public sealed class WindowFocus : IWindowState
{
	private volatile bool _active;
	private volatile bool _hidden;

	public bool IsInFront => _active && !_hidden;

	public void Watch(Window window)
	{
		// Minimising also deactivates the window.
		window.Activated += (_, _) => _active = true;
		window.Deactivated += (_, _) => _active = false;
		window.Stopped += (_, _) => _hidden = true;
		window.Resumed += (_, _) => _hidden = false;
	}
}

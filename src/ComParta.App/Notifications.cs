using ComParta.Core.Ports;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace ComParta.App;

/// <summary>
/// Windows app notifications (CAP-20, AD-11). A click hands the item back so the window can come
/// to the front with its details open.
/// </summary>
public sealed class WindowsNotifier : INotifier
{
	private readonly Action<Exception> _log;
	private bool _registered;

	public WindowsNotifier(Action<NotifiedItem, string, string> clicked, Action<Exception> log)
	{
		_log = log;
		try
		{
			var manager = AppNotificationManager.Default;
			manager.NotificationInvoked += (_, args) =>
			{
				var values = args.Arguments;
				if (values.TryGetValue("id", out var id) && values.TryGetValue("kind", out var kind))
					clicked(kind == "share" ? NotifiedItem.Share : NotifiedItem.Transfer, id, values.TryGetValue("name", out var name) ? name : "");
			};
			manager.Register();
			_registered = true;
		}
		catch (Exception e)
		{
			// Without notifications the app still works; the window shows everything.
			_log(e);
		}
	}

	public void Show(Notification notification)
	{
		if (!_registered)
			return;
		var shown = new AppNotificationBuilder()
			.AddArgument("kind", notification.Kind == NotifiedItem.Share ? "share" : "transfer")
			.AddArgument("id", notification.Id)
			.AddArgument("name", notification.Name)
			.AddText(notification.Title)
			.AddText(notification.Body)
			.BuildNotification();
		AppNotificationManager.Default.Show(shown);
	}

	/// <summary>Lets go of the registration before the process ends, as unpackaged apps should.</summary>
	public void Stop()
	{
		if (!_registered)
			return;
		try { AppNotificationManager.Default.Unregister(); }
		catch (Exception e) { _log(e); }
	}
}

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

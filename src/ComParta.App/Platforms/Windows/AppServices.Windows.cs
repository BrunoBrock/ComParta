using ComParta.Core.UseCases;
using ComParta.Shell;
using ComParta.Storage.Sqlite;
using Ports = ComParta.Core.Ports;

namespace ComParta.App;

/// <summary>The Windows adapters: the notification-area icon, the firewall, Explorer and Windows' thumbnails.</summary>
public sealed partial class AppServices
{
	private static readonly string Executable = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "ComParta.exe");
	private WindowsNotifier? _notifier;
	private WindowsPresence? _presence;

	private static partial Ports.IAppPaths CreatePaths() => new AppPaths();

	partial void BeforeStart()
	{
		// The first packaged run brings over what the pilot build kept (epic 5 story 5).
		if (Packaged.IsPackaged && AppPaths.IsUsualRoot)
		{
			try { PilotData.Import(AppPaths.PilotRoot(), AppPaths.DataRoot(), SqliteStore.CopyDatabase, Log); }
			catch (Exception e) { Log(e); }
		}
	}

	private partial PlatformParts CreatePlatform()
	{
		_notifier = new WindowsNotifier(OnNotificationClicked, Log);
		_presence = new WindowsPresence(Executable, () => QuitRequested?.Invoke(), () => HideWindowRequested?.Invoke(), Log);
		return new PlatformParts(new WindowsFirewall(Executable), new ReceivedFolderOpener(_paths), new WindowsThumbnails(), _notifier, _presence);
	}

	partial void AfterStart(NotificationUseCases notifications, BackgroundUseCases background)
	{
		// Written at every start so a clicked link finds the app even after its folder moved. The
		// package declares the link itself.
		if (!Packaged.IsPackaged)
			LinkScheme.Register(Executable);
		_presence!.ShowIcon(
			() => (notifications.Text("notify.menuOpen"), notifications.Text("notify.menuQuit")),
			() => ShowWindowRequested?.Invoke(),
			() =>
			{
				// Files moving: the window shows and asks the person first.
				if (!background.QuitFromOutside())
					ShowWindowRequested?.Invoke();
			});
	}

	partial void StopPlatform()
	{
		_presence?.Dispose();
		_notifier?.Stop();
	}
}

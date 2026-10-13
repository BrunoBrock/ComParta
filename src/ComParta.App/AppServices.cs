using ComParta.Contracts;
using ComParta.Core;
using ComParta.Core.UseCases;
using ComParta.Engine.MonoTorrent;
using ComParta.Shell;
using ComParta.Storage.Sqlite;

namespace ComParta.App;

/// <summary>The only place adapters are wired to the core's ports.</summary>
public sealed class AppServices
{
	private readonly AppPaths _paths = new();
	private MonoTorrentTransferEngine? _engine;
	private WindowsNotifier? _notifier;
	private WindowsPresence? _presence;
	private int _stopped;

	private AppServices()
	{
		Router = Task.Run(StartAsync);
	}

	/// <summary>Completes when the app is ready to answer commands.</summary>
	public Task<BridgeRouter> Router { get; }

	/// <summary>Invites handed to the app from outside, such as a clicked link (AD-16).</summary>
	public OpenedInvites Opened { get; } = new();
	public DroppedFiles Dropped { get; } = new();

	/// <summary>Whether the person is looking at the window; notifications wait for when they are not (CAP-20).</summary>
	public WindowFocus Focus { get; } = new();

	/// <summary>Raised when the person clicked a notification, so the window comes to the front.</summary>
	public event Action? NotificationClicked;

	/// <summary>Raised when the icon, or quitting from it, needs the window shown. Any thread.</summary>
	public event Action? ShowWindowRequested;

	/// <summary>Raised when the app should end now. Any thread.</summary>
	public event Action? QuitRequested;

	/// <summary>Whether there is work, and quitting, once the app has started (AD-14).</summary>
	public BackgroundUseCases? Background { get; private set; }

	public static AppServices Start() => new();

	private async Task<BridgeRouter> StartAsync()
	{
		try
		{
			Directory.CreateDirectory(_paths.ReceivedRoot);
			var store = await SqliteStore.OpenAsync(_paths.DatabasePath);
			_engine = new MonoTorrentTransferEngine(_paths);
			var shares = new ShareUseCases(store, _engine, new NativeFilePicker(), new LocalFileSystem());
			var transfers = new TransferUseCases(
				store, _engine, picker: new NativeFilePicker(), files: new LocalFileSystem(), storage: new LocalStorageInfo(_paths));
			var settings = new SettingsUseCases(store, new SystemInfo(), _engine);
			var executable = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "ComParta.exe");
			var connection = new ConnectionUseCases(_engine, new WindowsFirewall(executable));
			var activity = new ActivityUseCases(_engine, store);
			var opener = new ReceivedFolderOpener(_paths);
			var received = new ReceivedUseCases(transfers, _paths, opener, new LocalFileSystem());
			var thumbnails = new ThumbnailUseCases(shares, received, new WindowsThumbnails());
			var sending = new SendingUseCases(_engine, shares);
			NotificationUseCases? notifications = null;
			_notifier = new WindowsNotifier(
				(kind, id, name) =>
				{
					NotificationClicked?.Invoke();
					notifications?.Open(kind, id, name);
				},
				Log);
			notifications = new NotificationUseCases(_engine, transfers, shares, sending, settings, _notifier, Focus);
			_presence = new WindowsPresence(executable, () => QuitRequested?.Invoke(), Log);
			var background = new BackgroundUseCases(_engine, shares, transfers, sending, settings, store, notifications, _presence);
			var router = new BridgeRouter(
				shares, transfers, settings, connection, Opened, activity, received, new NativeClipboard(), opener, thumbnails, Dropped,
				sending, notifications, background);
			router.CommandFailed += (_, e) => Log(e);

			// Written at every start so a clicked link finds the app even after its folder moved.
			LinkScheme.Register(executable);

			await _engine.StartAsync(SharingPolicy.Default);
			// The public servers the person named reach the engine before any file is shared again.
			await settings.StartAsync();
			// A stop chosen before the app last closed holds before anything starts moving (AD-22).
			await activity.StartAsync();
			await shares.StartAsync();
			await transfers.StartAsync();
			await notifications.StartAsync();
			await background.StartAsync();
			_presence.ShowIcon(
				() => (notifications.Text("notify.menuOpen"), notifications.Text("notify.menuQuit")),
				() => ShowWindowRequested?.Invoke(),
				() =>
				{
					// Files moving: the window shows and asks the person first.
					if (!background.QuitFromOutside())
						ShowWindowRequested?.Invoke();
				});
			Background = background;
			return router;
		}
		catch (Exception e)
		{
			Log(e);
			throw;
		}
	}

	/// <summary>Saves where transfers got to and lets go of the icon. Safe to call more than once.</summary>
	public void Stop()
	{
		if (Interlocked.Exchange(ref _stopped, 1) == 1)
			return;
		try
		{
			_presence?.Dispose();
			// Lets the engine save where each transfer got to before the process ends.
			_engine?.StopAsync().Wait(TimeSpan.FromSeconds(5));
			_notifier?.Stop();
		}
		catch (Exception e)
		{
			Log(e);
		}
	}

	/// <summary>Local log file only; never file contents (conventions) and nothing is sent anywhere (AD-9).</summary>
	public void Log(Exception e)
	{
		try
		{
			Directory.CreateDirectory(_paths.LogsRoot);
			File.AppendAllText(Path.Combine(_paths.LogsRoot, "comparta.log"), $"{DateTimeOffset.UtcNow:O} {e}{Environment.NewLine}");
		}
		catch (Exception)
		{
			// Nothing sensible is left to do when even the log cannot be written.
		}
	}
}

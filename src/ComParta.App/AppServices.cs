using ComParta.Contracts;
using ComParta.Core;
using ComParta.Core.UseCases;
using ComParta.Engine.MonoTorrent;
using ComParta.Shell;
using ComParta.Storage.Sqlite;
using Ports = ComParta.Core.Ports;

namespace ComParta.App;

/// <summary>
/// The only place adapters are wired to the core's ports. What differs between Windows and Android
/// (AD-1) is in Platforms/&lt;system&gt;/AppServices.*.cs: the folders, and the adapters made by
/// <see cref="CreatePlatform"/>.
/// </summary>
public sealed partial class AppServices
{
	private readonly Ports.IAppPaths _paths = CreatePaths();
	private MonoTorrentTransferEngine? _engine;
	private NotificationUseCases? _notifications;
	private int _stopped;

	private AppServices()
	{
		Router = Task.Run(StartAsync);
	}

	/// <summary>Completes when the app is ready to answer commands.</summary>
	public Task<BridgeRouter> Router { get; }

	/// <summary>Invites handed to the app from outside, such as a clicked link or an opened download file (AD-16).</summary>
	public OpenedInvites Opened { get; } = new(MonoTorrentTransferEngine.FindLinkIn, MonoTorrentTransferEngine.DownloadFileExtensions);
	public DroppedFiles Dropped { get; } = new();

	/// <summary>Whether the person is looking at the window; notifications wait for when they are not (CAP-20).</summary>
	public WindowFocus Focus { get; } = new();

	/// <summary>Raised when the person clicked a notification, so the window comes to the front.</summary>
	public event Action? NotificationClicked;

	/// <summary>Raised when the icon, or quitting from it, needs the window shown. Any thread.</summary>
	public event Action? ShowWindowRequested;

	/// <summary>Raised when the app should end now. Any thread.</summary>
	public event Action? QuitRequested;

	/// <summary>Raised when the window should go to the notification area, as when it is closed with work. Any thread.</summary>
	public event Action? HideWindowRequested;

	/// <summary>Whether there is work, and quitting, once the app has started (AD-14).</summary>
	public BackgroundUseCases? Background { get; private set; }

	public static AppServices Start() => new();

	private async Task<BridgeRouter> StartAsync()
	{
		try
		{
			Directory.CreateDirectory(_paths.ReceivedRoot);
			BeforeStart();
			var platform = CreatePlatform();
			var store = await SqliteStore.OpenAsync(_paths.DatabasePath);
			// What is shared is read through the system's own content source (AD-20).
			_engine = platform.SharedContent is { } content
				? new MonoTorrentTransferEngine(_paths) { Content = content }
				: new MonoTorrentTransferEngine(_paths);
			var shares = new ShareUseCases(store, _engine, platform.SharePicker, platform.SharedFiles, paths: _paths);
			// Where received files go: the usual folder, or the one chosen in Settings (epic 12 story 2).
			var receivedFolder = new ReceivedFolder(store, _paths, new NativeFilePicker(), new LocalFileSystem());
			var transfers = new TransferUseCases(
				store, _engine, picker: new NativeFilePicker(), files: new LocalFileSystem(), storage: new LocalStorageInfo(_paths),
				received: receivedFolder, guard: platform.ReceivedGuard);
			var settings = new SettingsUseCases(store, new SystemInfo(), _engine, receivedFolder);
			var connection = new ConnectionUseCases(_engine, platform.Firewall);
			var activity = new ActivityUseCases(_engine, store);
			var opener = platform.Opener;
			var received = new ReceivedUseCases(transfers, _paths, opener, new LocalFileSystem());
			var thumbnails = new ThumbnailUseCases(shares, received, platform.Thumbnails);
			var sending = new SendingUseCases(_engine, shares);
			var notifications = _notifications = new NotificationUseCases(_engine, transfers, shares, sending, settings, platform.Notifier, Focus);
			var background = new BackgroundUseCases(_engine, shares, transfers, sending, settings, store, notifications, platform.Presence, activity: activity);
			var router = new BridgeRouter(
				shares, transfers, settings, connection, Opened, activity, received, new NativeClipboard(), opener, thumbnails, Dropped,
				sending, notifications, background, new MediaUseCases(shares, received, platform.SharedFiles),
				new SupportUseCases(store, new SystemInfo(), settings, shares, transfers, connection),
				new AboutUseCases(new AppPackageNotices()));
			router.CommandFailed += (_, e) => Log(e);

			await _engine.StartAsync(SharingPolicy.Default);
			// The public servers the person named reach the engine before any file is shared again.
			await settings.StartAsync();
			// A stop chosen before the app last closed holds before anything starts moving (AD-22).
			await activity.StartAsync();
			await shares.StartAsync();
			await transfers.StartAsync();
			await notifications.StartAsync();
			await background.StartAsync();
			AfterStart(notifications, background);
			Background = background;
			return router;
		}
		catch (Exception e)
		{
			Log(e);
			throw;
		}
	}

	/// <summary>A notification the person tapped: the window comes to the front with that item's details.</summary>
	private void OnNotificationClicked(Ports.NotifiedItem kind, string id, string name)
	{
		NotificationClicked?.Invoke();
		_notifications?.Open(kind, id, name);
	}

	/// <summary>The folders for this system (AD-10).</summary>
	private static partial Ports.IAppPaths CreatePaths();

	/// <summary>Runs before the database opens.</summary>
	partial void BeforeStart();

	/// <summary>The adapters that differ between systems.</summary>
	private partial PlatformParts CreatePlatform();

	/// <summary>Runs once everything has started.</summary>
	partial void AfterStart(NotificationUseCases notifications, BackgroundUseCases background);

	/// <summary>Lets go of what the system adapters hold.</summary>
	partial void StopPlatform();

	/// <summary>True once <see cref="Stop"/> ran.</summary>
	public bool Stopped => Volatile.Read(ref _stopped) == 1;

	/// <summary>Saves where transfers got to and lets go of the icon. Safe to call more than once.</summary>
	public void Stop()
	{
		if (Interlocked.Exchange(ref _stopped, 1) == 1)
			return;
		try
		{
			StopPlatform();
			// Lets the engine save where each transfer got to before the process ends.
			_engine?.StopAsync().Wait(TimeSpan.FromSeconds(5));
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

/// <summary>The adapters that differ between Windows and Android (AD-1).</summary>
internal sealed record PlatformParts(
	Ports.IFirewall Firewall,
	Ports.IContentOpener Opener,
	Ports.IThumbnailProvider Thumbnails,
	Ports.INotifier Notifier,
	Ports.IBackgroundPresence Presence,
	Ports.IFilePicker SharePicker,
	Ports.IFileSystem SharedFiles,
	Ports.IContentSource? SharedContent,
	Ports.IReceivedFileGuard ReceivedGuard);

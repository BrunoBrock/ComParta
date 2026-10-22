using Ports = ComParta.Core.Ports;

namespace ComParta.App;

/// <summary>
/// The app's own folders on the phone (AD-10). Received files stay inside the app until epic 7
/// story 3 publishes them to Downloads.
/// </summary>
public sealed class AndroidPaths : Ports.IAppPaths
{
	private readonly string _root = FileSystem.AppDataDirectory;

	public string DatabasePath => Path.Combine(_root, "comparta.db");
	public string EngineRoot => Path.Combine(_root, "engine");
	public string ReceivedRoot => Path.Combine(_root, "Received");
	public string LogsRoot => Path.Combine(_root, "logs");
	public string SavedRoot => Path.Combine(FileSystem.CacheDirectory, "Saved");
}

/// <summary>A phone has no firewall the person manages, so nothing blocks others here.</summary>
public sealed class AndroidFirewall : Ports.IFirewall
{
	public bool? AllowsIncoming() => true;

	public Task OpenSettingsAsync(CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>
/// Opens a received file with the app the phone uses for it. Showing the folder needs the Files app
/// and Downloads (epic 7 story 3), so until then those do nothing.
/// </summary>
public sealed class AndroidOpener : Ports.IContentOpener
{
	public Task OpenReceivedFolderAsync(string? folder, CancellationToken ct = default) => Task.CompletedTask;

	public async Task<bool> OpenAsync(string path, CancellationToken ct = default)
	{
		try
		{
			return await MainThread.InvokeOnMainThreadAsync(() =>
				Launcher.Default.OpenAsync(new OpenFileRequest { File = new ReadOnlyFile(path) }));
		}
		catch (Exception)
		{
			return false;
		}
	}

	public Task RevealAsync(string path, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>No pictures yet: cards show their file icons (AD-18 on Android comes later).</summary>
public sealed class NoThumbnails : Ports.IThumbnailProvider
{
	public Task<Ports.Thumbnail?> GetAsync(string path, int size, CancellationToken ct = default) =>
		Task.FromResult<Ports.Thumbnail?>(null);
}

/// <summary>Phone notifications arrive with the background service (epic 7 story 7).</summary>
public sealed class NoNotifications : Ports.INotifier
{
	public void Show(Ports.Notification notification)
	{
	}
}

/// <summary>
/// Staying present on a phone (AD-14). The foreground service that keeps transfers going with the
/// screen off is epic 7 story 7; until then leaving puts the app behind the others, as the home
/// button does, and quitting ends it.
/// </summary>
public sealed class AndroidPresence(Action stop) : Ports.IBackgroundPresence
{
	public void KeepAwake(bool awake)
	{
	}

	public void StartWithSystem(bool on)
	{
	}

	public void Quit() => MainThread.BeginInvokeOnMainThread(() =>
	{
		stop();
		Platform.CurrentActivity?.FinishAndRemoveTask();
	});

	public void Hide() => MainThread.BeginInvokeOnMainThread(() => Platform.CurrentActivity?.MoveTaskToBack(true));
}

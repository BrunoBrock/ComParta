using Android.App;
using Android.Content;
using Android.Media;
using Android.Provider;
using Android.Webkit;
using Ports = ComParta.Core.Ports;
using AndroidUri = Android.Net.Uri;

namespace ComParta.App;

/// <summary>
/// The app's own folders on the phone (AD-10). What arrives goes to Downloads/ComParta (epic 7
/// story 3), where the Gallery and the Files app find it and it stays when the app is removed;
/// unfinished data waits in a hidden folder there, which the phone does not list. Before Android 11
/// that needs a permission to write to shared storage, so there it stays inside the app.
/// </summary>
public sealed class AndroidPaths : Ports.IAppPaths
{
	private readonly string _root = FileSystem.AppDataDirectory;

	public string DatabasePath => Path.Combine(_root, "comparta.db");
	public string EngineRoot => Path.Combine(_root, "engine");
	public string ReceivedRoot { get; } = OperatingSystem.IsAndroidVersionAtLeast(30) ? DownloadsFolder : Path.Combine(FileSystem.AppDataDirectory, "Received");
	public string LogsRoot => Path.Combine(_root, "logs");
	public string SavedRoot => Path.Combine(FileSystem.CacheDirectory, "Saved");

	/// <summary>Downloads/ComParta on the phone's own storage.</summary>
	public static string DownloadsFolder =>
		Path.Combine(Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDownloads)!.AbsolutePath, "ComParta");
}

/// <summary>A phone has no firewall the person manages, so nothing blocks others here.</summary>
public sealed class AndroidFirewall : Ports.IFirewall
{
	public bool? AllowsIncoming() => true;

	public Task OpenSettingsAsync(CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>
/// Opens a received file with the app the phone uses for its kind (epic 7 story 3), and shows the
/// Downloads/ComParta folder in the Files app. A file in Downloads is handed over by the phone's
/// own reference to it, so nothing is copied; one inside the app goes through MAUI's launcher.
/// </summary>
public sealed class AndroidOpener : Ports.IContentOpener
{
	public Task OpenReceivedFolderAsync(string? folder, CancellationToken ct = default) => ShowFolderAsync();

	public async Task<bool> OpenAsync(string path, CancellationToken ct = default)
	{
		try
		{
			if (await PhoneReferenceAsync(path) is not { } uri)
				return await MainThread.InvokeOnMainThreadAsync(() =>
					Launcher.Default.OpenAsync(new OpenFileRequest { File = new ReadOnlyFile(path) }));
			var intent = new Intent(Intent.ActionView);
			intent.SetDataAndType(uri, KindOf(path));
			intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.GrantReadUriPermission);
			await MainThread.InvokeOnMainThreadAsync(() => Android.App.Application.Context.StartActivity(intent));
			return true;
		}
		catch (Exception)
		{
			// No app on the phone opens this kind of file.
			return false;
		}
	}

	/// <summary>A phone has no way to point at one file in a folder; the folder it is in is shown.</summary>
	public Task RevealAsync(string path, CancellationToken ct = default) => ShowFolderAsync();

	/// <summary>The Files app at Downloads/ComParta, or the phone's list of downloads where it cannot go there.</summary>
	private static Task ShowFolderAsync() => MainThread.InvokeOnMainThreadAsync(() =>
	{
		var context = Android.App.Application.Context;
		try
		{
			var folder = DocumentsContract.BuildDocumentUri("com.android.externalstorage.documents", "primary:Download/ComParta");
			var intent = new Intent(Intent.ActionView);
			intent.SetDataAndType(folder, DocumentsContract.Document.MimeTypeDir);
			intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.GrantReadUriPermission);
			context.StartActivity(intent);
		}
		catch (ActivityNotFoundException)
		{
			try { context.StartActivity(new Intent(DownloadManager.ActionViewDownloads).AddFlags(ActivityFlags.NewTask)); }
			catch (ActivityNotFoundException) { /* nothing on this phone lists files */ }
		}
	});

	/// <summary>
	/// The phone's own reference to a file in shared storage, which also makes sure the Gallery and
	/// the Files app list it; null for a file inside the app.
	/// </summary>
	private static Task<AndroidUri?> PhoneReferenceAsync(string path)
	{
		var shared = Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath;
		if (shared is null || !path.StartsWith(shared + "/", StringComparison.Ordinal))
			return Task.FromResult<AndroidUri?>(null);
		var found = new Scanned();
		MediaScannerConnection.ScanFile(Android.App.Application.Context, [path], [KindOf(path)], found);
		return found.Uri.Task.WaitAsync(TimeSpan.FromSeconds(10));
	}

	private static string KindOf(string path) =>
		MimeTypeMap.Singleton?.GetMimeTypeFromExtension(Path.GetExtension(path).TrimStart('.').ToLowerInvariant()) ?? "application/octet-stream";

	private sealed class Scanned : Java.Lang.Object, MediaScannerConnection.IOnScanCompletedListener
	{
		public TaskCompletionSource<AndroidUri?> Uri { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public void OnScanCompleted(string? path, AndroidUri? uri) => Uri.TrySetResult(uri);
	}
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

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

	/// <summary>
	/// The Files app at Downloads/ComParta, or the phone's list of downloads where it cannot go there.
	/// The folder is handed over without lending a read permission: the app holds none to lend for
	/// the phone's own storage (the phone refuses the whole request when it is asked to), and the
	/// Files app needs none.
	/// </summary>
	private static Task ShowFolderAsync() => MainThread.InvokeOnMainThreadAsync(() =>
	{
		var context = Android.App.Application.Context;
		try
		{
			var folder = DocumentsContract.BuildDocumentUri("com.android.externalstorage.documents", "primary:Download/ComParta");
			var intent = new Intent(Intent.ActionView);
			intent.SetDataAndType(folder, DocumentsContract.Document.MimeTypeDir);
			intent.AddFlags(ActivityFlags.NewTask);
			context.StartActivity(intent);
		}
		catch (Java.Lang.Exception first)
		{
			Android.Util.Log.Warn("ComParta", $"The Files app did not open at the folder ({first.GetType().Name}); showing the downloads list.");
			try { context.StartActivity(new Intent(DownloadManager.ActionViewDownloads).AddFlags(ActivityFlags.NewTask)); }
			catch (Java.Lang.Exception) { /* nothing on this phone lists files */ }
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
/// Staying present on a phone (AD-14). While bytes move, a foreground service keeps the app working
/// with the screen off, shown as a notification with how far the files are and a Stop button (epic 7
/// story 7). Core decides what it says and when it starts and stops; this only shows it. Leaving
/// puts the app behind the others, as the home button does, and quitting ends it.
/// </summary>
public sealed class AndroidPresence : Ports.IBackgroundPresence
{
	private const string AskedKey = "notifications.asked";
	private const int AskRequest = 7701;

	private readonly Action _stop;
	private readonly Func<ComParta.Core.UseCases.BackgroundUseCases?> _background;

	public AndroidPresence(Action stop, Func<ComParta.Core.UseCases.BackgroundUseCases?> background)
	{
		_stop = stop;
		_background = background;
		Current = this;
	}

	/// <summary>The running app's presence, for the service and the activity to reach core.</summary>
	public static AndroidPresence? Current { get; private set; }

	public void KeepAwake(bool awake)
	{
	}

	public void StartWithSystem(bool on)
	{
	}

	public void Quit() => MainThread.BeginInvokeOnMainThread(() =>
	{
		MovingService.Hide(Android.App.Application.Context);
		_stop();
		Platform.CurrentActivity?.FinishAndRemoveTask();
	});

	public void Hide() => MainThread.BeginInvokeOnMainThread(() => Platform.CurrentActivity?.MoveTaskToBack(true));

	public void ShowMoving(Ports.MovingNotice? notice) => MainThread.BeginInvokeOnMainThread(() =>
	{
		var context = Android.App.Application.Context;
		try
		{
			if (notice is null)
			{
				MovingService.Hide(context);
				MovingService.HidePaused(context);
			}
			else if (notice.Paused)
			{
				MovingService.Hide(context);
				MovingService.ShowPaused(context, notice);
			}
			else
			{
				AskOnce(notice);
				MovingService.Show(context, notice);
			}
		}
		catch (Exception e)
		{
			// The phone refused the service, as it does for an app out of sight on Android 12 and later;
			// the transfer goes on while the screen is on, and the next change tries again.
			Android.Util.Log.Warn("ComParta", $"Could not keep going with the screen off: {e.GetType().Name}");
		}
	});

	/// <summary>The Stop button on the notification.</summary>
	internal void StopFromNotice() => Run(background => background.StopFromNoticeAsync());

	/// <summary>The phone allowed no more time with the screen off.</summary>
	internal void TimedOut() => Run(background => background.TimedOutAsync());

	/// <summary>The person opened the app: what was paused for lack of time continues.</summary>
	public void AppOpened() => Run(background => background.AppOpenedAsync());

	private void Run(Func<ComParta.Core.UseCases.BackgroundUseCases, Task> step)
	{
		if (_background() is not { } background)
			return;
		_ = Task.Run(async () =>
		{
			try { await step(background); }
			catch (Exception e) { Android.Util.Log.Warn("ComParta", e.GetType().Name); }
		});
	}

	/// <summary>
	/// The first time files move, says in one sentence why ComParta wants to show a notification, then
	/// lets the phone ask (Android 13 and later). Asked once; the person can change it in the phone's settings.
	/// </summary>
	private static void AskOnce(Ports.MovingNotice notice)
	{
		if (!OperatingSystem.IsAndroidVersionAtLeast(33) || Platform.CurrentActivity is not { } activity)
			return;
		if (AndroidX.Core.Content.ContextCompat.CheckSelfPermission(activity, Android.Manifest.Permission.PostNotifications)
				== Android.Content.PM.Permission.Granted
			|| Preferences.Default.Get(AskedKey, false))
			return;
		Preferences.Default.Set(AskedKey, true);
		new AndroidX.AppCompat.App.AlertDialog.Builder(activity)
			.SetMessage(notice.Why)!
			.SetCancelable(false)!
			.SetPositiveButton(notice.WhyOk, (_, _) =>
				AndroidX.Core.App.ActivityCompat.RequestPermissions(activity, [Android.Manifest.Permission.PostNotifications], AskRequest))!
			.Show();
	}
}

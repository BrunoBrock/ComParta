using System.Diagnostics;
using Ports = ComParta.Core.Ports;

namespace ComParta.App;

/// <summary>
/// Per-user folders. Setting COMPARTA_HOME moves everything, Received included, under one folder,
/// which lets two copies run side by side on one PC for testing.
/// </summary>
public sealed class AppPaths : Ports.IAppPaths
{
	private readonly string _root;

	public AppPaths()
	{
		_root = DataRoot();
		ReceivedRoot = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("COMPARTA_HOME"))
			? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "ComParta")
			: Path.Combine(_root, "Received");
	}

	/// <summary>
	/// The folder that holds the database and engine data, and names the running copy (AD-14). The
	/// packaged install keeps its own in the package's folder, which Windows removes with it.
	/// </summary>
	public static string DataRoot()
	{
		var home = Environment.GetEnvironmentVariable("COMPARTA_HOME");
		if (!string.IsNullOrWhiteSpace(home))
			return Path.GetFullPath(home);
		return Packaged.IsPackaged
			? Path.Combine(global::Windows.Storage.ApplicationData.Current.LocalFolder.Path, "ComParta")
			: PilotRoot();
	}

	/// <summary>Where the unpackaged build keeps its data; the packaged install imports it once (epic 5 story 5).</summary>
	public static string PilotRoot() =>
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ComParta");

	/// <summary>True when the data folder is the usual one, not one COMPARTA_HOME chose for testing.</summary>
	public static bool IsUsualRoot => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("COMPARTA_HOME"));

	public string DatabasePath => Path.Combine(_root, "comparta.db");
	public string EngineRoot => Path.Combine(_root, "engine");
	public string ReceivedRoot { get; }
	public string LogsRoot => Path.Combine(_root, "logs");
	public string SavedRoot => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("COMPARTA_HOME"))
		? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
		: Path.Combine(_root, "Saved");
}

public sealed class NativeFilePicker : Ports.IFilePicker
{
	public async Task<string?> PickFileAsync(CancellationToken ct = default)
	{
		var picked = await MainThread.InvokeOnMainThreadAsync(() => FilePicker.Default.PickAsync());
		return picked?.FullPath;
	}

	public async Task<string?> PickDownloadFileAsync(CancellationToken ct = default)
	{
		// The engine adapter names the extensions; the shell only shows them (AD-2, AD-21).
		var types = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
		{
			[DevicePlatform.WinUI] = Engine.MonoTorrent.MonoTorrentTransferEngine.DownloadFileExtensions,
		});
		var picked = await MainThread.InvokeOnMainThreadAsync(() => FilePicker.Default.PickAsync(new PickOptions { FileTypes = types }));
		return picked?.FullPath;
	}

	public async Task<IReadOnlyList<string>> PickFilesAsync(CancellationToken ct = default)
	{
		var picked = await MainThread.InvokeOnMainThreadAsync(() => FilePicker.Default.PickMultipleAsync());
		return [.. (picked ?? []).Where(file => file is not null).Select(file => file!.FullPath)];
	}

	/// <summary>Windows' own folder dialog, on top of ComParta's window.</summary>
	public Task<string?> PickFolderAsync(CancellationToken ct = default) =>
		MainThread.InvokeOnMainThreadAsync(async () =>
		{
			var picker = new Windows.Storage.Pickers.FolderPicker
			{
				SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
			};
			// Every file type, or Windows shows the folder dialog with nothing to choose.
			picker.FileTypeFilter.Add("*");
			if (Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is Microsoft.UI.Xaml.Window window)
				WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
			var folder = await picker.PickSingleFolderAsync();
			return folder?.Path;
		});
}

public sealed class NativeClipboard : Ports.IClipboard
{
	/// <summary>
	/// Windows refuses the clipboard while another program has it open, which clipboard managers and
	/// remote desktop do for a moment now and then; Copy invite then failed. It is asked again a few times.
	/// </summary>
	public async Task SetTextAsync(string text, CancellationToken ct = default)
	{
		for (var attempt = 1; ; attempt++)
		{
			try
			{
				await MainThread.InvokeOnMainThreadAsync(() => Clipboard.Default.SetTextAsync(text));
				return;
			}
			catch (System.Runtime.InteropServices.COMException) when (attempt < 5)
			{
				await Task.Delay(100 * attempt, ct);
			}
		}
	}

	public Task<string?> GetTextAsync(CancellationToken ct = default) =>
		MainThread.InvokeOnMainThreadAsync(() => Clipboard.Default.HasText ? Clipboard.Default.GetTextAsync() : Task.FromResult<string?>(null));
}

public sealed class LocalStorageInfo(Ports.IAppPaths paths) : Ports.IStorageInfo
{
	public long? GetReceivedFreeBytes()
	{
		try
		{
			Directory.CreateDirectory(paths.ReceivedRoot);
			var root = Path.GetPathRoot(Path.GetFullPath(paths.ReceivedRoot));
			return root is null ? null : new DriveInfo(root).AvailableFreeSpace;
		}
		catch (Exception)
		{
			return null;
		}
	}
}

public sealed class ReceivedFolderOpener(Ports.IAppPaths paths) : Ports.IContentOpener
{
	public Task OpenReceivedFolderAsync(CancellationToken ct = default)
	{
		Directory.CreateDirectory(paths.ReceivedRoot);
		// Shows the folder; nothing received is ever run by the app.
		Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { paths.ReceivedRoot } })?.Dispose();
		return Task.CompletedTask;
	}

	public Task<bool> OpenAsync(string path, CancellationToken ct = default)
	{
		// Core has already refused programs; this opens a document with its usual app.
		try
		{
			Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
			return Task.FromResult(true);
		}
		catch (Exception)
		{
			return Task.FromResult(false);
		}
	}

	public Task RevealAsync(string path, CancellationToken ct = default)
	{
		Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false })?.Dispose();
		return Task.CompletedTask;
	}
}

/// <summary>
/// Thumbnails drawn by Windows itself, from its own thumbnail cache (AD-18). Only real pictures
/// come back; when Windows would show an icon, the UI shows its own file icon instead.
/// </summary>
public sealed class WindowsThumbnails : Ports.IThumbnailProvider
{
	public async Task<Ports.Thumbnail?> GetAsync(string path, int size, CancellationToken ct = default)
	{
		var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path).AsTask(ct);
		using var thumbnail = await file.GetThumbnailAsync(
			Windows.Storage.FileProperties.ThumbnailMode.SingleItem,
			(uint)size,
			Windows.Storage.FileProperties.ThumbnailOptions.ResizeThumbnail).AsTask(ct);
		if (thumbnail is null || thumbnail.Type != Windows.Storage.FileProperties.ThumbnailType.Image)
			return null;
		using var picture = thumbnail.AsStreamForRead();
		using var copy = new MemoryStream();
		await picture.CopyToAsync(copy, ct);
		var type = string.IsNullOrEmpty(thumbnail.ContentType) ? "image/jpeg" : thumbnail.ContentType;
		return new Ports.Thumbnail(type, copy.ToArray());
	}
}

public sealed class SystemInfo : Ports.ISystemInfo
{
	public string UiLanguage => System.Globalization.CultureInfo.CurrentUICulture.Name is { Length: > 0 } name ? name : "en";

	public string AppVersion => AppInfo.Current.VersionString;

	public string SystemVersion => System.Runtime.InteropServices.RuntimeInformation.OSDescription;
}

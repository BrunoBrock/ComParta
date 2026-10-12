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

	/// <summary>The folder that holds the database and engine data, and names the running copy (AD-14).</summary>
	public static string DataRoot()
	{
		var home = Environment.GetEnvironmentVariable("COMPARTA_HOME");
		return string.IsNullOrWhiteSpace(home)
			? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ComParta")
			: Path.GetFullPath(home);
	}

	public string DatabasePath => Path.Combine(_root, "comparta.db");
	public string EngineRoot => Path.Combine(_root, "engine");
	public string ReceivedRoot { get; }
	public string LogsRoot => Path.Combine(_root, "logs");
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
}

public sealed class NativeClipboard : Ports.IClipboard
{
	public Task SetTextAsync(string text, CancellationToken ct = default) =>
		MainThread.InvokeOnMainThreadAsync(() => Clipboard.Default.SetTextAsync(text));

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

public sealed class LocalFileSystem : Ports.IFileSystem
{
	public bool FileExists(string path) => File.Exists(path);

	public long GetReadableFileSize(string path)
	{
		using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		return stream.Length;
	}

	public void DeleteFile(string path) => File.Delete(path);

	public byte[] ReadSmallFile(string path, int maxBytes)
	{
		using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		if (stream.Length > maxBytes)
			throw new IOException("The file is larger than allowed.");
		var content = new byte[stream.Length];
		stream.ReadExactly(content);
		return content;
	}
}

public sealed class SystemInfo : Ports.ISystemInfo
{
	public string UiLanguage => System.Globalization.CultureInfo.CurrentUICulture.Name is { Length: > 0 } name ? name : "en";
}

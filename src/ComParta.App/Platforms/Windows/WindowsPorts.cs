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

public sealed class ReceivedFolderOpener(Ports.IAppPaths paths) : Ports.IContentOpener
{
	public Task OpenReceivedFolderAsync(string? folder, CancellationToken ct = default)
	{
		folder ??= paths.ReceivedRoot;
		Directory.CreateDirectory(folder);
		// Shows the folder; nothing received is ever run by the app.
		Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { folder } })?.Dispose();
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

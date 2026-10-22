using Ports = ComParta.Core.Ports;

namespace ComParta.App;

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
			// Android's picker filters by type, not by name; download files often come as plain bytes.
			[DevicePlatform.Android] = ["application/x-bittorrent", "application/octet-stream"],
		});
		var picked = await MainThread.InvokeOnMainThreadAsync(() => FilePicker.Default.PickAsync(new PickOptions { FileTypes = types }));
		return picked?.FullPath;
	}

	public async Task<IReadOnlyList<string>> PickFilesAsync(CancellationToken ct = default)
	{
		var picked = await MainThread.InvokeOnMainThreadAsync(() => FilePicker.Default.PickMultipleAsync());
		return [.. (picked ?? []).Where(file => file is not null).Select(file => file!.FullPath)];
	}

#if WINDOWS
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
#else
	/// <summary>A phone picks a folder through the system's document tree, which epic 7 story 2 adds.</summary>
	public Task<string?> PickFolderAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);
#endif
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
	public long? GetReceivedFreeBytes(string? folder)
	{
		try
		{
			// A chosen folder on a drive that was unplugged is not made again on another one.
			if (folder is null)
				Directory.CreateDirectory(paths.ReceivedRoot);
			else if (!Directory.Exists(folder))
				return null;
#if ANDROID
			// A phone's storage is one volume under the app; DriveInfo would read the system's own.
			return new Android.OS.StatFs(folder ?? paths.ReceivedRoot).AvailableBytes;
#else
			var root = Path.GetPathRoot(Path.GetFullPath(folder ?? paths.ReceivedRoot));
			return root is null ? null : new DriveInfo(root).AvailableFreeSpace;
#endif
		}
		catch (Exception)
		{
			return null;
		}
	}
}

public sealed class SystemInfo : Ports.ISystemInfo
{
	public string UiLanguage => System.Globalization.CultureInfo.CurrentUICulture.Name is { Length: > 0 } name ? name : "en";

	public string AppVersion => AppInfo.Current.VersionString;

	public string SystemVersion => System.Runtime.InteropServices.RuntimeInformation.OSDescription;
}

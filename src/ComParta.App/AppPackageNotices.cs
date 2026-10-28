using Ports = ComParta.Core.Ports;

namespace ComParta.App;

/// <summary>
/// Reads the notices file shipped inside the app (a MauiAsset: the output folder and MSIX on
/// Windows, the APK's assets on Android), so one adapter serves both systems (epic 5 story 1).
/// </summary>
public sealed class AppPackageNotices : Ports.IThirdPartyNotices
{
	public async Task<string?> ReadAsync(CancellationToken ct = default)
	{
		try
		{
			await using var stream = await FileSystem.Current.OpenAppPackageFileAsync(Ports.NoticesFile.Name);
			using var reader = new StreamReader(stream);
			return await reader.ReadToEndAsync(ct);
		}
		catch (IOException)
		{
			// Missing from this build: the router answers with a plain problem instead.
			return null;
		}
#if ANDROID
		catch (Java.IO.IOException)
		{
			return null;
		}
#endif
	}
}

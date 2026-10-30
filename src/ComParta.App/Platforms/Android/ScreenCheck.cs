namespace ComParta.App;

/// <summary>
/// Lets the Android check in CI (.github/scripts/check-android.sh) reach the screen over the web
/// view's debugging socket, so it can drive the real app as the Windows release check does. Only
/// for a test copy: the check puts a marker in the app's own folder on shared storage, which on
/// Android 11 and later (the only ones it is open on) nothing but the app itself and a computer connected over adb can write, and
/// the socket answers only adb.
/// </summary>
internal static class ScreenCheck
{
	public const string Marker = "screen-check";

	public static void OpenIfAsked()
	{
		try
		{
			if (!OperatingSystem.IsAndroidVersionAtLeast(30))
				return;
			var folder = Android.App.Application.Context.GetExternalFilesDir(null)?.AbsolutePath;
			if (folder is null || !File.Exists(Path.Combine(folder, Marker)))
				return;
			Android.Webkit.WebView.SetWebContentsDebuggingEnabled(true);
			Android.Util.Log.Info("ComParta", "Screen check open");
		}
		catch (Exception)
		{
			// No shared storage now: an ordinary start.
		}
	}
}

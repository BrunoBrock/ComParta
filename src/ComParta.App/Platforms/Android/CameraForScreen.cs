using Android.Webkit;

namespace ComParta.App;

/// <summary>
/// Lets the screen use the phone's camera for "Scan a QR code" in Receive (epic 7 story 9), and
/// nothing else. The web view asks when the screen starts the camera, which happens only after the
/// person tapped Scan; the phone's own question about the camera comes then, the first time, and
/// never at start-up. Only the app's own page may have it, only the camera (never the microphone),
/// and a refusal leaves paste and the download file as they are.
/// </summary>
internal sealed class CameraForScreen(Android.Webkit.WebView web, Action<Exception> log) : WebChromeClient
{
	public static void Attach(HybridWebView webView, Action<Exception> log)
	{
		webView.HandlerChanged += (_, _) =>
		{
			if (webView.Handler?.PlatformView is Android.Webkit.WebView native)
				native.SetWebChromeClient(new CameraForScreen(native, log));
		};
	}

	public override void OnPermissionRequest(PermissionRequest? request)
	{
		if (request is null)
			return;
		var asked = request.GetResources() ?? [];
		if (asked.Length == 0 || asked.Any(resource => resource != PermissionRequest.ResourceVideoCapture) || !IsOwnPage(request.Origin))
		{
			Android.Util.Log.Info("ComParta", "Camera kept from the page");
			request.Deny();
			return;
		}

		MainThread.BeginInvokeOnMainThread(async () =>
		{
			try
			{
				var answer = await Permissions.RequestAsync<Permissions.Camera>();
				// Read by the Android check in CI.
				Android.Util.Log.Info("ComParta", answer == PermissionStatus.Granted ? "Camera allowed" : "Camera refused");
				if (answer == PermissionStatus.Granted)
					request.Grant(asked);
				else
					request.Deny();
			}
			catch (Exception error)
			{
				log(error);
				request.Deny();
			}
		});
	}

	/// <summary>The page asking is the one the app loaded, not anything it might link to.</summary>
	private bool IsOwnPage(Android.Net.Uri? origin) =>
		origin?.Host is { Length: > 0 } host
		&& Android.Net.Uri.Parse(web.Url ?? "")?.Host is { } page
		&& string.Equals(host, page, StringComparison.OrdinalIgnoreCase);
}

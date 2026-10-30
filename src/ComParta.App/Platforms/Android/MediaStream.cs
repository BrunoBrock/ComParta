using Android.Webkit;
using ComParta.Contracts;

namespace ComParta.App;

/// <summary>
/// Serves opened pictures, songs and videos to the phone's web view (epic 7 story 12, AD-26), as
/// the Windows one does. The web view hands every request for <see cref="MediaAddress.Root"/> here
/// before any network; core decides what the address may read and which part of the file, so a
/// player can seek without the screen ever naming a path or a phone reference.
/// </summary>
internal static class MediaStream
{
	public static void Attach(HybridWebView webView, Task<BridgeRouter> router, Action<Exception> log)
	{
		webView.WebResourceRequested += (_, e) =>
		{
			if (MediaAddress.TokenOf(e.Uri.OriginalString) is null || e.PlatformArgs is not { } platform)
				return;
			e.Handled = true;
			platform.Response = Answer(e, router, log);
		};
		webView.HandlerChanged += (_, _) =>
		{
			// A song or video starts as soon as the viewer opens, as on Windows; the tap that opened it
			// was the person's own.
			if (webView.Handler?.PlatformView is Android.Webkit.WebView native)
				native.Settings.MediaPlaybackRequiresUserGesture = false;
		};
	}

	/// <summary>Asked on the web view's own thread, which waits for the answer, never on the screen's.</summary>
	private static WebResourceResponse Answer(Microsoft.Maui.Controls.WebViewWebResourceRequestedEventArgs e, Task<BridgeRouter> router, Action<Exception> log)
	{
		try
		{
			var range = e.Headers.TryGetValue("Range", out var asked) ? asked : null;
			var reply = router.GetAwaiter().GetResult().ServeMedia(e.Uri.OriginalString, range);
			var headers = new Dictionary<string, string>
			{
				["Accept-Ranges"] = "bytes",
				["Cache-Control"] = "no-store",
				["Content-Length"] = reply.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
			};
			if (reply.ContentRange is { } part)
				headers["Content-Range"] = part;
			// The Android check in CI reads this: what the web view asked for and what it got.
			Android.Util.Log.Info("ComParta", $"Media answered: {reply.Status}{(range is null ? "" : " for a part")}");
			return new WebResourceResponse(reply.ContentType, null, reply.Status, Reason(reply.Status), headers, reply.Body);
		}
		catch (Exception error)
		{
			log(error);
			return new WebResourceResponse(null, null, 500, "Internal Server Error", null, null);
		}
	}

	private static string Reason(int status) => status switch
	{
		200 => "OK",
		206 => "Partial Content",
		416 => "Range Not Satisfiable",
		_ => "Not Found",
	};
}

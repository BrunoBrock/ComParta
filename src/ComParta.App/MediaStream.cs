using ComParta.Contracts;
using Microsoft.Web.WebView2.Core;

namespace ComParta.App;

/// <summary>
/// Serves opened pictures, songs and videos to the web view (AD-26). WebView2 hands every request
/// for <see cref="MediaAddress.Root"/> here before any network; core decides what the address may
/// read and which part of the file, so a player can seek without the UI ever naming a path.
/// </summary>
internal static class MediaStream
{
	public static void Attach(HybridWebView webView, Task<BridgeRouter> router, Action<Exception> log)
	{
		if (webView.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.WebView2 native)
			return;
		if (native.CoreWebView2 is { } core)
			Listen(core, router, log);
		else
			native.CoreWebView2Initialized += (sender, _) =>
			{
				if (sender.CoreWebView2 is { } ready)
					Listen(ready, router, log);
			};
	}

	private static void Listen(CoreWebView2 core, Task<BridgeRouter> router, Action<Exception> log)
	{
		core.AddWebResourceRequestedFilter(MediaAddress.Root + "*", CoreWebView2WebResourceContext.All);
		core.WebResourceRequested += async (_, e) =>
		{
			if (MediaAddress.TokenOf(e.Request.Uri) is null)
				return;
			var deferral = e.GetDeferral();
			try
			{
				var range = e.Request.Headers.Contains("Range") ? e.Request.Headers.GetHeader("Range") : null;
				var reply = (await router).ServeMedia(e.Request.Uri, range);
				var headers = new List<string> { "Accept-Ranges: bytes", "Cache-Control: no-store", $"Content-Length: {reply.Length}" };
				if (reply.ContentType is { } type)
					headers.Add("Content-Type: " + type);
				if (reply.ContentRange is { } part)
					headers.Add("Content-Range: " + part);
				e.Response = core.Environment.CreateWebResourceResponse(
					reply.Body?.AsRandomAccessStream(), reply.Status, Reason(reply.Status), string.Join("\r\n", headers));
			}
			catch (Exception error)
			{
				log(error);
				e.Response = core.Environment.CreateWebResourceResponse(null, 500, "Internal Server Error", "");
			}
			finally
			{
				deferral.Complete();
			}
		};
	}

	private static string Reason(int status) => status switch
	{
		200 => "OK",
		206 => "Partial Content",
		416 => "Range Not Satisfiable",
		_ => "Not Found",
	};
}

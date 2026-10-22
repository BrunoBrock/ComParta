using ComParta.Core.UseCases;
using Microsoft.Web.WebView2.Core;

namespace ComParta.App;

/// <summary>
/// Files dropped onto the window (epic 8 story 6, AD-7 amended). The UI hands the dropped files to
/// WebView2 with <c>postMessageWithAdditionalObjects</c>; WebView2 gives them back here as files
/// with their real paths, which go to core under the UI's token. The UI itself never sees a path.
/// </summary>
internal static class DropTarget
{
	/// <summary>The message the UI posts with the dropped files; the token follows it.</summary>
	internal const string Prefix = "comparta-drop:";

	public static void Attach(HybridWebView webView, DroppedFiles dropped, Action<Exception> log)
	{
		if (webView.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.WebView2 native)
			return;
		if (native.CoreWebView2 is { } core)
			Listen(core, dropped, log);
		else
			native.CoreWebView2Initialized += (sender, _) =>
			{
				if (sender.CoreWebView2 is { } ready)
					Listen(ready, dropped, log);
			};
	}

	private static void Listen(CoreWebView2 core, DroppedFiles dropped, Action<Exception> log)
	{
		core.WebMessageReceived += (_, e) =>
		{
			try
			{
				string message;
				try { message = e.TryGetWebMessageAsString(); }
				catch (ArgumentException) { return; }
				if (!message.StartsWith(Prefix, StringComparison.Ordinal) || !Guid.TryParse(message[Prefix.Length..], out var token))
					return;

				// A folder comes as a file whose path is the folder; core shares it whole (AD-19).
				var paths = new List<string>();
				if (e.AdditionalObjects is { } objects)
					foreach (var item in objects)
						if (item is CoreWebView2File file && (File.Exists(file.Path) || Directory.Exists(file.Path)))
							paths.Add(file.Path);
				dropped.Put(token, paths);
			}
			catch (Exception error)
			{
				log(error);
			}
		};
	}
}

namespace ComParta.App;

/// <summary>One window, one web view showing the React UI.</summary>
public sealed class MainPage : ContentPage
{
	public MainPage(AppServices services)
	{
		var webView = new HybridWebView { HybridRoot = "wwwroot", DefaultFile = "index.html" };
		webView.SetInvokeJavaScriptTarget(new BridgeHost(services, webView));
#if WINDOWS
		webView.WebViewInitializing += (_, e) => OpenScreenForChecks(e);
		webView.HandlerChanged += (_, _) =>
		{
			DropTarget.Attach(webView, services.Dropped, services.Log);
			MediaStream.Attach(webView, services.Router, services.Log);
		};
#elif ANDROID
		ScreenCheck.OpenIfAsked();
		MediaStream.Attach(webView, services.Router, services.Log);
#endif
		_services = services;
		Content = webView;
	}

	private readonly AppServices _services;

#if WINDOWS
	/// <summary>
	/// Lets the release check (.github/scripts/check-zip.ps1) reach the screen over the web view's
	/// debugging port, so it can drive the real app. Only for a test copy: COMPARTA_HOME must also be
	/// set, so a normal install never opens the port.
	/// </summary>
	private static void OpenScreenForChecks(WebViewInitializingEventArgs e)
	{
		var port = Environment.GetEnvironmentVariable("COMPARTA_SCREEN_CHECK_PORT");
		if (AppPaths.IsUsualRoot || !int.TryParse(port, out var number) || number is < 1024 or > 65535 || e.PlatformArgs is not { } platform)
			return;
		var options = platform.EnvironmentOptions ??= new Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions();
		options.AdditionalBrowserArguments = $"{options.AdditionalBrowserArguments} --remote-debugging-port={number}".Trim();
	}
#endif

	/// <summary>
	/// The phone's back button: the screen closes what is open, or leaves the app when nothing is
	/// (epic 7 story 4). The app never goes away on its own here.
	/// </summary>
	protected override bool OnBackButtonPressed()
	{
		_services.Background?.BackPressed();
		return true;
	}
}

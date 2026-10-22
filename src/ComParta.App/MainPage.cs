namespace ComParta.App;

/// <summary>One window, one web view showing the React UI.</summary>
public sealed class MainPage : ContentPage
{
	public MainPage(AppServices services)
	{
		var webView = new HybridWebView { HybridRoot = "wwwroot", DefaultFile = "index.html" };
		webView.SetInvokeJavaScriptTarget(new BridgeHost(services, webView));
#if WINDOWS
		webView.HandlerChanged += (_, _) =>
		{
			DropTarget.Attach(webView, services.Dropped, services.Log);
			MediaStream.Attach(webView, services.Router, services.Log);
		};
#endif
		_services = services;
		Content = webView;
	}

	private readonly AppServices _services;

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

namespace ComParta.App;

/// <summary>One window, one web view showing the React UI.</summary>
public sealed class MainPage : ContentPage
{
	public MainPage(AppServices services)
	{
		var webView = new HybridWebView { HybridRoot = "wwwroot", DefaultFile = "index.html" };
		webView.SetInvokeJavaScriptTarget(new BridgeHost(services, webView));
		Content = webView;
	}
}

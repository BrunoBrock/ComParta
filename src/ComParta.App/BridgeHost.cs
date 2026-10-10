using ComParta.Contracts;

namespace ComParta.App;

/// <summary>
/// The single bridge between the UI and the app (AD-3). Commands arrive as <c>InvokeDotNet</c> calls
/// to <see cref="Invoke"/>; events leave as one raw message holding an <c>{ event, payload }</c> envelope.
/// </summary>
public sealed class BridgeHost
{
	private readonly AppServices _services;

	public BridgeHost(AppServices services, HybridWebView webView)
	{
		_services = services;
		_ = ForwardEventsAsync(webView);
	}

	public async Task<string> Invoke(string command, string argsJson)
	{
		try
		{
			var router = await _services.Router;
			return await router.InvokeAsync(command, argsJson);
		}
		catch (Exception e)
		{
			_services.Log(e);
			return """{"ok":false,"error":{"code":"unexpected"}}""";
		}
	}

	private async Task ForwardEventsAsync(HybridWebView webView)
	{
		try
		{
			var router = await _services.Router;
			router.EventRaised += json => MainThread.BeginInvokeOnMainThread(() =>
			{
				try { webView.SendRawMessage(json); }
				catch (Exception e) { _services.Log(e); }
			});
		}
		catch (Exception)
		{
			// Start-up failed and was logged; commands report it to the UI.
		}
	}
}

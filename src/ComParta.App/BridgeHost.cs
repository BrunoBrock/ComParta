using ComParta.Contracts;

namespace ComParta.App;

/// <summary>
/// The single bridge between the UI and the app (AD-3). Commands arrive as <c>InvokeDotNet</c> calls
/// to <see cref="Invoke"/>; events leave as one raw message holding an <c>{ event, payload }</c> envelope.
/// </summary>
public sealed class BridgeHost
{
	private readonly AppServices _services;
#if ANDROID
	private int _answered;
#endif

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
			var result = await router.InvokeAsync(command, argsJson);
#if ANDROID
			// The Android check in CI looks for this: the screen loaded and the bridge answered it.
			if (Interlocked.Exchange(ref _answered, 1) == 0)
				Android.Util.Log.Info("ComParta", $"Screen answered: {command}");
			// And for these: the screen showed "Get this file?" for a tapped or shared invite, or said a
			// shared message had none (epic 7 story 6).
			if (command is Commands.TransfersPreview or Commands.InvitesTakeOpened)
				LogAnswer(command, result);
#endif
			return result;
		}
		catch (Exception e)
		{
			_services.Log(e);
			return """{"ok":false,"error":{"code":"unexpected"}}""";
		}
	}

#if ANDROID
	private static void LogAnswer(string command, string result)
	{
		using var answer = System.Text.Json.JsonDocument.Parse(result);
		var root = answer.RootElement;
		if (command == Commands.TransfersPreview && root.GetProperty("ok").GetBoolean())
			Android.Util.Log.Info("ComParta", "Asked: Get this file?");
		else if (command == Commands.InvitesTakeOpened && !root.GetProperty("ok").GetBoolean())
			Android.Util.Log.Info("ComParta", $"Said: {root.GetProperty("error").GetProperty("code").GetString()}");
	}
#endif

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

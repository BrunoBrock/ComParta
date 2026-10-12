using ComParta.Shell;

namespace ComParta.App;

/// <summary>
/// One running copy per data folder (AD-14). The first launch keeps running and receives what
/// later launches were started with, such as a clicked <c>comparta:</c> link; a later launch
/// hands its arguments over and exits.
/// </summary>
public static class Launch
{
	private static readonly object Lock = new();
	private static readonly List<string[]> Waiting = [];
	private static SingleInstance? _instance;
	private static Action<string[]>? _handler;

	/// <summary>True when this is the first copy and should start; false when it handed over and should exit.</summary>
	public static bool ClaimOrForward(IReadOnlyList<string> arguments)
	{
		var key = SingleInstance.KeyFor(AppPaths.DataRoot());
		_instance = SingleInstance.TryClaim(key);
		if (_instance is not null)
		{
			_instance.ArgumentsReceived += Deliver;
			return true;
		}
		// Off the UI thread, so the pipe's awaits never wait for the thread that is blocked here.
		Task.Run(() => SingleInstance.ForwardAsync(key, arguments, TimeSpan.FromSeconds(5))).GetAwaiter().GetResult();
		return false;
	}

	/// <summary>Receives what later launches hand over, including anything that arrived before this call.</summary>
	public static void OnArguments(Action<string[]> handler)
	{
		string[][] waiting;
		lock (Lock)
		{
			_handler = handler;
			waiting = [.. Waiting];
			Waiting.Clear();
		}
		foreach (var arguments in waiting)
			handler(arguments);
	}

	private static void Deliver(string[] arguments)
	{
		Action<string[]>? handler;
		lock (Lock)
		{
			handler = _handler;
			if (handler is null)
				Waiting.Add(arguments);
		}
		handler?.Invoke(arguments);
	}
}

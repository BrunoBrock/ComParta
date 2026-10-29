using Android.Content;

namespace ComParta.App;

/// <summary>
/// What other apps hand ComParta (epic 7 story 6): a tapped invite link, or a whole message shared to
/// it. It goes to the same place as a link clicked on Windows (<see cref="Core.UseCases.OpenedInvites"/>),
/// which keeps it until the screen takes it and shows "Get this file?"; nothing is received before
/// the person taps there. What arrives before the app has started waits here.
/// </summary>
public static class PhoneLinks
{
	private static readonly object Lock = new();
	private static readonly List<(string Text, bool Message)> Waiting = [];
	private static Action<string, bool>? s_handler;

	/// <summary>Reads what an intent from another app carries, if it is a link or a shared message.</summary>
	public static void Hand(Intent intent)
	{
		var (text, message) = intent.Action switch
		{
			Intent.ActionView => (intent.DataString, false),
			Intent.ActionSend when intent.Type == "text/plain" => (intent.GetStringExtra(Intent.ExtraText), true),
			_ => (null, false),
		};
		if (text is null && !message)
			return;
		Android.Util.Log.Info("ComParta", message ? "Message shared to the app" : "Link tapped");
		Deliver(text ?? "", message);
	}

	/// <summary>Receives what was handed over, including anything that arrived before this call.</summary>
	/// <param name="handler">The text, and whether it is a whole message rather than a link.</param>
	public static void OnHanded(Action<string, bool> handler)
	{
		(string, bool)[] waiting;
		lock (Lock)
		{
			s_handler = handler;
			waiting = [.. Waiting];
			Waiting.Clear();
		}
		foreach (var (text, message) in waiting)
			handler(text, message);
	}

	private static void Deliver(string text, bool message)
	{
		Action<string, bool>? handler;
		lock (Lock)
		{
			handler = s_handler;
			if (handler is null)
				Waiting.Add((text, message));
		}
		handler?.Invoke(text, message);
	}
}

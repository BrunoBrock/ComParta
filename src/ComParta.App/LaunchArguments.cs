namespace ComParta.App;

/// <summary>What this launch was started with, as text the core can search for an invite link.</summary>
public static class LaunchArguments
{
	public static string[] Current()
	{
		var arguments = Environment.GetCommandLineArgs().ToList();
#if WINDOWS
		// A packaged install receives a clicked link as an activation, not on the command line.
		try
		{
			var activation = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
			if (activation.Kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.Protocol
				&& activation.Data is global::Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs protocol)
				arguments.Add(protocol.Uri.OriginalString);
		}
		catch (Exception)
		{
			// Unpackaged builds without activation support still have the command line.
		}
#endif
		return [.. arguments];
	}
}

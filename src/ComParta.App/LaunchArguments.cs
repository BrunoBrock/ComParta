namespace ComParta.App;

/// <summary>What this launch was started with, as text the core can search for an invite link.</summary>
public static class LaunchArguments
{
	public static string[] Current()
	{
		var arguments = Environment.GetCommandLineArgs().ToList();
#if WINDOWS
		// A packaged install receives a clicked link or an opened download file as an activation,
		// not on the command line (epic 14 story 5).
		try
		{
			var activation = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
			if (activation.Kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.Protocol
				&& activation.Data is global::Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs protocol)
				arguments.Add(protocol.Uri.OriginalString);
			else if (activation.Kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.File
				&& activation.Data is global::Windows.ApplicationModel.Activation.IFileActivatedEventArgs file)
				arguments.AddRange(file.Files.Select(item => item.Path).Where(path => !string.IsNullOrEmpty(path)));
		}
		catch (Exception)
		{
			// Unpackaged builds without activation support still have the command line.
		}
#endif
		return [.. arguments];
	}
}

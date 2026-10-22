namespace ComParta.App;

/// <summary>
/// What changes when ComParta runs installed from its MSIX package (epic 5 story 2): Windows starts
/// it at sign-in through the package's startup task, and the package itself declares the
/// <c>comparta:</c> links and the firewall rule, so nothing is written to the registry.
/// </summary>
public static class Packaged
{
	/// <summary>The startup task's id, as Package.appxmanifest declares it.</summary>
	public const string StartupTaskId = "ComPartaStartup";

	/// <summary>True when this copy was installed from the package.</summary>
	public static bool IsPackaged { get; } = Detect();

	private static bool Detect()
	{
		try
		{
			return global::Windows.ApplicationModel.Package.Current is not null;
		}
		catch (Exception)
		{
			// An unpackaged copy has no package identity, and Windows says so by throwing.
			return false;
		}
	}

	/// <summary>True when Windows started the packaged copy at sign-in, so the window stays hidden.</summary>
	public static bool StartedAtSignIn()
	{
		if (!IsPackaged)
			return false;
		try
		{
			return Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs().Kind
				== Microsoft.Windows.AppLifecycle.ExtendedActivationKind.StartupTask;
		}
		catch (Exception)
		{
			return false;
		}
	}

	/// <summary>Turns the startup task on or off. False when Windows keeps it off, for example by the person's choice in Task Manager.</summary>
	public static async Task<bool> SetStartAtSignInAsync(bool on)
	{
		var task = await global::Windows.ApplicationModel.StartupTask.GetAsync(StartupTaskId);
		if (!on)
		{
			task.Disable();
			return true;
		}
		var state = task.State is global::Windows.ApplicationModel.StartupTaskState.Enabled
			? task.State
			: await task.RequestEnableAsync();
		return state is global::Windows.ApplicationModel.StartupTaskState.Enabled
			or global::Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
	}
}

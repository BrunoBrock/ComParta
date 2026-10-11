using Microsoft.UI.Xaml;

namespace ComParta.App.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	/// <summary>
	/// Initializes the singleton application object.  This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		this.InitializeComponent();
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	protected override void OnLaunched(LaunchActivatedEventArgs args)
	{
		// A second launch, for example from a clicked link, hands over to the running copy (AD-14).
		if (!Launch.ClaimOrForward(LaunchArguments.Current()))
		{
			Exit();
			return;
		}
		base.OnLaunched(args);
	}
}

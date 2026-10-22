using ComParta.Core.UseCases;
using ComParta.Shell;

namespace ComParta.App;

public partial class App : Application
{
	// Set once the app is really ending, so closing the window no longer hides it.
	private bool _quitting;

	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var services = AppServices.Start();
		var window = new Window(new MainPage(services))
		{
			Title = "ComParta",
			Width = 760,
			Height = 820,
			MinimumWidth = 420,
			MinimumHeight = 520,
		};
		window.Destroying += (_, _) => services.Stop();
		services.Focus.Watch(window);
		services.NotificationClicked += () => MainThread.BeginInvokeOnMainThread(() => BringToFront(window));
		services.ShowWindowRequested += () => MainThread.BeginInvokeOnMainThread(() => BringToFront(window));
		services.HideWindowRequested += () => MainThread.BeginInvokeOnMainThread(() => Hide(window));
		services.QuitRequested += () => MainThread.BeginInvokeOnMainThread(() =>
		{
			_quitting = true;
			services.Stop();
			Quit();
		});
		var startHidden = StartupEntry.IsBackgroundLaunch(Environment.GetCommandLineArgs()) || Packaged.StartedAtSignIn();
		window.HandlerChanged += (_, _) => KeepWhenClosed(window, services, startHidden);

		// What this launch was started with, then whatever later launches hand over (AD-14). A
		// later launch always shows the window, which may be hidden in the notification area.
		services.Opened.Open(LaunchArguments.Current());
		Launch.OnArguments(arguments =>
		{
			services.Opened.Open(arguments);
			MainThread.BeginInvokeOnMainThread(() => BringToFront(window));
		});
		return window;
	}

	/// <summary>
	/// Closing the window hides it while something is shared or moving, and quits otherwise
	/// (AD-14). Started from the start-up entry, the window begins hidden.
	/// </summary>
	private void KeepWhenClosed(Window window, AppServices services, bool startHidden)
	{
#if WINDOWS
		if (window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window native)
			return;
		native.AppWindow.Closing += (appWindow, args) =>
		{
			if (_quitting)
				return;
			if ((services.Background?.WindowClosing() ?? CloseChoice.Quit) == CloseChoice.Hide)
			{
				args.Cancel = true;
				appWindow.Hide();
			}
		};
		if (startHidden)
			native.AppWindow.Hide();
#endif
	}

	private static void Hide(Window window)
	{
#if WINDOWS
		if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window native)
			native.AppWindow.Hide();
#endif
	}

	private static void BringToFront(Window window)
	{
#if WINDOWS
		if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window native)
		{
			native.AppWindow.Show();
			if (native.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized } presenter)
				presenter.Restore();
			native.Activate();
		}
#endif
	}
}

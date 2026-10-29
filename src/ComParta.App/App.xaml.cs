using ComParta.Core.UseCases;
using ComParta.Shell;

namespace ComParta.App;

public partial class App : Application
{
	// Set once the app is really ending, so closing the window no longer hides it.
	private bool _quitting;
#if ANDROID
	// Android makes the screen again while the app still runs (the system closed it to save memory,
	// or it was opened anew), so the engine and storage already running are kept, never started twice.
	private static AppServices? s_running;
#endif

	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
#if ANDROID
		var services = s_running is { Stopped: false } ? s_running : s_running = AppServices.Start();
#else
		var services = AppServices.Start();
#endif
		var window = new Window(new MainPage(services))
		{
			Title = "ComParta",
			Width = 760,
			Height = 820,
			MinimumWidth = 420,
			MinimumHeight = 520,
		};
#if WINDOWS
		window.Destroying += (_, _) => services.Stop();
#endif
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
		// What this launch was started with (AD-14).
		services.Opened.Open(LaunchArguments.Current());
#if ANDROID
		// An invite tapped or shared in another app (epic 7 story 6), now and while the app runs.
		PhoneLinks.OnHanded((text, message) =>
		{
			if (message)
				services.Opened.OpenMessage(text);
			else
				services.Opened.Open([text]);
		});
#endif
#if WINDOWS
		var startHidden = StartupEntry.IsBackgroundLaunch(Environment.GetCommandLineArgs()) || Packaged.StartedAtSignIn();
		window.HandlerChanged += (_, _) => KeepWhenClosed(window, services, startHidden);

		// Whatever later launches hand over. A later launch always shows the window, which may be
		// hidden in the notification area.
		Launch.OnArguments(arguments =>
		{
			services.Opened.Open(arguments);
			MainThread.BeginInvokeOnMainThread(() => BringToFront(window));
		});
#endif
		return window;
	}

	/// <summary>
	/// Closing the window hides it while something is shared or moving, and quits otherwise
	/// (AD-14). Started from the start-up entry, the window begins hidden.
	/// </summary>
#if WINDOWS
	private void KeepWhenClosed(Window window, AppServices services, bool startHidden)
	{
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
	}
#endif

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

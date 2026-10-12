namespace ComParta.App;

public partial class App : Application
{
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

		// What this launch was started with, then whatever later launches hand over (AD-14).
		services.Opened.Open(LaunchArguments.Current());
		Launch.OnArguments(arguments =>
		{
			if (services.Opened.Open(arguments))
				MainThread.BeginInvokeOnMainThread(() => BringToFront(window));
		});
		return window;
	}

	private static void BringToFront(Window window)
	{
#if WINDOWS
		if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window native)
		{
			if (native.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized } presenter)
				presenter.Restore();
			native.Activate();
		}
#endif
	}
}

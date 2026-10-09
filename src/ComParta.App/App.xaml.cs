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
		return window;
	}
}

using Android.App;
using Android.Content.PM;

namespace ComParta.App;

// A fixed name, so the Android check in CI can start it.
[Activity(
	Name = "org.comparta.app.MainActivity",
	Theme = "@style/Maui.SplashTheme",
	MainLauncher = true,
	LaunchMode = LaunchMode.SingleTask,
	ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout
		| ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
	protected override void OnCreate(Android.OS.Bundle? savedInstanceState)
	{
		// Read by the Android check in CI, with the start line, to see the app starts only once.
		Android.Util.Log.Info("ComParta", $"Screen opened{(savedInstanceState is null ? "" : " again")}");
		base.OnCreate(savedInstanceState);
	}

	/// <summary>The phone's picker answered: what the person picked, or nothing when they went back.</summary>
	protected override void OnActivityResult(int requestCode, Result resultCode, Android.Content.Intent? data)
	{
		base.OnActivityResult(requestCode, resultCode, data);
		AndroidPicker.Answered(requestCode, resultCode, data);
	}
}

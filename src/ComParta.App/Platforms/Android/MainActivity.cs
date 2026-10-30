using Android.App;
using Android.Content;
using Android.Content.PM;

namespace ComParta.App;

// A fixed name, so the Android check in CI can start it.
[Activity(
	Name = "org.comparta.app.MainActivity",
	Theme = "@style/Maui.SplashTheme",
	MainLauncher = true,
	Exported = true,
	LaunchMode = LaunchMode.SingleTask,
	ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout
		| ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
// An invite tapped in a chat or an email opens "Get this file?" (epic 7 story 6): the ComParta links
// of earlier builds, the engine's own links that invites are now (AD-16, amended), and a whole
// message shared from another app's Share menu, such as WhatsApp's.
[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable }, DataScheme = "comparta")]
[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable }, DataScheme = Engine.MonoTorrent.MonoTorrentTransferEngine.LinkScheme)]
[IntentFilter(new[] { Intent.ActionSend }, Categories = new[] { Intent.CategoryDefault }, DataMimeType = "text/plain")]
public class MainActivity : MauiAppCompatActivity
{
	protected override void OnCreate(Android.OS.Bundle? savedInstanceState)
	{
		// Read by the Android check in CI, with the start line, to see the app starts only once.
		Android.Util.Log.Info("ComParta", $"Screen opened{(savedInstanceState is null ? "" : " again")}");
		base.OnCreate(savedInstanceState);
#if DEBUG
		// A debug build tries the phone's time limit for the screen off in a minute:
		// adb shell am start -n org.comparta.app/org.comparta.app.MainActivity --ei limitSeconds 60
		if (Intent?.GetIntExtra("limitSeconds", 0) is > 0 and var seconds)
			MovingService.Limit = TimeSpan.FromSeconds(seconds);
#endif
		// A screen made again (turned, or after the system closed it) would bring back the link it was
		// first opened with, and so would opening the app again from the recent apps.
		if (savedInstanceState is null && Intent is { } intent && !intent.Flags.HasFlag(ActivityFlags.LaunchedFromHistory))
			PhoneLinks.Hand(intent);
	}

	/// <summary>The person opened the app: what was paused to save battery continues (epic 7 story 7).</summary>
	protected override void OnResume()
	{
		base.OnResume();
		AndroidPresence.Current?.AppOpened();
	}

	/// <summary>The app was already open: the same screen and engine take the link (one running copy, as on Windows).</summary>
	protected override void OnNewIntent(Intent? intent)
	{
		base.OnNewIntent(intent);
		if (intent is not null)
		{
			Intent = intent;
			PhoneLinks.Hand(intent);
		}
	}

	/// <summary>The phone's picker answered: what the person picked, or nothing when they went back.</summary>
	protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
	{
		base.OnActivityResult(requestCode, resultCode, data);
		AndroidPicker.Answered(requestCode, resultCode, data);
	}
}

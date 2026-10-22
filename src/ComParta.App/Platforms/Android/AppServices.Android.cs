using ComParta.Core.UseCases;
using Ports = ComParta.Core.Ports;

namespace ComParta.App;

/// <summary>
/// The Android adapters (epic 7). This first version runs the same core, engine and screen; the
/// phone's own pickers, received files in Downloads, notifications and the background service come
/// in the stories after it, so their adapters here do as little as is safe.
/// </summary>
public sealed partial class AppServices
{
	private static partial Ports.IAppPaths CreatePaths() => new AndroidPaths();

	private partial PlatformParts CreatePlatform() =>
		new(new AndroidFirewall(), new AndroidOpener(), new NoThumbnails(), new NoNotifications(), new AndroidPresence(Stop));

	partial void AfterStart(NotificationUseCases notifications, BackgroundUseCases background) =>
		// The line the Android check in CI looks for: the engine started and the screen can be answered.
		Android.Util.Log.Info("ComParta", "ComParta started");
}

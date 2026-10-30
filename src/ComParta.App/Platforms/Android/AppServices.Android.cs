using ComParta.Core.UseCases;
using Ports = ComParta.Core.Ports;

namespace ComParta.App;

/// <summary>
/// The Android adapters (epic 7): the same core, engine and screen, with the phone's own picker and
/// its picked items read where they are (story 2), and the service that keeps transfers going with
/// the screen off (story 7). Notifications for what arrived come in a later story, so that adapter
/// does as little as is safe.
/// </summary>
public sealed partial class AppServices
{
	private static partial Ports.IAppPaths CreatePaths() => new AndroidPaths();

	private partial PlatformParts CreatePlatform()
	{
		// Picked items are read where they are, through the phone's content resolver (story 2).
		var storage = new AndroidStorage();
		return new(
			new AndroidFirewall(), new AndroidOpener(), new NoThumbnails(), new NoNotifications(), new AndroidPresence(Stop, () => Background),
			// The phone keeps received files in its own sandbox; there is no protection to hand them to.
			new AndroidPicker(), storage, storage, Ports.NoReceivedFileCheck.Instance);
	}

	partial void AfterStart(NotificationUseCases notifications, BackgroundUseCases background) =>
		// The line the Android check in CI looks for: the engine started and the screen can be answered.
		Android.Util.Log.Info("ComParta", "ComParta started");
}

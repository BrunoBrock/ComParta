using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using Ports = ComParta.Core.Ports;

namespace ComParta.App;

/// <summary>
/// Keeps transfers going with the screen off (AD-14, epic 7 story 7): a foreground service of the
/// data sync kind, running while bytes move and shown as a notification with how far the files are
/// and a Stop button. What it says comes from core (<see cref="Ports.MovingNotice"/>).
/// </summary>
[Service(Name = "org.comparta.app.MovingService", Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class MovingService : Service
{
	private const string Channel = "moving";
	private const int MovingId = 7701;
	private const int PausedId = 7702;
	private const string StopAction = "org.comparta.app.STOP";

	private static Ports.MovingNotice? s_notice;
	private static bool s_running;
	private Handler? _limit;

	/// <summary>
	/// A shorter time the phone allows with the screen off than Android 15's six hours, so a debug
	/// build can try the time-out in a minute; null leaves it to the phone.
	/// </summary>
	public static TimeSpan? Limit { get; set; }

	/// <summary>Starts the service, or updates its notification while it runs.</summary>
	public static void Show(Context context, Ports.MovingNotice notice)
	{
		s_notice = notice;
		MakeChannel(context);
		if (s_running)
			NotificationManagerCompat.From(context).Notify(MovingId, Build(context, notice));
		else
			context.StartForegroundService(new Intent(context, typeof(MovingService)));
	}

	/// <summary>Stops the service and takes its notification away.</summary>
	public static void Hide(Context context)
	{
		s_notice = null;
		if (s_running)
			context.StopService(new Intent(context, typeof(MovingService)));
	}

	/// <summary>Says plainly that transfers were paused to save battery; opening the app continues them.</summary>
	public static void ShowPaused(Context context, Ports.MovingNotice notice)
	{
		MakeChannel(context);
		NotificationManagerCompat.From(context).Notify(PausedId, Builder(context, notice).SetAutoCancel(true)!.Build());
		Android.Util.Log.Info("ComParta", "Paused to save battery");
	}

	public static void HidePaused(Context context) => NotificationManagerCompat.From(context).Cancel(PausedId);

	public override IBinder? OnBind(Intent? intent) => null;

	public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
	{
		if (intent?.Action == StopAction)
		{
			Android.Util.Log.Info("ComParta", "Stop pressed on the notification");
			AndroidPresence.Current?.StopFromNotice();
			return StartCommandResult.NotSticky;
		}
		if (s_notice is not { } notice)
		{
			StopSelf();
			return StartCommandResult.NotSticky;
		}
		var notification = Build(this, notice);
		if (OperatingSystem.IsAndroidVersionAtLeast(29))
			StartForeground(MovingId, notification, ForegroundService.TypeDataSync);
		else
			StartForeground(MovingId, notification);
		if (!s_running)
		{
			s_running = true;
			// Read by the Android check in CI.
			Android.Util.Log.Info("ComParta", "Keeping going with the screen off");
			if (Limit is { } limit)
			{
				_limit = new Handler(Looper.MainLooper!);
				_limit.PostDelayed(TimeUp, (long)limit.TotalMilliseconds);
			}
		}
		return StartCommandResult.NotSticky;
	}

	/// <summary>Android 15 and later: the six hours a data sync service may run are up.</summary>
	public override void OnTimeout(int startId, int fgsType) => TimeUp();

	private void TimeUp()
	{
		Android.Util.Log.Info("ComParta", "The phone allowed no more time");
		// The phone ends the app if the service does not stop at once.
		StopForeground(StopForegroundFlags.Remove);
		StopSelf();
		AndroidPresence.Current?.TimedOut();
	}

	public override void OnDestroy()
	{
		s_running = false;
		_limit?.RemoveCallbacksAndMessages(null);
		_limit = null;
		Android.Util.Log.Info("ComParta", "Stopped keeping going with the screen off");
		base.OnDestroy();
	}

	private static void MakeChannel(Context context)
	{
		var manager = (NotificationManager?)context.GetSystemService(NotificationService);
		// Quiet: it changes as files move, and the person did not ask to be told.
		manager?.CreateNotificationChannel(new NotificationChannel(Channel, "ComParta", NotificationImportance.Low));
	}

	private static Notification Build(Context context, Ports.MovingNotice notice)
	{
		var builder = Builder(context, notice).SetOngoing(true)!;
		if (notice.Percent is { } percent)
			builder.SetProgress(100, percent, false);
		else
			builder.SetProgress(0, 0, true);
		if (notice.Stop is { } stop)
		{
			var stopIntent = new Intent(context, typeof(MovingService)).SetAction(StopAction);
			builder.AddAction(0, stop, PendingIntent.GetService(context, 1, stopIntent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent));
		}
		return builder.Build()!;
	}

	/// <summary>Title and words, the app's icon, and a tap that opens ComParta.</summary>
	private static NotificationCompat.Builder Builder(Context context, Ports.MovingNotice notice)
	{
		var open = new Intent(context, typeof(MainActivity)).SetAction(Intent.ActionMain).AddCategory(Intent.CategoryLauncher);
		return new NotificationCompat.Builder(context, Channel)
			.SetSmallIcon(context.ApplicationInfo!.Icon)!
			.SetContentTitle(notice.Title)!
			.SetContentText(notice.Body)!
			.SetOnlyAlertOnce(true)!
			.SetSilent(true)!
			.SetForegroundServiceBehavior(NotificationCompat.ForegroundServiceImmediate)!
			.SetContentIntent(PendingIntent.GetActivity(context, 0, open, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent))!;
	}
}

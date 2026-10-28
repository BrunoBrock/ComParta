using Android.App;
using Android.Content;
using Ports = ComParta.Core.Ports;
using AndroidUri = Android.Net.Uri;

namespace ComParta.App;

/// <summary>
/// The phone's own picker for what the person shares (epic 7 story 2): one or several files, or a
/// folder. Each item keeps a read permission that lasts across restarts, so a share is still offered
/// after the phone restarts, and nothing is copied (AD-20). Download files are picked as before.
/// </summary>
public sealed class AndroidPicker : Ports.IFilePicker
{
	private const int FilesRequest = 7201;
	private const int FolderRequest = 7202;

	private static readonly object Gate = new();
	private static (int Request, TaskCompletionSource<Intent?> Answer)? s_waiting;

	private readonly NativeFilePicker _native = new();

	/// <summary>Called by the activity with the picker's answer.</summary>
	public static void Answered(int requestCode, Result resultCode, Intent? data)
	{
		TaskCompletionSource<Intent?>? answer = null;
		lock (Gate)
		{
			if (s_waiting is { } waiting && waiting.Request == requestCode)
			{
				answer = waiting.Answer;
				s_waiting = null;
			}
		}
		answer?.TrySetResult(resultCode == Result.Ok ? data : null);
	}

	public async Task<string?> PickFileAsync(CancellationToken ct = default) => (await PickFilesAsync(ct)).FirstOrDefault();

	public Task<string?> PickDownloadFileAsync(CancellationToken ct = default) => _native.PickDownloadFileAsync(ct);

	public async Task<IReadOnlyList<string>> PickFilesAsync(CancellationToken ct = default)
	{
		var intent = new Intent(Intent.ActionOpenDocument);
		intent.AddCategory(Intent.CategoryOpenable);
		intent.SetType("*/*");
		intent.PutExtra(Intent.ExtraAllowMultiple, true);
		intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);
		var data = await AskAsync(intent, FilesRequest, ct);
		if (data is null)
			return [];

		var picked = new List<AndroidUri>();
		if (data.ClipData is { } several)
		{
			for (var i = 0; i < several.ItemCount; i++)
				if (several.GetItemAt(i)?.Uri is { } uri)
					picked.Add(uri);
		}
		else if (data.Data is { } one)
		{
			picked.Add(one);
		}
		return [.. picked.Select(Keep).OfType<string>()];
	}

	public async Task<string?> PickFolderAsync(CancellationToken ct = default)
	{
		var intent = new Intent(Intent.ActionOpenDocumentTree);
		intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);
		var data = await AskAsync(intent, FolderRequest, ct);
		return data?.Data is { } tree ? Keep(tree) : null;
	}

	/// <summary>Keeps the permission to read the item after the app or the phone restarts.</summary>
	private static string? Keep(AndroidUri uri)
	{
		try
		{
			Android.App.Application.Context.ContentResolver!.TakePersistableUriPermission(uri, ActivityFlags.GrantReadUriPermission);
		}
		catch (Java.Lang.SecurityException)
		{
			// The app that holds the item lends it only for now; it is still shared until a restart.
		}
		return uri.ToString();
	}

	private static async Task<Intent?> AskAsync(Intent intent, int request, CancellationToken ct)
	{
		var answer = new TaskCompletionSource<Intent?>(TaskCreationOptions.RunContinuationsAsynchronously);
		lock (Gate)
		{
			// One picker at a time: an earlier one that never answered counts as cancelled.
			s_waiting?.Answer.TrySetResult(null);
			s_waiting = (request, answer);
		}
		await MainThread.InvokeOnMainThreadAsync(() =>
		{
			var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("The app has no screen to show the picker on.");
			activity.StartActivityForResult(intent, request);
		});
		using (ct.Register(() => answer.TrySetCanceled(ct)))
			return await answer.Task;
	}
}

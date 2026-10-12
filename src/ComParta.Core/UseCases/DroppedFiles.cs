using System.Collections.Concurrent;

namespace ComParta.Core.UseCases;

/// <summary>
/// Files dropped onto the window (epic 8 story 6, AD-7). The UI hands the dropped files to the
/// window with a token, the window resolves them natively and puts their paths here, and the UI
/// then asks to share whatever arrived under its token. No path ever reaches the UI.
/// </summary>
public sealed class DroppedFiles
{
    /// <summary>How long the UI's request waits for the window to hand over the files.</summary>
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<IReadOnlyList<string>>> _drops = new();

    /// <summary>Called by the window with the paths it resolved for one drop.</summary>
    public void Put(Guid token, IReadOnlyList<string> paths) => Slot(token).TrySetResult(paths);

    /// <summary>The paths dropped under this token, once; null when none came in time.</summary>
    public async Task<IReadOnlyList<string>?> TakeAsync(Guid token, TimeSpan wait, CancellationToken ct = default)
    {
        try { return await Slot(token).Task.WaitAsync(wait, ct); }
        catch (TimeoutException) { return null; }
        finally { _drops.TryRemove(token, out _); }
    }

    private TaskCompletionSource<IReadOnlyList<string>> Slot(Guid token) =>
        _drops.GetOrAdd(token, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
}

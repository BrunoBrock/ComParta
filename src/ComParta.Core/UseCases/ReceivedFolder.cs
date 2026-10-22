using ComParta.Core.Ports;

namespace ComParta.Core.UseCases;

/// <summary>
/// Where received files go: the usual Received root, or a folder the person chose in Settings with
/// Windows' own folder dialog (epic 12 story 2, AD-6, AD-7). Only new transfers use a new choice; each
/// transfer keeps the folder it started in (<see cref="Transfer.Folder"/>).
/// </summary>
public sealed class ReceivedFolder(ISettingsRepository repository, IAppPaths paths, IFilePicker? picker = null, IFileSystem? files = null)
{
    private const string Key = "receivedFolder";
    private string? _chosen;

    /// <summary>The folder the person chose, or null for the usual one.</summary>
    public string? Chosen => Volatile.Read(ref _chosen);

    /// <summary>Where a new transfer goes now.</summary>
    public string Current => Chosen ?? Path.GetFullPath(paths.ReceivedRoot);

    /// <summary>Reads the stored choice; call once at start, before any transfer starts.</summary>
    public async Task LoadAsync(CancellationToken ct = default) =>
        Volatile.Write(ref _chosen, await repository.GetAsync(Key, ct) is { Length: > 0 } stored && Path.IsPathFullyQualified(stored) ? stored : null);

    /// <summary>The name of a Received root as the person knows it, without the rest of its path (AD-7).</summary>
    public static string NameOf(string folder)
    {
        var full = Path.GetFullPath(folder);
        // A drive's own root has no name of its own; it is shown as the drive, such as "D:\".
        return Path.GetFileName(Path.TrimEndingDirectorySeparator(full)) is { Length: > 0 } name
            ? name
            : Path.TrimEndingDirectorySeparator(full);
    }

    /// <summary>
    /// Opens the folder dialog and keeps the folder picked, after checking ComParta can write there.
    /// The value is the new folder's name, or null when the person closed the dialog.
    /// </summary>
    public async Task<Result<string?>> ChooseAsync(CancellationToken ct = default)
    {
        if (picker is null || files is null)
            return Result<string?>.Fail(ErrorKind.Unexpected);
        var picked = await picker.PickFolderAsync(ct);
        if (picked is null)
            return Result<string?>.Success(null);
        if (!Path.IsPathFullyQualified(picked))
            return Result<string?>.Fail(ErrorKind.ReceivedFolderUnusable);

        var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(picked));
        // Picking the usual folder again is the same as never having chosen one.
        var usual = Same(folder, paths.ReceivedRoot);
        // ComParta's own folders hold its database and the engine's data; received files must not mix with them.
        if (!usual && (OwnFolders().Any(own => Same(own, folder) || ReceivedNames.IsInside(own, folder)) || !CanWrite(folder)))
            return Result<string?>.Fail(ErrorKind.ReceivedFolderUnusable);

        var chosen = usual ? null : folder;
        await repository.SetAsync(Key, chosen, ct);
        Volatile.Write(ref _chosen, chosen);
        return Result<string?>.Success(NameOf(folder));
    }

    /// <summary>New transfers go to the usual folder again.</summary>
    public async Task UseUsualAsync(CancellationToken ct = default)
    {
        await repository.SetAsync(Key, null, ct);
        Volatile.Write(ref _chosen, null);
    }

    private IEnumerable<string> OwnFolders()
    {
        yield return paths.EngineRoot;
        yield return paths.LogsRoot;
        if (Path.GetDirectoryName(Path.GetFullPath(paths.DatabasePath)) is { } data)
            yield return data;
    }

    private static bool Same(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    /// <summary>Writes and deletes a small file, as receiving will; a folder that is read only, or gone, fails here.</summary>
    private bool CanWrite(string folder)
    {
        if (!files!.FolderExists(folder))
            return false;
        var probe = Path.Combine(folder, $".comparta-check-{Guid.NewGuid():N}");
        try
        {
            files.WriteNewFile(probe, [0]);
            files.DeleteFile(probe);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
}

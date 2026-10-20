namespace ComParta.Shell;

/// <summary>
/// Brings what the unpackaged pilot build kept into the packaged install the first time it runs
/// (epic 5 story 5): the database and the engine's data, so shares, transfers, settings and the
/// port invites name all carry over. The pilot's folder is only read, never changed.
/// </summary>
public static class PilotData
{
    /// <summary>Written once the import is done, so it happens once.</summary>
    public const string MarkerName = "imported-from-pilot.txt";

    // The same layout as AppPaths: the database and the engine's folder at the top of the data folder.
    private const string DatabaseName = "comparta.db";
    private const string EngineFolderName = "engine";

    /// <summary>
    /// Copies the pilot's data into <paramref name="root"/> when there is pilot data and this
    /// install has none of its own yet. True when it copied.
    /// </summary>
    /// <param name="copyDatabase">Copies the database safely, even while the pilot has it open.</param>
    public static bool Import(string pilotRoot, string root, Action<string, string> copyDatabase)
    {
        var database = Path.Combine(pilotRoot, DatabaseName);
        if (File.Exists(Path.Combine(root, MarkerName)) || File.Exists(Path.Combine(root, DatabaseName)) || !File.Exists(database))
            return false;
        Directory.CreateDirectory(root);
        CopyFolder(Path.Combine(pilotRoot, EngineFolderName), Path.Combine(root, EngineFolderName));
        // The database last: once it is there, nothing is copied again even if the marker was not written.
        copyDatabase(database, Path.Combine(root, DatabaseName));
        File.WriteAllText(Path.Combine(root, MarkerName), DateTime.UtcNow.ToString("O"));
        return true;
    }

    private static void CopyFolder(string from, string to)
    {
        if (!Directory.Exists(from))
            return;
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            try
            {
                File.Copy(file, target, overwrite: true);
            }
            catch (IOException)
            {
                // A file the running pilot holds is only engine cache; the engine rebuilds it.
            }
        }
    }
}

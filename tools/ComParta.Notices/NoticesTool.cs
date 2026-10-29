namespace ComParta.Notices;

/// <summary>Lists the parts every shipped project uses, applies the license policy and writes or checks the notices.</summary>
public static class NoticesTool
{
    private const string AppFramework = "net10.0-windows10.0.19041.0";

    /// <summary>
    /// The projects that ship, with the framework of their shipped build. The app's assets already hold the
    /// packages of the projects it references; listing those too keeps a package they gain from being missed.
    /// </summary>
    public static readonly IReadOnlyList<(string Project, string Framework)> Shipped =
    [
        ("src/ComParta.App", AppFramework),
        ("src/ComParta.Helper", "net10.0"),
        ("src/ComParta.Core", "net10.0"),
        ("src/ComParta.Contracts", "net10.0"),
        ("src/ComParta.Engine.MonoTorrent", "net10.0"),
        ("src/ComParta.Shell", "net10.0"),
        ("src/ComParta.Storage.Sqlite", "net10.0"),
    ];

    public const string StaleMessage = $"{NoticesWriter.FileName} is out of date. Run: dotnet run --project tools/ComParta.Notices";

    /// <summary>0 when the notices were written or are up to date, 1 when a package fails or the file is stale, 2 when restore has not run.</summary>
    public static int Run(string root, bool check, bool withoutApp, TextWriter output)
    {
        var parts = new List<Part>();
        foreach (var (project, framework) in Shipped)
        {
            var assets = Path.Combine(root, project, "obj", "project.assets.json");
            if (NuGetParts.HasTarget(assets, framework))
            {
                parts.AddRange(NuGetParts.Read(assets, framework));
                continue;
            }
            if (framework == AppFramework && withoutApp)
            {
                output.WriteLine($"Left out {project}: --without-app was given, so the notices miss the app's own Windows packages.");
                continue;
            }
            output.WriteLine(framework == AppFramework
                ? $"{project} has not been restored for Windows. Build the app on Windows first (build.ps1 Build)."
                : $"{project} has not been restored. Run: dotnet restore ComParta.sln");
            return 2;
        }

        var ui = Path.Combine(root, "src", "ui");
        if (!Directory.Exists(Path.Combine(ui, "node_modules")))
        {
            output.WriteLine("The screen's packages are not installed. Run: npm ci (in src/ui)");
            return 2;
        }
        parts.AddRange(NpmParts.Read(ui));

        var resolved = parts.Select(LicensePolicy.Resolve).ToList();
        var problems = LicensePolicy.Problems(resolved);
        if (problems.Count > 0)
        {
            output.WriteLine("These packages cannot ship with ComParta (see CONTRIBUTING.md, New packages):");
            foreach (var problem in problems.Distinct())
                output.WriteLine($"  {problem}");
            return 1;
        }

        var fresh = NoticesWriter.Write(resolved, NoticesWriter.StandardText);
        var target = Path.Combine(root, NoticesWriter.FileName);
        if (!check)
        {
            File.WriteAllText(target, fresh);
            output.WriteLine($"Wrote {target}");
            return 0;
        }

        var difference = NoticesWriter.Difference(File.Exists(target) ? File.ReadAllText(target) : null, fresh);
        if (difference is null)
        {
            output.WriteLine($"{NoticesWriter.FileName} is up to date.");
            return 0;
        }
        // Kept where CI can upload it, so the right file can be taken from a Windows run.
        var copy = Path.Combine(root, "publish", NoticesWriter.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.WriteAllText(copy, fresh);
        output.WriteLine(difference);
        output.WriteLine($"The fresh file is in {copy}.");
        output.WriteLine(StaleMessage);
        return 1;
    }
}

using System.Text.RegularExpressions;
using ComParta.Contracts;
using ComParta.Core.UseCases;

namespace ComParta.Core.Tests;

/// <summary>Checks that keep the architecture's rules from eroding (AD-1, AD-3, AD-5).</summary>
public sealed partial class GuardTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    // Folders where these words are allowed: the engine adapter and its tests (AD-5).
    private static readonly string[] EngineFolders =
    [
        Path.Combine("src", "ComParta.Engine.MonoTorrent"),
        Path.Combine("tests", "ComParta.Engine.MonoTorrent.Tests"),
    ];

    // The two files that have to spell the words out in order to forbid them.
    private static readonly string[] FilesNamingTheTerms = ["GuardTests.cs", "catalog.test.ts"];

    [GeneratedRegex(@"\b(seed|peer|leech|swarm|tracker|magnet|torrent|hash|dht)(s|ed|er|ers|ing)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex TechnicalTerm();

    [Fact]
    public void Core_depends_on_no_other_project_and_no_adapter_package()
    {
        var references = typeof(ShareUseCases).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        Assert.All(references, name => Assert.True(
            name.StartsWith("System.", StringComparison.Ordinal) || name is "netstandard" or "mscorlib",
            $"ComParta.Core references {name}."));
    }

    [Fact]
    public void Technical_terms_appear_only_inside_the_engine_adapter()
    {
        var offences = new List<string>();
        foreach (var file in SourceFiles())
        {
            var relative = Path.GetRelativePath(RepositoryRoot, file);
            if (EngineFolders.Any(folder => relative.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                || FilesNamingTheTerms.Contains(Path.GetFileName(file)))
                continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (TechnicalTerm().Match(lines[i]) is { Success: true } match)
                    offences.Add($"{relative}:{i + 1} \"{match.Value}\"");
            }
        }

        Assert.True(offences.Count == 0, "Technical terms outside the engine adapter:\n" + string.Join("\n", offences));
    }

    [Fact]
    public void The_generated_TypeScript_contract_is_up_to_date()
    {
        var onDisk = File.ReadAllText(Path.Combine(RepositoryRoot, TypeScriptWriter.GeneratedFile)).ReplaceLineEndings("\n");

        Assert.True(onDisk == TypeScriptWriter.Write(),
            "The contract changed. Run: dotnet run --project tools/ComParta.Contracts.TypeGen");
    }

    [Fact]
    public void The_notification_sentences_match_the_UI_catalogs()
    {
        var onDisk = File.ReadAllText(Path.Combine(RepositoryRoot, NotificationTextsWriter.GeneratedFile)).ReplaceLineEndings("\n");

        Assert.True(onDisk == NotificationTextsWriter.Write(RepositoryRoot),
            "A notification sentence changed. Run: dotnet run --project tools/ComParta.Contracts.TypeGen");
    }

    private static IEnumerable<string> SourceFiles()
    {
        string[] extensions = [".cs", ".ts", ".tsx", ".css", ".html", ".xaml", ".csproj"];
        string[] skipped = ["bin", "obj", "node_modules", "wwwroot", "dist"];
        foreach (var top in new[] { "src", "tests", "tools" })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(RepositoryRoot, top), "*", SearchOption.AllDirectories))
            {
                var parts = Path.GetRelativePath(RepositoryRoot, file).Split(Path.DirectorySeparatorChar);
                if (extensions.Contains(Path.GetExtension(file)) && !parts.Any(skipped.Contains))
                    yield return file;
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "ComParta.sln")))
            folder = folder.Parent;
        return folder?.FullName ?? throw new InvalidOperationException("ComParta.sln was not found above the test folder.");
    }
}

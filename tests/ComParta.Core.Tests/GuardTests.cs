using System.Text.RegularExpressions;
using ComParta.Contracts;
using ComParta.Core.UseCases;

namespace ComParta.Core.Tests;

/// <summary>Checks that keep the architecture's rules from eroding (AD-1, AD-3, AD-5).</summary>
public sealed partial class GuardTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    // Folders where these words are allowed: the engine adapter, the helper and their tests (AD-5).
    private static readonly string[] EngineFolders =
    [
        Path.Combine("src", "ComParta.Engine.MonoTorrent"),
        Path.Combine("tests", "ComParta.Engine.MonoTorrent.Tests"),
        // Drives the real engine through the whole app, so it hands it a download link of its own.
        Path.Combine("tests", "ComParta.Wiring.Tests"),
        // The helper is a torrent program on a server, run by whoever sets it up, never seen by the person (AD-23).
        Path.Combine("src", "ComParta.Helper"),
        Path.Combine("tests", "ComParta.Helper.Tests"),
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

    // The only files that start a program: the Windows adapter that opens or shows a file when the person
    // asks, and the one that opens Windows' firewall page, which is not a received file (AD-6).
    private static readonly string[] FilesStartingPrograms =
    [
        Path.Combine("src", "ComParta.App", "PlatformPorts.cs"),
        Path.Combine("src", "ComParta.Shell", "WindowsFirewall.cs"),
    ];

    // The only files that name the opener, so a notification or any other screen cannot reach it.
    private static readonly string[] FilesNamingTheOpener =
    [
        Path.Combine("src", "ComParta.Core", "Ports.cs"),
        Path.Combine("src", "ComParta.Core", "UseCases", "ReceivedUseCases.cs"),
        Path.Combine("src", "ComParta.Contracts", "BridgeRouter.cs"),
        Path.Combine("src", "ComParta.App", "PlatformPorts.cs"),
        Path.Combine("src", "ComParta.App", "AppServices.cs"),
    ];

    // Each explicit command that opens or shows a file, and how many calls it makes: received.open and
    // received.reveal, shares.reveal, shares.saveDownloadFile (shows the saved file), received.openFolder.
    private static readonly string[] OpenerCalls =
    [
        $"{Path.Combine("src", "ComParta.Core", "UseCases", "ReceivedUseCases.cs")} 2",
        $"{Path.Combine("src", "ComParta.Contracts", "BridgeRouter.cs")} 5",
    ];

    [GeneratedRegex(@"\bProcess\.Start\(|\bUseShellExecute\b")]
    private static partial Regex StartsProcess();

    [GeneratedRegex(@"\bIContentOpener\b|\bReceivedFolderOpener\b")]
    private static partial Regex NamesTheOpener();

    [GeneratedRegex(@"\b_?(?:opener|received)\.(?:OpenAsync|RevealAsync|OpenReceivedFolderAsync)\(")]
    private static partial Regex CallsTheOpener();

    [Fact]
    public void Only_the_windows_adapter_and_the_firewall_page_start_a_process()
    {
        var offences = ProductionLines()
            .Where(line => StartsProcess().IsMatch(line.Text) && !FilesStartingPrograms.Contains(line.Relative))
            .Select(line => $"{line.Relative}:{line.Number}")
            .ToList();

        Assert.True(offences.Count == 0, "A process is started outside the adapters:\n" + string.Join("\n", offences));
    }

    [Fact]
    public void Only_the_explicit_commands_reach_the_opener_so_nothing_opens_by_itself()
    {
        // Notifications, timers and the transfer engine never name the opener, so they cannot open a file.
        var namers = ProductionLines().Where(line => NamesTheOpener().IsMatch(line.Text)).Select(line => line.Relative).ToHashSet();
        Assert.True(namers.SetEquals(FilesNamingTheOpener),
            "The opener is named in: " + string.Join(", ", namers.Order()));

        var calls = ProductionLines()
            .Where(line => CallsTheOpener().IsMatch(line.Text))
            .GroupBy(line => line.Relative)
            .Select(group => $"{group.Key} {group.Count()}")
            .Order()
            .ToList();
        Assert.Equal(OpenerCalls.Order(), calls);
    }

    private static IEnumerable<(string Relative, int Number, string Text)> ProductionLines()
    {
        foreach (var file in SourceFiles().Where(file => Path.GetRelativePath(RepositoryRoot, file).StartsWith("src" + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
        {
            var relative = Path.GetRelativePath(RepositoryRoot, file);
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                yield return (relative, i + 1, lines[i]);
        }
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

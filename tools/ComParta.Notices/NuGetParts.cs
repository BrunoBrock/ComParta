using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ComParta.Notices;

/// <summary>
/// The NuGet packages a project ships: every package (direct or not) restore resolved for its target
/// framework, read from <c>obj/project.assets.json</c>, with the license each package's .nuspec declares.
/// </summary>
public static partial class NuGetParts
{
    [GeneratedRegex(@"(\.0)+$")]
    private static partial Regex TrailingZeros();

    /// <summary>True when restore resolved <paramref name="targetFramework"/> for this project.</summary>
    public static bool HasTarget(string assetsJsonPath, string targetFramework)
    {
        if (!File.Exists(assetsJsonPath))
            return false;
        using var document = JsonDocument.Parse(File.ReadAllText(assetsJsonPath));
        return Targets(document.RootElement, targetFramework).Any();
    }

    public static IReadOnlyList<Part> Read(string assetsJsonPath, string targetFramework)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(assetsJsonPath));
        var root = document.RootElement;
        var folders = root.TryGetProperty("packageFolders", out var packageFolders)
            ? packageFolders.EnumerateObject().Select(folder => folder.Name).ToList()
            : [];
        var libraries = root.GetProperty("libraries");

        var keys = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in Targets(root, targetFramework))
        {
            foreach (var package in target.EnumerateObject())
            {
                if (package.Value.TryGetProperty("type", out var type) && type.GetString() == "package")
                    keys.Add(package.Name);
            }
        }

        var parts = new List<Part>();
        foreach (var key in keys)
        {
            var (name, version) = (key[..key.IndexOf('/')], key[(key.IndexOf('/') + 1)..]);
            var nuspec = libraries.TryGetProperty(key, out var library) ? FindNuspec(library, folders) : null;
            parts.Add(nuspec is null
                ? new Part(name, version, null, null, null, PartSource.NuGet)
                : FromNuspec(name, version, nuspec));
        }
        return parts;
    }

    private static IEnumerable<JsonElement> Targets(JsonElement root, string targetFramework)
    {
        if (!root.TryGetProperty("targets", out var targets))
            yield break;
        // Keys look like "net10.0" or "net10.0-windows10.0.19041/win-x64": the framework, maybe shortened,
        // then a runtime.
        var wanted = TrailingZeros().Replace(targetFramework, "");
        foreach (var target in targets.EnumerateObject())
        {
            var framework = target.Name.Split('/')[0];
            if (string.Equals(TrailingZeros().Replace(framework, ""), wanted, StringComparison.OrdinalIgnoreCase))
                yield return target.Value;
        }
    }

    private static string? FindNuspec(JsonElement library, IReadOnlyList<string> folders)
    {
        if (!library.TryGetProperty("path", out var pathElement) || pathElement.GetString() is not { } path)
            return null;
        var file = library.TryGetProperty("files", out var files)
            ? files.EnumerateArray().Select(f => f.GetString()).FirstOrDefault(f => f is not null && f.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) && !f.Contains('/'))
            : null;
        foreach (var folder in folders)
        {
            var directory = Path.Combine(folder, path);
            var candidate = file is not null
                ? Path.Combine(directory, file)
                : Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*.nuspec").FirstOrDefault() : null;
            if (candidate is not null && File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    public static Part FromNuspec(string name, string version, string nuspecPath)
    {
        var metadata = XDocument.Load(nuspecPath).Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "metadata");
        string? Value(string element) =>
            metadata?.Elements().FirstOrDefault(e => e.Name.LocalName == element)?.Value.Trim() is { Length: > 0 } text ? text : null;

        var licenseElement = metadata?.Elements().FirstOrDefault(e => e.Name.LocalName == "license");
        // A license that is a file, or only a link, has no expression: the policy's reviewed exceptions decide.
        var license = licenseElement?.Attribute("type")?.Value == "expression" && licenseElement.Value.Trim() is { Length: > 0 } expression
            ? expression
            : null;

        var copyright = Value("copyright")
            ?? CopyrightLine.FromFiles(Directory.EnumerateFiles(Path.GetDirectoryName(nuspecPath)!));
        var project = Value("projectUrl")
            ?? metadata?.Elements().FirstOrDefault(e => e.Name.LocalName == "repository")?.Attribute("url")?.Value;
        return new Part(name, version, license, OneLine(copyright), project, PartSource.NuGet);
    }

    internal static string? OneLine(string? text) =>
        text is null ? null : string.Join(' ', text.Split((char[])['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
}

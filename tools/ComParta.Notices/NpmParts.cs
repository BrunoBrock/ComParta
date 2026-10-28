using System.Text.Json;

namespace ComParta.Notices;

/// <summary>
/// The npm packages bundled into the screen: the production packages of <c>src/ui</c>'s
/// <c>package-lock.json</c>, with the license, copyright and project each package declares.
/// </summary>
public static class NpmParts
{
    private const string Modules = "node_modules/";

    public static IReadOnlyList<Part> Read(string uiFolder)
    {
        using var lockFile = JsonDocument.Parse(File.ReadAllText(Path.Combine(uiFolder, "package-lock.json")));
        var parts = new List<Part>();
        foreach (var entry in lockFile.RootElement.GetProperty("packages").EnumerateObject())
        {
            var value = entry.Value;
            // "" is the screen itself; development packages are only used to build and test it.
            if (entry.Name.Length == 0 || IsTrue(value, "dev") || IsTrue(value, "devOptional"))
                continue;

            var name = entry.Name[(entry.Name.LastIndexOf(Modules, StringComparison.Ordinal) + Modules.Length)..];
            var version = Text(value, "version") ?? "";
            var folder = Path.Combine(uiFolder, entry.Name.Replace('/', Path.DirectorySeparatorChar));
            using var manifest = File.Exists(Path.Combine(folder, "package.json"))
                ? JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "package.json")))
                : null;
            var package = manifest?.RootElement;

            var license = Text(value, "license") ?? (package is { } p ? Field(p, "license", "type") : null);
            var copyright = (Directory.Exists(folder) ? CopyrightLine.FromFiles(Directory.EnumerateFiles(folder)) : null)
                ?? (package is { } a ? Field(a, "author", "name") : null);
            var project = package is { } h ? Text(h, "homepage") ?? Field(h, "repository", "url") : null;
            parts.Add(new Part(name, version, license, NuGetParts.OneLine(copyright), Tidy(project), PartSource.Npm));
        }
        return parts;
    }

    private static bool IsTrue(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text
            : null;

    // Fields such as "license", "author" and "repository" are either a string or an object.
    private static string? Field(JsonElement element, string property, string inner) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object
            ? Text(value, inner)
            : Text(element, property);

    private static string? Tidy(string? url) =>
        url is null ? null : url.StartsWith("git+", StringComparison.Ordinal) ? url[4..] : url;
}

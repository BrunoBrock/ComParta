using System.Text.Json;

namespace ComParta.Notices.Tests;

public sealed class NoticesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("comparta-notices-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static Part NuGet(string name, string? license, string version = "1.0.0") =>
        new(name, version, license, $"Copyright {name} authors", $"https://example.org/{name}", PartSource.NuGet);

    private static Part Npm(string name, string? license, string version = "1.0.0") =>
        new(name, version, license, $"Copyright {name} authors", $"https://example.org/{name}", PartSource.Npm);

    [Fact]
    public void Mit_and_apache_parts_pass()
    {
        Assert.Empty(LicensePolicy.Problems([NuGet("Alpha", "MIT"), NuGet("Beta", "Apache-2.0"), Npm("gamma", "ISC")]));
    }

    [Fact]
    public void A_part_with_no_license_fails_naming_it()
    {
        var problems = LicensePolicy.Problems([NuGet("Alpha", "MIT"), NuGet("Nameless", null, "2.1.0"), Npm("blank", " ")]);

        Assert.Equal(["Nameless 2.1.0 (NuGet) has no license", "blank 1.0.0 (npm) has no license"], problems);
    }

    [Theory]
    [InlineData("GPL-3.0-only")]
    [InlineData("AGPL-3.0-or-later")]
    [InlineData("LGPL-2.1-only")]
    [InlineData("SSPL-1.0")]
    [InlineData("SomethingNobodyKnows")]
    public void A_gpl_part_fails_naming_it(string license)
    {
        var problems = LicensePolicy.Problems([NuGet("Alpha", "MIT"), Npm("copyleft", license, "3.0.0")]);

        Assert.Equal([$"copyleft 3.0.0 (npm) uses {license}, which ComParta cannot ship"], problems);
    }

    [Theory]
    [InlineData("MIT OR GPL-3.0-only")]
    [InlineData("(GPL-2.0-only OR Apache-2.0)")]
    [InlineData("Apache-2.0 WITH LLVM-exception")]
    [InlineData("(MIT AND BSD-3-Clause) OR GPL-3.0-only")]
    public void An_or_choice_passes_when_one_is_allowed(string license)
    {
        Assert.True(LicensePolicy.IsAllowed(license));
    }

    [Theory]
    [InlineData("MIT AND GPL-3.0-only")]
    [InlineData("(MIT OR ISC) AND GPL-2.0-only")]
    [InlineData("MIT AND")]
    [InlineData("(MIT")]
    [InlineData("MIT ISC")]
    public void An_and_expression_fails_when_one_is_not_allowed(string license)
    {
        Assert.False(LicensePolicy.IsAllowed(license));
    }

    [Fact]
    public void A_reviewed_exception_passes_with_its_license_named()
    {
        var parts = new[] { NuGet("Microsoft.WindowsAppSDK.Foundation", null), NuGet("MonoTorrent", null, "3.0.2") }
            .Select(LicensePolicy.Resolve).ToList();

        Assert.Empty(LicensePolicy.Problems(parts));
        Assert.StartsWith("LicenseRef-Microsoft-WindowsAppSDK", parts[0].License);
        Assert.Equal("MIT", parts[1].License);
        // An exception names a package whose license is a file: it does not let a declared GPL through.
        Assert.NotEmpty(LicensePolicy.Problems([NuGet("MonoTorrent", "GPL-3.0-only")]));
    }

    [Fact]
    public void The_same_parts_give_the_same_file_in_any_order()
    {
        Part[] parts = [NuGet("Zeta", "MIT"), Npm("react", "MIT"), NuGet("alpha", "Apache-2.0"), NuGet("Zeta", "MIT", "0.9.0"), Npm("lucide-react", "ISC")];

        var first = NoticesWriter.Write(parts, NoticesWriter.StandardText);
        var second = NoticesWriter.Write(parts.Reverse().Concat(parts), NoticesWriter.StandardText);

        Assert.Equal(first, second);
        Assert.DoesNotContain("\r", first);
        Assert.EndsWith("\n", first);
        Assert.DoesNotContain("\n\n\n", first);
        Assert.True(first.IndexOf("alpha 1.0.0", StringComparison.Ordinal) < first.IndexOf("Zeta 0.9.0", StringComparison.Ordinal));
        Assert.True(first.IndexOf("Zeta 0.9.0", StringComparison.Ordinal) < first.IndexOf("Zeta 1.0.0", StringComparison.Ordinal));
    }

    [Fact]
    public void Each_part_is_written_as_name_version_license_copyright_project()
    {
        var text = NoticesWriter.Write(
            [NuGet("MonoTorrent", "MIT", "3.0.2"), Npm("lucide-react", "ISC", "1.57.0"), new Part("bare", "2.0.0", "MIT", null, null, PartSource.Npm)],
            NoticesWriter.StandardText);

        Assert.StartsWith("ComParta third-party notices\n============================\n\n", text);
        Assert.Contains("MonoTorrent 3.0.2\nLicense: MIT\nCopyright: Copyright MonoTorrent authors\nProject: https://example.org/MonoTorrent\n", text);
        Assert.Contains("lucide-react 1.57.0\nLicense: ISC\n", text);
        Assert.Contains("bare 2.0.0\nLicense: MIT\nCopyright: not stated by the package\nProject: not stated by the package\n", text);
        Assert.True(text.IndexOf("Parts in the app and the helper (NuGet)", StringComparison.Ordinal) < text.IndexOf("MonoTorrent 3.0.2", StringComparison.Ordinal));
        Assert.True(text.IndexOf("Parts in the screen (npm)", StringComparison.Ordinal) < text.IndexOf("lucide-react 1.57.0", StringComparison.Ordinal));
        // Each license's text once, at the end.
        Assert.Contains("License texts\n-------------\n\n== ISC ==\n", text);
        Assert.Single(text.Split("== MIT ==").Skip(1));
        Assert.DoesNotContain("== Apache-2.0 ==", text);
    }

    [Fact]
    public void NuGet_parts_come_from_the_runtime_target_with_their_nuspec_license()
    {
        var assets = WriteAssets("src/ComParta.Engine.MonoTorrent", "net10.0",
            ("Alpha", "1.2.3", "<license type=\"expression\">MIT</license><copyright>Alpha Ltd</copyright><projectUrl>https://alpha.example</projectUrl>"),
            ("Filed", "4.0.0", "<license type=\"file\">LICENSE.txt</license>"));
        File.WriteAllText(Path.Combine(_root, "packages", "filed", "4.0.0", "LICENSE.txt"), "The License\n\nCopyright (c) 2020 Filed people\n");

        var parts = NuGetParts.Read(assets, "net10.0");

        Assert.Equal(
            [
                new Part("Alpha", "1.2.3", "MIT", "Alpha Ltd", "https://alpha.example", PartSource.NuGet),
                new Part("Filed", "4.0.0", null, "Copyright (c) 2020 Filed people", null, PartSource.NuGet),
            ],
            parts);
        Assert.False(NuGetParts.HasTarget(assets, "net10.0-windows10.0.19041.0"));
    }

    [Theory]
    // Apache's own words and its template line name no one; the package's line further down does.
    [InlineData("Apache License\n\n      copyright notice that is included in or attached to the work\n      (c) You must retain, in the Source form of any Derivative Works\n   Copyright [yyyy] [name of copyright owner]\n", null)]
    [InlineData("Apache License\n      copyright license to reproduce\nCopyright 2024 Real people\n", "Copyright 2024 Real people")]
    [InlineData("MIT License\n\nCopyright (c) <year> <owner>\n(c) 2020 Someone\n", "(c) 2020 Someone")]
    public void The_copyright_line_is_a_line_that_names_someone_not_the_license_own_words(string license, string? expected) =>
        Assert.Equal(expected, CopyrightLine.FromText(license));

    [Fact]
    public void Npm_parts_leave_out_development_packages()
    {
        WriteUi(("react", "19.0.0", "MIT", false), ("vitest", "5.0.0", "MIT", true));

        var parts = NpmParts.Read(Path.Combine(_root, "src", "ui"));

        var part = Assert.Single(parts);
        Assert.Equal(new Part("react", "19.0.0", "MIT", "Copyright (c) react people", "https://example.org/react", PartSource.Npm), part);
    }

    [Fact]
    public void The_check_fails_when_the_committed_file_is_stale()
    {
        foreach (var (project, framework) in NoticesTool.Shipped)
            WriteAssets(project, framework, ("Alpha", "1.2.3", "<license type=\"expression\">MIT</license>"));
        WriteUi(("react", "19.0.0", "MIT", false));
        var output = new StringWriter();

        Assert.Equal(0, NoticesTool.Run(_root, check: false, withoutApp: false, output));
        Assert.Equal(0, NoticesTool.Run(_root, check: true, withoutApp: false, output));

        // Windows line endings, as git may check the file out, are not a difference.
        var committed = Path.Combine(_root, NoticesWriter.FileName);
        File.WriteAllText(committed, File.ReadAllText(committed).ReplaceLineEndings("\r\n"));
        Assert.Equal(0, NoticesTool.Run(_root, check: true, withoutApp: false, output));

        // A dependency bump changes what ships.
        WriteUi(("react", "19.1.0", "MIT", false));
        output = new StringWriter();
        Assert.Equal(1, NoticesTool.Run(_root, check: true, withoutApp: false, output));
        Assert.Contains(NoticesTool.StaleMessage, output.ToString());
        Assert.Contains("react 19.1.0", File.ReadAllText(Path.Combine(_root, "publish", NoticesWriter.FileName)));
        Assert.DoesNotContain("react 19.1.0", File.ReadAllText(committed));
    }

    [Fact]
    public void A_package_that_cannot_ship_fails_the_step_naming_it()
    {
        foreach (var (project, framework) in NoticesTool.Shipped)
            WriteAssets(project, framework, ("Copyleft.Thing", "1.0.0", "<license type=\"expression\">GPL-3.0-only</license>"));
        WriteUi(("react", "19.0.0", "MIT", false));
        var output = new StringWriter();

        Assert.Equal(1, NoticesTool.Run(_root, check: true, withoutApp: false, output));
        Assert.Contains("Copyleft.Thing 1.0.0 (NuGet) uses GPL-3.0-only, which ComParta cannot ship", output.ToString());
    }

    [Fact]
    public void Without_the_app_restored_for_windows_the_step_says_to_build_it_first()
    {
        foreach (var (project, framework) in NoticesTool.Shipped.Skip(1))
            WriteAssets(project, framework, ("Alpha", "1.2.3", "<license type=\"expression\">MIT</license>"));
        WriteUi(("react", "19.0.0", "MIT", false));
        var output = new StringWriter();

        Assert.Equal(2, NoticesTool.Run(_root, check: true, withoutApp: false, output));
        Assert.Contains("Build the app on Windows first", output.ToString());
        Assert.Equal(0, NoticesTool.Run(_root, check: false, withoutApp: true, new StringWriter()));
    }

    private string WriteAssets(string project, string framework, params (string Name, string Version, string Metadata)[] packages)
    {
        var packageFolder = Path.Combine(_root, "packages") + Path.DirectorySeparatorChar;
        var target = new Dictionary<string, object> { ["ComParta.Core/1.0.0"] = new { type = "project" } };
        var libraries = new Dictionary<string, object>();
        foreach (var (name, version, metadata) in packages)
        {
            var id = name.ToLowerInvariant();
            var folder = Path.Combine(packageFolder, id, version);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, $"{id}.nuspec"),
                $"""<?xml version="1.0"?><package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><id>{name}</id><version>{version}</version>{metadata}</metadata></package>""");
            target[$"{name}/{version}"] = new { type = "package" };
            libraries[$"{name}/{version}"] = new { type = "package", path = $"{id}/{version}", files = new[] { $"{id}.nuspec", "lib/net10.0/x.dll" } };
        }
        // Restore shortens a Windows framework's version in its key, and a runtime adds a second key.
        var key = framework.Replace("19041.0", "19041");
        var assets = new
        {
            targets = new Dictionary<string, object> { [key] = target, [$"{key}/win-x64"] = target },
            libraries,
            packageFolders = new Dictionary<string, object> { [packageFolder] = new { } },
        };
        var path = Path.Combine(_root, project, "obj", "project.assets.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(assets));
        return path;
    }

    private void WriteUi(params (string Name, string Version, string License, bool Dev)[] packages)
    {
        var ui = Path.Combine(_root, "src", "ui");
        var entries = new Dictionary<string, object> { [""] = new { name = "comparta-ui" } };
        foreach (var (name, version, license, dev) in packages)
        {
            entries[$"node_modules/{name}"] = dev ? new { version, license, dev = true } : new { version, license };
            var folder = Path.Combine(ui, "node_modules", name);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "package.json"), JsonSerializer.Serialize(new { name, version, license, homepage = $"https://example.org/{name}" }));
            File.WriteAllText(Path.Combine(folder, "LICENSE"), $"MIT License\n\nCopyright (c) {name} people\n");
        }
        File.WriteAllText(Path.Combine(ui, "package-lock.json"), JsonSerializer.Serialize(new { lockfileVersion = 3, packages = entries }));
    }
}

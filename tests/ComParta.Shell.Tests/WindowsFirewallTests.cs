namespace ComParta.Shell.Tests;

public sealed class WindowsFirewallTests
{
    private static readonly string App = Path.Combine(Path.GetTempPath(), "ComParta", "ComParta.exe");

    private static string Rule(string action, string app, string dir = "In", string active = "TRUE", string profile = "Private") =>
        $"v2.33|Action={action}|Active={active}|Dir={dir}|Protocol=6|Profile={profile}|App={app}|Name=comparta|";

    private static bool? Decide(params string[] rules) => WindowsFirewall.Decide(rules, App, path => path);

    [Fact]
    public void Nothing_about_the_app_means_Windows_has_not_asked_yet() =>
        Assert.Null(Decide(Rule("Allow", Path.Combine(Path.GetTempPath(), "other.exe"))));

    [Fact]
    public void An_allowing_rule_lets_connections_in() => Assert.True(Decide(Rule("Allow", App)));

    [Fact]
    public void Refusing_the_prompt_leaves_only_blocking_rules() =>
        Assert.False(Decide(Rule("Block", App, profile: "Private"), Rule("Block", App, profile: "Public")));

    [Fact]
    public void Allowing_one_kind_of_network_is_enough() =>
        Assert.True(Decide(Rule("Block", App, profile: "Public"), Rule("Allow", App, profile: "Private")));

    [Fact]
    public void Outgoing_and_switched_off_rules_do_not_count()
    {
        Assert.Null(Decide(Rule("Block", App, dir: "Out")));
        Assert.Null(Decide(Rule("Block", App, active: "FALSE")));
    }

    [Fact]
    public void Paths_are_compared_without_case_and_with_variables_expanded()
    {
        var rules = new[] { Rule("Allow", "%APPROOT%\\ComParta\\COMPARTA.EXE".Replace('\\', Path.DirectorySeparatorChar)) };
        var expanded = WindowsFirewall.Decide(rules, App, path => path.Replace("%APPROOT%", Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)));

        Assert.True(expanded);
    }

    [Fact]
    public void Off_Windows_the_firewall_is_not_asked() =>
        Assert.Equal(OperatingSystem.IsWindows() ? new WindowsFirewall(App).AllowsIncoming() : null, new WindowsFirewall(App).AllowsIncoming());
}

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ComParta.Core.Ports;

namespace ComParta.Core.UseCases;

/// <summary>
/// A plain-text report the person copies for whoever helps them (epic 12 story 5, CAP-22): the
/// version, the system, the language, how reachable this computer is, how many files are shared
/// and received in each state, and the last problems the app answered with. It never holds a file
/// name, a path, an invite or an address, and it is only copied when the person asks (AD-9). It is
/// written for a helper, in English whatever the screen's language, so it is the one text core
/// writes itself (an exception to AD-11).
/// </summary>
public sealed partial class SupportUseCases(
    ISettingsRepository repository,
    ISystemInfo system,
    SettingsUseCases settings,
    ShareUseCases shares,
    TransferUseCases transfers,
    ConnectionUseCases connection,
    TimeProvider? clock = null)
{
    /// <summary>How many problems the report lists; older ones are forgotten.</summary>
    public const int ProblemsKept = 20;

    private const string ProblemsKey = "recentProblems";
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Problem codes are short words with dots, such as "share.notFound"; nothing else is written down.
    [GeneratedRegex("^[a-zA-Z]+(\\.[a-zA-Z]+)*$")]
    private static partial Regex ProblemCode();

    /// <summary>Writes down a problem the app answered with, with its time, keeping the last <see cref="ProblemsKept"/>.</summary>
    public async Task RecordAsync(string code, CancellationToken ct = default)
    {
        if (code.Length > 60 || !ProblemCode().IsMatch(code))
            return;
        await _gate.WaitAsync(ct);
        try
        {
            var kept = Problems(await repository.GetAsync(ProblemsKey, ct))
                .Append($"{_clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)} {code}")
                .TakeLast(ProblemsKept);
            await repository.SetAsync(ProblemsKey, string.Join('\n', kept), ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The report, ready to copy.</summary>
    public async Task<string> ReportAsync(CancellationToken ct = default)
    {
        var chosen = await settings.GetAsync(ct);
        var report = new StringBuilder();
        void Line(string label, object value) => report.Append(label).Append(": ").Append(value).Append('\n');

        report.Append("ComParta report\n");
        Line("Made", Time(_clock.GetUtcNow().UtcDateTime));
        Line("Version", Known(system.AppVersion));
        Line("System", Known(system.SystemVersion));
        Line("Language", $"{chosen.Language ?? "same as the system"} (system {chosen.SystemLanguage})");

        report.Append('\n');
        Line("Connection", connection.Current() switch
        {
            ConnectionHealth.Good => "good",
            ConnectionHealth.Limited => "limited",
            ConnectionHealth.Blocked => "blocked",
            _ => "not known yet",
        });
        if (connection.Facts() is { } facts)
        {
            Line("Waiting for connections", YesNo(facts.Listening));
            Line("Port used by another program", YesNo(facts.ListenPortBusy));
            Line("Router opening", facts.PortMapping switch
            {
                PortMappingState.NotRequested => "not asked",
                PortMappingState.Pending => "waiting for the router",
                PortMappingState.Obtained => "opened",
                _ => "refused",
            });
            Line("Outside port", facts.ExternalPort?.ToString(CultureInfo.InvariantCulture) ?? "none");
            Line("Router's outside address", facts.ExternalAddress switch
            {
                null => "not known",
                var address when NetworkAddresses.IsPublic(address) => "public",
                _ => "shared with other homes",
            });
            Line("IPv6 address on this computer", YesNo(facts.GlobalIPv6Present));
            Line("Waiting on IPv6 too", YesNo(facts.ListeningOnIPv6));
            Line("Reached from outside in the last hour", YesNo(facts.IncomingConnectionSeenRecently));
            Line("Reached through the helper", YesNo(facts.ReachableThroughHelper));
        }
        Line("Helper computer named", YesNo(chosen.HelperAddress is not null));
        Line("Public servers named", chosen.PublicServers.Count);

        report.Append('\n');
        var shared = shares.Current();
        Line("Shared", Counts(shared.Count, shared.GroupBy(s => s.Share.Paused ? "paused" : Word(s.Status))));
        var received = transfers.List();
        Line("Received", Counts(received.Count, received.GroupBy(t => Word(t.State))));

        report.Append('\n');
        var problems = Problems(await repository.GetAsync(ProblemsKey, ct)).ToList();
        report.Append("Last problems, newest last:\n");
        if (problems.Count == 0)
            report.Append("none\n");
        foreach (var problem in problems)
        {
            var parts = problem.Split(' ', 2);
            var when = DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var time)
                ? Time(time)
                : parts[0];
            report.Append(when).Append("  ").Append(parts[1]).Append('\n');
        }
        return report.ToString();
    }

    private static IEnumerable<string> Problems(string? stored) =>
        (stored ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(line => line.Contains(' '));

    private static string Time(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";

    private static string Known(string value) => value.Length > 0 ? value : "not known";

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static string Counts(int total, IEnumerable<IGrouping<string, object>> groups)
    {
        var parts = groups.OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} {g.Count()}").ToList();
        return parts.Count == 0 ? "0" : $"{total} ({string.Join(", ", parts)})";
    }

    private static string Word<T>(T state) where T : struct, Enum =>
        string.Concat(state.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
}

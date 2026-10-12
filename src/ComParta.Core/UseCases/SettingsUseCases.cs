using System.Text.RegularExpressions;
using ComParta.Core.Ports;

namespace ComParta.Core.UseCases;

/// <param name="Language">The language the person chose, or null to follow the system.</param>
/// <param name="SystemLanguage">The language the operating system shows.</param>
/// <param name="View">How files are shown, "grid" or "list", or null for the default.</param>
/// <param name="Sort">How files are ordered, "added", "name" or "size", or null for the default.</param>
/// <param name="SendStepsSeen">How many times the guided send has run; at <see cref="SettingsUseCases.SendStepsShown"/> it stops.</param>
/// <param name="StartWithWindows">Start hidden in the notification area when the person signs in (AD-14). Off until they agree.</param>
public sealed record Settings(
    string? Language, string SystemLanguage, string? View = null, string? Sort = null, int SendStepsSeen = 0, bool StartWithWindows = false);

/// <summary>The person's choices. Later stories add fields (your name, theme).</summary>
public sealed partial class SettingsUseCases(ISettingsRepository repository, ISystemInfo system)
{
    private const string LanguageKey = "language";
    private const string ViewKey = "view";
    private const string SortKey = "sort";
    private const string SendStepsKey = "sendStepsSeen";
    private const string StartWithWindowsKey = "startWithWindows";

    /// <summary>The guided send runs this many times, or until the person opts out (UX design).</summary>
    public const int SendStepsShown = 3;
    private static readonly string[] Views = ["grid", "list"];
    private static readonly string[] Sorts = ["added", "name", "size"];

    [GeneratedRegex("^[a-z]{2,3}(-[A-Za-z0-9]{2,8})*$")]
    private static partial Regex LanguageTag();

    /// <summary>Raised after a setting changes.</summary>
    public event Action? Changed;

    public async Task<Settings> GetAsync(CancellationToken ct = default) => new(
        await repository.GetAsync(LanguageKey, ct),
        system.UiLanguage,
        await repository.GetAsync(ViewKey, ct),
        await repository.GetAsync(SortKey, ct),
        int.TryParse(await repository.GetAsync(SendStepsKey, ct), out var seen) ? Math.Clamp(seen, 0, SendStepsShown) : 0,
        await repository.GetAsync(StartWithWindowsKey, ct) == "true");

    /// <summary>
    /// Stores every choice at once, as the UI sends them all; null means the default. The UI
    /// decides which languages it has words for; anything that is not a choice it offers is refused
    /// and nothing changes.
    /// </summary>
    public async Task<Result> SetAsync(string? language, string? view, string? sort, int sendStepsSeen = 0, bool startWithWindows = false, CancellationToken ct = default)
    {
        if (language is not null && (language.Length > 35 || !LanguageTag().IsMatch(language)))
            return Result.Fail(ErrorKind.SettingsInvalid);
        if ((view is not null && !Views.Contains(view)) || (sort is not null && !Sorts.Contains(sort)))
            return Result.Fail(ErrorKind.SettingsInvalid);
        if (sendStepsSeen is < 0 or > SendStepsShown)
            return Result.Fail(ErrorKind.SettingsInvalid);
        await repository.SetAsync(LanguageKey, language, ct);
        await repository.SetAsync(ViewKey, view, ct);
        await repository.SetAsync(SortKey, sort, ct);
        await repository.SetAsync(SendStepsKey, sendStepsSeen == 0 ? null : sendStepsSeen.ToString(System.Globalization.CultureInfo.InvariantCulture), ct);
        await repository.SetAsync(StartWithWindowsKey, startWithWindows ? "true" : null, ct);
        Changed?.Invoke();
        return Result.Success;
    }
}

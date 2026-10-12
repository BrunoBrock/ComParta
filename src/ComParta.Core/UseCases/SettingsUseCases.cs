using System.Text.RegularExpressions;
using ComParta.Core.Ports;

namespace ComParta.Core.UseCases;

/// <param name="Language">The language the person chose, or null to follow the system.</param>
/// <param name="SystemLanguage">The language the operating system shows.</param>
/// <param name="View">How files are shown, "grid" or "list", or null for the default.</param>
/// <param name="Sort">How files are ordered, "added", "name" or "size", or null for the default.</param>
public sealed record Settings(string? Language, string SystemLanguage, string? View = null, string? Sort = null);

/// <summary>The person's choices. Later stories add fields (your name, start with Windows, theme).</summary>
public sealed partial class SettingsUseCases(ISettingsRepository repository, ISystemInfo system)
{
    private const string LanguageKey = "language";
    private const string ViewKey = "view";
    private const string SortKey = "sort";
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
        await repository.GetAsync(SortKey, ct));

    /// <summary>
    /// Stores every choice at once, as the UI sends them all; null means the default. The UI
    /// decides which languages it has words for; anything that is not a choice it offers is refused
    /// and nothing changes.
    /// </summary>
    public async Task<Result> SetAsync(string? language, string? view, string? sort, CancellationToken ct = default)
    {
        if (language is not null && (language.Length > 35 || !LanguageTag().IsMatch(language)))
            return Result.Fail(ErrorKind.SettingsInvalid);
        if ((view is not null && !Views.Contains(view)) || (sort is not null && !Sorts.Contains(sort)))
            return Result.Fail(ErrorKind.SettingsInvalid);
        await repository.SetAsync(LanguageKey, language, ct);
        await repository.SetAsync(ViewKey, view, ct);
        await repository.SetAsync(SortKey, sort, ct);
        Changed?.Invoke();
        return Result.Success;
    }
}

using System.Text.RegularExpressions;
using ComParta.Core.Ports;

namespace ComParta.Core.UseCases;

/// <param name="Language">The language the person chose, or null to follow the system.</param>
/// <param name="SystemLanguage">The language the operating system shows.</param>
public sealed record Settings(string? Language, string SystemLanguage);

/// <summary>The person's choices. Later stories add fields (your name, start with Windows, theme).</summary>
public sealed partial class SettingsUseCases(ISettingsRepository repository, ISystemInfo system)
{
    private const string LanguageKey = "language";

    [GeneratedRegex("^[a-z]{2,3}(-[A-Za-z0-9]{2,8})*$")]
    private static partial Regex LanguageTag();

    /// <summary>Raised after a setting changes.</summary>
    public event Action? Changed;

    public async Task<Settings> GetAsync(CancellationToken ct = default) =>
        new(await repository.GetAsync(LanguageKey, ct), system.UiLanguage);

    /// <summary>Stores the language; the UI decides which ones it has words for. Null follows the system again.</summary>
    public async Task<Result> SetLanguageAsync(string? language, CancellationToken ct = default)
    {
        if (language is not null && (language.Length > 35 || !LanguageTag().IsMatch(language)))
            return Result.Fail(ErrorKind.SettingsInvalid);
        await repository.SetAsync(LanguageKey, language, ct);
        Changed?.Invoke();
        return Result.Success;
    }
}

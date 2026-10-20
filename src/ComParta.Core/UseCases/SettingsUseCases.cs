using System.Text.RegularExpressions;
using ComParta.Core.Ports;

namespace ComParta.Core.UseCases;

/// <param name="Language">The language the person chose, or null to follow the system.</param>
/// <param name="SystemLanguage">The language the operating system shows.</param>
/// <param name="View">How files are shown, "grid" or "list", or null for the default.</param>
/// <param name="Sort">How files are ordered, "added", "name" or "size", or null for the default.</param>
/// <param name="SendStepsSeen">How many times the guided send has run; at <see cref="SettingsUseCases.SendStepsShown"/> it stops.</param>
/// <param name="StartWithWindows">Start hidden in the notification area when the person signs in (AD-14). Off until they agree.</param>
/// <param name="PublicServers">
/// Addresses of public servers the person named, which help other devices find what this computer
/// shares; empty until they add some (AD-8). Every shared file is announced to them and every invite carries them.
/// </param>
/// <param name="HelperAddress">Where the helper computer the person named takes files (AD-23); null for none.</param>
/// <param name="HelperKey">The helper's key, set exactly when <paramref name="HelperAddress"/> is.</param>
public sealed record Settings(
    string? Language, string SystemLanguage, string? View = null, string? Sort = null, int SendStepsSeen = 0, bool StartWithWindows = false,
    IReadOnlyList<string>? PublicServers = null, string? HelperAddress = null, string? HelperKey = null)
{
    public IReadOnlyList<string> PublicServers { get; init; } = PublicServers ?? [];
}

/// <summary>The person's choices. Later stories add fields (your name, theme).</summary>
/// <param name="engine">Told which public servers to use, at start and whenever they change; none in tests that do not need it.</param>
public sealed partial class SettingsUseCases(ISettingsRepository repository, ISystemInfo system, ITransferEngine? engine = null)
{
    private const string LanguageKey = "language";
    private const string ViewKey = "view";
    private const string SortKey = "sort";
    private const string SendStepsKey = "sendStepsSeen";
    private const string StartWithWindowsKey = "startWithWindows";
    private const string PublicServersKey = "publicServers";
    private const string HelperAddressKey = "helperAddress";
    private const string HelperKeyKey = "helperKey";

    /// <summary>The shortest helper key the helper accepts (its MinimumKeyLength).</summary>
    public const int MinHelperKeyLength = 16;
    private const int MaxHelperKeyLength = 200;
    private static readonly string[] HelperSchemes = ["http", "https"];

    /// <summary>At most this many public servers, so a pasted list cannot slow every share down.</summary>
    public const int MaxPublicServers = 20;
    private const int MaxServerLength = 300;
    private static readonly string[] ServerSchemes = ["http", "https", "udp"];

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
        await repository.GetAsync(StartWithWindowsKey, ct) == "true",
        ReadServers(await repository.GetAsync(PublicServersKey, ct)),
        await repository.GetAsync(HelperAddressKey, ct),
        await repository.GetAsync(HelperKeyKey, ct));

    /// <summary>Hands the stored public servers to the engine; call after it starts and before anything is shared.</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (engine is null)
            return;
        await engine.SetPublicServersAsync(ToUris(ReadServers(await repository.GetAsync(PublicServersKey, ct))), ct);
        await engine.SetHelperAsync(
            ReadHelper(await repository.GetAsync(HelperAddressKey, ct), await repository.GetAsync(HelperKeyKey, ct)), ct);
    }

    /// <summary>
    /// Stores every choice at once, as the UI sends them all; null means the default. The UI
    /// decides which languages it has words for; anything that is not a choice it offers is refused
    /// and nothing changes.
    /// </summary>
    public async Task<Result> SetAsync(
        string? language, string? view, string? sort, int sendStepsSeen = 0, bool startWithWindows = false,
        IReadOnlyList<string>? publicServers = null, string? helperAddress = null, string? helperKey = null, CancellationToken ct = default)
    {
        if (language is not null && (language.Length > 35 || !LanguageTag().IsMatch(language)))
            return Result.Fail(ErrorKind.SettingsInvalid);
        if ((view is not null && !Views.Contains(view)) || (sort is not null && !Sorts.Contains(sort)))
            return Result.Fail(ErrorKind.SettingsInvalid);
        if (sendStepsSeen is < 0 or > SendStepsShown)
            return Result.Fail(ErrorKind.SettingsInvalid);
        if (NormalizeServers(publicServers ?? []) is not { } servers)
            return Result.Fail(ErrorKind.SettingsInvalid);
        helperAddress = string.IsNullOrWhiteSpace(helperAddress) ? null : helperAddress.Trim();
        helperKey = string.IsNullOrWhiteSpace(helperKey) ? null : helperKey.Trim();
        var helper = ReadHelper(helperAddress, helperKey);
        if ((helperAddress is not null || helperKey is not null) && helper is null)
            return Result.Fail(ErrorKind.SettingsInvalid);
        var serversBefore = ReadServers(await repository.GetAsync(PublicServersKey, ct));
        var helperBefore = ReadHelper(await repository.GetAsync(HelperAddressKey, ct), await repository.GetAsync(HelperKeyKey, ct));
        await repository.SetAsync(LanguageKey, language, ct);
        await repository.SetAsync(ViewKey, view, ct);
        await repository.SetAsync(SortKey, sort, ct);
        await repository.SetAsync(SendStepsKey, sendStepsSeen == 0 ? null : sendStepsSeen.ToString(System.Globalization.CultureInfo.InvariantCulture), ct);
        await repository.SetAsync(StartWithWindowsKey, startWithWindows ? "true" : null, ct);
        await repository.SetAsync(PublicServersKey, servers.Count == 0 ? null : string.Join('\n', servers), ct);
        await repository.SetAsync(HelperAddressKey, helperAddress, ct);
        await repository.SetAsync(HelperKeyKey, helperKey, ct);
        if (engine is not null && !servers.SequenceEqual(serversBefore))
            await engine.SetPublicServersAsync(ToUris(servers), ct);
        if (engine is not null && helper != helperBefore)
            await engine.SetHelperAsync(helper, ct);
        Changed?.Invoke();
        return Result.Success;
    }

    /// <summary>
    /// The servers without blank lines or repeats, or null when one is not an internet address the
    /// engine can use (http, https or udp, with a server name) or there are too many.
    /// </summary>
    internal static IReadOnlyList<string>? NormalizeServers(IEnumerable<string?> servers)
    {
        var kept = new List<string>();
        foreach (var raw in servers)
        {
            var text = raw?.Trim();
            if (string.IsNullOrEmpty(text))
                continue;
            if (text.Length > MaxServerLength
                || text.Any(char.IsControl)
                || !Uri.TryCreate(text, UriKind.Absolute, out var uri)
                || !ServerSchemes.Contains(uri.Scheme)
                || string.IsNullOrEmpty(uri.Host)
                || !string.IsNullOrEmpty(uri.UserInfo)
                || (uri.Scheme == "udp" && uri.IsDefaultPort))
                return null;
            if (!kept.Contains(text, StringComparer.OrdinalIgnoreCase))
                kept.Add(text);
        }
        return kept.Count > MaxPublicServers ? null : kept;
    }

    /// <summary>
    /// The helper computer, or null when either part is missing or not usable: an http or https
    /// address with a server name and no sign-in, and a key of printable characters long enough
    /// that the helper takes it.
    /// </summary>
    internal static HelperComputer? ReadHelper(string? address, string? key)
    {
        if (address is null || key is null
            || address.Length > MaxServerLength || address.Any(char.IsControl)
            || key.Length is < MinHelperKeyLength or > MaxHelperKeyLength || key.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))
            || !Uri.TryCreate(address, UriKind.Absolute, out var uri)
            || !HelperSchemes.Contains(uri.Scheme)
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo))
            return null;
        return new HelperComputer(uri, key);
    }

    private static IReadOnlyList<string> ReadServers(string? stored) =>
        NormalizeServers((stored ?? "").Split('\n')) ?? [];

    private static IReadOnlyList<Uri> ToUris(IReadOnlyList<string> servers) => [.. servers.Select(s => new Uri(s))];
}

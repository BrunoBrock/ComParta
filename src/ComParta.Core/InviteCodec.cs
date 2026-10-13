using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ComParta.Core;

/// <summary>
/// What an invite says about the file before anything is received. Shown to the person labelled
/// as "from the invite"; never used as a name on disk (AD-16).
/// </summary>
public sealed record InviteHints(string? SenderName, string? Name, long? SizeBytes, int? ItemCount)
{
    public static InviteHints None { get; } = new(null, null, null, null);
}

/// <summary>An invite read from pasted text: the engine's opaque text and the display hints.</summary>
public sealed record Invite(string EngineText, InviteHints Hints, bool IsLink);

/// <summary>
/// Reads the ComParta links of earlier builds (AD-16): <c>comparta://receive/v1/&lt;payload&gt;</c>,
/// where the payload is base64url of a small JSON object holding the engine's text and the display
/// hints. The app no longer hands these out: an invite is now the engine's own link, which other
/// apps open too (AD-16, amended). Core never looks inside the engine text.
/// </summary>
public static partial class InviteCodec
{
    public const string Prefix = "comparta://receive/v1/";

    private const int MaxHintLength = 200;
    private const int MaxPayloadLength = 16 * 1024;

    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed record Payload(
        [property: JsonPropertyName("e")] string? EngineText,
        [property: JsonPropertyName("s")] string? SenderName,
        [property: JsonPropertyName("n")] string? Name,
        [property: JsonPropertyName("b")] long? SizeBytes,
        [property: JsonPropertyName("c")] int? ItemCount);

    [GeneratedRegex(@"comparta://receive/v1/([A-Za-z0-9_-]*)", RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();

    /// <summary>Writes the ComParta link for an engine invite and its display hints.</summary>
    public static string Write(string engineText, InviteHints hints)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engineText);
        var payload = new Payload(
            engineText, Clean(hints.SenderName), Clean(hints.Name), hints.SizeBytes, hints.ItemCount);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
        return Prefix + Base64Url(bytes);
    }

    /// <summary>The first ComParta link inside any text, such as a whole chat message, or null.</summary>
    public static string? FindLink(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        var match = LinkPattern().Match(text);
        return match.Success ? Prefix + match.Groups[1].Value : null;
    }

    /// <summary>
    /// Reads pasted text. A ComParta link anywhere in it wins; text with no link is passed on whole
    /// as engine text (invites from the first build, and download links, AD-21), for the engine to
    /// accept or refuse. A link whose payload is broken is refused here.
    /// </summary>
    public static bool TryRead(string? text, out Invite invite)
    {
        invite = null!;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var match = LinkPattern().Match(text);
        if (!match.Success)
        {
            invite = new Invite(text.Trim(), InviteHints.None, IsLink: false);
            return true;
        }

        var encoded = match.Groups[1].Value;
        if (encoded.Length == 0 || encoded.Length > MaxPayloadLength)
            return false;
        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(FromBase64Url(encoded), Json);
            if (payload is null || string.IsNullOrWhiteSpace(payload.EngineText))
                return false;
            var hints = new InviteHints(
                Clean(payload.SenderName),
                Clean(payload.Name),
                payload.SizeBytes is >= 0 ? payload.SizeBytes : null,
                payload.ItemCount is > 0 ? payload.ItemCount : null);
            invite = new Invite(payload.EngineText.Trim(), hints, IsLink: true);
            return true;
        }
        catch (Exception e) when (e is FormatException or JsonException or DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>Display text only: no control characters, trimmed, and short.</summary>
    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (!char.IsControl(c))
                builder.Append(c);
        }
        var cleaned = builder.ToString().Trim();
        if (cleaned.Length > MaxHintLength)
            cleaned = cleaned[..MaxHintLength];
        return cleaned.Length == 0 ? null : cleaned;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var base64 = text.Replace('-', '+').Replace('_', '/');
        base64 += (base64.Length % 4) switch { 2 => "==", 3 => "=", 0 => "", _ => throw new FormatException() };
        return Convert.FromBase64String(base64);
    }
}

using ComParta.Core.Ports;

namespace ComParta.Core.UseCases;

/// <summary>What Settings, About shows besides the version: the licenses of the parts ComParta uses (epic 5 story 1).</summary>
public sealed class AboutUseCases(IThirdPartyNotices notices)
{
    /// <summary>The notices with plain line endings, or null when the shipped file is missing or empty.</summary>
    public async Task<string?> NoticesAsync(CancellationToken ct = default)
    {
        var text = await notices.ReadAsync(ct);
        return string.IsNullOrWhiteSpace(text) ? null : text.ReplaceLineEndings("\n");
    }
}

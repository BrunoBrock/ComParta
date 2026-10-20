using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ComParta.Core;

namespace ComParta.Engine.MonoTorrent;

/// <summary>What a helper says about itself: its public servers and where it serves files.</summary>
internal sealed record HelperAbout(IReadOnlyList<Uri> Trackers, IPEndPoint? Peer);

/// <summary>
/// Talks to a helper computer (AD-23): the only code that does. Every request carries the key; any
/// failure (no answer, a refusal) is reported as false or null, never thrown.
/// </summary>
internal sealed class HelperClient(HelperComputer helper)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public HelperComputer Helper { get; } = helper;

    /// <summary>The helper's public servers and address, or null when it cannot be reached or refuses the key.</summary>
    public async Task<HelperAbout?> AboutAsync(CancellationToken ct)
    {
        try
        {
            using var response = await Http.SendAsync(Request(HttpMethod.Get, "about"), ct);
            if (!response.IsSuccessStatusCode)
                return null;
            var about = await response.Content.ReadFromJsonAsync<AboutJson>(ct);
            if (about?.Trackers is not { } trackers)
                return null;
            var uris = trackers
                .Select(t => Uri.TryCreate(t, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "udp" ? uri : null)
                .OfType<Uri>()
                .Take(4)
                .ToList();
            var peer = IPEndPoint.TryParse(about.Peer ?? "", out var endPoint) && endPoint.Port > 0 ? endPoint : null;
            return uris.Count == 0 ? null : new HelperAbout(uris, peer);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Hands the helper a shared file's description. True once the helper holds it.</summary>
    public async Task<bool> AddAsync(byte[] description, CancellationToken ct)
    {
        try
        {
            var request = Request(HttpMethod.Put, "files");
            request.Content = new ByteArrayContent(description);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-bittorrent");
            using var response = await Http.SendAsync(request, ct);
            return response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>Tells the helper to drop a file. True when it is gone there, or was never there.</summary>
    public async Task<bool> RemoveAsync(string id, CancellationToken ct)
    {
        try
        {
            using var response = await Http.SendAsync(Request(HttpMethod.Delete, "files/" + Uri.EscapeDataString(id)), ct);
            return response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var baseUri = Helper.Address.AbsoluteUri.EndsWith('/') ? Helper.Address : new Uri(Helper.Address.AbsoluteUri + "/");
        var request = new HttpRequestMessage(method, new Uri(baseUri, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Helper.Key);
        return request;
    }

    private sealed record AboutJson(string[]? Trackers, string? Peer);
}

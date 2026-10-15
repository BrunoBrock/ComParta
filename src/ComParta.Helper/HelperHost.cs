using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using MonoTorrent.TrackerServer;

namespace ComParta.Helper;

/// <summary>
/// A running helper (AD-23): a public server (tracker) on HTTP and UDP, a peer that serves the files
/// it holds, and a control port where ComParta installs holding the key add and remove files.
/// </summary>
public sealed class HelperHost : IAsyncDisposable
{
    /// <summary>The biggest file description accepted; a description of a few gigabytes is far smaller.</summary>
    internal const int MaxDescriptionBytes = 4 * 1024 * 1024;

    private readonly WebApplication _web;
    private readonly TrackerServer _tracker;
    private readonly CheckedUdpTrackerListener _udp;
    private readonly HeldFiles _files;

    private HelperHost(HelperOptions options, WebApplication web, TrackerServer tracker, CheckedUdpTrackerListener udp, HeldFiles files)
    {
        Options = options;
        _web = web;
        _tracker = tracker;
        _udp = udp;
        _files = files;
    }

    public HelperOptions Options { get; }

    /// <summary>The public server's address as it goes into a link (<c>tr=</c>).</summary>
    public Uri TrackerUri => new($"http://{Host}:{Options.TrackerPort}/announce");

    /// <summary>The same public server on UDP.</summary>
    public Uri UdpTrackerUri => new($"udp://{Host}:{Options.TrackerPort}");

    /// <summary>Where the files are served (<c>x.pe=</c> in a link).</summary>
    public IPEndPoint PeerEndPoint => new(Options.Address, Options.PeerPort);

    /// <summary>Where ComParta adds and removes files.</summary>
    public Uri ControlUri => new($"{(Options.UsesCertificate ? "https" : "http")}://{Host}:{Options.ControlPort}/");

    /// <summary>The files held now.</summary>
    public IReadOnlyList<HeldFile> Files => _files.List();

    /// <summary>For tests: the public server, to see who it knows.</summary>
    internal TrackerServer Tracker => _tracker;

    private string Host => Options.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
        ? $"[{Options.PublicAddress}]"
        : Options.PublicAddress;

    /// <summary>Starts a helper with settings that <see cref="HelperOptions.Problems"/> found nothing wrong with.</summary>
    /// <param name="baseFolder">What a relative <see cref="HelperOptions.DataFolder"/> is relative to.</param>
    public static async Task<HelperHost> StartAsync(HelperOptions options, string baseFolder, CancellationToken ct = default)
    {
        if (options.Problems() is { Count: > 0 } problems)
            throw new ArgumentException(string.Join(" ", problems), nameof(options));

        var tracker = new TrackerServer
        {
            // Only files a ComParta install handed over: the helper is not a public server for anything else.
            AllowUnregisteredTorrents = false,
            AnnounceInterval = TimeSpan.FromMinutes(5),
            MinAnnounceInterval = TimeSpan.FromMinutes(1),
        };
        var web = new WebTrackerListener();
        tracker.RegisterListener(web);
        var udp = new CheckedUdpTrackerListener(new IPEndPoint(IPAddress.Any, options.TrackerPort));
        tracker.RegisterListener(udp);

        var files = new HeldFiles(options, tracker, Path.GetFullPath(options.DataFolder, baseFolder));
        var app = BuildWeb(options, web, files);
        var host = new HelperHost(options, app, tracker, udp, files);
        try
        {
            web.Start();
            udp.Start();
            await app.StartAsync(ct);
            await files.StartAsync();
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    private static WebApplication BuildWeb(HelperOptions options, WebTrackerListener trackerListener, HeldFiles files)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Limits.MaxRequestBodySize = MaxDescriptionBytes;
            kestrel.ListenAnyIP(options.TrackerPort);
            kestrel.ListenAnyIP(options.ControlPort, listen =>
            {
                if (options.UsesCertificate)
                    listen.UseHttps(X509Certificate2.CreateFromPemFile(options.CertificatePath, options.CertificateKeyPath));
            });
        });
        var app = builder.Build();

        // Each port answers only its own requests: the tracker never takes a file, the control port never announces.
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            var trackerPath = path == "/announce" || path == "/scrape";
            if ((context.Connection.LocalPort == options.TrackerPort) != trackerPath)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            await next(context);
        });

        app.MapGet("/announce", context => AnswerTrackerAsync(context, trackerListener, isScrape: false));
        app.MapGet("/scrape", context => AnswerTrackerAsync(context, trackerListener, isScrape: true));

        var key = Encoding.UTF8.GetBytes(options.Key);
        app.MapPut("/files", async context =>
        {
            if (!HoldsKey(context, key))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            using var body = new MemoryStream();
            await context.Request.Body.CopyToAsync(body, context.RequestAborted);
            var (result, id) = await files.AddAsync(body.ToArray());
            context.Response.StatusCode = result switch
            {
                AddResult.Added => StatusCodes.Status201Created,
                AddResult.AlreadyHeld => StatusCodes.Status200OK,
                AddResult.TooLarge => StatusCodes.Status413PayloadTooLarge,
                _ => StatusCodes.Status400BadRequest,
            };
            if (id is not null && result is AddResult.Added or AddResult.AlreadyHeld)
                await context.Response.WriteAsJsonAsync(new AddedFile(id), HelperJson.Default.AddedFile);
        });
        app.MapDelete("/files/{id}", async (HttpContext context, string id) =>
        {
            if (!HoldsKey(context, key))
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            else
                context.Response.StatusCode = await files.RemoveAsync(id.ToLowerInvariant())
                    ? StatusCodes.Status204NoContent
                    : StatusCodes.Status404NotFound;
        });
        app.MapGet("/files", async context =>
        {
            if (!HoldsKey(context, key))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            await context.Response.WriteAsJsonAsync(files.List(), HelperJson.Default.IReadOnlyListHeldFile);
        });
        return app;
    }

    private static async Task AnswerTrackerAsync(HttpContext context, WebTrackerListener listener, bool isScrape)
    {
        var remote = AnnounceRules.Plain(context.Connection.RemoteIpAddress ?? IPAddress.None);
        // The raw query, as torrent apps encode it: the content's fingerprint is bytes, not text.
        var answer = listener.Handle(context.Request.QueryString.Value ?? "", remote, isScrape);
        context.Response.ContentType = "text/plain";
        await context.Response.Body.WriteAsync(answer.Encode(), context.RequestAborted);
    }

    /// <summary>The request carries the helper key (<c>Authorization: Bearer ...</c>), compared in constant time.</summary>
    private static bool HoldsKey(HttpContext context, byte[] key)
    {
        var header = context.Request.Headers.Authorization.ToString();
        const string scheme = "Bearer ";
        if (!header.StartsWith(scheme, StringComparison.Ordinal))
            return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(header[scheme.Length..]), key);
    }

    /// <summary>Waits until the program is asked to stop (Ctrl+C, or the service manager).</summary>
    public Task WaitForShutdownAsync(CancellationToken ct = default) => _web.WaitForShutdownAsync(ct);

    public async ValueTask DisposeAsync()
    {
        await _files.DisposeAsync();
        try { await _web.StopAsync(); }
        catch (Exception) { /* shutting down anyway */ }
        await _web.DisposeAsync();
        _udp.Stop();
        _tracker.Dispose();
    }
}

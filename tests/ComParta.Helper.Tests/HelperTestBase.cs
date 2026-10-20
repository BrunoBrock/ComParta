using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using MonoTorrent;

namespace ComParta.Helper.Tests;

/// <summary>Starts real helpers on this machine and cleans up after them.</summary>
public abstract class HelperTestBase : IAsyncLifetime
{
    protected const string Key = "a-helper-key-for-tests";
    protected static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    protected readonly string Folder = Path.Combine(Path.GetTempPath(), "comparta-helper-tests", Guid.NewGuid().ToString("N"));
    private readonly List<HelperHost> _helpers = [];
    protected readonly HttpClient Http = new();

    public virtual Task InitializeAsync() => Task.CompletedTask;

    public virtual async Task DisposeAsync()
    {
        foreach (var helper in _helpers)
            await helper.DisposeAsync();
        Http.Dispose();
        try { Directory.Delete(Folder, recursive: true); }
        catch (IOException) { }
    }

    protected HelperOptions Options(Action<HelperOptions>? change = null)
    {
        var options = new HelperOptions
        {
            PublicAddress = "127.0.0.1",
            Key = Key,
            PeerPort = FreeTcpPort(),
            TrackerPort = FreeTcpAndUdpPort(),
            ControlPort = FreeTcpPort(),
            DataFolder = Path.Combine(Folder, "helper"),
        };
        change?.Invoke(options);
        return options;
    }

    protected async Task<HelperHost> StartHelperAsync(HelperOptions? options = null)
    {
        var helper = await HelperHost.StartAsync(options ?? Options(), Folder);
        _helpers.Add(helper);
        return helper;
    }

    protected async Task StopHelperAsync(HelperHost helper)
    {
        _helpers.Remove(helper);
        await helper.DisposeAsync();
    }

    protected Task<HttpResponseMessage> AddAsync(HelperHost helper, byte[] description, string? key = Key)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, new Uri(helper.ControlUri, "files"))
        {
            Content = new ByteArrayContent(description),
        };
        if (key is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return Http.SendAsync(request);
    }

    protected Task<HttpResponseMessage> RemoveAsync(HelperHost helper, string id, string? key = Key)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(helper.ControlUri, "files/" + id));
        if (key is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return Http.SendAsync(request);
    }

    /// <summary>Waits until the helper's own public server lists the helper for this file.</summary>
    protected static async Task WaitUntilTheHelperIsListedAsync(HelperHost helper, InfoHashes hashes)
    {
        var until = DateTime.UtcNow + Timeout;
        while (!helper.Tracker.GetTrackerItems().Any(item => item.Trackable.InfoHash == hashes.V1OrV2 && item.Count > 0))
        {
            Assert.True(DateTime.UtcNow < until, "The helper never announced the file on its own server.");
            await Task.Delay(100);
        }
    }

    protected string CreateFile(string name, int sizeBytes)
    {
        var folder = Path.Combine(Folder, "originals");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, RandomNumberGenerator.GetBytes(sizeBytes));
        return path;
    }

    /// <summary>A description of one file, as ComParta writes it.</summary>
    protected static async Task<byte[]> DescribeAsync(string path)
    {
        var creator = new TorrentCreator(TorrentType.V1Only) { CreatedBy = "ComParta", Private = false };
        return (await creator.CreateAsync(new TorrentFileSource(path))).Encode();
    }

    protected static string Checksum(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    protected static int FreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>
    /// A port free on both TCP and UDP, as the public server takes both. Tried across a range rather
    /// than taken from the system's own TCP choices: Windows reserves blocks of ports for UDP that
    /// those choices can fall in, and then every attempt is refused.
    /// </summary>
    protected static int FreeTcpAndUdpPort()
    {
        for (var attempt = 0; ; attempt++)
        {
            var port = Random.Shared.Next(20_000, 45_000);
            try
            {
                using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
                var tcp = new TcpListener(IPAddress.Any, port);
                tcp.Start();
                tcp.Stop();
                return port;
            }
            catch (SocketException) when (attempt < 500)
            {
            }
        }
    }
}

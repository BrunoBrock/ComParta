using ComParta.Core;
using ComParta.Core.Ports;
using ComParta.Core.Tests;
using ComParta.Core.UseCases;
using ComParta.Engine.MonoTorrent;
using ComParta.Shell;
using ComParta.Storage.Sqlite;

namespace ComParta.Wiring.Tests;

/// <summary>Installing the package keeps what the pilot build had (epic 5 story 5).</summary>
public sealed class UpgradeTests : IAsyncLifetime
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "comparta-tests", Guid.NewGuid().ToString("N"));

    // The app's own layout (AppPaths): the database and the engine's folder at the top.
    private sealed record Paths(string Root) : IAppPaths
    {
        public string DatabasePath => Path.Combine(Root, "comparta.db");
        public string EngineRoot => Path.Combine(Root, "engine");
        public string ReceivedRoot => Path.Combine(Root, "Received");
        public string LogsRoot => Path.Combine(Root, "logs");
        public string SavedRoot => Path.Combine(Root, "Downloads");
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task The_packaged_install_starts_with_the_pilots_shares_settings_and_port()
    {
        var pilot = new Paths(Path.Combine(_folder, "pilot"));
        var packaged = new Paths(Path.Combine(_folder, "packaged"));
        var share = new Share(ShareId.New(), Path.Combine(_folder, "family video.mp4"), "family video.mp4", 5_000, DateTimeOffset.UtcNow);
        var pilotStore = await SqliteStore.OpenAsync(pilot.DatabasePath);
        await pilotStore.AddAsync(share);
        await new SettingsUseCases(pilotStore, new FakeSystem("pt-BR")).SetAsync("pt-BR", "list", null, yourName: "Ana");
        int port;
        await using (var engine = new MonoTorrentTransferEngine(pilot))
        {
            await engine.StartAsync(SharingPolicy.Closed);
            port = engine.ListenEndPoint!.Port;
        }

        Assert.True(PilotData.Import(pilot.Root, packaged.Root, SqliteStore.CopyDatabase));

        var store = await SqliteStore.OpenAsync(packaged.DatabasePath);
        Assert.Equal([share], await ((IShareRepository)store).GetAllAsync());
        var settings = await new SettingsUseCases(store, new FakeSystem("en")).GetAsync();
        Assert.Equal(("pt-BR", "list", "Ana"), (settings.Language, settings.View, settings.YourName));
        // Invites given out before keep working: the engine listens on the same port.
        await using var installed = new MonoTorrentTransferEngine(packaged);
        await installed.StartAsync(SharingPolicy.Closed);
        Assert.Equal(port, installed.ListenEndPoint!.Port);
    }
}

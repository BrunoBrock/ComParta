using ComParta.Core;
using ComParta.Core.Ports;
using Microsoft.Data.Sqlite;

namespace ComParta.Storage.Sqlite.Tests;

public sealed class SqliteStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "comparta-tests", Guid.NewGuid().ToString("N"));
    private string DatabasePath => Path.Combine(_folder, "data", "comparta.db");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task A_saved_share_is_read_back_by_a_new_instance()
    {
        var share = new Share(ShareId.New(), @"C:\Videos\birthday.mp4", "birthday.mp4", 123_456_789_012, DateTimeOffset.UtcNow);
        IShareRepository first = await SqliteStore.OpenAsync(DatabasePath);
        await first.AddAsync(share);

        IShareRepository second = await SqliteStore.OpenAsync(DatabasePath);

        Assert.Equal([share], await second.GetAllAsync());
    }

    [Fact]
    public async Task A_removed_share_is_not_read_back()
    {
        var kept = new Share(ShareId.New(), @"C:\a.txt", "a.txt", 1, DateTimeOffset.UtcNow);
        var removed = new Share(ShareId.New(), @"C:\b.txt", "b.txt", 2, DateTimeOffset.UtcNow.AddSeconds(1));
        IShareRepository store = await SqliteStore.OpenAsync(DatabasePath);
        await store.AddAsync(kept);
        await store.AddAsync(removed);

        await store.RemoveAsync(removed.Id);

        IShareRepository reopened = await SqliteStore.OpenAsync(DatabasePath);
        Assert.Equal([kept], await reopened.GetAllAsync());
    }

    [Fact]
    public async Task A_transfer_is_saved_updated_and_removed()
    {
        var transfer = new Transfer(TransferId.New(), null, null, false, DateTimeOffset.UtcNow);
        ITransferRepository store = await SqliteStore.OpenAsync(DatabasePath);
        await store.AddAsync(transfer);
        Assert.Equal([transfer], await store.GetAllAsync());

        var finished = transfer with { Name = "album.zip", SizeBytes = 42, Done = true };
        await store.UpdateAsync(finished);
        ITransferRepository reopened = await SqliteStore.OpenAsync(DatabasePath);
        Assert.Equal([finished], await reopened.GetAllAsync());

        await reopened.RemoveAsync(transfer.Id);
        Assert.Empty(await reopened.GetAllAsync());
    }

    [Fact]
    public async Task A_paused_transfer_is_read_back_paused()
    {
        var transfer = new Transfer(TransferId.New(), "film.mkv", 10, false, DateTimeOffset.UtcNow, Paused: true);
        ITransferRepository store = await SqliteStore.OpenAsync(DatabasePath);
        await store.AddAsync(transfer);

        ITransferRepository reopened = await SqliteStore.OpenAsync(DatabasePath);
        Assert.Equal([transfer], await reopened.GetAllAsync());
        await reopened.UpdateAsync(transfer with { Paused = false });
        Assert.False((await reopened.GetAllAsync()).Single().Paused);
    }

    [Fact]
    public async Task A_database_from_the_first_version_keeps_its_transfers()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        var id = TransferId.New();
        await using (var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = $"""
                CREATE TABLE shares (id TEXT PRIMARY KEY, file_path TEXT NOT NULL, name TEXT NOT NULL,
                    size_bytes INTEGER NOT NULL, added_at TEXT NOT NULL);
                CREATE TABLE transfers (id TEXT PRIMARY KEY, name TEXT NULL, size_bytes INTEGER NULL,
                    done INTEGER NOT NULL, added_at TEXT NOT NULL);
                INSERT INTO transfers VALUES ('{id}', 'album.zip', 42, 0, '2026-10-01T10:00:00.0000000+00:00');
                PRAGMA user_version = 1;
                """;
            await command.ExecuteNonQueryAsync();
        }

        ITransferRepository store = await SqliteStore.OpenAsync(DatabasePath);

        var transfer = Assert.Single(await store.GetAllAsync());
        Assert.Equal(id, transfer.Id);
        Assert.Equal("album.zip", transfer.Name);
        Assert.False(transfer.Paused);
    }

    [Fact]
    public async Task A_setting_survives_a_new_instance_and_can_be_removed()
    {
        ISettingsRepository store = await SqliteStore.OpenAsync(DatabasePath);
        Assert.Null(await store.GetAsync("language"));
        await store.SetAsync("language", "pt-BR");
        await store.SetAsync("language", "en");

        ISettingsRepository reopened = await SqliteStore.OpenAsync(DatabasePath);
        Assert.Equal("en", await reopened.GetAsync("language"));
        await reopened.SetAsync("language", null);
        Assert.Null(await reopened.GetAsync("language"));
    }

    [Fact]
    public async Task A_database_from_the_second_version_gains_settings_and_keeps_its_transfers()
    {
        var transfer = new Transfer(TransferId.New(), "film.mkv", 10, false, DateTimeOffset.UtcNow, Paused: true);
        ITransferRepository first = await SqliteStore.OpenAsync(DatabasePath);
        await first.AddAsync(transfer);
        await using (var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE settings; ALTER TABLE transfers DROP COLUMN source; PRAGMA user_version = 2;";
            await command.ExecuteNonQueryAsync();
        }

        var store = await SqliteStore.OpenAsync(DatabasePath);

        Assert.Equal([transfer], await ((ITransferRepository)store).GetAllAsync());
        await ((ISettingsRepository)store).SetAsync("language", "pt-BR");
        Assert.Equal("pt-BR", await ((ISettingsRepository)store).GetAsync("language"));
    }

    [Fact]
    public async Task Where_a_transfer_came_from_is_read_back()
    {
        var link = new Transfer(TransferId.New(), "film.mkv", 10, false, DateTimeOffset.UtcNow, Source: TransferSource.DownloadLink);
        var file = new Transfer(TransferId.New(), "album.zip", 20, true, DateTimeOffset.UtcNow.AddMinutes(1), Source: TransferSource.DownloadFile);
        ITransferRepository store = await SqliteStore.OpenAsync(DatabasePath);
        await store.AddAsync(link);
        await store.AddAsync(file);

        ITransferRepository reopened = await SqliteStore.OpenAsync(DatabasePath);

        Assert.Equal([link, file], await reopened.GetAllAsync());
    }

    [Fact]
    public async Task Transfers_from_the_third_version_count_as_invites()
    {
        var transfer = new Transfer(TransferId.New(), "film.mkv", 10, false, DateTimeOffset.UtcNow, Source: TransferSource.DownloadFile);
        ITransferRepository first = await SqliteStore.OpenAsync(DatabasePath);
        await first.AddAsync(transfer);
        await using (var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE transfers DROP COLUMN source; PRAGMA user_version = 3;";
            await command.ExecuteNonQueryAsync();
        }

        ITransferRepository store = await SqliteStore.OpenAsync(DatabasePath);

        Assert.Equal(TransferSource.Invite, Assert.Single(await store.GetAllAsync()).Source);
    }
}

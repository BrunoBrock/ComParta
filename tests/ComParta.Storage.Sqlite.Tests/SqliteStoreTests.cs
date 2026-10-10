using ComParta.Core;
using ComParta.Core.Ports;

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
}

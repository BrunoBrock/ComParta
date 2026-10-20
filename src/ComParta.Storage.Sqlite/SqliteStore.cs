using System.Globalization;
using ComParta.Core;
using ComParta.Core.Ports;
using Microsoft.Data.Sqlite;

namespace ComParta.Storage.Sqlite;

/// <summary>
/// The product database (AD-10). Created and upgraded when opened; the only writer of this file.
/// </summary>
public sealed class SqliteStore : IShareRepository, ITransferRepository, ISettingsRepository
{
    private const int SchemaVersion = 6;
    private readonly string _connectionString;

    private SqliteStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
    }

    public static async Task<SqliteStore> OpenAsync(string databasePath, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        var store = new SqliteStore(databasePath);
        await store.UpgradeAsync(ct);
        return store;
    }

    private async Task UpgradeAsync(CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        var version = Convert.ToInt32(await Command(connection, "PRAGMA user_version").ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        if (version >= SchemaVersion)
            return;

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
        if (version < 1)
        {
            await Command(connection, """
                CREATE TABLE shares (
                    id TEXT PRIMARY KEY,
                    file_path TEXT NOT NULL,
                    name TEXT NOT NULL,
                    size_bytes INTEGER NOT NULL,
                    added_at TEXT NOT NULL
                );
                CREATE TABLE transfers (
                    id TEXT PRIMARY KEY,
                    name TEXT NULL,
                    size_bytes INTEGER NULL,
                    done INTEGER NOT NULL,
                    added_at TEXT NOT NULL
                );
                """, transaction).ExecuteNonQueryAsync(ct);
        }
        if (version < 2)
        {
            await Command(connection, "ALTER TABLE transfers ADD COLUMN paused INTEGER NOT NULL DEFAULT 0", transaction).ExecuteNonQueryAsync(ct);
        }
        if (version < 3)
        {
            await Command(connection, "CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL)", transaction).ExecuteNonQueryAsync(ct);
        }
        if (version < 4)
        {
            // Where each transfer came from; older ones were all invites.
            await Command(connection, "ALTER TABLE transfers ADD COLUMN source INTEGER NOT NULL DEFAULT 0", transaction).ExecuteNonQueryAsync(ct);
        }
        if (version < 5)
        {
            // Shared files paused one by one; older ones were all sending.
            await Command(connection, "ALTER TABLE shares ADD COLUMN paused INTEGER NOT NULL DEFAULT 0", transaction).ExecuteNonQueryAsync(ct);
        }
        if (version < 6)
        {
            // A share is a set of items (AD-19); each older share is one file, its only item.
            await Command(connection, """
                ALTER TABLE shares ADD COLUMN kind INTEGER NOT NULL DEFAULT 0;
                CREATE TABLE share_items (
                    share_id TEXT NOT NULL,
                    position INTEGER NOT NULL,
                    relative_path TEXT NOT NULL,
                    size_bytes INTEGER NOT NULL,
                    content_ref TEXT NOT NULL,
                    PRIMARY KEY (share_id, position)
                );
                INSERT INTO share_items (share_id, position, relative_path, size_bytes, content_ref)
                    SELECT id, 0, name, size_bytes, file_path FROM shares;
                """, transaction).ExecuteNonQueryAsync(ct);
        }
        await Command(connection, $"PRAGMA user_version = {SchemaVersion}", transaction).ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }

    async Task<IReadOnlyList<Share>> IShareRepository.GetAllAsync(CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        var items = new Dictionary<string, List<ShareItem>>();
        await using (var itemReader = await Command(connection, "SELECT share_id, relative_path, size_bytes, content_ref FROM share_items ORDER BY share_id, position").ExecuteReaderAsync(ct))
        {
            while (await itemReader.ReadAsync(ct))
            {
                var id = itemReader.GetString(0);
                if (!items.TryGetValue(id, out var list))
                    items[id] = list = [];
                list.Add(new ShareItem(itemReader.GetString(1), itemReader.GetInt64(2), itemReader.GetString(3)));
            }
        }

        await using var reader = await Command(connection, "SELECT id, file_path, name, size_bytes, added_at, paused, kind FROM shares ORDER BY added_at").ExecuteReaderAsync(ct);
        var shares = new List<Share>();
        while (await reader.ReadAsync(ct))
        {
            var kind = (ShareKind)reader.GetInt64(6);
            shares.Add(new Share(
                new ShareId(Guid.Parse(reader.GetString(0))),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3),
                ParseTime(reader.GetString(4)),
                reader.GetInt64(5) != 0,
                items.GetValueOrDefault(reader.GetString(0)),
                Enum.IsDefined(kind) ? kind : ShareKind.File));
        }
        return shares;
    }

    public async Task AddAsync(Share share, CancellationToken ct = default)
    {
        await using var connection = await ConnectAsync(ct);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
        var command = Command(connection, "INSERT INTO shares (id, file_path, name, size_bytes, added_at, paused, kind) VALUES ($id, $path, $name, $size, $added, $paused, $kind)", transaction);
        command.Parameters.AddWithValue("$id", share.Id.ToString());
        command.Parameters.AddWithValue("$path", share.FilePath);
        command.Parameters.AddWithValue("$name", share.Name);
        command.Parameters.AddWithValue("$size", share.SizeBytes);
        command.Parameters.AddWithValue("$added", FormatTime(share.AddedAt));
        command.Parameters.AddWithValue("$paused", share.Paused ? 1 : 0);
        command.Parameters.AddWithValue("$kind", (int)share.Kind);
        await command.ExecuteNonQueryAsync(ct);
        for (var i = 0; i < share.Items.Count; i++)
        {
            var item = Command(connection, "INSERT INTO share_items (share_id, position, relative_path, size_bytes, content_ref) VALUES ($id, $position, $path, $size, $ref)", transaction);
            item.Parameters.AddWithValue("$id", share.Id.ToString());
            item.Parameters.AddWithValue("$position", i);
            item.Parameters.AddWithValue("$path", share.Items[i].RelativePath);
            item.Parameters.AddWithValue("$size", share.Items[i].SizeBytes);
            item.Parameters.AddWithValue("$ref", share.Items[i].ContentRef);
            await item.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }

    public async Task SetPausedAsync(ShareId id, bool paused, CancellationToken ct = default)
    {
        await using var connection = await ConnectAsync(ct);
        var command = Command(connection, "UPDATE shares SET paused = $paused WHERE id = $id");
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$paused", paused ? 1 : 0);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task RemoveAsync(ShareId id, CancellationToken ct = default)
    {
        await using var connection = await ConnectAsync(ct);
        var command = Command(connection, "DELETE FROM share_items WHERE share_id = $id; DELETE FROM shares WHERE id = $id");
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(ct);
    }

    async Task<IReadOnlyList<Transfer>> ITransferRepository.GetAllAsync(CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        await using var reader = await Command(connection, "SELECT id, name, size_bytes, done, added_at, paused, source FROM transfers ORDER BY added_at").ExecuteReaderAsync(ct);
        var transfers = new List<Transfer>();
        while (await reader.ReadAsync(ct))
        {
            transfers.Add(new Transfer(
                new TransferId(Guid.Parse(reader.GetString(0))),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt64(2),
                reader.GetInt64(3) != 0,
                ParseTime(reader.GetString(4)),
                reader.GetInt64(5) != 0,
                Enum.IsDefined((TransferSource)reader.GetInt64(6)) ? (TransferSource)reader.GetInt64(6) : TransferSource.Invite));
        }
        return transfers;
    }

    public async Task AddAsync(Transfer transfer, CancellationToken ct = default)
    {
        await using var connection = await ConnectAsync(ct);
        var command = Command(connection, "INSERT INTO transfers (id, name, size_bytes, done, added_at, paused, source) VALUES ($id, $name, $size, $done, $added, $paused, $source)");
        Bind(command, transfer);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateAsync(Transfer transfer, CancellationToken ct = default)
    {
        await using var connection = await ConnectAsync(ct);
        var command = Command(connection, "UPDATE transfers SET name = $name, size_bytes = $size, done = $done, added_at = $added, paused = $paused, source = $source WHERE id = $id");
        Bind(command, transfer);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task RemoveAsync(TransferId id, CancellationToken ct = default)
    {
        await using var connection = await ConnectAsync(ct);
        var command = Command(connection, "DELETE FROM transfers WHERE id = $id");
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        await using var connection = await ConnectAsync(ct);
        var command = Command(connection, "SELECT value FROM settings WHERE key = $key");
        command.Parameters.AddWithValue("$key", key);
        return await command.ExecuteScalarAsync(ct) as string;
    }

    public async Task SetAsync(string key, string? value, CancellationToken ct = default)
    {
        await using var connection = await ConnectAsync(ct);
        var command = value is null
            ? Command(connection, "DELETE FROM settings WHERE key = $key")
            : Command(connection, "INSERT INTO settings (key, value) VALUES ($key, $value) ON CONFLICT(key) DO UPDATE SET value = $value");
        command.Parameters.AddWithValue("$key", key);
        if (value is not null)
            command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static void Bind(SqliteCommand command, Transfer transfer)
    {
        command.Parameters.AddWithValue("$id", transfer.Id.ToString());
        command.Parameters.AddWithValue("$name", (object?)transfer.Name ?? DBNull.Value);
        command.Parameters.AddWithValue("$size", (object?)transfer.SizeBytes ?? DBNull.Value);
        command.Parameters.AddWithValue("$done", transfer.Done ? 1 : 0);
        command.Parameters.AddWithValue("$paused", transfer.Paused ? 1 : 0);
        command.Parameters.AddWithValue("$added", FormatTime(transfer.AddedAt));
        command.Parameters.AddWithValue("$source", (int)transfer.Source);
    }

    private async Task<SqliteConnection> ConnectAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    private static SqliteCommand Command(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        return command;
    }

    private static string FormatTime(DateTimeOffset time) => time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTime(string text) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}

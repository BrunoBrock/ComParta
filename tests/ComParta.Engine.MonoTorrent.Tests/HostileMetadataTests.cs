using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using ComParta.Core;
using ComParta.Core.Ports;
using MonoTorrent;
using MonoTorrent.BEncoding;
using MonoTorrent.Client;
using MonoTorrent.PieceWriter;
using ReusableTasks;

namespace ComParta.Engine.MonoTorrent.Tests;

/// <summary>
/// A crafted sender announces content whose name tries to escape, disguise itself or flood the disk
/// (CAP-9, AD-6). Whatever the receiver keeps lands inside its Received folder under a safe name.
/// </summary>
public sealed class HostileMetadataTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
    private const int PieceLength = 16 * 1024;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "comparta-tests", Guid.NewGuid().ToString("N"));
    private readonly List<MonoTorrentTransferEngine> _engines = [];
    private readonly List<ClientEngine> _senders = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var engine in _engines)
            await engine.StopAsync();
        foreach (var sender in _senders)
        {
            await sender.StopAllAsync();
            sender.Dispose();
        }
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
    }

    private sealed record Paths(string Root) : IAppPaths
    {
        public string DatabasePath => Path.Combine(Root, "comparta.db");
        public string EngineRoot => Path.Combine(Root, "engine");
        public string ReceivedRoot => Path.Combine(Root, "Received");
        public string LogsRoot => Path.Combine(Root, "logs");
        public string SavedRoot => Path.Combine(Root, "Downloads");
    }

    private async Task<(MonoTorrentTransferEngine Engine, Paths Paths)> StartReceiverAsync()
    {
        // The receiver's data sits one level down, so a name that climbs two levels still lands in the test folder.
        var paths = new Paths(Path.Combine(_folder, "home", "receiver"));
        var engine = new MonoTorrentTransferEngine(paths);
        _engines.Add(engine);
        await engine.StartAsync(SharingPolicy.Closed with { ListenPort = FreePort(), AllowServersInDownloadLinks = true });
        return (engine, paths);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>Download-file bytes announcing one file of <paramref name="content"/> under any name at all.</summary>
    private static byte[] CraftSingle(string name, byte[] content)
    {
        var pieces = new List<byte>();
        for (var offset = 0; offset < content.Length; offset += PieceLength)
            pieces.AddRange(SHA1.HashData(content.AsSpan(offset, Math.Min(PieceLength, content.Length - offset))));
        var info = new BEncodedDictionary
        {
            ["name"] = new BEncodedString(name),
            ["length"] = new BEncodedNumber(content.Length),
            ["piece length"] = new BEncodedNumber(PieceLength),
            ["pieces"] = new BEncodedString(pieces.ToArray()),
        };
        return new BEncodedDictionary { ["info"] = info }.Encode();
    }

    /// <summary>Download-file bytes announcing <paramref name="count"/> tiny files.</summary>
    private static byte[] CraftMany(int count)
    {
        var files = new BEncodedList();
        for (var i = 0; i < count; i++)
        {
            files.Add(new BEncodedDictionary
            {
                ["length"] = new BEncodedNumber(1),
                ["path"] = new BEncodedList { new BEncodedString($"f{i}.txt") },
            });
        }
        var pieceCount = (count + PieceLength - 1) / PieceLength;
        var info = new BEncodedDictionary
        {
            ["name"] = new BEncodedString("many"),
            ["files"] = files,
            ["piece length"] = new BEncodedNumber(PieceLength),
            ["pieces"] = new BEncodedString(new byte[20 * pieceCount]),
        };
        return new BEncodedDictionary { ["info"] = info }.Encode();
    }

    /// <summary>A plain engine that serves <paramref name="content"/> for the crafted file, whatever its name.</summary>
    private async Task<int> StartSenderAsync(byte[] downloadFile, byte[] content) =>
        await StartSenderAsync(downloadFile, _ => content);

    /// <summary>Serves each file's own bytes, for a folder whose files differ.</summary>
    private async Task<int> StartSenderAsync(byte[] downloadFile, Func<ITorrentManagerFile, byte[]> contentOf)
    {
        var port = FreePort();
        var settings = new EngineSettingsBuilder
        {
            AllowLocalPeerDiscovery = false,
            AllowPortForwarding = false,
            DhtEndPoint = null,
            ListenEndPoints = new Dictionary<string, IPEndPoint> { ["ipv4"] = new(IPAddress.Loopback, port) },
            CacheDirectory = Path.Combine(_folder, "sender-cache"),
        }.ToSettings();
        var sender = new ClientEngine(settings, Factories.Default.WithPieceWriterCreator(_ => new MemoryWriter(contentOf)));
        _senders.Add(sender);
        var manager = await sender.AddAsync(Torrent.Load(downloadFile), Path.Combine(_folder, "sender"));
        await manager.HashCheckAsync(autoStart: true);
        return port;
    }

    /// <summary>Every file in the test folder that is not where the receiver is allowed to keep files.</summary>
    private string[] Escaped(Paths paths, string sender) =>
        [.. Directory.EnumerateFiles(_folder, "*", SearchOption.AllDirectories)
            .Where(file => !ReceivedNames.IsInside(paths.ReceivedRoot, file)
                && !ReceivedNames.IsInside(paths.EngineRoot, file)
                && !ReceivedNames.IsInside(Path.Combine(_folder, "sender-cache"), file)
                && !file.Contains(sender, StringComparison.Ordinal))];

    [Theory]
    [InlineData(@"..\..\escape.exe")]
    [InlineData("../../escape.exe")]
    [InlineData("../../../escape.exe")]
    [InlineData(@"C:\Windows\escape.exe")]
    [InlineData("/tmp/escape.exe")]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData("film.mp4:hidden.exe")]
    [InlineData("photo\u202Egpj.exe")]
    [InlineData("trailing. . .")]
    public async Task A_hostile_name_lands_inside_Received_under_a_safe_name_or_not_at_all(string hostile)
    {
        var content = RandomNumberGenerator.GetBytes(3 * PieceLength + 11);
        var downloadFile = CraftSingle(hostile, content);
        var (receiver, paths) = await StartReceiverAsync();
        var port = await StartSenderAsync(downloadFile, content);
        var done = new TaskCompletionSource<ReceiveProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.ReceiveProgressed += progress =>
        {
            if (progress.Done || progress.Failed)
                done.TrySetResult(progress);
        };

        var id = TransferId.New();
        if (await receiver.ReceiveFromFileAsync(id, downloadFile))
        {
            await receiver.ConnectAsync(id, new IPEndPoint(IPAddress.Loopback, port));
            var result = await done.Task.WaitAsync(Timeout);
            if (result.Done)
            {
                // The one file kept sits directly in Received, with the safe name and the right bytes.
                var kept = Assert.Single(Directory.GetFiles(paths.ReceivedRoot));
                Assert.Equal(ReceivedNames.Sanitize(Path.GetFileName(kept)), Path.GetFileName(kept));
                Assert.EndsWith(Path.GetExtension(ReceivedNames.Sanitize(hostile)), kept, StringComparison.Ordinal);
                Assert.False(Path.GetFileName(kept).Contains('\u202E'));
                Assert.Equal(content, await File.ReadAllBytesAsync(kept));
            }
        }

        Assert.Empty(Escaped(paths, "sender"));
        Assert.False(File.Exists(Path.Combine(_folder, "escape.exe")));
        Assert.False(File.Exists(Path.Combine(_folder, "home", "escape.exe")));
    }

    /// <summary>A download file for a folder: each file's path and bytes, in the order the sender lists them.</summary>
    private static (byte[] DownloadFile, Dictionary<string, byte[]> Data) CraftTree(string name, IReadOnlyList<(string[] Path, byte[] Data)> files)
    {
        var list = new BEncodedList();
        var all = new List<byte>();
        var data = new Dictionary<string, byte[]>();
        foreach (var (path, bytes) in files)
        {
            var segments = new BEncodedList();
            foreach (var segment in path)
                segments.Add(new BEncodedString(segment));
            list.Add(new BEncodedDictionary { ["length"] = new BEncodedNumber(bytes.Length), ["path"] = segments });
            all.AddRange(bytes);
            data[string.Join(Path.DirectorySeparatorChar, path)] = bytes;
        }
        var stream = all.ToArray();
        var pieces = new List<byte>();
        for (var offset = 0; offset < stream.Length; offset += PieceLength)
            pieces.AddRange(SHA1.HashData(stream.AsSpan(offset, Math.Min(PieceLength, stream.Length - offset))));
        var info = new BEncodedDictionary
        {
            ["name"] = new BEncodedString(name),
            ["files"] = list,
            ["piece length"] = new BEncodedNumber(PieceLength),
            ["pieces"] = new BEncodedString(pieces.ToArray()),
        };
        return (new BEncodedDictionary { ["info"] = info }.Encode(), data);
    }

    /// <summary>Receives a whole folder from a crafted sender and waits until it has arrived.</summary>
    private async Task<(MonoTorrentTransferEngine Receiver, Paths Paths, bool Done)> ReceiveFolderAsync(byte[] downloadFile, Func<ITorrentManagerFile, byte[]> contentOf)
    {
        var (receiver, paths) = await StartReceiverAsync();
        var port = await StartSenderAsync(downloadFile, contentOf);
        var done = new TaskCompletionSource<ReceiveProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.ReceiveProgressed += progress =>
        {
            if (progress.Done || progress.Failed)
                done.TrySetResult(progress);
        };
        var id = TransferId.New();
        Assert.True(await receiver.ReceiveFromFileAsync(id, downloadFile));
        await receiver.ConnectAsync(id, new IPEndPoint(IPAddress.Loopback, port));
        var result = await done.Task.WaitAsync(Timeout);
        return (receiver, paths, result.Done);
    }

    [Fact]
    public async Task A_folder_of_two_hundred_files_arrives_as_one_folder_with_its_tree()
    {
        var files = new List<(string[] Path, byte[] Data)>();
        for (var day = 0; day < 4; day++)
            for (var photo = 0; photo < 50; photo++)
                files.Add(([$"day {day}", $"photo {photo}.jpg"], RandomNumberGenerator.GetBytes(100)));
        var (downloadFile, data) = CraftTree("Trip", files);

        var (_, paths, done) = await ReceiveFolderAsync(downloadFile, file => data[file.Path]);

        Assert.True(done);
        // The unfinished folder is hidden in Received; only the arrived folder counts.
        var arrived = Assert.Single(Directory.GetDirectories(paths.ReceivedRoot), d => !Path.GetFileName(d).StartsWith('.'));
        Assert.Equal("Trip", Path.GetFileName(arrived));
        Assert.Equal(4, Directory.GetDirectories(arrived).Length);
        foreach (var (path, bytes) in files)
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine([paths.ReceivedRoot, "Trip", .. path])));
        Assert.Equal(200, Directory.GetFiles(arrived, "*", SearchOption.AllDirectories).Length);
        Assert.Empty(Escaped(paths, "sender"));
    }

    [Fact]
    public async Task Hostile_names_inside_a_received_folder_get_their_safe_names_too()
    {
        var files = new List<(string[] Path, byte[] Data)>
        {
            (["days", "CON"], RandomNumberGenerator.GetBytes(120)),
            (["days", "nul.txt"], RandomNumberGenerator.GetBytes(120)),
            (["pics", "photo\u202Egpj.exe"], RandomNumberGenerator.GetBytes(120)),
        };
        var (downloadFile, data) = CraftTree("Trip", files);

        var (_, paths, done) = await ReceiveFolderAsync(downloadFile, file => data[file.Path]);

        Assert.True(done);
        foreach (var (path, bytes) in files)
        {
            var safe = path.Select(ReceivedNames.Sanitize).ToArray();
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine([paths.ReceivedRoot, "Trip", .. safe])));
        }
        var names = Directory.GetFileSystemEntries(paths.ReceivedRoot, "*", SearchOption.AllDirectories).Select(Path.GetFileName);
        Assert.All(names, name => Assert.False(name!.Contains('\u202E')));
        Assert.Empty(Escaped(paths, "sender"));
    }

    [Fact]
    public async Task Content_announcing_thousands_of_files_is_refused()
    {
        var (receiver, paths) = await StartReceiverAsync();
        var downloadFile = CraftMany(MonoTorrentTransferEngine.MaxFiles + 1);

        Assert.Null(receiver.ReadDownloadFileKey(downloadFile));
        Assert.False(await receiver.ReceiveFromFileAsync(TransferId.New(), downloadFile));
        Assert.Empty(Directory.Exists(paths.ReceivedRoot) ? Directory.GetFileSystemEntries(paths.ReceivedRoot) : []);
    }

    /// <summary>Serves the same bytes for any file name, so a sender can offer what no disk could hold.</summary>
    private sealed class MemoryWriter(Func<ITorrentManagerFile, byte[]> contentOf) : IPieceWriter
    {
        public MemoryWriter(byte[] content) : this(_ => content) { }

        public int OpenFiles => 0;
        public int MaximumOpenFiles => 0;

        public ReusableTask<int> ReadAsync(ITorrentManagerFile file, long offset, Memory<byte> buffer)
        {
            var content = contentOf(file);
            var count = (int)Math.Max(0, Math.Min(buffer.Length, content.Length - offset));
            content.AsMemory((int)offset, count).CopyTo(buffer);
            return ReusableTask.FromResult(count);
        }

        public ReusableTask WriteAsync(ITorrentManagerFile file, long offset, ReadOnlyMemory<byte> buffer) => ReusableTask.CompletedTask;
        public ReusableTask CloseAsync(ITorrentManagerFile file) => ReusableTask.CompletedTask;
        public ReusableTask<bool> ExistsAsync(ITorrentManagerFile file) => ReusableTask.FromResult(true);
        public ReusableTask FlushAsync(ITorrentManagerFile file) => ReusableTask.CompletedTask;
        public ReusableTask MoveAsync(ITorrentManagerFile file, string fullPath, bool overwrite) => ReusableTask.CompletedTask;
        public ReusableTask SetMaximumOpenFilesAsync(int maximumOpenFiles) => ReusableTask.CompletedTask;
        public void Dispose() { }
    }
}

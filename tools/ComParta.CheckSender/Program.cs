using ComParta.Core;
using ComParta.Core.Ports;
using ComParta.Engine.MonoTorrent;

// Another device for the Android check in CI (.github/scripts/check-android.sh): shares files with
// the real engine, so the app on the emulator receives them through its own screen, the way a
// person's phone does. Writes one invite per file, in the order given, that reaches this computer at
// the address given (from the emulator, the computer it runs on is 10.0.2.2), then keeps sharing
// until it is stopped.
// Usage: dotnet ComParta.CheckSender.dll <port> <address the phone reaches> <invites file> <file>...
if (args.Length < 4 || !int.TryParse(args[0], out var port))
{
    Console.Error.WriteLine("Usage: ComParta.CheckSender <port> <address> <invites file> <file>...");
    return 2;
}
var (address, invitesFile, files) = (args[1], args[2], args[3..]);

var root = Path.Combine(Path.GetTempPath(), "comparta-check-sender", Guid.NewGuid().ToString("N"));
var engine = new MonoTorrentTransferEngine(new CheckPaths(root));
await engine.StartAsync(SharingPolicy.Closed with { ListenPort = port });

var ready = new Dictionary<ShareId, TaskCompletionSource>();
engine.OfferChanged += (id, state) =>
{
    if (state == OfferState.Ready && ready.TryGetValue(id, out var done))
        done.TrySetResult();
    else if (state == OfferState.Failed && ready.TryGetValue(id, out var failed))
        failed.TrySetException(new IOException("A file could not be shared."));
};

var invites = new List<string>();
foreach (var file in files)
{
    var id = ShareId.New();
    ready[id] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    await engine.OfferAsync(id, Path.GetFullPath(file));
    await ready[id].Task.WaitAsync(TimeSpan.FromMinutes(2));
    // The engine names no address with this policy; the one the phone can reach is added here.
    invites.Add((await engine.GetInviteAsync(id))! + $"&x.pe={address}:{port}");
}
// Written whole at the end, so the check never reads half of it.
File.WriteAllLines(invitesFile + ".part", invites);
File.Move(invitesFile + ".part", invitesFile, overwrite: true);
Console.WriteLine($"Sharing {files.Length} files on port {port}.");

var stop = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.TrySetResult();
};
AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.TrySetResult();
await stop.Task;
await engine.StopAsync();
return 0;

internal sealed class CheckPaths(string root) : IAppPaths
{
    public string DatabasePath => Path.Combine(root, "comparta.db");
    public string EngineRoot => Path.Combine(root, "engine");
    public string ReceivedRoot => Path.Combine(root, "Received");
    public string LogsRoot => Path.Combine(root, "logs");
    public string SavedRoot => Path.Combine(root, "Saved");
}

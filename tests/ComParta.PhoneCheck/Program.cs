using ComParta.Core;
using ComParta.Core.Ports;
using ComParta.Engine.MonoTorrent;

// The computer side of the Android check in CI (check-android.sh): shares one made-up file, slowly,
// so a phone on the emulator can receive it while the check turns the screen off. The emulator
// reaches the computer it runs on at 10.0.2.2, so the invite names that address.
//   dotnet run --project tests/ComParta.PhoneCheck -- <folder> <megabytes> <bytes per second> <port>
// Writes the invite to <folder>/invite.txt and keeps sharing until it is stopped, writing how many
// bytes the phone got so far to <folder>/sent.txt every second.
if (args.Length != 4)
{
    Console.Error.WriteLine("Usage: <folder> <megabytes> <bytes per second> <port>");
    return 2;
}
var folder = Path.GetFullPath(args[0]);
var megabytes = int.Parse(args[1]);
var bytesPerSecond = long.Parse(args[2]);
var port = int.Parse(args[3]);

var file = Path.Combine(folder, "files", "Natal.mp4");
Directory.CreateDirectory(Path.GetDirectoryName(file)!);
await using (var output = File.Create(file))
{
    var block = new byte[1024 * 1024];
    for (var i = 0; i < megabytes; i++)
    {
        Random.Shared.NextBytes(block);
        await output.WriteAsync(block);
    }
}

var engine = new MonoTorrentTransferEngine(new CheckPaths(folder));
await engine.StartAsync(SharingPolicy.Closed with { ListenPort = port });
await engine.SetSpeedLimitsAsync(null, bytesPerSecond);
var shareId = ShareId.New();
var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
engine.OfferChanged += (id, state) =>
{
    if (id == shareId && state == OfferState.Ready)
        ready.TrySetResult();
};
await engine.OfferAsync(shareId, file);
await ready.Task.WaitAsync(TimeSpan.FromMinutes(5));
var invite = await engine.GetInviteAsync(shareId) + $"&x.pe=10.0.2.2:{port}";
await File.WriteAllTextAsync(Path.Combine(folder, "invite.txt"), invite);
Console.WriteLine($"Sharing: {invite}");
var sent = Path.Combine(folder, "sent.txt");
while (true)
{
    await Task.Delay(TimeSpan.FromSeconds(1));
    var bytes = engine.GetActivity().Shares.GetValueOrDefault(shareId)?.BytesSent ?? 0;
    await File.WriteAllTextAsync(sent + ".new", bytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
    File.Move(sent + ".new", sent, overwrite: true);
}

internal sealed class CheckPaths(string root) : IAppPaths
{
    public string DatabasePath => Path.Combine(root, "comparta.db");
    public string EngineRoot => Path.Combine(root, "engine");
    public string ReceivedRoot => Path.Combine(root, "Received");
    public string LogsRoot => Path.Combine(root, "logs");
    public string SavedRoot => Path.Combine(root, "Saved");
}

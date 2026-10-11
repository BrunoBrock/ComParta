using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ComParta.Shell;

/// <summary>
/// Keeps ComParta to one running copy per data folder (AD-14). The first copy claims the name and
/// listens on a local pipe only this user can open; a later launch, such as one started by a
/// clicked <c>comparta:</c> link, hands its arguments to the first copy and exits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const int MaxMessageBytes = 64 * 1024;
    private const byte Ack = 1;

    private readonly Mutex _claim;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();

    private SingleInstance(Mutex claim, string pipeName)
    {
        _claim = claim;
        _pipeName = pipeName;
        _ = ListenAsync(_stop.Token);
    }

    /// <summary>Raised on a background thread with the arguments another launch handed over.</summary>
    public event Action<string[]>? ArgumentsReceived;

    /// <summary>
    /// The name both copies agree on. It comes from the data folder, so two copies with their own
    /// COMPARTA_HOME can still run side by side for testing.
    /// </summary>
    public static string KeyFor(string dataFolder)
    {
        var normalized = Path.GetFullPath(dataFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (OperatingSystem.IsWindows())
            normalized = normalized.ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..16];
    }

    /// <summary>Claims the name, or returns null when another copy already holds it.</summary>
    public static SingleInstance? TryClaim(string key)
    {
        var claim = new Mutex(initiallyOwned: false, $"ComParta-{key}", out var createdNew);
        if (createdNew)
            return new SingleInstance(claim, PipeName(key));
        claim.Dispose();
        return null;
    }

    /// <summary>
    /// Hands the arguments to the copy that holds the name and waits until it confirms. False when
    /// it could not be reached in time.
    /// </summary>
    public static async Task<bool> ForwardAsync(string key, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(arguments);
        if (payload.Length > MaxMessageBytes)
            return false;
        using var cancel = new CancellationTokenSource(timeout);
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeClientStream(
                    ".", PipeName(key), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                // The first copy may still be starting; ConnectAsync waits for its pipe to appear.
                await pipe.ConnectAsync(cancel.Token);
                await pipe.WriteAsync(BitConverter.GetBytes(payload.Length), cancel.Token);
                await pipe.WriteAsync(payload, cancel.Token);
                await pipe.FlushAsync(cancel.Token);
                var ack = new byte[1];
                if (await pipe.ReadAsync(ack, cancel.Token) == 1 && ack[0] == Ack)
                    return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException)
            {
                // The first copy was between two launches; try again until the time is up.
            }
            try { await Task.Delay(50, cancel.Token); }
            catch (OperationCanceledException) { return false; }
        }
        return false;
    }

    private static string PipeName(string key) => $"ComParta-{key}";

    private async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    _pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(ct);
                var arguments = await ReadAsync(pipe, ct);
                if (arguments is null)
                    continue;
                await pipe.WriteAsync(new[] { Ack }, ct);
                await pipe.FlushAsync(ct);
                ArgumentsReceived?.Invoke(arguments);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                // One bad message must not stop the next launch from getting through.
            }
        }
    }

    /// <summary>A length, then that many bytes of JSON: a list of strings.</summary>
    private static async Task<string[]?> ReadAsync(Stream pipe, CancellationToken ct)
    {
        var header = new byte[4];
        await pipe.ReadExactlyAsync(header, ct);
        var length = BitConverter.ToInt32(header);
        if (length is <= 0 or > MaxMessageBytes)
            return null;
        var payload = new byte[length];
        await pipe.ReadExactlyAsync(payload, ct);
        try
        {
            return JsonSerializer.Deserialize<string[]>(payload) is { } arguments
                ? [.. arguments.Where(a => a is not null)]
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _claim.Dispose();
    }
}

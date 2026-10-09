namespace ComParta.Core;

public readonly record struct ShareId(Guid Value)
{
    public static ShareId New() => new(Guid.NewGuid());
    public static bool TryParse(string? text, out ShareId id)
    {
        var ok = Guid.TryParse(text, out var value);
        id = new ShareId(value);
        return ok;
    }
    public override string ToString() => Value.ToString("D");
}

public readonly record struct TransferId(Guid Value)
{
    public static TransferId New() => new(Guid.NewGuid());
    public static bool TryParse(string? text, out TransferId id)
    {
        var ok = Guid.TryParse(text, out var value);
        id = new TransferId(value);
        return ok;
    }
    public override string ToString() => Value.ToString("D");
}

/// <summary>A file a person offers. The file stays where it is (AD-6).</summary>
public sealed record Share(ShareId Id, string FilePath, string Name, long SizeBytes, DateTimeOffset AddedAt);

public enum ShareStatus
{
    /// <summary>The file is being read so it can be offered.</summary>
    Preparing,
    /// <summary>The file is offered and an invite can be given out.</summary>
    Ready,
    /// <summary>The file was moved, renamed, deleted or changed outside the app.</summary>
    Unavailable,
}

public sealed record ShareView(Share Share, ShareStatus Status);

/// <summary>A file on its way from another person's device into the Received folder.</summary>
public sealed record Transfer(TransferId Id, string? Name, long? SizeBytes, bool Done, DateTimeOffset AddedAt);

public enum TransferState
{
    /// <summary>Looking for the other person's device.</summary>
    Connecting,
    Receiving,
    Done,
    /// <summary>It cannot continue; the person removes it and asks for a new invite.</summary>
    Failed,
}

public sealed record TransferView(Transfer Transfer, TransferState State, long ReceivedBytes);

/// <summary>
/// The one place network behaviour is decided (AD-8). Only the engine adapter applies it.
/// A <see cref="ListenPort"/> of 0 lets the engine choose a port and keep it between runs.
/// </summary>
public sealed record SharingPolicy(
    bool AllowLocalDiscovery,
    bool AllowPublicLookup,
    bool AllowPeerExchange,
    bool AllowAnnounceServers,
    bool AllowPortForwarding,
    bool AllowAddressInInvite,
    bool RequireEncryption,
    int ListenPort)
{
    /// <summary>
    /// Devices find each other on the local network and through the public lookup network,
    /// using only the invite. The invite also carries this device's own network address so the
    /// other device can reach it straight away. Nothing else is switched on.
    /// </summary>
    public static SharingPolicy Default { get; } = new(
        AllowLocalDiscovery: true,
        AllowPublicLookup: true,
        AllowPeerExchange: false,
        AllowAnnounceServers: false,
        AllowPortForwarding: true,
        AllowAddressInInvite: true,
        RequireEncryption: true,
        ListenPort: 0);

    /// <summary>No discovery at all; used by tests that connect two devices by hand.</summary>
    public static SharingPolicy Closed { get; } = new(false, false, false, false, false, false, true, 0);
}

public enum ErrorKind
{
    ShareDuplicate,
    ShareNotFound,
    ShareNotReady,
    FileUnreadable,
    InviteInvalid,
    TransferDuplicate,
    TransferNotFound,
    Unexpected,
}

public readonly record struct Result(ErrorKind? Error)
{
    public bool Ok => Error is null;
    public static Result Success { get; } = new((ErrorKind?)null);
    public static Result Fail(ErrorKind error) => new(error);
}

public readonly record struct Result<T>(T? Value, ErrorKind? Error)
{
    public bool Ok => Error is null;
    public static Result<T> Success(T value) => new(value, null);
    public static Result<T> Fail(ErrorKind error) => new(default, error);
}

namespace ComParta.Core;

/// <summary>
/// Roughly how long a transfer or a sending share has left (CAP-8), from a smoothed speed so the
/// words do not jump about. Unknown until the speed is known, and again once nothing has moved for
/// <see cref="StallAfter"/>.
/// </summary>
public sealed class TimeLeft(TimeProvider clock)
{
    public static readonly TimeSpan StallAfter = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SampleEvery = TimeSpan.FromSeconds(1);
    // How much each new second counts against the speed so far.
    private const double Weight = 0.3;

    private long _lastBytes = -1;
    private DateTimeOffset _lastAt;
    private DateTimeOffset _lastMovedAt;
    private double _speed;

    /// <param name="movedBytes">A running count of the bytes moved so far.</param>
    /// <param name="remainingBytes">The bytes still to move.</param>
    /// <returns>Seconds left, or null while it cannot be told.</returns>
    public long? Update(long movedBytes, long remainingBytes)
    {
        var now = clock.GetUtcNow();
        if (_lastBytes < 0 || movedBytes < _lastBytes)
        {
            _speed = 0;
            _lastBytes = movedBytes;
            _lastAt = _lastMovedAt = now;
            return null;
        }
        var seconds = (now - _lastAt).TotalSeconds;
        if (now - _lastAt >= SampleEvery)
        {
            var rate = (movedBytes - _lastBytes) / seconds;
            _speed = _speed == 0 ? rate : _speed + Weight * (rate - _speed);
            if (movedBytes > _lastBytes)
                _lastMovedAt = now;
            _lastBytes = movedBytes;
            _lastAt = now;
        }
        if (now - _lastMovedAt >= StallAfter)
            _speed = 0;
        return _speed > 0 && remainingBytes > 0 ? (long)Math.Ceiling(remainingBytes / _speed) : null;
    }
}

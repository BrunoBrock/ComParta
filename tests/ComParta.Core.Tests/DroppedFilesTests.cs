using ComParta.Core.UseCases;

namespace ComParta.Core.Tests;

public sealed class DroppedFilesTests
{
    private readonly DroppedFiles _dropped = new();

    [Fact]
    public async Task Files_put_before_or_after_the_request_are_taken_once()
    {
        var early = Guid.NewGuid();
        _dropped.Put(early, ["a.jpg"]);
        Assert.Equal(["a.jpg"], await _dropped.TakeAsync(early, TimeSpan.FromSeconds(1)));

        var late = Guid.NewGuid();
        var waiting = _dropped.TakeAsync(late, TimeSpan.FromSeconds(5));
        _dropped.Put(late, ["b.mp4"]);
        Assert.Equal(["b.mp4"], await waiting);

        Assert.Null(await _dropped.TakeAsync(early, TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public async Task A_drop_the_window_never_hands_over_gives_nothing()
    {
        Assert.Null(await _dropped.TakeAsync(Guid.NewGuid(), TimeSpan.FromMilliseconds(50)));
    }
}

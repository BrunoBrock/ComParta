using ComParta.Core.Ports;
using ComParta.Core.UseCases;

namespace ComParta.Core.Tests;

public sealed class MediaUseCasesTests
{
    private readonly FakeEngine _engine = new();
    private readonly FakeFilePicker _picker = new();
    private readonly FakeFileSystem _files = new();
    private readonly FakePaths _paths = new(Path.Combine(Path.GetTempPath(), "comparta-media-tests"));
    private readonly ShareUseCases _shares;
    private readonly TransferUseCases _transfers;
    private readonly MediaUseCases _media;

    /// <summary>The first bytes of each kind of file, as they really start.</summary>
    internal static class Heads
    {
        public static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F'];
        public static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D];
        public static readonly byte[] Mp3 = [.. "ID3"u8, 4, 0, 0, 0, 0, 0, 0];
        public static readonly byte[] Mp4 = [0, 0, 0, 0x20, .. "ftypisom"u8, 0, 0, 2, 0];
        public static readonly byte[] WebM = [0x1A, 0x45, 0xDF, 0xA3, 0x9F, 0x42, 0x86, 0x81];
        public static readonly byte[] Program = [.. "MZ"u8, 0x90, 0, 3, 0, 0, 0, 4, 0];
    }

    public MediaUseCasesTests()
    {
        _shares = new ShareUseCases(new FakeShareRepository(), _engine, _picker, _files);
        _transfers = new TransferUseCases(new FakeTransferRepository(), _engine, files: _files);
        var received = new ReceivedUseCases(_transfers, _paths, new FakeOpener(), _files);
        _media = new MediaUseCases(_shares, received, _files);
    }

    /// <summary>A file of <paramref name="size"/> bytes that starts with <paramref name="head"/>; the rest counts up.</summary>
    private static byte[] Content(byte[] head, int size)
    {
        var content = new byte[size];
        for (var i = 0; i < size; i++)
            content[i] = (byte)i;
        head.CopyTo(content, 0);
        return content;
    }

    private async Task<string> ShareAsync(string name, byte[] content)
    {
        var path = Path.Combine(Path.GetTempPath(), "Media", name);
        _files.Files[path] = content.Length;
        _files.Contents[path] = content;
        _picker.NextPick = path;
        return (await _shares.AddAsync()).Value!.Share.Id.ToString();
    }

    private async Task<string> ReceiveAsync(string name, byte[] content)
    {
        var id = (await _transfers.ReceiveAsync("invite:" + name)).Value!.Transfer.Id;
        _engine.Report(new ReceiveProgress(id, name, content.Length, content.Length, true));
        var path = Path.Combine(_paths.ReceivedRoot, name);
        _files.Files[path] = content.Length;
        _files.Contents[path] = content;
        return id.ToString();
    }

    private static byte[] Read(MediaReply reply)
    {
        using var body = reply.Body!;
        using var copy = new MemoryStream();
        body.CopyTo(copy);
        return copy.ToArray();
    }

    [Theory]
    [InlineData("photo.jpg", MediaKind.Picture, "image/jpeg")]
    [InlineData("drawing.PNG", MediaKind.Picture, "image/png")]
    [InlineData("song.mp3", MediaKind.Song, "audio/mpeg")]
    [InlineData("party.mp4", MediaKind.Video, "video/mp4")]
    [InlineData("clip.webm", MediaKind.Video, "video/webm")]
    public async Task A_shared_picture_song_or_video_opens_and_its_whole_file_is_served(string name, MediaKind kind, string type)
    {
        var head = Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".jpg" => Heads.Jpeg,
            ".png" => Heads.Png,
            ".mp3" => Heads.Mp3,
            ".mp4" => Heads.Mp4,
            _ => Heads.WebM,
        };
        var content = Content(head, 1_000);
        var id = await ShareAsync(name, content);

        var opened = _media.Open(MediaSource.Share, id);

        Assert.True(opened.Ok);
        Assert.Equal(kind, opened.Value!.Kind);
        Assert.Equal(name, opened.Value.Name);
        var reply = _media.Serve(opened.Value.Token, null);
        Assert.Equal((200, type, 1_000L, (string?)null), (reply.Status, reply.ContentType, reply.Length, reply.ContentRange));
        Assert.Equal(content, Read(reply));
    }

    [Fact]
    public async Task A_player_seeking_gets_just_the_part_it_asks_for()
    {
        var content = Content(Heads.Mp4, 1_000);
        var token = _media.Open(MediaSource.Share, await ShareAsync("film.mp4", content)).Value!.Token;

        var middle = _media.Serve(token, "bytes=100-199");
        Assert.Equal((206, 100L, "bytes 100-199/1000"), (middle.Status, middle.Length, middle.ContentRange));
        Assert.Equal(content[100..200], Read(middle));

        var rest = _media.Serve(token, "bytes=900-");
        Assert.Equal((206, "bytes 900-999/1000"), (rest.Status, rest.ContentRange));
        Assert.Equal(content[900..], Read(rest));

        var last = _media.Serve(token, "bytes=-10");
        Assert.Equal("bytes 990-999/1000", last.ContentRange);
        Assert.Equal(content[990..], Read(last));

        var pastTheEnd = _media.Serve(token, "bytes=950-5000");
        Assert.Equal("bytes 950-999/1000", pastTheEnd.ContentRange);
        Assert.Equal(50, Read(pastTheEnd).Length);
    }

    [Fact]
    public async Task A_part_outside_the_file_is_refused_and_odd_requests_get_the_whole_file()
    {
        var token = _media.Open(MediaSource.Share, await ShareAsync("film.mp4", Content(Heads.Mp4, 1_000))).Value!.Token;

        var outside = _media.Serve(token, "bytes=1000-");
        Assert.Equal((416, "bytes */1000", (Stream?)null), (outside.Status, outside.ContentRange, outside.Body));
        Assert.Equal(416, _media.Serve(token, "bytes=-0").Status);

        foreach (var odd in new[] { "bytes=0-1,5-6", "lines=1-2", "bytes=x-", "bytes=5-2", "bytes=5" })
        {
            var whole = _media.Serve(token, odd);
            Assert.Equal((200, 1_000L), (whole.Status, whole.Length));
            whole.Body!.Dispose();
        }
    }

    [Fact]
    public async Task A_part_can_be_moved_around_in_as_a_player_does()
    {
        var content = Content(Heads.Mp4, 1_000);
        var token = _media.Open(MediaSource.Share, await ShareAsync("film.mp4", content)).Value!.Token;
        using var part = _media.Serve(token, "bytes=500-599").Body!;

        part.Seek(-10, SeekOrigin.End);
        var buffer = new byte[20];
        Assert.Equal(10, await part.ReadAsync(buffer));
        Assert.Equal(content[590..600], buffer[..10]);
        Assert.Equal(0, part.Read(buffer, 0, 20));

        part.Position = 0;
        Assert.Equal(content[500], (byte)part.ReadByte());
        Assert.Throws<IOException>(() => part.Seek(-1, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => part.Write([1], 0, 1));
    }

    [Fact]
    public async Task A_received_song_that_has_arrived_plays_but_one_still_coming_does_not()
    {
        var content = Content(Heads.Mp3, 500);
        var id = await ReceiveAsync("song.mp3", content);
        var opened = _media.Open(MediaSource.Received, id);
        Assert.Equal((MediaKind.Song, "song.mp3"), (opened.Value!.Kind, opened.Value.Name));
        Assert.Equal(content, Read(_media.Serve(opened.Value.Token, null)));

        var waiting = (await _transfers.ReceiveAsync("invite:later.mp3")).Value!.Transfer.Id.ToString();
        Assert.Equal(ErrorKind.ReceivedNotFound, _media.Open(MediaSource.Received, waiting).Error);
    }

    [Theory]
    [InlineData("setup.exe")]
    [InlineData("run.bat")]
    [InlineData("letter.pdf")]
    [InlineData("film.mkv")]
    [InlineData("no-extension")]
    public async Task Programs_and_kinds_the_screen_cannot_play_are_refused(string name)
    {
        Assert.Equal(ErrorKind.NotPlayable, _media.Open(MediaSource.Share, await ShareAsync(name, Content(Heads.Mp4, 100))).Error);
        Assert.Equal(ErrorKind.NotPlayable, _media.Open(MediaSource.Received, await ReceiveAsync(name, Content(Heads.Mp4, 100))).Error);
    }

    [Theory]
    [InlineData("holiday.jpg")]
    [InlineData("song.mp3")]
    [InlineData("film.mp4")]
    [InlineData("clip.webm")]
    public async Task A_program_renamed_to_look_like_a_picture_song_or_video_is_refused(string name)
    {
        Assert.Equal(ErrorKind.NotPlayable, _media.Open(MediaSource.Received, await ReceiveAsync(name, Content(Heads.Program, 100))).Error);
        Assert.Equal(ErrorKind.NotPlayable, _media.Open(MediaSource.Received, await ReceiveAsync("empty-" + name, [])).Error);
    }

    [Fact]
    public async Task A_file_that_cannot_be_read_is_not_playable_and_one_gone_since_is_not_found()
    {
        var id = await ShareAsync("photo.jpg", Content(Heads.Jpeg, 100));
        var path = Path.Combine(Path.GetTempPath(), "Media", "photo.jpg");
        var token = _media.Open(MediaSource.Share, id).Value!.Token;

        _files.Locked.Add(path);
        Assert.Equal(ErrorKind.NotPlayable, _media.Open(MediaSource.Share, id).Error);
        Assert.Equal(404, _media.Serve(token, null).Status);
    }

    [Fact]
    public void Unknown_items_and_tokens_are_not_found()
    {
        Assert.Equal(ErrorKind.ShareNotFound, _media.Open(MediaSource.Share, ShareId.New().ToString()).Error);
        Assert.Equal(ErrorKind.ShareNotFound, _media.Open(MediaSource.Share, "not an id").Error);
        Assert.Equal(ErrorKind.TransferNotFound, _media.Open(MediaSource.Received, TransferId.New().ToString()).Error);
        Assert.Equal(ErrorKind.TransferNotFound, _media.Open(MediaSource.Received, null).Error);
        Assert.Equal(404, _media.Serve("0123456789abcdef0123456789abcdef", null).Status);
        Assert.Equal(404, _media.Serve(null, "bytes=0-").Status);
    }

    [Fact]
    public async Task Each_token_reads_only_its_own_file_and_old_ones_stop_working()
    {
        var picture = Content(Heads.Png, 300);
        var first = _media.Open(MediaSource.Share, await ShareAsync("first.png", picture)).Value!.Token;
        var song = _media.Open(MediaSource.Share, await ShareAsync("second.mp3", Content(Heads.Mp3, 400))).Value!.Token;

        Assert.NotEqual(first, song);
        Assert.Equal(picture, Read(_media.Serve(first, null)));
        Assert.Equal(400, _media.Serve(song, null).Length);

        var id = await ShareAsync("third.png", Content(Heads.Png, 200));
        for (var i = 0; i < MediaUseCases.KeptOpen; i++)
            _media.Open(MediaSource.Share, id);
        Assert.Equal(404, _media.Serve(first, null).Status);
    }
}

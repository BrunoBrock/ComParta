using System.Collections.Specialized;
using System.Net;
using System.Net.Http.Json;
using MonoTorrent;
using MonoTorrent.BEncoding;

namespace ComParta.Helper.Tests;

/// <summary>Who may add files to a helper, what it takes, and how it makes room.</summary>
public sealed class ControlTests : HelperTestBase
{
    [Fact]
    public async Task Nobody_without_the_key_can_add_list_or_remove_files()
    {
        var helper = await StartHelperAsync();
        var description = await DescribeAsync(CreateFile("photo.jpg", 50_000));

        Assert.Equal(HttpStatusCode.Unauthorized, (await AddAsync(helper, description, key: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await AddAsync(helper, description, key: "a-wrong-key-of-enough-length")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Http.GetAsync(new Uri(helper.ControlUri, "files"))).StatusCode);
        Assert.Empty(helper.Files);

        var added = await AddAsync(helper, description);
        var id = (await added.Content.ReadFromJsonAsync<AddedFile>())!.Id;
        Assert.Equal(HttpStatusCode.Unauthorized, (await RemoveAsync(helper, id, key: null)).StatusCode);
        Assert.Single(helper.Files);
    }

    [Fact]
    public async Task The_helper_tells_an_install_holding_the_key_where_its_server_and_files_are()
    {
        var helper = await StartHelperAsync();
        var about = new Uri(helper.ControlUri, "about");

        Assert.Equal(HttpStatusCode.Unauthorized, (await Http.GetAsync(about)).StatusCode);

        var request = new HttpRequestMessage(HttpMethod.Get, about);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Key);
        var answer = await (await Http.SendAsync(request)).Content.ReadFromJsonAsync<HelperAbout>();
        Assert.Equal([helper.TrackerUri.OriginalString, $"udp://127.0.0.1:{helper.Options.TrackerPort}"], answer!.Trackers);
        Assert.Equal(helper.PeerEndPoint.ToString(), answer.Peer);
    }

    [Fact]
    public async Task A_file_is_added_once_and_removed_with_everything_the_helper_kept()
    {
        var helper = await StartHelperAsync();
        var description = await DescribeAsync(CreateFile("photo.jpg", 50_000));
        var hashes = Torrent.Load(description).InfoHashes;

        var added = await AddAsync(helper, description);
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var id = (await added.Content.ReadFromJsonAsync<AddedFile>())!.Id;
        Assert.Equal(hashes.V1OrV2.ToHex().ToLowerInvariant(), id);
        Assert.Equal(HttpStatusCode.OK, (await AddAsync(helper, description)).StatusCode);
        var held = Assert.Single(helper.Files);
        Assert.Equal(("photo.jpg", 50_000L), (held.Name, held.Size));
        Assert.True(helper.Tracker.Contains(new MonoTorrent.TrackerServer.InfoHashTrackable(Torrent.Load(description))));

        Assert.Equal(HttpStatusCode.NoContent, (await RemoveAsync(helper, id)).StatusCode);

        Assert.Empty(helper.Files);
        Assert.Empty(helper.Tracker.GetTrackerItems());
        Assert.False(Directory.Exists(Path.Combine(helper.Options.DataFolder, "files", id)));
        Assert.Empty(Directory.GetFiles(Path.Combine(helper.Options.DataFolder, "descriptions")));
        Assert.Equal(HttpStatusCode.NotFound, (await RemoveAsync(helper, id)).StatusCode);
    }

    [Fact]
    public async Task A_file_over_the_limit_is_refused_and_the_oldest_make_room()
    {
        var helper = await StartHelperAsync(Options(o =>
        {
            o.MaxFileBytes = 300_000;
            o.MaxTotalBytes = 500_000;
        }));
        var first = await DescribeAsync(CreateFile("first.jpg", 200_000));
        var second = await DescribeAsync(CreateFile("second.jpg", 200_000));
        var third = await DescribeAsync(CreateFile("third.jpg", 200_000));
        var tooBig = await DescribeAsync(CreateFile("film.mp4", 300_001));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await AddAsync(helper, tooBig)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await AddAsync(helper, first)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await AddAsync(helper, second)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await AddAsync(helper, third)).StatusCode);

        Assert.Equal(["second.jpg", "third.jpg"], helper.Files.Select(f => f.Name));
        Assert.True(helper.Files.Sum(f => f.Size) <= 500_000);
    }

    [Fact]
    public async Task Only_a_description_of_one_plain_file_is_taken()
    {
        var helper = await StartHelperAsync();
        var folder = Path.Combine(Folder, "album");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "a.jpg"), new byte[1000]);
        File.WriteAllBytes(Path.Combine(folder, "b.jpg"), new byte[1000]);
        var twoFiles = (await new TorrentCreator(TorrentType.V1Only).CreateAsync(new TorrentFileSource(folder))).Encode();
        var escaping = await DescribeAsync(CreateFile("innocent.jpg", 1000));
        escaping = Rename(escaping, "../../outside.jpg");

        Assert.Equal(HttpStatusCode.BadRequest, (await AddAsync(helper, "not a description"u8.ToArray())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AddAsync(helper, twoFiles)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AddAsync(helper, escaping)).StatusCode);
        Assert.Empty(helper.Files);
    }

    [Fact]
    public async Task Files_held_before_a_restart_are_held_again_oldest_first()
    {
        var options = Options(o => o.MaxTotalBytes = 500_000);
        var helper = await StartHelperAsync(options);
        await AddAsync(helper, await DescribeAsync(CreateFile("first.jpg", 200_000)));
        await Task.Delay(50);
        await AddAsync(helper, await DescribeAsync(CreateFile("second.jpg", 200_000)));
        await StopHelperAsync(helper);

        var again = await StartHelperAsync(options);
        Assert.Equal(["first.jpg", "second.jpg"], again.Files.Select(f => f.Name));
        await AddAsync(again, await DescribeAsync(CreateFile("third.jpg", 200_000)));
        Assert.Equal(["second.jpg", "third.jpg"], again.Files.Select(f => f.Name));
    }

    [Fact]
    public async Task The_public_server_answers_only_for_files_the_helper_holds()
    {
        var helper = await StartHelperAsync();
        var stranger = Torrent.Load(await DescribeAsync(CreateFile("stranger.bin", 1000))).InfoHashes.V1OrV2;

        var answer = await AnnounceAsync(helper, stranger, port: 6000);

        Assert.True(answer.ContainsKey("failure reason"));
        Assert.Empty(helper.Tracker.GetTrackerItems());
        // The control port is not a public server, and the public server does not take files.
        Assert.Equal(HttpStatusCode.NotFound, (await Http.GetAsync(new Uri(helper.ControlUri, "announce"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Http.PutAsync(new Uri(helper.TrackerUri, "/files"), new ByteArrayContent([]))).StatusCode);
    }

    private async Task<BEncodedDictionary> AnnounceAsync(HelperHost helper, InfoHash hash, int port)
    {
        var query = $"?info_hash={hash.UrlEncode()}&peer_id=-XX0000-000000000000&port={port}&uploaded=0&downloaded=0&left=0&compact=1";
        var bytes = await Http.GetByteArrayAsync(helper.TrackerUri + query);
        return BEncodedValue.Decode<BEncodedDictionary>(bytes);
    }

    [Fact]
    public void An_app_cannot_send_others_to_an_address_that_is_not_its_own()
    {
        var claim = new NameValueCollection { ["ip"] = "198.51.100.9", ["port"] = "6000" };

        Assert.Null(AnnounceRules.Clean(claim, IPAddress.Parse("203.0.113.5"), isScrape: false)["ip"]);
        Assert.Equal("198.51.100.9", AnnounceRules.Clean(claim, IPAddress.Parse("198.51.100.9"), isScrape: false)["ip"]);
        // The helper itself, announcing its public address from the same computer.
        Assert.Equal("198.51.100.9", AnnounceRules.Clean(claim, IPAddress.Loopback, isScrape: false)["ip"]);
    }

    [Fact]
    public void An_app_that_takes_no_connections_still_gets_an_answer()
    {
        var request = new NameValueCollection { ["port"] = "-1" };

        Assert.Equal("0", AnnounceRules.Clean(request, IPAddress.Loopback, isScrape: false)["port"]);
        Assert.Equal("6881", AnnounceRules.Clean(new NameValueCollection { ["port"] = "6881" }, IPAddress.Loopback, isScrape: false)["port"]);
    }

    [Fact]
    public void Settings_that_cannot_work_are_named_before_starting()
    {
        var options = new HelperOptions { PublicAddress = "", Key = "short", PeerPort = 7000, TrackerPort = 7000 };

        var problems = options.Problems();

        Assert.Contains(problems, p => p.StartsWith("PublicAddress", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("Key", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("PeerPort, TrackerPort", StringComparison.Ordinal));
        Assert.Empty(Options().Problems());
    }

    /// <summary>Changes the name inside a description, as someone sending a hostile one would.</summary>
    private static byte[] Rename(byte[] description, string name)
    {
        var torrent = BEncodedValue.Decode<BEncodedDictionary>(description);
        var info = (BEncodedDictionary)torrent["info"];
        info["name"] = new BEncodedString(name);
        return torrent.Encode();
    }
}

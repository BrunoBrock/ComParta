using System.Net;
using ComParta.Core.UseCases;

namespace ComParta.Core.Tests;

public sealed class SupportUseCasesTests
{
    private readonly FakeEngine _engine = new();
    private readonly FakeFilePicker _picker = new();
    private readonly FakeFileSystem _files = new();
    private readonly FakeSettingsRepository _store = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 10, 20, 14, 3, 0, TimeSpan.Zero));
    private readonly SettingsUseCases _settings;
    private readonly ShareUseCases _shares;
    private readonly TransferUseCases _transfers;
    private readonly SupportUseCases _support;

    public SupportUseCasesTests()
    {
        _settings = new SettingsUseCases(_store, new FakeSystem("pt-BR", "1.4.2", "Microsoft Windows 10.0.22631"));
        _shares = new ShareUseCases(new FakeShareRepository(), _engine, _picker, _files);
        _transfers = new TransferUseCases(new FakeTransferRepository(), _engine, _clock, files: _files);
        _support = new SupportUseCases(
            _store, new FakeSystem("pt-BR", "1.4.2", "Microsoft Windows 10.0.22631"), _settings, _shares, _transfers,
            new ConnectionUseCases(_engine), _clock);
    }

    private async Task ShareAsync(string path)
    {
        _files.Files[path] = 2_000;
        _picker.NextPick = path;
        Assert.True((await _shares.AddAsync()).Ok);
    }

    [Fact]
    public async Task The_report_has_the_facts_a_helper_needs()
    {
        await ShareAsync(Path.Combine(Path.GetTempPath(), "Videos", "Birthday party.mp4"));
        await ShareAsync(Path.Combine(Path.GetTempPath(), "Documents", "Recipe.pdf"));
        await _transfers.ReceiveAsync("invite:abc");
        _engine.Report(new ReachabilityFacts(
            true, false, PortMappingState.Obtained, 40_000, IPAddress.Parse("203.0.113.7"), true, true, false));
        await _support.RecordAsync("share.notFound");
        _clock.Now = _clock.Now.AddMinutes(5);
        await _support.RecordAsync("disk.full");

        var report = await _support.ReportAsync();

        Assert.StartsWith("ComParta report\n", report);
        Assert.Contains("Made: 2026-10-20 14:08 UTC\n", report);
        Assert.Contains("Version: 1.4.2\n", report);
        Assert.Contains("System: Microsoft Windows 10.0.22631\n", report);
        Assert.Contains("Language: same as the system (system pt-BR)\n", report);
        Assert.Contains("Connection: good\n", report);
        Assert.Contains("Router opening: opened\n", report);
        Assert.Contains("Outside port: 40000\n", report);
        Assert.Contains("Router's outside address: public\n", report);
        Assert.Contains("Shared: 2 (ready 2)\n", report);
        Assert.Contains("Received: 1 (connecting 1)\n", report);
        Assert.EndsWith("2026-10-20 14:03 UTC  share.notFound\n2026-10-20 14:08 UTC  disk.full\n", report);
    }

    [Fact]
    public async Task The_report_holds_no_name_path_invite_or_address()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Ana's photos");
        await ShareAsync(Path.Combine(folder, "Birthday party.mp4"));
        await _transfers.ReceiveAsync("invite:secretkey");
        await _settings.SetAsync(
            null, null, null, publicServers: ["udp://open.example.org:6969"], helperAddress: "https://helper.example.net",
            helperKey: "a-very-long-helper-key", yourName: "Ana Souza");
        _engine.Report(new ReachabilityFacts(
            true, false, PortMappingState.Obtained, 40_000, IPAddress.Parse("100.70.0.1"), false, false, false));

        var report = await _support.ReportAsync();

        foreach (var secret in new[] { "Birthday", "photos", "Ana", Path.GetTempPath(), "secretkey", "invite:", "example", "helper-key", "100.70" })
            Assert.DoesNotContain(secret, report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Router's outside address: shared with other homes\n", report);
        Assert.Contains("Helper computer named: yes\n", report);
        Assert.Contains("Public servers named: 1\n", report);
    }

    [Fact]
    public async Task Only_the_last_twenty_problems_are_kept_and_nothing_but_a_code_is_written_down()
    {
        for (var i = 0; i < SupportUseCases.ProblemsKept + 5; i++)
        {
            _clock.Now = _clock.Now.AddMinutes(1);
            await _support.RecordAsync(i < 5 ? "share.duplicate" : "transfer.notFound");
        }
        await _support.RecordAsync("C:\\Users\\Ana\\file.txt");
        await _support.RecordAsync("two words");

        var report = await _support.ReportAsync();

        Assert.DoesNotContain("share.duplicate", report);
        Assert.Equal(SupportUseCases.ProblemsKept, report.Split("transfer.notFound").Length - 1);
        Assert.DoesNotContain("Ana", report);
        Assert.DoesNotContain("two words", report);
    }

    [Fact]
    public async Task With_nothing_shared_or_known_the_report_says_so()
    {
        var support = new SupportUseCases(_store, new FakeSystem("en"), _settings, _shares, _transfers, new ConnectionUseCases(_engine));

        var report = await support.ReportAsync();

        Assert.Contains("Version: not known\n", report);
        Assert.Contains("Connection: not known yet\n", report);
        Assert.DoesNotContain("Router opening", report);
        Assert.Contains("Shared: 0\n", report);
        Assert.EndsWith("Last problems, newest last:\nnone\n", report);
    }
}

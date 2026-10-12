using ComParta.Core.Ports;
using ComParta.Core.UseCases;

namespace ComParta.Core.Tests;

public sealed class NotificationUseCasesTests
{
    private static readonly string Video = Path.Combine(Path.GetTempPath(), "Videos", "birthday.mp4");

    private readonly FakeEngine _engine = new();
    private readonly FakeNotifier _notifier = new();
    private readonly FakeWindow _window = new();
    private readonly FakeSettingsRepository _settingsStore = new();
    private readonly FakeFileSystem _files = new();
    private readonly ManualClock _clock = new(DateTimeOffset.UnixEpoch);
    private readonly TransferUseCases _transfers;
    private readonly ShareUseCases _shares;
    private readonly SettingsUseCases _settings;
    private readonly NotificationUseCases _notifications;

    public NotificationUseCasesTests()
    {
        _files.Files[Video] = 10_000;
        _transfers = new TransferUseCases(new FakeTransferRepository(), _engine, _clock);
        _shares = new ShareUseCases(new FakeShareRepository(), _engine, new FakeFilePicker { NextPick = Video }, _files);
        _settings = new SettingsUseCases(_settingsStore, new FakeSystem("pt-BR"));
        var sending = new SendingUseCases(_engine, _shares, _clock);
        _notifications = new NotificationUseCases(_engine, _transfers, _shares, sending, _settings, _notifier, _window, _clock);
    }

    private async Task<TransferId> ReceiveAsync()
    {
        await _notifications.StartAsync();
        return (await _transfers.ReceiveAsync("invite:abc")).Value!.Transfer.Id;
    }

    [Fact]
    public async Task A_file_that_arrives_is_announced_in_the_persons_language_and_opens_its_details()
    {
        var id = await ReceiveAsync();
        _engine.Report(new ReceiveProgress(id, "film.mp4", 1_000, 500, false));
        Assert.Empty(_notifier.Shown);

        _engine.Report(new ReceiveProgress(id, "film.mp4", 1_000, 1_000, true));

        var shown = Assert.Single(_notifier.Shown);
        Assert.Equal("Um arquivo chegou", shown.Title);
        Assert.Equal("“film.mp4” está nos seus arquivos recebidos.", shown.Body);
        Assert.Equal((NotifiedItem.Transfer, id.ToString(), "film.mp4"), (shown.Kind, shown.Id, shown.Name));
    }

    [Fact]
    public async Task Nothing_is_shown_while_the_window_is_in_front()
    {
        var id = await ReceiveAsync();
        _window.IsInFront = true;

        _engine.Report(new ReceiveProgress(id, "film.mp4", 1_000, 1_000, true));

        Assert.Empty(_notifier.Shown);
    }

    [Fact]
    public async Task Someone_finishing_a_shared_file_is_announced_once_per_person()
    {
        await _settings.SetAsync("en", null, null);
        await _notifications.StartAsync();
        var id = (await _shares.AddAsync()).Value!.Share.Id;
        void Finished(int count) => _engine.Move(EngineActivity.None with
        {
            Shares = new Dictionary<ShareId, ItemActivity> { [id] = new(0, 0, 0, 0, 10_000, Finished: count) },
        });

        Finished(1);
        Finished(1);
        Finished(2);

        Assert.Equal(2, _notifier.Shown.Count);
        Assert.All(_notifier.Shown, n => Assert.Equal("Someone finished getting “birthday.mp4”.", n.Body));
        Assert.Equal(NotifiedItem.Share, _notifier.Shown[0].Kind);
    }

    [Fact]
    public async Task A_transfer_waiting_over_an_hour_is_announced_once()
    {
        var id = await ReceiveAsync();
        _engine.Report(new ReceiveProgress(id, "film.mp4", 1_000, 500, false));
        void Tick(TimeSpan after)
        {
            _clock.Now += after;
            _engine.Move(EngineActivity.None);
        }

        Tick(TransferUseCases.WaitingAfter);
        Assert.Equal(TransferState.Waiting, Assert.Single(_transfers.List()).State);
        Tick(TimeSpan.FromMinutes(59));
        Assert.Empty(_notifier.Shown);
        Tick(TimeSpan.FromMinutes(1));
        Tick(TimeSpan.FromMinutes(30));

        var shown = Assert.Single(_notifier.Shown);
        Assert.Equal("Um arquivo ainda está esperando", shown.Title);
    }

    [Theory]
    [InlineData(null, "pt-PT", "pt-BR")]
    [InlineData(null, "fr-FR", "en")]
    [InlineData("en", "pt-BR", "en")]
    [InlineData("xx", "en-GB", "en")]
    public void The_language_is_picked_as_the_screen_picks_it(string? saved, string system, string expected) =>
        Assert.Equal(expected, NotificationTexts.PickLanguage(saved, system));
}

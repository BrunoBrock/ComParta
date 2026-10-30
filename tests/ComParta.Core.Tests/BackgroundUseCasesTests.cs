using ComParta.Core.Ports;
using ComParta.Core.UseCases;

namespace ComParta.Core.Tests;

public sealed class BackgroundUseCasesTests
{
    private static readonly string Video = Path.Combine(Path.GetTempPath(), "Videos", "birthday.mp4");

    private readonly FakeEngine _engine = new();
    private readonly FakeFileSystem _files = new();
    private readonly FakeNotifier _notifier = new();
    private readonly FakePresence _presence = new();
    private readonly FakeSettingsRepository _store = new();
    private readonly ManualClock _clock = new(DateTimeOffset.UnixEpoch);
    private readonly ShareUseCases _shares;
    private readonly TransferUseCases _transfers;
    private readonly SettingsUseCases _settings;
    private readonly NotificationUseCases _notifications;
    private readonly ActivityUseCases _activity;
    private readonly BackgroundUseCases _background;

    public BackgroundUseCasesTests()
    {
        _files.Files[Video] = 10_000;
        _shares = new ShareUseCases(new FakeShareRepository(), _engine, new FakeFilePicker { NextPick = Video }, _files);
        _transfers = new TransferUseCases(new FakeTransferRepository(), _engine, _clock);
        _settings = new SettingsUseCases(_store, new FakeSystem("pt-BR"));
        var sending = new SendingUseCases(_engine, _shares, _clock);
        _notifications = new NotificationUseCases(_engine, _transfers, _shares, sending, _settings, _notifier, new FakeWindow(), _clock);
        _activity = new ActivityUseCases(_engine, _store);
        _background = new BackgroundUseCases(_engine, _shares, _transfers, sending, _settings, _store, _notifications, _presence, _clock, _activity);
    }

    private async Task<ShareId> ShareAsync() => (await _shares.AddAsync()).Value!.Share!.Share.Id;

    private async Task<TransferId> ReceiveAsync(string name)
    {
        var id = (await _transfers.ReceiveAsync("invite:" + name)).Value!.Transfer.Id;
        _engine.Report(new ReceiveProgress(id, name, 1_000, 100, false));
        return id;
    }

    private void Move(long receiving, long sending, ShareId? share = null, int receivers = 0) => _engine.Move(
        new EngineActivity(
            receiving,
            sending,
            share is { } id
                ? new Dictionary<ShareId, ItemActivity> { [id] = new(0, sending, 1, 0, 0, Receivers: receivers, ReceiversPercent: 10) }
                : new Dictionary<ShareId, ItemActivity>(),
            new Dictionary<TransferId, ItemActivity>()));

    [Fact]
    public async Task With_nothing_shared_or_moving_closing_the_window_quits()
    {
        await _background.StartAsync();

        Assert.False(_background.HasActiveWork);
        Assert.Equal(CloseChoice.Quit, _background.WindowClosing());
        Assert.Empty(_notifier.Shown);
    }

    [Fact]
    public async Task A_ready_share_hides_the_window_and_the_first_time_says_why_in_the_persons_language()
    {
        await _notifications.StartAsync();
        await _background.StartAsync();
        await ShareAsync();

        Assert.True(_background.HasActiveWork);
        Assert.Equal(CloseChoice.Hide, _background.WindowClosing());
        Assert.Equal(CloseChoice.Hide, _background.WindowClosing());

        var shown = Assert.Single(_notifier.Shown);
        Assert.Equal("O ComParta continua aberto", shown.Title);
        Assert.Null(shown.Kind);
        Assert.Equal("true", _store.Values["background.explained"]);
    }

    [Fact]
    public async Task Back_with_nothing_open_hides_while_a_file_is_shared_and_quits_otherwise()
    {
        await _background.StartAsync();
        var asked = 0;
        _background.BackAsked += () => asked++;

        _background.BackPressed();
        Assert.Equal(1, asked);
        Assert.Equal(CloseChoice.Quit, _background.Leave());
        Assert.Equal((1, 0), (_presence.Quits, _presence.Hides));

        await ShareAsync();
        Assert.Equal(CloseChoice.Hide, _background.Leave());
        Assert.Equal((1, 1), (_presence.Quits, _presence.Hides));
    }

    [Fact]
    public async Task The_explanation_is_not_repeated_after_a_restart()
    {
        _store.Values["background.explained"] = "true";
        await _background.StartAsync();
        await ShareAsync();

        Assert.Equal(CloseChoice.Hide, _background.WindowClosing());
        Assert.Empty(_notifier.Shown);
    }

    [Fact]
    public async Task An_unfinished_transfer_is_work_and_a_finished_one_is_not()
    {
        var id = await ReceiveAsync("film.mp4");
        Assert.True(_background.HasActiveWork);

        _engine.Report(new ReceiveProgress(id, "film.mp4", 1_000, 1_000, true));

        Assert.False(_background.HasActiveWork);
    }

    [Fact]
    public async Task Quitting_while_nothing_moves_quits_at_once()
    {
        await ShareAsync();

        var answer = _background.Quit(force: false);

        Assert.True(answer.Quitting);
        Assert.Equal(1, _presence.Quits);
    }

    [Fact]
    public async Task Quitting_while_someone_gets_a_file_or_one_is_arriving_asks_first_with_their_names()
    {
        var share = await ShareAsync();
        await ReceiveAsync("film.mp4");
        Move(1_000, 1_000, share, receivers: 1);

        var answer = _background.Quit(force: false);

        Assert.False(answer.Quitting);
        Assert.Equal(["birthday.mp4", "film.mp4"], answer.Moving);
        Assert.Equal(0, _presence.Quits);

        Assert.True(_background.Quit(force: true).Quitting);
        Assert.Equal(1, _presence.Quits);
    }

    [Fact]
    public async Task Quitting_from_the_icon_while_files_move_asks_the_screen()
    {
        await ReceiveAsync("film.mp4");
        QuitAnswer? asked = null;
        _background.QuitAsked += answer => asked = answer;

        Assert.False(_background.QuitFromOutside());

        Assert.Equal(["film.mp4"], asked!.Moving);
        Assert.Equal(0, _presence.Quits);
    }

    [Fact]
    public void The_computer_stays_awake_while_bytes_move_and_may_sleep_a_while_after()
    {
        Move(0, 0);
        Assert.Empty(_presence.Awake);

        Move(5_000, 0);
        _clock.Now += TimeSpan.FromSeconds(10);
        Move(0, 2_000);
        Assert.Equal([true], _presence.Awake);

        _clock.Now += TimeSpan.FromSeconds(30);
        Move(0, 0);
        Assert.Equal([true], _presence.Awake);

        _clock.Now += BackgroundUseCases.AwakeAfterMoving;
        Move(0, 0);
        Assert.Equal([true, false], _presence.Awake);
    }

    [Fact]
    public async Task Start_with_Windows_follows_the_setting_and_is_off_until_the_person_agrees()
    {
        await _background.StartAsync();
        Assert.Equal([false], _presence.StartWithSystem);

        await _settings.SetAsync(null, null, null, startWithWindows: true);
        await Task.Yield();

        Assert.Equal([false, true], _presence.StartWithSystem);
        Assert.True((await _settings.GetAsync()).StartWithWindows);
    }

    /// <summary>A transfer half received, with bytes moving now.</summary>
    private async Task<TransferId> HalfReceivedAsync(string name)
    {
        var id = (await _transfers.ReceiveAsync("invite:" + name)).Value!.Transfer.Id;
        HalfReceived(id, name);
        return id;
    }

    private void HalfReceived(TransferId id, string name)
    {
        _engine.Report(new ReceiveProgress(id, name, 1_000, 450, false, BytesPerSecond: 100));
        Move(100, 0);
    }

    [Fact]
    public async Task The_phone_notification_appears_when_bytes_start_moving_names_the_file_and_goes_a_minute_after_they_stop()
    {
        await _notifications.StartAsync();
        await _background.StartAsync();
        var id = (await _transfers.ReceiveAsync("invite:Natal.mp4")).Value!.Transfer.Id;
        Assert.Empty(_presence.Moving);

        HalfReceived(id, "Natal.mp4");

        var shown = _presence.Moving[^1]!;
        Assert.Equal("Recebendo “Natal.mp4”: 45 %", shown.Title);
        Assert.Equal(45, shown.Percent);
        Assert.Equal("Parar", shown.Stop);
        Assert.False(shown.Paused);
        Assert.False(string.IsNullOrWhiteSpace(shown.Why));

        _clock.Now += TimeSpan.FromSeconds(30);
        Move(0, 0);
        Assert.NotNull(_presence.Moving[^1]);

        _clock.Now += BackgroundUseCases.ShownAfterMoving;
        Move(0, 0);
        Assert.Null(_presence.Moving[^1]);
        Assert.Equal(1, _presence.Moving.Count(notice => notice is null));
    }

    [Fact]
    public async Task Bytes_that_arrive_show_the_notification_even_before_the_engine_measures_a_speed()
    {
        await _notifications.StartAsync();
        await _background.StartAsync();
        var id = (await _transfers.ReceiveAsync("invite:Natal.mp4")).Value!.Transfer.Id;

        _engine.Report(new ReceiveProgress(id, "Natal.mp4", 1_000, 300, false));

        Assert.Equal(30, _presence.Moving[^1]!.Percent);
    }

    [Fact]
    public async Task Sending_and_receiving_several_files_are_counted_in_one_line_in_English()
    {
        await _settings.SetAsync("en", null, null);
        await _notifications.StartAsync();
        var share = await ShareAsync();
        var receiving = (await _transfers.ReceiveAsync("invite:film.mp4")).Value!.Transfer.Id;
        _engine.Report(new ReceiveProgress(receiving, "film.mp4", 1_000, 100, false, BytesPerSecond: 100));

        Move(100, 100, share, receivers: 1);

        Assert.Equal("Sending and receiving 2 files: 10 %", _presence.Moving[^1]!.Title);
        Assert.Equal("Stop", _presence.Moving[^1]!.Stop);
    }

    [Fact]
    public async Task Stop_on_the_notification_stops_sending_and_receiving_as_the_speed_bar_does_and_the_notification_goes()
    {
        await _activity.StartAsync();
        await HalfReceivedAsync("Natal.mp4");
        Assert.NotNull(_presence.Moving[^1]);

        await _background.StopFromNoticeAsync();

        Assert.Equal((true, true), _engine.StoppedFlags);
        Assert.True(_activity.Current() is { SendingStopped: true, ReceivingStopped: true });
        Assert.Equal("true", _store.Values["activity.sendingStopped"]);
        Assert.Null(_presence.Moving[^1]);
    }

    [Fact]
    public async Task When_the_phone_allows_no_more_time_everything_pauses_says_so_and_continues_when_the_app_opens()
    {
        await _notifications.StartAsync();
        await _activity.StartAsync();
        var id = await HalfReceivedAsync("Natal.mp4");

        await _background.TimedOutAsync();

        Assert.Equal((true, true), _engine.StoppedFlags);
        var paused = _presence.Moving[^1]!;
        Assert.True(paused.Paused);
        Assert.Equal(("Pausado para economizar bateria", "Abra o ComParta para continuar."), (paused.Title, paused.Body));
        Assert.Null(paused.Stop);
        // A pause for time is not the person's stop: nothing is remembered for the next start.
        Assert.False(_store.Values.ContainsKey("activity.sendingStopped"));
        // The paused notice stays however long nothing moves.
        _clock.Now += TimeSpan.FromMinutes(10);
        Move(0, 0);
        Assert.True(_presence.Moving[^1]!.Paused);

        await _background.AppOpenedAsync();

        Assert.Equal((false, false), _engine.StoppedFlags);
        Assert.Null(_presence.Moving[^1]);
        _engine.Report(new ReceiveProgress(id, "Natal.mp4", 1_000, 460, false, BytesPerSecond: 100));
        Move(100, 0);
        Assert.Equal("Recebendo “Natal.mp4”: 46 %", _presence.Moving[^1]!.Title);
    }

    [Fact]
    public async Task Opening_the_app_keeps_a_stop_the_person_chose()
    {
        await _activity.StartAsync();
        await _activity.SetStoppedAsync(sending: true, receiving: false);

        await _background.AppOpenedAsync();

        Assert.Equal((true, false), _engine.StoppedFlags);
    }
}

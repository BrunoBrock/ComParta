using ComParta.Core.Ports;

namespace ComParta.Core.UseCases;

/// <summary>What closing the window does: hide it while there is work, else quit (AD-14).</summary>
public enum CloseChoice { Hide, Quit }

/// <summary>The answer to quitting: it goes ahead, or these files would stop moving and the person is asked first.</summary>
public sealed record QuitAnswer(bool Quitting, IReadOnlyList<string> Moving);

/// <summary>
/// Keeps sharing when the window is closed (CAP-17, AD-14): whether there is work, whether the
/// computer must stay awake, starting with Windows, and quitting without cutting anyone off.
/// </summary>
public sealed class BackgroundUseCases
{
    /// <summary>How long the computer stays awake after the last bytes moved.</summary>
    public static readonly TimeSpan AwakeAfterMoving = TimeSpan.FromSeconds(45);

    /// <summary>How long the phone's notification stays after the last bytes moved (epic 7 story 7).</summary>
    public static readonly TimeSpan ShownAfterMoving = TimeSpan.FromMinutes(1);

    private const string ExplainedKey = "background.explained";

    private readonly ITransferEngine _engine;
    private readonly ShareUseCases _shares;
    private readonly TransferUseCases _transfers;
    private readonly SendingUseCases _sending;
    private readonly SettingsUseCases _settings;
    private readonly ISettingsRepository _store;
    private readonly NotificationUseCases _notifications;
    private readonly IBackgroundPresence _presence;
    private readonly ActivityUseCases? _activity;
    private readonly TimeProvider _clock;
    private readonly object _lock = new();
    private readonly object _showing = new();
    private DateTimeOffset? _lastMoved;
    private bool _awake;
    private bool _explained;
    private bool? _startWithSystem;
    private MovingNotice? _shown;
    private long _receivedSoFar;
    private bool _bothStopped;
    private bool _pausedForTime;

    public BackgroundUseCases(
        ITransferEngine engine,
        ShareUseCases shares,
        TransferUseCases transfers,
        SendingUseCases sending,
        SettingsUseCases settings,
        ISettingsRepository store,
        NotificationUseCases notifications,
        IBackgroundPresence presence,
        TimeProvider? clock = null,
        ActivityUseCases? activity = null)
    {
        _engine = engine;
        _shares = shares;
        _transfers = transfers;
        _sending = sending;
        _settings = settings;
        _store = store;
        _notifications = notifications;
        _presence = presence;
        _activity = activity;
        _clock = clock ?? TimeProvider.System;
        _engine.ActivityChanged += OnActivityChanged;
        _transfers.Changed += OnTransfersChanged;
        if (_activity is not null)
            _activity.Changed += OnStoppedChanged;
        _settings.Changed += () => _ = ApplyStartWithSystemAsync();
    }

    /// <summary>Raised when quitting from outside the window found files moving; the screen asks the person.</summary>
    public event Action<QuitAnswer>? QuitAsked;

    /// <summary>
    /// Raised when the person pressed the system's back button (Android). The screen closes what is
    /// open on top, and leaves with <see cref="Leave"/> when nothing is.
    /// </summary>
    public event Action? BackAsked;

    /// <summary>Reads whether the first hide was explained already, and applies start with Windows.</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        var explained = await _store.GetAsync(ExplainedKey, ct) == "true";
        lock (_lock)
            _explained |= explained;
        await ApplyStartWithSystemAsync(ct);
    }

    /// <summary>Anything shared and ready, or any transfer not finished (AD-14).</summary>
    public bool HasActiveWork =>
        _shares.Current().Any(view => view.Status == ShareStatus.Ready)
        || _transfers.List().Any(view => view.State is not (TransferState.Done or TransferState.Failed or TransferState.Blocked));

    /// <summary>The shell asks this when the person closes the window. The first hide says why once.</summary>
    public CloseChoice WindowClosing()
    {
        if (!HasActiveWork)
            return CloseChoice.Quit;
        bool explain;
        lock (_lock)
        {
            explain = !_explained;
            _explained = true;
        }
        if (explain)
        {
            _notifications.ExplainBackground();
            _ = RememberExplainedAsync();
        }
        return CloseChoice.Hide;
    }

    /// <summary>The shell calls this when the person presses the system's back button.</summary>
    public void BackPressed() => BackAsked?.Invoke();

    /// <summary>
    /// Back with nothing open on the screen: the same as closing the window, so the app stays out of
    /// sight while there is work and ends otherwise (AD-14).
    /// </summary>
    public CloseChoice Leave()
    {
        var choice = WindowClosing();
        if (choice == CloseChoice.Hide)
            _presence.Hide();
        else
            _presence.Quit();
        return choice;
    }

    /// <summary>
    /// What would stop if the app quit now: shared files someone is getting, and files this
    /// computer is receiving.
    /// </summary>
    public IReadOnlyList<string> Moving()
    {
        var sending = _shares.Current()
            .Where(view => _sending.For(view.Share.Id).ReceiverCount > 0)
            .Select(view => view.Share.Name);
        var receiving = _transfers.List()
            .Where(view => view.State == TransferState.Receiving)
            .Select(view => view.Transfer.Name ?? "");
        return [.. sending.Concat(receiving)];
    }

    /// <summary>Quits, unless files are moving and the person has not confirmed (<paramref name="force"/>).</summary>
    public QuitAnswer Quit(bool force)
    {
        var moving = force ? [] : Moving();
        if (moving.Count > 0)
            return new QuitAnswer(false, moving);
        _presence.Quit();
        return new QuitAnswer(true, []);
    }

    /// <summary>
    /// Quitting from the icon's menu: the same check, and when files are moving the screen is asked
    /// to confirm. Returns false when the window should be shown for that.
    /// </summary>
    public bool QuitFromOutside()
    {
        var answer = Quit(force: false);
        if (!answer.Quitting)
            QuitAsked?.Invoke(answer);
        return answer.Quitting;
    }

    /// <summary>
    /// The Stop button on the phone's notification: the same stop as the speed bar's Stop, kept
    /// after a restart (AD-22). The screen shows it stopped.
    /// </summary>
    public Task StopFromNoticeAsync() =>
        _activity?.SetStoppedAsync(sending: true, receiving: true) ?? Task.CompletedTask;

    /// <summary>
    /// The phone allowed no more time for working with the screen off (Android 15 gives six hours):
    /// everything is paused, not remembered as the person's stop, and the notification says to open
    /// ComParta, which continues it (<see cref="AppOpened"/>).
    /// </summary>
    public async Task TimedOutAsync()
    {
        lock (_lock)
            _pausedForTime = true;
        if (_activity is not null)
            await _activity.StopForNowAsync();
        ShowMoving();
    }

    /// <summary>The person opened the app: what was paused for lack of time continues.</summary>
    public async Task AppOpenedAsync()
    {
        lock (_lock)
        {
            if (!_pausedForTime)
                return;
            _pausedForTime = false;
            _lastMoved = null;
        }
        if (_activity is not null)
            await _activity.StartAsync();
        ShowMoving();
    }

    private void OnStoppedChanged(Activity activity)
    {
        lock (_lock)
            _bothStopped = activity.SendingStopped && activity.ReceivingStopped;
        ShowMoving();
    }

    /// <summary>
    /// What the phone's notification says now: the files moving and how far they are, until a minute
    /// after the last bytes moved; nothing once the person stopped everything.
    /// </summary>
    private MovingNotice? Notice()
    {
        bool paused, moving;
        lock (_lock)
        {
            paused = _pausedForTime;
            moving = !_bothStopped && _lastMoved is { } last && _clock.GetUtcNow() - last < ShownAfterMoving;
        }
        string Text(string key, string name = "", int count = 0, int percent = 0) => _notifications.Text(key, name, count, percent);
        var why = Text("notify.moving.why");
        var whyOk = Text("notify.moving.whyOk");
        if (paused)
            return new MovingNotice(Text("notify.paused.title"), Text("notify.paused.body"), null, null, why, whyOk, Paused: true);
        if (!moving)
            return null;

        var receiving = _transfers.List()
            .Where(view => view.State == TransferState.Receiving)
            .Select(view => (Name: view.Transfer.Name ?? "", Percent: view.Transfer.SizeBytes is > 0 and var size
                ? (int)Math.Clamp(view.ReceivedBytes * 100 / size, 0, 100) : 0))
            .ToList();
        var sending = _shares.Current()
            .Select(view => (Name: view.Share.Name, Sending: _sending.For(view.Share.Id)))
            .Where(item => item.Sending.ReceiverCount > 0)
            .Select(item => (item.Name, item.Sending.Percent))
            .ToList();
        var all = sending.Concat(receiving).ToList();
        if (all.Count == 0)
            return new MovingNotice(Text("notify.moving.other"), Text("notify.moving.body"), null, Text("notify.moving.stop"), why, whyOk);
        var percent = (int)Math.Round(all.Average(item => item.Percent));
        var title = (sending.Count, receiving.Count) switch
        {
            (1, 0) => Text("notify.moving.sendingOne", sending[0].Name, 1, percent),
            (_, 0) => Text("notify.moving.sendingMany", "", sending.Count, percent),
            (0, 1) => Text("notify.moving.receivingOne", receiving[0].Name, 1, percent),
            (0, _) => Text("notify.moving.receivingMany", "", receiving.Count, percent),
            _ => Text("notify.moving.both", "", all.Count, percent),
        };
        return new MovingNotice(title, Text("notify.moving.body"), percent, Text("notify.moving.stop"), why, whyOk);
    }

    /// <summary>
    /// Changes arrive from several threads; one at a time, so an older notice never replaces a newer one.
    /// </summary>
    private void ShowMoving()
    {
        lock (_showing)
        {
            MovingNotice? notice;
            try { notice = Notice(); }
            catch (Exception) { return; }
            lock (_lock)
            {
                if (notice == _shown)
                    return;
                _shown = notice;
            }
            try { _presence.ShowMoving(notice); }
            catch (Exception) { /* the transfer continues while the screen is on */ }
        }
    }

    /// <summary>Bytes that arrived count as moving too, even between two of the engine's looks at its speed.</summary>
    private void OnTransfersChanged()
    {
        long received;
        try { received = _transfers.List().Where(view => view.State == TransferState.Receiving).Sum(view => view.ReceivedBytes); }
        catch (Exception) { return; }
        lock (_lock)
        {
            if (received > _receivedSoFar)
                _lastMoved = _clock.GetUtcNow();
            _receivedSoFar = received;
        }
        ShowMoving();
    }

    private void OnActivityChanged()
    {
        var now = _clock.GetUtcNow();
        EngineActivity activity;
        try { activity = _engine.GetActivity(); }
        catch (Exception) { return; }
        bool? change = null;
        lock (_lock)
        {
            if (activity.ReceivingBytesPerSecond + activity.SendingBytesPerSecond > 0)
                _lastMoved = now;
            var awake = _lastMoved is { } last && now - last < AwakeAfterMoving;
            if (awake != _awake)
                change = _awake = awake;
        }
        if (change is { } keep)
        {
            try { _presence.KeepAwake(keep); }
            catch (Exception) { /* the computer may sleep; the transfer continues after */ }
        }
        ShowMoving();
    }

    private async Task ApplyStartWithSystemAsync(CancellationToken ct = default)
    {
        try
        {
            var on = (await _settings.GetAsync(ct)).StartWithWindows;
            lock (_lock)
            {
                if (_startWithSystem == on)
                    return;
                _startWithSystem = on;
            }
            _presence.StartWithSystem(on);
        }
        catch (Exception) { /* tried again at the next start */ }
    }

    private async Task RememberExplainedAsync()
    {
        try { await _store.SetAsync(ExplainedKey, "true"); }
        catch (Exception) { /* explained again next time, which does no harm */ }
    }
}

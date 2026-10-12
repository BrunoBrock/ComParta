using ComParta.Core.Ports;

namespace ComParta.Core.UseCases;

/// <summary>
/// System notifications for what happens while the person is not looking (CAP-20, UX design
/// "Notifications and toasts"): a file arrived, someone finished getting a shared file, and a
/// transfer has waited over an hour. Never while the window is in front.
/// </summary>
public sealed class NotificationUseCases
{
    public static readonly TimeSpan WaitingTooLong = TimeSpan.FromHours(1);

    private readonly TransferUseCases _transfers;
    private readonly ShareUseCases _shares;
    private readonly SendingUseCases _sending;
    private readonly SettingsUseCases _settings;
    private readonly INotifier _notifier;
    private readonly IWindowState _window;
    private readonly TimeProvider _clock;
    private readonly object _lock = new();
    private readonly Dictionary<TransferId, TransferState> _states = [];
    private readonly Dictionary<TransferId, DateTimeOffset> _waitingSince = [];
    private readonly HashSet<TransferId> _toldWaiting = [];
    private readonly Dictionary<ShareId, int> _finished = [];
    private string _language = "en";

    public NotificationUseCases(
        ITransferEngine engine,
        TransferUseCases transfers,
        ShareUseCases shares,
        SendingUseCases sending,
        SettingsUseCases settings,
        INotifier notifier,
        IWindowState window,
        TimeProvider? clock = null)
    {
        _transfers = transfers;
        _shares = shares;
        _sending = sending;
        _settings = settings;
        _notifier = notifier;
        _window = window;
        _clock = clock ?? TimeProvider.System;
        _transfers.Changed += OnTransfersChanged;
        _sending.Changed += OnSendingChanged;
        _settings.Changed += () => _ = LoadLanguageAsync();
        // The wait is measured on the engine's tick, so it is noticed without anything changing.
        engine.ActivityChanged += OnTransfersChanged;
    }

    /// <summary>Raised when the person clicked a notification; the screen opens that item's details.</summary>
    public event Action<NotifiedItem, string, string>? OpenRequested;

    /// <summary>Reads the language once; later changes arrive through the settings.</summary>
    public Task StartAsync(CancellationToken ct = default) => LoadLanguageAsync(ct);

    /// <summary>The shell calls this with what a clicked notification carried.</summary>
    public void Open(NotifiedItem kind, string id, string name) => OpenRequested?.Invoke(kind, id, name);

    private async Task LoadLanguageAsync(CancellationToken ct = default)
    {
        try
        {
            var settings = await _settings.GetAsync(ct);
            lock (_lock)
                _language = NotificationTexts.PickLanguage(settings.Language, settings.SystemLanguage);
        }
        catch (Exception) { /* English until the next change */ }
    }

    private void OnTransfersChanged()
    {
        var now = _clock.GetUtcNow();
        var due = new List<Notification>();
        lock (_lock)
        {
            var seen = new HashSet<TransferId>();
            foreach (var view in _transfers.List())
            {
                var id = view.Transfer.Id;
                seen.Add(id);
                var name = view.Transfer.Name ?? "";
                if (_states.TryGetValue(id, out var before) && before != TransferState.Done && view.State == TransferState.Done)
                    due.Add(Say("arrived", name, NotifiedItem.Transfer, id.ToString()));
                _states[id] = view.State;

                if (view.State != TransferState.Waiting)
                {
                    _waitingSince.Remove(id);
                    _toldWaiting.Remove(id);
                    continue;
                }
                if (!_waitingSince.TryGetValue(id, out var since))
                    _waitingSince[id] = since = now;
                if (now - since >= WaitingTooLong && _toldWaiting.Add(id))
                    due.Add(Say("waiting", name, NotifiedItem.Transfer, id.ToString()));
            }
            foreach (var gone in _states.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                _states.Remove(gone);
                _waitingSince.Remove(gone);
                _toldWaiting.Remove(gone);
            }
        }
        Show(due);
    }

    private void OnSendingChanged()
    {
        var due = new List<Notification>();
        lock (_lock)
        {
            foreach (var view in _shares.Current())
            {
                var id = view.Share.Id;
                var finished = _sending.For(id).FinishedCount;
                if (finished > _finished.GetValueOrDefault(id))
                    due.Add(Say("finished", view.Share.Name, NotifiedItem.Share, id.ToString()));
                _finished[id] = finished;
            }
        }
        Show(due);
    }

    private Notification Say(string what, string name, NotifiedItem kind, string id) => new(
        NotificationTexts.Format(_language, $"notify.{what}.title", name),
        NotificationTexts.Format(_language, $"notify.{what}.body", name),
        kind,
        id,
        name);

    private void Show(List<Notification> due)
    {
        if (due.Count == 0 || _window.IsInFront)
            return;
        foreach (var notification in due)
        {
            try { _notifier.Show(notification); }
            catch (Exception) { /* a notification that cannot be shown is not worth stopping for */ }
        }
    }
}

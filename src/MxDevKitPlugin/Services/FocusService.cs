namespace Loupedeck.MxDevKitPlugin;

internal sealed class FocusService : IDisposable
{
    private readonly object _sync = new();
    private Timer? _timer;
    private string? _lastKey;

    public FocusService(FocusConfig config)
    {
        this.Timer = new FocusTimer(TimeSpan.FromMinutes(config.Minutes), TimeSpan.FromMinutes(config.BreakMinutes));
    }

    public event Action? Changed;

    public FocusTimer Timer { get; }

    public void Start() => _timer = new Timer(_ => this.Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void Apply(FocusConfig config)
    {
        lock (_sync)
        {
            this.Timer.FocusLength = TimeSpan.FromMinutes(config.Minutes);
            this.Timer.BreakLength = TimeSpan.FromMinutes(config.BreakMinutes);
        }

        this.Redraw(force: true);
    }

    public void Click()
    {
        lock (_sync)
        {
            this.Timer.Click(DateTime.UtcNow);
        }

        this.Redraw(force: true);
    }

    public Bubble Bubble(Strings strings)
    {
        lock (_sync)
        {
            return Bubbles.Focus(this.Timer, strings, DateTime.UtcNow);
        }
    }

    private void Tick()
    {
        FocusEvent e;
        lock (_sync)
        {
            e = this.Timer.Tick(DateTime.UtcNow);
        }

        if (e == FocusEvent.FocusEnded)
        {
            DevKit.Haptics.Raise(HapticEvents.FocusEnded);
        }
        else if (e == FocusEvent.BreakEnded)
        {
            DevKit.Haptics.Raise(HapticEvents.BreakEnded);
        }

        this.Redraw(force: e != FocusEvent.None);
    }

    // Redraw only when what the bubble shows changes: the value, or the arc by 1/60 of a turn.
    private void Redraw(bool force)
    {
        string key;
        lock (_sync)
        {
            var now = DateTime.UtcNow;
            key = $"{this.Timer.Phase}|{FocusTimer.Format(this.Timer.Remaining(now))}|{(int)(this.Timer.Progress(now) * 60)}";
        }

        if (!force && key == _lastKey)
        {
            return;
        }

        _lastKey = key;
        this.Changed?.Invoke();
    }
}

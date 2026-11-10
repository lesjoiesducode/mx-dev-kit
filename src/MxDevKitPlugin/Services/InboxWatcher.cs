namespace Loupedeck.MxDevKitPlugin;

// The one GitHub poller for what concerns you across GitHub, always through `gh api` and your
// `gh auth login` session (no other token, no webhook). Two calls per minute:
//   - GraphQL: review requests (the review bubble), your open pull requests (approved, changes
//     requested, CI red) and your recently merged ones;
//   - GET /notifications with If-Modified-Since, at the pace GitHub asks for (X-Poll-Interval):
//     mentions, team mentions, assignments. A 304 does not count against the rate limit.
// Nothing is ever marked as read on GitHub: what was already seen is remembered here.
internal sealed class InboxWatcher : IDisposable
{
    private const string NotificationsPage = "https://github.com/notifications";
    private const int MaxNotificationsPerPoll = 3;

    private readonly object _sync = new();
    private readonly InboxTracker _tracker = new();
    private readonly NotificationTracker _threads = new();
    private Timer? _timer;
    private int _ticking;
    private bool _warned;
    private bool _notificationsWarned;
    private DateTime _lastQuery = DateTime.MinValue;
    private DateTime _nextNotifications = DateTime.MinValue;
    private string? _lastModified;
    private string? _etag;
    private string? _login;
    private InboxEvent? _latest;
    private bool _latestSeen;

    public event Action? Changed;

    public int? ReviewCount { get; private set; }

    public bool Available { get; private set; } = true;

    public (InboxEvent? Latest, bool Seen) Latest
    {
        get
        {
            lock (_sync)
            {
                return (_latest, _latestSeen);
            }
        }
    }

    // Checks every 5 s whether something is due, so a new value in config.json applies right away.
    public void Start() => _timer = new Timer(_ => this.Tick(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void RefreshNow()
    {
        _lastQuery = DateTime.MinValue;
        _nextNotifications = DateTime.MinValue;
        ProcessRunner.Detach(this.Tick);
    }

    // The bubble was clicked: opens the last event (or the GitHub notifications) and greys the bubble.
    public void Open()
    {
        string url;
        lock (_sync)
        {
            url = _latest?.Url ?? NotificationsPage;
            _latestSeen = true;
        }

        ProcessRunner.Open(url);
        this.Changed?.Invoke();
    }

    // echo inbox-test > trigger: a made-up event, to check the vibration, the notification and the bubble.
    public void Test() => this.Deliver(new[]
    {
        new InboxEvent(InboxKind.Merged, "lesjoiesducode/mx-dev-kit", null, "MX Dev Kit test event", NotificationsPage),
    });

    private void Tick()
    {
        if (Interlocked.Exchange(ref _ticking, 1) == 1)
        {
            return;
        }

        try
        {
            var config = DevKit.Config.GitHub;
            var now = DateTime.UtcNow;
            var queryDue = now - _lastQuery >= TimeSpan.FromSeconds(config.RefreshSeconds);
            var notificationsDue = now >= _nextNotifications;
            if (!queryDue && !notificationsDue)
            {
                return;
            }

            var gh = config.Enabled ? ToolLocator.Find("gh") : null;
            if (gh == null)
            {
                _lastQuery = now;
                this.SetReviews(null, available: false, config.Enabled ? "gh not found: the GitHub inbox and review requests are off" : null);
                return;
            }

            // The query first: it gives the login that mention checks need.
            if (queryDue)
            {
                _lastQuery = now;
                this.RefreshQuery(gh, config);
            }

            if (notificationsDue && this.Available)
            {
                this.RefreshNotifications(gh, config);
            }
        }
        catch (Exception e)
        {
            PluginLog.Warning(e, "GitHub inbox refresh failed");
        }
        finally
        {
            Interlocked.Exchange(ref _ticking, 0);
        }
    }

    private void RefreshQuery(string gh, GitHubConfig config)
    {
        var result = ProcessRunner.Run(gh, null, TimeSpan.FromSeconds(30), new[]
        {
            "api", "graphql", "-f", "query=" + GitHubInbox.Query,
            "-F", "reviews=" + config.ReviewQuery,
            "-F", "open=" + GitHubInbox.OpenQuery,
            "-F", "merged=" + GitHubInbox.MergedQuery(DateTime.UtcNow),
        });

        if (result is { Ok: false } && result.Stderr.Contains("gh auth login", StringComparison.OrdinalIgnoreCase))
        {
            this.SetReviews(null, available: false, "gh is not signed in: run gh auth login");
            return;
        }

        // GraphQL answers partial data with an error for the part that failed: keep the rest.
        var snapshot = GitHubInbox.Snapshot(result?.Stdout);
        if (snapshot == null)
        {
            if (result != null)
            {
                PluginLog.Info($"GitHub inbox query: exit {result.ExitCode} {FirstLine(result.Stderr)}");
            }

            return; // network hiccup: keep what is shown
        }

        _login = snapshot.Login ?? _login;
        this.SetReviews(snapshot.ReviewTotal ?? this.ReviewCount, available: true, null);
        this.Deliver(_tracker.Update(snapshot));
    }

    private void RefreshNotifications(string gh, GitHubConfig config)
    {
        var args = new List<string> { "api", "-i", GitHubInbox.NotificationsPath };
        if (_lastModified != null)
        {
            args.AddRange(new[] { "-H", "If-Modified-Since: " + _lastModified });
        }
        else if (_etag != null)
        {
            args.AddRange(new[] { "-H", "If-None-Match: " + _etag });
        }

        var result = ProcessRunner.Run(gh, null, TimeSpan.FromSeconds(20), args);
        var page = GitHubInbox.Notifications(result?.Stdout);
        var wait = TimeSpan.FromSeconds(Math.Max(config.RefreshSeconds, page?.PollInterval ?? 60));
        _nextNotifications = DateTime.UtcNow + wait;
        if (page == null)
        {
            return;
        }

        if (page.Status is 401 or 403 or 404)
        {
            // A token without the notifications or repo scope (GH_TOKEN, fine-grained token).
            if (!_notificationsWarned)
            {
                _notificationsWarned = true;
                PluginLog.Warning($"GET /notifications answered {page.Status}: mentions and assignments are off (gh auth refresh -s notifications)");
            }

            _nextNotifications = DateTime.UtcNow + TimeSpan.FromMinutes(10);
            return;
        }

        if (page.Status != 200 || page.Threads == null)
        {
            return; // 304: nothing new
        }

        _notificationsWarned = false;
        _lastModified = page.LastModified;
        _etag = page.ETag;

        var events = new List<InboxEvent>();
        foreach (var (thread, kind, checkComment) in _threads.Update(page.Threads))
        {
            var url = GitHubInbox.HtmlUrl(thread.SubjectUrl, thread.Repo);
            if (checkComment && thread.LatestCommentUrl != null && _login != null)
            {
                // Same thread, new activity: only a comment that mentions you again counts.
                var comment = GitHubInbox.Comment(ProcessRunner.Run(gh, null, TimeSpan.FromSeconds(20), new[] { "api", thread.LatestCommentUrl })?.Stdout);
                if (comment is { } c)
                {
                    if (!GitHubInbox.Mentions(c.Body, _login))
                    {
                        continue;
                    }

                    url = c.HtmlUrl ?? url;
                }
            }

            events.Add(new InboxEvent(kind, thread.Repo, GitHubInbox.NumberOf(thread.SubjectUrl), thread.Title, url));
        }

        this.Deliver(events.OrderBy(e => e.Kind).ToList());
    }

    // One vibration per poll (the most important event), one notification per event (three at most).
    private void Deliver(IReadOnlyList<InboxEvent> events)
    {
        var config = DevKit.Config.GitHub;
        var wanted = events.Where(e => config.Events.IsOn(e.Kind)).ToList();
        if (wanted.Count == 0)
        {
            return;
        }

        lock (_sync)
        {
            _latest = wanted[0];
            _latestSeen = false;
        }

        foreach (var e in wanted)
        {
            PluginLog.Info($"GitHub inbox: {e.Kind} {Strings.InboxText(e)} {e.Url}");
        }

        var buzz = wanted.FirstOrDefault(e => !this.CiBubbleCovers(e, config));
        if (buzz != null)
        {
            DevKit.Haptics.Raise(HapticEvents.For(buzz.Kind));
        }

        var s = DevKit.Strings;
        foreach (var e in wanted.Take(MaxNotificationsPerPoll))
        {
            Notifier.Show(s.InboxTitle(e), Strings.InboxText(e), e.Url);
        }

        if (wanted.Count > MaxNotificationsPerPoll)
        {
            Notifier.Show(s.ShortInbox, s.InboxMoreText(wanted.Count - MaxNotificationsPerPoll), NotificationsPage);
        }

        this.Changed?.Invoke();
    }

    // The CI bubble already vibrates when the run of the current branch fails: no second vibration
    // for the same failure (the notification still shows).
    private bool CiBubbleCovers(InboxEvent e, GitHubConfig config)
    {
        var view = DevKit.Project.View;
        return e.Kind == InboxKind.CiFailed && config.NotifyCi && e.Branch != null
            && string.Equals(view.Repo, e.Repo, StringComparison.OrdinalIgnoreCase) && view.Branch == e.Branch;
    }

    private void SetReviews(int? count, bool available, string? warning)
    {
        if (warning != null && !_warned)
        {
            _warned = true;
            PluginLog.Warning(warning);
        }

        if (available)
        {
            _warned = false;
        }

        if (count == this.ReviewCount && available == this.Available)
        {
            return;
        }

        this.ReviewCount = count;
        this.Available = available;
        this.Changed?.Invoke();
    }

    private static string FirstLine(string text)
    {
        var line = text.Trim().Split('\n')[0].Trim();
        return line.Length > 200 ? line[..200] : line;
    }
}

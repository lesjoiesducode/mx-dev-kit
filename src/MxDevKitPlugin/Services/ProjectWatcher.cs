namespace Loupedeck.MxDevKitPlugin;

// Follows the current project (last folder used in a terminal with the shell integration, or in the Finder),
// brought back to the root of its git repository, and keeps the ProjectView the bubbles draw.
internal sealed class ProjectWatcher : IDisposable
{
    private static readonly TimeSpan TickEvery = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan BranchEvery = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LocalEvery = TimeSpan.FromSeconds(4);

    // How long GitHub is polled at the running pace after a push.
    private static readonly TimeSpan PushWatch = TimeSpan.FromMinutes(2);

    // A lockfile that changed (npm install, poetry lock...) gets a new scan once it has settled.
    private static readonly TimeSpan AuditAfterLockfileChange = TimeSpan.FromSeconds(5);

    // The plugin runs git every few seconds while you work: without optional locks, `git status` never
    // takes .git/index.lock, so it cannot get in the way of a commit or a rebase in your terminal.
    private static readonly IReadOnlyDictionary<string, string> GitEnvironment = new Dictionary<string, string> { ["GIT_OPTIONAL_LOCKS"] = "0" };

    private readonly object _sync = new();
    private readonly IFolderWindow _finder = OperatingSystem.IsWindows() ? new ExplorerFolder() : new FinderFolder();
    private Timer? _timer;
    private int _ticking;
    private int _actionBusy;
    private ProjectView _view = ProjectView.Empty;

    // Commit on the default branch: the first click arms, a second one within the window commits.
    private (string Root, string? Branch, DateTime At)? _commitArmed;

    private string? _lastDir;
    private string? _chosenDir;
    private DateTime _currentDirWrite = DateTime.MinValue;
    private bool _finderWasFront;
    private string? _lastFinderDir;
    private bool _ghWarned;

    private DateTime _lastBranch;
    private DateTime _lastLocal;
    private DateTime _lastFetch;
    private DateTime _lastGitHub;
    private DateTime _lastAudit;
    private DateTime? _redrawAt;
    private DateTime? _pushedAt;

    // Lockfiles of the project (git ls-files), what they looked like, and the commit they come from.
    private IReadOnlyList<string> _lockfiles = Array.Empty<string>();
    private string? _lockStamp;
    private string? _head;
    private DateTime? _auditDue;
    private string? _noPackagesIn;
    private bool? _osvVersion1;
    private bool _osvWarned;

    public event Action? Changed;

    public ProjectView View
    {
        get
        {
            lock (_sync)
            {
                return _view;
            }
        }
    }

    public void Start() => _timer = new Timer(_ => this.Tick(), null, TimeSpan.Zero, TickEvery);

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void RefreshSoon()
    {
        _lastGitHub = DateTime.MinValue;
        _lastAudit = DateTime.MinValue;
        _lastLocal = DateTime.MinValue;
        _osvVersion1 = null;
    }

    private void Update(Func<ProjectView, ProjectView> change)
    {
        bool changed;
        lock (_sync)
        {
            var next = change(_view);
            changed = !Equals(next, _view);
            _view = next;
        }

        if (changed)
        {
            this.Notify();
        }
    }

    private void Notify()
    {
        try
        {
            this.Changed?.Invoke();
        }
        catch (Exception e)
        {
            PluginLog.Warning(e, "Redraw failed");
        }
    }

    private void Tick()
    {
        // One pass at a time: network calls can take longer than the tick.
        if (Interlocked.Exchange(ref _ticking, 1) == 1)
        {
            return;
        }

        try
        {
            DevKit.ReloadConfigIfChanged();
            this.FollowCurrentDir();
            this.HandleTrigger();

            var view = this.View;
            if (view.Root == null)
            {
                return;
            }

            var now = DateTime.UtcNow;

            if (_redrawAt != null && now > _redrawAt)
            {
                _redrawAt = null;
                this.Notify();
            }

            if (now - _lastBranch > BranchEvery)
            {
                _lastBranch = now;
                var branch = this.CurrentBranch(view.Root);
                if (branch != view.Branch)
                {
                    this.Update(v => v with { Branch = branch, Ci = null });
                    _lastGitHub = DateTime.MinValue;
                }

                // A checkout or a pull can bring other lockfiles.
                var head = Git(view.Root, 5, "rev-parse", "HEAD")?.Trim();
                if (head != _head)
                {
                    _head = head;
                    this.ListLockfiles(view.Root);
                }
            }

            var fetchEvery = DevKit.Config.Git.FetchMinutes;
            if (fetchEvery > 0 && now - _lastFetch > TimeSpan.FromMinutes(fetchEvery))
            {
                _lastFetch = now;
                Git(view.Root, 30, "fetch", "--quiet");
                _lastLocal = DateTime.MinValue;
            }

            if (now - _lastLocal > LocalEvery)
            {
                _lastLocal = now;
                this.RefreshLocal();
            }

            var github = DevKit.Config.GitHub;
            var fast = this.View.Ci?.State == CiState.Running || this.AfterPush(now);
            var githubEvery = TimeSpan.FromSeconds(fast ? github.RunningRefreshSeconds : github.RefreshSeconds);
            if (github.Enabled && this.View.Repo != null && now - _lastGitHub > githubEvery)
            {
                _lastGitHub = now;
                this.RefreshGitHub();
            }

            var audit = DevKit.Config.Audit;
            if (audit.Enabled && (now - _lastAudit > TimeSpan.FromMinutes(audit.RefreshMinutes) || now > _auditDue))
            {
                _lastAudit = now;
                _auditDue = null;
                this.RefreshAudit();
            }
        }
        catch (Exception e)
        {
            PluginLog.Warning(e, "Project tick failed");
        }
        finally
        {
            Interlocked.Exchange(ref _ticking, 0);
        }
    }

    private void FollowCurrentDir()
    {
        var dir = this.CurrentDir();
        if (dir == null || dir == _lastDir)
        {
            return;
        }

        _lastDir = dir;
        var top = Git(dir, 5, "rev-parse", "--show-toplevel")?.Trim();

        // Outside a git repository (home folder, Downloads...): keep the previous project.
        if (string.IsNullOrEmpty(top))
        {
            return;
        }

        var root = GitParsers.NormalizePath(top);
        if (root != this.View.Root)
        {
            this.SetProject(root);
        }

        this.UpdateAuditTarget();
    }

    // The last folder used wins: a new prompt in the terminal rewrites current-dir,
    // and the Finder (File Explorer on Windows) counts when it comes to the front or changes folder.
    private string? CurrentDir()
    {
        try
        {
            var file = DevKitPaths.CurrentDirFile;
            var written = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue;
            if (written != _currentDirWrite)
            {
                _currentDirWrite = written;
                var d = written == DateTime.MinValue ? null : File.ReadAllText(file).Trim();
                if (!string.IsNullOrEmpty(d) && Directory.Exists(d))
                {
                    _chosenDir = d;
                }
            }
        }
        catch (IOException)
        {
            // The shell is writing the file right now: next tick.
        }

        if (DevKit.Config.FollowFinder)
        {
            var front = _finder.IsFront();
            if (front)
            {
                var f = _finder.FrontWindowFolder();
                if (f != null && (!_finderWasFront || f != _lastFinderDir))
                {
                    _chosenDir = f;
                }

                _lastFinderDir = f ?? _lastFinderDir;
            }

            _finderWasFront = front;
        }

        return _chosenDir;
    }

    private void SetProject(string root)
    {
        var repo = GitParsers.GitHubRepo(Git(root, 5, "remote", "get-url", "origin"));
        var branch = this.CurrentBranch(root);
        lock (_sync)
        {
            _view = ProjectView.Empty with
            {
                Root = root,
                Name = Path.GetFileName(root),
                Branch = branch,
                Repo = repo,
                GitHubAvailable = _view.GitHubAvailable,
            };
        }

        _lastBranch = DateTime.UtcNow;
        _lastFetch = DateTime.MinValue;
        _pushedAt = null;
        _head = Git(root, 5, "rev-parse", "HEAD")?.Trim();
        this.ListLockfiles(root);
        this.RefreshSoon();
        PluginLog.Info($"Current project: {root} ({repo ?? "no GitHub remote"})");
        this.Notify();
    }

    private string? CurrentBranch(string root)
    {
        var branch = Git(root, 5, "branch", "--show-current")?.Trim();
        return string.IsNullOrEmpty(branch) ? null : branch;
    }

    private void RefreshLocal()
    {
        var root = this.View.Root;
        if (root == null)
        {
            return;
        }

        var status = Git(root, 10, "status", "--porcelain");
        int? changes = status == null ? null : GitParsers.CountChanges(status);
        this.WatchLockfiles(root, status);

        // Fails quietly when the branch has no upstream.
        var counts = GitParsers.AheadBehind(Git(root, 10, "rev-list", "--left-right", "--count", "@{upstream}...HEAD", "--"));
        var before = this.View;

        // Commits to push that are gone while the branch stayed: a git push from a terminal.
        if (before.Root == root && before.Ahead > 0 && counts?.Ahead == 0 && this.CurrentBranch(root) == before.Branch)
        {
            this.Pushed();
        }

        this.Update(v => v.Root != root ? v : v with { Changes = changes, Behind = counts?.Behind, Ahead = counts?.Ahead });
    }

    private void RefreshGitHub()
    {
        var view = this.View;
        var gh = ToolLocator.Find("gh");
        if (gh == null || view.Repo == null)
        {
            this.GitHubUnavailable("gh not found: install the GitHub CLI or set tools.gh in config.json");
            return;
        }

        // One call for the counts and every check of the head commit (all workflows, GitHub Apps, statuses).
        var parts = view.Repo.Split('/');
        var status = RunGh(gh, view.Root, out var authProblem, "api", "graphql",
            "-f", "query=" + GitHubParsers.RepoStatusQuery, "-F", "owner=" + parts[0], "-F", "name=" + parts[1],
            "-f", "branch=" + GitHubParsers.BranchRef(view.Branch));
        if (authProblem)
        {
            this.GitHubUnavailable("gh is not signed in: run gh auth login");
            return;
        }

        var issuesAndPrs = GitHubParsers.RepoCounts(status);
        var ci = GitHubParsers.CommitChecks(status);

        CiRun? before = null;
        this.Update(v =>
        {
            if (v.Repo != view.Repo || v.Branch != view.Branch)
            {
                return v; // the project changed during the calls
            }

            before = v.Ci;
            return v with
            {
                GitHubAvailable = true,
                Issues = issuesAndPrs?.Issues,
                PullRequests = issuesAndPrs?.PullRequests,
                Ci = ci ?? v.Ci,
            };
        });

        _ghWarned = false;
        var finished = GitHubParsers.Finished(before, ci, this.AfterPush(DateTime.UtcNow));
        if (finished != null && DevKit.Config.GitHub.NotifyCi)
        {
            PluginLog.Info($"{view.Repo}: checks of {ci?.Commit?[..Math.Min(7, ci.Commit.Length)]} {finished}");
            DevKit.Haptics.Raise(finished == CiState.Passed ? HapticEvents.CiPassed : HapticEvents.CiFailed);
        }
    }

    // A push from the bubble or from a terminal: GitHub is polled faster for a while, so that checks
    // which start and end between two regular polls still vibrate.
    private bool AfterPush(DateTime now) => _pushedAt is { } at && now - at < PushWatch;

    private void Pushed()
    {
        _pushedAt = DateTime.UtcNow;
        _lastGitHub = DateTime.MinValue;
    }

    private void GitHubUnavailable(string reason)
    {
        if (!_ghWarned)
        {
            _ghWarned = true;
            PluginLog.Warning(reason);
        }

        this.Update(v => v with { GitHubAvailable = false });
    }

    // osv-scanner when it is installed (every lockfile of the repository), npm audit otherwise.
    private string AuditTool()
    {
        var configured = DevKit.Config.Audit.Tool;
        if (configured != "npm" && ToolLocator.Find(OsvScanner.Name) != null)
        {
            return OsvScanner.Name;
        }

        if (configured == "osv" && !_osvWarned)
        {
            _osvWarned = true;
            PluginLog.Warning("osv-scanner not found: using npm audit (install osv-scanner or set tools.osv-scanner)");
        }

        return "npm";
    }

    // Where the scan runs: the repository root for osv-scanner, the closest package-lock.json for npm.
    private void UpdateAuditTarget()
    {
        var view = this.View;
        if (view.Root == null)
        {
            return;
        }

        var tool = this.AuditTool();
        var dir = tool == OsvScanner.Name
            ? (_lockfiles.Count > 0 && _noPackagesIn != view.Root ? view.Root : null)
            : GitParsers.FindUp(_lastDir ?? view.Root, view.Root, "package-lock.json");
        if (dir != view.AuditDir || tool != view.AuditTool)
        {
            this.Update(v => v.Root != view.Root ? v : v with { AuditDir = dir, AuditTool = tool, Vulnerabilities = null });
            _lastAudit = DateTime.MinValue;
        }
    }

    private void ListLockfiles(string root)
    {
        var files = Git(root, 20, "ls-files", "--cached", "--others", "--exclude-standard");
        _lockfiles = files == null
            ? Array.Empty<string>()
            : files.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(OsvScanner.IsLockfile)
                .Take(500)
                .Select(f => Path.GetFullPath(Path.Combine(root, f)))
                .ToList();
        _lockStamp = null;
        _noPackagesIn = null;
        this.WatchLockfiles(root, null);
        this.UpdateAuditTarget();
    }

    // Every few seconds: did a lockfile change, or did a new one appear? Then scan again shortly.
    private void WatchLockfiles(string root, string? porcelain)
    {
        if (porcelain != null)
        {
            foreach (var line in porcelain.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var path = line.Length > 3 ? line[3..].Trim().Trim('"') : "";
                if (OsvScanner.IsLockfile(path) && !_lockfiles.Contains(Path.GetFullPath(Path.Combine(root, path))))
                {
                    this.ListLockfiles(root);
                    return;
                }
            }
        }

        var stamp = string.Join("\n", _lockfiles.Select(f =>
        {
            var info = new FileInfo(f);
            return info.Exists ? $"{f}|{info.LastWriteTimeUtc.Ticks}|{info.Length}" : f;
        }));

        if (_lockStamp != null && stamp != _lockStamp)
        {
            _noPackagesIn = null;
            _auditDue = DateTime.UtcNow + AuditAfterLockfileChange;
            this.UpdateAuditTarget();
        }

        _lockStamp = stamp;
    }

    // Never fixes anything. Offline or failing: the last result stays on the bubble.
    public void RefreshAudit()
    {
        this.UpdateAuditTarget();
        var view = this.View;
        var dir = view.AuditDir;
        if (dir == null || view.AuditTool == null)
        {
            return;
        }

        var osv = view.AuditTool == OsvScanner.Name;
        var exe = ToolLocator.Find(osv ? OsvScanner.Name : "npm");
        if (exe == null)
        {
            PluginLog.Warning($"{view.AuditTool} not found: set tools.{view.AuditTool} in config.json");
            return;
        }

        this.Update(v => v with { AuditRunning = true });
        var audit = DevKit.Config.Audit;
        ProcessResult? result;
        int? count;
        if (osv)
        {
            result = this.RunOsvScanner(exe, dir);
            if (result?.ExitCode == OsvScanner.NoPackagesExitCode)
            {
                PluginLog.Info($"osv-scanner found no lockfile it can read in {dir}");
                _noPackagesIn = dir;
                this.Update(v => v.AuditDir != dir ? v : v with { AuditDir = null, Vulnerabilities = null, AuditRunning = false });
                return;
            }

            count = OsvScanner.Count(result?.Stdout, audit.MinSeverity, audit.UnknownSeverity);
        }
        else
        {
            // npm audit exits with 1 when it finds something: read the report anyway.
            result = ProcessRunner.Run(exe, dir, TimeSpan.FromSeconds(120), new[] { "audit", "--json" });
            count = NpmAudit.Count(result?.Stdout, audit.MinSeverity);
        }

        if (count == null && result != null)
        {
            PluginLog.Info($"{view.AuditTool} gave no report in {dir} (exit {result.ExitCode}): {FirstLine(result.Stderr)}");
        }

        this.Update(v => v.AuditDir != dir ? v : v with { Vulnerabilities = count ?? v.Vulnerabilities, AuditRunning = false });
    }

    // osv-scanner 2 has `scan source`; version 1 takes the flags directly. The first one that works is kept.
    private ProcessResult? RunOsvScanner(string exe, string dir)
    {
        var timeout = TimeSpan.FromSeconds(180);
        if (_osvVersion1 != true)
        {
            var v2 = ProcessRunner.Run(exe, dir, timeout, OsvScanner.Arguments(dir, version1: false));
            if (v2 == null || _osvVersion1 == false || OsvScanner.Understood(v2.ExitCode))
            {
                if (v2 != null && OsvScanner.Understood(v2.ExitCode))
                {
                    _osvVersion1 = false;
                }

                return v2;
            }
        }

        var v1 = ProcessRunner.Run(exe, dir, timeout, OsvScanner.Arguments(dir, version1: true));
        if (v1 != null && OsvScanner.Understood(v1.ExitCode))
        {
            _osvVersion1 = true;
        }

        return v1;
    }

    public bool CanRun(string action)
    {
        var v = this.View;
        return v.Root != null && action switch
        {
            GitActions.Commit => v.Changes > 0,
            GitActions.Push => v.Ahead > 0 || (v.Ahead == null && v.Branch != null),
            GitActions.Pull => v.Behind != null,
            _ => false,
        };
    }

    // One git action at a time. The bubble turns amber, then shows OK or Failed for a few seconds.
    public void Run(string action)
    {
        var root = this.View.Root;
        if (root == null || !this.CanRun(action) || Interlocked.Exchange(ref _actionBusy, 1) == 1)
        {
            return;
        }

        ProcessRunner.Detach(() =>
        {
            if (action == GitActions.Commit && !this.CommitAllowed(root))
            {
                Interlocked.Exchange(ref _actionBusy, 0);
                return;
            }

            var ok = false;
            try
            {
                this.SetAction(action, ActionPhase.Running);
                ok = action switch
                {
                    GitActions.Commit => this.Commit(root),
                    GitActions.Push => this.Push(root),
                    GitActions.Pull => Git(root, 60, "pull", "--ff-only", "--quiet") != null,
                    _ => false,
                };
            }
            finally
            {
                this.SetAction(action, ok ? ActionPhase.Succeeded : ActionPhase.Failed);
                _redrawAt = DateTime.UtcNow + ActionStatus.ResultVisibleFor + TimeSpan.FromSeconds(0.5);
                Interlocked.Exchange(ref _actionBusy, 0);
                DevKit.Haptics.Raise(ok ? HapticEvents.ActionSucceeded : HapticEvents.ActionFailed);
                this.RefreshLocal();
                if (ok && action == GitActions.Push)
                {
                    this.Pushed(); // a push usually starts checks
                }
            }
        });
    }

    private void SetAction(string action, ActionPhase phase) =>
        this.Update(v =>
        {
            var actions = new Dictionary<string, ActionStatus>(v.Actions) { [action] = new ActionStatus(phase, DateTime.UtcNow) };
            return v with { Actions = actions };
        });

    // Before `git add -A`: no commit during a conflict or with a secret file, a second click on the
    // default branch. Nothing is staged or written when it says no.
    private bool CommitAllowed(string root)
    {
        var check = this.CheckCommit(root);
        var s = DevKit.Strings;
        if (check == null)
        {
            return true; // git did not answer: the commit itself will fail and say so
        }

        if (!check.Ok)
        {
            _commitArmed = null;
            PluginLog.Info($"Commit refused in {root}: {check.Block} {string.Join(", ", check.Files)}");
            this.SetAction(GitActions.Commit, ActionPhase.Failed);
            _redrawAt = DateTime.UtcNow + ActionStatus.ResultVisibleFor + TimeSpan.FromSeconds(0.5);
            Notifier.Show(s.CommitRefused, s.CommitRefusal(check));
            DevKit.Haptics.Raise(HapticEvents.ActionFailed);
            return false;
        }

        var branch = this.CurrentBranch(root);
        var defaultBranch = CommitGuard.DefaultBranch(Git(root, 5, "symbolic-ref", "--quiet", "--short", "refs/remotes/origin/HEAD"));
        if (!CommitGuard.NeedsConfirmation(branch, defaultBranch))
        {
            return true;
        }

        var now = DateTime.UtcNow;
        if (_commitArmed is { } armed && armed.Root == root && armed.Branch == branch && now - armed.At < CommitGuard.ConfirmWindow)
        {
            _commitArmed = null;
            return true;
        }

        _commitArmed = (root, branch, now);
        PluginLog.Info($"Commit on {branch} in {root}: waiting for a second click");
        this.SetAction(GitActions.Commit, ActionPhase.Confirming);
        _redrawAt = now + CommitGuard.ConfirmWindow + TimeSpan.FromSeconds(0.2);
        Notifier.Show(string.Format(s.CommitConfirmTitle, branch), string.Format(s.CommitConfirmText, branch));
        return false;
    }

    // Null when git cannot tell (the commit then runs as before and fails on its own).
    private CommitCheck? CheckCommit(string root)
    {
        var porcelain = Git(root, 10, "-c", "core.quotePath=false", "status", "--porcelain=v1", "-z", "--untracked-files=all");
        if (porcelain == null)
        {
            return null;
        }

        var status = CommitGuard.ParseStatus(porcelain);
        string? operation = null;
        var gitDir = Git(root, 5, "rev-parse", "--absolute-git-dir")?.Trim();
        if (!string.IsNullOrEmpty(gitDir))
        {
            operation = CommitGuard.OperationFiles
                .Where(o => File.Exists(Path.Combine(gitDir, o.Path)) || Directory.Exists(Path.Combine(gitDir, o.Path)))
                .Select(o => o.Operation)
                .FirstOrDefault();
        }

        if (status.Any(e => e.Unmerged) || operation != null)
        {
            return CommitGuard.Check(status, operation, Array.Empty<string>());
        }

        // Markers added since HEAD in tracked files, anywhere in the new ones. No HEAD yet: every file is new.
        var hasHead = Git(root, 5, "rev-parse", "--verify", "--quiet", "HEAD") != null;
        var markers = new List<string>();
        if (hasHead)
        {
            markers.AddRange(CommitGuard.MarkersInDiff(Git(root, 20, "-c", "core.quotePath=false", "diff", "HEAD", "--no-color", "--no-ext-diff", "--no-textconv", "-U0")));
        }

        foreach (var e in status.Where(e => !e.Deleted && (e.Untracked || !hasHead)).Take(2000))
        {
            if (!markers.Contains(e.Path) && FileHasMarkers(Path.Combine(root, e.Path)))
            {
                markers.Add(e.Path);
            }
        }

        return CommitGuard.Check(status, null, markers);
    }

    private static bool FileHasMarkers(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length <= 2_000_000 && CommitGuard.HasMarkers(File.ReadAllText(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private bool Commit(string root)
    {
        if (Git(root, 20, "add", "-A") == null)
        {
            return false;
        }

        var changes = GitParsers.NameStatus(Git(root, 20, "diff", "--cached", "--name-status") ?? "");
        if (changes.Count == 0)
        {
            PluginLog.Info($"Nothing to commit in {root}");
            return false;
        }

        var message = this.GenerateMessage(root) ?? CommitMessage.Fallback(changes);
        var ok = Git(root, 60, "commit", "--quiet", "-m", message) != null;
        PluginLog.Info($"Commit in {root}: {(ok ? message : "failed")}");
        return ok;
    }

    private string? GenerateMessage(string root)
    {
        var command = DevKit.Config.Commit.MessageCommand;
        if (command is not { Length: > 0 })
        {
            return null;
        }

        var diff = Git(root, 20, "diff", "--cached", "--stat") + "\n" + Git(root, 20, "diff", "--cached");
        if (diff.Length > 30000)
        {
            diff = diff[..30000];
        }

        var (args, stdin) = CommitMessage.Invocation(command.Skip(1).ToArray(), DevKit.Config.Commit.Prompt, diff);
        var exe = ToolLocator.Find(command[0]) ?? command[0];
        var result = ProcessRunner.Run(exe, root, TimeSpan.FromSeconds(90), args, stdin);
        var message = result?.Ok == true ? CommitMessage.Clean(result.Stdout) : null;
        if (message == null)
        {
            PluginLog.Warning($"Commit message command gave nothing usable ({command[0]}), using the file list instead");
        }

        return message;
    }

    // Never forced. A branch without upstream is published to origin.
    private bool Push(string root)
    {
        var view = this.View;
        return view.Ahead == null
            ? Git(root, 60, "push", "--quiet", "-u", "origin", "HEAD") != null
            : Git(root, 60, "push", "--quiet") != null;
    }

    // agent null: the generic Agent bubble and its configured command, started as it is.
    // A coding agent that is not found opens its install page instead.
    public void LaunchAgent(CodingAgent? agent = null)
    {
        var config = DevKit.Config.Agent;
        if (agent != null && !AgentLauncher.IsInstalled(agent, recheck: true))
        {
            PluginLog.Info($"{agent.Name} not found ({AgentLauncher.Program(agent)}), opening {agent.InstallUrl}");
            ProcessRunner.Open(agent.InstallUrl);
            return;
        }

        var root = this.View.Root;
        if (root == null)
        {
            PluginLog.Info("Agent bubble clicked without a current project");
            Notifier.Show(agent?.Name ?? config.Label, DevKit.Strings.NoProject);
            return;
        }

        AgentLauncher.Launch(root, agent == null ? config : AgentLauncher.For(agent, config));
    }

    // Test without a mouse: echo commit > ~/.config/mx-dev-kit/trigger
    private void HandleTrigger()
    {
        var file = DevKitPaths.TriggerFile;
        if (!File.Exists(file))
        {
            return;
        }

        string what;
        try
        {
            what = File.ReadAllText(file).Trim().ToLowerInvariant();
            File.Delete(file);
        }
        catch (IOException)
        {
            return;
        }

        PluginLog.Info($"Trigger file: {what}");
        switch (what)
        {
            case GitActions.Commit or GitActions.Push or GitActions.Pull:
                this.Run(what);
                break;
            case "agent":
                this.LaunchAgent();
                break;
            case "claude" or "codex" or "gemini" or "copilot":
                this.LaunchAgent(CodingAgents.Find(what));
                break;
            case "focus":
                DevKit.Focus.Click();
                break;
            case "audit":
                ProcessRunner.Detach(this.RefreshAudit);
                break;
            case "inbox":
                DevKit.GitHub.Open();
                break;
            case "inbox-test":
                DevKit.GitHub.Test();
                break;
            case "notify":
                Notifier.Show("MX Dev Kit", DevKit.Strings.NotificationTest, "https://github.com/notifications");
                break;
            default:
                if (HapticEvents.DefaultWaveforms.ContainsKey(what))
                {
                    DevKit.Haptics.Raise(HapticEvents.DefaultWaveforms.Keys.First(k => k.Equals(what, StringComparison.OrdinalIgnoreCase)));
                }

                break;
        }
    }

    // stdout of a git command, or null when it fails.
    private static string? Git(string cwd, int timeoutSeconds, params string[] args)
    {
        var git = ToolLocator.Find("git");
        if (git == null)
        {
            return null;
        }

        var result = ProcessRunner.Run(git, cwd, TimeSpan.FromSeconds(timeoutSeconds), args, env: GitEnvironment);
        var command = args[0] == "-c" && args.Length > 2 ? args[2] : args[0];
        if (result is { Ok: false } && command is not ("rev-list" or "rev-parse" or "remote" or "symbolic-ref"))
        {
            PluginLog.Info($"git {command} in {cwd}: exit {result.ExitCode} {FirstLine(result.Stderr)}");
        }

        return result?.Ok == true ? result.Stdout : null;
    }

    private static string? RunGh(string gh, string? cwd, out bool authProblem, params string[] args)
    {
        authProblem = false;
        var result = ProcessRunner.Run(gh, cwd, TimeSpan.FromSeconds(20), args);
        if (result == null)
        {
            return null;
        }

        if (!result.Ok)
        {
            authProblem = result.Stderr.Contains("gh auth login", StringComparison.OrdinalIgnoreCase);
            PluginLog.Info($"gh {args[0]} {args[1]}: exit {result.ExitCode} {FirstLine(result.Stderr)}");
            return null;
        }

        return result.Stdout;
    }

    private static string FirstLine(string text)
    {
        var line = text.Trim().Split('\n')[0].Trim();
        return line.Length > 200 ? line[..200] : line;
    }
}

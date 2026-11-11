using System;
using System.Collections.Generic;
using System.Globalization;

namespace MxDevKit;

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb? Parse(string? hex)
    {
        var h = (hex ?? "").Trim().TrimStart('#');
        return h.Length == 6 && uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)
            ? new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v)
            : null;
    }
}

public static class Palette
{
    public static readonly Rgb Grey = new(48, 54, 61);
    public static readonly Rgb Slate = new(22, 27, 34);
    public static readonly Rgb Blue = new(31, 111, 235);
    public static readonly Rgb Purple = new(130, 80, 223);
    public static readonly Rgb Green = new(26, 127, 55);
    public static readonly Rgb Red = new(207, 34, 46);
    public static readonly Rgb Amber = new(191, 135, 0);
    public static readonly Rgb Orange = new(219, 109, 40);
    public static readonly Rgb Tomato = new(214, 69, 65);
}

public enum Glyph
{
    None,
    Issue,
    PullRequest,
    Check,
    Cross,
    Clock,
    Shield,
    Commit,
    ArrowUp,
    ArrowDown,
    Prompt,
    Eye,
    Timer,
    Bell,
    At,
    Comment,
}

// The mark of a coding agent, drawn instead of a pictogram.
public enum AgentLogo
{
    None,
    Claude,
    Codex,
    Gemini,
    Copilot,
}

// What a bubble shows: a pictogram, a big value and a background. Label is the name Options+ shows next to it.
// Title bubbles (the project) show Value as a name and Subtitle under it instead of a pictogram.
public sealed record Bubble(Glyph Glyph, string Value, Rgb Background, string Label)
{
    public bool IsTitle { get; init; }
    public string? Subtitle { get; init; }

    // 0..1 draws an arc along the edge (focus timer). Null: no arc.
    public double? Progress { get; init; }

    // Agent bubbles: the agent's mark instead of the pictogram. Mono: in white, for the grey "not installed" state.
    public AgentLogo Logo { get; init; }
    public bool LogoMono { get; init; }
}

public enum ActionPhase
{
    Idle,
    Running,
    Succeeded,
    Failed,

    // Commit on the default branch: waiting for the second click.
    Confirming,
}

public sealed record ActionStatus(ActionPhase Phase, DateTime At)
{
    public static readonly ActionStatus Idle = new(ActionPhase.Idle, DateTime.MinValue);

    public static readonly TimeSpan ResultVisibleFor = TimeSpan.FromSeconds(8);

    // A result (OK, Failed) stays on the bubble a few seconds, then the counter comes back.
    // A confirmation request lasts as long as the second click can come.
    public ActionPhase Visible(DateTime now) => this.Phase switch
    {
        ActionPhase.Running => ActionPhase.Running,
        ActionPhase.Confirming => now - this.At < CommitGuard.ConfirmWindow ? ActionPhase.Confirming : ActionPhase.Idle,
        _ => now - this.At < ResultVisibleFor ? this.Phase : ActionPhase.Idle,
    };
}

// Snapshot of everything the project bubbles show. Null numbers mean "not known yet".
public sealed record ProjectView
{
    public static readonly ProjectView Empty = new();

    public string? Root { get; init; }
    public string? Name { get; init; }
    public string? Branch { get; init; }
    public string? Repo { get; init; }

    // False when gh is missing or not signed in: the GitHub bubbles show a dash instead of waiting.
    public bool GitHubAvailable { get; init; } = true;
    public int? Issues { get; init; }
    public int? PullRequests { get; init; }
    public CiRun? Ci { get; init; }
    // Folder the vulnerability scan runs in: the repository root for osv-scanner, the closest
    // package-lock.json for npm audit. Null: nothing to scan.
    public string? AuditDir { get; init; }

    // osv-scanner or npm.
    public string? AuditTool { get; init; }
    public int? Vulnerabilities { get; init; }
    public bool AuditRunning { get; init; }
    public int? Changes { get; init; }
    public int? Ahead { get; init; }
    public int? Behind { get; init; }
    public IReadOnlyDictionary<string, ActionStatus> Actions { get; init; } = new Dictionary<string, ActionStatus>();

    public ActionStatus Action(string name) => this.Actions.TryGetValue(name, out var s) ? s : ActionStatus.Idle;
}

public static class GitActions
{
    public const string Commit = "commit";
    public const string Push = "push";
    public const string Pull = "pull";
}

public static class Bubbles
{
    public const string None = "–";
    public const string Pending = "…";

    public static Bubble Project(ProjectView v, Strings s)
    {
        var name = v.Name ?? None;
        return new Bubble(Glyph.None, name, Palette.Slate, v.Name ?? s.ProjectName)
        {
            IsTitle = true,
            Subtitle = v.Branch,
        };
    }

    public static Bubble Issues(ProjectView v, Strings s)
    {
        var value = !HasGitHub(v) ? None : v.Issues?.ToString() ?? Pending;
        return new Bubble(Glyph.Issue, value, v.Issues > 0 ? Palette.Green : Palette.Grey, s.Label(s.ShortIssues, value));
    }

    public static Bubble PullRequests(ProjectView v, Strings s)
    {
        var value = !HasGitHub(v) ? None : v.PullRequests?.ToString() ?? Pending;
        return new Bubble(Glyph.PullRequest, value, v.PullRequests > 0 ? Palette.Purple : Palette.Grey, s.Label(s.ShortPullRequests, value));
    }

    public static Bubble Ci(ProjectView v, Strings s)
    {
        if (!HasGitHub(v))
        {
            return new Bubble(Glyph.Check, None, Palette.Grey, s.ShortCi);
        }

        return (v.Ci?.State ?? CiState.Unknown) switch
        {
            CiState.Passed => new Bubble(Glyph.Check, s.Ok, Palette.Green, s.Label(s.ShortCi, s.Ok)),
            CiState.Failed => new Bubble(Glyph.Cross, s.Failed, Palette.Red, s.Label(s.ShortCi, s.Failed)),
            CiState.Running => new Bubble(Glyph.Clock, s.Running, Palette.Amber, s.Label(s.ShortCi, s.Running)),
            CiState.None => new Bubble(Glyph.Check, None, Palette.Grey, s.Label(s.ShortCi, None)),
            _ => new Bubble(Glyph.Check, Pending, Palette.Grey, s.ShortCi),
        };
    }

    public static Bubble Audit(ProjectView v, Strings s)
    {
        if (v.Root == null || v.AuditDir == null)
        {
            return new Bubble(Glyph.Shield, None, Palette.Grey, s.ShortAudit);
        }

        if (v.Vulnerabilities == null)
        {
            return v.AuditRunning
                ? new Bubble(Glyph.Shield, Pending, Palette.Amber, s.ShortAudit)
                : new Bubble(Glyph.Shield, "?", Palette.Grey, s.ShortAudit);
        }

        var n = v.Vulnerabilities.Value;
        return new Bubble(Glyph.Shield, n.ToString(), n > 0 ? Palette.Red : Palette.Green, s.Label(s.ShortAudit, n.ToString()));
    }

    public static Bubble Commit(ProjectView v, Strings s, DateTime now) =>
        GitAction(v, GitActions.Commit, Glyph.Commit, s.ShortCommit, v.Changes, Palette.Amber, s, now);

    public static Bubble Push(ProjectView v, Strings s, DateTime now) =>
        GitAction(v, GitActions.Push, Glyph.ArrowUp, s.ShortPush, v.Ahead, Palette.Blue, s, now);

    public static Bubble Pull(ProjectView v, Strings s, DateTime now) =>
        GitAction(v, GitActions.Pull, Glyph.ArrowDown, s.ShortPull, v.Behind, Palette.Blue, s, now);

    public static Bubble Agent(AgentConfig agent) =>
        new(Glyph.Prompt, "", Rgb.Parse(agent.Color) ?? Palette.Slate, agent.Label);

    // A bubble for one coding agent: its mark on its colour, or the white mark on grey with "Install"
    // when its program is not found on this computer.
    public static Bubble CodingAgent(CodingAgent agent, bool installed, Strings s) => installed
        ? new Bubble(Glyph.None, "", agent.Background, agent.Name) { Logo = agent.Logo, LogoMono = agent.MonoLogo }
        : new Bubble(Glyph.None, s.Install, Palette.Grey, s.Label(agent.Name, s.NotInstalled)) { Logo = agent.Logo, LogoMono = true };

    // count: null while unknown; available: false when gh is missing or not signed in.
    public static Bubble Reviews(int? count, bool available, Strings s)
    {
        if (!available)
        {
            return new Bubble(Glyph.Eye, None, Palette.Grey, s.ShortReviews);
        }

        var value = count?.ToString() ?? Pending;
        return new Bubble(Glyph.Eye, value, count > 0 ? Palette.Orange : Palette.Grey, s.Label(s.ShortReviews, value));
    }

    // The last GitHub inbox event: its pictogram, its number, a colour that says good or bad news.
    // Grey once clicked (seen). Nothing on GitHub is marked as read.
    public static Bubble Inbox(InboxEvent? latest, bool seen, bool available, Strings s)
    {
        if (!available)
        {
            return new Bubble(Glyph.Bell, None, Palette.Grey, s.ShortInbox);
        }

        if (latest == null)
        {
            return new Bubble(Glyph.Bell, "", Palette.Grey, s.ShortInbox);
        }

        var (glyph, colour) = latest.Kind switch
        {
            InboxKind.ReviewRequested => (Glyph.Eye, Palette.Orange),
            InboxKind.Mention or InboxKind.TeamMention => (Glyph.At, Palette.Orange),
            InboxKind.Assigned => (Glyph.Issue, Palette.Orange),
            InboxKind.Approved => (Glyph.Check, Palette.Green),
            InboxKind.Merged => (Glyph.PullRequest, Palette.Purple),
            InboxKind.ChangesRequested => (Glyph.Comment, Palette.Red),
            _ => (Glyph.Cross, Palette.Red),
        };

        var value = latest.Number is { } n ? "#" + n : s.ShortInbox;
        var label = s.Label(s.ShortInbox, s.InboxTitle(latest) + " " + Strings.InboxText(latest));
        if (label.Length > 60)
        {
            label = label[..59] + "…";
        }

        return new Bubble(glyph, value, seen ? Palette.Grey : colour, label);
    }

    public static Bubble Focus(FocusTimer timer, Strings s, DateTime now)
    {
        var value = FocusTimer.Format(timer.Remaining(now));
        return timer.Phase switch
        {
            FocusPhase.Focus => new Bubble(Glyph.Timer, value, Palette.Tomato, s.Label(s.ShortFocus, value)) { Progress = timer.Progress(now) },
            FocusPhase.Break => new Bubble(Glyph.Timer, value, Palette.Green, s.Label(s.ShortBreak, value)) { Progress = timer.Progress(now) },
            _ => new Bubble(Glyph.Timer, value, Palette.Grey, s.Label(s.ShortFocus, value + " " + s.Minutes)),
        };
    }

    private static bool HasGitHub(ProjectView v) => v.Repo != null && v.GitHubAvailable;

    private static Bubble GitAction(ProjectView v, string action, Glyph glyph, string name, int? count, Rgb busy, Strings s, DateTime now)
    {
        if (v.Root == null)
        {
            return new Bubble(glyph, None, Palette.Grey, name);
        }

        switch (v.Action(action).Visible(now))
        {
            case ActionPhase.Running:
                return new Bubble(glyph, Pending, Palette.Amber, name);
            case ActionPhase.Succeeded:
                return new Bubble(Glyph.Check, s.Ok, Palette.Green, s.Label(name, s.Ok));
            case ActionPhase.Failed:
                return new Bubble(Glyph.Cross, s.Failed, Palette.Red, s.Label(name, s.Failed));
            case ActionPhase.Confirming:
                return new Bubble(glyph, s.Confirm, Palette.Orange, s.Label(name, s.Confirm));
        }

        if (count == null)
        {
            return new Bubble(glyph, None, Palette.Grey, name);
        }

        return new Bubble(glyph, count.Value.ToString(), count > 0 ? busy : Palette.Grey, s.Label(name, count.Value.ToString()));
    }
}

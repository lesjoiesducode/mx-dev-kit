using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MxDevKit;

// config.json, every key optional. See config.example.json for the documented version.
public sealed class DevKitConfig
{
    public string Language { get; set; } = "auto";
    public HapticsConfig Haptics { get; set; } = new();
    public GitConfig Git { get; set; } = new();
    public GitHubConfig GitHub { get; set; } = new();
    public AuditConfig Audit { get; set; } = new();
    public CommitConfig Commit { get; set; } = new();
    public AgentConfig Agent { get; set; } = new();
    public FocusConfig Focus { get; set; } = new();
    public NotificationsConfig Notifications { get; set; } = new();

    // macOS only: the folder of the front Finder window counts as the current project too.
    public bool FollowFinder { get; set; } = true;

    // Explicit paths for git, gh, npm or the agent command when they are not found automatically.
    public Dictionary<string, string> Tools { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static DevKitConfig Parse(string json)
    {
        var config = string.IsNullOrWhiteSpace(json)
            ? new DevKitConfig()
            : JsonSerializer.Deserialize<DevKitConfig>(json, JsonOptions) ?? new DevKitConfig();
        return config.Normalized();
    }

    // Missing file: defaults. Broken file: defaults plus the error, so the plugin can log it.
    public static DevKitConfig Load(string path, out string? error)
    {
        error = null;
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : new DevKitConfig();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            error = e.Message;
            return new DevKitConfig();
        }
    }

    public DevKitConfig Normalized()
    {
        this.Haptics ??= new();
        this.Git ??= new();
        this.GitHub ??= new();
        this.Audit ??= new();
        this.Commit ??= new();
        this.Agent ??= new();
        this.Focus ??= new();
        this.Notifications ??= new();

        // The deserializer creates plain dictionaries: make the keys case-insensitive again.
        this.Tools = new Dictionary<string, string>(this.Tools ?? new(), StringComparer.OrdinalIgnoreCase);
        this.Haptics.Waveforms = new Dictionary<string, string>(this.Haptics.Waveforms ?? new(), StringComparer.OrdinalIgnoreCase);

        this.Language = (this.Language ?? "auto").Trim().ToLowerInvariant();
        this.Haptics.Mode = (this.Haptics.Mode ?? "sdk").Trim().ToLowerInvariant() switch
        {
            "http" or "web" => "http",
            "off" or "none" or "false" => "off",
            _ => "sdk",
        };
        if (string.IsNullOrWhiteSpace(this.Haptics.Url))
        {
            this.Haptics.Url = HapticsConfig.DefaultUrl;
        }

        this.Git.FetchMinutes = Math.Max(0, this.Git.FetchMinutes);
        this.GitHub.RefreshSeconds = Math.Max(15, this.GitHub.RefreshSeconds);
        this.GitHub.RunningRefreshSeconds = Math.Clamp(this.GitHub.RunningRefreshSeconds, 10, this.GitHub.RefreshSeconds);
        if (string.IsNullOrWhiteSpace(this.GitHub.ReviewQuery))
        {
            this.GitHub.ReviewQuery = GitHubConfig.DefaultReviewQuery;
        }

        this.GitHub.Events ??= new();
        if (!this.GitHub.NotifyReviews)
        {
            this.GitHub.Events.ReviewRequested = false; // the 1.0 setting
        }

        this.Audit.RefreshMinutes = Math.Max(1, this.Audit.RefreshMinutes);
        this.Audit.MinSeverity = NpmAudit.NormalizeSeverity(this.Audit.MinSeverity);
        this.Audit.UnknownSeverity = NpmAudit.NormalizeSeverity(this.Audit.UnknownSeverity);
        this.Audit.Tool = (this.Audit.Tool ?? "auto").Trim().ToLowerInvariant() switch
        {
            "osv" or OsvScanner.Name => "osv",
            "npm" => "npm",
            _ => "auto",
        };
        if (string.IsNullOrWhiteSpace(this.Commit.Prompt))
        {
            this.Commit.Prompt = CommitConfig.DefaultPrompt;
        }

        if (string.IsNullOrWhiteSpace(this.Agent.Command))
        {
            this.Agent.Command = "claude";
        }

        this.Agent.Label = string.IsNullOrWhiteSpace(this.Agent.Label) ? "Claude Code" : this.Agent.Label.Trim();
        this.Agent.Terminal = string.IsNullOrWhiteSpace(this.Agent.Terminal) ? "auto" : this.Agent.Terminal.Trim();
        this.Agent.Commands = new Dictionary<string, string>(this.Agent.Commands ?? new(), StringComparer.OrdinalIgnoreCase);
        this.Focus.Minutes = Math.Clamp(this.Focus.Minutes, 1, 180);
        this.Focus.BreakMinutes = Math.Clamp(this.Focus.BreakMinutes, 0, 60);
        return this;
    }
}

public sealed class HapticsConfig
{
    public const string DefaultUrl = "https://local.jmw.nz:41443/haptic/";

    // sdk: the plugin raises its own haptic events (no other plugin needed).
    // http: POST to the Haptic Web Plugin, like the shell scripts do.
    // off: never vibrate.
    public string Mode { get; set; } = "sdk";
    public string Url { get; set; } = DefaultUrl;

    // http mode only: event name -> waveform, e.g. { "ciPassed": "firework" }.
    public Dictionary<string, string> Waveforms { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class GitConfig
{
    // How often to run git fetch so the Pull bubble knows about new commits. 0 disables it.
    public int FetchMinutes { get; set; } = 3;
}

public sealed class GitHubConfig
{
    public const string DefaultReviewQuery = "is:open is:pr user-review-requested:@me archived:false";

    public bool Enabled { get; set; } = true;
    public int RefreshSeconds { get; set; } = 60;

    // Faster polling while the CI bubble shows a running workflow.
    public int RunningRefreshSeconds { get; set; } = 15;
    public bool NotifyCi { get; set; } = true;

    // 1.0 setting, kept working: false is the same as events.reviewRequested false.
    public bool NotifyReviews { get; set; } = true;
    public string ReviewQuery { get; set; } = DefaultReviewQuery;

    // The GitHub inbox: what vibrates and shows a notification.
    public GitHubEventsConfig Events { get; set; } = new();
}

public sealed class GitHubEventsConfig
{
    public bool ReviewRequested { get; set; } = true;
    public bool Mention { get; set; } = true;
    public bool TeamMention { get; set; } = true;
    public bool Assigned { get; set; } = true;
    public bool Approved { get; set; } = true;
    public bool ChangesRequested { get; set; } = true;
    public bool Merged { get; set; } = true;
    public bool CiFailed { get; set; } = true;

    public bool IsOn(InboxKind kind) => kind switch
    {
        InboxKind.ReviewRequested => this.ReviewRequested,
        InboxKind.Mention => this.Mention,
        InboxKind.TeamMention => this.TeamMention,
        InboxKind.Assigned => this.Assigned,
        InboxKind.Approved => this.Approved,
        InboxKind.ChangesRequested => this.ChangesRequested,
        InboxKind.Merged => this.Merged,
        InboxKind.CiFailed => this.CiFailed,
        _ => false,
    };
}

public sealed class AuditConfig
{
    public bool Enabled { get; set; } = true;
    public int RefreshMinutes { get; set; } = 15;

    // Lowest severity that counts: low, moderate, high or critical.
    public string MinSeverity { get; set; } = "high";

    // auto: osv-scanner when it is installed, npm audit otherwise. osv or npm: only that one.
    public string Tool { get; set; } = "auto";

    // osv-scanner: severity given to the advisories that have no score.
    public string UnknownSeverity { get; set; } = "high";
}

public sealed class CommitConfig
{
    public const string DefaultPrompt =
        "Write a git commit message for the diff below: a single line, 72 characters at most, " +
        "imperative mood, no prefix, no quotes. Reply with the message only.";

    // Optional command that writes the commit message, e.g. ["claude", "-p", "{prompt}", "--model", "haiku"].
    // The staged diff goes to its standard input. Without {prompt} in the arguments, the prompt is sent
    // on standard input before the diff. Null, or any failure: a message is built from the file list.
    public string[]? MessageCommand { get; set; }
    public string Prompt { get; set; } = DefaultPrompt;
}

public sealed class AgentConfig
{
    public string Label { get; set; } = "Claude Code";
    public string Command { get; set; } = "claude";

    // macOS: auto, Terminal, iTerm. Windows: auto, wt, powershell, pwsh.
    public string Terminal { get; set; } = "auto";

    // Full control over the launch, e.g. ["open", "-na", "Ghostty", "--args", "--working-directory={dir}", "-e", "{command}"].
    public string[]? Launch { get; set; }

    // Bubble background as #RRGGBB.
    public string? Color { get; set; }

    // Command lines of the Claude Code, Codex, Gemini CLI and Copilot CLI bubbles, when they should not
    // be the plain claude, codex, gemini and copilot. e.g. { "codex": "codex --full-auto" }.
    // They open in the terminal set above, and "launch" applies to them too.
    public Dictionary<string, string> Commands { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class NotificationsConfig
{
    // System notifications from the plugin, through bin/mx-notify. Vibrations do not depend on it.
    public bool Enabled { get; set; } = true;
}

public sealed class FocusConfig
{
    public int Minutes { get; set; } = 25;
    public int BreakMinutes { get; set; } = 5;
}

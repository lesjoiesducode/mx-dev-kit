namespace MxDevKit.Tests;

public class BubblesTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Strings En = Strings.English;
    private static readonly Strings Fr = Strings.French;

    private static readonly ProjectView Repo = new()
    {
        Root = "/src/api",
        Name = "api",
        Branch = "main",
        Repo = "octo-org/api",
    };

    [Fact]
    public void Github_bubbles_show_a_dash_outside_github()
    {
        var local = Repo with { Repo = null };

        Assert.Equal(Bubbles.None, Bubbles.Issues(local, En).Value);
        Assert.Equal(Bubbles.None, Bubbles.PullRequests(local, En).Value);
        Assert.Equal(Bubbles.None, Bubbles.Ci(local, En).Value);
    }

    [Fact]
    public void Github_bubbles_show_a_dash_when_gh_is_unavailable() =>
        Assert.Equal(Bubbles.None, Bubbles.Issues(Repo with { Issues = 3, GitHubAvailable = false }, En).Value);

    [Fact]
    public void Counters_wait_then_colour_when_not_zero()
    {
        Assert.Equal(Bubbles.Pending, Bubbles.Issues(Repo, En).Value);

        var some = Bubbles.Issues(Repo with { Issues = 7 }, En);
        Assert.Equal("7", some.Value);
        Assert.Equal(Palette.Green, some.Background);
        Assert.Equal("Issues: 7", some.Label);

        Assert.Equal(Palette.Grey, Bubbles.PullRequests(Repo with { PullRequests = 0 }, En).Background);
        Assert.Equal(Palette.Purple, Bubbles.PullRequests(Repo with { PullRequests = 2 }, En).Background);
    }

    [Theory]
    [InlineData(CiState.Passed, Glyph.Check, "OK")]
    [InlineData(CiState.Failed, Glyph.Cross, "Failed")]
    [InlineData(CiState.Running, Glyph.Clock, "Running")]
    [InlineData(CiState.None, Glyph.Check, Bubbles.None)]
    public void Ci_bubble_follows_the_run(CiState state, Glyph glyph, string value)
    {
        var bubble = Bubbles.Ci(Repo with { Ci = new CiRun(state, 1, null) }, En);

        Assert.Equal(glyph, bubble.Glyph);
        Assert.Equal(value, bubble.Value);
    }

    [Fact]
    public void Labels_are_localised() =>
        Assert.Equal("CI : Échec", Bubbles.Ci(Repo with { Ci = new CiRun(CiState.Failed, 1, null) }, Fr).Label);

    [Fact]
    public void Audit_bubble_states()
    {
        Assert.Equal(Bubbles.None, Bubbles.Audit(Repo, En).Value);
        Assert.Equal(Bubbles.Pending, Bubbles.Audit(Repo with { AuditDir = "/src/api", AuditRunning = true }, En).Value);
        Assert.Equal("?", Bubbles.Audit(Repo with { AuditDir = "/src/api" }, En).Value);
        Assert.Equal(Palette.Red, Bubbles.Audit(Repo with { AuditDir = "/src/api", Vulnerabilities = 5 }, En).Background);
        Assert.Equal(Palette.Green, Bubbles.Audit(Repo with { AuditDir = "/src/api", Vulnerabilities = 0 }, En).Background);
    }

    [Fact]
    public void Git_action_shows_progress_then_result_then_counter()
    {
        var view = Repo with { Changes = 3 };
        Assert.Equal("3", Bubbles.Commit(view, En, Now).Value);

        var running = view with { Actions = new Dictionary<string, ActionStatus> { [GitActions.Commit] = new(ActionPhase.Running, Now.AddMinutes(-5)) } };
        Assert.Equal(Bubbles.Pending, Bubbles.Commit(running, En, Now).Value);

        var done = view with { Actions = new Dictionary<string, ActionStatus> { [GitActions.Commit] = new(ActionPhase.Succeeded, Now.AddSeconds(-2)) } };
        Assert.Equal("OK", Bubbles.Commit(done, En, Now).Value);
        Assert.Equal("3", Bubbles.Commit(done, En, Now.AddSeconds(10)).Value);

        var failed = view with { Actions = new Dictionary<string, ActionStatus> { [GitActions.Push] = new(ActionPhase.Failed, Now) } };
        Assert.Equal("Failed", Bubbles.Push(failed, En, Now).Value);
        Assert.Equal("3", Bubbles.Commit(failed, En, Now).Value);
    }

    [Fact]
    public void Push_without_upstream_shows_a_dash() =>
        Assert.Equal(Bubbles.None, Bubbles.Push(Repo, En, Now).Value);

    [Fact]
    public void Project_bubble_is_a_title_with_the_branch()
    {
        var bubble = Bubbles.Project(Repo, En);

        Assert.True(bubble.IsTitle);
        Assert.Equal("api", bubble.Value);
        Assert.Equal("main", bubble.Subtitle);
    }

    [Fact]
    public void Agent_bubble_uses_the_configured_colour_and_label()
    {
        var bubble = Bubbles.Agent(new AgentConfig { Label = "Gemini CLI", Color = "#1A73E8" });

        Assert.Equal("Gemini CLI", bubble.Label);
        Assert.Equal(new Rgb(0x1A, 0x73, 0xE8), bubble.Background);
        Assert.Equal(Palette.Slate, Bubbles.Agent(new AgentConfig { Color = "blue" }).Background);
    }

    [Fact]
    public void Reviews_bubble_states()
    {
        Assert.Equal(Bubbles.None, Bubbles.Reviews(2, available: false, En).Value);
        Assert.Equal(Bubbles.Pending, Bubbles.Reviews(null, available: true, En).Value);
        Assert.Equal(Palette.Orange, Bubbles.Reviews(2, available: true, En).Background);
        Assert.Equal("Revues : 0", Bubbles.Reviews(0, available: true, Fr).Label);
    }

    [Fact]
    public void Focus_bubble_draws_an_arc_only_while_running()
    {
        var timer = new FocusTimer(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5));
        var idle = Bubbles.Focus(timer, En, Now);
        Assert.Null(idle.Progress);
        Assert.Equal("25", idle.Value);
        Assert.Equal("Focus: 25 min", idle.Label);

        timer.Click(Now);
        var running = Bubbles.Focus(timer, En, Now.AddMinutes(5));
        Assert.Equal(Palette.Tomato, running.Background);
        Assert.Equal(0.2, running.Progress!.Value, 3);
        Assert.Equal("20", running.Value);
    }

    [Theory]
    [InlineData("fr", "en-US", "fr")]
    [InlineData("auto", "fr", "fr")]
    [InlineData("auto", "en", "en")]
    [InlineData(null, "fr-CA", "fr")]
    [InlineData("de", "fr", "en")]
    public void Strings_follow_config_then_system(string? configured, string system, string expected) =>
        Assert.Equal(expected, Strings.For(configured, system).Code);

    [Fact]
    public void Every_text_exists_in_both_languages()
    {
        foreach (var property in typeof(Strings).GetProperties())
        {
            if (property.PropertyType == typeof(string))
            {
                Assert.False(string.IsNullOrWhiteSpace((string?)property.GetValue(En)), $"English {property.Name}");
                Assert.False(string.IsNullOrWhiteSpace((string?)property.GetValue(Fr)), $"French {property.Name}");
            }
        }
    }
}

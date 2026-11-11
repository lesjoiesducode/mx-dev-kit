namespace MxDevKit.Preview;

// Made-up projects for the README: nothing here comes from a real repository.
internal static class Samples
{
    private static readonly DateTime Now = new(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Strings En = Strings.English;

    private static readonly ProjectView Api = new()
    {
        Root = "/work/acme-api",
        Name = "acme-api",
        Branch = "main",
        Repo = "acme/acme-api",
        Issues = 7,
        PullRequests = 2,
        Ci = new CiRun(CiState.Failed, "c1", null),
        AuditDir = "/work/acme-api",
        Vulnerabilities = 5,
        Changes = 3,
        Ahead = 2,
        Behind = 0,
    };

    private static readonly ProjectView Web = new()
    {
        Root = "/work/web-app",
        Name = "web-app",
        Branch = "feature/search",
        Repo = "acme/web-app",
        Issues = 3,
        PullRequests = 1,
        Ci = new CiRun(CiState.Passed, "c2", null),
        AuditDir = "/work/web-app",
        Vulnerabilities = 0,
        Changes = 0,
        Ahead = 0,
        Behind = 1,
    };

    private static readonly AgentConfig Agent = new();

    private static readonly InboxEvent Merged = new(InboxKind.Merged, "acme/acme-api", 482, "Add full-text search", "https://github.com/acme/acme-api/pull/482");

    public static IEnumerable<(string Name, Bubble Bubble)> Gallery()
    {
        yield return ("project", Bubbles.Project(Api, En));
        yield return ("issues", Bubbles.Issues(Api, En));
        yield return ("pull-requests", Bubbles.PullRequests(Api, En));
        yield return ("ci-passed", Bubbles.Ci(Web, En));
        yield return ("ci-failed", Bubbles.Ci(Api, En));
        yield return ("ci-running", Bubbles.Ci(Api with { Ci = new CiRun(CiState.Running, "c3", null) }, En));
        yield return ("audit", Bubbles.Audit(Api, En));
        yield return ("audit-clean", Bubbles.Audit(Web, En));
        yield return ("commit", Bubbles.Commit(Api, En, Now));
        yield return ("commit-running", Bubbles.Commit(With(Api, GitActions.Commit, ActionPhase.Running), En, Now));
        yield return ("commit-confirm", Bubbles.Commit(With(Api, GitActions.Commit, ActionPhase.Confirming), En, Now));
        yield return ("commit-confirm-fr", Bubbles.Commit(With(Api, GitActions.Commit, ActionPhase.Confirming), Strings.French, Now));
        yield return ("push", Bubbles.Push(Api, En, Now));
        yield return ("push-done", Bubbles.Push(With(Api, GitActions.Push, ActionPhase.Succeeded), En, Now));
        yield return ("pull", Bubbles.Pull(Web, En, Now));
        yield return ("pull-failed", Bubbles.Pull(With(Web, GitActions.Pull, ActionPhase.Failed), En, Now));
        yield return ("agent", Bubbles.Agent(Agent));
        yield return ("claude", Bubbles.CodingAgent(CodingAgents.Claude, true, En));
        yield return ("codex", Bubbles.CodingAgent(CodingAgents.Codex, true, En));
        yield return ("gemini", Bubbles.CodingAgent(CodingAgents.Gemini, true, En));
        yield return ("copilot", Bubbles.CodingAgent(CodingAgents.Copilot, true, En));
        yield return ("agent-missing", Bubbles.CodingAgent(CodingAgents.Codex, false, En));
        yield return ("reviews", Bubbles.Reviews(2, true, En));
        yield return ("inbox", Bubbles.Inbox(Merged, false, true, En));
        yield return ("inbox-mention", Bubbles.Inbox(Merged with { Kind = InboxKind.Mention, Number = 57 }, false, true, En));
        yield return ("inbox-changes", Bubbles.Inbox(Merged with { Kind = InboxKind.ChangesRequested, Actor = "alice" }, false, true, En));
        yield return ("inbox-empty", Bubbles.Inbox(null, false, true, En));
        yield return ("focus-idle", Bubbles.Focus(Focus(FocusPhase.Idle), En, Now));
        yield return ("focus-running", Bubbles.Focus(Focus(FocusPhase.Focus), En, Now));
        yield return ("focus-break", Bubbles.Focus(Focus(FocusPhase.Break), En, Now));
    }

    // Clockwise from the top, like the slots of the Actions Ring.
    public static Bubble[] ProjectRing() => new[]
    {
        Bubbles.CodingAgent(CodingAgents.Claude, true, En),
        Bubbles.Pull(Api, En, Now),
        Bubbles.Issues(Api, En),
        Bubbles.PullRequests(Api, En),
        Bubbles.Ci(Api, En),
        Bubbles.Audit(Api, En),
        Bubbles.Commit(Api, En, Now),
        Bubbles.Push(Api, En, Now),
    };

    public static Bubble[] OverviewRing() => new[]
    {
        Bubbles.Agent(Agent),
        Bubbles.Pull(Web, En, Now),
        Bubbles.Issues(Web, En),
        Bubbles.PullRequests(Web, En),
        Bubbles.Ci(Web, En),
        Bubbles.Push(With(Web, GitActions.Push, ActionPhase.Succeeded), En, Now),
        Bubbles.Focus(Focus(FocusPhase.Focus), En, Now),
        Bubbles.Reviews(2, true, En),
    };

    public static Bubble[] FrenchRing()
    {
        var fr = Strings.French;
        return new[]
        {
            Bubbles.Agent(Agent),
            Bubbles.Pull(Api, fr, Now),
            Bubbles.Issues(Api, fr),
            Bubbles.PullRequests(Api, fr),
            Bubbles.Ci(Api, fr),
            Bubbles.Audit(Api, fr),
            Bubbles.Commit(Api, fr, Now),
            Bubbles.Push(Api, fr, Now),
        };
    }

    private static ProjectView With(ProjectView view, string action, ActionPhase phase) =>
        view with { Actions = new Dictionary<string, ActionStatus> { [action] = new(phase, Now) } };

    private static FocusTimer Focus(FocusPhase phase)
    {
        var timer = new FocusTimer(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5));
        switch (phase)
        {
            case FocusPhase.Focus:
                timer.Click(Now.AddMinutes(-7));
                break;
            case FocusPhase.Break:
                timer.Click(Now.AddMinutes(-27));
                timer.Tick(Now.AddMinutes(-2));
                break;
        }

        return timer;
    }
}

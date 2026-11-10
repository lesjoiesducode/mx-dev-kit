namespace Loupedeck.MxDevKitPlugin;

public class ProjectNameCommand : ProjectCommand
{
    public ProjectNameCommand()
        : base(S.ProjectName, S.ProjectDescription)
    {
    }

    protected override Bubble Build() => Bubbles.Project(View, S);

    protected override void RunCommand(string actionParameter) => ProcessRunner.Open(GitHubUrl("") ?? View.Root);
}

public class IssuesCommand : ProjectCommand
{
    public IssuesCommand()
        : base(S.IssuesName, S.IssuesDescription)
    {
    }

    protected override Bubble Build() => Bubbles.Issues(View, S);

    protected override void RunCommand(string actionParameter) => ProcessRunner.Open(GitHubUrl("/issues"));
}

public class PullRequestsCommand : ProjectCommand
{
    public PullRequestsCommand()
        : base(S.PullRequestsName, S.PullRequestsDescription)
    {
    }

    protected override Bubble Build() => Bubbles.PullRequests(View, S);

    protected override void RunCommand(string actionParameter) => ProcessRunner.Open(GitHubUrl("/pulls"));
}

public class CiCommand : ProjectCommand
{
    public CiCommand()
        : base(S.CiName, S.CiDescription)
    {
    }

    protected override Bubble Build() => Bubbles.Ci(View, S);

    protected override void RunCommand(string actionParameter) => ProcessRunner.Open(View.Ci?.Url ?? GitHubUrl("/actions"));
}

public class NpmAuditCommand : ProjectCommand
{
    public NpmAuditCommand()
        : base(S.AuditName, S.AuditDescription)
    {
    }

    protected override Bubble Build() => Bubbles.Audit(View, S);

    // Never fixes anything: runs the audit again.
    protected override void RunCommand(string actionParameter) => ProcessRunner.Detach(DevKit.Project.RefreshAudit);
}

public class CommitCommand : ProjectCommand
{
    public CommitCommand()
        : base(S.CommitName, S.CommitDescription)
    {
    }

    protected override Bubble Build() => Bubbles.Commit(View, S, DateTime.UtcNow);

    protected override void RunCommand(string actionParameter) => DevKit.Project.Run(GitActions.Commit);
}

public class PushCommand : ProjectCommand
{
    public PushCommand()
        : base(S.PushName, S.PushDescription)
    {
    }

    protected override Bubble Build() => Bubbles.Push(View, S, DateTime.UtcNow);

    protected override void RunCommand(string actionParameter) => DevKit.Project.Run(GitActions.Push);
}

public class PullCommand : ProjectCommand
{
    public PullCommand()
        : base(S.PullName, S.PullDescription)
    {
    }

    protected override Bubble Build() => Bubbles.Pull(View, S, DateTime.UtcNow);

    protected override void RunCommand(string actionParameter) => DevKit.Project.Run(GitActions.Pull);
}

public class AgentCommand : ProjectCommand
{
    public AgentCommand()
        : base(S.AgentName, S.AgentDescription)
    {
        DevKit.ConfigChanged += this.Redraw;
    }

    protected override Bubble Build() => Bubbles.Agent(DevKit.Config.Agent);

    protected override void RunCommand(string actionParameter) => DevKit.Project.LaunchAgent();
}

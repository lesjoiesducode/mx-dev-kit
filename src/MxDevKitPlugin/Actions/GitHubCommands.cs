namespace Loupedeck.MxDevKitPlugin;

public class ReviewsCommand : DevKitCommand
{
    public ReviewsCommand()
        : base(S.ReviewsName, S.ReviewsDescription, S.GroupGitHub)
    {
        DevKit.GitHub.Changed += this.Redraw;
    }

    protected override Bubble Build() => Bubbles.Reviews(DevKit.GitHub.ReviewCount, DevKit.GitHub.Available, S);

    protected override void RunCommand(string actionParameter) => ProcessRunner.Open("https://github.com/pulls/review-requested");
}

public class InboxCommand : DevKitCommand
{
    public InboxCommand()
        : base(S.InboxName, S.InboxDescription, S.GroupGitHub)
    {
        DevKit.GitHub.Changed += this.Redraw;
    }

    protected override Bubble Build()
    {
        var (latest, seen) = DevKit.GitHub.Latest;
        return Bubbles.Inbox(latest, seen, DevKit.GitHub.Available, S);
    }

    // Opens the last event; nothing is marked as read on GitHub.
    protected override void RunCommand(string actionParameter) => DevKit.GitHub.Open();
}

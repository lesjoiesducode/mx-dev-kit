namespace Loupedeck.MxDevKitPlugin;

public class ExcuseCommand : DevKitCommand
{
    public ExcuseCommand()
        : base(S.ExcuseName, S.ExcuseDescription, S.GroupExtras)
    {
    }

    protected override Bubble Build() => Bubbles.Excuse(S);

    protected override void RunCommand(string actionParameter) => DevKit.Excuses.Write();
}

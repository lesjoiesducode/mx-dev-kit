namespace Loupedeck.MxDevKitPlugin;

public class FocusCommand : DevKitCommand
{
    public FocusCommand()
        : base(S.FocusName, S.FocusDescription, S.GroupFocus)
    {
        DevKit.Focus.Changed += this.Redraw;
    }

    protected override Bubble Build() => DevKit.Focus.Bubble(S);

    protected override void RunCommand(string actionParameter) => DevKit.Focus.Click();
}

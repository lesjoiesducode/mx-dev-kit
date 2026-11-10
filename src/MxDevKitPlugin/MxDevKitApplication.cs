namespace Loupedeck.MxDevKitPlugin;

// Not tied to an application: the bubbles work in any ring where they are placed.
public class MxDevKitApplication : ClientApplication
{
    protected override string GetProcessName() => "";

    protected override string GetBundleName() => "";

    public override ClientApplicationStatus GetApplicationStatus() => ClientApplicationStatus.Unknown;
}

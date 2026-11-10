namespace Loupedeck.MxDevKitPlugin;

public class MxDevKitPlugin : Plugin
{
    public MxDevKitPlugin()
    {
        PluginLog.Init(this.Log);
    }

    public override bool UsesApplicationApiOnly => true;

    public override bool HasNoApplication => true;

    public override void Load() => DevKit.Start(this);

    public override void Unload() => DevKit.Stop();
}

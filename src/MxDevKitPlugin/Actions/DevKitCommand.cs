namespace Loupedeck.MxDevKitPlugin;

// Base of every bubble: builds a Bubble from the current state and paints it.
public abstract class DevKitCommand : PluginDynamicCommand
{
    protected DevKitCommand(string displayName, string description, string groupName)
        : base(displayName, description, groupName)
    {
    }

    protected static Strings S => DevKit.Strings;

    protected abstract Bubble Build();

    protected override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize)
    {
        using var builder = new BitmapBuilder(imageSize);
        BubblePainter.Paint(builder, this.Build());
        var image = builder.ToImage();
        this.SavePreview(image, imageSize);
        return image;
    }

    protected override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize) => this.Build().Label;

    protected void Redraw()
    {
        try
        {
            this.ActionImageChanged();
        }
        catch (Exception e)
        {
            PluginLog.Warning(e, "ActionImageChanged failed");
        }
    }

    // mkdir ~/.config/mx-dev-kit/preview to get every rendered bubble as a PNG.
    private void SavePreview(BitmapImage image, PluginImageSize imageSize)
    {
        try
        {
            if (Directory.Exists(DevKitPaths.PreviewDir))
            {
                image.SaveToFile(Path.Combine(DevKitPaths.PreviewDir, $"{this.GetType().Name}-{(int)imageSize}.png"));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}

public abstract class ProjectCommand : DevKitCommand
{
    protected ProjectCommand(string displayName, string description)
        : base(displayName, description, DevKit.Strings.GroupProject)
    {
        DevKit.Project.Changed += this.Redraw;
    }

    protected static ProjectView View => DevKit.Project.View;

    protected static string? GitHubUrl(string suffix) => View.Repo == null ? null : $"https://github.com/{View.Repo}{suffix}";
}

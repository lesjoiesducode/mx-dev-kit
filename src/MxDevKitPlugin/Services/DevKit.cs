namespace Loupedeck.MxDevKitPlugin;

// Shared services. Logi Plugin Service creates the actions on its own, so they reach these statically.
internal static class DevKit
{
    private static readonly object Sync = new();
    private static DateTime _configWrite = DateTime.MinValue;

    static DevKit()
    {
        Directory.CreateDirectory(DevKitPaths.StateDir);
        Config = LoadConfig();
        Strings = Strings.For(Config.Language, SystemLanguage.Detect());
        Focus = new FocusService(Config.Focus);
    }

    public static DevKitConfig Config { get; private set; }

    // Read once: Options+ keeps the action names it got at load time.
    public static Strings Strings { get; }

    public static Haptics Haptics { get; } = new();

    public static ProjectWatcher Project { get; } = new();

    public static InboxWatcher GitHub { get; } = new();

    public static FocusService Focus { get; }

    public static event Action? ConfigChanged;

    public static void Start(Plugin plugin)
    {
        Haptics.Register(plugin, Strings);
        Project.Start();
        GitHub.Start();
        Focus.Start();
        PluginLog.Info($"MX Dev Kit started (language {Strings.Code}, haptics {Config.Haptics.Mode}, state in {DevKitPaths.StateDir})");
    }

    public static void Stop()
    {
        Project.Dispose();
        GitHub.Dispose();
        Focus.Dispose();
    }

    public static void ReloadConfigIfChanged()
    {
        var written = File.Exists(DevKitPaths.ConfigFile) ? File.GetLastWriteTimeUtc(DevKitPaths.ConfigFile) : DateTime.MinValue;
        if (written == _configWrite)
        {
            return;
        }

        lock (Sync)
        {
            Config = LoadConfig();
        }

        ToolLocator.Reset();
        Focus.Apply(Config.Focus);
        Project.RefreshSoon();
        GitHub.RefreshNow();
        PluginLog.Info("config.json reloaded");
        ConfigChanged?.Invoke();
    }

    private static DevKitConfig LoadConfig()
    {
        _configWrite = File.Exists(DevKitPaths.ConfigFile) ? File.GetLastWriteTimeUtc(DevKitPaths.ConfigFile) : DateTime.MinValue;
        var config = DevKitConfig.Load(DevKitPaths.ConfigFile, out var error);
        if (error != null)
        {
            PluginLog.Warning($"config.json ignored, using defaults: {error}");
        }

        return config;
    }
}
